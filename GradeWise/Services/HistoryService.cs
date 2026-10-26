using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GradeWise.Data;
using GradeWise.Models;

namespace GradeWise.Services
{
    /// <summary>
    /// Keeps and reads the progress history (SampleData.Snapshots). Call Record after any change that can
    /// move a student's numbers; it only keeps a snapshot when something really changed, so calling it for
    /// every student of a course is fine. Call these only while holding SampleData.Sync.
    /// </summary>
    public static class HistoryService
    {
        /// <summary>Most snapshots kept for one student in one course. The oldest are dropped first.</summary>
        public const int MaxPerStudent = 100;

        private const double Same = 0.000000001;

        public static void Record(Course course, string studentId, DateTime when)
        {
            var record = SampleData.Grades.FirstOrDefault(g => g.CourseId == course.Id && g.StudentId == studentId);
            var scores = record == null ? new List<ScoreEntry>() : record.Scores;
            var result = GradeCalculator.Calculate(course.Categories, scores);

            var mine = SampleData.Snapshots.Where(s => s.CourseId == course.Id && s.StudentId == studentId).ToList();
            var last = mine.Count == 0 ? null : mine[mine.Count - 1];

            // Nothing graded and nothing recorded before: there is no history to keep yet.
            if (last == null && !result.HasGrades) return;

            if (last != null && SameNumbers(last, result)) return;

            SampleData.Snapshots.Add(Make(course.Id, studentId, when, result));

            if (mine.Count + 1 > MaxPerStudent)
            {
                SampleData.Snapshots.Remove(mine[0]);
            }
        }

        /// <summary>Builds a snapshot from a calculated result.</summary>
        public static ScoreSnapshot Make(string courseId, string studentId, DateTime when, GradeResult result)
        {
            return new ScoreSnapshot
            {
                Id = "s-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                CourseId = courseId,
                StudentId = studentId,
                TakenAt = when,
                Average = result.Average,
                Earned = result.Earned,
                GradedWeight = result.GradedWeight
            };
        }

        /// <summary>The student's history in this course, oldest first.</summary>
        public static List<HistoryPoint> GetHistory(string courseId, string studentId)
        {
            var points = new List<HistoryPoint>();
            double? previous = null;

            var snapshots = SampleData.Snapshots
                .Where(s => s.CourseId == courseId && s.StudentId == studentId)
                .OrderBy(s => s.TakenAt)
                .ToList();

            foreach (var s in snapshots)
            {
                var point = new HistoryPoint
                {
                    TakenAt = s.TakenAt,
                    Label = s.TakenAt.ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture),
                    Average = s.Average,
                    Equivalent = GradeCalculator.EquivalentGrade(s.Average)
                };

                if (s.Average.HasValue && previous.HasValue)
                {
                    point.Change = s.Average.Value - previous.Value;
                }

                previous = s.Average;
                points.Add(point);
            }

            return points;
        }

        private static bool SameNumbers(ScoreSnapshot last, GradeResult result)
        {
            if (last.Average.HasValue != result.Average.HasValue) return false;
            if (last.Average.HasValue && Math.Abs(last.Average.Value - result.Average.Value) > Same) return false;

            return Math.Abs(last.Earned - result.Earned) <= Same
                && Math.Abs(last.GradedWeight - result.GradedWeight) <= Same;
        }
    }
}
