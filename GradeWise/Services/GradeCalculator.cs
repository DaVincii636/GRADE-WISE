using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GradeWise.Models;

namespace GradeWise.Services
{
    /// <summary>Result of the weighted-grade formula for one student in one course.</summary>
    public class GradeResult
    {
        /// <summary>
        /// Points earned so far = sum of (weight x score / 100) over the graded categories. Ungraded work counts as 0,
        /// so for a whole course (effective weights) this is out of 100, and for one period (CalculatePeriod) too.
        /// </summary>
        public double Earned { get; set; }

        /// <summary>Total weight of the categories that already have a score.</summary>
        public double GradedWeight { get; set; }

        /// <summary>Total weight of all categories in the course (should be 100).</summary>
        public double TotalWeight { get; set; }

        /// <summary>Weight of the categories that are not graded yet.</summary>
        public double RemainingWeight
        {
            get { return Math.Max(0, TotalWeight - GradedWeight); }
        }

        public bool HasGrades
        {
            get { return GradedWeight > 0; }
        }

        /// <summary>Earned points, or null when nothing is graded yet (so reports show a dash, not 0 or 5.00).</summary>
        public double? EarnedOrNull
        {
            get { return HasGrades ? (double?)Earned : null; }
        }

        /// <summary>
        /// Weighted average of the graded work, as a percent: earned / graded weight x 100.
        /// Null when nothing is graded yet.
        /// </summary>
        public double? Average
        {
            get { return HasGrades ? (double?)(Earned / GradedWeight * 100) : null; }
        }
    }

    /// <summary>Average, highest and lowest across a class.</summary>
    public class ClassSummary
    {
        /// <summary>How many students have at least one score.</summary>
        public int Graded { get; set; }

        public double? Average { get; set; }

        public double? Highest { get; set; }

        public double? Lowest { get; set; }
    }

    /// <summary>What a target final grade needs, as worked out by <see cref="GradeCalculator.CheckTarget"/>.</summary>
    public enum TargetStatus
    {
        /// <summary>Every category is already graded, so there is nothing left to simulate.</summary>
        NothingLeft,

        /// <summary>The points already earned meet the target; even a 0 on the rest is enough.</summary>
        Secured,

        /// <summary>The target needs an average between 0 and 100 on the remaining work.</summary>
        Reachable,

        /// <summary>The target needs more than 100 on the remaining work, so it cannot be reached.</summary>
        OutOfReach
    }

    public class TargetResult
    {
        public TargetStatus Status { get; set; }

        /// <summary>
        /// Average (percent) needed on the remaining work. Can be 0 or below (secured) or above 100
        /// (out of reach). Null when nothing is left to grade.
        /// </summary>
        public double? Required { get; set; }
    }

    /// <summary>
    /// The one place the grade formula lives, so it can be checked on its own.
    /// The What-If page repeats the same formulas in Scripts/whatif.js and checks itself against this class.
    /// Scores are percentages from 0 to 100; weights are percentages of the final grade.
    /// </summary>
    public static class GradeCalculator
    {
        /// <summary>Tolerance when checking that the weights add up to 100.</summary>
        public const double WeightTolerance = 0.005;

        /// <summary>
        /// The weight a category counts for in the whole course: its weight inside its period x the period's share.
        /// With both periods at 100 and a share of 0.5, the course total is still 100, so the rest of the app is unchanged.
        /// </summary>
        public static double EffectiveWeight(GradeCategory category)
        {
            return category.Weight * GradePeriods.Share;
        }

        /// <summary>Whole-course result. Uses effective weights, so Earned is out of 100.</summary>
        public static GradeResult Calculate(IEnumerable<GradeCategory> categories, IEnumerable<ScoreEntry> scores)
        {
            return Accumulate(categories, scores, EffectiveWeight);
        }

        /// <summary>
        /// Result for one period only ("Midterm" or "Final Term"), using the raw weights, which total 100 per period.
        /// Earned is then 0-100 for that period.
        /// </summary>
        public static GradeResult CalculatePeriod(IEnumerable<GradeCategory> categories, IEnumerable<ScoreEntry> scores, string period)
        {
            var wanted = GradePeriods.Normalize(period);
            var inPeriod = (categories ?? Enumerable.Empty<GradeCategory>())
                .Where(c => GradePeriods.Normalize(c.Period) == wanted);
            return Accumulate(inPeriod, scores, c => c.Weight);
        }

        private static GradeResult Accumulate(IEnumerable<GradeCategory> categories, IEnumerable<ScoreEntry> scores, Func<GradeCategory, double> weightOf)
        {
            var result = new GradeResult();
            var scoreList = scores == null ? new List<ScoreEntry>() : scores.ToList();

            foreach (var category in categories ?? Enumerable.Empty<GradeCategory>())
            {
                var weight = weightOf(category);
                result.TotalWeight += weight;

                var entry = scoreList.FirstOrDefault(s =>
                    string.Equals(s.Category, category.Name, StringComparison.OrdinalIgnoreCase));

                if (entry != null && entry.Score.HasValue)
                {
                    result.GradedWeight += weight;
                    result.Earned += weight * entry.Score.Value / 100.0;
                }
            }

            return result;
        }

