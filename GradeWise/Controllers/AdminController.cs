using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using GradeWise.Data;
using GradeWise.Helpers;
using GradeWise.Models;

namespace GradeWise.Controllers
{
    /// <summary>
    /// Admin module: user accounts and academic terms. Everything is kept in memory (SampleData),
    /// so changes are lost when the site restarts. All changes are POST with an anti-forgery token.
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
            List<User> users;
            AcademicTerm current;
            lock (SampleData.Sync)
            {
                users = SampleData.Users
                    .OrderBy(u => Array.IndexOf(AllRoles, u.Role))
                    .ThenBy(u => u.FullName)
                    .ToList();
                current = SampleData.Terms.FirstOrDefault(t => t.IsCurrent);
            }

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
                lock (SampleData.Sync)
                {
                    if (SampleData.Users.Any(u => string.Equals(u.StudentNo, loginId, StringComparison.OrdinalIgnoreCase)))
                        errors.Add("That student number or username is already taken.");
                    if (SampleData.Users.Any(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)))
                        errors.Add("That email is already used by another account.");

                    if (errors.Count == 0)
                    {
                        tempPassword = PasswordHelper.GenerateTemporaryPassword();
                        string salt;
                        var hash = PasswordHelper.CreateHash(tempPassword, out salt);

                        SampleData.Users.Add(new User
                        {
                            Id = "u-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                            StudentNo = loginId,
                            FullName = fullName,
                            Email = email,
                            PasswordHash = hash,
                            Salt = salt,
                            Role = role,
                            IsActive = true
                        });
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

            lock (SampleData.Sync)
            {
                var target = SampleData.Users.FirstOrDefault(u => u.Id == id);
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
                    target.Role = role;
                    TempData["Message"] = target.FullName + " is now " + role + ".";
                }
            }

            return RedirectToAction("Index");
        }

        // POST: /Admin/SetActive/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SetActive(string id, bool active)
        {
            lock (SampleData.Sync)
            {
                var target = SampleData.Users.FirstOrDefault(u => u.Id == id);
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
                    target.IsActive = active;
                    TempData["Message"] = target.FullName + (active ? " can sign in again." : " has been disabled and can no longer sign in.");
                }
            }

            return RedirectToAction("Index");
        }

        // POST: /Admin/ResetPassword/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ResetPassword(string id)
        {
            lock (SampleData.Sync)
            {
                var target = SampleData.Users.FirstOrDefault(u => u.Id == id);
                if (target == null)
                {
                    TempData["Error"] = "That user no longer exists.";
                }
                else
                {
                    var tempPassword = PasswordHelper.GenerateTemporaryPassword();
                    string salt;
                    target.PasswordHash = PasswordHelper.CreateHash(tempPassword, out salt);
                    target.Salt = salt;

                    TempData["Message"] = "Password reset for " + target.FullName + ".";
                    TempData["TempPassword"] = tempPassword;
                    TempData["TempPasswordFor"] = target.FullName + " (" + target.StudentNo + ")";
                }
            }

            return RedirectToAction("Index");
        }

        // ---------- Academic terms ----------

        // GET: /Admin/Terms
        public ActionResult Terms()
        {
            List<AcademicTerm> terms;
            lock (SampleData.Sync)
            {
                terms = SampleData.Terms.ToList();
            }

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
                lock (SampleData.Sync)
                {
                    if (SampleData.Terms.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        TempData["Error"] = "That term already exists.";
                    }
                    else
                    {
                        SampleData.Terms.Add(new AcademicTerm
                        {
                            Id = "t-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                            Name = name,
                            IsCurrent = false
                        });
                        TempData["Message"] = "Term added: " + name + ".";
                    }
                }
            }

            return RedirectToAction("Terms");
        }

        // POST: /Admin/SetCurrentTerm/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SetCurrentTerm(string id)
        {
            lock (SampleData.Sync)
            {
                var chosen = SampleData.Terms.FirstOrDefault(t => t.Id == id);
                if (chosen == null)
                {
                    TempData["Error"] = "That term no longer exists.";
                }
                else
                {
                    foreach (var t in SampleData.Terms)
                    {
                        t.IsCurrent = (t == chosen);
                    }
                    TempData["Message"] = chosen.Name + " is now the current term.";
                }
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
