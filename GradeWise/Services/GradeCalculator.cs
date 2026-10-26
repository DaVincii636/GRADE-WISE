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

    /// <summary>
    /// The one place the grade formula lives, so it can be checked on its own (Phase 7 builds on it).
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

            foreach (var step in GradeScale)
            {
                if (average.Value >= step[0]) return step[1];
            }

            return FailingGrade;
        }

        /// <summary>Text for showing a grade or weight: two decimals, or a dash when there is no value.</summary>
        public static string Format(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.00", CultureInfo.InvariantCulture) : "\u2014";
        }
    }
}
