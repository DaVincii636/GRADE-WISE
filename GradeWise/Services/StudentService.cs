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

            foreach (var category in GradeCalculator.ScopeCategories(course.Categories, scope))
            {
                var own = FindScore(scores, category.Name);
                var weight = GradeCalculator.ScopeWeight(category, scope);

                if (own.HasValue)
                {
                    model.Graded.Add(new BreakdownRow
                    {
                        Category = category.Name,
                        Period = GradePeriods.Normalize(category.Period),
                        Weight = weight,
                        Score = own,
                        WeightedPoints = weight * own.Value / 100.0
                    });
                }
                else
                {
                    model.Ungraded.Add(new GradeCategory { Name = category.Name, Period = GradePeriods.Normalize(category.Period), Weight = weight });
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
            var hierarchyChanged = false;
            foreach (var category in course.Categories)
            {
                if (string.IsNullOrWhiteSpace(category.Group) || (string.Equals(category.Group, "Class Standing", StringComparison.OrdinalIgnoreCase) && category.Name != null && category.Name.IndexOf("exam", StringComparison.OrdinalIgnoreCase) >= 0))
                { category.Group = category.Name != null && category.Name.IndexOf("exam", StringComparison.OrdinalIgnoreCase) >= 0 ? "Exam" : "Class Standing"; hierarchyChanged = true; }
                if (category.Assessments == null) { category.Assessments = new List<GradeAssessment>(); hierarchyChanged = true; }
                if (category.Assessments.Count == 0)
                { category.Assessments.Add(new GradeAssessment { Id = "a-" + Guid.NewGuid().ToString("N").Substring(0, 10), Name = "Assessment 1", Order = 1 }); hierarchyChanged = true; }
            }
            if (hierarchyChanged) db.Courses.UpdateOne(c => c.Id == course.Id, Builders<Course>.Update.Set(c => c.Categories, course.Categories));
            model.CategoriesForDisplay = course.Categories.Select(c => new GradeCategory { Name = c.Name, Group = c.Group }).ToList();
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

            var migratedRecords = false;
            foreach (var record in records)
            {
                foreach (var category in course.Categories)
                {
                    var entry = record.Scores.FirstOrDefault(x => string.Equals(x.Category, category.Name, StringComparison.OrdinalIgnoreCase));
                    if (entry != null && entry.Score.HasValue && (entry.AssessmentScores == null || entry.AssessmentScores.Count == 0))
                    {
                        entry.AssessmentScores = new List<AssessmentScoreEntry> { new AssessmentScoreEntry { AssessmentId = category.Assessments[0].Id, Score = entry.Score } };
                        migratedRecords = true;
                    }
                }
                if (migratedRecords) db.Grades.UpdateOne(g => g.Id == record.Id, Builders<GradeRecord>.Update.Set(g => g.Scores, record.Scores));
            }
            if (migratedRecords)
            {
                byStudent.Clear();
                foreach (var record in records) if (record.StudentId != null) byStudent[record.StudentId] = record.Scores ?? new List<ScoreEntry>();
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

            model.History = historyPoints;
            model.HistoryHidden = hiddenCount;

            var everyone = course.StudentIds.Select(id => ScoresFrom(byStudent, id)).ToList();
            var earned = everyone.Select(s => GradeCalculator.CalculateScope(course.Categories, s, scope).EarnedOrNull).ToList();
            var summary = GradeCalculator.Summarize(earned);
            model.ClassGraded = summary.Graded;
            model.ClassAverage = summary.Graded >= MinClassSize ? summary.Average : null;

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
                    Assessments = (category.Assessments ?? new List<GradeAssessment>()).OrderBy(a => a.Order).Select(a => {
                        var scoreEntry = mine.FirstOrDefault(x => string.Equals(x.Category, category.Name, StringComparison.OrdinalIgnoreCase));
                        var assessmentScore = scoreEntry == null || scoreEntry.AssessmentScores == null ? null : scoreEntry.AssessmentScores.FirstOrDefault(x => x.AssessmentId == a.Id);
                        return new AssessmentBreakdownItem { Name = a.Name, Score = assessmentScore == null ? (double?)null : assessmentScore.Score };
                    }).ToList(),
                    Period = GradePeriods.Normalize(category.Period),
                    Weight = weight,
                    Score = own,
                    WeightedPoints = own.HasValue ? (double?)(weight * own.Value / 100.0) : null,
                    ClassAverage = classScores.Count >= MinClassSize ? (double?)classScores.Average() : null
                });
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
            return entry == null ? null : (entry.AssessmentScores != null && entry.AssessmentScores.Any(x => x.Score.HasValue) ? (double?)entry.AssessmentScores.Where(x => x.Score.HasValue).Average(x => x.Score.Value) : entry.Score);
        }
    }
}