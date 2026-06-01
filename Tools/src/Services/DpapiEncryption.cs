using System;
using System.Security.Cryptography;
using System.Text;

namespace BlogTools.Services
{
    /// <summary>
    /// Helper for encrypting/decrypting API keys using Windows DPAPI.
    /// The encrypted data is scoped to the current user account.
    /// </summary>
    public static class DpapiEncryption
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("JekyllCli.AiCommit.v1");

        /// <summary>
        /// Encrypt a plain-text string. Returns Base64-encoded ciphertext,
        /// or empty string on null/empty input or failure.
        /// </summary>
        public static string Encrypt(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            try
            {
                var plainBytes = Encoding.UTF8.GetBytes(plainText);
                var cipherBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(cipherBytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Decrypt a Base64-encoded ciphertext. Returns the plain-text string,
        /// or empty string on null/empty input, corrupted data, or failure.
        /// </summary>
        public static string Decrypt(string? cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return string.Empty;

            try
            {
                var cipherBytes = Convert.FromBase64String(cipherText);
                var plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                // Corrupted or from a different user — return empty gracefully
                return string.Empty;
            }
        }
    }
}
