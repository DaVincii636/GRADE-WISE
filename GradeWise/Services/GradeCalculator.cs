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
        /// <summary>Points earned so far = sum of (weight x score / 100) over the graded categories.</summary>
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

        public static GradeResult Calculate(IEnumerable<GradeCategory> categories, IEnumerable<ScoreEntry> scores)
        {
            var result = new GradeResult();
            var scoreList = scores == null ? new List<ScoreEntry>() : scores.ToList();

            foreach (var category in categories ?? Enumerable.Empty<GradeCategory>())
            {
                result.TotalWeight += category.Weight;

                var entry = scoreList.FirstOrDefault(s =>
                    string.Equals(s.Category, category.Name, StringComparison.OrdinalIgnoreCase));

                if (entry != null && entry.Score.HasValue)
                {
                    result.GradedWeight += category.Weight;
                    result.Earned += category.Weight * entry.Score.Value / 100.0;
                }
            }

            return result;
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

        public static double TotalWeight(IEnumerable<GradeCategory> categories)
        {
            return (categories ?? Enumerable.Empty<GradeCategory>()).Sum(c => c.Weight);
        }

        public static bool IsFullWeight(double totalWeight)
        {
            return Math.Abs(totalWeight - 100) < WeightTolerance;
        }

        // Lowest average (percent) for each equivalent grade, highest first. Anything below the last
        // line is 5.00 (failed). This is the common 1.00 to 5.00 scale used in Philippine universities;
        // change the numbers here if the school uses a different table.
        private static readonly double[][] GradeScale =
        {
            new[] { 97.0, 1.00 }, new[] { 94.0, 1.25 }, new[] { 91.0, 1.50 },
            new[] { 88.0, 1.75 }, new[] { 85.0, 2.00 }, new[] { 82.0, 2.25 },
            new[] { 79.0, 2.50 }, new[] { 76.0, 2.75 }, new[] { 75.0, 3.00 }
        };

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
