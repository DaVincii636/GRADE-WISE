using MongoDB.Bson.Serialization.Attributes;
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

        /// <summary>Top-level grading group: Class Standing or Exam.</summary>
        public string Group { get; set; } = GradeGroups.ClassStanding;

        /// <summary>Individual assessment columns inside this category (Quiz 1, Quiz 2, etc.).</summary>
        public List<GradeAssessment> Assessments { get; set; } = new List<GradeAssessment>();
    }

    public class GradeAssessment
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Order { get; set; }

        /// <summary>Highest possible score (HPS) of this assessment, like the HIGHEST POSSIBLE SCORE row of the class record.</summary>
        public double MaxScore { get; set; } = 100;
    }

    /// <summary>
    /// The two major grading groups inside a period. Class Standing must total 60 and Exam 40
    /// (100 combined), so the period still totals 100.
    /// </summary>
    public static class GradeGroups
    {
        public const string ClassStanding = "Class Standing";
        public const string Exam = "Exam";

        public static readonly string[] All = { ClassStanding, Exam };

        /// <summary>Anything that is not "Exam" (including a missing value from an old document) is Class Standing.</summary>
        public static string Normalize(string group)
        {
            return string.Equals((group ?? string.Empty).Trim(), Exam, System.StringComparison.OrdinalIgnoreCase) ? Exam : ClassStanding;
        }

        /// <summary>The weight a group must total inside one period.</summary>
        public static double RequiredTotal(string group)
        {
            return Normalize(group) == Exam ? 40 : 60;
        }
    }

    /// <summary>A course.</summary>
    public class Course
    {
        public string Id { get; set; }
        [BsonElement("course_id")]
        public string CourseId
        {
            get { return Id; }
            set { Id = value; }
        }

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