        /// <summary>Raw weight total of one period (should be 100 once the period is set up).</summary>
        public static double PeriodTotal(IEnumerable<GradeCategory> categories, string period)
        {
            var wanted = GradePeriods.Normalize(period);
            return (categories ?? Enumerable.Empty<GradeCategory>())
                .Where(c => GradePeriods.Normalize(c.Period) == wanted)
                .Sum(c => c.Weight);
        }

        // ---------- grade scope (Midterm / Final Term / Final Grade) ----------

        /// <summary>Result for a scope: one period with raw weights, or the whole course with effective weights.</summary>
        public static GradeResult CalculateScope(IEnumerable<GradeCategory> categories, IEnumerable<ScoreEntry> scores, string scope)
        {
            var period = GradeScopes.PeriodOf(scope);
            return period == null
                ? Calculate(categories, scores)
                : CalculatePeriod(categories, scores, period);
        }

        /// <summary>The weight a category counts for in a scope (raw inside a period, effective for Final Grade).</summary>
        public static double ScopeWeight(GradeCategory category, string scope)
        {
            return GradeScopes.PeriodOf(scope) == null ? EffectiveWeight(category) : category.Weight;
        }

        /// <summary>The categories that belong to a scope.</summary>
        public static IEnumerable<GradeCategory> ScopeCategories(IEnumerable<GradeCategory> categories, string scope)
        {
            return (categories ?? Enumerable.Empty<GradeCategory>()).Where(c => GradeScopes.Includes(scope, c));
        }

        /// <summary>Weight total of a scope (100 once it is set up).</summary>
        public static double ScopeTotal(IEnumerable<GradeCategory> categories, string scope)
        {
            return ScopeCategories(categories, scope).Sum(c => ScopeWeight(c, scope));
        }

        /// <summary>
        /// True when the scope has categories and they total 100. For Final Grade both periods
        /// must be set and each must total 100.
        /// </summary>
        public static bool ScopeReady(IEnumerable<GradeCategory> categories, string scope)
        {
            var list = (categories ?? Enumerable.Empty<GradeCategory>()).ToList();

            if (GradeScopes.PeriodOf(scope) == null)
            {
                return GradePeriods.All.All(p =>
                    list.Any(c => GradePeriods.Normalize(c.Period) == p) && IsFullWeight(PeriodTotal(list, p)));
            }

            return ScopeCategories(list, scope).Any() && IsFullWeight(ScopeTotal(list, scope));
        }

        /// <summary>Equivalent grade of the points earned (zero-based). Null when nothing is graded, so reports show a dash.</summary>
        public static double? EquivalentFromEarned(GradeResult result)
        {
            return result != null && result.HasGrades ? EquivalentGrade(result.Earned) : null;
        }

        /// <summary>Summarises the students' averages. Students with no scores yet (null) are skipped.</summary>
        public static ClassSummary Summarize(IEnumerable<double?> averages)
        {
            var values = (averages ?? Enumerable.Empty<double?>())
                .Where(a => a.HasValue)
                .Select(a => a.Value)
                .ToList();

            var summary = new ClassSummary { Graded = values.Count };
            if (values.Count > 0)
            {
                summary.Average = values.Average();
                summary.Highest = values.Max();
                summary.Lowest = values.Min();
            }

            return summary;
        }

        /// <summary>
        /// What-If: the final grade (percent of the whole course) if the remaining categories get the given scores.
        /// final = earned + sum of (weight x score / 100). Scores are limited to 0-100.
        /// </summary>
        public static double ProjectFinal(double earned, IEnumerable<double> weights, IEnumerable<double> scores)
        {
            var total = earned;
            var weightList = (weights ?? Enumerable.Empty<double>()).ToList();
            var scoreList = (scores ?? Enumerable.Empty<double>()).ToList();

            for (var i = 0; i < weightList.Count && i < scoreList.Count; i++)
            {
                var score = Math.Max(0, Math.Min(100, scoreList[i]));
                total += weightList[i] * score / 100.0;
            }

            return total;
        }

        /// <summary>Tolerance for the target checks, so rounding noise cannot flip a result.</summary>
        public const double TargetTolerance = 0.000001;

