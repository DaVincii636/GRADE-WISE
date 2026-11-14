using System;
using System.Collections.Generic;
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
            return View(db.Terms.Find(_ => true).ToList());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddTerm(string name)
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
                    db.Terms.InsertOne(new AcademicTerm { Id = "t-" + Guid.NewGuid().ToString("N").Substring(0, 8), Name = name, IsCurrent = false });
                    TempData["Message"] = "Term added: " + name + ".";
                }
            }
            return RedirectToAction("Terms");
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