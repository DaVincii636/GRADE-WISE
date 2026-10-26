using System.Collections.Generic;
using GradeWise.Helpers;
using GradeWise.Models;

namespace GradeWise.Data
{
    /// <summary>
    /// In-memory demo data. Everything the app shows or changes lives here until a real
    /// database is added later. Restarting the site resets it to the values below.
    ///
    /// Demo logins:  admin / Admin@123   faculty / Faculty@123
    ///               2026-0001, 2026-0002, 2026-0003 / Student@123
    /// </summary>
    public static class SampleData
    {
        public static readonly List<User> Users = new List<User>
        {
            MakeUser("u-admin",    "admin",     "Demo Administrator", "admin@example.com",    "Admin@123",   Roles.Administrator),
            MakeUser("u-faculty",  "faculty",   "Demo Faculty",       "faculty@example.com",  "Faculty@123", Roles.Faculty),
            MakeUser("u-student1", "2026-0001", "Demo Student One",   "student1@example.com", "Student@123", Roles.Student),
            MakeUser("u-student2", "2026-0002", "Demo Student Two",   "student2@example.com", "Student@123", Roles.Student),
            MakeUser("u-student3", "2026-0003", "Demo Student Three", "student3@example.com", "Student@123", Roles.Student)
        };

        private static User MakeUser(string id, string loginId, string fullName, string email, string password, string role)
        {
            string salt;
            var hash = PasswordHelper.CreateHash(password, out salt);

            return new User
            {
                Id = id,
                StudentNo = loginId,
                FullName = fullName,
                Email = email.ToLowerInvariant(),
                PasswordHash = hash,
                Salt = salt,
                Role = role,
                IsActive = true
            };
        }

        public static readonly List<Course> Courses = new List<Course>
        {
            new Course
            {
                Id = "c-it101",
                Code = "IT101",
                Name = "Introduction to Programming",
                Term = "1st Semester, S.Y. 2026-2027",
                FacultyId = "u-faculty",
                Categories = new List<GradeCategory>
                {
                    new GradeCategory { Name = "Quizzes", Weight = 20 },
                    new GradeCategory { Name = "Projects", Weight = 30 },
                    new GradeCategory { Name = "Midterm", Weight = 20 },
                    new GradeCategory { Name = "Final", Weight = 30 }
                },
                StudentIds = new List<string> { "u-student1", "u-student2", "u-student3" }
            }
        };

        public static readonly List<GradeRecord> Grades = new List<GradeRecord>
        {
            new GradeRecord
            {
                Id = "g-1", CourseId = "c-it101", StudentId = "u-student1",
                Scores = new List<ScoreEntry>
                {
                    new ScoreEntry { Category = "Quizzes", Score = 85 },
                    new ScoreEntry { Category = "Projects", Score = 90 },
                    new ScoreEntry { Category = "Midterm", Score = 78 },
                    new ScoreEntry { Category = "Final", Score = null }
                }
            },
            new GradeRecord
            {
                Id = "g-2", CourseId = "c-it101", StudentId = "u-student2",
                Scores = new List<ScoreEntry>
                {
                    new ScoreEntry { Category = "Quizzes", Score = 72 },
                    new ScoreEntry { Category = "Projects", Score = 80 },
                    new ScoreEntry { Category = "Midterm", Score = 65 },
                    new ScoreEntry { Category = "Final", Score = null }
                }
            },
            new GradeRecord
            {
                Id = "g-3", CourseId = "c-it101", StudentId = "u-student3",
                Scores = new List<ScoreEntry>
                {
                    new ScoreEntry { Category = "Quizzes", Score = 95 },
                    new ScoreEntry { Category = "Projects", Score = null },
                    new ScoreEntry { Category = "Midterm", Score = null },
                    new ScoreEntry { Category = "Final", Score = null }
                }
            }
        };
    }
}
