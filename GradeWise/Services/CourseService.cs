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
                var scope = GradeScopes.Normalize(course.ReflectedScope);
                var rows = BuildRows(db, course, null);
                var summary = GradeCalculator.Summarize(rows.Select(r => r.ScopeResult.EarnedOrNull));
                var missing = rows.Sum(r => r.Inputs.Count(string.IsNullOrEmpty));

                model.Courses.Add(new CourseListItem
                {
                    Id = course.Id,
                    Code = course.Code,
                    Name = course.Name,
                    Term = course.Term,
                    Section = course.Section,
                    StudentCount = course.StudentIds.Count,
                    CategoryCount = course.Categories.Count,
                    WeightTotal = GradeCalculator.TotalWeight(course.Categories),
                    ClassAverage = summary.Average,
                    ScopeLabel = GradeScopes.Label(scope),
                    MissingScores = missing
                });
            }

            model.StudentCount = model.Courses.Sum(c => c.StudentCount);
            model.MissingScores = model.Courses.Sum(c => c.MissingScores);
            return model;
        }

        // ---------- create course ----------

        /// <summary>
        /// Sections a faculty member can pick when creating a course.
        /// TODO: load these from the sections module once it exists. Empty for now.
        /// </summary>
        public static List<string> GetAvailableSections()
        {
            return new List<string>();
        }

        public static CreateCourseViewModel NewCourseForm()
        {
            var model = new CreateCourseViewModel();
            var db = new MongoDbContext();

            var terms = db.Terms.Find(_ => true).ToList();
            model.TermNames = terms.Select(t => t.Name).ToList();
            model.SectionNames = GetAvailableSections();
            var current = terms.FirstOrDefault(t => t.IsCurrent);
            model.Term = current == null ? null : current.Name;

            return model;
        }

        /// <summary>Creates a course owned by the faculty member. Returns the new id, or null when there are errors.</summary>
        public static string CreateCourse(string facultyId, string code, string name, string section, string term, out List<string> errors)
        {
            code = (code ?? string.Empty).Trim();
            name = (name ?? string.Empty).Trim();
            section = (section ?? string.Empty).Trim();
            term = (term ?? string.Empty).Trim();

            var db = new MongoDbContext();
            AcademicTerm chosenTerm;
            errors = ValidateCourseFields(db, facultyId, null, code, name, section, true, term, out chosenTerm);

            if (errors.Count > 0) return null;

            var course = new Course
            {
                Id = "c-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Code = code,
                Name = name,
                Section = section,
                Term = chosenTerm.Name,
                FacultyId = facultyId
            };

            db.Courses.InsertOne(course);
            return course.Id;
        }

        /// <summary>
        /// The rules shared by Create and Edit. excludeCourseId is the course being edited (null when creating),
        /// so saving a course without changing its code is not a duplicate of itself.
        /// </summary>
        private static List<string> ValidateCourseFields(MongoDbContext db, string facultyId, string excludeCourseId,
            string code, string name, string section, bool checkSection, string term, out AcademicTerm chosenTerm)
        {
            var errors = new List<string>();

            if (code.Length == 0) errors.Add("Course code is required.");
            else if (code.Length > MaxCodeLength) errors.Add("Course code must be " + MaxCodeLength + " characters or fewer.");

            if (name.Length == 0) errors.Add("Course name is required.");
            else if (name.Length > MaxNameLength) errors.Add("Course name must be " + MaxNameLength + " characters or fewer.");

            if (checkSection && section.Length > 0 &&
                !GetAvailableSections().Any(x => string.Equals(x, section, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add("Choose a section from the list.");
            }

            var termKey = term.ToLower();
            chosenTerm = db.Terms.Find(t => t.Name.ToLower() == termKey).FirstOrDefault();
            if (chosenTerm == null)
            {
                errors.Add("Choose a term from the list.");
            }

            if (errors.Count == 0)
            {
                var codeKey = code.ToLower();
                var termName = chosenTerm.Name.ToLower();
                var exclude = excludeCourseId ?? string.Empty;   // course ids are never empty, so "" excludes nothing

                var existingCourse = db.Courses.Find(c =>
                    c.FacultyId == facultyId &&
                    c.Id != exclude &&
                    c.Code.ToLower() == codeKey &&
                    c.Term.ToLower() == termName).FirstOrDefault();

                if (existingCourse != null)
                {
                    errors.Add("You already have a course with that code in this term.");
                }
            }

            return errors;
        }

        /// <summary>Changes the code, name, section and term of a course the faculty member owns.</summary>
        public static bool UpdateCourse(string courseId, string facultyId, string code, string name, string section, string term, out List<string> errors)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null)
            {
                errors = new List<string> { "That course was not found." };
                return false;
            }

            code = (code ?? string.Empty).Trim();
            name = (name ?? string.Empty).Trim();
            term = (term ?? string.Empty).Trim();

            // No section field posted (the window showed it as read-only text): keep the stored value.
            // A section is checked against the list only when it was changed.
            var stored = course.Section ?? string.Empty;
            section = section == null ? stored : section.Trim();
            var sectionChanged = !string.Equals(section, stored, StringComparison.OrdinalIgnoreCase);
            if (!sectionChanged) section = stored;

            AcademicTerm chosenTerm;
            errors = ValidateCourseFields(db, facultyId, course.Id, code, name, section, sectionChanged, term, out chosenTerm);
            if (errors.Count > 0) return false;

            // Categories, enrolled students, scores and history are keyed by course id, so they are untouched.
            var courseKey = course.Id;
            var update = Builders<Course>.Update
                .Set(c => c.Code, code)
                .Set(c => c.Name, name)
                .Set(c => c.Section, section)
                .Set(c => c.Term, chosenTerm.Name);
            db.Courses.UpdateOne(c => c.Id == courseKey && c.FacultyId == facultyId, update);
            return true;
        }

        /// <summary>Sets which numbers the course reflects. Returns false when the course is not the faculty member's.</summary>
        public static bool SetReflectedScope(string courseId, string facultyId, string scope)
        {
            if (string.IsNullOrEmpty(courseId) || string.IsNullOrEmpty(facultyId)) return false;

            var db = new MongoDbContext();
            var saved = GradeScopes.Normalize(scope);
            var result = db.Courses.UpdateOne(
                c => c.Id == courseId && c.FacultyId == facultyId,
                Builders<Course>.Update.Set(c => c.ReflectedScope, saved));
            return result.MatchedCount > 0;
        }

        // ---------- course detail and score entry ----------

        /// <summary>Tab names the Course page accepts. Anything else (or nothing) opens the Midterm tab.</summary>
        public static string NormalizeTab(string tab)
        {
            return tab == "final-term" || tab == "final-grade" ? tab : "midterm";
        }

        public static CourseDetailViewModel GetDetail(string courseId, string facultyId, Func<string, string> raw, string tab = null)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return null;

            var rows = BuildRows(db, course, raw);
            var scope = GradeScopes.Normalize(course.ReflectedScope);
            var summary = GradeCalculator.Summarize(rows.Select(r => r.ScopeResult.EarnedOrNull));

            var terms = db.Terms.Find(_ => true).ToList();
            var edit = new CreateCourseViewModel
            {
                Code = course.Code,
                Name = course.Name,
                Section = course.Section,
                Term = course.Term,
                SectionNames = GetAvailableSections(),
                TermNames = terms.Select(t => t.Name).ToList()
            };

            return new CourseDetailViewModel
            {
                Scope = scope,
                ScopeLabel = GradeScopes.Label(scope),
                ScopeReady = GradeCalculator.ScopeReady(course.Categories, scope),
                Equivalent = GradeCalculator.EquivalentGrade(summary.Average),
                EditCourse = edit,
                CourseId = course.Id,
                Code = course.Code,
                Name = course.Name,
                Term = course.Term,
                Section = course.Section,
                Categories = course.Categories.Select(c => new GradeCategory { Name = c.Name, Weight = c.Weight, Period = GradePeriods.Normalize(c.Period) }).ToList(),
                WeightTotal = GradeCalculator.TotalWeight(course.Categories),
                // No tab asked for: open on the scope the course reflects.
                ActiveTab = string.IsNullOrEmpty(tab) ? scope : NormalizeTab(tab),
                MidtermTotal = GradeCalculator.PeriodTotal(course.Categories, GradePeriods.Midterm),
                FinalTotal = GradeCalculator.PeriodTotal(course.Categories, GradePeriods.FinalTerm),
                Rows = rows,
                Summary = summary
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

            // The scores that really changed, per student. Worked out once and shared by the progress
            // history and the notification below. A record is written once, however many cells changed.
            var changesByStudent = new Dictionary<string, List<ScoreChange>>();
            var recordsByStudent = new Dictionary<string, GradeRecord>();
            var toWrite = new Dictionary<string, GradeRecord>();

            foreach (var change in pending)
            {
                var record = change.Item1;
                var categoryName = change.Item2;
                var newScore = change.Item3;

                var entry = record.Scores.FirstOrDefault(s => string.Equals(s.Category, categoryName, StringComparison.OrdinalIgnoreCase));
                if (entry == null) continue;

                recordsByStudent[record.StudentId] = record;

                if (!SameScore(entry.Score, newScore))
                {
                    List<ScoreChange> list;
                    if (!changesByStudent.TryGetValue(record.StudentId, out list))
                    {
                        list = new List<ScoreChange>();
                        changesByStudent[record.StudentId] = list;
                    }
                    list.Add(new ScoreChange { Category = entry.Category, OldScore = entry.Score, NewScore = newScore });
                }

                if (entry.Score != newScore)
                {
                    entry.Score = newScore;
                    toWrite[record.Id] = record;
                }
            }

            foreach (var record in toWrite.Values)
            {
                var update = Builders<GradeRecord>.Update.Set(g => g.Scores, record.Scores);
                db.Grades.UpdateOne(g => g.Id == record.Id, update);
            }

            // One history row and one notification per student per save, only for students whose scores really changed.
            var nowUtc = DateTime.UtcNow;
            foreach (var changed in changesByStudent)
            {
                HistoryService.RecordChange(course, changed.Key, recordsByStudent[changed.Key].Scores,
                    nowUtc, HistoryService.ReasonScores, changed.Value);

                NotificationService.NotifyScoresChanged(course, changed.Key,
                    changed.Value.Select(c => c.Category).ToList());
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

            var enrolledIds = new List<string>();

            foreach (var id in (studentIds ?? Enumerable.Empty<string>()).Distinct())
            {
                var student = db.Users.Find(u => u.Id == id).FirstOrDefault();
                if (student == null || student.Role != Roles.Student || !student.IsActive) continue;
                if (course.StudentIds.Contains(id)) continue;

                course.StudentIds.Add(id);
                EnsureRecord(db, course, id);
                enrolledIds.Add(id);
                added++;
            }

            var update = Builders<Course>.Update.Set(c => c.StudentIds, course.StudentIds);
            db.Courses.UpdateOne(c => c.Id == course.Id, update);

            foreach (var id in enrolledIds)
            {
                NotificationService.NotifyEnrolled(course, id);
            }

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

            NotificationService.NotifyRemoved(course, studentId);

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
                    Period = GradePeriods.Normalize(c.Period),
                    Weight = c.Weight.ToString("0.##", CultureInfo.InvariantCulture)
                });
            }
            return model;
        }

        public static CategoriesViewModel EchoCategories(string courseId, string facultyId, IList<string> originals, IList<string> names, IList<string> periods, IList<string> weights, List<string> errors)
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
                    Period = GradePeriods.Normalize(At(periods, i)),
                    Weight = At(weights, i)
                });
            }
            model.Errors = errors;
            return model;
        }

        public static bool SaveCategories(string courseId, string facultyId, IList<string> originals, IList<string> names, IList<string> periods, IList<string> weights, out List<string> errors)
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
                var period = GradePeriods.Normalize((At(periods, i) ?? string.Empty).Trim());

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
                    errors.Add(row + " is used twice. Each category name must be unique in the course (for example Midterm Quizzes and Final Quizzes).");
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
                    newCategories.Add(new GradeCategory { Name = name, Period = period, Weight = Math.Round(weight, 2) });
                }
            }

            if (errors.Count == 0 && newCategories.Count == 0)
            {
                errors.Add("Add at least one grading category.");
            }

            // Each period that has categories must total exactly 100. A period with no categories is allowed.
            if (errors.Count == 0)
            {
                foreach (var period in GradePeriods.All)
                {
                    var inPeriod = newCategories.Where(c => c.Period == period).ToList();
                    if (inPeriod.Count == 0) continue;

                    var total = Math.Round(inPeriod.Sum(c => c.Weight), 2);
                    if (Math.Abs(total - 100) >= GradeCalculator.WeightTolerance)
                    {
                        errors.Add(period + " weights must add up to exactly 100% (currently " +
                            total.ToString("0.##", CultureInfo.InvariantCulture) + "%).");
                    }
                }
            }

            if (errors.Count > 0) return false;

            // Store Midterm first, then Final Term, keeping the typed order inside each group.
            // parsed and newCategories are reordered together: the rename mapping below matches them by index.
            var order = Enumerable.Range(0, newCategories.Count)
                .OrderBy(i => newCategories[i].Period == GradePeriods.FinalTerm ? 1 : 0)
                .ToList();
            newCategories = order.Select(i => newCategories[i]).ToList();
            parsed = order.Select(i => parsed[i]).ToList();

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
            var rebuiltByStudent = new Dictionary<string, List<ScoreEntry>>();
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
                rebuiltByStudent[record.StudentId] = rebuilt;
            }

            // Compared before saving, so students are only told when something really changed.
            var categoriesChanged = !SameCategories(course.Categories, newCategories);

            var courseUpdate = Builders<Course>.Update.Set(c => c.Categories, newCategories);
            db.Courses.UpdateOne(c => c.Id == course.Id, courseUpdate);

            if (categoriesChanged)
            {
                // The history is worked out with the NEW categories and the rebuilt scores.
                course.Categories = newCategories;
                var nowUtc = DateTime.UtcNow;

                foreach (var studentId in course.StudentIds)
                {
                    List<ScoreEntry> scores;
                    if (!rebuiltByStudent.TryGetValue(studentId, out scores)) scores = new List<ScoreEntry>();

                    HistoryService.RecordCategoriesChanged(course, studentId, scores, nowUtc);
                    NotificationService.NotifyCategoriesChanged(course, studentId);
                }
            }

            return true;
        }

        // ---------- helpers ----------

        /// <summary>True when two scores are the same (both empty, or equal to two decimals).</summary>
        private static bool SameScore(double? a, double? b)
        {
            if (!a.HasValue && !b.HasValue) return true;
            if (!a.HasValue || !b.HasValue) return false;
            return Math.Abs(a.Value - b.Value) < 0.005;
        }

        private static bool SameCategories(IList<GradeCategory> before, IList<GradeCategory> after)
        {
            if (before.Count != after.Count) return false;

            for (var i = 0; i < before.Count; i++)
            {
                if (before[i].Name != after[i].Name) return false;
                if (before[i].Period != after[i].Period) return false;
                if (Math.Abs(before[i].Weight - after[i].Weight) >= 0.005) return false;
            }
            return true;
        }

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

            // One query for every enrolled student's scores (instead of one per student).
            var courseId = course.Id;
            var enrolled = course.StudentIds;
            var records = db.Grades.Find(g => g.CourseId == courseId && enrolled.Contains(g.StudentId)).ToList();
            var byStudent = new Dictionary<string, List<ScoreEntry>>();
            foreach (var r in records)
            {
                if (r.StudentId != null && !byStudent.ContainsKey(r.StudentId))
                {
                    byStudent[r.StudentId] = r.Scores ?? new List<ScoreEntry>();
                }
            }

            var scope = GradeScopes.Normalize(course.ReflectedScope);

            foreach (var student in students)
            {
                List<ScoreEntry> scores;
                if (!byStudent.TryGetValue(student.Id, out scores)) scores = new List<ScoreEntry>();

                var row = new ScoreRow
                {
                    StudentId = student.Id,
                    StudentNo = student.StudentNo,
                    FullName = student.FullName,
                    Result = GradeCalculator.Calculate(course.Categories, scores),
                    MidtermResult = GradeCalculator.CalculatePeriod(course.Categories, scores, GradePeriods.Midterm),
                    FinalResult = GradeCalculator.CalculatePeriod(course.Categories, scores, GradePeriods.FinalTerm),
                    ScopeResult = GradeCalculator.CalculateScope(course.Categories, scores, scope)
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