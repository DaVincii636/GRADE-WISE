using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using GradeWise.Helpers;
using GradeWise.Models;
using MongoDB.Driver;

namespace GradeWise.Data
{
    public static class MongoDbSeeder
    {
        // The demo accounts and their demo passwords (see SampleData).
        private static readonly Dictionary<string, string> DemoPasswords =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "admin", "Admin@123" },
                { "faculty", "Faculty@123" },
                { "2026-0001", "Student@123" },
                { "2026-0002", "Student@123" },
                { "2026-0003", "Student@123" }
            };

        public static void RestoreDemoPasswords()
        {
            var wanted = ConfigurationManager.AppSettings["RestoreDemoPasswords"];
            if (string.IsNullOrWhiteSpace(wanted)) return;

            var db = new MongoDbContext();
            var names = wanted.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var raw in names)
            {
                var loginId = raw.Trim();

                string password;
                if (!DemoPasswords.TryGetValue(loginId, out password)) continue;

                var lower = loginId.ToLower();
                var user = db.Users.Find(u => u.StudentNo.ToLower() == lower).FirstOrDefault();
                if (user == null) continue;

                string salt;
                int iterations;
                var hash = PasswordHelper.CreateHash(password, out salt, out iterations);

                db.Users.UpdateOne(
                    u => u.Id == user.Id,
                    Builders<User>.Update
                        .Set(u => u.PasswordHash, hash)
                        .Set(u => u.Salt, salt)
                        .Set(u => u.PasswordIterations, iterations)
                        .Set(u => u.MustChangePassword, false)
                        .Set(u => u.IsActive, true));
            }
        }

        public static void Initialize()
        {
            var db = new MongoDbContext();

            var userCount = db.Users.CountDocuments(Builders<User>.Filter.Empty);
            if (userCount == 0)
            {
                db.Users.InsertMany(SampleData.Users);
            }

            var courseCount = db.Courses.CountDocuments(Builders<Course>.Filter.Empty);
            if (courseCount == 0)
            {
                db.Courses.InsertMany(SampleData.Courses);
            }

            var gradeCount = db.Grades.CountDocuments(Builders<GradeRecord>.Filter.Empty);
            if (gradeCount == 0)
            {
                db.Grades.InsertMany(SampleData.Grades);
            }

            var termCount = db.Terms.CountDocuments(Builders<AcademicTerm>.Filter.Empty);
            if (termCount == 0)
            {
                db.Terms.InsertMany(SampleData.Terms);
            }

            var snapshotCount = db.Snapshots.CountDocuments(Builders<ScoreSnapshot>.Filter.Empty);
            if (snapshotCount == 0)
            {
                db.Snapshots.InsertMany(SampleData.Snapshots);
            }

            EnsureIndexes(db);
        }

        private static void EnsureIndexes(MongoDbContext db)
        {
            // Grades are read per course and per student.
            try
            {
                var keys = Builders<GradeRecord>.IndexKeys
                    .Ascending(g => g.CourseId)
                    .Ascending(g => g.StudentId);
                db.Grades.Indexes.CreateOne(new CreateIndexModel<GradeRecord>(keys));
            }
            catch (System.Exception) { }

            // Progress history is read per course and student, in time order.
            try
            {
                var keys = Builders<ScoreSnapshot>.IndexKeys
                    .Ascending(s => s.CourseId)
                    .Ascending(s => s.StudentId)
                    .Ascending(s => s.TakenAt);
                db.Snapshots.Indexes.CreateOne(new CreateIndexModel<ScoreSnapshot>(keys));
            }
            catch (System.Exception) { }

            // FIX: Every page looks the signed-in account up by its sign-in name. 
            // Enforce Unique constraints to prevent duplicate users.
            try
            {
                var indexOptions = new CreateIndexOptions { Unique = true };

                db.Users.Indexes.CreateOne(new CreateIndexModel<User>(
                    Builders<User>.IndexKeys.Ascending(u => u.StudentNo), indexOptions));

                db.Users.Indexes.CreateOne(new CreateIndexModel<User>(
                    Builders<User>.IndexKeys.Ascending(u => u.Email), indexOptions));
            }
            catch (System.Exception)
            {
                // If this fails, duplicate users exist in the DB. Delete them in Compass and restart.
            }

            // Notifications are listed per student, newest first, so index them that way.
            try
            {
                var keys = Builders<Notification>.IndexKeys
                    .Ascending(n => n.StudentId)
                    .Descending(n => n.CreatedAtUtc);
                db.Notifications.Indexes.CreateOne(new CreateIndexModel<Notification>(keys));
            }
            catch (System.Exception) { }
        }
    }
}