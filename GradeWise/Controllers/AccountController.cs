using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using GradeWise.Data;
using GradeWise.Helpers;
using GradeWise.Models;
using MongoDB.Driver;
using Roles = GradeWise.Models.Roles;

namespace GradeWise.Controllers
{
    public class AccountController : Controller
    {
        private const int TimeoutMinutes = 30;   // keep in sync with <forms timeout> in Web.config

        // GET: /Account/Login
        [AllowAnonymous]
        public ActionResult Login(string returnUrl)
        {
            if (User.Identity.IsAuthenticated)
            {
                if (string.IsNullOrEmpty(returnUrl))
                {
                    return RedirectToDashboard(CurrentRole());
                }

                ViewBag.Error = "Your account does not have access to that page. Sign in with a different account to continue.";
            }

            return View();
        }

        // POST: /Account/Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public ActionResult Login(string loginId, string password)
        {
            loginId = (loginId ?? string.Empty).Trim();
            ViewBag.LoginId = loginId;

            if (loginId.Length == 0 || string.IsNullOrEmpty(password))
            {
                ViewBag.Error = "Enter your student number or email and your password.";
                return View();
            }

            User user = null;
            try
            {
                var db = new MongoDbContext();
                var lowerLogin = loginId.ToLower();
                user = db.Users.Find(u => u.StudentNo.ToLower() == lowerLogin || u.Email.ToLower() == lowerLogin).FirstOrDefault();
            }
            catch (Exception)
            {
                // Handle connection errors gracefully
            }

            // Same message for "no such user" and "wrong password".
            // Use the per-user iteration count so upgraded hashes still verify.
            var passwordOk = user != null &&
                             PasswordHelper.Verify(password, user.PasswordHash, user.Salt, user.PasswordIterations);

            if (!passwordOk)
            {
                ViewBag.Error = "Incorrect ID/email or password.";
                return View();
            }

            if (!user.IsActive)
            {
                ViewBag.Error = "This account has been disabled. Contact your administrator.";
                return View();
            }

            if (!Roles.IsValid(user.Role))
            {
                ViewBag.Error = "This account has an invalid role. Contact your administrator.";
                return View();
            }

            // Record the successful sign-in for the DB team's audit trail.
            lock (SampleData.Sync)
            {
                user.LastLoginAt = DateTime.UtcNow;
            }

            SignIn(user);

            // Honor returnUrl if it is a safe local URL.
            if (Url.IsLocalUrl(Request.QueryString["ReturnUrl"]))
            {
                return Redirect(Request.QueryString["ReturnUrl"]);
            }

            return RedirectToDashboard(user.Role);
        }

        // GET: /Account/Logout
        public ActionResult Logout()
        {
            FormsAuthentication.SignOut();
            Session.Abandon();
            return RedirectToAction("Login", "Account");
        }

        // POST: /Account/ChangeName
        // The signed-in person's own display name. No password is asked (the name is low risk);
        // the ID number is the sign-in name and cannot be changed here.
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ChangeName(string newName)
        {
            var db = new MongoDbContext();
            var me = FindCurrentUser(db);
            if (me == null || !me.IsActive) return RedirectToAction("Logout");

            var typed = (newName ?? string.Empty).Trim();
            var errors = new List<string>();

            if (typed.Length == 0)
            {
                errors.Add("Enter your name.");
            }
            else if (typed.Any(char.IsControl))
            {
                errors.Add("Your name cannot contain control characters.");
            }

            // Collapse repeated spaces ("Ana   Cruz" becomes "Ana Cruz") before measuring and comparing.
            var saved = System.Text.RegularExpressions.Regex.Replace(typed, " {2,}", " ");

            if (errors.Count == 0)
            {
                if (saved.Length > 100) errors.Add("Your name must be 100 characters or fewer.");
                else if (string.Equals(saved, me.FullName, StringComparison.Ordinal)) errors.Add("That is already your name.");
            }

            if (errors.Count > 0)
            {
                TempData["EditInfoName"] = newName ?? string.Empty;
                return EditInfoFailed("name", errors, me);
            }

            db.Users.UpdateOne(u => u.Id == me.Id, Builders<User>.Update.Set(u => u.FullName, saved));

            TempData["Message"] = "Name updated.";
            return RedirectToDashboard(me.Role);
        }

