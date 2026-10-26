using System.Web.Mvc;
using GradeWise.Models;

namespace GradeWise.Controllers
{
    [Authorize(Roles = Roles.Faculty)]
    public class FacultyController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }
    }
}
