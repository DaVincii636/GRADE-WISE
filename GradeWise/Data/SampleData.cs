using System.Collections.Generic;
using GradeWise.Models;

namespace GradeWise.Data
{
    /// <summary>
    /// In-memory demo data used until a real database is added.
    /// Passwords are filled in later (Phase 3).
    /// </summary>
    public static class SampleData
    {
        public static readonly List<User> Users = new List<User>
        {
            new User { Id = "u-admin", StudentNo = "admin", FullName = "Demo Administrator", Email = "admin@example.com", Role = Roles.Administrator, IsActive = true },
            new User { Id = "u-faculty", StudentNo = "faculty", FullName = "Demo Faculty", Email = "faculty@example.com", Role = Roles.Faculty, IsActive = true },
            new User { Id = "u-student1", StudentNo = "2026-0001", FullName = "Demo Student One", Email = "student1@example.com", Role = Roles.Student, IsActive = true },
            new User { Id = "u-student2", StudentNo = "2026-0002", FullName = "Demo Student Two", Email = "student2@example.com", Role = Roles.Student, IsActive = true },
            new User { Id = "u-student3", StudentNo = "2026-0003", FullName = "Demo Student Three", Email = "student3@example.com", Role = Roles.Student, IsActive = true }
        };

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
