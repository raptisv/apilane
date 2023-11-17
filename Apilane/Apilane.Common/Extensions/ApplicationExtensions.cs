using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Common.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Apilane.Common.Extensions.ObjectTreeExtensions;

namespace Apilane.Common.Extensions
{
    public static class ApplicationExtensions
    {
        public static string GetConnectionstring(this DBWS_Application application, string filesPath)
        {
            return application.DatabaseType switch
            {
                (int)DatabaseType.SQLServer => application.ConnectionString ?? throw new Exception("ConnectionString cannot be empty"),
                (int)DatabaseType.MySQL => application.ConnectionString ?? throw new Exception("ConnectionString cannot be empty"),
                (int)DatabaseType.SQLLite => $"Data Source={application.Token.GetApplicationFileInfo(filesPath).FullName};Cache Size=2000;Version=3;FailIfMissing=True;foreign keys=true;",
                _ => throw new NotImplementedException(),
            };
        }

        public static DirectoryInfo GetRootDirectoryInfo(this string appToken, string filesPath)
        {
            return new DirectoryInfo(Path.Combine(filesPath, appToken));
        }

        public static string ApplicationEncrypt(this string appEncryptionKey, string str)
        {
            return Encryptor.Encrypt(str, Encryptor.Decrypt(appEncryptionKey, Globals.EncryptionKey));
        }

        public static FileInfo GetApplicationFileInfo(this string appToken, string filesPath)
        {
            return new FileInfo($"{Path.Combine(filesPath, appToken, appToken)}.db");
        }

        public static DirectoryInfo GetFilesRootDirectoryInfo(this string appToken, string filesPath)
        {
            return new DirectoryInfo(Path.Combine(filesPath, appToken, "Files"));
        }

        public static string GetDifferentiationPropertyName(this string differentiationEntity) => $"{differentiationEntity}_ID";

        public static (IList<GroupItem> Tree, IList<GroupItem> Flat) GroupEntitesByFKReferences(this DBWS_Application application)
        {
            // Generate entities ordered by referenced entities first, to avoid missing entities during constraint creation.
            var data = application.Entities
                .SelectMany(x => x.Constraints.Where(x => x.TypeID == (int)ConstraintType.ForeignKey)
                    .Select(c => new GroupItem() { ID = x.Name, ParentID = c.GetForeignKeyProperties().FKEntity }))
                .ToList();

            // Add differentiation Entity or Users as root without parent to start from
            var rootEntity = string.IsNullOrWhiteSpace(application.DifferentiationEntity) ? "Users" : application.DifferentiationEntity;
            data.Add(new GroupItem() { ID = rootEntity, ParentID = null });

            data = data.OrderBy(x => x.Level).DistinctBy(x => new { x.ID, x.ParentID }).ToList();
            return (data.BuildTree(), data);
        }
    }
}
