
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;

namespace GradeWise.Models
{
    public class GradeCategory
    {
        public string Name { get; set; }

        public double Weight { get; set; }

        public string Period { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class Course
    {
        public string Id { get; set; }

        public string Code { get; set; }

        public string Name { get; set; }

        public string Term { get; set; }

        public string Section { get; set; }

        public string ReflectedScope { get; set; }

        public string FacultyId { get; set; }

        public List<GradeCategory> Categories { get; set; }
            = new List<GradeCategory>();

        public List<string> StudentIds { get; set; }
            = new List<string>();
    }
}
