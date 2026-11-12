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
            // Earned Grade (points collected out of 100, ungraded work counts as 0). Null = nothing graded yet.
            var earned = students
                .Select(s => GradeCalculator.Calculate(course.Categories, ScoresOf(db, course, s.Id)).EarnedOrNull)
                .ToList();

            var summary = GradeCalculator.Summarize(earned);
            var bins = GradeCalculator.Distribution(earned);

            var notGradedStudents = new List<DistributionStudent>();
            for (var sIdx = 0; sIdx < students.Count; sIdx++)
            {
                var student = students[sIdx];
                var avg = earned[sIdx];
                var entry = new DistributionStudent
                {
                    StudentNo = student.StudentNo,
                    FullName = student.FullName,
                    Average = avg // holds the student's Earned Grade
                };

                if (!avg.HasValue)
                {
                    notGradedStudents.Add(entry);
                    continue;
                }

                var rounded = Math.Round(avg.Value, 2);
                for (var i = bins.Count - 1; i >= 0; i--)
                {
                    if (rounded >= bins[i].Min || i == 0)
                    {
                        bins[i].Students.Add(entry);
                        break;
                    }
                }
            }

            var totalStudents = Math.Max(1, students.Count);
            foreach (var bin in bins)
            {
                bin.Percent = Math.Round(bin.Count * 100.0 / totalStudents, 2);
                bin.BarPercent = (int)Math.Round(bin.Percent);
            }

            var notGradedCount = students.Count - summary.Graded;
            var model = new AnalyticsViewModel
            {
                CourseId = course.Id,
                Code = course.Code,
                Name = course.Name,
                Term = course.Term,
                StudentCount = students.Count,
                NotGraded = notGradedCount,
                NotGradedPercent = Math.Round(notGradedCount * 100.0 / totalStudents, 2),
                WeightTotal = GradeCalculator.TotalWeight(course.Categories),
                Summary = summary,
                Bins = bins,
                NotGradedStudents = notGradedStudents
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
                    Period = GradePeriods.Normalize(category.Period),
                    Weight = category.Weight,
                    Graded = scores.Count,
                    Average = scores.Count > 0 ? (double?)scores.Average() : null
                });
            }

            return model;
        }

        // ---------- class record CSV ----------

        /// <summary>
        /// The class record as CSV text: one row per student with each category score, then the Midterm and
        /// Final Term Earned Grade and Equivalent, the Final Grade and its Equivalent, followed by class
        /// average / highest / lowest rows. Every number is the zero-based Earned Grade, as on the Course page.
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
            var width = 2 + course.Categories.Count + 6;
            var sb = new StringBuilder();

            var header = new List<string> { Text("Student No"), Text("Name") };
            header.AddRange(course.Categories.Select(c => Text(c.Name + " (" + Whole(c.Weight) + "%)")));
            header.Add(Text("Midterm Earned"));
            header.Add(Text("Midterm Equivalent"));
            header.Add(Text("Final Term Earned"));
            header.Add(Text("Final Term Equivalent"));
            header.Add(Text("Final Grade"));
            header.Add(Text("Equivalent"));
            AppendRow(sb, header);

            var midtermEarned = new List<double?>();
            var finalTermEarned = new List<double?>();
            var finalGrades = new List<double?>();
            var categoryScores = course.Categories.Select(c => new List<double>()).ToList();

            foreach (var student in students)
            {
                var scores = ScoresOf(db, course, student.Id);
                var result = GradeCalculator.Calculate(course.Categories, scores);
                var midterm = GradeCalculator.CalculatePeriod(course.Categories, scores, GradePeriods.Midterm);
                var finalTerm = GradeCalculator.CalculatePeriod(course.Categories, scores, GradePeriods.FinalTerm);
                midtermEarned.Add(midterm.EarnedOrNull);
                finalTermEarned.Add(finalTerm.EarnedOrNull);
                finalGrades.Add(result.EarnedOrNull);

                var cells = new List<string> { Text(student.StudentNo), Text(student.FullName) };

                for (var i = 0; i < course.Categories.Count; i++)
                {
                    var value = FindScore(scores, course.Categories[i].Name);
                    cells.Add(value.HasValue ? Whole(value.Value) : string.Empty);
                    if (value.HasValue) categoryScores[i].Add(value.Value);
                }

                cells.Add(Cell(midterm.EarnedOrNull));
                cells.Add(Cell(GradeCalculator.EquivalentFromEarned(midterm)));
                cells.Add(Cell(finalTerm.EarnedOrNull));
                cells.Add(Cell(GradeCalculator.EquivalentFromEarned(finalTerm)));
                cells.Add(Cell(result.EarnedOrNull));
                cells.Add(Cell(GradeCalculator.EquivalentFromEarned(result)));
                AppendRow(sb, cells);
            }

            var midtermSummary = GradeCalculator.Summarize(midtermEarned);
            var finalTermSummary = GradeCalculator.Summarize(finalTermEarned);
            var gradeSummary = GradeCalculator.Summarize(finalGrades);
            if (gradeSummary.Graded > 0)
            {
                AppendRow(sb, Enumerable.Repeat(string.Empty, width));

                var categoryAverages = categoryScores.Select(l => l.Count > 0 ? Fixed(l.Average()) : string.Empty).ToList();
                var noCategoryCells = Enumerable.Repeat(string.Empty, course.Categories.Count).ToList();

                AppendRow(sb, ClassRow("Class average", categoryAverages, midtermSummary.Average, finalTermSummary.Average, gradeSummary.Average));
                AppendRow(sb, ClassRow("Highest", noCategoryCells, midtermSummary.Highest, finalTermSummary.Highest, gradeSummary.Highest));
                AppendRow(sb, ClassRow("Lowest", noCategoryCells, midtermSummary.Lowest, finalTermSummary.Lowest, gradeSummary.Lowest));
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

        // One class row (average, highest or lowest). Each Equivalent is worked out from the Earned Grade next to it.
        private static List<string> ClassRow(string label, IEnumerable<string> categoryCells, double? midterm, double? finalTerm, double? finalGrade)
        {
            var cells = new List<string> { string.Empty, Text(label) };
            cells.AddRange(categoryCells);
            cells.Add(Cell(midterm));
            cells.Add(Cell(GradeCalculator.EquivalentGrade(midterm)));
            cells.Add(Cell(finalTerm));
            cells.Add(Cell(GradeCalculator.EquivalentGrade(finalTerm)));
            cells.Add(Cell(finalGrade));
            cells.Add(Cell(GradeCalculator.EquivalentGrade(finalGrade)));
            return cells;
        }

        // Two decimals, or an empty cell when there is no value (nothing graded in that scope).
        private static string Cell(double? value)
        {
            return value.HasValue ? Fixed(value.Value) : string.Empty;
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