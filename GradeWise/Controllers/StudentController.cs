using System.Web.Mvc;

namespace GradeWise.Controllers
{
    public class StudentController : Controller
    {
        public ActionResult Index()
        {
            // Static for now. Phase 3 replaces these with the signed-in user.
            ViewBag.UserName = "Demo Student";
            ViewBag.Role = "Student";
            return View();
        }
    }
}
