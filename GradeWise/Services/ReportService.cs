using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GradeWise.Data;
using GradeWise.Models;
using MongoDB.Driver;

namespace GradeWise.Services
{
    /// <summary>
    /// Faculty reports: the class analytics page and the class record CSV backed by MongoDB Atlas.
    /// </summary>
    public static class ReportService
    {
        // ---------- analytics ----------

        /// <summary>Distribution, class summary and per-category averages for one course. Null if it is not the faculty member's course.</summary>
        public static AnalyticsViewModel GetAnalytics(string courseId, string facultyId)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return null;

            var students = EnrolledStudents(db, course);
            var averages = students
                .Select(s => GradeCalculator.Calculate(course.Categories, ScoresOf(db, course, s.Id)).Average)
                .ToList();

            var summary = GradeCalculator.Summarize(averages);
            var bins = GradeCalculator.Distribution(averages);

            var biggest = Math.Max(1, bins.Max(b => b.Count));
            foreach (var bin in bins)
            {
                bin.BarPercent = (int)Math.Round(bin.Count * 100.0 / biggest);
            }

            var model = new AnalyticsViewModel
            {
                CourseId = course.Id,
                Code = course.Code,
                Name = course.Name,
                Term = course.Term,
                StudentCount = students.Count,
                NotGraded = students.Count - summary.Graded,
                WeightTotal = GradeCalculator.TotalWeight(course.Categories),
                Summary = summary,
                Bins = bins
            };

            foreach (var category in course.Categories)
            {
                var scores = students
                    .Select(s => FindScore(ScoresOf(db, course, s.Id), category.Name))
                    .Where(v => v.HasValue)
                    .Select(v => v.Value)
                    .ToList();

                model.Categories.Add(new CategoryStat
                {
                    Name = category.Name,
                    Weight = category.Weight,
                    Graded = scores.Count,
                    Average = scores.Count > 0 ? (double?)scores.Average() : null
                });
            }

            return model;
        }

        // ---------- class record CSV ----------

        /// <summary>
        /// The class record as CSV text: one row per student with each category score, the average,
        /// the equivalent grade and the points earned, then class average / highest / lowest rows.
        /// Returns null if it is not the faculty member's course. The caller adds the UTF-8 marker Excel needs.
        /// </summary>
        public static string BuildClassCsv(string courseId, string facultyId, out string fileName)
        {
            fileName = null;

            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return null;

            fileName = SafeFileName(course.Code) + "-class-record-"
                + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv";

            var students = EnrolledStudents(db, course);
            var width = 2 + course.Categories.Count + 3;
            var sb = new StringBuilder();

            var header = new List<string> { Text("Student No"), Text("Name") };
            header.AddRange(course.Categories.Select(c => Text(c.Name + " (" + Whole(c.Weight) + "%)")));
            header.Add(Text("Average (%)"));
            header.Add(Text("Equivalent grade"));
            header.Add(Text("Points earned"));
            AppendRow(sb, header);

            var averages = new List<double?>();
            var categoryScores = course.Categories.Select(c => new List<double>()).ToList();

            foreach (var student in students)
            {
                var scores = ScoresOf(db, course, student.Id);
                var result = GradeCalculator.Calculate(course.Categories, scores);
                averages.Add(result.Average);

                var cells = new List<string> { Text(student.StudentNo), Text(student.FullName) };

                for (var i = 0; i < course.Categories.Count; i++)
                {
                    var value = FindScore(scores, course.Categories[i].Name);
                    cells.Add(value.HasValue ? Whole(value.Value) : string.Empty);
                    if (value.HasValue) categoryScores[i].Add(value.Value);
                }

                var equivalent = GradeCalculator.EquivalentGrade(result.Average);
                cells.Add(result.Average.HasValue ? Fixed(result.Average.Value) : string.Empty);
                cells.Add(equivalent.HasValue ? Fixed(equivalent.Value) : string.Empty);
                cells.Add(result.HasGrades ? Fixed(result.Earned) : string.Empty);
                AppendRow(sb, cells);
            }

            var summary = GradeCalculator.Summarize(averages);
            if (summary.Graded > 0)
            {
                AppendRow(sb, Enumerable.Repeat(string.Empty, width));

                var average = new List<string> { string.Empty, Text("Class average") };
                average.AddRange(categoryScores.Select(l => l.Count > 0 ? Fixed(l.Average()) : string.Empty));
                average.Add(Fixed(summary.Average.Value));
                var classGrade = GradeCalculator.EquivalentGrade(summary.Average);
                average.Add(classGrade.HasValue ? Fixed(classGrade.Value) : string.Empty);
                average.Add(string.Empty);
                AppendRow(sb, average);

                AppendRow(sb, SummaryRow("Highest", summary.Highest.Value, course.Categories.Count));
                AppendRow(sb, SummaryRow("Lowest", summary.Lowest.Value, course.Categories.Count));
            }

            return sb.ToString();
        }

        // ---------- CSV helpers ----------

        public static string Text(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var first = value[0];
            if (first == '=' || first == '+' || first == '-' || first == '@' || first == '\t' || first == '\r' || first == '\n')
            {
                value = "'" + value;
            }

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || value != value.Trim())
            {
                value = "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        public static string SafeFileName(string value)
        {
            var chars = (value ?? string.Empty)
                .Select(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' ? c : '-')
                .ToArray();

            var name = new string(chars).Trim('-');
            return name.Length == 0 ? "course" : name;
        }

        private static List<string> SummaryRow(string label, double average, int categoryCount)
        {
            var cells = new List<string> { string.Empty, Text(label) };
            cells.AddRange(Enumerable.Repeat(string.Empty, categoryCount));
            cells.Add(Fixed(average));
            cells.Add(string.Empty);
            cells.Add(string.Empty);
            return cells;
        }

        private static void AppendRow(StringBuilder sb, IEnumerable<string> cells)
        {
            sb.Append(string.Join(",", cells));
            sb.Append("\r\n");
        }

        private static string Fixed(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string Whole(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // ---------- helpers ----------

        private static Course FindOwned(MongoDbContext db, string courseId, string facultyId)
        {
            if (string.IsNullOrEmpty(courseId) || string.IsNullOrEmpty(facultyId)) return null;
            return db.Courses.Find(c => c.Id == courseId && c.FacultyId == facultyId).FirstOrDefault();
        }

        private static List<User> EnrolledStudents(MongoDbContext db, Course course)
        {
            return db.Users.Find(u => course.StudentIds.Contains(u.Id))
                .SortBy(u => u.FullName)
                .ToList();
        }

        private static List<ScoreEntry> ScoresOf(MongoDbContext db, Course course, string studentId)
        {
            var record = db.Grades.Find(g => g.CourseId == course.Id && g.StudentId == studentId).FirstOrDefault();
            return record == null ? new List<ScoreEntry>() : record.Scores;
        }

        private static double? FindScore(IEnumerable<ScoreEntry> scores, string category)
        {
            var entry = scores.FirstOrDefault(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase));
            return entry == null ? null : entry.Score;
        }
    }
}