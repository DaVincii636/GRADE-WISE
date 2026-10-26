using System;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;
using System.Web.Helpers;
using System.Web.Optimization;
using System.Web.Routing;
using System.Web.Security;
using GradeWise.Data;

namespace GradeWise
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            // Our sign-in identity has no NameIdentifier claim, so tell the anti-forgery check to use the user name.
            AntiForgeryConfig.SuppressIdentityHeuristicChecks = true;
        }

        /// <summary>
        /// Forms authentication only gives us the user name. Look up the current role of that
        /// account so [Authorize(Roles = "...")] and User.IsInRole(...) work.
        /// </summary>
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

            // Look the account up on every request, so a disabled or removed account is signed out
            // straight away and a role change by the admin takes effect without signing in again.
            var user = SampleData.FindByLogin(ticket.Name);
            if (user == null || !user.IsActive)
            {
                Context.User = new GenericPrincipal(new GenericIdentity(string.Empty), new string[0]);
                return;
            }

            Context.User = new GenericPrincipal(new FormsIdentity(ticket), new[] { user.Role });
        }
    }
}
