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
    /// Builds the student dashboard backed by MongoDB Atlas.
    /// </summary>
    public static class StudentService
    {
        /// <summary>A class average needs at least this many students with scores.</summary>
        public const int MinClassSize = 3;

        /// <summary>
        /// Dashboard for one student.
        /// </summary>
        public static StudentDashboardViewModel GetDashboard(string studentId, string requestedCourseId, User student = null)
        {
            var model = new StudentDashboardViewModel { MinClassSize = MinClassSize };
            var db = new MongoDbContext();

            // The controller usually has the account already; only look it up when it was not passed in.
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

            model.Courses = CourseOptions(db, mine);
            if (mine.Count == 0) return model;

            var course = mine.FirstOrDefault(c => c.Id == requestedCourseId) ?? mine[0];

            FillCourse(db, model, course, studentId);

            return model;
        }

        /// <summary>
        /// Data for the What-If simulator backed by MongoDB Atlas.
        /// </summary>
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

        /// <summary>
        /// Course picker entries. The instructors' names are loaded with one query for all the courses.
        /// </summary>
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
            // One query for every enrolled student's scores (instead of one round trip per student).
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

            // Everything below follows the scope the instructor chose for this course.
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
            model.History = HistoryService.GetHistory(course.Id, studentId);

            // One list of scores per enrolled student (empty when a student has no record yet), same as before.
            var everyone = course.StudentIds.Select(id => ScoresFrom(byStudent, id)).ToList();

            // The class benchmark is the average of every student's Earned Grade (null = nothing graded yet).
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
            return entry == null ? null : entry.Score;
        }
    }
}