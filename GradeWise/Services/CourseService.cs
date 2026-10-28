using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GradeWise.Data;
using GradeWise.Models;
using MongoDB.Driver;

namespace GradeWise.Services
{
    /// <summary>
    /// Faculty-side rules for courses, enrollment, categories and scores backed by MongoDB Atlas.
    /// </summary>
    public static class CourseService
    {
        public const int MaxCodeLength = 20;
        public const int MaxNameLength = 100;
        public const int MaxCategoryNameLength = 40;

        private const int MaxErrorsShown = 15;

        // ---------- course list ----------

        public static CourseListViewModel GetCourseList(string facultyId)
        {
            var model = new CourseListViewModel();
            var db = new MongoDbContext();

            var courses = db.Courses.Find(c => c.FacultyId == facultyId).SortBy(c => c.Code).ToList();
            foreach (var course in courses)
            {
                var rows = BuildRows(db, course, null);
                var summary = GradeCalculator.Summarize(rows.Select(r => r.Result.Average));
                var missing = rows.Sum(r => r.Inputs.Count(string.IsNullOrEmpty));

                model.Courses.Add(new CourseListItem
                {
                    Id = course.Id,
                    Code = course.Code,
                    Name = course.Name,
                    Term = course.Term,
                    StudentCount = course.StudentIds.Count,
                    CategoryCount = course.Categories.Count,
                    WeightTotal = GradeCalculator.TotalWeight(course.Categories),
                    ClassAverage = summary.Average,
                    MissingScores = missing
                });
            }

            model.StudentCount = model.Courses.Sum(c => c.StudentCount);
            model.MissingScores = model.Courses.Sum(c => c.MissingScores);
            return model;
        }

        // ---------- create course ----------

        public static CreateCourseViewModel NewCourseForm()
        {
            var model = new CreateCourseViewModel();
            var db = new MongoDbContext();

            var terms = db.Terms.Find(_ => true).ToList();
            model.TermNames = terms.Select(t => t.Name).ToList();
            var current = terms.FirstOrDefault(t => t.IsCurrent);
            model.Term = current == null ? null : current.Name;

            return model;
        }

        /// <summary>Creates a course owned by the faculty member. Returns the new id, or null when there are errors.</summary>
        public static string CreateCourse(string facultyId, string code, string name, string term, out List<string> errors)
        {
            errors = new List<string>();
            code = (code ?? string.Empty).Trim();
            name = (name ?? string.Empty).Trim();
            term = (term ?? string.Empty).Trim();

            if (code.Length == 0) errors.Add("Course code is required.");
            else if (code.Length > MaxCodeLength) errors.Add("Course code must be " + MaxCodeLength + " characters or fewer.");

            if (name.Length == 0) errors.Add("Course name is required.");
            else if (name.Length > MaxNameLength) errors.Add("Course name must be " + MaxNameLength + " characters or fewer.");

            var db = new MongoDbContext();
            var chosenTerm = db.Terms.Find(t => t.Name.ToLower() == term.ToLower()).FirstOrDefault();
            if (chosenTerm == null)
            {
                errors.Add("Choose a term from the list.");
            }

            if (errors.Count == 0)
            {
                var existingCourse = db.Courses.Find(c =>
                    c.FacultyId == facultyId &&
                    c.Code.ToLower() == code.ToLower() &&
                    c.Term.ToLower() == chosenTerm.Name.ToLower()).FirstOrDefault();

                if (existingCourse != null)
                {
                    errors.Add("You already have a course with that code in this term.");
                }
            }

            if (errors.Count > 0) return null;

            var course = new Course
            {
                Id = "c-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Code = code,
                Name = name,
                Term = chosenTerm.Name,
                FacultyId = facultyId
            };

            db.Courses.InsertOne(course);
            return course.Id;
        }

        // ---------- course detail and score entry ----------

        public static CourseDetailViewModel GetDetail(string courseId, string facultyId, Func<string, string> raw)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return null;

