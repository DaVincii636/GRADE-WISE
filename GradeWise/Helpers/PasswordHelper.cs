using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace GradeWise.Helpers
{
    /// <summary>
    /// Password hashing with PBKDF2 (Rfc2898DeriveBytes) and a random salt per user.
    /// Plain passwords are never stored: only the Base64 hash and salt are kept.
    /// The iteration count is stored per-user so it can be raised later without
    /// invalidating existing accounts.
    /// </summary>
    public static class PasswordHelper
    {
        private const int SaltSize = 16;          // 128-bit salt
        private const int HashSize = 32;          // 256-bit hash
        public const int DefaultIterations = 100000;

        /// <summary>
        /// Hashes a password with a new random salt. Both results are Base64 strings.
        /// Returns the iteration count used so the caller can store it on the user.
        /// </summary>
        public static string CreateHash(string password, out string salt, out int iterations)
        {
            iterations = DefaultIterations;

            var saltBytes = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(saltBytes);
            }

            salt = Convert.ToBase64String(saltBytes);
            return Convert.ToBase64String(Derive(password, saltBytes, iterations));
        }

        /// <summary>Back-compat overload: uses DefaultIterations (existing callers keep working).</summary>
        public static string CreateHash(string password, out string salt)
        {
            int ignored;
            return CreateHash(password, out salt, out ignored);
        }

        /// <summary>
        /// Returns true when the password matches the stored hash and salt. The iteration
        /// count stored on the user is used, so an upgraded iteration count still verifies.
        /// </summary>
        public static bool Verify(string password, string storedHash, string storedSalt, int iterations)
        {
            if (string.IsNullOrEmpty(password) ||
                string.IsNullOrEmpty(storedHash) ||
                string.IsNullOrEmpty(storedSalt))
            {
                return false;
            }

            if (iterations <= 0) iterations = DefaultIterations;

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

            var actual = Derive(password, saltBytes, iterations);

            if (actual.Length != expected.Length) return false;
            var diff = 0;
            for (var i = 0; i < actual.Length; i++)
            {
                diff |= actual[i] ^ expected[i];
            }
            return diff == 0;
        }

        /// <summary>Back-compat overload: assumes DefaultIterations.</summary>
        public static bool Verify(string password, string storedHash, string storedSalt)
        {
            return Verify(password, storedHash, storedSalt, DefaultIterations);
        }

        // ----- temporary password generator (unchanged behavior) -----

        private const string UpperLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        private const string LowerLetters = "abcdefghijkmnopqrstuvwxyz";
        private const string Digits = "23456789";

        private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

        public static string GenerateTemporaryPassword(int length = 10)
        {
            var all = UpperLetters + LowerLetters + Digits;
            var chars = new List<char>
            {
                Pick(UpperLetters),
                Pick(LowerLetters),
                Pick(Digits)
            };
            while (chars.Count < length) chars.Add(Pick(all));

            for (var i = chars.Count - 1; i > 0; i--)
            {
                var j = RandomIndex(i + 1);
                var tmp = chars[i];
                chars[i] = chars[j];
                chars[j] = tmp;
            }
            return new string(chars.ToArray());
        }

        private static char Pick(string set) { return set[RandomIndex(set.Length)]; }

        private static int RandomIndex(int max)
        {
            var buffer = new byte[4];
            var limit = uint.MaxValue - (uint.MaxValue % (uint)max);
            uint value;
            do
            {
                Rng.GetBytes(buffer);
                value = BitConverter.ToUInt32(buffer, 0);
            } while (value >= limit);
            return (int)(value % (uint)max);
        }

        private static byte[] Derive(string password, byte[] salt, int iterations)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(HashSize);
            }
        }
    }
}