using System.Web.Mvc;
using GradeWise.Models;

namespace GradeWise.Controllers
{
    /// <summary>The default route. It has no page of its own; it only sends people where they belong.</summary>
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            if (!User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Login", "Account");
            }

            var role = User.IsInRole(Roles.Administrator) ? Roles.Administrator
                     : User.IsInRole(Roles.Faculty) ? Roles.Faculty
                     : Roles.Student;

            return RedirectToAction("Index", AccountController.DashboardController(role));
        }
    }
}
