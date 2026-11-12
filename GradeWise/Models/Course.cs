using System.Collections.Generic;

namespace GradeWise.Models
{
    /// <summary>One grading category of a course, e.g. { "Quizzes", 20 }.</summary>
    public class GradeCategory
    {
        public string Name { get; set; }

        /// <summary>
        /// Percent of its period (0-100). The weights inside one period should total 100.
        /// The category counts for Weight x GradePeriods.Share of the final grade.
        /// </summary>
        public double Weight { get; set; }

        /// <summary>"Midterm" or "Final Term". Documents saved before periods existed have no value and load as Midterm.</summary>
        public string Period { get; set; } = GradePeriods.Midterm;
    }

    /// <summary>A course.</summary>
    public class Course
    {
        public string Id { get; set; }

        public string Code { get; set; }

        public string Name { get; set; }

        public string Term { get; set; }

        /// <summary>Section the course is for (e.g. SBIT-3D). Empty until the sections module exists.</summary>
        public string Section { get; set; } = string.Empty;

        /// <summary>Id of the faculty user who owns the course.</summary>
        public string FacultyId { get; set; }

        /// <summary>
        /// Which numbers the course reflects: "midterm" (default), "final-term" or "final-grade".
        /// Documents saved before this field existed load as midterm. Read it with GradeScopes.Normalize.
        /// </summary>
        public string ReflectedScope { get; set; } = GradeScopes.Midterm;

        public List<GradeCategory> Categories { get; set; } = new List<GradeCategory>();

        /// <summary>Ids of enrolled student users.</summary>
        public List<string> StudentIds { get; set; } = new List<string>();
    }
}
