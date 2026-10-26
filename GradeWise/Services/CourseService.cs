using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GradeWise.Data;
using GradeWise.Models;

namespace GradeWise.Services
{
    /// <summary>
    /// Faculty-side rules for courses, enrollment, categories and scores. It reads and writes only
    /// through SampleData, so swapping in a real database later only changes this class and SampleData.
    /// Every method locks SampleData.Sync and only touches courses owned by the given faculty id.
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
            lock (SampleData.Sync)
            {
                foreach (var course in SampleData.Courses.Where(c => c.FacultyId == facultyId).OrderBy(c => c.Code))
                {
                    var rows = BuildRows(course, null);
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
            }

            model.StudentCount = model.Courses.Sum(c => c.StudentCount);
            model.MissingScores = model.Courses.Sum(c => c.MissingScores);
            return model;
        }

        // ---------- create course ----------

        public static CreateCourseViewModel NewCourseForm()
        {
            var model = new CreateCourseViewModel();
            lock (SampleData.Sync)
            {
                model.TermNames = SampleData.Terms.Select(t => t.Name).ToList();
                var current = SampleData.Terms.FirstOrDefault(t => t.IsCurrent);
                model.Term = current == null ? null : current.Name;
            }
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

            lock (SampleData.Sync)
            {
                var chosenTerm = SampleData.Terms.FirstOrDefault(t => string.Equals(t.Name, term, StringComparison.OrdinalIgnoreCase));
                if (chosenTerm == null)
                {
                    errors.Add("Choose a term from the list.");
                }

                if (errors.Count == 0 && SampleData.Courses.Any(c =>
                        c.FacultyId == facultyId &&
                        string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(c.Term, chosenTerm.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add("You already have a course with that code in this term.");
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
                SampleData.Courses.Add(course);
                return course.Id;
            }
        }

        // ---------- course detail and score entry ----------

        /// <summary>
        /// Builds the score entry page. When <paramref name="raw"/> is given, the boxes show what the user
        /// typed (used when a save is rejected) instead of the saved scores. Returns null if the course
        /// does not exist or belongs to someone else.
        /// </summary>
        public static CourseDetailViewModel GetDetail(string courseId, string facultyId, Func<string, string> raw)
        {
            lock (SampleData.Sync)
            {
                var course = FindOwned(courseId, facultyId);
                if (course == null) return null;

                var rows = BuildRows(course, raw);
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
        }

        /// <summary>Name of the form box for one score. The category name is part of it, so a stale page can never write to the wrong category.</summary>
        public static string ScoreFieldName(string studentId, string category)
        {
            return "score__" + studentId + "__" + category;
        }

        /// <summary>
        /// Checks every box first and only saves when all of them are fine, so a typo never leaves
        /// the table half saved. A box that is missing from the form is left unchanged.
        /// </summary>
        public static bool SaveScores(string courseId, string facultyId, Func<string, string> raw, out List<string> errors)
        {
            errors = new List<string>();

            lock (SampleData.Sync)
            {
                var course = FindOwned(courseId, facultyId);
                if (course == null)
                {
                    errors.Add("That course no longer exists.");
                    return false;
                }

                var pending = new List<Tuple<GradeRecord, string, double?>>();
                var problems = 0;

                foreach (var studentId in course.StudentIds)
                {
                    var student = SampleData.Users.FirstOrDefault(u => u.Id == studentId);
                    var label = student == null ? studentId : student.FullName;
                    var record = EnsureRecord(course, studentId);

                    foreach (var category in course.Categories)
                    {
                        var text = raw(ScoreFieldName(studentId, category.Name));
                        if (text == null) continue;   // not in the form: leave as it is

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
                    var entry = change.Item1.Scores.First(s => string.Equals(s.Category, change.Item2, StringComparison.OrdinalIgnoreCase));
                    entry.Score = change.Item3;
                }

                return true;
            }
        }

        /// <summary>Empty text means "not graded yet" (null). Otherwise a number from 0 to 100, rounded to 2 decimals.</summary>
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
            lock (SampleData.Sync)
            {
                var course = FindOwned(courseId, facultyId);
                if (course == null) return null;

                var enrolled = course.StudentIds
                    .Select(id => SampleData.Users.FirstOrDefault(u => u.Id == id))
                    .Where(u => u != null)
                    .OrderBy(u => u.FullName)
                    .ToList();

                var available = SampleData.Users
                    .Where(u => u.Role == Roles.Student && u.IsActive && !course.StudentIds.Contains(u.Id))
                    .OrderBy(u => u.FullName)
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
        }

        /// <summary>Enrolls the chosen active students and gives each an empty grade record. Returns how many were added.</summary>
        public static int Enroll(string courseId, string facultyId, IEnumerable<string> studentIds, out string error)
        {
            error = null;
            var added = 0;

            lock (SampleData.Sync)
            {
                var course = FindOwned(courseId, facultyId);
                if (course == null)
                {
                    error = "That course no longer exists.";
                    return 0;
                }

                foreach (var id in (studentIds ?? Enumerable.Empty<string>()).Distinct())
                {
                    var student = SampleData.Users.FirstOrDefault(u => u.Id == id);
                    if (student == null || student.Role != Roles.Student || !student.IsActive) continue;
                    if (course.StudentIds.Contains(id)) continue;

                    course.StudentIds.Add(id);
                    EnsureRecord(course, id);
                    added++;
                }
            }

            if (added == 0) error = "Choose at least one student to enroll.";
            return added;
        }

        /// <summary>Removes a student from the course together with their scores for it.</summary>
        public static string Unenroll(string courseId, string facultyId, string studentId)
        {
            lock (SampleData.Sync)
            {
                var course = FindOwned(courseId, facultyId);
                if (course == null) return null;

                if (!course.StudentIds.Remove(studentId)) return null;

                SampleData.Grades.RemoveAll(g => g.CourseId == course.Id && g.StudentId == studentId);

                var student = SampleData.Users.FirstOrDefault(u => u.Id == studentId);
                return student == null ? studentId : student.FullName;
            }
        }

        // ---------- categories and weights ----------

        public static CategoriesViewModel GetCategories(string courseId, string facultyId)
        {
            lock (SampleData.Sync)
            {
                var course = FindOwned(courseId, facultyId);
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
        }

        /// <summary>The categories form filled with what the user typed, for showing errors.</summary>
        public static CategoriesViewModel EchoCategories(string courseId, string facultyId, IList<string> originals, IList<string> names, IList<string> weights, List<string> errors)
        {
            lock (SampleData.Sync)
            {
                var course = FindOwned(courseId, facultyId);
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
        }

        /// <summary>
        /// Saves the course's categories in the order given. A row with an Original name that still exists
        /// keeps its students' scores (even when renamed); a new row starts empty; an Original that is
        /// no longer listed is removed along with its scores. Rows with no name and no weight are ignored.
        /// The weights are allowed to total something other than 100: the page shows a warning instead.
        /// </summary>
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

                if (name.Length == 0 && weightText.Length == 0) continue;   // empty row = removed / unused

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

            lock (SampleData.Sync)
            {
                var course = FindOwned(courseId, facultyId);
                if (course == null)
                {
                    errors.Add("That course no longer exists.");
                    return false;
                }

                // Which old category each new row continues. An old name can only be continued once.
                var oldNames = course.Categories.Select(c => c.Name).ToList();
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var sources = new List<string>();
                foreach (var row in parsed)
                {
                    var source = oldNames.FirstOrDefault(n => string.Equals(n, row.Original, StringComparison.OrdinalIgnoreCase));
                    if (source != null && used.Add(source)) sources.Add(source);
                    else sources.Add(null);
                }

                foreach (var record in SampleData.Grades.Where(g => g.CourseId == course.Id))
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
                    record.Scores = rebuilt;
                }

                course.Categories = newCategories;
                return true;
            }
        }

        // ---------- helpers (call only while holding SampleData.Sync) ----------

        private static Course FindOwned(string courseId, string facultyId)
        {
            if (string.IsNullOrEmpty(courseId) || string.IsNullOrEmpty(facultyId)) return null;
            return SampleData.Courses.FirstOrDefault(c => c.Id == courseId && c.FacultyId == facultyId);
        }

        /// <summary>Finds the student's grade record for the course, creating it (and any missing category entries) if needed.</summary>
        private static GradeRecord EnsureRecord(Course course, string studentId)
        {
            var record = SampleData.Grades.FirstOrDefault(g => g.CourseId == course.Id && g.StudentId == studentId);
            if (record == null)
            {
                record = new GradeRecord
                {
                    Id = "g-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    CourseId = course.Id,
                    StudentId = studentId
                };
                SampleData.Grades.Add(record);
            }

            foreach (var category in course.Categories)
            {
                if (!record.Scores.Any(s => string.Equals(s.Category, category.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    record.Scores.Add(new ScoreEntry { Category = category.Name, Score = null });
                }
            }

            return record;
        }

        /// <summary>One table row per enrolled student (sorted by name), without changing any data.</summary>
        private static List<ScoreRow> BuildRows(Course course, Func<string, string> raw)
        {
            var rows = new List<ScoreRow>();

            var students = course.StudentIds
                .Select(id => SampleData.Users.FirstOrDefault(u => u.Id == id))
                .Where(u => u != null)
                .OrderBy(u => u.FullName)
                .ToList();

            foreach (var student in students)
            {
                var record = SampleData.Grades.FirstOrDefault(g => g.CourseId == course.Id && g.StudentId == student.Id);
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
