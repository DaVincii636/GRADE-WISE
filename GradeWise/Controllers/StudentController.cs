using System.Web.Mvc;
using GradeWise.Data;
using GradeWise.Models;
using GradeWise.Services;

namespace GradeWise.Controllers
{
    /// <summary>
    /// Student dashboard. A student only ever sees their own scores: the id comes from the
    /// signed-in account, never from the address, and the course must be one they are enrolled in.
    /// </summary>
    [Authorize(Roles = Roles.Student)]
    public class StudentController : Controller
    {
        // GET: /Student  or  /Student?id={courseId}
        public ActionResult Index(string id)
        {
            var me = SampleData.FindByLogin(User.Identity.Name);
            if (me == null) return new HttpUnauthorizedResult();

            return View(StudentService.GetDashboard(me.Id, id));
        }
    }
}
