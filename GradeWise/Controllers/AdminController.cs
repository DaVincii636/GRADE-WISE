using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using GradeWise.Data;
using GradeWise.Helpers;
using GradeWise.Models;
using MongoDB.Driver;
using Roles = GradeWise.Models.Roles;

namespace GradeWise.Controllers
{
    /// <summary>
    /// Admin module: user accounts and academic terms backed by MongoDB Atlas.
    /// </summary>
    [Authorize(Roles = Roles.Administrator)]
    public class AdminController : Controller
    {
        private static readonly string[] AllRoles = { Roles.Administrator, Roles.Faculty, Roles.Student };

        // Letters, digits, dot, dash, underscore. No "@" so an ID can never be mistaken for an email at login.
        private static readonly Regex LoginIdPattern = new Regex(@"^[A-Za-z0-9._-]{1,50}$");

        // ---------- Users ----------

        // GET: /Admin
        public ActionResult Index()
        {
            var db = new MongoDbContext();

            var users = db.Users.Find(_ => true).ToList()
                .OrderBy(u => Array.IndexOf(AllRoles, u.Role))
                .ThenBy(u => u.FullName)
                .ToList();

            var current = db.Terms.Find(t => t.IsCurrent).FirstOrDefault();

            // Why each account cannot be deleted (missing = can be deleted), so the Edit modal can say so
            // before the admin tries. DeleteUser checks the same rules again on the server.
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

        // GET: /Admin/CreateUser
        public ActionResult CreateUser()
        {
            ViewBag.Role = Roles.Student;
            return View();
        }

        // POST: /Admin/CreateUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CreateUser(string fullName, string loginId, string email, string role)
        {
            fullName = (fullName ?? string.Empty).Trim();
            loginId = (loginId ?? string.Empty).Trim();
            email = (email ?? string.Empty).Trim().ToLowerInvariant();

            var errors = new List<string>();

            if (fullName.Length == 0) errors.Add("Full name is required.");
            else if (fullName.Length > 100) errors.Add("Full name must be 100 characters or fewer.");

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
                if (existingUser != null)
                    errors.Add("That student number or username is already taken.");

                var existingEmail = db.Users.Find(u => u.Email.ToLower() == email.ToLower()).FirstOrDefault();
                if (existingEmail != null)
                    errors.Add("That email is already used by another account.");

                if (errors.Count == 0)
                {
                    tempPassword = PasswordHelper.GenerateTemporaryPassword();
                    string salt;
                    var hash = PasswordHelper.CreateHash(tempPassword, out salt);

                    var newUser = new User
                    {
                        Id = "u-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        StudentNo = loginId,
                        FullName = fullName,
                        Email = email,
                        PasswordHash = hash,
                        Salt = salt,
                        Role = role,
                        IsActive = true
                    };

                    db.Users.InsertOne(newUser);
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

            TempData["Message"] = "Account created for " + fullName + " (" + loginId + ").";
            TempData["TempPassword"] = tempPassword;
            TempData["TempPasswordFor"] = fullName + " (" + loginId + ")";
            return RedirectToAction("Index");
        }

        // POST: /Admin/ChangeRole/{id}
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

            if (target == null)
            {
                TempData["Error"] = "That user no longer exists.";
            }
            else if (IsMe(target))
            {
                TempData["Error"] = "You cannot change your own role. Ask another administrator.";
            }
            else if (target.Role == role)
            {
                TempData["Message"] = target.FullName + " is already " + role + ".";
            }
            else
            {
                var update = Builders<User>.Update.Set(u => u.Role, role);
                db.Users.UpdateOne(u => u.Id == id, update);
                TempData["Message"] = target.FullName + " is now " + role + ".";
            }

            return RedirectToAction("Index");
        }

        // POST: /Admin/SetActive/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SetActive(string id, bool active)
        {
            var db = new MongoDbContext();
            var target = db.Users.Find(u => u.Id == id).FirstOrDefault();

            if (target == null)
            {
                TempData["Error"] = "That user no longer exists.";
            }
            else if (IsMe(target))
            {
                TempData["Error"] = "You cannot disable your own account.";
            }
            else
            {
                var update = Builders<User>.Update.Set(u => u.IsActive, active);
                db.Users.UpdateOne(u => u.Id == id, update);
                TempData["Message"] = target.FullName + (active ? " can sign in again." : " has been disabled and can no longer sign in.");
            }

            return RedirectToAction("Index");
        }

        // POST: /Admin/ResetPassword/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ResetPassword(string id)
        {
            var db = new MongoDbContext();
            var target = db.Users.Find(u => u.Id == id).FirstOrDefault();

            if (target == null)
            {
                TempData["Error"] = "That user no longer exists.";
            }
            else
            {
                var tempPassword = PasswordHelper.GenerateTemporaryPassword();
                string salt;
                var hash = PasswordHelper.CreateHash(tempPassword, out salt);

                var update = Builders<User>.Update
                    .Set(u => u.PasswordHash, hash)
                    .Set(u => u.Salt, salt);

                db.Users.UpdateOne(u => u.Id == id, update);

                TempData["Message"] = "Password reset for " + target.FullName + ".";
                TempData["TempPassword"] = tempPassword;
                TempData["TempPasswordFor"] = target.FullName + " (" + target.StudentNo + ")";
            }

            return RedirectToAction("Index");
        }

        // POST: /Admin/DeleteUser/{id}
        //
        // Permanently deletes an account, but only when that leaves no orphaned or inconsistent data:
        //   - Refused for your own account, when the typed ID does not match, and for the last active administrator.
        //   - Refused when the account owns courses (Courses.FacultyId): the courses would lose their faculty.
        //   - Refused when the account has academic records: any recorded score (Grades) or any progress
        //     snapshot (Snapshots). Those are kept; the admin is told to disable the account instead.
        //   - Otherwise the account is removed from course rosters (Courses.StudentIds), its empty grade
        //     records (created automatically at enrollment, no scores) and its notifications are deleted,
        //     and then the user document is deleted.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteUser(string id, string confirmLoginId)
        {
            var db = new MongoDbContext();
            var target = string.IsNullOrEmpty(id) ? null : db.Users.Find(u => u.Id == id).FirstOrDefault();

            if (target == null)
            {
                TempData["Error"] = "That user no longer exists.";
                return RedirectToAction("Index");
            }

            var label = target.FullName + " (" + target.StudentNo + ")";

            if (IsMe(target))
            {
                TempData["Error"] = "You cannot delete your own account. Ask another administrator.";
                return RedirectToAction("Index");
            }

            if (!string.Equals((confirmLoginId ?? string.Empty).Trim(), target.StudentNo, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "The account ID you typed does not match " + label + ". The account was not deleted.";
                return RedirectToAction("Index");
            }

            try
            {
                var otherActiveAdmins = db.Users.CountDocuments(u => u.Role == Roles.Administrator && u.IsActive && u.Id != target.Id);
                var lastAdmin = LastAdministratorReason(target, otherActiveAdmins);
                if (lastAdmin != null)
                {
                    TempData["Error"] = label + " cannot be deleted. " + lastAdmin;
                    return RedirectToAction("Index");
                }

                string reason;
                if (FindDeleteBlockers(db, new List<string> { target.Id }).TryGetValue(target.Id, out reason))
                {
                    TempData["Error"] = label + " cannot be deleted. " + reason;
                    return RedirectToAction("Index");
                }

                // 1. Take the account off every course roster (atomic per course, no read-modify-write).
                //    The roster course ids are remembered so the enrollments can be put back if needed.
                var rosterFilter = Builders<Course>.Filter.AnyEq(c => c.StudentIds, target.Id);
                var rosterCourseIds = db.Courses.Find(rosterFilter).ToList().Select(c => c.Id).ToList();
                var unenrolled = rosterCourseIds.Count == 0 ? 0 : db.Courses.UpdateMany(
                    Builders<Course>.Filter.In(c => c.Id, rosterCourseIds) & rosterFilter,
                    Builders<Course>.Update.Pull(c => c.StudentIds, target.Id)).ModifiedCount;

                // A score saved for this student in the moment between the check above and the roster
                // update would make the delete unsafe after all: restore every enrollment and stop.
                var gradedNow = db.Grades.CountDocuments(GradedRecordsOf(new List<string> { target.Id })) > 0;
                var hasSnapshots = db.Snapshots.CountDocuments(s => s.StudentId == target.Id) > 0;
                if (gradedNow || hasSnapshots)
                {
                    if (rosterCourseIds.Count > 0)
                    {
                        db.Courses.UpdateMany(
                            Builders<Course>.Filter.In(c => c.Id, rosterCourseIds),
                            Builders<Course>.Update.AddToSet(c => c.StudentIds, target.Id));
                    }
                    TempData["Error"] = label + " cannot be deleted because grades were just recorded for this account. The account and its enrollments were kept.";
                    return RedirectToAction("Index");
                }

                // 2. Empty grade records only (never one with a recorded score).
                var emptyRecords = Builders<GradeRecord>.Filter.Eq(g => g.StudentId, target.Id)
                    & Builders<GradeRecord>.Filter.Not(Builders<GradeRecord>.Filter.ElemMatch(g => g.Scores, s => s.Score != null));
                db.Grades.DeleteMany(emptyRecords);

                // 3. The account's own notifications (messages to this user, not academic records).
                db.Notifications.DeleteMany(n => n.StudentId == target.Id);

                // 4. The account itself.
                var deleted = db.Users.DeleteOne(u => u.Id == target.Id).DeletedCount;
                if (deleted != 1)
                {
                    TempData["Error"] = "That user no longer exists.";
                    return RedirectToAction("Index");
                }

                TempData["Message"] = "Account deleted: " + label + "."
                    + (unenrolled > 0
                        ? " Also removed from " + unenrolled + (unenrolled == 1 ? " course" : " courses") + " with no recorded grades."
                        : string.Empty);
            }
            catch (Exception ex) when (ex is MongoException || ex is TimeoutException)
            {
                TempData["Error"] = "The database could not be reached, so " + label + " may not have been deleted. Check the list and try again.";
            }

            return RedirectToAction("Index");
        }

        // ---------- Academic terms ----------

        // GET: /Admin/Terms
        public ActionResult Terms()
        {
            var db = new MongoDbContext();
            var terms = db.Terms.Find(_ => true).ToList();
            return View(terms);
        }

        // POST: /Admin/AddTerm
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddTerm(string name)
        {
            name = (name ?? string.Empty).Trim();

            if (name.Length == 0)
            {
                TempData["Error"] = "Enter a term name, for example \"1st Semester, S.Y. 2027-2028\".";
            }
            else if (name.Length > 80)
            {
                TempData["Error"] = "Term name must be 80 characters or fewer.";
            }
            else
            {
                var db = new MongoDbContext();
                var existingTerm = db.Terms.Find(t => t.Name.ToLower() == name.ToLower()).FirstOrDefault();

                if (existingTerm != null)
                {
                    TempData["Error"] = "That term already exists.";
                }
                else
                {
                    var newTerm = new AcademicTerm
                    {
                        Id = "t-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        Name = name,
                        IsCurrent = false
                    };

                    db.Terms.InsertOne(newTerm);
                    TempData["Message"] = "Term added: " + name + ".";
                }
            }

            return RedirectToAction("Terms");
        }

        // POST: /Admin/SetCurrentTerm/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SetCurrentTerm(string id)
        {
            var db = new MongoDbContext();
            var chosen = db.Terms.Find(t => t.Id == id).FirstOrDefault();

            if (chosen == null)
            {
                TempData["Error"] = "That term no longer exists.";
            }
            else
            {
                // Unset IsCurrent for all terms
                var unsetUpdate = Builders<AcademicTerm>.Update.Set(t => t.IsCurrent, false);
                db.Terms.UpdateMany(_ => true, unsetUpdate);

                // Set IsCurrent to true for the chosen term
                var setUpdt = Builders<AcademicTerm>.Update.Set(t => t.IsCurrent, true);
                db.Terms.UpdateOne(t => t.Id == id, setUpdt);

                TempData["Message"] = chosen.Name + " is now the current term.";
            }

            return RedirectToAction("Terms");
        }

        // ---------- helpers ----------

        private bool IsMe(User target)
        {
            return string.Equals(target.StudentNo, User.Identity.Name, StringComparison.OrdinalIgnoreCase);
        }

        // Grade records of these users that hold at least one recorded score.
        private static FilterDefinition<GradeRecord> GradedRecordsOf(List<string> userIds)
        {
            return Builders<GradeRecord>.Filter.In(g => g.StudentId, userIds)
                & Builders<GradeRecord>.Filter.ElemMatch(g => g.Scores, s => s.Score != null);
        }

        /// <summary>
        /// For each of the given users that must not be deleted, the reason why (users that can be deleted are
        /// left out). Blocks: owning courses, any recorded score, any progress snapshot. Used by Index (to explain
        /// in the Edit modal) and by DeleteUser (to enforce), so both always apply the same rules.
        /// </summary>
        private static Dictionary<string, string> FindDeleteBlockers(MongoDbContext db, List<string> userIds)
        {
            var reasons = new Dictionary<string, List<string>>();
            Action<string, string> add = (userId, text) =>
            {
                List<string> list;
                if (!reasons.TryGetValue(userId, out list)) reasons[userId] = list = new List<string>();
                list.Add(text);
            };

            if (userIds.Count > 0)
            {
                // Courses this user teaches.
                var owned = db.Courses.Find(Builders<Course>.Filter.In(c => c.FacultyId, userIds)).ToList();
                foreach (var group in owned.GroupBy(c => c.FacultyId))
                {
                    var count = group.Count();
                    add(group.Key, "They own " + count + (count == 1 ? " course" : " courses") + " (" + CourseList(group.Select(c => c.Code)) + ")," +
                        " which would be left without a faculty member.");
                }

                // Recorded scores.
                var graded = db.Grades.Find(GradedRecordsOf(userIds)).ToList();
                if (graded.Count > 0)
                {
                    var courseIds = graded.Select(g => g.CourseId).Distinct().ToList();
                    var codes = db.Courses.Find(Builders<Course>.Filter.In(c => c.Id, courseIds)).ToList()
                        .ToDictionary(c => c.Id, c => c.Code);
                    foreach (var group in graded.GroupBy(g => g.StudentId))
                    {
                        var names = group.Select(g => g.CourseId).Distinct()
                            .Select(cid => codes.ContainsKey(cid) ? codes[cid] : "a removed course");
                        var count = group.Select(g => g.CourseId).Distinct().Count();
                        add(group.Key, "They have recorded grades in " + count + (count == 1 ? " course" : " courses") + " (" + CourseList(names) + "), and grades are academic records that must be kept.");
                    }
                }

                // Progress history (kept only once a student has grades).
                var withHistory = db.Snapshots.Distinct(s => s.StudentId, Builders<ScoreSnapshot>.Filter.In(s => s.StudentId, userIds)).ToList();
                foreach (var userId in withHistory)
                {
                    if (!reasons.ContainsKey(userId)) add(userId, "They have a saved grade history, which is an academic record that must be kept.");
                }
            }

            return reasons.ToDictionary(
                r => r.Key,
                r => string.Join(" ", r.Value) + " Disable the account instead to stop them from signing in.");
        }

        // Null when the account may be deleted as far as administrators go.
        private static string LastAdministratorReason(User target, long otherActiveAdministrators)
        {
            if (target.Role == Roles.Administrator && otherActiveAdministrators < 1)
                return "It is the last active administrator account. Make another administrator first.";
            return null;
        }

        // "IT101, IT202, IT303 and 2 more"
        private static string CourseList(IEnumerable<string> codes)
        {
            var list = codes.Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();
            if (list.Count == 0) return "unnamed";
            if (list.Count <= 3) return string.Join(", ", list);
            return string.Join(", ", list.Take(3)) + " and " + (list.Count - 3) + " more";
        }
    }
}