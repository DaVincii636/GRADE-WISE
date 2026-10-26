using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using GradeWise.Data;
using GradeWise.Models;
using GradeWise.Services;

namespace GradeWise.Controllers
{
    /// <summary>
    /// Faculty module: courses, enrollment, grading categories and score entry.
    /// A faculty member can only open the courses they own (the service checks this on every call).
    /// All changes are POST with an anti-forgery token and live in memory (SampleData).
    /// </summary>
    [Authorize(Roles = Roles.Faculty)]
    public class FacultyController : Controller
    {
        // ---------- My courses ----------

        // GET: /Faculty
        public ActionResult Index()
        {
            return View(CourseService.GetCourseList(CurrentFacultyId()));
        }

        // GET: /Faculty/CreateCourse
        public ActionResult CreateCourse()
        {
            return View(CourseService.NewCourseForm());
        }

        // POST: /Faculty/CreateCourse
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CreateCourse(string code, string name, string term)
        {
            List<string> errors;
            var id = CourseService.CreateCourse(CurrentFacultyId(), code, name, term, out errors);

            if (id == null)
            {
                var form = CourseService.NewCourseForm();
                form.Code = code;
                form.Name = name;
                form.Term = term;
                form.Errors = errors;
                return View(form);
            }

            TempData["Message"] = "Course created. Next, set its grading categories and enroll students.";
            return RedirectToAction("Categories", new { id = id });
        }

        // ---------- One course: score entry ----------

        // GET: /Faculty/Course/{id}
        public ActionResult Course(string id)
        {
            var model = CourseService.GetDetail(id, CurrentFacultyId(), null);
            if (model == null) return HttpNotFound();

            return View(model);
        }

        // POST: /Faculty/SaveScores/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SaveScores(string id)
        {
            var facultyId = CurrentFacultyId();
            if (CourseService.GetDetail(id, facultyId, null) == null) return HttpNotFound();

            List<string> errors;
            if (!CourseService.SaveScores(id, facultyId, key => Request.Form[key], out errors))
            {
                // Show the page again with what was typed and the problems, nothing was saved.
                var model = CourseService.GetDetail(id, facultyId, key => Request.Form[key]);
                if (model == null) return HttpNotFound();
                model.Errors = errors;
                return View("Course", model);
            }

            TempData["Message"] = "Scores saved.";
            return RedirectToAction("Course", new { id = id });
        }

        // ---------- Enrollment ----------

        // GET: /Faculty/Enroll/{id}
        public ActionResult Enroll(string id)
        {
            var model = CourseService.GetEnrollment(id, CurrentFacultyId());
            if (model == null) return HttpNotFound();

            return View(model);
        }

        // POST: /Faculty/EnrollStudents/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EnrollStudents(string id)
        {
            var facultyId = CurrentFacultyId();
            if (CourseService.GetEnrollment(id, facultyId) == null) return HttpNotFound();

            var chosen = Request.Form.GetValues("studentIds") ?? new string[0];

            string error;
            var added = CourseService.Enroll(id, facultyId, chosen, out error);
            if (added == 0) TempData["Error"] = error;
            else TempData["Message"] = added == 1 ? "1 student enrolled." : added + " students enrolled.";

            return RedirectToAction("Enroll", new { id = id });
        }

        // POST: /Faculty/Unenroll/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Unenroll(string id, string studentId)
        {
            var facultyId = CurrentFacultyId();
            if (CourseService.GetEnrollment(id, facultyId) == null) return HttpNotFound();

            var removed = CourseService.Unenroll(id, facultyId, studentId);
            if (removed == null) TempData["Error"] = "That student is not enrolled in this course.";
            else TempData["Message"] = removed + " was removed from the course along with their scores.";

            return RedirectToAction("Enroll", new { id = id });
        }

        // ---------- Categories and weights ----------

        // GET: /Faculty/Categories/{id}
        public ActionResult Categories(string id)
        {
            var model = CourseService.GetCategories(id, CurrentFacultyId());
            if (model == null) return HttpNotFound();

            return View(model);
        }

        // POST: /Faculty/Categories/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Categories(string id, FormCollection form)
        {
            var facultyId = CurrentFacultyId();
            if (CourseService.GetCategories(id, facultyId) == null) return HttpNotFound();

            var originals = form.GetValues("originals") ?? new string[0];
            var names = form.GetValues("names") ?? new string[0];
            var weights = form.GetValues("weights") ?? new string[0];

            List<string> errors;
            if (!CourseService.SaveCategories(id, facultyId, originals, names, weights, out errors))
            {
                var model = CourseService.EchoCategories(id, facultyId, originals, names, weights, errors);
                if (model == null) return HttpNotFound();
                return View(model);
            }

            TempData["Message"] = "Grading categories saved.";
            return RedirectToAction("Course", new { id = id });
        }

        // ---------- helpers ----------

        /// <summary>Id of the signed-in faculty member (looked up by sign-in name, like the rest of the app).</summary>
        private string CurrentFacultyId()
        {
            var me = SampleData.FindByLogin(User.Identity.Name);
            return me == null ? null : me.Id;
        }
    }
}
