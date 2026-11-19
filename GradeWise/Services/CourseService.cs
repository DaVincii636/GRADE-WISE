using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GradeWise.Data;
using GradeWise.Models;
using MongoDB.Driver;

namespace GradeWise.Services
{
    public static class CourseService
    {
        public const int MaxCodeLength = 20;
        public const int MaxNameLength = 100;
        public const int MaxCategoryNameLength = 40;
        private const int MaxErrorsShown = 15;

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

        public static List<string> GetAvailableSections() => new List<string>();

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

        public static string CreateCourse(string facultyId, string code, string name, string section, string term, out List<string> errors)
        {
            code = (code ?? string.Empty).Trim();
            name = (name ?? string.Empty).Trim();
            section = (section ?? string.Empty).Trim();
            term = (term ?? string.Empty).Trim();

            var db = new MongoDbContext();
            errors = ValidateCourseFields(db, facultyId, null, code, name, section, true, term, out var chosenTerm);
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

        private static List<string> ValidateCourseFields(MongoDbContext db, string facultyId, string excludeCourseId, string code, string name, string section, bool checkSection, string term, out AcademicTerm chosenTerm)
        {
            var errors = new List<string>();
            if (code.Length == 0) errors.Add("Course code is required.");
            else if (code.Length > MaxCodeLength) errors.Add("Course code must be " + MaxCodeLength + " characters or fewer.");
            if (name.Length == 0) errors.Add("Course name is required.");
            else if (name.Length > MaxNameLength) errors.Add("Course name must be " + MaxNameLength + " characters or fewer.");

            if (checkSection && section.Length > 0 && !GetAvailableSections().Any(x => string.Equals(x, section, StringComparison.OrdinalIgnoreCase)))
                errors.Add("Choose a section from the list.");

            var termKey = term.ToLower();
            chosenTerm = db.Terms.Find(t => t.Name.ToLower() == termKey).FirstOrDefault();
            if (chosenTerm == null) errors.Add("Choose a term from the list.");

            if (errors.Count == 0)
            {
                var codeKey = code.ToLower();
                var termName = chosenTerm.Name.ToLower();
                var exclude = excludeCourseId ?? string.Empty;

                var existingCourse = db.Courses.Find(c =>
                    c.FacultyId == facultyId &&
                    c.Id != exclude &&
                    c.Code.ToLower() == codeKey &&
                    c.Term.ToLower() == termName).FirstOrDefault();

                if (existingCourse != null) errors.Add("You already have a course with that code in this term.");
            }
            return errors;
        }

        public static bool UpdateCourse(string courseId, string facultyId, string code, string name, string section, string term, out List<string> errors)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) { errors = new List<string> { "That course was not found." }; return false; }

            code = (code ?? string.Empty).Trim();
            name = (name ?? string.Empty).Trim();
            term = (term ?? string.Empty).Trim();

            var stored = course.Section ?? string.Empty;
            section = section == null ? stored : section.Trim();
            var sectionChanged = !string.Equals(section, stored, StringComparison.OrdinalIgnoreCase);
            if (!sectionChanged) section = stored;

            errors = ValidateCourseFields(db, facultyId, course.Id, code, name, section, sectionChanged, term, out var chosenTerm);
            if (errors.Count > 0) return false;

            var courseKey = course.Id;
            db.Courses.UpdateOne(c => c.Id == courseKey && c.FacultyId == facultyId, Builders<Course>.Update
                .Set(c => c.Code, code).Set(c => c.Name, name).Set(c => c.Section, section).Set(c => c.Term, chosenTerm.Name));
            return true;
        }

        public static bool SetReflectedScope(string courseId, string facultyId, string scope)
        {
            if (string.IsNullOrEmpty(courseId) || string.IsNullOrEmpty(facultyId)) return false;
            var db = new MongoDbContext();
            var saved = GradeScopes.Normalize(scope);
            return db.Courses.UpdateOne(c => c.Id == courseId && c.FacultyId == facultyId, Builders<Course>.Update.Set(c => c.ReflectedScope, saved)).MatchedCount > 0;
        }

        public static string NormalizeTab(string tab) => tab == "final-term" || tab == "final-grade" ? tab : "midterm";

