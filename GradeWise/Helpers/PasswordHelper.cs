using System;
using System.Security.Cryptography;

namespace GradeWise.Helpers
{
    /// <summary>
    /// Password hashing with PBKDF2 (Rfc2898DeriveBytes) and a random salt per user.
    /// Plain passwords are never stored: only the Base64 hash and salt go in MongoDB.
    /// </summary>
    public static class PasswordHelper
    {
        private const int SaltSize = 16;        // 128-bit salt
        private const int HashSize = 32;        // 256-bit hash
        private const int Iterations = 100000;

        /// <summary>Hashes a password with a new random salt. Both results are Base64 strings.</summary>
        public static string CreateHash(string password, out string salt)
        {
            var saltBytes = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(saltBytes);
            }

            salt = Convert.ToBase64String(saltBytes);
            return Convert.ToBase64String(Derive(password, saltBytes));
        }

        /// <summary>Returns true when the password matches the stored hash and salt.</summary>
        public static bool Verify(string password, string storedHash, string storedSalt)
        {
            if (string.IsNullOrEmpty(password) ||
                string.IsNullOrEmpty(storedHash) ||
                string.IsNullOrEmpty(storedSalt))
            {
                return false;
            }

            byte[] expected;
            byte[] saltBytes;
            try
            {
                expected = Convert.FromBase64String(storedHash);
                saltBytes = Convert.FromBase64String(storedSalt);
            }
            catch (FormatException)
            {
                return false;
            }

            var actual = Derive(password, saltBytes);

            // Compare every byte so the time taken doesn't reveal where they differ.
            if (actual.Length != expected.Length) return false;
            var diff = 0;
            for (var i = 0; i < actual.Length; i++)
            {
                diff |= actual[i] ^ expected[i];
            }
            return diff == 0;
        }

        private static byte[] Derive(string password, byte[] salt)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(HashSize);
            }
        }
    }
}