            var rows = BuildRows(db, course, raw);
            return new CourseDetailViewModel
            {
                CourseId = course.Id,
                Code = course.Code,
                Name = course.Name,
                Term = course.Term,
                Categories = course.Categories.Select(c => new GradeCategory { Name = c.Name, Weight = c.Weight }).ToList(),
                WeightTotal = GradeCalculator.TotalWeight(course.Categories),
                Rows = rows,
                Summary = GradeCalculator.Summarize(rows.Select(r => r.Result.Average))
            };
        }

        public static string ScoreFieldName(string studentId, string category)
        {
            return "score__" + studentId + "__" + category;
        }

        public static bool SaveScores(string courseId, string facultyId, Func<string, string> raw, out List<string> errors)
        {
            errors = new List<string>();
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);

            if (course == null)
            {
                errors.Add("That course no longer exists.");
                return false;
            }

            var pending = new List<Tuple<GradeRecord, string, double?>>();
            var problems = 0;

            foreach (var studentId in course.StudentIds)
            {
                var student = db.Users.Find(u => u.Id == studentId).FirstOrDefault();
                var label = student == null ? studentId : student.FullName;
                var record = EnsureRecord(db, course, studentId);

                foreach (var category in course.Categories)
                {
                    var text = raw(ScoreFieldName(studentId, category.Name));
                    if (text == null) continue;

                    double? value;
                    if (!TryParseScore(text, out value))
                    {
                        problems++;
                        if (errors.Count < MaxErrorsShown)
                        {
                            errors.Add(label + " / " + category.Name + ": enter a number from 0 to 100, or leave it empty.");
                        }
                        continue;
                    }

                    pending.Add(Tuple.Create(record, category.Name, value));
                }
            }

            if (problems > MaxErrorsShown)
            {
                errors.Add("...and " + (problems - MaxErrorsShown) + " more.");
            }
            if (problems > 0) return false;

            foreach (var change in pending)
            {
                var record = change.Item1;
                var categoryName = change.Item2;
                var newScore = change.Item3;

                var entry = record.Scores.FirstOrDefault(s => string.Equals(s.Category, categoryName, StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                {
                    entry.Score = newScore;
                }

                var update = Builders<GradeRecord>.Update.Set(g => g.Scores, record.Scores);
                db.Grades.UpdateOne(g => g.Id == record.Id, update);
            }

            var now = DateTime.Now;
            foreach (var studentId in course.StudentIds)
            {
                HistoryService.Record(course, studentId, now);
            }

            return true;
        }

        public static bool TryParseScore(string text, out double? value)
        {
            value = null;
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0) return true;

            double parsed;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return false;
            if (double.IsNaN(parsed) || double.IsInfinity(parsed)) return false;
            if (parsed < 0 || parsed > 100) return false;

            value = Math.Round(parsed, 2);
            return true;
        }

        // ---------- enrollment ----------

        public static EnrollViewModel GetEnrollment(string courseId, string facultyId)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return null;

            var enrolled = db.Users.Find(u => course.StudentIds.Contains(u.Id))
                .SortBy(u => u.FullName)
                .ToList();

            var available = db.Users.Find(u => u.Role == Roles.Student && u.IsActive && !course.StudentIds.Contains(u.Id))
                .SortBy(u => u.FullName)
                .ToList();

