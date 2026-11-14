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
    /// Keeps and reads the progress history backed by MongoDB Atlas.
    /// </summary>
    public static class HistoryService
    {
        public const int MaxPerStudent = 100;

        public const string ReasonScores = "Scores updated";
        public const string ReasonCategories = "Categories changed";

        public const int MaxNamed = 3;

        private const double Same = 0.000000001;

        public static void RecordChange(Course course, string studentId, IEnumerable<ScoreEntry> savedScores,
            DateTime whenUtc, string reason, IList<ScoreChange> changes)
        {
            if (course == null || string.IsNullOrEmpty(studentId)) return;
            if (changes == null || changes.Count == 0) return;

            var scoreList = (savedScores ?? new List<ScoreEntry>()).ToList();
            var result = GradeCalculator.Calculate(course.Categories, scoreList);
            var snapshot = Make(course.Id, studentId, whenUtc, result, course.Categories, scoreList);
            snapshot.Reason = string.IsNullOrEmpty(reason) ? ReasonScores : reason;
            snapshot.Changes = changes.ToList();

            Insert(snapshot);
        }

        public static void RecordCategoriesChanged(Course course, string studentId, IEnumerable<ScoreEntry> savedScores, DateTime whenUtc)
        {
            if (course == null || string.IsNullOrEmpty(studentId)) return;

            var scoreList = (savedScores ?? new List<ScoreEntry>()).ToList();
            var result = GradeCalculator.Calculate(course.Categories, scoreList);
            var midterm = GradeCalculator.CalculatePeriod(course.Categories, scoreList, GradePeriods.Midterm);
            var finalTerm = GradeCalculator.CalculatePeriod(course.Categories, scoreList, GradePeriods.FinalTerm);

            var db = new MongoDbContext();
            var last = db.Snapshots
                .Find(s => s.CourseId == course.Id && s.StudentId == studentId)
                .SortByDescending(s => s.TakenAt)
                .FirstOrDefault();

            if (last == null && !result.HasGrades) return;

            if (last != null
                && last.MidtermEarned.HasValue && last.FinalTermEarned.HasValue
                && Math.Abs(last.Earned - result.Earned) <= Same
                && Math.Abs(last.MidtermEarned.Value - midterm.Earned) <= Same
                && Math.Abs(last.FinalTermEarned.Value - finalTerm.Earned) <= Same) return;

            var snapshot = Make(course.Id, studentId, whenUtc, result, course.Categories, scoreList);
            snapshot.Reason = ReasonCategories;

            Insert(snapshot);
        }

        public static ScoreSnapshot Make(string courseId, string studentId, DateTime when, GradeResult result,
            IEnumerable<GradeCategory> categories = null, IEnumerable<ScoreEntry> scores = null)
        {
            var snapshot = new ScoreSnapshot
            {
                Id = "s-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                CourseId = courseId,
                StudentId = studentId,
                TakenAt = when,
                Average = result.Average,
                Earned = result.Earned,
                GradedWeight = result.GradedWeight
            };

            if (categories != null && scores != null)
            {
                var scoreList = scores.ToList();
                var midterm = GradeCalculator.CalculatePeriod(categories, scoreList, GradePeriods.Midterm);
                var finalTerm = GradeCalculator.CalculatePeriod(categories, scoreList, GradePeriods.FinalTerm);
                snapshot.MidtermEarned = midterm.Earned;
                snapshot.MidtermGradedWeight = midterm.GradedWeight;
                snapshot.FinalTermEarned = finalTerm.Earned;
                snapshot.FinalTermGradedWeight = finalTerm.GradedWeight;
            }

            return snapshot;
        }

        private static void Insert(ScoreSnapshot snapshot)
        {
            var db = new MongoDbContext();
            db.Snapshots.InsertOne(snapshot);

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

        public static List<HistoryPoint> GetHistory(Course course, string studentId, string scope, out int hidden)
        {
            hidden = 0;
            var points = new List<HistoryPoint>();
            if (course == null || string.IsNullOrEmpty(studentId)) return points;

            scope = GradeScopes.Normalize(scope);
            var period = GradeScopes.PeriodOf(scope);
            var courseId = course.Id;

            var periodOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in course.Categories ?? new List<GradeCategory>())
            {
                if (c.Name != null) periodOf[c.Name] = GradePeriods.Normalize(c.Period);
            }

            var db = new MongoDbContext();
            var snapshots = db.Snapshots
                .Find(s => s.CourseId == courseId && s.StudentId == studentId)
                .SortBy(s => s.TakenAt)
                .ToList();

            double? previous = null;

            foreach (var s in snapshots)
            {
                double earned;
                double gradedWeight;

                if (period == null)
                {
                    earned = s.Earned;
                    gradedWeight = s.GradedWeight;
                }
                else if (period == GradePeriods.Midterm)
                {
                    if (!s.MidtermEarned.HasValue) { hidden++; continue; }
                    earned = s.MidtermEarned.Value;
                    gradedWeight = s.MidtermGradedWeight ?? 0;
                }
                else
                {
                    if (!s.FinalTermEarned.HasValue) { hidden++; continue; }
                    earned = s.FinalTermEarned.Value;
                    gradedWeight = s.FinalTermGradedWeight ?? 0;
                }

                var hasGrades = gradedWeight > GradeCalculator.WeightTolerance;

                if (period != null)
                {
                    if (!previous.HasValue && !hasGrades) continue;
                    if (previous.HasValue && Math.Abs(earned - previous.Value) <= Same) continue;
                }

                // FIX: Force UTC safely so JS Date() doesn't misinterpret timezones.
                var utc = ToUtc(s.TakenAt);
                var point = new HistoryPoint
                {
                    TakenAt = utc,
                    Iso = utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    Label = utc.ToLocalTime().ToString("MM/dd/yyyy h:mm tt", CultureInfo.InvariantCulture),
                    Earned = earned,
                    HasGrades = hasGrades,
                    Equivalent = hasGrades ? GradeCalculator.EquivalentGrade(earned) : null
                };

                if (previous.HasValue)
                {
                    point.Change = earned - previous.Value;
                }

                var shown = s;
                if (period != null && s.Changes != null && s.Changes.Count > 0)
                {
                    shown = new ScoreSnapshot
                    {
                        Reason = s.Reason,
                        Changes = s.Changes
                            .Where(c =>
                            {
                                string p;
                                return c.Category != null && periodOf.TryGetValue(c.Category, out p) && p == period;
                            })
                            .ToList()
                    };
                }

                point.Summary = Describe(shown, MaxNamed);
                point.Details = Describe(shown, int.MaxValue);

                previous = earned;
                points.Add(point);
            }

            return points;
        }

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

        private static DateTime ToUtc(DateTime value)
        {
            // FIX: Ensure MongoDB dates are properly recognized as UTC.
            if (value.Kind == DateTimeKind.Utc) return value;
            if (value.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return value.ToUniversalTime();
        }
    }
}