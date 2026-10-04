using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class AuditLogService : IAuditLogService
    {
        private readonly ApplicationDbContext _dbContext;

        public AuditLogService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<ListResponse<AuditLogEntryResponse>> QueryAsync(long? appId, PageQuery page)
        {
            var query = _dbContext.AuditLogs
                .AsNoTracking()
                .Where(x => x.AppID == appId);

            var total = await query.CountAsync();

            // A page number can be large enough to overflow an int; such a page is past the end anyway.
            var skip = (long)(page.Page - 1) * page.PageSize;

            if (skip >= total)
            {
                return new ListResponse<AuditLogEntryResponse>(new List<AuditLogEntryResponse>(), total);
            }

            // Entries saved together can share a timestamp; the ID keeps their order the same on every call.
            var logs = await query
                .OrderByDescending(x => x.Timestamp)
                .ThenByDescending(x => x.ID)
                .Skip((int)skip)
                .Take(page.PageSize)
                .ToListAsync();

            return new ListResponse<AuditLogEntryResponse>(logs.Select(ToResponse).ToList(), total);
        }

        private static AuditLogEntryResponse ToResponse(PortalAuditLog log)
        {
            return new AuditLogEntryResponse
            {
                ID = log.ID,
                // Stored as UTC; SQLite gives the value back without that mark.
                Timestamp = DateTime.SpecifyKind(log.Timestamp, DateTimeKind.Utc),
                UserId = log.UserId,
                UserEmail = log.UserEmail,
                EntityType = log.EntityType,
                EntityIdentifier = log.EntityIdentifier,
                Action = log.Action,
                Changes = ParseChanges(log.Changes)
            };
        }

        private static List<AuditChangeResponse> ParseChanges(string? changes)
        {
            if (string.IsNullOrWhiteSpace(changes))
            {
                return new List<AuditChangeResponse>();
            }

            try
            {
                var items = JsonSerializer.Deserialize<List<AuditChangeResponse?>>(changes);

                return (items ?? new List<AuditChangeResponse?>())
                    .OfType<AuditChangeResponse>()
                    // System.Text.Json writes a JSON null into Property although it is not nullable.
                    .Where(x => x.Property is not null)
                    .ToList();
            }
            catch (JsonException)
            {
                // Text that is not the expected JSON is shown as an entry without detail, like the Razor page does.
                return new List<AuditChangeResponse>();
            }
        }
    }
}
