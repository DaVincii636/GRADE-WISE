using System.Collections.Generic;

namespace GradeWise.Models
{
    /// <summary>A student's score in one category. Score is null until it is graded.</summary>
    public class ScoreEntry
    {
        /// <summary>Matches a <see cref="GradeCategory.Name"/> in the course.</summary>
        public string Category { get; set; }

        public double? Score { get; set; }

        /// <summary>Individual scores keyed by the assessment definition on the course.</summary>
        public List<AssessmentScoreEntry> AssessmentScores { get; set; } = new List<AssessmentScoreEntry>();
    }

    public class AssessmentScoreEntry
    {
        public string AssessmentId { get; set; }
        public double? Score { get; set; }
    }

    /// <summary>Grades of one student in one course.</summary>
    public class GradeRecord
    {
        public string Id { get; set; }

        public string CourseId { get; set; }

        public string StudentId { get; set; }

        public List<ScoreEntry> Scores { get; set; } = new List<ScoreEntry>();
    }
}
