using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using GradeWise.Data;
using GradeWise.Helpers;
using GradeWise.Models;
using GradeWise.Services;
using MongoDB.Driver;
using Roles = GradeWise.Models.Roles;

namespace GradeWise.Controllers
{
    [Authorize(Roles = Roles.Administrator)]
    public class AdminController : Controller
    {
        private static readonly string[] AllRoles = { Roles.Administrator, Roles.Faculty, Roles.Student };
        private static readonly Regex LoginIdPattern = new Regex(@"^[A-Za-z0-9._-]{1,50}$");

        public ActionResult Index()
        {
            var db = new MongoDbContext();
            var users = db.Users.Find(_ => true).ToList()
                .OrderBy(u => Array.IndexOf(AllRoles, u.Role))
                .ThenBy(u => u.FullName).ToList();

            var current = db.Terms.Find(t => t.IsCurrent).FirstOrDefault();
            var deleteBlockers = FindDeleteBlockers(db, users.Select(u => u.Id).ToList());
            var activeAdmins = users.Count(u => u.Role == Roles.Administrator && u.IsActive);

            foreach (var u in users)
            {
                var lastAdmin = LastAdministratorReason(u, activeAdmins - (u.Role == Roles.Administrator && u.IsActive ? 1 : 0));
                if (lastAdmin != null && !deleteBlockers.ContainsKey(u.Id)) deleteBlockers[u.Id] = lastAdmin;
            }

            ViewBag.CurrentTerm = current == null ? null : current.Name;
            ViewBag.MeLogin = User.Identity.Name;
            ViewBag.DeleteBlockers = deleteBlockers;
            return View(users);
        }

