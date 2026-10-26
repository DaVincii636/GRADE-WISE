using System;
using System.Collections.Generic;
using GradeWise.Services;

namespace GradeWise.Models
{
    /// <summary>
    /// A student's standing in one course at one moment. A new one is kept each time a score
    /// or a weight changes the student's numbers, so the progress history can be drawn later.
    /// </summary>
    public class ScoreSnapshot
    {
        public string Id { get; set; }

        public string CourseId { get; set; }

        public string StudentId { get; set; }

        public DateTime TakenAt { get; set; }

        /// <summary>Weighted average of the graded work at that time (percent). Null when nothing was graded.</summary>
        public double? Average { get; set; }

        public double Earned { get; set; }

        public double GradedWeight { get; set; }
    }

    /// <summary>One point of a student's progress history (a snapshot made ready for display).</summary>
    public class HistoryPoint
    {
        public DateTime TakenAt { get; set; }

        /// <summary>The date and time as text, e.g. "Oct 26, 2026 3:05 PM".</summary>
        public string Label { get; set; }

        public double? Average { get; set; }

        public double? Equivalent { get; set; }

        /// <summary>Change in average since the snapshot before this one. Null for the first, or when either has no average.</summary>
        public double? Change { get; set; }
    }

    /// <summary>One score range of the grade distribution. Min is included, Max is not (the last range includes 100).</summary>
    public class DistributionBin
    {
        public string Label { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }

        /// <summary>How many students have an average in this range.</summary>
        public int Count { get; set; }

        /// <summary>Bar length, 0 to 100, relative to the biggest range.</summary>
        public int BarPercent { get; set; }
    }

    /// <summary>Class-wide numbers for one grading category.</summary>
    public class CategoryStat
    {
        public string Name { get; set; }
        public double Weight { get; set; }

        /// <summary>How many students have a score in this category.</summary>
        public int Graded { get; set; }

        public double? Average { get; set; }
    }

    public class AnalyticsViewModel
    {
        public string CourseId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Term { get; set; }

        public int StudentCount { get; set; }

        /// <summary>Students with no score at all yet. They are not in any range.</summary>
        public int NotGraded { get; set; }

        public double WeightTotal { get; set; }
        public ClassSummary Summary { get; set; }
        public List<DistributionBin> Bins { get; set; } = new List<DistributionBin>();
        public List<CategoryStat> Categories { get; set; } = new List<CategoryStat>();

        public bool WeightsOk
        {
            get { return Categories.Count > 0 && GradeCalculator.IsFullWeight(WeightTotal); }
        }
    }
}
