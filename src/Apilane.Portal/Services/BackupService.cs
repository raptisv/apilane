using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class BackupService : IBackupService
    {
        public const string TempFilePrefix = "apilane-backup-";

        public const string AuditEntityType = "Database backup";
        public const string AuditEntityIdentifier = "Apilane.db";
        public const string AuditAction = "Downloaded";

        private readonly ApplicationDbContext _dbContext;
        private readonly IPortalAccessService _portalAccessService;

        public BackupService(
            ApplicationDbContext dbContext,
            IPortalAccessService portalAccessService)
        {
            _dbContext = dbContext;
            _portalAccessService = portalAccessService;
        }

        public async Task<Stream> CreateAsync()
        {
            var user = await _portalAccessService.GetCurrentUserAsync();

            var sourceConnectionString = _dbContext.Database.GetConnectionString()
                ?? throw new Exception("The Portal database has no connection string");

            // A new name for every download, so two downloads at the same time do not collide.
            var path = Path.Combine(Path.GetTempPath(), $"{TempFilePrefix}{Guid.NewGuid():N}.db");

            try
            {
                // The OS temp folder is shared on Linux, and SQLite would create the file readable by every
                // local user. Create it empty and owner-only first: SQLite keeps the mode of an existing file.
                if (!OperatingSystem.IsWindows())
                {
                    new FileStream(path, new FileStreamOptions
                    {
                        Mode = FileMode.CreateNew,
                        Access = FileAccess.Write,
                        UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                    }).Dispose();
                }

                // Not pooled: a pooled connection would keep the file open after the copy.
                var destinationConnectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

                await using (var source = new SqliteConnection(sourceConnectionString))
                await using (var destination = new SqliteConnection(destinationConnectionString))
                {
                    await source.OpenAsync();
                    await destination.OpenAsync();

                    // SQLite's online backup: a consistent copy while the Portal keeps using the database.
                    source.BackupDatabase(destination);
                }

                // The whole database, password hashes and application secrets included, leaves the
                // server: worth a line in the audit log. The line is not in the copy itself.
                _dbContext.AuditLogs.Add(new PortalAuditLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserId = user.Id,
                    UserEmail = user.Email ?? string.Empty,
                    AppID = null,
                    EntityType = AuditEntityType,
                    EntityIdentifier = AuditEntityIdentifier,
                    Action = AuditAction,
                    Changes = null
                });

                await _dbContext.SaveChangesAsync();

                // The file removes itself when the response has been sent and the stream is closed.
                return new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            }
            catch
            {
                File.Delete(path);
                throw;
            }
        }
    }
}
