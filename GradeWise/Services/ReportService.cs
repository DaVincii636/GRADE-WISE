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
    public static class ReportService
    {
        public static AnalyticsViewModel GetAnalytics(string courseId, string facultyId, string scope = null)
        {
            var db = new MongoDbContext();
            var course = FindOwned(db, courseId, facultyId);
            if (course == null) return null;

            var courseScope = GradeScopes.Normalize(course.ReflectedScope);
            scope = string.IsNullOrEmpty(scope) ? courseScope : GradeScopes.Normalize(scope);

            var students = EnrolledStudents(db, course);
            var byStudent = ScoresByStudent(db, course);

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
                    Average = avg
                };

                if (!avg.HasValue)
                {
                    notGradedStudents.Add(entry);
                    continue;
                }

                var rounded = Math.Round(avg.Value, 2);
                // FIX: Ensure students with exactly 100 or slight floating point errors above 100 go to the last bin.
                var binIndex = bins.Count - 1;
                for (var i = bins.Count - 2; i >= 0; i--)
                {
                    if (rounded >= bins[i].Min)
                    {
                        binIndex = i;
                        break;
                    }
                }
                bins[binIndex].Students.Add(entry);
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
                    Group = GradeGroups.Normalize(category.Group),
                    Period = GradePeriods.Normalize(category.Period),
                    Weight = GradeCalculator.ScopeWeight(category, scope),
                    Graded = scores.Count,
                    Average = scores.Count > 0 ? (double?)scores.Average() : null,
                    Lowest = scores.Count > 0 ? (double?)scores.Min() : null,
                    Highest = scores.Count > 0 ? (double?)scores.Max() : null,
                    PassRate = scores.Count > 0 ? (double?)Math.Round(scores.Count(v => GradeCalculator.IsPassing(v)) * 100.0 / scores.Count, 2) : null
                });
            }

            AddExtraStatistics(model, course, students, byStudent, earned, scope);
            return model;
        }

        // Median, spread, pass rate, completion, top and attention lists, equivalent grades and the term comparison.
        private static void AddExtraStatistics(AnalyticsViewModel model, Course course, List<User> students,
            Dictionary<string, List<ScoreEntry>> byStudent, List<double?> earned, string scope)
        {
            var graded = new List<DistributionStudent>();
            for (var i = 0; i < students.Count; i++)
            {
                if (!earned[i].HasValue) continue;
                graded.Add(new DistributionStudent { StudentNo = students[i].StudentNo, FullName = students[i].FullName, Average = earned[i] });
            }

            if (graded.Count > 0)
            {
                var values = graded.Select(g => g.Average.Value).OrderBy(v => v).ToList();
                var mid = values.Count / 2;
                model.Median = values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2.0;

                var mean = values.Average();
                model.StdDev = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
                model.Range = values[values.Count - 1] - values[0];

                model.Passing = graded.Count(g => GradeCalculator.IsPassing(g.Average));
                model.Failing = graded.Count - model.Passing;
                model.PassRate = Math.Round(model.Passing * 100.0 / graded.Count, 2);

                model.TopStudents = graded.OrderByDescending(g => g.Average).ThenBy(g => g.FullName).Take(5).ToList();
                model.AttentionStudents = graded.Where(g => !GradeCalculator.IsPassing(g.Average))
                    .OrderBy(g => g.Average).ThenBy(g => g.FullName).Take(5).ToList();

                var labels = new[] { 1.00, 1.25, 1.50, 1.75, 2.00, 2.25, 2.50, 2.75, 3.00, 5.00 };
                var counts = new Dictionary<double, int>();
                foreach (var g in graded)
                {
                    var eq = GradeCalculator.EquivalentGrade(g.Average);
                    if (!eq.HasValue) continue;
                    var key = Math.Round(eq.Value, 2);
                    int n;
                    counts[key] = counts.TryGetValue(key, out n) ? n + 1 : 1;
                }
                foreach (var label in labels)
                {
                    int n;
                    counts.TryGetValue(label, out n);
                    model.EquivalentBins.Add(new EquivalentBin
                    {
                        Label = label.ToString("0.00", CultureInfo.InvariantCulture),
                        Count = n,
                        Percent = Math.Round(n * 100.0 / graded.Count, 2)
                    });
                }
            }

            if (scope == GradeScopes.FinalGrade)
            {
                model.Incomplete = students.Count(s => GradeCalculator.IsIncomplete(course.Categories, ScoresFrom(byStudent, s.Id)));
            }

            // Score completion: category scores entered out of students x categories.
            model.ScoresEntered = model.Categories.Sum(c => c.Graded);
            model.ScoresExpected = students.Count * model.Categories.Count;
            if (model.ScoresExpected > 0)
            {
                model.CompletionPercent = Math.Round(model.ScoresEntered * 100.0 / model.ScoresExpected, 2);
            }

            // Class Standing and Exam averages, weighted by the categories' own weights.
            model.ClassStandingAverage = GroupAverage(model.Categories, GradeGroups.ClassStanding);
            model.ExamAverage = GroupAverage(model.Categories, GradeGroups.Exam);

            // Midterm vs Final Term, for the students graded in both.
            if (GradeCalculator.ScopeReady(course.Categories, GradeScopes.Midterm) && GradeCalculator.ScopeReady(course.Categories, GradeScopes.FinalTerm))
            {
                var midterms = new List<double>();
                var finals = new List<double>();
                foreach (var student in students)
                {
                    var scores = ScoresFrom(byStudent, student.Id);
                    var m = GradeCalculator.CalculatePeriod(course.Categories, scores, GradePeriods.Midterm).EarnedOrNull;
                    var f = GradeCalculator.CalculatePeriod(course.Categories, scores, GradePeriods.FinalTerm).EarnedOrNull;
                    if (!m.HasValue || !f.HasValue) continue;

                    midterms.Add(m.Value);
                    finals.Add(f.Value);
                    var rm = Math.Round(m.Value, 2);
                    var rf = Math.Round(f.Value, 2);
                    if (rf > rm) model.Improved++;
                    else if (rf < rm) model.Declined++;
                    else model.Unchanged++;
                }

                if (midterms.Count > 0)
                {
                    model.HasTermComparison = true;
                    model.TermCompared = midterms.Count;
                    model.MidtermAverage = midterms.Average();
                    model.FinalTermAverage = finals.Average();
                    model.TermChange = model.FinalTermAverage - model.MidtermAverage;
                }
            }
        }

        private static double? GroupAverage(IEnumerable<CategoryStat> categories, string group)
        {
            var rows = categories.Where(c => c.Group == group && c.Average.HasValue && c.Weight > 0).ToList();
            var weight = rows.Sum(c => c.Weight);
            return weight > 0 ? (double?)(rows.Sum(c => c.Average.Value * c.Weight) / weight) : null;
        }

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

                var incomplete = wholeCourse && GradeCalculator.IsIncomplete(course.Categories, scores);
                for (var i = 0; i < results.Length; i++)
                {
                    columns[i].Add(results[i].EarnedOrNull);
                    cells.Add(Cell(results[i].EarnedOrNull));
                    // The last column of a whole-course row is the Final Grade, which can be INC (incomplete).
                    cells.Add(wholeCourse && i == results.Length - 1 && incomplete
                        ? "INC"
                        : Cell(GradeCalculator.EquivalentFromEarned(results[i])));
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