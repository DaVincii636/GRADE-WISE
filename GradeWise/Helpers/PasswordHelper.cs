using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace GradeWise.Helpers
{
    /// <summary>
    /// Password hashing with PBKDF2 (Rfc2898DeriveBytes) and a random salt per user.
    /// Plain passwords are never stored: only the Base64 hash and salt are kept.
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

        private const string UpperLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // no I or O
        private const string LowerLetters = "abcdefghijkmnopqrstuvwxyz";   // no l
        private const string Digits = "23456789";                           // no 0 or 1

        private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

        /// <summary>
        /// Makes a random temporary password with at least one upper-case letter, one lower-case
        /// letter and one digit. Look-alike characters (0/O, 1/l/I) are left out so it is easy to read out.
        /// </summary>
        public static string GenerateTemporaryPassword(int length = 10)
        {
            var all = UpperLetters + LowerLetters + Digits;
            var chars = new List<char>
            {
                Pick(UpperLetters),
                Pick(LowerLetters),
                Pick(Digits)
            };
            while (chars.Count < length)
            {
                chars.Add(Pick(all));
            }

            // Shuffle so the three guaranteed characters are not always first.
            for (var i = chars.Count - 1; i > 0; i--)
            {
                var j = RandomIndex(i + 1);
                var tmp = chars[i];
                chars[i] = chars[j];
                chars[j] = tmp;
            }

            return new string(chars.ToArray());
        }

        private static char Pick(string set)
        {
            return set[RandomIndex(set.Length)];
        }

        /// <summary>Random number from 0 to max-1 without modulo bias.</summary>
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

        private static byte[] Derive(string password, byte[] salt)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(HashSize);
            }
        }
    }
}
