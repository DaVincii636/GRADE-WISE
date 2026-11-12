using System;
using System.Collections.Generic;
using GradeWise.Services;

namespace GradeWise.Models
{
    /// <summary>One score that changed in a save: Category, what it was and what it became (null = empty).</summary>
    public class ScoreChange
    {
        public string Category { get; set; }

        public double? OldScore { get; set; }

        public double? NewScore { get; set; }
    }

    /// <summary>
    /// A student's standing in one course at one moment. A new one is kept each time a score
    /// or a weight changes the student's numbers, so the progress history can be drawn later.
    /// Snapshots saved before Reason and Changes existed have neither; they load with empty values.
    /// </summary>
    public class ScoreSnapshot
    {
        public string Id { get; set; }

        public string CourseId { get; set; }

        public string StudentId { get; set; }

        /// <summary>When the snapshot was taken. New snapshots are written in UTC.</summary>
        public DateTime TakenAt { get; set; }

        /// <summary>Weighted average of the graded work at that time (percent). Null when nothing was graded. Kept for old snapshots.</summary>
        public double? Average { get; set; }

        /// <summary>Earned Grade at that time: points collected out of 100 (ungraded work counts as 0).</summary>
        public double Earned { get; set; }

        public double GradedWeight { get; set; }

        /// <summary>Why it was taken, e.g. "Scores updated" or "Categories changed". Empty on old snapshots.</summary>
        public string Reason { get; set; }

        /// <summary>The scores that changed in that save. Empty on old snapshots and on category changes.</summary>
        public List<ScoreChange> Changes { get; set; } = new List<ScoreChange>();
    }

    /// <summary>One point of a student's progress history (a snapshot made ready for display).</summary>
    public class HistoryPoint
    {
        /// <summary>When it was taken, in UTC.</summary>
        public DateTime TakenAt { get; set; }

        /// <summary>The same moment as ISO text (UTC, ends in Z). The page turns it into the viewer's own time.</summary>
        public string Iso { get; set; }

        /// <summary>The date and time as text, e.g. "10/26/2026 3:05 PM" (server time; the page replaces it with the viewer's time).</summary>
        public string Label { get; set; }

        /// <summary>Earned Grade at that moment (points out of 100).</summary>
        public double Earned { get; set; }

        /// <summary>False when nothing was graded yet at that moment (the equivalent grade is then not shown).</summary>
        public bool HasGrades { get; set; }

        public double? Equivalent { get; set; }

        /// <summary>Change in Earned Grade since the snapshot before this one. Null for the first.</summary>
        public double? Change { get; set; }

        /// <summary>Short text of what changed, e.g. "Quizzes 80 -> 85, Exam - -> 90".</summary>
        public string Summary { get; set; }

        /// <summary>The full list of changes (for a tooltip); the same as Summary when it is short.</summary>
        public string Details { get; set; }
    }

    /// <summary>A student and their calculated average inside a grade distribution range.</summary>
    public class DistributionStudent
    {
        public string StudentNo { get; set; }
        public string FullName { get; set; }
        public double? Average { get; set; }
    }

    /// <summary>One score range of the grade distribution. Min is included, Max is not (the last range includes 100).</summary>
    public class DistributionBin
    {
        public string Label { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }

        /// <summary>How many students have an average in this range.</summary>
        public int Count { get; set; }

        /// <summary>Percentage of enrolled students in this range (0 to 100), rounded to 2 decimals.</summary>
        public double Percent { get; set; }

        /// <summary>Bar length, 0 to 100, matching the percentage of students.</summary>
        public int BarPercent { get; set; }

        public List<DistributionStudent> Students { get; set; } = new List<DistributionStudent>();

        public string PercentText
        {
            get { return Percent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%"; }
        }
    }

    /// <summary>Class-wide numbers for one grading category.</summary>
    public class CategoryStat
    {
        public string Name { get; set; }

        /// <summary>"Midterm" or "Final Term".</summary>
        public string Period { get; set; }

        /// <summary>Raw weight inside its period (the period totals 100).</summary>
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

        /// <summary>Percentage of enrolled students with no score yet (0 to 100), rounded to 2 decimals.</summary>
        public double NotGradedPercent { get; set; }

        public string NotGradedPercentText
        {
            get { return NotGradedPercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%"; }
        }

        public double WeightTotal { get; set; }
        public ClassSummary Summary { get; set; }
        public List<DistributionBin> Bins { get; set; } = new List<DistributionBin>();
        public List<DistributionStudent> NotGradedStudents { get; set; } = new List<DistributionStudent>();
        public List<CategoryStat> Categories { get; set; } = new List<CategoryStat>();

        public bool WeightsOk
        {
            get { return Categories.Count > 0 && GradeCalculator.IsFullWeight(WeightTotal); }
        }
    }
}
