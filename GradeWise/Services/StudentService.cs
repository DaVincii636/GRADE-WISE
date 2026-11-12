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
        public static StudentDashboardViewModel GetDashboard(string studentId, string requestedCourseId)
        {
            var model = new StudentDashboardViewModel { MinClassSize = MinClassSize };
            var db = new MongoDbContext();

            var me = db.Users.Find(u => u.Id == studentId).FirstOrDefault();
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
            var scores = ScoresOf(db, course, studentId);
            var result = GradeCalculator.Calculate(course.Categories, scores);

            model.HasCourse = true;
            model.SelectedId = course.Id;
            model.Code = course.Code;
            model.Name = course.Name;
            model.Term = course.Term;
            model.HasCategories = course.Categories.Count > 0;
            model.WeightTotal = result.TotalWeight;
            model.WeightsOk = model.HasCategories && GradeCalculator.IsFullWeight(result.TotalWeight);
            model.Average = result.Average;
            model.Equivalent = GradeCalculator.EquivalentFromEarned(result);
            model.Earned = result.Earned;
            model.RemainingWeight = result.RemainingWeight;
            model.NothingLeft = model.WeightsOk && result.RemainingWeight <= GradeCalculator.WeightTolerance;

            model.DefaultScore = result.Average.HasValue
                ? Math.Max(0, Math.Min(100, Math.Round(result.Average.Value)))
                : 75;

            foreach (var category in course.Categories)
            {
                var own = FindScore(scores, category.Name);

                if (own.HasValue)
                {
                    var effective = GradeCalculator.EffectiveWeight(category);
                    model.Graded.Add(new BreakdownRow
                    {
                        Category = category.Name,
                        Period = GradePeriods.Normalize(category.Period),
                        Weight = effective,
                        Score = own,
                        WeightedPoints = effective * own.Value / 100.0
                    });
                }
                else
                {
                    model.Ungraded.Add(new GradeCategory { Name = category.Name, Period = GradePeriods.Normalize(category.Period), Weight = GradeCalculator.EffectiveWeight(category) });
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
            var mine = ScoresOf(db, course, studentId);
            var result = GradeCalculator.Calculate(course.Categories, mine);

            model.HasCourse = true;
            model.SelectedId = course.Id;
            model.Code = course.Code;
            model.Name = course.Name;
            model.Term = course.Term;
            model.HasCategories = course.Categories.Count > 0;
            model.WeightTotal = result.TotalWeight;
            model.WeightsOk = model.HasCategories && GradeCalculator.IsFullWeight(result.TotalWeight);
            model.Average = result.Average;
            model.Equivalent = GradeCalculator.EquivalentFromEarned(result);
            model.Earned = result.Earned;
            model.EarnedGrade = result.EarnedOrNull;
            model.RemainingWeight = result.RemainingWeight;
            model.History = HistoryService.GetHistory(course.Id, studentId);

            var everyone = course.StudentIds.Select(id => ScoresOf(db, course, id)).ToList();

            // The class benchmark is the average of every student's Earned Grade (null = nothing graded yet).
            var earned = everyone.Select(s => GradeCalculator.Calculate(course.Categories, s).EarnedOrNull).ToList();
            var summary = GradeCalculator.Summarize(earned);
            model.ClassGraded = summary.Graded;
            model.ClassAverage = summary.Graded >= MinClassSize ? summary.Average : null;

            foreach (var category in course.Categories)
            {
                var own = FindScore(mine, category.Name);

                var classScores = everyone
                    .Select(s => FindScore(s, category.Name))
                    .Where(v => v.HasValue)
                    .Select(v => v.Value)
                    .ToList();

                var effective = GradeCalculator.EffectiveWeight(category);
                model.Rows.Add(new BreakdownRow
                {
                    Category = category.Name,
                    Period = GradePeriods.Normalize(category.Period),
                    Weight = effective,
                    Score = own,
                    WeightedPoints = own.HasValue ? (double?)(effective * own.Value / 100.0) : null,
                    ClassAverage = classScores.Count >= MinClassSize ? (double?)classScores.Average() : null
                });
            }
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