        public static CourseDetailViewModel GetDetail(string courseId, string facultyId, Func<string, string> raw, string tab = null)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return null;
            EnsureHierarchy(db, course);

            var rows = BuildRows(db, course, raw);
            var scope = GradeScopes.Normalize(course.ReflectedScope);
            var summary = GradeCalculator.Summarize(rows.Select(r => r.ScopeResult.EarnedOrNull));
            var terms = db.Terms.Find(_ => true).ToList();

            return new CourseDetailViewModel
            {
                Scope = scope,
                ScopeLabel = GradeScopes.Label(scope),
                ScopeReady = GradeCalculator.ScopeReady(course.Categories, scope),
                Equivalent = GradeCalculator.EquivalentGrade(summary.Average),
                EditCourse = new CreateCourseViewModel { Code = course.Code, Name = course.Name, Section = course.Section, Term = course.Term, SectionNames = GetAvailableSections(), TermNames = terms.Select(t => t.Name).ToList() },
                CourseId = course.Id,
                Code = course.Code,
                Name = course.Name,
                Term = course.Term,
                Section = course.Section,
                Categories = course.Categories.Select(c => new GradeCategory { Name = c.Name, Weight = c.Weight, Period = GradePeriods.Normalize(c.Period), Group = NormalizeGroup(c.Group), Assessments = c.Assessments ?? new List<GradeAssessment>() }).ToList(),
                WeightTotal = GradeCalculator.TotalWeight(course.Categories),
                ActiveTab = string.IsNullOrEmpty(tab) ? scope : NormalizeTab(tab),
                MidtermTotal = GradeCalculator.PeriodTotal(course.Categories, GradePeriods.Midterm),
                FinalTotal = GradeCalculator.PeriodTotal(course.Categories, GradePeriods.FinalTerm),
                Rows = rows,
                Summary = summary
            };
        }

        public static string ScoreFieldName(string studentId, string category) => "score__" + studentId + "__" + category;

        public static bool SaveScores(string courseId, string facultyId, Func<string, string> raw, out List<string> errors)
        {
            errors = new List<string>();
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);

            if (course == null) { errors.Add("That course no longer exists."); return false; }

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

                    if (!TryParseScore(text, out double? value))
                    {
                        problems++;
                        if (errors.Count < MaxErrorsShown) errors.Add(label + " / " + category.Name + ": enter a number from 0 to 100.");
                        continue;
                    }
                    pending.Add(Tuple.Create(record, category.Name, value));
                }
            }

            if (problems > MaxErrorsShown) errors.Add("...and " + (problems - MaxErrorsShown) + " more.");
            if (problems > 0) return false;

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
                    if (!changesByStudent.TryGetValue(record.StudentId, out var list))
                    {
                        list = new List<ScoreChange>();
                        changesByStudent[record.StudentId] = list;
                    }
                    list.Add(new ScoreChange { Category = entry.Category, OldScore = entry.Score, NewScore = newScore });
                }

                if (entry.Score != newScore) { entry.Score = newScore; toWrite[record.Id] = record; }
            }

            foreach (var record in toWrite.Values)
                db.Grades.UpdateOne(g => g.Id == record.Id, Builders<GradeRecord>.Update.Set(g => g.Scores, record.Scores));

            var nowUtc = DateTime.UtcNow;
            foreach (var changed in changesByStudent)
            {
                HistoryService.RecordChange(course, changed.Key, recordsByStudent[changed.Key].Scores, nowUtc, HistoryService.ReasonScores, changed.Value);
                NotificationService.NotifyScoresChanged(course, changed.Key, changed.Value.Select(c => c.Category).ToList());

                // NEW: Send Email Alert
                var studentUser = db.Users.Find(u => u.Id == changed.Key).FirstOrDefault();
                if (studentUser != null && !string.IsNullOrEmpty(studentUser.Email))
                {
                    EmailService.SendGradeUpdate(studentUser.Email, studentUser.FullName, course.Code, course.Name);
                }
            }
            return true;
        }

        private static string NormalizeGroup(string group)
        {
            return string.Equals(group, "Exam", StringComparison.OrdinalIgnoreCase) ? "Exam" : "Class Standing";
        }

        /// <summary>Backfills older courses into the expandable hierarchy without deleting their legacy scores.</summary>
        private static void EnsureHierarchy(MongoDbContext db, Course course)
        {
            var changed = false;
            foreach (var category in course.Categories)
            {
                if (string.IsNullOrWhiteSpace(category.Group) || (string.Equals(category.Group, "Class Standing", StringComparison.OrdinalIgnoreCase) && category.Name != null && category.Name.IndexOf("exam", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    category.Group = category.Name != null && category.Name.IndexOf("exam", StringComparison.OrdinalIgnoreCase) >= 0 ? "Exam" : "Class Standing";
                    changed = true;
                }
                if (category.Assessments == null) { category.Assessments = new List<GradeAssessment>(); changed = true; }
                if (category.Assessments.Count == 0)
                {
                    category.Assessments.Add(new GradeAssessment { Id = "a-" + Guid.NewGuid().ToString("N").Substring(0, 10), Name = "Assessment 1", Order = 1 });
                    changed = true;
                }
            }
            if (changed)
            {
                db.Courses.UpdateOne(c => c.Id == course.Id, Builders<Course>.Update.Set(c => c.Categories, course.Categories));
                var records = db.Grades.Find(g => g.CourseId == course.Id).ToList();
                foreach (var record in records)
                {
                    var scoreChanged = false;
                    foreach (var category in course.Categories)
                    {
                        var entry = record.Scores.FirstOrDefault(x => string.Equals(x.Category, category.Name, StringComparison.OrdinalIgnoreCase));
                        if (entry != null && entry.Score.HasValue && category.Assessments.Count > 0 && (entry.AssessmentScores == null || entry.AssessmentScores.Count == 0))
                        {
                            entry.AssessmentScores = new List<AssessmentScoreEntry> { new AssessmentScoreEntry { AssessmentId = category.Assessments[0].Id, Score = entry.Score } };
                            scoreChanged = true;
                        }
                    }
                    if (scoreChanged) db.Grades.UpdateOne(g => g.Id == record.Id, Builders<GradeRecord>.Update.Set(g => g.Scores, record.Scores));
                }
            }
        }

        public static AssessmentScoreViewModel GetAssessmentScores(string courseId, string facultyId, string categoryName, string assessmentId)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return null;
            EnsureHierarchy(db, course);
            var category = course.Categories.FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase));
            var assessment = category == null ? null : category.Assessments.FirstOrDefault(a => a.Id == assessmentId);
            if (assessment == null) return null;
            var students = db.Users.Find(u => course.StudentIds.Contains(u.Id)).SortBy(u => u.FullName).ToList();
            var records = db.Grades.Find(g => g.CourseId == course.Id && course.StudentIds.Contains(g.StudentId)).ToList();
            return new AssessmentScoreViewModel
            {
                CourseId = course.Id, CategoryName = category.Name, Period = GradePeriods.Normalize(category.Period),
                AssessmentId = assessment.Id, AssessmentName = assessment.Name,
                Rows = students.Select(st => {
                    var rec = records.FirstOrDefault(r => r.StudentId == st.Id);
                    var cat = rec == null ? null : rec.Scores.FirstOrDefault(x => string.Equals(x.Category, category.Name, StringComparison.OrdinalIgnoreCase));
                    var score = cat == null || cat.AssessmentScores == null ? null : cat.AssessmentScores.FirstOrDefault(x => x.AssessmentId == assessment.Id);
                    return new AssessmentScoreRow { StudentId = st.Id, StudentNo = st.StudentNo, FullName = st.FullName, Score = score == null || !score.Score.HasValue ? "" : score.Score.Value.ToString("0.##", CultureInfo.InvariantCulture) };
                }).ToList()
            };
        }

        public static bool SaveAssessmentScores(string courseId, string facultyId, string categoryName, string assessmentId, Func<string,string> raw, out List<string> errors)
        {
            errors = new List<string>();
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) { errors.Add("Course not found."); return false; }
            EnsureHierarchy(db, course);
            var category = course.Categories.FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase));
            var assessment = category == null ? null : category.Assessments.FirstOrDefault(a => a.Id == assessmentId);
            if (assessment == null) { errors.Add("Assessment not found."); return false; }
            var pending = new List<Tuple<GradeRecord,string,double?>>();
            foreach (var studentId in course.StudentIds)
            {
                var student = db.Users.Find(u => u.Id == studentId).FirstOrDefault();
                var key = "assessment__" + studentId;
                var text = raw(key);
                if (text == null) continue;
                if (!TryParseScore(text, out var score)) { errors.Add((student == null ? studentId : student.FullName) + ": enter a score from 0 to 100."); continue; }
                var record = EnsureRecord(db, course, studentId);
                var entry = record.Scores.FirstOrDefault(x => string.Equals(x.Category, category.Name, StringComparison.OrdinalIgnoreCase));
                if (entry == null) { entry = new ScoreEntry { Category = category.Name }; record.Scores.Add(entry); }
                pending.Add(Tuple.Create(record, studentId, score));
            }
            if (errors.Count > 0) return false;
            foreach (var item in pending)
            {
                var record = item.Item1;
                var entry = record.Scores.First(x => string.Equals(x.Category, category.Name, StringComparison.OrdinalIgnoreCase));
                if (entry.AssessmentScores == null) entry.AssessmentScores = new List<AssessmentScoreEntry>();
                var score = entry.AssessmentScores.FirstOrDefault(x => x.AssessmentId == assessmentId);
                if (score == null) { score = new AssessmentScoreEntry { AssessmentId = assessmentId }; entry.AssessmentScores.Add(score); }
                score.Score = item.Item3;
                db.Grades.UpdateOne(g => g.Id == record.Id, Builders<GradeRecord>.Update.Set(g => g.Scores, record.Scores));
            }
            foreach (var item in pending.Where(x => x.Item3.HasValue))
                NotificationService.NotifyScoresChanged(course, item.Item2, new List<string> { category.Name + " / " + assessment.Name });
            return true;
        }

        public static bool AddAssessment(string courseId, string facultyId, string categoryName, string name, out string error)
        {
            error = null; name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 40) { error = "Enter an assessment name up to 40 characters."; return false; }
            var db = new MongoDbContext(); var course = FindOwned(db, courseId, facultyId);
            if (course == null) { error = "Course not found."; return false; }
            EnsureHierarchy(db, course);
            var category = course.Categories.FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase));
            if (category == null) { error = "Category not found."; return false; }
            if (category.Assessments.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))) { error = "That assessment already exists."; return false; }
            category.Assessments.Add(new GradeAssessment { Id = "a-" + Guid.NewGuid().ToString("N").Substring(0, 10), Name = name, Order = category.Assessments.Count + 1 });
            db.Courses.UpdateOne(c => c.Id == course.Id, Builders<Course>.Update.Set(c => c.Categories, course.Categories));
            return true;
        }

        public static bool RemoveAssessment(string courseId, string facultyId, string categoryName, string assessmentId)
        {
            var db = new MongoDbContext(); var course = FindOwned(db, courseId, facultyId);
            if (course == null) return false;
            var category = course.Categories.FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase));
            if (category == null || category.Assessments == null || category.Assessments.Count <= 1) return false;
            if (!category.Assessments.RemoveAll(a => a.Id == assessmentId).Equals(1)) return false;
            db.Courses.UpdateOne(c => c.Id == course.Id, Builders<Course>.Update.Set(c => c.Categories, course.Categories));
            var records = db.Grades.Find(g => g.CourseId == course.Id).ToList();
            foreach (var record in records)
            {
                foreach (var entry in record.Scores.Where(x => string.Equals(x.Category, category.Name, StringComparison.OrdinalIgnoreCase)))
                    if (entry.AssessmentScores != null) entry.AssessmentScores.RemoveAll(x => x.AssessmentId == assessmentId);
                db.Grades.UpdateOne(g => g.Id == record.Id, Builders<GradeRecord>.Update.Set(g => g.Scores, record.Scores));
            }
            return true;
        }

        public static bool TryParseScore(string text, out double? value)
        {
            value = null;
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0) return true;

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) return false;
            if (double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < 0 || parsed > 100) return false;

            value = Math.Round(parsed, 2);
            return true;
        }

        public static EnrollViewModel GetEnrollment(string courseId, string facultyId)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return null;

            return new EnrollViewModel
            {
                CourseId = course.Id,
                Code = course.Code,
                Name = course.Name,
                Term = course.Term,
                Enrolled = db.Users.Find(u => course.StudentIds.Contains(u.Id)).SortBy(u => u.FullName).ToList(),
                Available = db.Users.Find(u => u.Role == Roles.Student && u.IsActive && !course.StudentIds.Contains(u.Id)).SortBy(u => u.FullName).ToList()
            };
        }

        public static int Enroll(string courseId, string facultyId, IEnumerable<string> studentIds, out string error)
        {
            error = null;
            var added = 0;
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);

            if (course == null) { error = "That course no longer exists."; return 0; }

            var idsToEnroll = (studentIds ?? Enumerable.Empty<string>()).Distinct().ToList();
            var enrolledIds = new List<string>();

            foreach (var id in idsToEnroll)
            {
                var student = db.Users.Find(u => u.Id == id).FirstOrDefault();
                if (student == null || student.Role != Roles.Student || !student.IsActive || course.StudentIds.Contains(id)) continue;

                course.StudentIds.Add(id);
                EnsureRecord(db, course, id);
                enrolledIds.Add(id);
                added++;
            }

            if (added > 0)
            {
                db.Courses.UpdateOne(c => c.Id == course.Id, Builders<Course>.Update.AddToSetEach(c => c.StudentIds, enrolledIds));
                foreach (var id in enrolledIds) NotificationService.NotifyEnrolled(course, id);
            }
            else error = "Choose at least one student to enroll.";

            return added;
        }

        public static string Unenroll(string courseId, string facultyId, string studentId)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null || !course.StudentIds.Remove(studentId)) return null;

            db.Courses.UpdateOne(c => c.Id == course.Id, Builders<Course>.Update.Pull(c => c.StudentIds, studentId));
            db.Grades.DeleteMany(g => g.CourseId == course.Id && g.StudentId == studentId);
            db.Snapshots.DeleteMany(h => h.CourseId == course.Id && h.StudentId == studentId);
            NotificationService.NotifyRemoved(course, studentId);

            return db.Users.Find(u => u.Id == studentId).FirstOrDefault()?.FullName ?? studentId;
        }

        public static CategoriesViewModel GetCategories(string courseId, string facultyId)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return null;

            var model = NewCategoriesModel(course);
            EnsureHierarchy(db, course);
            foreach (var c in course.Categories)
            {
                model.Rows.Add(new CategoryRow
                {
                    Original = c.Name,
                    Name = c.Name,
                    Period = GradePeriods.Normalize(c.Period),
                    Weight = c.Weight.ToString("0.##", CultureInfo.InvariantCulture),
                    Group = NormalizeGroup(c.Group)
                });
            }
            return model;
        }

        public static CategoriesViewModel EchoCategories(string courseId, string facultyId, IList<string> originals, IList<string> names, IList<string> periods, IList<string> weights, IList<string> groups, List<string> errors)
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
                    Weight = At(weights, i),
                    Group = NormalizeGroup(At(groups, i))
                });
            }
            model.Errors = errors;
            return model;
        }

        public static bool SaveCategories(string courseId, string facultyId, IList<string> originals, IList<string> names, IList<string> periods, IList<string> weights, IList<string> groups, out List<string> errors)
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
                var group = NormalizeGroup((At(groups, i) ?? (name.IndexOf("exam", StringComparison.OrdinalIgnoreCase) >= 0 ? "Exam" : "Class Standing")).Trim());

                if (name.Length == 0 && weightText.Length == 0) continue;

                var row = name.Length > 0 ? "\"" + name + "\"" : "Row " + (i + 1);
                var rowOk = true;

                if (name.Length == 0) { errors.Add(row + ": enter a category name."); rowOk = false; }
                else if (name.Length > MaxCategoryNameLength) { errors.Add(row + ": the name must be " + MaxCategoryNameLength + " characters or fewer."); rowOk = false; }
                else if (!seenNames.Add(name)) { errors.Add(row + " is used twice. Each category name must be unique."); rowOk = false; }

                if (!double.TryParse(weightText, NumberStyles.Float, CultureInfo.InvariantCulture, out double weight) || double.IsNaN(weight) || double.IsInfinity(weight) || weight <= 0 || weight > 100)
                { errors.Add(row + ": weight must be a number above 0 and up to 100."); rowOk = false; }

                if (rowOk)
                {
                    parsed.Add(new CategoryRow { Original = original, Name = name, Group = group });
                    newCategories.Add(new GradeCategory { Name = name, Period = period, Weight = Math.Round(weight, 2), Group = group, Assessments = new List<GradeAssessment> { new GradeAssessment { Id = "a-" + Guid.NewGuid().ToString("N").Substring(0, 10), Name = "Assessment 1", Order = 1 } } });
                }
            }

            if (errors.Count == 0 && newCategories.Count == 0) errors.Add("Add at least one grading category.");

            if (errors.Count == 0)
            {
                foreach (var period in GradePeriods.All)
                {
                    var inPeriod = newCategories.Where(c => c.Period == period).ToList();
                    if (inPeriod.Count == 0) continue;
                    var total = Math.Round(inPeriod.Sum(c => c.Weight), 2);
                    if (Math.Abs(total - 100) >= GradeCalculator.WeightTolerance)
                        errors.Add(period + " category weights must add up to 100% (currently " + total.ToString("0.##", CultureInfo.InvariantCulture) + "%).");
                    var standing = Math.Round(inPeriod.Where(c => NormalizeGroup(c.Group) == "Class Standing").Sum(c => c.Weight), 2);
                    var exam = Math.Round(inPeriod.Where(c => NormalizeGroup(c.Group) == "Exam").Sum(c => c.Weight), 2);
                    if (Math.Abs(standing - 60) >= GradeCalculator.WeightTolerance)
                        errors.Add(period + " Class Standing categories must total 60% (currently " + standing.ToString("0.##", CultureInfo.InvariantCulture) + "%). Use category names containing 'Exam' for the Exam group.");
                    if (Math.Abs(exam - 40) >= GradeCalculator.WeightTolerance)
                        errors.Add(period + " Exam categories must total 40% (currently " + exam.ToString("0.##", CultureInfo.InvariantCulture) + "%).");
                }
            }

            if (errors.Count > 0) return false;

            var order = Enumerable.Range(0, newCategories.Count).OrderBy(i => newCategories[i].Period == GradePeriods.FinalTerm ? 1 : 0).ToList();
            newCategories = order.Select(i => newCategories[i]).ToList();
            parsed = order.Select(i => parsed[i]).ToList();

            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) { errors.Add("That course no longer exists."); return false; }

            var oldNames = course.Categories.Select(c => c.Name).ToList();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sources = new List<string>();
            foreach (var row in parsed)
            {
                var source = oldNames.FirstOrDefault(n => string.Equals(n, row.Original, StringComparison.OrdinalIgnoreCase));
                if (source != null && used.Add(source)) sources.Add(source);
                else sources.Add(null);
            }

            for (var i = 0; i < newCategories.Count; i++)
            {
                if (sources[i] == null) continue;
                var oldCategory = course.Categories.FirstOrDefault(c => string.Equals(c.Name, sources[i], StringComparison.OrdinalIgnoreCase));
                if (oldCategory != null)
                {
                    newCategories[i].Group = NormalizeGroup(newCategories[i].Group);
                    newCategories[i].Assessments = oldCategory.Assessments ?? new List<GradeAssessment>();
                }
            }

            var gradeRecords = db.Grades.Find(g => g.CourseId == course.Id).ToList();
            var rebuiltByStudent = new Dictionary<string, List<ScoreEntry>>();
            foreach (var record in gradeRecords)
            {
                var rebuilt = new List<ScoreEntry>();
                for (var i = 0; i < newCategories.Count; i++)
                {
                    double? score = null;
                    var assessmentScores = new List<AssessmentScoreEntry>();
                    if (sources[i] != null)
                    {
                        var old = record.Scores.FirstOrDefault(s => string.Equals(s.Category, sources[i], StringComparison.OrdinalIgnoreCase));
                        if (old != null) { score = old.Score; assessmentScores = old.AssessmentScores ?? new List<AssessmentScoreEntry>(); }
                    }
                    rebuilt.Add(new ScoreEntry { Category = newCategories[i].Name, Score = score, AssessmentScores = assessmentScores });
                }

                db.Grades.UpdateOne(g => g.Id == record.Id, Builders<GradeRecord>.Update.Set(g => g.Scores, rebuilt));
                rebuiltByStudent[record.StudentId] = rebuilt;
            }

            var categoriesChanged = !SameCategories(course.Categories, newCategories);
            db.Courses.UpdateOne(c => c.Id == course.Id, Builders<Course>.Update.Set(c => c.Categories, newCategories));

            if (categoriesChanged)
            {
                course.Categories = newCategories;
                var nowUtc = DateTime.UtcNow;
                foreach (var studentId in course.StudentIds)
                {
                    if (!rebuiltByStudent.TryGetValue(studentId, out var scores)) scores = new List<ScoreEntry>();
                    HistoryService.RecordCategoriesChanged(course, studentId, scores, nowUtc);
                    NotificationService.NotifyCategoriesChanged(course, studentId);
                }
            }
            return true;
        }

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
                if (before[i].Name != after[i].Name || before[i].Period != after[i].Period || Math.Abs(before[i].Weight - after[i].Weight) >= 0.005) return false;
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
                record = new GradeRecord { Id = "g-" + Guid.NewGuid().ToString("N").Substring(0, 8), CourseId = course.Id, StudentId = studentId };
                db.Grades.InsertOne(record);
            }

            var updated = false;
            foreach (var category in course.Categories)
            {
                if (!record.Scores.Any(s => string.Equals(s.Category, category.Name, StringComparison.OrdinalIgnoreCase)))
                { record.Scores.Add(new ScoreEntry { Category = category.Name, Score = null }); updated = true; }
            }

            if (updated) db.Grades.UpdateOne(g => g.Id == record.Id, Builders<GradeRecord>.Update.Set(g => g.Scores, record.Scores));
            return record;
        }

        private static List<ScoreRow> BuildRows(MongoDbContext db, Course course, Func<string, string> raw)
        {
            var rows = new List<ScoreRow>();
            var students = db.Users.Find(u => course.StudentIds.Contains(u.Id)).SortBy(u => u.FullName).ToList();

            var courseId = course.Id;
            var enrolled = course.StudentIds;
            var records = db.Grades.Find(g => g.CourseId == courseId && enrolled.Contains(g.StudentId)).ToList();
            var byStudent = new Dictionary<string, List<ScoreEntry>>();
            foreach (var r in records)
                if (r.StudentId != null && !byStudent.ContainsKey(r.StudentId)) byStudent[r.StudentId] = r.Scores ?? new List<ScoreEntry>();

            var scope = GradeScopes.Normalize(course.ReflectedScope);

            foreach (var student in students)
            {
                if (!byStudent.TryGetValue(student.Id, out var scores)) scores = new List<ScoreEntry>();

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
                        row.Inputs.Add(typed);
                        row.Invalid.Add(!TryParseScore(typed, out _));
                    }
                    else
                    {
                        var entry = scores.FirstOrDefault(s => string.Equals(s.Category, category.Name, StringComparison.OrdinalIgnoreCase));
                        var categoryScore = entry == null ? (double?)null : (entry.AssessmentScores != null && entry.AssessmentScores.Any(a => a.Score.HasValue) ? (double?)entry.AssessmentScores.Where(a => a.Score.HasValue).Average(a => a.Score.Value) : entry.Score);
                        row.Inputs.Add(categoryScore.HasValue ? categoryScore.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty);
                        row.Invalid.Add(false);
                    }
                }
                rows.Add(row);
            }
            return rows;
        }

        private static CategoriesViewModel NewCategoriesModel(Course course)
        {
            return new CategoriesViewModel { CourseId = course.Id, Code = course.Code, Name = course.Name, Term = course.Term };
        }

        private static string At(IList<string> list, int index) => list != null && index < list.Count ? list[index] : null;
    }
}