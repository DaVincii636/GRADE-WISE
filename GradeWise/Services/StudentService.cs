using System;
using System.Collections.Generic;
using System.Linq;
using GradeWise.Data;
using GradeWise.Models;

namespace GradeWise.Services
{
    /// <summary>
    /// Builds the student dashboard. Reads only through SampleData and only returns the signed-in
    /// student's own scores plus class averages. Class numbers are shown only when at least
    /// MinClassSize students are behind them, so one classmate's score can never be worked out.
    /// </summary>
    public static class StudentService
    {
        /// <summary>A class average needs at least this many students with scores.</summary>
        public const int MinClassSize = 3;

        /// <summary>
        /// Dashboard for one student. <paramref name="requestedCourseId"/> picks the course; if it is empty,
        /// unknown or not one of the student's courses, the first enrolled course is shown instead.
        /// </summary>
        public static StudentDashboardViewModel GetDashboard(string studentId, string requestedCourseId)
        {
            var model = new StudentDashboardViewModel { MinClassSize = MinClassSize };

            lock (SampleData.Sync)
            {
                var me = SampleData.Users.FirstOrDefault(u => u.Id == studentId);
                model.StudentName = me == null ? null : me.FullName;

                var mine = SampleData.Courses
                    .Where(c => c.StudentIds.Contains(studentId))
                    .OrderBy(c => c.Code)
                    .ThenBy(c => c.Name)
                    .ToList();

                model.Courses = mine.Select(c => new StudentCourseOption { Id = c.Id, Code = c.Code, Name = c.Name }).ToList();
                if (mine.Count == 0) return model;

                var course = mine.FirstOrDefault(c => c.Id == requestedCourseId) ?? mine[0];

                foreach (var c in mine)
                {
                    model.Notifications.AddRange(NotificationsFor(c, studentId));
                }

                FillCourse(model, course, studentId);
            }

            return model;
        }

        // ---------- helpers (call only while holding SampleData.Sync) ----------

        private static void FillCourse(StudentDashboardViewModel model, Course course, string studentId)
        {
            var mine = ScoresOf(course, studentId);
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

            // Everyone's scores in this course, used only to make averages.
            var everyone = course.StudentIds.Select(id => ScoresOf(course, id)).ToList();

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

        private static IEnumerable<string> NotificationsFor(Course course, string studentId)
        {
            var label = course.Code + ": ";

            if (course.Categories.Count == 0)
            {
                yield return label + "your instructor has not set the grading categories yet.";
                yield break;
            }

            var scores = ScoresOf(course, studentId);
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

        private static List<ScoreEntry> ScoresOf(Course course, string studentId)
        {
            var record = SampleData.Grades.FirstOrDefault(g => g.CourseId == course.Id && g.StudentId == studentId);
            return record == null ? new List<ScoreEntry>() : record.Scores;
        }

        private static double? FindScore(IEnumerable<ScoreEntry> scores, string category)
        {
            var entry = scores.FirstOrDefault(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase));
            return entry == null ? null : entry.Score;
        }
    }
}