        public ActionResult CreateUser()
        {
            ViewBag.Role = Roles.Student;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CreateUser(string fullName, string loginId, string email, string role,
            string firstName, string middleName, string lastName, string suffix)
        {
            firstName = (firstName ?? string.Empty).Trim();
            middleName = (middleName ?? string.Empty).Trim();
            lastName = (lastName ?? string.Empty).Trim();
            suffix = (suffix ?? string.Empty).Trim();
            var validSuffixes = new[] { "", "Jr.", "Sr.", "II", "III", "IV", "V" };
            fullName = string.Join(" ", new[] { firstName, middleName, lastName, suffix }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
            loginId = (loginId ?? string.Empty).Trim();
            email = (email ?? string.Empty).Trim().ToLowerInvariant();

            var errors = new List<string>();

            if (firstName.Length == 0) errors.Add("First name is required.");
            if (lastName.Length == 0) errors.Add("Last name is required.");
            if (new[] { firstName, middleName, lastName }.Any(x => x.Length > 40)) errors.Add("Each name field must be 40 characters or fewer.");
            if (!validSuffixes.Contains(suffix)) errors.Add("Choose a valid suffix.");
            if (fullName.Length > 100) errors.Add("Full name must be 100 characters or fewer.");

            if (loginId.Length == 0) errors.Add("Student number or username is required.");
            else if (!LoginIdPattern.IsMatch(loginId))
                errors.Add("Student number or username can only use letters, numbers, dots, dashes and underscores (up to 50 characters).");

            if (email.Length == 0) errors.Add("Email is required.");
            else if (!ValidationHelper.IsValidEmail(email)) errors.Add("Enter a valid email address.");

            if (!AllRoles.Contains(role)) errors.Add("Choose a role.");

            string tempPassword = null;
            if (errors.Count == 0)
            {
                var db = new MongoDbContext();
                var existingUser = db.Users.Find(u => u.StudentNo.ToLower() == loginId.ToLower()).FirstOrDefault();
                if (existingUser != null) errors.Add("That student number or username is already taken.");

                var existingEmail = db.Users.Find(u => u.Email.ToLower() == email.ToLower()).FirstOrDefault();
                if (existingEmail != null) errors.Add("That email is already used by another account.");

                if (errors.Count == 0)
                {
                    tempPassword = PasswordHelper.GenerateTemporaryPassword();
                    string salt;
                    int iterations;
                    var hash = PasswordHelper.CreateHash(tempPassword, out salt, out iterations);

                    var newUser = new User
                    {
                        Id = "u-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        StudentNo = loginId,
                        FullName = fullName,
                        FirstName = firstName,
                        MiddleName = middleName,
                        LastName = lastName,
                        Suffix = suffix,
                        Email = email,
                        PasswordHash = hash,
                        Salt = salt,
                        PasswordIterations = iterations,
                        Role = role,
                        IsActive = true,
                        MustChangePassword = true
                    };

                    try
                    {
                        db.Users.InsertOne(newUser);
                        EmailService.SendTempPassword(email, fullName, tempPassword);
                    }
                    catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
                    {
                        errors.Add("A user with that ID or email was just created by someone else. Please try again.");
                    }
                }
            }

            if (errors.Count > 0)
            {
                ViewBag.Errors = errors;
                ViewBag.FullName = fullName;
                ViewBag.LoginId = loginId;
                ViewBag.Email = email;
                ViewBag.Role = role;
                return View();
            }

            TempData["Message"] = "Account created for " + fullName + " (" + loginId + "). An email with their temporary password has been sent.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ChangeRole(string id, string role)
        {
            if (!AllRoles.Contains(role))
            {
                TempData["Error"] = "Choose a valid role.";
                return RedirectToAction("Index");
            }

            var db = new MongoDbContext();
            var target = db.Users.Find(u => u.Id == id).FirstOrDefault();

            if (target == null) TempData["Error"] = "That user no longer exists.";
            else if (IsMe(target)) TempData["Error"] = "You cannot change your own role. Ask another administrator.";
            else if (target.Role == role) TempData["Message"] = target.FullName + " is already " + role + ".";
            else
            {
                db.Users.UpdateOne(u => u.Id == id, Builders<User>.Update.Set(u => u.Role, role));
                TempData["Message"] = target.FullName + " is now " + role + ".";
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SetActive(string id, bool active)
        {
            var db = new MongoDbContext();
            var target = db.Users.Find(u => u.Id == id).FirstOrDefault();

            if (target == null) TempData["Error"] = "That user no longer exists.";
            else if (IsMe(target)) TempData["Error"] = "You cannot disable your own account.";
            else
            {
                db.Users.UpdateOne(u => u.Id == id, Builders<User>.Update.Set(u => u.IsActive, active));
                TempData["Message"] = target.FullName + (active ? " can sign in again." : " has been disabled.");
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ResetPassword(string id)
        {
            var db = new MongoDbContext();
            var target = db.Users.Find(u => u.Id == id).FirstOrDefault();

            if (target == null) TempData["Error"] = "That user no longer exists.";
            else
            {
                var tempPassword = PasswordHelper.GenerateTemporaryPassword();
                string salt;
                int iterations;
                var hash = PasswordHelper.CreateHash(tempPassword, out salt, out iterations);

                var update = Builders<User>.Update
                    .Set(u => u.PasswordHash, hash)
                    .Set(u => u.Salt, salt)
                    .Set(u => u.PasswordIterations, iterations)
                    .Set(u => u.MustChangePassword, true); // FORCE CHANGE

                db.Users.UpdateOne(u => u.Id == id, update);
                EmailService.SendTempPassword(target.Email, target.FullName, tempPassword);

                TempData["Message"] = "Password reset for " + target.FullName + ". Email sent.";
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteUser(string id, string confirmLoginId)
        {
            var db = new MongoDbContext();
            var target = string.IsNullOrEmpty(id) ? null : db.Users.Find(u => u.Id == id).FirstOrDefault();

            if (target == null) { TempData["Error"] = "That user no longer exists."; return RedirectToAction("Index"); }
            var label = target.FullName + " (" + target.StudentNo + ")";

            if (IsMe(target)) { TempData["Error"] = "You cannot delete your own account."; return RedirectToAction("Index"); }

            if (!string.Equals((confirmLoginId ?? string.Empty).Trim(), target.StudentNo, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "The account ID you typed does not match " + label + ". The account was not deleted.";
                return RedirectToAction("Index");
            }

            try
            {
                var otherActiveAdmins = db.Users.CountDocuments(u => u.Role == Roles.Administrator && u.IsActive && u.Id != target.Id);
                var lastAdmin = LastAdministratorReason(target, otherActiveAdmins);
                if (lastAdmin != null) { TempData["Error"] = label + " cannot be deleted. " + lastAdmin; return RedirectToAction("Index"); }

                string reason;
                if (FindDeleteBlockers(db, new List<string> { target.Id }).TryGetValue(target.Id, out reason))
                {
                    TempData["Error"] = label + " cannot be deleted. " + reason; return RedirectToAction("Index");
                }

                var rosterFilter = Builders<Course>.Filter.AnyEq(c => c.StudentIds, target.Id);
                var rosterCourseIds = db.Courses.Find(rosterFilter).ToList().Select(c => c.Id).ToList();
                var unenrolled = rosterCourseIds.Count == 0 ? 0 : db.Courses.UpdateMany(
                    Builders<Course>.Filter.In(c => c.Id, rosterCourseIds) & rosterFilter,
                    Builders<Course>.Update.Pull(c => c.StudentIds, target.Id)).ModifiedCount;

                var gradedNow = db.Grades.CountDocuments(GradedRecordsOf(new List<string> { target.Id })) > 0;
                var hasSnapshots = db.Snapshots.CountDocuments(s => s.StudentId == target.Id) > 0;
                if (gradedNow || hasSnapshots)
                {
                    if (rosterCourseIds.Count > 0) db.Courses.UpdateMany(Builders<Course>.Filter.In(c => c.Id, rosterCourseIds), Builders<Course>.Update.AddToSet(c => c.StudentIds, target.Id));
                    TempData["Error"] = label + " cannot be deleted because grades were just recorded.";
                    return RedirectToAction("Index");
                }

                var emptyRecords = Builders<GradeRecord>.Filter.Eq(g => g.StudentId, target.Id)
                    & Builders<GradeRecord>.Filter.Not(Builders<GradeRecord>.Filter.ElemMatch(g => g.Scores, s => s.Score != null));
                db.Grades.DeleteMany(emptyRecords);
                db.Notifications.DeleteMany(n => n.StudentId == target.Id);

                var deleted = db.Users.DeleteOne(u => u.Id == target.Id).DeletedCount;
                if (deleted != 1) { TempData["Error"] = "That user no longer exists."; return RedirectToAction("Index"); }

                TempData["Message"] = "Account deleted: " + label + "."
                    + (unenrolled > 0 ? " Also removed from " + unenrolled + (unenrolled == 1 ? " course." : " courses.") : string.Empty);
            }
            catch (Exception ex) when (ex is MongoException || ex is TimeoutException)
            {
                TempData["Error"] = "The database could not be reached, so " + label + " may not have been deleted.";
            }
            return RedirectToAction("Index");
        }

        public ActionResult Terms()
        {
            var db = new MongoDbContext();
            var terms = db.Terms.Find(_ => true).ToList();

            // Why a term cannot be deleted (in use, or courses use it). Terms not listed can be deleted.
            // AdminController.DeleteTerm enforces the same rules on the server.
            ViewBag.TermDeleteBlockers = FindTermDeleteBlockers(db, terms);
            return View(terms);
        }

        /// <summary>
        /// Adds a term. startDate / endDate (yyyy-MM-dd) are saved with it; both may be left out
        /// (the basic form used without JavaScript only sends the name), but not just one of them.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddTerm(string name, string startDate, string endDate)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0) TempData["Error"] = "Enter a term name.";
            else if (name.Length > 80) TempData["Error"] = "Term name must be 80 characters or fewer.";
            else
            {
                var db = new MongoDbContext();
                var existingTerm = db.Terms.Find(t => t.Name.ToLower() == name.ToLower()).FirstOrDefault();

                if (existingTerm != null) TempData["Error"] = "That term already exists.";
                else
                {
                    DateTime? start, end;
                    var dateError = CheckTermDates(db, null, name, startDate, endDate, out start, out end);
                    if (dateError != null) TempData["Error"] = "The term was not added. " + dateError;
                    else
                    {
                        db.Terms.InsertOne(new AcademicTerm
                        {
                            Id = "t-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                            Name = name,
                            IsCurrent = false,
                            StartDate = start,
                            EndDate = end
                        });
                        TempData["Message"] = "Term added: " + name + ".";
                    }
                }
            }
            return RedirectToAction("Terms");
        }

        /// <summary>
        /// Saves an edited term: its name (semester and academic year) and its dates.
        /// The name cannot change once courses use the term, because courses refer to their term by name.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditTerm(string id, string name, string startDate, string endDate)
        {
            var db = new MongoDbContext();
            try
            {
                var target = string.IsNullOrEmpty(id) ? null : db.Terms.Find(t => t.Id == id).FirstOrDefault();
                if (target == null) { TempData["Error"] = "That term no longer exists."; return RedirectToAction("Terms"); }

                name = (name ?? string.Empty).Trim();
                if (name.Length == 0) { TempData["Error"] = "The changes were not saved. Choose an academic year and semester."; return RedirectToAction("Terms"); }
                if (name.Length > 80) { TempData["Error"] = "The changes were not saved. Term name must be 80 characters or fewer."; return RedirectToAction("Terms"); }

                var renamed = !string.Equals(name, target.Name, StringComparison.Ordinal);
                if (renamed)
                {
                    var nameKey = name.ToLower();
                    if (db.Terms.Find(t => t.Id != target.Id && t.Name.ToLower() == nameKey).Any())
                    {
                        TempData["Error"] = "The changes were not saved. " + name + " already exists.";
                        return RedirectToAction("Terms");
                    }
                    var used = CoursesUsingTerm(db, target.Name);
                    if (used.Count > 0)
                    {
                        TempData["Error"] = "The changes were not saved. " + target.Name + " is used by " + used.Count +
                            (used.Count == 1 ? " course" : " courses") + " (" + CourseList(used.Select(c => c.Code)) +
                            "), so its semester and academic year cannot change. You can still change its dates.";
                        return RedirectToAction("Terms");
                    }
                }

                DateTime? start, end;
                var dateError = CheckTermDates(db, target.Id, name, startDate, endDate, out start, out end);
                if (dateError != null) { TempData["Error"] = "The changes were not saved. " + dateError; return RedirectToAction("Terms"); }

                var update = Builders<AcademicTerm>.Update.Set(t => t.Name, name);
                update = start.HasValue ? update.Set(t => t.StartDate, start).Set(t => t.EndDate, end)
                                        : update.Unset(t => t.StartDate).Unset(t => t.EndDate);
                if (db.Terms.UpdateOne(t => t.Id == target.Id, update).MatchedCount != 1)
                {
                    TempData["Error"] = "That term no longer exists.";
                    return RedirectToAction("Terms");
                }
                TempData["Message"] = "Term updated: " + name + (start.HasValue ? " (" + TermDate(start.Value) + " to " + TermDate(end.Value) + ")." : ".");
            }
            catch (Exception ex) when (ex is MongoException || ex is TimeoutException)
            {
                TempData["Error"] = "The database could not be reached, so the changes may not have been saved.";
            }
            return RedirectToAction("Terms");
        }

        private static readonly Regex TermNamePattern = new Regex(
            @"^\s*(1st Semester|2nd Semester|Summer)(?: Term)?\s*,\s*[SA]\.?\s*Y\.?\s*(\d{4})\s*-\s*(\d{4})\s*$",
            RegexOptions.IgnoreCase);

        /// <summary>"Aug 9, 2027", the same date format the Academic terms page shows.</summary>
        private static string TermDate(DateTime date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

        /// <summary>
        /// Checks a term's dates with the same rules as the Academic terms page, and returns the reason they
        /// cannot be saved (null when they can). Both dates empty means "no dates" and is allowed.
        /// Rules: real yyyy-MM-dd dates; the end date after the start date; when the name has an academic year
        /// (S.Y. 2026-2027) both dates fall within its two calendar years (Jan 1, 2026 to Dec 31, 2027);
        /// and the dates do not overlap another term's dates.
        /// </summary>
        private static string CheckTermDates(MongoDbContext db, string termId, string name, string startText, string endText,
            out DateTime? start, out DateTime? end)
        {
            start = null; end = null;
            startText = (startText ?? string.Empty).Trim();
            endText = (endText ?? string.Empty).Trim();
            if (startText.Length == 0 && endText.Length == 0) return null;
            if (startText.Length == 0 || endText.Length == 0) return "Enter both a start date and an end date.";

            DateTime s, e;
            if (!DateTime.TryParseExact(startText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out s) ||
                !DateTime.TryParseExact(endText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out e))
                return "Enter the dates as calendar dates.";
            if (e <= s) return "The end date must be later than the start date.";

            var m = TermNamePattern.Match(name ?? string.Empty);
            if (m.Success)
            {
                var min = new DateTime(int.Parse(m.Groups[2].Value), 1, 1);
                var max = new DateTime(int.Parse(m.Groups[3].Value), 12, 31);
                if (s < min || s > max || e < min || e > max)
                    return "Both dates must fall within A.Y. " + m.Groups[2].Value + "-" + m.Groups[3].Value +
                           " (" + TermDate(min) + " to " + TermDate(max) + ").";
            }

            var clash = db.Terms.Find(t => t.Id != termId && t.StartDate != null && t.EndDate != null).ToList()
                .Where(t => s <= t.EndDate.Value && e >= t.StartDate.Value)
                .OrderBy(t => t.StartDate.Value).FirstOrDefault();
            if (clash != null)
                return "These dates overlap " + clash.Name + " (" + TermDate(clash.StartDate.Value) + " to " + TermDate(clash.EndDate.Value) + ").";

            start = s; end = e;
            return null;
        }

        /// <summary>The courses that use a term (matched by name, case-insensitively); only each course's term and code are read.</summary>
        private static List<Course> CoursesUsingTerm(MongoDbContext db, string termName)
        {
            var key = (termName ?? string.Empty).ToLower();
            return db.Courses.Find(_ => true)
                .Project<Course>(Builders<Course>.Projection.Include(c => c.Term).Include(c => c.Code)).ToList()
                .Where(c => (c.Term ?? string.Empty).ToLower() == key)
                .ToList();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SetCurrentTerm(string id)
        {
            var db = new MongoDbContext();
            var chosen = db.Terms.Find(t => t.Id == id).FirstOrDefault();

            if (chosen == null) TempData["Error"] = "That term no longer exists.";
            else
            {
                db.Terms.UpdateMany(_ => true, Builders<AcademicTerm>.Update.Set(t => t.IsCurrent, false));
                db.Terms.UpdateOne(t => t.Id == id, Builders<AcademicTerm>.Update.Set(t => t.IsCurrent, true));
                TempData["Message"] = chosen.Name + " is now the current term.";
            }
            return RedirectToAction("Terms");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteTerm(string id)
        {
            var db = new MongoDbContext();
            try
            {
                var target = string.IsNullOrEmpty(id) ? null : db.Terms.Find(t => t.Id == id).FirstOrDefault();
                if (target == null) { TempData["Error"] = "That term no longer exists."; return RedirectToAction("Terms"); }

                string reason;
                if (FindTermDeleteBlockers(db, new List<AcademicTerm> { target }).TryGetValue(target.Id, out reason))
                {
                    TempData["Error"] = target.Name + " cannot be deleted. " + reason;
                    return RedirectToAction("Terms");
                }

                var deleted = db.Terms.DeleteOne(t => t.Id == target.Id && !t.IsCurrent).DeletedCount;
                if (deleted != 1) { TempData["Error"] = target.Name + " was not deleted. It may have just been made current or removed by someone else."; return RedirectToAction("Terms"); }

                // A course could have been created for this term while it was being deleted: put the term back.
                var termKey = (target.Name ?? string.Empty).ToLower();
                if (db.Courses.CountDocuments(c => c.Term.ToLower() == termKey) > 0)
                {
                    db.Terms.InsertOne(target);
                    TempData["Error"] = target.Name + " was not deleted because a course was just created for it.";
                    return RedirectToAction("Terms");
                }

                TempData["Message"] = "Term deleted: " + target.Name + ".";
            }
            catch (Exception ex) when (ex is MongoException || ex is TimeoutException)
            {
                TempData["Error"] = "The database could not be reached, so the term may not have been deleted.";
            }
            return RedirectToAction("Terms");
        }

        /// <summary>
        /// Terms that cannot be deleted, with the reason. Courses refer to a term by its name (case-insensitive),
        /// and grades, grade history and notifications belong to courses, so a term without courses has no records.
        /// </summary>
        private static Dictionary<string, string> FindTermDeleteBlockers(MongoDbContext db, List<AcademicTerm> terms)
        {
            var reasons = new Dictionary<string, string>();
            if (terms.Count == 0) return reasons;

            // Only each course's term and code are read; matching is done here because names are compared case-insensitively.
            var keys = new HashSet<string>(terms.Select(t => (t.Name ?? string.Empty).ToLower()));
            var coursesByTerm = db.Courses.Find(_ => true)
                .Project<Course>(Builders<Course>.Projection.Include(c => c.Term).Include(c => c.Code)).ToList()
                .Where(c => keys.Contains((c.Term ?? string.Empty).ToLower()))
                .GroupBy(c => (c.Term ?? string.Empty).ToLower())
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var t in terms)
            {
                List<Course> used;
                if (coursesByTerm.TryGetValue((t.Name ?? string.Empty).ToLower(), out used) && used.Count > 0)
                    reasons[t.Id] = "It is used by " + used.Count + (used.Count == 1 ? " course" : " courses")
                        + " (" + CourseList(used.Select(c => c.Code)) + "). Archive it instead.";
                else if (t.IsCurrent)
                    reasons[t.Id] = "It is the term in use. Make another term current first.";
            }
            return reasons;
        }

        private bool IsMe(User target) => string.Equals(target.StudentNo, User.Identity.Name, StringComparison.OrdinalIgnoreCase);

        private static FilterDefinition<GradeRecord> GradedRecordsOf(List<string> userIds)
        {
            return Builders<GradeRecord>.Filter.In(g => g.StudentId, userIds)
                & Builders<GradeRecord>.Filter.ElemMatch(g => g.Scores, s => s.Score != null);
        }

        private static Dictionary<string, string> FindDeleteBlockers(MongoDbContext db, List<string> userIds)
        {
            var reasons = new Dictionary<string, List<string>>();
            Action<string, string> add = (userId, text) => {
                if (!reasons.TryGetValue(userId, out var list)) reasons[userId] = list = new List<string>();
                list.Add(text);
            };

            if (userIds.Count > 0)
            {
                var owned = db.Courses.Find(Builders<Course>.Filter.In(c => c.FacultyId, userIds)).ToList();
                foreach (var group in owned.GroupBy(c => c.FacultyId))
                    add(group.Key, "They own " + group.Count() + " course(s) (" + CourseList(group.Select(c => c.Code)) + ").");

                var graded = db.Grades.Find(GradedRecordsOf(userIds)).ToList();
                if (graded.Count > 0)
                {
                    var courseIds = graded.Select(g => g.CourseId).Distinct().ToList();
                    var codes = db.Courses.Find(Builders<Course>.Filter.In(c => c.Id, courseIds)).ToList().ToDictionary(c => c.Id, c => c.Code);
                    foreach (var group in graded.GroupBy(g => g.StudentId))
                    {
                        var names = group.Select(g => g.CourseId).Distinct().Select(cid => codes.ContainsKey(cid) ? codes[cid] : "removed course");
                        add(group.Key, "They have recorded grades in " + group.Select(g => g.CourseId).Distinct().Count() + " course(s) (" + CourseList(names) + ").");
                    }
                }

                var withHistory = db.Snapshots.Distinct(s => s.StudentId, Builders<ScoreSnapshot>.Filter.In(s => s.StudentId, userIds)).ToList();
                foreach (var userId in withHistory)
                    if (!reasons.ContainsKey(userId)) add(userId, "They have a saved grade history.");
            }

            return reasons.ToDictionary(r => r.Key, r => string.Join(" ", r.Value) + " Disable the account instead.");
        }

        private static string LastAdministratorReason(User target, long otherActiveAdministrators)
        {
            if (target.Role == Roles.Administrator && otherActiveAdministrators < 1)
                return "It is the last active administrator account.";
            return null;
        }

        private static string CourseList(IEnumerable<string> codes)
        {
            var list = codes.Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();
            if (list.Count == 0) return "unnamed";
            if (list.Count <= 3) return string.Join(", ", list);
            return string.Join(", ", list.Take(3)) + " and " + (list.Count - 3) + " more";
        }
    }
}