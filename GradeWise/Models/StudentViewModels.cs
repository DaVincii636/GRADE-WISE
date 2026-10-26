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
    }
}
