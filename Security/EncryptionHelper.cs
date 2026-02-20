using System;
using System.Security.Cryptography;
using System.Text;

namespace Rally_to_ADO_Migration.Security
{
    /// <summary>
    /// Provides secure encryption and decryption functionality for sensitive data
    /// Uses AES encryption with a machine-specific key for enhanced security
    /// </summary>
    public static class EncryptionHelper
    {
        private static readonly byte[] AdditionalEntropy = { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 };

        /// <summary>
        /// Encrypts a plain text string using AES encryption
        /// </summary>
        /// <param name="plainText">The text to encrypt</param>
        /// <returns>Base64 encoded encrypted string</returns>
        public static string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            try
            {
                using (Aes aesAlg = Aes.Create())
                {
                    // Generate a key based on machine characteristics for security
                    aesAlg.Key = DeriveKeyFromMachine();
                    aesAlg.GenerateIV();

                    using (var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV))
                    {
                        byte[] plainTextBytes = Encoding.UTF8.GetBytes(plainText);
                        byte[] encryptedBytes = encryptor.TransformFinalBlock(plainTextBytes, 0, plainTextBytes.Length);

                        // Combine IV and encrypted data
                        byte[] result = new byte[aesAlg.IV.Length + encryptedBytes.Length];
                        Buffer.BlockCopy(aesAlg.IV, 0, result, 0, aesAlg.IV.Length);
                        Buffer.BlockCopy(encryptedBytes, 0, result, aesAlg.IV.Length, encryptedBytes.Length);

                        return Convert.ToBase64String(result);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to encrypt data", ex);
            }
        }

        /// <summary>
        /// Decrypts an encrypted string back to plain text
        /// </summary>
        /// <param name="encryptedText">Base64 encoded encrypted string</param>
        /// <returns>Decrypted plain text</returns>
        public static string DecryptString(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return string.Empty;

            try
            {
                byte[] fullCipher = Convert.FromBase64String(encryptedText);

                using (Aes aesAlg = Aes.Create())
                {
                    aesAlg.Key = DeriveKeyFromMachine();

                    // Extract IV
                    byte[] iv = new byte[aesAlg.IV.Length];
                    Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
                    aesAlg.IV = iv;

                    // Extract encrypted data
                    byte[] cipher = new byte[fullCipher.Length - iv.Length];
                    Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);

                    using (var decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV))
                    {
                        byte[] decryptedBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                        return Encoding.UTF8.GetString(decryptedBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to decrypt data", ex);
            }
        }

        /// <summary>
        /// Derives a consistent encryption key based on machine characteristics
        /// This provides security while ensuring the same key is generated on the same machine
        /// </summary>
        /// <returns>32-byte AES key</returns>
        private static byte[] DeriveKeyFromMachine()
        {
            // Use machine name and user name to generate a consistent key
            string machineInfo = Environment.MachineName + Environment.UserName + string.Join("", AdditionalEntropy);
            
            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(Encoding.UTF8.GetBytes(machineInfo));
            }
        }

        public static void SecurelyDisposeString(ref string sensitiveString)
        {
            if (sensitiveString != null)
            {
                // For .NET Framework 4.8, we'll just set to null
                // In .NET 5+ we could use SecureString or other methods
                sensitiveString = null;
            }
        }
    }
}