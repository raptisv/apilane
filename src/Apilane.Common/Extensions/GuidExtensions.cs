using System;
using System.Security.Cryptography;
using System.Text;

namespace Apilane.Common.Extensions
{
    public static class GuidExtensions
    {
        public static Guid ToGuid(this string input)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                return new Guid(hash);
            }
        }
    }
}
