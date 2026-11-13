using System;
using System.Diagnostics;
using System.Net;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;
using System.Web.Helpers;
using System.Web.Optimization;
using System.Web.Routing;
using System.Web.Security;
using GradeWise.Data;
using GradeWise.Models;
using MongoDB.Driver;

namespace GradeWise
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            // Atlas requires TLS 1.2+. Set this before any MongoDB call.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            // Our sign-in identity has no NameIdentifier claim, so tell the anti-forgery check to use the user name.
            AntiForgeryConfig.SuppressIdentityHeuristicChecks = true;

            // Seed initial data into MongoDB Atlas if the database is empty
            try
            {
                MongoDbSeeder.Initialize();
            }
            catch (Exception ex)
            {
                // Don't take the whole site down if Atlas is unreachable.
                Trace.TraceError("MongoDB seeding failed: " + ex.Message);
            }
        }

        protected void Application_PostAuthenticateRequest(object sender, EventArgs e)
        {
            // Styles, scripts, bundles and the icon do not need to know who is signed in,
            // so do not spend a database round trip on them.
            var path = Request.AppRelativeCurrentExecutionFilePath ?? string.Empty;
            if (path.StartsWith("~/Content", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("~/Scripts", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("~/bundles", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("~/favicon", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var cookie = Request.Cookies[FormsAuthentication.FormsCookieName];
            if (cookie == null || string.IsNullOrEmpty(cookie.Value))
            {
                return;
            }

            FormsAuthenticationTicket ticket;
            try
            {
                ticket = FormsAuthentication.Decrypt(cookie.Value);
            }
            catch (Exception)
            {
                return;   // tampered or expired key: treat as signed out
            }

            if (ticket == null || ticket.Expired)
            {
                return;
            }

            // Look up the account in MongoDB Atlas on every request
            User user = null;
            try
            {
                var db = new MongoDbContext();
                var signedInAs = ticket.Name;

                // The sign-in name is saved exactly as stored, so this can use the StudentNo index.
                // The case-insensitive search below is only a fallback for an unusual ticket.
                user = db.Users.Find(u => u.StudentNo == signedInAs).FirstOrDefault()
                    ?? db.Users.Find(u => u.StudentNo.ToLower() == signedInAs.ToLower()).FirstOrDefault();
            }
            catch (Exception)
            {
                // Fallback or handle connection issues gracefully if needed
            }

            if (user == null || !user.IsActive)
            {
                Context.User = new GenericPrincipal(new GenericIdentity(string.Empty), new string[0]);
                return;
            }

            // Keep the account for the rest of this request, so the layout and controllers do not look it up again.
            Context.Items["gw.user"] = user;

            Context.User = new GenericPrincipal(new FormsIdentity(ticket), new[] { user.Role });
        }
    }
}