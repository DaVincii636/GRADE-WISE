using System.Web.Mvc;
using GradeWise.Models;

namespace GradeWise.Controllers
{
    [Authorize(Roles = Roles.Student)]
    public class StudentController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }
    }
}
