using System;
using System.Net.Mail;

namespace GradeWise.Helpers
{
    /// <summary>
    /// Small input checks shared by the Admin and Account screens, so both apply the same rules.
    /// </summary>
    public static class ValidationHelper
    {
        public const int MaxEmailLength = 254;
        public const int MinPasswordLength = 8;
        public const int MaxPasswordLength = 128;

        /// <summary>True when the text is a plain email address such as name@school.edu.</summary>
        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email) || email.Length > MaxEmailLength) return false;

            try
            {
                return new MailAddress(email).Address == email;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>
        /// Returns a message when the password is not allowed, or null when it is fine.
        /// Rule: 8 to 128 characters, with at least one letter and at least one number.
        /// </summary>
        public static string PasswordProblem(string password)
        {
            if (string.IsNullOrEmpty(password)) return "Enter a new password.";

            if (password.Length > MaxPasswordLength)
            {
                return "The new password must be " + MaxPasswordLength + " characters or fewer.";
            }

            var hasLetter = false;
            var hasDigit = false;
            foreach (var c in password)
            {
                if (char.IsLetter(c)) hasLetter = true;
                else if (char.IsDigit(c)) hasDigit = true;
            }

            if (password.Length < MinPasswordLength || !hasLetter || !hasDigit)
            {
                return "The new password must be at least " + MinPasswordLength + " characters with at least one letter and one number.";
            }

            return null;
        }
    }
}
