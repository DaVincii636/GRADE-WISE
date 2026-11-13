using System;
using System.Linq;
using System.Web.Mvc;
using GradeWise.Data;
using GradeWise.Models;
using GradeWise.Services;
using MongoDB.Driver;

namespace GradeWise.Controllers
{
    /// <summary>
    /// The student's bell. The student id always comes from the signed-in account, never from the
    /// request, and every service call is limited to that student.
    /// </summary>
    [Authorize(Roles = Roles.Student)]
    public class NotificationsController : Controller
    {
        // Drawn by the layout: @Html.Action("Bell", "Notifications")
        [ChildActionOnly]
        public ActionResult Bell()
        {
            try
            {
                var me = CurrentStudentUser();
                if (me == null) return new EmptyResult();

                // One query instead of two: a student keeps at most MaxPerStudent messages, so the
                // unread count can be taken from the same list. Only the newest ones are shown.
                var all = NotificationService.ListFor(me.Id, NotificationService.MaxPerStudent);
                var model = new NotificationBellViewModel
                {
                    Items = all.Take(NotificationService.DefaultListLimit).ToList(),
                    Unread = all.Count(n => !n.IsRead)
                };
                return PartialView(model);
            }
            catch (Exception)
            {
                // A database problem must not break every page, so the bell just does not appear.
                return new EmptyResult();
            }
        }

        // POST: /Notifications/Read
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Read(string id)
        {
            return Run(me => NotificationService.MarkRead(id, me.Id));
        }

        // POST: /Notifications/Unread
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Unread(string id)
        {
            return Run(me => NotificationService.MarkUnread(id, me.Id));
        }

        // POST: /Notifications/ReadAll
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ReadAll()
        {
            return Run(me =>
            {
                NotificationService.MarkAllRead(me.Id);
                return true;
            });
        }

        // POST: /Notifications/Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(string id)
        {
            return Run(me => NotificationService.Delete(id, me.Id));
        }

        // POST: /Notifications/DeleteAll
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteAll()
        {
            return Run(me =>
            {
                NotificationService.DeleteAll(me.Id);
                return true;
            });
        }

        // ---------- helpers ----------

        /// <summary>Runs one change for the signed-in student and answers { ok, unread }.</summary>
        private ActionResult Run(Func<User, bool> change)
        {
            try
            {
                var me = CurrentStudentUser();
                if (me == null) return Json(new { ok = false, unread = 0 });

                var ok = change(me);
                return Json(new { ok = ok, unread = NotificationService.UnreadCount(me.Id) });
            }
            catch (Exception)
            {
                return Json(new { ok = false, unread = 0 });
            }
        }

        private User CurrentStudentUser()
        {
            // Global.asax already found the signed-in account for this request.
            var cached = HttpContext.Items["gw.user"] as User;
            if (cached != null) return cached;

            var db = new MongoDbContext();
            var identityName = User.Identity.Name.ToLower();
            return db.Users.Find(u => u.StudentNo.ToLower() == identityName || u.Email.ToLower() == identityName).FirstOrDefault();
        }
    }
}
