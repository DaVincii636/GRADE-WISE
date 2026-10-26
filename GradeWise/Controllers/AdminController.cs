using System.Web.Mvc;

namespace GradeWise.Controllers
{
    public class AdminController : Controller
    {
        public ActionResult Index()
        {
            // Static for now. Phase 3 replaces these with the signed-in user.
            ViewBag.UserName = "Demo Administrator";
            ViewBag.Role = "Administrator";
            return View();
        }
    }
}
