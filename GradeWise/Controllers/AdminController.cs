using System.Web.Mvc;
using GradeWise.Models;

namespace GradeWise.Controllers
{
    [Authorize(Roles = Roles.Administrator)]
    public class AdminController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }
    }
}
