using System;

namespace GradeWise.Models
{
    /// <summary>The three roles in GradeWise. Stored as plain strings.</summary>
    public static class Roles
    {
        public const string Student = "Student";
        public const string Faculty = "Faculty";
        public const string Administrator = "Administrator";

        public static readonly string[] All = { Administrator, Faculty, Student };

        public static bool IsValid(string role)
        {
            return role == Administrator || role == Faculty || role == Student;
        }
    }

    /// <summary>A user account.</summary>
    public class User
    {
        public string Id { get; set; }

        /// <summary>Student number or username used to sign in. Unique, case-insensitive.</summary>
        public string StudentNo { get; set; }

        public string FullName { get; set; }

        public string Email { get; set; }

        /// <summary>PBKDF2 hash. Never store plain passwords.</summary>
        public string PasswordHash { get; set; }

        public string Salt { get; set; }

        /// <summary>One of the values in <see cref="Roles"/>.</summary>
        public string Role { get; set; }

        /// <summary>False = account disabled by admin; sign-in is refused.</summary>
        public bool IsActive { get; set; } = true;

        // ---------- auth audit fields (required for DB team / Sprint 2) ----------

        /// <summary>When the account was created (UTC). Set once.</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Last successful sign-in (UTC). Null until the first login.</summary>
        public DateTime? LastLoginAt { get; set; }

        /// <summary>
        /// True until the user has changed the temporary password an admin generated.
        /// Sprint 2 will force a change-password screen when this is true.
        /// </summary>
        public bool MustChangePassword { get; set; }

        /// <summary>
        /// PBKDF2 iteration count used to produce PasswordHash. Stored per-user so the
        /// hash can be upgraded later without invalidating old accounts.
        /// </summary>
        public int PasswordIterations { get; set; }
    }
}