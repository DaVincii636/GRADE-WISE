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
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            AntiForgeryConfig.SuppressIdentityHeuristicChecks = true;

            try
            {
                MongoDbSeeder.RestoreDemoPasswords();
            }
            catch (Exception ex)
            {
                Trace.TraceError("Restoring demo passwords failed: " + ex.Message);
            }

            System.Web.Hosting.HostingEnvironment.QueueBackgroundWorkItem(cancellationToken =>
            {
                try
                {
                    MongoDbSeeder.Initialize();
                }
                catch (Exception ex)
                {
                    Trace.TraceError("MongoDB seeding failed: " + ex.Message);
                }
            });
        }

        protected void Application_PostAuthenticateRequest(object sender, EventArgs e)
        {
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
                // FIX: Safely reset the user instead of leaving a half-authenticated state
                Context.User = new GenericPrincipal(new GenericIdentity(string.Empty), new string[0]);
                return;
            }

            if (ticket == null || ticket.Expired)
            {
                // FIX: Safely reset the user if the cookie is expired
                Context.User = new GenericPrincipal(new GenericIdentity(string.Empty), new string[0]);
                return;
            }

            User user = null;
            try
            {
                var db = new MongoDbContext();
                var signedInAs = ticket.Name;

                user = db.Users.Find(u => u.StudentNo == signedInAs).FirstOrDefault()
                    ?? db.Users.Find(u => u.StudentNo.ToLower() == signedInAs.ToLower()).FirstOrDefault();
            }
            catch (Exception)
            {
                // Ignore DB failure during auth, treat as unauthorized below
            }

            if (user == null || !user.IsActive)
            {
                Context.User = new GenericPrincipal(new GenericIdentity(string.Empty), new string[0]);
                return;
            }

            Context.Items["gw.user"] = user;
            Context.User = new GenericPrincipal(new FormsIdentity(ticket), new[] { user.Role });
        }
    }
}