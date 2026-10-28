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

            model.Courses = mine.Select(c => new StudentCourseOption { Id = c.Id, Code = c.Code, Name = c.Name }).ToList();
            if (mine.Count == 0) return model;

            var course = mine.FirstOrDefault(c => c.Id == requestedCourseId) ?? mine[0];

            foreach (var c in mine)
            {
                model.Notifications.AddRange(NotificationsFor(db, c, studentId));
            }

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

            model.Courses = mine.Select(c => new StudentCourseOption { Id = c.Id, Code = c.Code, Name = c.Name }).ToList();
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
            model.Equivalent = GradeCalculator.EquivalentGrade(result.Average);
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
                    model.Graded.Add(new BreakdownRow
                    {
                        Category = category.Name,
                        Weight = category.Weight,
                        Score = own,
                        WeightedPoints = category.Weight * own.Value / 100.0
                    });
                }
                else
                {
                    model.Ungraded.Add(new GradeCategory { Name = category.Name, Weight = category.Weight });
                }
            }

            model.Scale = GradeCalculator.Scale();
            model.Checks = BuildChecks();
            return model;
        }

        private static List<WhatIfCheck> BuildChecks()
        {
            return new List<WhatIfCheck>
            {
                FinalCheck("55 points earned, 80% on the remaining 45%", 55, 45, 80),
                FinalCheck("40 points earned, 50% on the remaining 60%", 40, 60, 50),
                TargetCheck("Target 85% with 55 earned and 45% left", 85, 55, 45),
                TargetCheck("Target 75% with 59.6 earned and 30% left", 75, 59.6, 30),
                TargetCheck("Target 70% with 72 earned and 20% left (already secured)", 70, 72, 20),
                TargetCheck("Target 100% with 50 earned and 40% left (out of reach)", 100, 50, 40)
            };
        }

        private static WhatIfCheck FinalCheck(string label, double earned, double remaining, double average)
        {
            return new WhatIfCheck
            {
                Label = label,
                Kind = "final",
                Earned = earned,
                Remaining = remaining,
                Input = average,
                Expected = GradeCalculator.ProjectFinal(earned, new[] { remaining }, new[] { average }),
                Status = ""
            };
        }

        private static WhatIfCheck TargetCheck(string label, double target, double earned, double remaining)
        {
            var result = GradeCalculator.CheckTarget(target, earned, remaining);

            return new WhatIfCheck
            {
                Label = label,
                Kind = "required",
                Earned = earned,
                Remaining = remaining,
                Input = target,
                Expected = result.Required ?? 0,
                Status = result.Status.ToString()
            };
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
            model.Equivalent = GradeCalculator.EquivalentGrade(result.Average);
            model.Earned = result.Earned;
            model.RemainingWeight = result.RemainingWeight;
            model.History = HistoryService.GetHistory(course.Id, studentId);

            var everyone = course.StudentIds.Select(id => ScoresOf(db, course, id)).ToList();

            var averages = everyone.Select(s => GradeCalculator.Calculate(course.Categories, s).Average).ToList();
            var summary = GradeCalculator.Summarize(averages);
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

                model.Rows.Add(new BreakdownRow
                {
                    Category = category.Name,
                    Weight = category.Weight,
                    Score = own,
                    WeightedPoints = own.HasValue ? (double?)(category.Weight * own.Value / 100.0) : null,
                    ClassAverage = classScores.Count >= MinClassSize ? (double?)classScores.Average() : null
                });
            }
        }

        private static IEnumerable<string> NotificationsFor(MongoDbContext db, Course course, string studentId)
        {
            var label = course.Code + ": ";

            if (course.Categories.Count == 0)
            {
                yield return label + "your instructor has not set the grading categories yet.";
                yield break;
            }

            var scores = ScoresOf(db, course, studentId);
            var result = GradeCalculator.Calculate(course.Categories, scores);

            var waiting = course.Categories
                .Where(c => !FindScore(scores, c.Name).HasValue)
                .Select(c => c.Name)
                .ToList();

            if (waiting.Count == 0)
            {
                yield return label + "all your categories are graded. Final average " + GradeCalculator.Format(result.Average) + ".";
            }
            else
            {
                yield return label + (waiting.Count == 1 ? "1 category is" : waiting.Count + " categories are")
                    + " still waiting for a score: " + string.Join(", ", waiting) + ".";
            }

            if (!GradeCalculator.IsFullWeight(result.TotalWeight))
            {
                yield return label + "the grading weights add up to " + GradeCalculator.Format(result.TotalWeight)
                    + "%, not 100%, so your numbers may change once your instructor fixes them.";
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