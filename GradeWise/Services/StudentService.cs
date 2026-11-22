using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GradeWise.Data;
using GradeWise.Models;
using MongoDB.Driver;

namespace GradeWise.Services
{
    public static class StudentService
    {
        public const int MinClassSize = 3;

        public static StudentDashboardViewModel GetDashboard(string studentId, string requestedCourseId, User student = null)
        {
            var model = new StudentDashboardViewModel { MinClassSize = MinClassSize };
            var db = new MongoDbContext();

            var me = student != null && student.Id == studentId
                ? student
                : db.Users.Find(u => u.Id == studentId).FirstOrDefault();
            model.StudentName = me == null ? null : me.FullName;
            model.StudentNo = me == null ? null : me.StudentNo;

            var mine = db.Courses
                .Find(c => c.StudentIds.Contains(studentId))
                .SortBy(c => c.Code)
                .ThenBy(c => c.Name)
                .ToList();

            if (mine.Count == 0)
            {
                model.Courses = CourseOptions(db, mine);
                return model;
            }

            // FIX: Removed Task.Run. It's an anti-pattern in ASP.NET MVC.
            var course = mine.FirstOrDefault(c => c.Id == requestedCourseId) ?? mine[0];

            FillCourse(db, model, course, studentId);

            model.Courses = CourseOptions(db, mine);
            return model;
        }

        public static WhatIfViewModel GetWhatIf(string studentId, string requestedCourseId)
        {
            var model = new WhatIfViewModel();
            var db = new MongoDbContext();

            var mine = db.Courses
                .Find(c => c.StudentIds.Contains(studentId))
                .SortBy(c => c.Code)
                .ThenBy(c => c.Name)
                .ToList();

            model.Courses = CourseOptions(db, mine);
            if (mine.Count == 0) return model;

            var course = mine.FirstOrDefault(c => c.Id == requestedCourseId) ?? mine[0];
            var scope = GradeScopes.Normalize(course.ReflectedScope);
            var scores = ScoresOf(db, course, studentId);
            var result = GradeCalculator.CalculateScope(course.Categories, scores, scope);

            model.HasCourse = true;
            model.SelectedId = course.Id;
            model.Code = course.Code;
            model.Name = course.Name;
            model.Term = course.Term;
            model.Scope = scope;
            model.ScopeLabel = GradeScopes.Label(scope);
            model.ScopeHasCategories = GradeCalculator.ScopeCategories(course.Categories, scope).Any();
            model.HasCategories = model.ScopeHasCategories;
            model.WeightTotal = GradeCalculator.ScopeTotal(course.Categories, scope);
            model.WeightsOk = GradeCalculator.ScopeReady(course.Categories, scope);
            model.Average = result.Average;
            model.Equivalent = GradeCalculator.EquivalentFromEarned(result);
            model.Earned = result.Earned;
            model.RemainingWeight = result.RemainingWeight;
            model.NothingLeft = model.WeightsOk && result.RemainingWeight <= GradeCalculator.WeightTolerance;

            model.DefaultScore = result.Average.HasValue
                ? Math.Max(0, Math.Min(100, Math.Round(result.Average.Value)))
                : 75;

            var perTerm = new Dictionary<string, int>();
            foreach (var category in GradeCalculator.ScopeCategories(course.Categories, scope))
            {
                var own = FindScore(scores, category.Name);
                var weight = GradeCalculator.ScopeWeight(category, scope);
                var period = GradePeriods.Normalize(category.Period);
                var group = GradeGroups.Normalize(category.Group);

                int position;
                perTerm.TryGetValue(period, out position);
                perTerm[period] = position + 1;
                model.Positions[period + "|" + category.Name] = position;

                if (own.HasValue)
                {
                    model.Graded.Add(new BreakdownRow
                    {
                        Category = category.Name,
                        Period = period,
                        Group = group,
                        Weight = weight,
                        Score = own,
                        WeightedPoints = weight * own.Value / 100.0
                    });
                }
                else
                {
                    model.Ungraded.Add(new GradeCategory { Name = category.Name, Period = period, Group = group, Weight = weight });
                }
            }

            model.Scale = GradeCalculator.Scale();
            return model;
        }