        /// <summary>
        /// What-If: the average needed on the remaining work to reach a target final grade.
        /// required = (target - earned) / remaining weight x 100.
        /// </summary>
        public static TargetResult CheckTarget(double target, double earned, double remainingWeight)
        {
            var result = new TargetResult();

            if (remainingWeight <= WeightTolerance)
            {
                result.Status = TargetStatus.NothingLeft;
                return result;
            }

            var required = (target - earned) / remainingWeight * 100.0;
            result.Required = required;

            if (required <= TargetTolerance) result.Status = TargetStatus.Secured;
            else if (required > 100 + TargetTolerance) result.Status = TargetStatus.OutOfReach;
            else result.Status = TargetStatus.Reachable;

            return result;
        }

        // Lower end of each distribution range. The last range runs up to 100.
        private static readonly double[] BinEdges = { 0, 60, 70, 80, 90 };

        private static readonly string[] BinLabels =
        {
            "Below 60", "60 to below 70", "70 to below 80", "80 to below 90", "90 to 100"
        };

        /// <summary>
        /// Counts students per score range. Each average is rounded to 2 decimals first (the same way it is
        /// shown), so the counts always agree with the numbers in the score table. Null (not graded) is skipped.
        /// </summary>
        public static List<DistributionBin> Distribution(IEnumerable<double?> averages)
        {
            var bins = new List<DistributionBin>();
            for (var i = 0; i < BinEdges.Length; i++)
            {
                bins.Add(new DistributionBin
                {
                    Label = BinLabels[i],
                    Min = BinEdges[i],
                    Max = i + 1 < BinEdges.Length ? BinEdges[i + 1] : 100
                });
            }

            foreach (var average in averages ?? Enumerable.Empty<double?>())
            {
                if (!average.HasValue) continue;

                var value = Math.Round(average.Value, 2);
                for (var i = bins.Count - 1; i >= 0; i--)
                {
                    if (value >= bins[i].Min || i == 0)
                    {
                        bins[i].Count++;
                        break;
                    }
                }
            }

            return bins;
        }

        /// <summary>Total effective weight of the course (both periods together). It is 100 when each period totals 100.</summary>
        public static double TotalWeight(IEnumerable<GradeCategory> categories)
        {
            return (categories ?? Enumerable.Empty<GradeCategory>()).Sum(c => EffectiveWeight(c));
        }

        public static bool IsFullWeight(double totalWeight)
        {
            return Math.Abs(totalWeight - 100) < WeightTolerance;
        }

        // Lowest average (percent) for each equivalent grade, highest first. Anything below the last
        // line (65) is 5.00 (failed). Lower bounds only, no rounding: 96.99 is 1.25 and 64.99 is 5.00.
        // Change the numbers here if the registrar uses a different table; the What-If page reads this
        // table from the server, so nothing else needs editing.
        //
        // Boundary checks (average -> equivalent):
        //   100 -> 1.00   97 -> 1.00   96.99 -> 1.25   93 -> 1.25   92.99 -> 1.50   89 -> 1.50
        //   88.99 -> 1.75   85 -> 1.75   84.99 -> 2.00   81 -> 2.00   80.99 -> 2.25   77 -> 2.25
        //   76.99 -> 2.50   73 -> 2.50   72.99 -> 2.75   69 -> 2.75   68.99 -> 3.00   65 -> 3.00
        //   64.99 -> 5.00   0 -> 5.00
        private static readonly double[][] GradeScale =
        {
            new[] { 97.0, 1.00 }, new[] { 93.0, 1.25 }, new[] { 89.0, 1.50 },
            new[] { 85.0, 1.75 }, new[] { 81.0, 2.00 }, new[] { 77.0, 2.25 },
            new[] { 73.0, 2.50 }, new[] { 69.0, 2.75 }, new[] { 65.0, 3.00 }
        };

        /// <summary>Lowest average (percent) that passes.</summary>
        public const double PassingAverage = 65.0;

        /// <summary>True when the average is at least the passing average. No average means not passing.</summary>
        public static bool IsPassing(double? average)
        {
            return average.HasValue && Math.Round(average.Value, 6) >= PassingAverage;
        }

        public const double FailingGrade = 5.00;

        /// <summary>Equivalent grade (1.00 to 5.00) for an average percent. Null when there is no average yet.</summary>
        public static double? EquivalentGrade(double? average)
        {
            if (!average.HasValue) return null;

            // Round away floating-point noise so an exact 85 is never read as 84.99999999.
            var value = Math.Round(average.Value, 6);

            foreach (var step in GradeScale)
            {
                if (value >= step[0]) return step[1];
            }

            return FailingGrade;
        }

        /// <summary>The grade table, highest first. The What-If page uses it for its target buttons.</summary>
        public static List<GradeStep> Scale()
        {
            return GradeScale.Select(s => new GradeStep { MinAverage = s[0], Grade = s[1] }).ToList();
        }

        /// <summary>A number written the same way on every computer (dot decimal), for HTML attributes read by JavaScript.</summary>
        public static string Raw(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Text for showing a grade or weight: two decimals, or a dash when there is no value.</summary>
        public static string Format(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.00", CultureInfo.InvariantCulture) : "\u2014";
        }
    }
}
