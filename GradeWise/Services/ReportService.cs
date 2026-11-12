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

        /// <summary>
        /// Distribution, class summary and per-category averages for one course and one scope
        /// ("midterm", "final-term" or "final-grade"; null or anything else means the course's own scope).
        /// Null if it is not the faculty member's course.
        /// </summary>
        public static AnalyticsViewModel GetAnalytics(string courseId, string facultyId, string scope = null)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return null;

            var courseScope = GradeScopes.Normalize(course.ReflectedScope);
            scope = string.IsNullOrEmpty(scope) ? courseScope : GradeScopes.Normalize(scope);

            var students = EnrolledStudents(db, course);
            var byStudent = ScoresByStudent(db, course);

            // Earned Grade of the scope (points collected out of 100, ungraded work counts as 0). Null = nothing graded yet.
            var earned = students
                .Select(s => GradeCalculator.CalculateScope(course.Categories, ScoresFrom(byStudent, s.Id), scope).EarnedOrNull)
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
                Scope = scope,
                ScopeLabel = GradeScopes.Label(scope),
                CourseScope = courseScope,
                CourseScopeLabel = GradeScopes.Label(courseScope),
                CourseId = course.Id,
                Code = course.Code,
                Name = course.Name,
                Term = course.Term,
                StudentCount = students.Count,
                NotGraded = notGradedCount,
                NotGradedPercent = Math.Round(notGradedCount * 100.0 / totalStudents, 2),
                WeightTotal = GradeCalculator.ScopeTotal(course.Categories, scope),
                WeightsOk = GradeCalculator.ScopeReady(course.Categories, scope),
                Summary = summary,
                Bins = bins,
                NotGradedStudents = notGradedStudents
            };

            foreach (var category in GradeCalculator.ScopeCategories(course.Categories, scope))
            {
                var scores = students
                    .Select(s => FindScore(ScoresFrom(byStudent, s.Id), category.Name))
                    .Where(v => v.HasValue)
                    .Select(v => v.Value)
                    .ToList();

                model.Categories.Add(new CategoryStat
                {
                    Name = category.Name,
                    Period = GradePeriods.Normalize(category.Period),
                    Weight = GradeCalculator.ScopeWeight(category, scope),
                    Graded = scores.Count,
                    Average = scores.Count > 0 ? (double?)scores.Average() : null
                });
            }

            return model;
        }

        // ---------- class record CSV ----------

        /// <summary>
        /// The class record as CSV text for one scope (null or anything else means the course's own scope).
        /// Midterm or Final Term: the scope's categories, then that period's Earned Grade and Equivalent.
        /// Final Grade: every category (headers name the period), the Midterm and Final Term Earned Grade and
        /// Equivalent, the Final Grade and its Equivalent. Class average / highest / lowest rows follow.
        /// Every number is the zero-based Earned Grade, as on the Course page.
        /// Returns null if it is not the faculty member's course. The caller adds the UTF-8 marker Excel needs.
        /// </summary>
        public static string BuildClassCsv(string courseId, string facultyId, string scope, out string fileName)
        {
            fileName = null;

            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return null;

            scope = string.IsNullOrEmpty(scope) ? GradeScopes.Normalize(course.ReflectedScope) : GradeScopes.Normalize(scope);
            var wholeCourse = scope == GradeScopes.FinalGrade;
            var scopeLabel = GradeScopes.Label(scope);

            fileName = SafeFileName(course.Code) + "-" + scope + "-class-record-"
                + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv";

            var students = EnrolledStudents(db, course);
            var byStudent = ScoresByStudent(db, course);
            var cats = GradeCalculator.ScopeCategories(course.Categories, scope).ToList();
            var gradeColumns = wholeCourse ? 3 : 1;
            var width = 2 + cats.Count + gradeColumns * 2;
            var sb = new StringBuilder();

            // Final Grade headers name each category's period, e.g. "Quiz (Midterm 15%)".
            var header = new List<string> { Text("Student No"), Text("Name") };
            header.AddRange(cats.Select(c => Text(wholeCourse
                ? c.Name + " (" + GradePeriods.Normalize(c.Period) + " " + Whole(c.Weight) + "%)"
                : c.Name + " (" + Whole(c.Weight) + "%)")));
            if (wholeCourse)
            {
                header.Add(Text("Midterm Earned"));
                header.Add(Text("Midterm Equivalent"));
                header.Add(Text("Final Term Earned"));
                header.Add(Text("Final Term Equivalent"));
                header.Add(Text("Final Grade"));
                header.Add(Text("Equivalent"));
            }
            else
            {
                header.Add(Text(scopeLabel + " Earned"));
                header.Add(Text(scopeLabel + " Equivalent"));
            }
            AppendRow(sb, header);

            // one list of Earned Grades per grade column (Midterm, Final Term, Final Grade) or just the scope
            var columns = Enumerable.Range(0, gradeColumns).Select(i => new List<double?>()).ToList();
            var categoryScores = cats.Select(c => new List<double>()).ToList();

            foreach (var student in students)
            {
                var scores = ScoresFrom(byStudent, student.Id);
                var cells = new List<string> { Text(student.StudentNo), Text(student.FullName) };

                for (var i = 0; i < cats.Count; i++)
                {
                    var value = FindScore(scores, cats[i].Name);
                    cells.Add(value.HasValue ? Whole(value.Value) : string.Empty);
                    if (value.HasValue) categoryScores[i].Add(value.Value);
                }

                var results = wholeCourse
                    ? new[]
                    {
                        GradeCalculator.CalculatePeriod(course.Categories, scores, GradePeriods.Midterm),
                        GradeCalculator.CalculatePeriod(course.Categories, scores, GradePeriods.FinalTerm),
                        GradeCalculator.Calculate(course.Categories, scores)
                    }
                    : new[] { GradeCalculator.CalculateScope(course.Categories, scores, scope) };

                for (var i = 0; i < results.Length; i++)
                {
                    columns[i].Add(results[i].EarnedOrNull);
                    cells.Add(Cell(results[i].EarnedOrNull));
                    cells.Add(Cell(GradeCalculator.EquivalentFromEarned(results[i])));
                }

                AppendRow(sb, cells);
            }

            var summaries = columns.Select(c => GradeCalculator.Summarize(c)).ToList();
            if (summaries[summaries.Count - 1].Graded > 0)
            {
                AppendRow(sb, Enumerable.Repeat(string.Empty, width));

                var categoryAverages = categoryScores.Select(l => l.Count > 0 ? Fixed(l.Average()) : string.Empty).ToList();
                var noCategoryCells = Enumerable.Repeat(string.Empty, cats.Count).ToList();

                AppendRow(sb, ClassRow("Class average", categoryAverages, summaries.Select(x => x.Average)));
                AppendRow(sb, ClassRow("Highest", noCategoryCells, summaries.Select(x => x.Highest)));
                AppendRow(sb, ClassRow("Lowest", noCategoryCells, summaries.Select(x => x.Lowest)));
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
        private static List<string> ClassRow(string label, IEnumerable<string> categoryCells, IEnumerable<double?> gradeValues)
        {
            var cells = new List<string> { string.Empty, Text(label) };
            cells.AddRange(categoryCells);
            foreach (var value in gradeValues)
            {
                cells.Add(Cell(value));
                cells.Add(Cell(GradeCalculator.EquivalentGrade(value)));
            }
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

        // One query for every enrolled student's scores (instead of one round trip per student).
        private static Dictionary<string, List<ScoreEntry>> ScoresByStudent(MongoDbContext db, Course course)
        {
            var courseId = course.Id;
            var enrolled = course.StudentIds;
            var map = new Dictionary<string, List<ScoreEntry>>();
            foreach (var record in db.Grades.Find(g => g.CourseId == courseId && enrolled.Contains(g.StudentId)).ToList())
            {
                if (record.StudentId != null && !map.ContainsKey(record.StudentId))
                {
                    map[record.StudentId] = record.Scores ?? new List<ScoreEntry>();
                }
            }
            return map;
        }

        private static List<ScoreEntry> ScoresFrom(Dictionary<string, List<ScoreEntry>> map, string studentId)
        {
            List<ScoreEntry> scores;
            return studentId != null && map.TryGetValue(studentId, out scores) ? scores : new List<ScoreEntry>();
        }

        private static double? FindScore(IEnumerable<ScoreEntry> scores, string category)
        {
            var entry = scores.FirstOrDefault(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase));
            return entry == null ? null : entry.Score;
        }
    }
}