        private static List<StudentCourseOption> CourseOptions(MongoDbContext db, List<Course> courses)
        {
            var facultyIds = courses
                .Select(c => c.FacultyId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();

            var names = new Dictionary<string, string>();
            if (facultyIds.Count > 0)
            {
                foreach (var teacher in db.Users.Find(u => facultyIds.Contains(u.Id)).ToList())
                {
                    names[teacher.Id] = teacher.FullName;
                }
            }

            return courses.Select(c => new StudentCourseOption
            {
                Id = c.Id,
                Code = c.Code,
                Name = c.Name,
                FacultyName = FacultyNameOf(names, c.FacultyId)
            }).ToList();
        }

        private static string FacultyNameOf(Dictionary<string, string> names, string facultyId)
        {
            string name;
            if (!string.IsNullOrEmpty(facultyId) && names.TryGetValue(facultyId, out name) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return "Unassigned";
        }

        private static void FillCourse(MongoDbContext db, StudentDashboardViewModel model, Course course, string studentId)
        {
            var historyScope = GradeScopes.Normalize(course.ReflectedScope);
            int hiddenCount;

            // FIX: Straight execution instead of background threads
            var historyPoints = HistoryService.GetHistory(course, studentId, historyScope, out hiddenCount);

            var courseId = course.Id;
            var enrolled = course.StudentIds;
            var records = db.Grades.Find(g => g.CourseId == courseId && enrolled.Contains(g.StudentId)).ToList();
            var byStudent = new Dictionary<string, List<ScoreEntry>>();
            foreach (var record in records)
            {
                if (record.StudentId != null && !byStudent.ContainsKey(record.StudentId))
                {
                    byStudent[record.StudentId] = record.Scores ?? new List<ScoreEntry>();
                }
            }

            var scope = GradeScopes.Normalize(course.ReflectedScope);
            var mine = ScoresFrom(byStudent, studentId);
            var result = GradeCalculator.CalculateScope(course.Categories, mine, scope);

            model.HasCourse = true;
            model.SelectedId = course.Id;
            model.Code = course.Code;
            model.Name = course.Name;
            model.Term = course.Term;
            model.Scope = scope;
            model.ScopeLabel = GradeScopes.Label(scope);
            model.ScopeHasCategories = GradeCalculator.ScopeCategories(course.Categories, scope).Any();
            model.HasCategories = model.ScopeHasCategories;
            model.WeightTotal = GradeCalculator.ScopeTotal(course.Categories, scope);
            model.WeightsOk = GradeCalculator.ScopeReady(course.Categories, scope);
            model.Average = result.Average;
            model.Equivalent = GradeCalculator.EquivalentFromEarned(result);
            model.Earned = result.Earned;
            model.EarnedGrade = result.EarnedOrNull;
            model.RemainingWeight = result.RemainingWeight;
            model.Incomplete = scope == GradeScopes.FinalGrade && GradeCalculator.IsIncomplete(course.Categories, mine);

            model.History = historyPoints;
            model.HistoryHidden = hiddenCount;

            var everyone = course.StudentIds.Select(id => ScoresFrom(byStudent, id)).ToList();
            var earned = everyone.Select(s => GradeCalculator.CalculateScope(course.Categories, s, scope).EarnedOrNull).ToList();
            var summary = GradeCalculator.Summarize(earned);
            model.ClassGraded = summary.Graded;
            model.ClassAverage = summary.Graded >= MinClassSize ? summary.Average : null;

            FillTerms(model, course, mine, everyone, scope);

            foreach (var category in GradeCalculator.ScopeCategories(course.Categories, scope))
            {
                var own = FindScore(mine, category.Name);

                var classScores = everyone
                    .Select(s => FindScore(s, category.Name))
                    .Where(v => v.HasValue)
                    .Select(v => v.Value)
                    .ToList();

                var weight = GradeCalculator.ScopeWeight(category, scope);
                model.Rows.Add(new BreakdownRow
                {
                    Category = category.Name,
                    Period = GradePeriods.Normalize(category.Period),
                    Weight = weight,
                    Score = own,
                    WeightedPoints = own.HasValue ? (double?)(weight * own.Value / 100.0) : null,
                    ClassAverage = classScores.Count >= MinClassSize ? (double?)classScores.Average() : null
                });
            }
        }

        /// <summary>
        /// Builds the term tables the same way the faculty score table is built: Class Standing then Exam,
        /// every assessment box, Total, PS, and the Earned grade and Eq. of each term.
        /// </summary>
        private static void FillTerms(StudentDashboardViewModel model, Course course, List<ScoreEntry> mine, List<List<ScoreEntry>> everyone, string scope)
        {
            model.ActiveTab = scope;
            model.ShowFinalGrade = scope == GradeScopes.FinalGrade;

            var midterm = GradeCalculator.CalculatePeriod(course.Categories, mine, GradePeriods.Midterm);
            var finalTerm = GradeCalculator.CalculatePeriod(course.Categories, mine, GradePeriods.FinalTerm);
            model.MidtermEarned = midterm.EarnedOrNull;
            model.MidtermEquivalent = GradeCalculator.EquivalentFromEarned(midterm);
            model.FinalTermEarned = finalTerm.EarnedOrNull;
            model.FinalTermEquivalent = GradeCalculator.EquivalentFromEarned(finalTerm);

            foreach (var period in GradePeriods.All)
            {
                // Only what the instructor released: one term, or both for the Final Grade.
                if (!GradeScopes.Includes(scope, new GradeCategory { Period = period })) continue;

                var result = period == GradePeriods.Midterm ? midterm : finalTerm;
                var table = new StudentTermTable
                {
                    Period = period,
                    Key = period == GradePeriods.Midterm ? GradeScopes.Midterm : GradeScopes.FinalTerm,
                    WeightTotal = GradeCalculator.PeriodTotal(course.Categories, period),
                    Earned = result.EarnedOrNull,
                    Equivalent = GradeCalculator.EquivalentFromEarned(result)
                };

                var categories = course.Categories
                    .Where(c => GradePeriods.Normalize(c.Period) == period)
                    .OrderBy(c => GradeGroups.Normalize(c.Group) == GradeGroups.Exam ? 1 : 0)
                    .ToList();

                foreach (var category in categories)
                {
                    var entry = mine.FirstOrDefault(s => string.Equals(s.Category, category.Name, StringComparison.OrdinalIgnoreCase));
                    var cell = new StudentCategoryCell
                    {
                        Name = category.Name,
                        Group = GradeGroups.Normalize(category.Group),
                        Weight = category.Weight
                    };

                    double total = 0, gradedMax = 0;
                    var any = false;

                    foreach (var assessment in CourseService.OrderedAssessments(category))
                    {
                        var own = entry == null || entry.AssessmentScores == null
                            ? null
                            : entry.AssessmentScores.FirstOrDefault(a => a.AssessmentId == assessment.Id);

                        cell.Assessments.Add(new StudentAssessmentCell
                        {
                            Name = assessment.Name,
                            MaxScore = assessment.MaxScore,
                            Score = own != null ? own.Score : null
                        });
                        cell.HighestTotal += assessment.MaxScore;

                        if (own != null && own.Score.HasValue)
                        {
                            any = true;
                            total += own.Score.Value;
                            gradedMax += assessment.MaxScore;
                        }
                    }

                    cell.GradedMax = gradedMax;
                    cell.Total = any ? (double?)Math.Round(total, 2) : null;
                    cell.Percent = any && gradedMax > 0 ? (double?)(total / gradedMax * 100.0) : null;
                    table.Categories.Add(cell);

                    // Anonymous class average of the category's PS, only when enough students have a score.
                    var classScores = everyone
                        .Select(s => FindScore(s, category.Name))
                        .Where(v => v.HasValue)
                        .Select(v => v.Value)
                        .ToList();
                    if (classScores.Count >= MinClassSize)
                    {
                        model.CategoryAverages.Add(new CategoryAverage { Name = category.Name, Period = period, Average = classScores.Average() });
                    }
                }

                model.Terms.Add(table);
            }
        }

        private static List<ScoreEntry> ScoresFrom(Dictionary<string, List<ScoreEntry>> byStudent, string studentId)
        {
            List<ScoreEntry> scores;
            return studentId != null && byStudent.TryGetValue(studentId, out scores) ? scores : new List<ScoreEntry>();
        }

        private static List<ScoreEntry> ScoresOf(MongoDbContext db, Course course, string studentId)
        {
            var record = db.Grades.Find(g => g.CourseId == course.Id && g.StudentId == studentId).FirstOrDefault();
            return record == null ? new List<ScoreEntry>() : record.Scores;
        }

        private static double? FindScore(IEnumerable<ScoreEntry> scores, string category)
        {
            var entry = scores.FirstOrDefault(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase));
            return entry == null ? null : entry.Score;
        }
    }
}