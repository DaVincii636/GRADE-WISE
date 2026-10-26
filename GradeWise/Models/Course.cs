using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GradeWise.Models
{
    /// <summary>One grading category of a course, e.g. { "Quizzes", 20 }.</summary>
    public class GradeCategory
    {
        public string Name { get; set; }

        /// <summary>Percent of the final grade (0-100). A course's weights should total 100.</summary>
        public double Weight { get; set; }
    }

    /// <summary>Collection: courses</summary>
    public class Course
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        public string Code { get; set; }

        public string Name { get; set; }

        public string Term { get; set; }

        /// <summary>Id of the faculty user who owns the course.</summary>
        [BsonRepresentation(BsonType.ObjectId)]
        public string FacultyId { get; set; }

        public List<GradeCategory> Categories { get; set; } = new List<GradeCategory>();

        /// <summary>Ids of enrolled student users.</summary>
        [BsonRepresentation(BsonType.ObjectId)]
        public List<string> StudentIds { get; set; } = new List<string>();
    }
}
