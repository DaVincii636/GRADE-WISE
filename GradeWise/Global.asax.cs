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
                user = db.Users.Find(u => u.StudentNo.ToLower() == ticket.Name.ToLower()).FirstOrDefault();
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

            Context.User = new GenericPrincipal(new FormsIdentity(ticket), new[] { user.Role });
        }
    }
}