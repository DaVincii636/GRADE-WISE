using System.Web.Mvc;

namespace GradeWise.Controllers
{
    public class FacultyController : Controller
    {
        public ActionResult Index()
        {
            // Static for now. Phase 3 replaces these with the signed-in user.
            ViewBag.UserName = "Demo Faculty";
            ViewBag.Role = "Faculty";
            return View();
        }
    }
}
