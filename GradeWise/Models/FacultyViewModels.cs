using System.Collections.Generic;
using GradeWise.Models;
using GradeWise.Services;

namespace GradeWise.Models
{
    /// <summary>One row of the "My courses" table.</summary>
    public class CourseListItem
    {
        public string Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Term { get; set; }
        public int StudentCount { get; set; }
        public int CategoryCount { get; set; }
        public double WeightTotal { get; set; }
        public double? ClassAverage { get; set; }

        /// <summary>Student-category cells that still have no score.</summary>
        public int MissingScores { get; set; }
    }

    public class CourseListViewModel
    {
        public List<CourseListItem> Courses { get; set; } = new List<CourseListItem>();
        public int StudentCount { get; set; }
        public int MissingScores { get; set; }
    }

    /// <summary>One student's row in the score entry table.</summary>
    public class ScoreRow
    {
        public string StudentId { get; set; }
        public string StudentNo { get; set; }
        public string FullName { get; set; }

        /// <summary>Text shown in each score box, one per category (same order as the course categories).</summary>
        public List<string> Inputs { get; set; } = new List<string>();

        /// <summary>True for each box whose text is not a valid score.</summary>
        public List<bool> Invalid { get; set; } = new List<bool>();

        public GradeResult Result { get; set; }
    }

    public class CourseDetailViewModel
    {
        public string CourseId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Term { get; set; }
        public List<GradeCategory> Categories { get; set; } = new List<GradeCategory>();
        public double WeightTotal { get; set; }
        public List<ScoreRow> Rows { get; set; } = new List<ScoreRow>();
        public ClassSummary Summary { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        public bool WeightsOk
        {
            get { return Categories.Count > 0 && GradeCalculator.IsFullWeight(WeightTotal); }
        }
    }

    public class EnrollViewModel
    {
        public string CourseId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Term { get; set; }
        public List<User> Enrolled { get; set; } = new List<User>();

        /// <summary>Active students who are not in the course yet.</summary>
        public List<User> Available { get; set; } = new List<User>();
    }

    /// <summary>One row of the categories form. Original is the name it had when the page was opened ("" for a new row).</summary>
    public class CategoryRow
    {
        public string Original { get; set; }
        public string Name { get; set; }
        public string Weight { get; set; }
    }

    public class CategoriesViewModel
    {
        public string CourseId { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Term { get; set; }
        public List<CategoryRow> Rows { get; set; } = new List<CategoryRow>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class CreateCourseViewModel
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Term { get; set; }
        public List<string> TermNames { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
    }
}
