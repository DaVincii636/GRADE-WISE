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
        public const int MaxAssessmentNameLength = 40;
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
                var missing = rows.Sum(r => r.CategoryCells.Sum(cc => cc.Inputs.Count(string.IsNullOrEmpty)));

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

            var rows = BuildRows(db, course, raw);
            var maxes = EffectiveMaxes(course, raw);
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
                Categories = course.Categories.Select(c => new GradeCategory { Name = c.Name, Weight = c.Weight, Period = GradePeriods.Normalize(c.Period), Group = GradeGroups.Normalize(c.Group), Assessments = OrderedAssessments(c).Select(a => new GradeAssessment { Id = a.Id, Name = a.Name, Order = a.Order, MaxScore = maxes.ContainsKey(a.Id) ? maxes[a.Id] : a.MaxScore }).ToList() }).ToList(),
                WeightTotal = GradeCalculator.TotalWeight(course.Categories),
                ActiveTab = string.IsNullOrEmpty(tab) ? scope : NormalizeTab(tab),
                MidtermTotal = GradeCalculator.PeriodTotal(course.Categories, GradePeriods.Midterm),
                FinalTotal = GradeCalculator.PeriodTotal(course.Categories, GradePeriods.FinalTerm),
                Rows = rows,
                Summary = summary
            };
        }

        public static string ScoreFieldName(string studentId, string category) => "score__" + studentId + "__" + category;

        /// <summary>Name of the score box on the single-assessment page (matches Views/Faculty/Assessment.cshtml).</summary>
        public static string AssessmentFieldName(string studentId) => "assessment__" + studentId;

        /// <summary>Name of one score box of the class record (one student, one assessment column).</summary>
        public static string CellFieldName(string studentId, string assessmentId) => "cell__" + studentId + "__" + assessmentId;

        /// <summary>Name of the HIGHEST POSSIBLE SCORE box above one assessment column.</summary>
        public static string HpsFieldName(string assessmentId) => "hps__" + assessmentId;

        /// <summary>The assessments of a category in column order.</summary>
        public static IEnumerable<GradeAssessment> OrderedAssessments(GradeCategory category)
        {
            return (category == null || category.Assessments == null ? new List<GradeAssessment>() : category.Assessments).OrderBy(a => a.Order);
        }

        /// <summary>The course page tab ("midterm" or "final-term") that holds a category, so a redirect lands on the right table.</summary>
        public static string TabOfCategory(string courseId, string facultyId, string categoryName)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            var category = FindCategory(course, categoryName);
            return category != null && GradePeriods.Normalize(category.Period) == GradePeriods.FinalTerm ? "final-term" : "midterm";
        }

        /// <summary>
        /// Saves the class record boxes (one per student and assessment) and the highest possible scores.
        /// Nothing is saved when any box is invalid. A category's score is its PS:
        /// the total of the entered scores divided by the highest possible total of the same columns, times 100.
        /// </summary>
        public static bool SaveScores(string courseId, string facultyId, Func<string, string> raw, out List<string> errors)
        {
            errors = new List<string>();
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);

            if (course == null) { errors.Add("That course no longer exists."); return false; }

            var problems = 0;

            // 1. highest possible scores
            var maxes = new Dictionary<string, double>();
            foreach (var category in course.Categories)
            {
                foreach (var assessment in OrderedAssessments(category))
                {
                    maxes[assessment.Id] = assessment.MaxScore;
                    var typedMax = raw(HpsFieldName(assessment.Id));
                    if (typedMax == null) continue;

                    double parsedMax;
                    if (!TryParseMax(typedMax, out parsedMax))
                    {
                        problems++;
                        if (errors.Count < MaxErrorsShown) errors.Add(category.Name + " / " + assessment.Name + ": the highest possible score must be a number above 0.");
                        continue;
                    }
                    maxes[assessment.Id] = parsedMax;
                }
            }

            // 2. the score boxes
            var records = new Dictionary<string, GradeRecord>();
            var pending = new List<Tuple<string, GradeCategory, GradeAssessment, double?>>();

            foreach (var studentId in course.StudentIds)
            {
                var student = db.Users.Find(u => u.Id == studentId).FirstOrDefault();
                var label = student == null ? studentId : student.FullName;
                records[studentId] = EnsureRecord(db, course, studentId);

                foreach (var category in course.Categories)
                {
                    foreach (var assessment in OrderedAssessments(category))
                    {
                        var text = raw(CellFieldName(studentId, assessment.Id));
                        if (text == null) continue;

                        double? value;
                        if (!TryParseRawScore(text, maxes[assessment.Id], out value))
                        {
                            problems++;
                            if (errors.Count < MaxErrorsShown)
                                errors.Add(label + " / " + category.Name + " " + assessment.Name + ": enter a number from 0 to " + maxes[assessment.Id].ToString("0.##", CultureInfo.InvariantCulture) + ".");
                            continue;
                        }
                        pending.Add(Tuple.Create(studentId, category, assessment, value));
                    }
                }
            }

            if (problems > MaxErrorsShown) errors.Add("...and " + (problems - MaxErrorsShown) + " more.");
            if (problems > 0) return false;

            // 3. apply the highest possible scores
            var maxChanged = false;
            foreach (var category in course.Categories)
            {
                foreach (var assessment in OrderedAssessments(category))
                {
                    if (Math.Abs(assessment.MaxScore - maxes[assessment.Id]) >= 0.005) { assessment.MaxScore = maxes[assessment.Id]; maxChanged = true; }
                }
            }

            // 4. apply the score boxes
            var touched = new HashSet<ScoreEntry>();
            var dirty = new HashSet<string>();

            foreach (var item in pending)
            {
                var record = records[item.Item1];
                var entry = record.Scores.FirstOrDefault(sc => string.Equals(sc.Category, item.Item2.Name, StringComparison.OrdinalIgnoreCase));
                if (entry == null) continue;
                if (entry.AssessmentScores == null) entry.AssessmentScores = new List<AssessmentScoreEntry>();

                var assessmentId = item.Item3.Id;
                var existing = entry.AssessmentScores.FirstOrDefault(a => a.AssessmentId == assessmentId);
                if (SameScore(existing == null ? null : existing.Score, item.Item4)) continue;

                if (existing == null)
                {
                    existing = new AssessmentScoreEntry { AssessmentId = assessmentId };
                    entry.AssessmentScores.Add(existing);
                }
                existing.Score = item.Item4;
                touched.Add(entry);
                dirty.Add(record.Id);
            }

            // 5. recalculate the category scores (PS)
            var changesByStudent = new Dictionary<string, List<ScoreChange>>();
            var recordsByStudent = new Dictionary<string, GradeRecord>();

            foreach (var studentId in course.StudentIds)
            {
                var record = records[studentId];
                foreach (var category in course.Categories)
                {
                    var entry = record.Scores.FirstOrDefault(sc => string.Equals(sc.Category, category.Name, StringComparison.OrdinalIgnoreCase));
                    if (entry == null) continue;

                    var isTouched = touched.Contains(entry);
                    if (!isTouched && !(maxChanged && HasAnyAssessmentScore(entry))) continue;

                    var oldScore = entry.Score;
                    var newScore = CategoryPercent(entry.AssessmentScores, category);
                    if (oldScore.HasValue != newScore.HasValue || (oldScore.HasValue && Math.Abs(oldScore.Value - newScore.Value) > 0.0000001))
                    {
                        entry.Score = newScore;
                        dirty.Add(record.Id);
                    }

                    if (!SameScore(oldScore, newScore))
                    {
                        List<ScoreChange> list;
                        if (!changesByStudent.TryGetValue(studentId, out list))
                        {
                            list = new List<ScoreChange>();
                            changesByStudent[studentId] = list;
                        }
                        list.Add(new ScoreChange { Category = entry.Category, OldScore = oldScore, NewScore = newScore });
                        recordsByStudent[studentId] = record;
                    }
                }
            }

            if (maxChanged)
                db.Courses.UpdateOne(c => c.Id == course.Id && c.FacultyId == facultyId, Builders<Course>.Update.Set(c => c.Categories, course.Categories));

            foreach (var record in records.Values)
                if (dirty.Contains(record.Id))
                    db.Grades.UpdateOne(g => g.Id == record.Id, Builders<GradeRecord>.Update.Set(g => g.Scores, record.Scores));

            AnnounceScoreChanges(db, course, changesByStudent, recordsByStudent);
            return true;
        }

        /// <summary>A raw score from 0 up to the assessment's highest possible score. Blank is allowed (not graded yet).</summary>
        public static bool TryParseRawScore(string text, double max, out double? value)
        {
            value = null;
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0) return true;

            double parsed;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return false;
            if (double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < 0 || parsed > max + 0.0000001) return false;

            value = Math.Round(parsed, 2);
            return true;
        }

        /// <summary>A highest possible score: a number above 0 (up to 10000).</summary>
        public static bool TryParseMax(string text, out double value)
        {
            value = 0;
            text = (text ?? string.Empty).Trim();
            double parsed;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return false;
            if (double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed <= 0 || parsed > 10000) return false;

            value = Math.Round(parsed, 2);
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
            foreach (var c in course.Categories)
            {
                model.Rows.Add(new CategoryRow
                {
                    Original = c.Name,
                    Name = c.Name,
                    Period = GradePeriods.Normalize(c.Period),
                    Group = GradeGroups.Normalize(c.Group),
                    Weight = c.Weight.ToString("0.##", CultureInfo.InvariantCulture)
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
                    Group = GradeGroups.Normalize(At(groups, i)),
                    Weight = At(weights, i)
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
                var group = GradeGroups.Normalize(At(groups, i));

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
                    parsed.Add(new CategoryRow { Original = original, Name = name });
                    newCategories.Add(new GradeCategory { Name = name, Period = period, Group = group, Weight = Math.Round(weight, 2) });
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
                    {
                        errors.Add(period + " weights must add up to exactly 100% (currently " + total.ToString("0.##", CultureInfo.InvariantCulture) + "%).");
                        continue;
                    }

                    // Inside a period Class Standing must total 60 and Exam 40.
                    foreach (var group in GradeGroups.All)
                    {
                        var groupTotal = Math.Round(inPeriod.Where(c => c.Group == group).Sum(c => c.Weight), 2);
                        var required = GradeGroups.RequiredTotal(group);
                        if (Math.Abs(groupTotal - required) >= GradeCalculator.WeightTolerance)
                            errors.Add(period + " " + group + " must add up to exactly " + required.ToString("0.##", CultureInfo.InvariantCulture)
                                + "% (currently " + groupTotal.ToString("0.##", CultureInfo.InvariantCulture) + "%).");
                    }
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

            // Renaming a category keeps its assessments; every category keeps at least one.
            for (var i = 0; i < newCategories.Count; i++)
            {
                var kept = sources[i] == null ? null : course.Categories.FirstOrDefault(c => string.Equals(c.Name, sources[i], StringComparison.OrdinalIgnoreCase));
                var assessments = kept == null || kept.Assessments == null
                    ? new List<GradeAssessment>()
                    : kept.Assessments.Select(a => new GradeAssessment { Id = a.Id, Name = a.Name, Order = a.Order, MaxScore = a.MaxScore }).ToList();

                if (assessments.Count == 0)
                    assessments.Add(new GradeAssessment { Id = NewAssessmentId(), Name = "1", Order = 1, MaxScore = 100 });

                newCategories[i].Assessments = assessments;
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
                        if (old != null)
                        {
                            score = old.Score;
                            if (old.AssessmentScores != null)
                                assessmentScores = old.AssessmentScores.Select(a => new AssessmentScoreEntry { AssessmentId = a.AssessmentId, Score = a.Score }).ToList();
                        }
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

        // ---------- individual assessments (Quiz 1, Quiz 2, ...) inside a category ----------

        /// <summary>
        /// Score page for one assessment. Returns null when the course is not the faculty member's
        /// or the category / assessment does not exist.
        /// </summary>
        public static AssessmentScoreViewModel GetAssessmentScores(string courseId, string facultyId, string categoryName, string assessmentId)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return null;

            var category = FindCategory(course, categoryName);
            var assessment = FindAssessment(category, assessmentId);
            if (category == null || assessment == null) return null;

            var model = new AssessmentScoreViewModel
            {
                CourseId = course.Id,
                CategoryName = category.Name,
                AssessmentId = assessment.Id,
                AssessmentName = assessment.Name,
                MaxScore = assessment.MaxScore,
                EditName = assessment.Name,
                EditMax = GradeCalculator.Raw(assessment.MaxScore),
                Period = GradePeriods.Normalize(category.Period)
            };

            var students = db.Users.Find(u => course.StudentIds.Contains(u.Id)).SortBy(u => u.FullName).ToList();

            var enrolled = course.StudentIds;
            var records = db.Grades.Find(g => g.CourseId == courseId && enrolled.Contains(g.StudentId)).ToList();
            var byStudent = new Dictionary<string, List<ScoreEntry>>();
            foreach (var r in records)
                if (r.StudentId != null && !byStudent.ContainsKey(r.StudentId)) byStudent[r.StudentId] = r.Scores ?? new List<ScoreEntry>();

            foreach (var student in students)
            {
                string text = string.Empty;
                List<ScoreEntry> scores;
                if (byStudent.TryGetValue(student.Id, out scores))
                {
                    var entry = scores.FirstOrDefault(s => string.Equals(s.Category, category.Name, StringComparison.OrdinalIgnoreCase));
                    var own = entry == null || entry.AssessmentScores == null ? null : entry.AssessmentScores.FirstOrDefault(a => a.AssessmentId == assessment.Id);
                    if (own != null && own.Score.HasValue) text = own.Score.Value.ToString("0.##", CultureInfo.InvariantCulture);
                }

                model.Rows.Add(new AssessmentScoreRow { StudentId = student.Id, StudentNo = student.StudentNo, FullName = student.FullName, Score = text });
            }
            return model;
        }

        /// <summary>Name of the box that edits the assessment's name on its score page.</summary>
        public const string AssessmentNameField = "assessmentName";

        /// <summary>Name of the box that edits the assessment's highest possible score on its score page.</summary>
        public const string AssessmentMaxField = "assessmentMax";

        /// <summary>
        /// Saves the scores typed for one assessment, and its name and highest possible score when those
        /// boxes are posted. Nothing is saved when anything is invalid.
        /// The category score of each student is recalculated from their entered assessment scores.
        /// </summary>
        public static bool SaveAssessmentScores(string courseId, string facultyId, string categoryName, string assessmentId, Func<string, string> raw, out List<string> errors)
        {
            errors = new List<string>();
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) { errors.Add("That course no longer exists."); return false; }

            var category = FindCategory(course, categoryName);
            var assessment = FindAssessment(category, assessmentId);
            if (category == null || assessment == null) { errors.Add("That assessment no longer exists."); return false; }

            // The assessment's own name and highest possible score (saved together with the scores).
            var newName = assessment.Name;
            var newMax = assessment.MaxScore;

            var typedName = raw(AssessmentNameField);
            if (typedName != null)
            {
                typedName = typedName.Trim();
                if (typedName.Length == 0) errors.Add("The assessment name cannot be empty.");
                else if (typedName.Length > MaxAssessmentNameLength) errors.Add("The assessment name must be " + MaxAssessmentNameLength + " characters or fewer.");
                else if (category.Assessments.Any(a => a.Id != assessment.Id && string.Equals(a.Name, typedName, StringComparison.OrdinalIgnoreCase)))
                    errors.Add("This category already has an assessment named " + typedName + ".");
                else newName = typedName;
            }

            var typedMax = raw(AssessmentMaxField);
            if (typedMax != null)
            {
                double parsedMax;
                if (!TryParseMax(typedMax, out parsedMax)) errors.Add("The highest possible score must be a number above 0.");
                else newMax = parsedMax;
            }

            if (errors.Count > 0) return false;

            var pending = new List<Tuple<GradeRecord, double?>>();
            var problems = 0;

            foreach (var studentId in course.StudentIds)
            {
                var text = raw(AssessmentFieldName(studentId));
                if (text == null) continue;

                var student = db.Users.Find(u => u.Id == studentId).FirstOrDefault();
                var label = student == null ? studentId : student.FullName;

                double? value;
                if (!TryParseRawScore(text, newMax, out value))
                {
                    problems++;
                    if (errors.Count < MaxErrorsShown) errors.Add(label + " / " + newName + ": enter a number from 0 to " + newMax.ToString("0.##", CultureInfo.InvariantCulture) + ".");
                    continue;
                }
                pending.Add(Tuple.Create(EnsureRecord(db, course, studentId), value));
            }

            if (problems > MaxErrorsShown) errors.Add("...and " + (problems - MaxErrorsShown) + " more.");
            if (problems > 0) return false;

            // Apply the new name and highest possible score before the category scores are recalculated.
            var detailsChanged = newName != assessment.Name;
            var maxChanged = Math.Abs(assessment.MaxScore - newMax) >= 0.005;
            if (detailsChanged || maxChanged)
            {
                assessment.Name = newName;
                assessment.MaxScore = newMax;
                db.Courses.UpdateOne(c => c.Id == course.Id && c.FacultyId == facultyId, Builders<Course>.Update.Set(c => c.Categories, course.Categories));
            }

            var changesByStudent = new Dictionary<string, List<ScoreChange>>();
            var recordsByStudent = new Dictionary<string, GradeRecord>();
            var toWrite = new Dictionary<string, GradeRecord>();

            foreach (var item in pending)
            {
                var record = item.Item1;
                var newValue = item.Item2;

                var entry = record.Scores.FirstOrDefault(s => string.Equals(s.Category, category.Name, StringComparison.OrdinalIgnoreCase));
                if (entry == null) continue;
                if (entry.AssessmentScores == null) entry.AssessmentScores = new List<AssessmentScoreEntry>();

                var existing = entry.AssessmentScores.FirstOrDefault(a => a.AssessmentId == assessment.Id);
                var scoreChanged = !SameScore(existing == null ? null : existing.Score, newValue);
                // A new highest possible score changes the PS of every student who has a score in this category.
                if (!scoreChanged && !(maxChanged && HasAnyAssessmentScore(entry))) continue;

                if (scoreChanged)
                {
                    if (existing == null)
                    {
                        existing = new AssessmentScoreEntry { AssessmentId = assessment.Id };
                        entry.AssessmentScores.Add(existing);
                    }
                    existing.Score = newValue;
                }

                var oldCategoryScore = entry.Score;
                var newCategoryScore = CategoryPercent(entry.AssessmentScores, category);
                entry.Score = newCategoryScore;

                toWrite[record.Id] = record;
                recordsByStudent[record.StudentId] = record;

                if (!SameScore(oldCategoryScore, newCategoryScore))
                {
                    List<ScoreChange> list;
                    if (!changesByStudent.TryGetValue(record.StudentId, out list))
                    {
                        list = new List<ScoreChange>();
                        changesByStudent[record.StudentId] = list;
                    }
                    list.Add(new ScoreChange { Category = entry.Category, OldScore = oldCategoryScore, NewScore = newCategoryScore });
                }
            }

            foreach (var record in toWrite.Values)
                db.Grades.UpdateOne(g => g.Id == record.Id, Builders<GradeRecord>.Update.Set(g => g.Scores, record.Scores));

            AnnounceScoreChanges(db, course, changesByStudent, recordsByStudent);
            return true;
        }

        /// <summary>
        /// Adds an assessment column to a category (the next number, like 1, 2, 3 of the class record, when no name is given).
        /// maxScoreText is its highest possible score; blank means 100.
        /// </summary>
        public static bool AddAssessment(string courseId, string facultyId, string categoryName, string name, string maxScoreText, out string error)
        {
            error = null;
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) { error = "That course no longer exists."; return false; }

            var category = FindCategory(course, categoryName);
            if (category == null) { error = "That category was not found."; return false; }

            double max = 100;
            if (!string.IsNullOrWhiteSpace(maxScoreText) && !TryParseMax(maxScoreText, out max))
            { error = "The highest possible score must be a number above 0."; return false; }

            if (category.Assessments == null) category.Assessments = new List<GradeAssessment>();

            name = (name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                var number = category.Assessments.Count + 1;
                while (category.Assessments.Any(a => string.Equals(a.Name, number.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))) number++;
                name = number.ToString(CultureInfo.InvariantCulture);
            }
            if (name.Length > MaxAssessmentNameLength) { error = "The assessment name must be " + MaxAssessmentNameLength + " characters or fewer."; return false; }

            if (category.Assessments.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            { error = "This category already has an assessment with that name."; return false; }

            var nextOrder = category.Assessments.Count == 0 ? 1 : category.Assessments.Max(a => a.Order) + 1;
            category.Assessments.Add(new GradeAssessment { Id = NewAssessmentId(), Name = name, Order = nextOrder, MaxScore = max });

            db.Courses.UpdateOne(c => c.Id == course.Id && c.FacultyId == facultyId, Builders<Course>.Update.Set(c => c.Categories, course.Categories));
            return true;
        }

        /// <summary>
        /// Removes an assessment and the scores entered for it. A category must keep at least one assessment.
        /// Students' category scores are recalculated from what is left.
        /// </summary>
        public static bool RemoveAssessment(string courseId, string facultyId, string categoryName, string assessmentId)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return false;

            var category = FindCategory(course, categoryName);
            var assessment = FindAssessment(category, assessmentId);
            if (category == null || assessment == null) return false;
            if (category.Assessments.Count <= 1) return false;

            var removedId = assessment.Id;
            category.Assessments.RemoveAll(a => a.Id == removedId);
            db.Courses.UpdateOne(c => c.Id == course.Id && c.FacultyId == facultyId, Builders<Course>.Update.Set(c => c.Categories, course.Categories));

            var changesByStudent = new Dictionary<string, List<ScoreChange>>();
            var recordsByStudent = new Dictionary<string, GradeRecord>();
            var courseKey = course.Id;

            foreach (var record in db.Grades.Find(g => g.CourseId == courseKey).ToList())
            {
                var entry = record.Scores.FirstOrDefault(s => string.Equals(s.Category, category.Name, StringComparison.OrdinalIgnoreCase));
                if (entry == null || entry.AssessmentScores == null) continue;

                var removed = entry.AssessmentScores.FirstOrDefault(a => a.AssessmentId == removedId);
                if (removed == null) continue;

                var hadScore = removed.Score.HasValue;
                entry.AssessmentScores.RemoveAll(a => a.AssessmentId == removedId);

                var oldCategoryScore = entry.Score;
                if (hadScore) entry.Score = CategoryPercent(entry.AssessmentScores, category);

                db.Grades.UpdateOne(g => g.Id == record.Id, Builders<GradeRecord>.Update.Set(g => g.Scores, record.Scores));

                if (!SameScore(oldCategoryScore, entry.Score))
                {
                    recordsByStudent[record.StudentId] = record;
                    changesByStudent[record.StudentId] = new List<ScoreChange> { new ScoreChange { Category = entry.Category, OldScore = oldCategoryScore, NewScore = entry.Score } };
                }
            }

            AnnounceScoreChanges(db, course, changesByStudent, recordsByStudent);
            return true;
        }

        /// <summary>Records history, notifies students and sends the grade-update email for changed category scores.</summary>
        private static void AnnounceScoreChanges(MongoDbContext db, Course course, Dictionary<string, List<ScoreChange>> changesByStudent, Dictionary<string, GradeRecord> recordsByStudent)
        {
            var nowUtc = DateTime.UtcNow;
            foreach (var changed in changesByStudent)
            {
                HistoryService.RecordChange(course, changed.Key, recordsByStudent[changed.Key].Scores, nowUtc, HistoryService.ReasonScores, changed.Value);
                NotificationService.NotifyScoresChanged(course, changed.Key, changed.Value.Select(c => c.Category).ToList());

                var studentUser = db.Users.Find(u => u.Id == changed.Key).FirstOrDefault();
                if (studentUser != null && !string.IsNullOrEmpty(studentUser.Email))
                    EmailService.SendGradeUpdate(studentUser.Email, studentUser.FullName, course.Code, course.Name);
            }
        }

        /// <summary>
        /// The category's PS, like the class record: total of the entered scores / highest possible total of those
        /// same columns x 100. Columns without a score yet are left out. Null when nothing is entered.
        /// </summary>
        public static double? CategoryPercent(IEnumerable<AssessmentScoreEntry> entries, GradeCategory category)
        {
            var maxById = new Dictionary<string, double>();
            foreach (var a in (category.Assessments ?? new List<GradeAssessment>())) maxById[a.Id] = a.MaxScore;

            double total = 0, gradedMax = 0;
            foreach (var e in entries ?? Enumerable.Empty<AssessmentScoreEntry>())
            {
                double max;
                if (!e.Score.HasValue || e.AssessmentId == null || !maxById.TryGetValue(e.AssessmentId, out max)) continue;
                total += e.Score.Value;
                gradedMax += max;
            }

            return gradedMax <= 0 ? (double?)null : Math.Round(total / gradedMax * 100.0, 6);
        }

        private static bool HasAnyAssessmentScore(ScoreEntry entry)
        {
            return entry != null && entry.AssessmentScores != null && entry.AssessmentScores.Any(a => a.Score.HasValue);
        }

        /// <summary>Highest possible score of every assessment: the typed box when valid, otherwise the saved value.</summary>
        private static Dictionary<string, double> EffectiveMaxes(Course course, Func<string, string> raw)
        {
            var maxes = new Dictionary<string, double>();
            foreach (var category in course.Categories)
            {
                foreach (var a in OrderedAssessments(category))
                {
                    var value = a.MaxScore;
                    var typed = raw == null ? null : raw(HpsFieldName(a.Id));
                    double parsed;
                    if (typed != null && TryParseMax(typed, out parsed)) value = parsed;
                    maxes[a.Id] = value;
                }
            }
            return maxes;
        }

        private static GradeCategory FindCategory(Course course, string categoryName)
        {
            if (course == null || string.IsNullOrEmpty(categoryName)) return null;
            return course.Categories.FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase));
        }

        private static GradeAssessment FindAssessment(GradeCategory category, string assessmentId)
        {
            if (category == null || category.Assessments == null || string.IsNullOrEmpty(assessmentId)) return null;
            return category.Assessments.FirstOrDefault(a => a.Id == assessmentId);
        }

        private static string NewAssessmentId() => "a-" + Guid.NewGuid().ToString("N").Substring(0, 8);

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
                if (before[i].Name != after[i].Name || before[i].Period != after[i].Period || GradeGroups.Normalize(before[i].Group) != GradeGroups.Normalize(after[i].Group) || Math.Abs(before[i].Weight - after[i].Weight) >= 0.005) return false;
            return true;
        }

        private static Course FindOwned(MongoDbContext db, string courseId, string facultyId)
        {
            if (string.IsNullOrEmpty(courseId) || string.IsNullOrEmpty(facultyId)) return null;
            var course = db.Courses.Find(c => c.Id == courseId && c.FacultyId == facultyId).FirstOrDefault();
            if (course != null) EnsureAssessments(db, course);
            return course;
        }

        /// <summary>
        /// Every category needs at least one assessment column. Categories saved before assessments existed get
        /// one column named "1" (highest possible score 100) and the score already entered moves into it,
        /// so nothing is lost. Does nothing when every category already has a column.
        /// </summary>
        private static void EnsureAssessments(MongoDbContext db, Course course)
        {
            var missing = course.Categories.Where(c => c.Assessments == null || c.Assessments.Count == 0).ToList();
            if (missing.Count == 0) return;

            foreach (var c in missing)
                c.Assessments = new List<GradeAssessment> { new GradeAssessment { Id = NewAssessmentId(), Name = "1", Order = 1, MaxScore = 100 } };

            db.Courses.UpdateOne(x => x.Id == course.Id, Builders<Course>.Update.Set(x => x.Categories, course.Categories));

            var courseKey = course.Id;
            foreach (var record in db.Grades.Find(g => g.CourseId == courseKey).ToList())
            {
                var changed = false;
                foreach (var c in missing)
                {
                    var entry = record.Scores.FirstOrDefault(sc => string.Equals(sc.Category, c.Name, StringComparison.OrdinalIgnoreCase));
                    if (entry == null || !entry.Score.HasValue) continue;
                    if (entry.AssessmentScores == null) entry.AssessmentScores = new List<AssessmentScoreEntry>();
                    if (entry.AssessmentScores.Count > 0) continue;

                    entry.AssessmentScores.Add(new AssessmentScoreEntry { AssessmentId = c.Assessments[0].Id, Score = entry.Score });
                    changed = true;
                }
                if (changed) db.Grades.UpdateOne(g => g.Id == record.Id, Builders<GradeRecord>.Update.Set(g => g.Scores, record.Scores));
            }
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
            var maxes = EffectiveMaxes(course, raw);

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
                    ScopeResult = GradeCalculator.CalculateScope(course.Categories, scores, scope),
                    Incomplete = GradeCalculator.IsIncomplete(course.Categories, scores)
                };

                foreach (var category in course.Categories)
                {
                    var entry = scores.FirstOrDefault(s => string.Equals(s.Category, category.Name, StringComparison.OrdinalIgnoreCase));

                    // Category-level text (the PS), kept for the rest of the app.
                    row.Inputs.Add(entry != null && entry.Score.HasValue ? entry.Score.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty);
                    row.Invalid.Add(false);

                    // One box per assessment column, plus the Total and PS of the class record.
                    var cells = new CategoryCells();
                    double total = 0, gradedMax = 0;
                    var any = false;

                    foreach (var assessment in OrderedAssessments(category))
                    {
                        var own = entry == null || entry.AssessmentScores == null ? null : entry.AssessmentScores.FirstOrDefault(a => a.AssessmentId == assessment.Id);
                        var typed = raw == null ? null : raw(CellFieldName(student.Id, assessment.Id));

                        if (typed != null)
                        {
                            double? ignored;
                            cells.Inputs.Add(typed);
                            cells.Invalid.Add(!TryParseRawScore(typed, maxes[assessment.Id], out ignored));
                        }
                        else
                        {
                            cells.Inputs.Add(own != null && own.Score.HasValue ? own.Score.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty);
                            cells.Invalid.Add(false);
                        }

                        if (own != null && own.Score.HasValue)
                        {
                            any = true;
                            total += own.Score.Value;
                            gradedMax += assessment.MaxScore;
                        }
                    }

                    cells.GradedMax = gradedMax;
                    cells.Total = any ? (double?)Math.Round(total, 2) : null;
                    cells.Percent = any && gradedMax > 0 ? (double?)(total / gradedMax * 100.0) : null;
                    row.CategoryCells.Add(cells);
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