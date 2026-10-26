using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GradeWise.Models
{
    /// <summary>The three roles in GradeWise. Stored in MongoDB as plain strings.</summary>
    public static class Roles
    {
        public const string Student = "Student";
        public const string Faculty = "Faculty";
        public const string Administrator = "Administrator";
    }

    /// <summary>Collection: users</summary>
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        /// <summary>Student number or username used to sign in.</summary>
        public string StudentNo { get; set; }

        public string FullName { get; set; }

        public string Email { get; set; }

        /// <summary>PBKDF2 hash (filled in Phase 3). Never store plain passwords.</summary>
        public string PasswordHash { get; set; }

        public string Salt { get; set; }

        /// <summary>One of the values in <see cref="Roles"/>.</summary>
        public string Role { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
