using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Apilane.Common.Utilities
{
    public static class Encryptor
    {
        public static string Decrypt(this string? stringToDecrypt, string key)
        {
            if (string.IsNullOrWhiteSpace(stringToDecrypt))
            {
                return stringToDecrypt ?? string.Empty;
            }

            var desProvider = DES.Create();
            desProvider.Mode = CipherMode.ECB;
            desProvider.Padding = PaddingMode.PKCS7;
            desProvider.Key = Encoding.UTF8.GetBytes(key);
            using (MemoryStream stream = new MemoryStream(Convert.FromBase64String(stringToDecrypt)))
            {
                using (CryptoStream cs = new CryptoStream(stream, desProvider.CreateDecryptor(), CryptoStreamMode.Read))
                {
                    using (StreamReader sr = new StreamReader(cs, Encoding.UTF8))
                    {
                        return sr.ReadToEnd();
                    }
                }
            }
        }

        /// <summary>
        /// Try decrypt the string. On exception, returns null.
        /// </summary>
        public static bool TryDecrypt(this string? stringToDecrypt, string key, out string? output)
        {
            try
            {
                output = Decrypt(stringToDecrypt, key);
                return true;
            }
            catch
            {
                output = null;
                return false;
            }
        }

        public static string Encrypt(this string strText, string Key)
        {
            var desProvider = DES.Create();
            desProvider.Mode = CipherMode.ECB;
            desProvider.Padding = PaddingMode.PKCS7;
            desProvider.Key = Encoding.UTF8.GetBytes(Key);

            using (MemoryStream stream = new MemoryStream())
            {
                using (CryptoStream cs = new CryptoStream(stream, desProvider.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    byte[] data = Encoding.UTF8.GetBytes(strText);
                    cs.Write(data, 0, data.Length);
                    cs.FlushFinalBlock();
                    return Convert.ToBase64String(stream.ToArray());
                }
            }
        }
    }
}
