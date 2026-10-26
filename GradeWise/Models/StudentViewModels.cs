using System.Collections.Generic;

namespace GradeWise.Models
{
    /// <summary>One entry of the student's course picker.</summary>
    public class StudentCourseOption
    {
        public string Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
    }

    /// <summary>One line of the assessment breakdown. Only the student's own score is here.</summary>
    public class BreakdownRow
    {
        public string Category { get; set; }
        public double Weight { get; set; }

        /// <summary>The student's own score, or null while it is not graded.</summary>
        public double? Score { get; set; }

        /// <summary>Weight x score / 100, or null while not graded.</summary>
        public double? WeightedPoints { get; set; }

        /// <summary>Anonymous class average for this category. Null when too few students have a score.</summary>
        public double? ClassAverage { get; set; }
    }

    /// <summary>Everything the student dashboard shows. It never contains another student's name or score.</summary>
    public class StudentDashboardViewModel
    {
        public string StudentName { get; set; }

        public string StudentNo { get; set; }

        public List<StudentCourseOption> Courses { get; set; } = new List<StudentCourseOption>();

        public bool HasCourse { get; set; }
        public string SelectedId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Term { get; set; }

        public bool HasCategories { get; set; }
        public double WeightTotal { get; set; }
        public bool WeightsOk { get; set; }

        /// <summary>Weighted average of the graded work, percent. Null until something is graded.</summary>
        public double? Average { get; set; }

        /// <summary>Equivalent grade such as 1.75, or null until something is graded.</summary>
        public double? Equivalent { get; set; }

        public double Earned { get; set; }
        public double RemainingWeight { get; set; }

        public List<BreakdownRow> Rows { get; set; } = new List<BreakdownRow>();

        /// <summary>Anonymous class average. Null when too few students have scores to keep it anonymous.</summary>
        public double? ClassAverage { get; set; }

        /// <summary>How many students the class average is based on.</summary>
        public int ClassGraded { get; set; }

        /// <summary>Smallest group that may be averaged and shown.</summary>
        public int MinClassSize { get; set; }

        public List<string> Notifications { get; set; } = new List<string>();

        /// <summary>The student's own progress history in this course, oldest first.</summary>
        public List<HistoryPoint> History { get; set; } = new List<HistoryPoint>();
    }
    /// <summary>One line of the grade table: the lowest average (percent) that earns this grade.</summary>
    public class GradeStep
    {
        public double MinAverage { get; set; }

        public double Grade { get; set; }
    }

    /// <summary>One worked example the What-If page re-checks against the server's calculator.</summary>
    public class WhatIfCheck
    {
        public string Label { get; set; }

        /// <summary>"final" (projected final grade) or "required" (average needed for a target).</summary>
        public string Kind { get; set; }

        public double Earned { get; set; }
        public double Remaining { get; set; }

        /// <summary>The assumed average on the remaining work ("final"), or the target ("required").</summary>
        public double Input { get; set; }

        /// <summary>The server's answer.</summary>
        public double Expected { get; set; }

        /// <summary>The server's target status for "required" examples; empty for "final".</summary>
        public string Status { get; set; }
    }

    /// <summary>Everything the What-If simulator page needs. Only the student's own scores are in it.</summary>
    public class WhatIfViewModel
    {
        public List<StudentCourseOption> Courses { get; set; } = new List<StudentCourseOption>();

        public bool HasCourse { get; set; }
        public string SelectedId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Term { get; set; }

        public bool HasCategories { get; set; }
        public double WeightTotal { get; set; }
        public bool WeightsOk { get; set; }

        /// <summary>True when every category is graded, so there is nothing left to simulate.</summary>
        public bool NothingLeft { get; set; }

        public double? Average { get; set; }
        public double? Equivalent { get; set; }

        /// <summary>Points earned so far (weight x score / 100, added up).</summary>
        public double Earned { get; set; }

        public double RemainingWeight { get; set; }

        /// <summary>Starting value of every slider: the current average, or 75 when nothing is graded yet.</summary>
        public double DefaultScore { get; set; }

        /// <summary>Categories that already have a score.</summary>
        public List<BreakdownRow> Graded { get; set; } = new List<BreakdownRow>();

        /// <summary>Categories still waiting for a score; each gets a slider.</summary>
        public List<GradeCategory> Ungraded { get; set; } = new List<GradeCategory>();

        public List<GradeStep> Scale { get; set; } = new List<GradeStep>();

        public List<WhatIfCheck> Checks { get; set; } = new List<WhatIfCheck>();
    }
}
