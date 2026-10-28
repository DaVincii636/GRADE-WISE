using System;
using System.Web.Mvc;
using GradeWise.Data;
using GradeWise.Models;
using GradeWise.Services;
using MongoDB.Driver;

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
            var me = CurrentStudentUser();
            if (me == null) return new HttpUnauthorizedResult();

            return View(StudentService.GetDashboard(me.Id, id));
        }

        // GET: /Student/Summary  or  /Student/Summary/{courseId}  (printable page)
        public ActionResult Summary(string id)
        {
            var me = CurrentStudentUser();
            if (me == null) return new HttpUnauthorizedResult();

            return View(StudentService.GetDashboard(me.Id, id));
        }

        // GET: /Student/WhatIf  or  /Student/WhatIf/{courseId}
        public ActionResult WhatIf(string id)
        {
            var me = CurrentStudentUser();
            if (me == null) return new HttpUnauthorizedResult();

            return View(StudentService.GetWhatIf(me.Id, id));
        }

        // ---------- helpers ----------

        private User CurrentStudentUser()
        {
            try
            {
                var db = new MongoDbContext();
                var identityName = User.Identity.Name.ToLower();
                return db.Users.Find(u => u.StudentNo.ToLower() == identityName || u.Email.ToLower() == identityName).FirstOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}