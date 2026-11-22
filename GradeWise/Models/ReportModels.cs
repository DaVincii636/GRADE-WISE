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

        /// <summary>Midterm-only Earned Grade (0-100) at that time. Null on snapshots saved before scopes existed.</summary>
        public double? MidtermEarned { get; set; }

        public double? MidtermGradedWeight { get; set; }

        /// <summary>Final-Term-only Earned Grade (0-100) at that time. Null on snapshots saved before scopes existed.</summary>
        public double? FinalTermEarned { get; set; }

        public double? FinalTermGradedWeight { get; set; }

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

        public string Group { get; set; }

        /// <summary>"Midterm" or "Final Term".</summary>
        public string Period { get; set; }

        /// <summary>Raw weight inside its period (the period totals 100).</summary>
        public double Weight { get; set; }

        /// <summary>How many students have a score in this category.</summary>
        public int Graded { get; set; }

        public double? Average { get; set; }

        /// <summary>Lowest and highest score in this category (percent). Null when nobody has a score.</summary>
        public double? Lowest { get; set; }

        public double? Highest { get; set; }

        /// <summary>Percent of the students with a score here who are at or above the passing line. Null when nobody has a score.</summary>
        public double? PassRate { get; set; }

        /// <summary>Students with a score here (Average holds the score), highest first. Used by the range chart's popup.</summary>
        public List<DistributionStudent> Students { get; set; } = new List<DistributionStudent>();
    }

    /// <summary>How many graded students land on one equivalent grade (1.00 to 3.00, or 5.00).</summary>
    public class EquivalentBin
    {
        public string Label { get; set; }

        public int Count { get; set; }

        /// <summary>Percent of the graded students (0 to 100), rounded to 2 decimals.</summary>
        public double Percent { get; set; }

        /// <summary>The students on this equivalent grade, highest first.</summary>
        public List<DistributionStudent> Students { get; set; } = new List<DistributionStudent>();

        public string PercentText
        {
            get { return Percent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%"; }
        }
    }

    /// <summary>One student in the Midterm vs Final Term comparison.</summary>
    public class TermStudent
    {
        public string StudentNo { get; set; }
        public string FullName { get; set; }
        public double Midterm { get; set; }
        public double FinalTerm { get; set; }

        /// <summary>Final Term minus Midterm (Earned Grade points).</summary>
        public double Change { get; set; }

        /// <summary>"Improved", "Declined" or "Unchanged".</summary>
        public string Status { get; set; }
    }

    /// <summary>One row of a student-list popup (see the Analytics page).</summary>
    public class StudentModalRow
    {
        public string FullName { get; set; }
        public string StudentNo { get; set; }
        public string Value { get; set; }
    }

    /// <summary>A popup that lists students when a chart segment is clicked.</summary>
    public class StudentModalDef
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Badge { get; set; }
        public string Meta { get; set; }
        public string ValueHeader { get; set; }
        public string EmptyText { get; set; }
        public List<StudentModalRow> Rows { get; set; } = new List<StudentModalRow>();
    }

    public class AnalyticsViewModel
    {
        /// <summary>The scope being shown: "midterm", "final-term" or "final-grade".</summary>
        public string Scope { get; set; } = GradeScopes.Midterm;

        public string ScopeLabel { get; set; } = "Midterm";

        /// <summary>The scope students currently see (the course's reflected scope).</summary>
        public string CourseScope { get; set; } = GradeScopes.Midterm;

        public string CourseScopeLabel { get; set; } = "Midterm";

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

        /// <summary>Set by the service: true when the scope has categories and they total 100.</summary>
        public bool WeightsOk { get; set; }

        // ---------- More statistics (all for the scope shown, graded students only) ----------

        /// <summary>The middle Earned Grade. Null when nobody is graded.</summary>
        public double? Median { get; set; }

        /// <summary>Standard deviation of the Earned Grades (how spread out the class is). Null when nobody is graded.</summary>
        public double? StdDev { get; set; }

        /// <summary>Highest minus lowest Earned Grade. Null when nobody is graded.</summary>
        public double? Range { get; set; }

        /// <summary>Graded students at or above the passing line, and below it.</summary>
        public int Passing { get; set; }

        public int Failing { get; set; }

        /// <summary>Percent of graded students who pass. Null when nobody is graded.</summary>
        public double? PassRate { get; set; }

        /// <summary>Final Grade scope only: students marked INC (class standing is fine, Final Term exam still blank).</summary>
        public int Incomplete { get; set; }

        /// <summary>Category scores entered out of the scores the class could have (students x categories).</summary>
        public int ScoresEntered { get; set; }

        public int ScoresExpected { get; set; }

        /// <summary>Percent of possible category scores entered (0 to 100). Null when there is nothing to enter.</summary>
        public double? CompletionPercent { get; set; }

        /// <summary>Weighted average of the Class Standing and of the Exam categories (percent). Null when nothing is graded there.</summary>
        public double? ClassStandingAverage { get; set; }

        public double? ExamAverage { get; set; }

        /// <summary>The best and the lowest graded students (up to 5 each).</summary>
        public List<DistributionStudent> TopStudents { get; set; } = new List<DistributionStudent>();

        public List<DistributionStudent> AttentionStudents { get; set; } = new List<DistributionStudent>();

        /// <summary>Every graded student at or above the passing line (best first) and below it (lowest first).</summary>
        public List<DistributionStudent> PassingStudents { get; set; } = new List<DistributionStudent>();

        public List<DistributionStudent> FailingStudents { get; set; } = new List<DistributionStudent>();

        /// <summary>Graded students per equivalent grade, best first. Empty when nobody is graded.</summary>
        public List<EquivalentBin> EquivalentBins { get; set; } = new List<EquivalentBin>();

        // ---------- Midterm vs Final Term (students graded in both) ----------

        /// <summary>True when both terms have full weights and at least one student is graded in both.</summary>
        public bool HasTermComparison { get; set; }

        public int TermCompared { get; set; }

        public double? MidtermAverage { get; set; }

        public double? FinalTermAverage { get; set; }

        /// <summary>Average of (Final Term minus Midterm) over the compared students.</summary>
        public double? TermChange { get; set; }

        public int Improved { get; set; }

        public int Declined { get; set; }

        public int Unchanged { get; set; }

        /// <summary>The compared students, with both term grades. Behind the Improved / Declined / Unchanged popups.</summary>
        public List<TermStudent> TermStudents { get; set; } = new List<TermStudent>();
    }
}
