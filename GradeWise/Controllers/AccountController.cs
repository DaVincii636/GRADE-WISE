using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using GradeWise.Data;
using GradeWise.Helpers;
using GradeWise.Models;
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

            var user = SampleData.FindByLoginOrEmail(loginId);

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

        // ---------- helpers ----------

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