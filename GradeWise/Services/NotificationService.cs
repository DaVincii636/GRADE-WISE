using System;
using System.Collections.Generic;
using System.Linq;
using GradeWise.Data;
using GradeWise.Models;
using MongoDB.Driver;

namespace GradeWise.Services
{
    public static class NotificationService
    {
        public const int MaxPerStudent = 100;
        public const int DefaultListLimit = 50;
        public const int MaxNamesListed = 3;

        public static void Add(string studentId, string courseId, string courseCode, string kind, string title, string message)
        {
            if (string.IsNullOrEmpty(studentId)) return;

            try
            {
                var db = new MongoDbContext();
                db.Notifications.InsertOne(new Notification
                {
                    Id = "n-" + Guid.NewGuid().ToString("N").Substring(0, 12), // FIX: Added prefix and length
                    StudentId = studentId,
                    CourseId = courseId,
                    CourseCode = courseCode,
                    Kind = kind,
                    Title = title,
                    Message = message,
                    CreatedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc), // FIX: Force UTC
                    IsRead = false
                });

                var tooOld = db.Notifications
                    .Find(n => n.StudentId == studentId)
                    .SortByDescending(n => n.CreatedAtUtc)
                    .Skip(MaxPerStudent)
                    .Project(n => n.Id)
                    .ToList();

                if (tooOld.Count > 0)
                {
                    db.Notifications.DeleteMany(n => n.StudentId == studentId && tooOld.Contains(n.Id));
                }
            }
            catch (Exception)
            {
                // intentionally ignored
            }
        }

        public static void NotifyScoresChanged(Course course, string studentId, IList<string> changedCategories)
        {
            if (course == null || changedCategories == null || changedCategories.Count == 0) return;

            Add(studentId, course.Id, course.Code, NotificationKinds.Scores,
                course.Code + " scores updated",
                DescribeChanged(changedCategories));
        }

        public static void NotifyCategoriesChanged(Course course, string studentId)
        {
            if (course == null) return;

            Add(studentId, course.Id, course.Code, NotificationKinds.Categories,
                course.Code + " grading categories updated",
                "Your instructor changed the grading categories or weights for " + course.Name + ".");
        }

        public static void NotifyEnrolled(Course course, string studentId)
        {
            if (course == null) return;

            Add(studentId, course.Id, course.Code, NotificationKinds.Enrolled,
                "Enrolled in " + course.Code,
                "You were added to " + course.Code + ": " + course.Name + ".");
        }

        public static void NotifyRemoved(Course course, string studentId)
        {
            if (course == null) return;

            Add(studentId, course.Id, course.Code, NotificationKinds.Removed,
                "Removed from " + course.Code,
                "You were removed from " + course.Code + ": " + course.Name + ".");
        }

        public static string DescribeChanged(IList<string> names)
        {
            var list = names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
            if (list.Count == 0) return "Your scores were updated.";
            if (list.Count == 1) return list[0] + " was updated.";

            if (list.Count <= MaxNamesListed)
            {
                return string.Join(", ", list.Take(list.Count - 1)) + " and " + list[list.Count - 1] + " were updated.";
            }

            var extra = list.Count - MaxNamesListed;
            return string.Join(", ", list.Take(MaxNamesListed)) + " and " + extra + " more were updated.";
        }

        public static List<Notification> ListFor(string studentId, int limit = DefaultListLimit)
        {
            var db = new MongoDbContext();
            return db.Notifications
                .Find(n => n.StudentId == studentId)
                .SortByDescending(n => n.CreatedAtUtc)
                .Limit(limit)
                .ToList();
        }

        public static int UnreadCount(string studentId)
        {
            var db = new MongoDbContext();
            return (int)db.Notifications.CountDocuments(n => n.StudentId == studentId && n.IsRead == false);
        }

        public static bool MarkRead(string id, string studentId)
        {
            return SetRead(id, studentId, true);
        }

        public static bool MarkUnread(string id, string studentId)
        {
            return SetRead(id, studentId, false);
        }

        public static void MarkAllRead(string studentId)
        {
            var db = new MongoDbContext();
            db.Notifications.UpdateMany(
                n => n.StudentId == studentId && n.IsRead == false,
                Builders<Notification>.Update.Set(n => n.IsRead, true));
        }

        public static bool Delete(string id, string studentId)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(studentId)) return false;

            var db = new MongoDbContext();
            var result = db.Notifications.DeleteOne(n => n.Id == id && n.StudentId == studentId);
            return result.DeletedCount > 0;
        }

        public static void DeleteAll(string studentId)
        {
            if (string.IsNullOrEmpty(studentId)) return;

            var db = new MongoDbContext();
            db.Notifications.DeleteMany(n => n.StudentId == studentId);
        }

        private static bool SetRead(string id, string studentId, bool read)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(studentId)) return false;

            var db = new MongoDbContext();
            var result = db.Notifications.UpdateOne(
                n => n.Id == id && n.StudentId == studentId,
                Builders<Notification>.Update.Set(n => n.IsRead, read));
            return result.MatchedCount > 0;
        }
    }
}