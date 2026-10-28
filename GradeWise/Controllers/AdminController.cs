using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
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

            ViewBag.CurrentTerm = current == null ? null : current.Name;
            ViewBag.MeLogin = User.Identity.Name;
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
            else if (!IsValidEmail(email)) errors.Add("Enter a valid email address.");

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

        private static bool IsValidEmail(string email)
        {
            try
            {
                return new MailAddress(email).Address == email;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}