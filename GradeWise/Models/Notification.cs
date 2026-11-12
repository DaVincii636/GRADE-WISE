using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;

namespace GradeWise.Models
{
    /// <summary>The kinds of notification a student can receive.</summary>
    public static class NotificationKinds
    {
        public const string Scores = "scores";
        public const string Categories = "categories";
        public const string Enrolled = "enrolled";
        public const string Removed = "removed";
    }

    /// <summary>A stored message for one student, created when faculty change something in their course.</summary>
    public class Notification
    {
        public string Id { get; set; }

        public string StudentId { get; set; }

        public string CourseId { get; set; }

        public string CourseCode { get; set; }

        /// <summary>One of <see cref="NotificationKinds"/>.</summary>
        public string Kind { get; set; }

        public string Title { get; set; }

        public string Message { get; set; }

        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime CreatedAtUtc { get; set; }

        public bool IsRead { get; set; }
    }

    /// <summary>Everything the bell dropdown needs to draw itself.</summary>
    public class NotificationBellViewModel
    {
        /// <summary>Newest first, at most 50.</summary>
        public List<Notification> Items { get; set; } = new List<Notification>();

        public int Unread { get; set; }
    }
}