            return new EnrollViewModel
            {
                CourseId = course.Id,
                Code = course.Code,
                Name = course.Name,
                Term = course.Term,
                Enrolled = enrolled,
                Available = available
            };
        }

        public static int Enroll(string courseId, string facultyId, IEnumerable<string> studentIds, out string error)
        {
            error = null;
            var added = 0;
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);

            if (course == null)
            {
                error = "That course no longer exists.";
                return 0;
            }

            foreach (var id in (studentIds ?? Enumerable.Empty<string>()).Distinct())
            {
                var student = db.Users.Find(u => u.Id == id).FirstOrDefault();
                if (student == null || student.Role != Roles.Student || !student.IsActive) continue;
                if (course.StudentIds.Contains(id)) continue;

                course.StudentIds.Add(id);
                EnsureRecord(db, course, id);
                added++;
            }

            var update = Builders<Course>.Update.Set(c => c.StudentIds, course.StudentIds);
            db.Courses.UpdateOne(c => c.Id == course.Id, update);

            if (added == 0) error = "Choose at least one student to enroll.";
            return added;
        }

        public static string Unenroll(string courseId, string facultyId, string studentId)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return null;

            if (!course.StudentIds.Remove(studentId)) return null;

            var updateCourse = Builders<Course>.Update.Set(c => c.StudentIds, course.StudentIds);
            db.Courses.UpdateOne(c => c.Id == course.Id, updateCourse);

            db.Grades.DeleteMany(g => g.CourseId == course.Id && g.StudentId == studentId);
            db.Snapshots.DeleteMany(h => h.CourseId == course.Id && h.StudentId == studentId);

            var student = db.Users.Find(u => u.Id == studentId).FirstOrDefault();
            return student == null ? studentId : student.FullName;
        }

        // ---------- categories and weights ----------

        public static CategoriesViewModel GetCategories(string courseId, string facultyId)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return null;

            var model = NewCategoriesModel(course);
            foreach (var c in course.Categories)
            {
                model.Rows.Add(new CategoryRow
                {
                    Original = c.Name,
                    Name = c.Name,
                    Weight = c.Weight.ToString("0.##", CultureInfo.InvariantCulture)
                });
            }
            return model;
        }

        public static CategoriesViewModel EchoCategories(string courseId, string facultyId, IList<string> originals, IList<string> names, IList<string> weights, List<string> errors)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return null;

            var model = NewCategoriesModel(course);
            for (var i = 0; i < names.Count; i++)
            {
                model.Rows.Add(new CategoryRow
                {
                    Original = At(originals, i),
                    Name = At(names, i),
                    Weight = At(weights, i)
                });
            }
            model.Errors = errors;
            return model;
        }

        public static bool SaveCategories(string courseId, string facultyId, IList<string> originals, IList<string> names, IList<string> weights, out List<string> errors)
        {
            errors = new List<string>();

            var parsed = new List<CategoryRow>();
            var newCategories = new List<GradeCategory>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < names.Count; i++)
            {
                var name = (At(names, i) ?? string.Empty).Trim();
                var weightText = (At(weights, i) ?? string.Empty).Trim();
                var original = (At(originals, i) ?? string.Empty).Trim();

                if (name.Length == 0 && weightText.Length == 0) continue;

                var row = name.Length > 0 ? "\"" + name + "\"" : "Row " + (i + 1);
                var rowOk = true;

                if (name.Length == 0)
                {
                    errors.Add(row + ": enter a category name.");
                    rowOk = false;
                }
                else if (name.Length > MaxCategoryNameLength)
                {
                    errors.Add(row + ": the name must be " + MaxCategoryNameLength + " characters or fewer.");
                    rowOk = false;
                }
                else if (!seenNames.Add(name))
                {
                    errors.Add(row + " is used twice. Each category needs its own name.");
                    rowOk = false;
                }

                double weight;
                if (!double.TryParse(weightText, NumberStyles.Float, CultureInfo.InvariantCulture, out weight) ||
                    double.IsNaN(weight) || double.IsInfinity(weight) || weight <= 0 || weight > 100)
                {
                    errors.Add(row + ": weight must be a number above 0 and up to 100.");
                    rowOk = false;
                }

                if (rowOk)
                {
                    parsed.Add(new CategoryRow { Original = original, Name = name });
                    newCategories.Add(new GradeCategory { Name = name, Weight = Math.Round(weight, 2) });
                }
            }

            if (errors.Count == 0 && newCategories.Count == 0)
            {
                errors.Add("Add at least one grading category.");
            }

            if (errors.Count > 0) return false;

            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null)
            {
                errors.Add("That course no longer exists.");
                return false;
            }

            var oldNames = course.Categories.Select(c => c.Name).ToList();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sources = new List<string>();
            foreach (var row in parsed)
            {
                var source = oldNames.FirstOrDefault(n => string.Equals(n, row.Original, StringComparison.OrdinalIgnoreCase));
                if (source != null && used.Add(source)) sources.Add(source);
                else sources.Add(null);
            }

            var gradeRecords = db.Grades.Find(g => g.CourseId == course.Id).ToList();
            foreach (var record in gradeRecords)
            {
                var rebuilt = new List<ScoreEntry>();
                for (var i = 0; i < newCategories.Count; i++)
                {
                    double? score = null;
                    if (sources[i] != null)
                    {
                        var old = record.Scores.FirstOrDefault(s => string.Equals(s.Category, sources[i], StringComparison.OrdinalIgnoreCase));
                        if (old != null) score = old.Score;
                    }
                    rebuilt.Add(new ScoreEntry { Category = newCategories[i].Name, Score = score });
                }

                var gradeUpdate = Builders<GradeRecord>.Update.Set(g => g.Scores, rebuilt);
                db.Grades.UpdateOne(g => g.Id == record.Id, gradeUpdate);
            }

            var courseUpdate = Builders<Course>.Update.Set(c => c.Categories, newCategories);
            db.Courses.UpdateOne(c => c.Id == course.Id, courseUpdate);

            var now = DateTime.Now;
            foreach (var studentId in course.StudentIds)
            {
                HistoryService.Record(course, studentId, now);
            }

            return true;
        }

        // ---------- helpers ----------

        private static Course FindOwned(MongoDbContext db, string courseId, string facultyId)
        {
            if (string.IsNullOrEmpty(courseId) || string.IsNullOrEmpty(facultyId)) return null;
            return db.Courses.Find(c => c.Id == courseId && c.FacultyId == facultyId).FirstOrDefault();
        }

        private static GradeRecord EnsureRecord(MongoDbContext db, Course course, string studentId)
        {
            var record = db.Grades.Find(g => g.CourseId == course.Id && g.StudentId == studentId).FirstOrDefault();
            if (record == null)
            {
                record = new GradeRecord
                {
                    Id = "g-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    CourseId = course.Id,
                    StudentId = studentId
                };
                db.Grades.InsertOne(record);
            }

            var updated = false;
            foreach (var category in course.Categories)
            {
                if (!record.Scores.Any(s => string.Equals(s.Category, category.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    record.Scores.Add(new ScoreEntry { Category = category.Name, Score = null });
                    updated = true;
                }
            }

            if (updated)
            {
                var update = Builders<GradeRecord>.Update.Set(g => g.Scores, record.Scores);
                db.Grades.UpdateOne(g => g.Id == record.Id, update);
            }

            return record;
        }

        private static List<ScoreRow> BuildRows(MongoDbContext db, Course course, Func<string, string> raw)
        {
            var rows = new List<ScoreRow>();

            var students = db.Users.Find(u => course.StudentIds.Contains(u.Id))
                .SortBy(u => u.FullName)
                .ToList();

            foreach (var student in students)
            {
                var record = db.Grades.Find(g => g.CourseId == course.Id && g.StudentId == student.Id).FirstOrDefault();
                var scores = record == null ? new List<ScoreEntry>() : record.Scores;

                var row = new ScoreRow
                {
                    StudentId = student.Id,
                    StudentNo = student.StudentNo,
                    FullName = student.FullName,
                    Result = GradeCalculator.Calculate(course.Categories, scores)
                };

                foreach (var category in course.Categories)
                {
                    string typed = raw == null ? null : raw(ScoreFieldName(student.Id, category.Name));
                    if (typed != null)
                    {
                        double? ignored;
                        row.Inputs.Add(typed);
                        row.Invalid.Add(!TryParseScore(typed, out ignored));
                    }
                    else
                    {
                        var entry = scores.FirstOrDefault(s => string.Equals(s.Category, category.Name, StringComparison.OrdinalIgnoreCase));
                        row.Inputs.Add(entry != null && entry.Score.HasValue
                            ? entry.Score.Value.ToString("0.##", CultureInfo.InvariantCulture)
                            : string.Empty);
                        row.Invalid.Add(false);
                    }
                }

                rows.Add(row);
            }

            return rows;
        }

        private static CategoriesViewModel NewCategoriesModel(Course course)
        {
            return new CategoriesViewModel
            {
                CourseId = course.Id,
                Code = course.Code,
                Name = course.Name,
                Term = course.Term
            };
        }

        private static string At(IList<string> list, int index)
        {
            return list != null && index < list.Count ? list[index] : null;
        }
    }
}