        // POST: /Account/ChangeEmail
        // The student's own email only. The signed-in account is found from the sign-in cookie,
        // never from the form, so nobody can change somebody else's details.
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ChangeEmail(string newEmail, string currentPassword)
        {
            var db = new MongoDbContext();
            var me = FindCurrentUser(db);
            if (me == null || !me.IsActive) return RedirectToAction("Logout");

            newEmail = (newEmail ?? string.Empty).Trim();
            var errors = new List<string>();

            if (newEmail.Length == 0) errors.Add("Enter your new email address.");
            else if (!ValidationHelper.IsValidEmail(newEmail)) errors.Add("Enter a valid email address.");
            else if (string.Equals(newEmail, me.Email, StringComparison.OrdinalIgnoreCase)) errors.Add("That is already your email address.");

            AddPasswordError(errors, me, currentPassword);

            // Only reveal that an address is taken once the password is right.
            if (errors.Count == 0)
            {
                var lower = newEmail.ToLower();
                var taken = db.Users.Find(u => u.Email.ToLower() == lower && u.Id != me.Id).FirstOrDefault();
                if (taken != null) errors.Add("That email is already used by another account.");
            }

            if (errors.Count > 0)
            {
                TempData["EditInfoEmail"] = newEmail;
                return EditInfoFailed("email", errors, me);
            }

            // Stored in lower case, the same way the Admin screen creates accounts.
            var saved = newEmail.ToLowerInvariant();
            db.Users.UpdateOne(u => u.Id == me.Id, Builders<User>.Update.Set(u => u.Email, saved));

            TempData["Message"] = "Email updated.";
            return RedirectToDashboard(me.Role);
        }

        // POST: /Account/ChangePassword
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            var db = new MongoDbContext();
            var me = FindCurrentUser(db);
            if (me == null || !me.IsActive) return RedirectToAction("Logout");

            var errors = new List<string>();

            AddPasswordError(errors, me, currentPassword);

            var problem = ValidationHelper.PasswordProblem(newPassword);
            if (problem != null)
            {
                errors.Add(problem);
            }
            else
            {
                if (newPassword == currentPassword) errors.Add("The new password must be different from your current password.");
                if (newPassword != confirmPassword) errors.Add("The new password and its confirmation do not match.");
            }

            if (errors.Count > 0) return EditInfoFailed("password", errors, me);

            string salt;
            int iterations;
            var hash = PasswordHelper.CreateHash(newPassword, out salt, out iterations);

            var update = Builders<User>.Update
                .Set(u => u.PasswordHash, hash)
                .Set(u => u.Salt, salt)
                .Set(u => u.PasswordIterations, iterations)
                .Set(u => u.MustChangePassword, false);
            db.Users.UpdateOne(u => u.Id == me.Id, update);

            TempData["Message"] = "Password updated.";
            return RedirectToDashboard(me.Role);
        }

        // ---------- helpers ----------

        /// <summary>The account behind the sign-in cookie (its name is the StudentNo), or null.</summary>
        private User FindCurrentUser(MongoDbContext db)
        {
            try
            {
                var name = (User.Identity.Name ?? string.Empty).ToLower();
                return db.Users.Find(u => u.StudentNo.ToLower() == name || u.Email.ToLower() == name).FirstOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void AddPasswordError(List<string> errors, User me, string currentPassword)
        {
            if (string.IsNullOrEmpty(currentPassword))
            {
                errors.Add("Enter your current password.");
            }
            else if (!PasswordHelper.Verify(currentPassword, me.PasswordHash, me.Salt, me.PasswordIterations))
            {
                errors.Add("Your current password is incorrect.");
            }
        }

        /// <summary>Keeps the problems for one request and sends the student back to the dashboard, where the layout reopens the modal.</summary>
        private ActionResult EditInfoFailed(string form, List<string> errors, User me)
        {
            TempData["EditInfoErrors"] = errors;
            TempData["EditInfoForm"] = form;
            return RedirectToDashboard(me.Role);
        }

        public static string DashboardController(string role)
        {
            switch (role)
            {
                case Roles.Administrator: return "Admin";
                case Roles.Faculty: return "Faculty";
                default: return "Student";
            }
        }

        private ActionResult RedirectToDashboard(string role)
        {
            return RedirectToAction("Index", DashboardController(role));
        }

        private string CurrentRole()
        {
            if (User.IsInRole(Roles.Administrator)) return Roles.Administrator;
            if (User.IsInRole(Roles.Faculty)) return Roles.Faculty;
            return Roles.Student;
        }

        private void SignIn(User user)
        {
            var now = DateTime.Now;
            var ticket = new FormsAuthenticationTicket(
                1,
                user.StudentNo,
                now,
                now.AddMinutes(TimeoutMinutes),
                false,
                user.Role + "|" + user.FullName);

            var cookie = new HttpCookie(FormsAuthentication.FormsCookieName, FormsAuthentication.Encrypt(ticket))
            {
                HttpOnly = true
            };
            Response.Cookies.Add(cookie);
        }
    }
}