using System;
using System.Collections.Generic;
using System.Linq;
using GradeWise.Helpers;
using GradeWise.Models;
using GradeWise.Services;

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
        /// <summary>
        /// Lock this while reading or changing the lists below. Several requests can run at the
        /// same time and a List is not safe to change while another request reads it.
        /// </summary>
        public static readonly object Sync = new object();

        /// <summary>Finds a user by student number / username (not case sensitive).</summary>
        public static User FindByLogin(string loginId)
        {
            lock (Sync)
            {
                return Users.FirstOrDefault(u => string.Equals(u.StudentNo, loginId, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>Finds a user by student number / username or by email (not case sensitive).</summary>
        public static User FindByLoginOrEmail(string value)
        {
            lock (Sync)
            {
                return Users.FirstOrDefault(u =>
                    string.Equals(u.StudentNo, value, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(u.Email, value, StringComparison.OrdinalIgnoreCase));
            }
        }

        public static readonly List<User> Users = new List<User>
        {
            MakeUser("u-admin",    "admin",     "Demo Administrator", "admin@example.com",    "Admin@123",   Roles.Administrator),
            MakeUser("u-faculty",  "faculty",   "Demo Faculty",       "faculty@example.com",  "Faculty@123", Roles.Faculty),
            MakeUser("u-student1", "2026-0001", "Jerick Du-ay",   "duay@example.com", "Student@123", Roles.Student),
            MakeUser("u-student2", "2026-0002", "Daniel Ponce",   "daniel@example.com", "Student@123", Roles.Student),
            MakeUser("u-student3", "2026-0003", "Khyle Castro", "khyle@example.com", "Student@123", Roles.Student)
        };

        private static User MakeUser(string id, string loginId, string fullName, string email, string password, string role)
        {
            string salt;
            int iterations;
            var hash = PasswordHelper.CreateHash(password, out salt, out iterations);

            return new User
            {
                Id = id,
                StudentNo = loginId,
                FullName = fullName,
                Email = email.ToLowerInvariant(),
                PasswordHash = hash,
                Salt = salt,
                PasswordIterations = iterations,
                Role = role,
                IsActive = true,
                CreatedAt = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc),
                LastLoginAt = null,
                MustChangePassword = false
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

        public static readonly List<AcademicTerm> Terms = new List<AcademicTerm>
        {
            new AcademicTerm { Id = "t-2025-2", Name = "2nd Semester, S.Y. 2025-2026", IsCurrent = false },
            new AcademicTerm { Id = "t-2026-1", Name = "1st Semester, S.Y. 2026-2027", IsCurrent = true },
            new AcademicTerm { Id = "t-2026-2", Name = "2nd Semester, S.Y. 2026-2027", IsCurrent = false }
        };

        /// <summary>
        /// Progress history: a snapshot of each student's standing every time their scores changed.
        /// New ones are added by CourseService when faculty save scores or weights. The demo history
        /// below is built from the scores above, one snapshot per graded category, two weeks apart.
        /// (Keep this field below Courses and Grades: fields are filled in the order they are written.)
        /// </summary>
        public static readonly List<ScoreSnapshot> Snapshots = BuildSnapshots();

        private static List<ScoreSnapshot> BuildSnapshots()
        {
            var list = new List<ScoreSnapshot>();
            var now = DateTime.Now;

            foreach (var course in Courses)
            {
                foreach (var record in Grades.Where(g => g.CourseId == course.Id))
                {
                    var graded = course.Categories
                        .Select(c => record.Scores.FirstOrDefault(s => s.Category == c.Name))
                        .Where(s => s != null && s.Score.HasValue)
                        .ToList();

                    for (var step = 0; step < graded.Count; step++)
                    {
                        var soFar = graded.Take(step + 1).ToList();
                        var result = GradeCalculator.Calculate(course.Categories, soFar);
                        var when = now.AddDays(-14 * (graded.Count - 1 - step)).AddHours(-1);
                        list.Add(HistoryService.Make(course.Id, record.StudentId, when, result));
                    }
                }
            }

            return list;
        }
    }
}