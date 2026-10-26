using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GradeWise.Models
{
    /// <summary>A student's score in one category. Score is null until it is graded.</summary>
    public class ScoreEntry
    {
        /// <summary>Matches a <see cref="GradeCategory.Name"/> in the course.</summary>
        public string Category { get; set; }

        public double? Score { get; set; }
    }

    /// <summary>Collection: grades (one document per student per course)</summary>
    public class GradeRecord
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string CourseId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string StudentId { get; set; }

        public List<ScoreEntry> Scores { get; set; } = new List<ScoreEntry>();
    }
}
