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
    /// Keeps and reads the progress history backed by MongoDB Atlas. One snapshot is kept per student per
    /// faculty save, and only when something changed for that student. The snapshot says what changed.
    /// </summary>
    public static class HistoryService
    {
        /// <summary>Most snapshots kept for one student in one course. The oldest are dropped first.</summary>
        public const int MaxPerStudent = 100;

        public const string ReasonScores = "Scores updated";
        public const string ReasonCategories = "Categories changed";

        /// <summary>How many changed scores the short summary names before it says "and N more".</summary>
        public const int MaxNamed = 3;

        private const double Same = 0.000000001;

        // ---------- record ----------

        /// <summary>
        /// Faculty saved scores: keeps one snapshot for the student when at least one score changed.
        /// The numbers are worked out from the saved scores (zero-based Earned Grade).
        /// </summary>
        public static void RecordChange(Course course, string studentId, IEnumerable<ScoreEntry> savedScores,
            DateTime whenUtc, string reason, IList<ScoreChange> changes)
        {
            if (course == null || string.IsNullOrEmpty(studentId)) return;
            if (changes == null || changes.Count == 0) return;

            var result = GradeCalculator.Calculate(course.Categories, savedScores ?? new List<ScoreEntry>());
            var snapshot = Make(course.Id, studentId, whenUtc, result);
            snapshot.Reason = string.IsNullOrEmpty(reason) ? ReasonScores : reason;
            snapshot.Changes = changes.ToList();

            Insert(snapshot);
        }

        /// <summary>
        /// Faculty changed the grading categories or weights: keeps a snapshot only when this student's
        /// Earned Grade really moved. The course must already hold the new categories.
        /// </summary>
        public static void RecordCategoriesChanged(Course course, string studentId, IEnumerable<ScoreEntry> savedScores, DateTime whenUtc)
        {
            if (course == null || string.IsNullOrEmpty(studentId)) return;

            var result = GradeCalculator.Calculate(course.Categories, savedScores ?? new List<ScoreEntry>());

            var db = new MongoDbContext();
            var last = db.Snapshots
                .Find(s => s.CourseId == course.Id && s.StudentId == studentId)
                .SortByDescending(s => s.TakenAt)
                .FirstOrDefault();

            // Nothing graded and nothing recorded before: there is no history to keep yet.
            if (last == null && !result.HasGrades) return;

            if (last != null && Math.Abs(last.Earned - result.Earned) <= Same) return;

            var snapshot = Make(course.Id, studentId, whenUtc, result);
            snapshot.Reason = ReasonCategories;

            Insert(snapshot);
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

        private static void Insert(ScoreSnapshot snapshot)
        {
            var db = new MongoDbContext();
            db.Snapshots.InsertOne(snapshot);

            // Keep only the newest MaxPerStudent for this student in this course.
            var courseId = snapshot.CourseId;
            var studentId = snapshot.StudentId;

            var tooOld = db.Snapshots
                .Find(s => s.CourseId == courseId && s.StudentId == studentId)
                .SortByDescending(s => s.TakenAt)
                .Skip(MaxPerStudent)
                .Project(s => s.Id)
                .ToList();

            if (tooOld.Count > 0)
            {
                db.Snapshots.DeleteMany(s => tooOld.Contains(s.Id));
            }
        }

        // ---------- read ----------

        /// <summary>The student's history in this course, oldest first.</summary>
        public static List<HistoryPoint> GetHistory(string courseId, string studentId)
        {
            var db = new MongoDbContext();
            var points = new List<HistoryPoint>();
            double? previous = null;

            var snapshots = db.Snapshots
                .Find(s => s.CourseId == courseId && s.StudentId == studentId)
                .SortBy(s => s.TakenAt)
                .ToList();

            foreach (var s in snapshots)
            {
                var utc = ToUtc(s.TakenAt);
                var hasGrades = s.GradedWeight > GradeCalculator.WeightTolerance;

                var point = new HistoryPoint
                {
                    TakenAt = utc,
                    Iso = utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    Label = utc.ToLocalTime().ToString("MM/dd/yyyy h:mm tt", CultureInfo.InvariantCulture),
                    Earned = s.Earned,
                    HasGrades = hasGrades,
                    Equivalent = hasGrades ? GradeCalculator.EquivalentGrade(s.Earned) : null
                };

                if (previous.HasValue)
                {
                    point.Change = s.Earned - previous.Value;
                }

                point.Summary = Describe(s, MaxNamed);
                point.Details = Describe(s, int.MaxValue);

                previous = s.Earned;
                points.Add(point);
            }

            return points;
        }

        /// <summary>
        /// "Quizzes 80 -> 85, Exam - -> 90" (an empty score is written as "-").
        /// Old snapshots have no changes: they read "Scores updated". Category changes read "Categories changed".
        /// More than maxNamed changes end with "and N more".
        /// </summary>
        public static string Describe(ScoreSnapshot snapshot, int maxNamed)
        {
            if (snapshot.Changes == null || snapshot.Changes.Count == 0)
            {
                return string.IsNullOrEmpty(snapshot.Reason) ? ReasonScores : snapshot.Reason;
            }

            var parts = snapshot.Changes
                .Take(maxNamed)
                .Select(c => c.Category + " " + ScoreText(c.OldScore) + " -> " + ScoreText(c.NewScore))
                .ToList();

            var text = string.Join(", ", parts);
            var more = snapshot.Changes.Count - parts.Count;
            if (more > 0) text += " and " + more + " more";

            return text;
        }

        private static string ScoreText(double? score)
        {
            return score.HasValue ? score.Value.ToString("0.##", CultureInfo.InvariantCulture) : "-";
        }

        /// <summary>Snapshots come back from MongoDB as UTC; a value with no kind is treated as UTC too.</summary>
        private static DateTime ToUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();
        }
    }
}
