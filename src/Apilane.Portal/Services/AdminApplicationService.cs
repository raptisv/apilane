using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Api.V1.Mapping;
using Apilane.Portal.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class AdminApplicationService : IAdminApplicationService
    {
        private const string EntityName = "Application";

        private readonly ApplicationDbContext _dbContext;

        public AdminApplicationService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<AdminApplicationResponse>> GetAllAsync()
        {
            var applications = await _dbContext.Applications
                .AsNoTracking()
                .Include(x => x.Server)
                // The Razor page has no order: the order they were created.
                .OrderBy(x => x.ID)
                .ToListAsync();

            return applications.Select(x => x.ToAdminResponse()).ToList();
        }

        public async Task<AdminApplicationDetailResponse> GetAsync(string appToken)
        {
            var application = await _dbContext.Applications
                .AsNoTracking()
                .Include(x => x.Server)
                .Include(x => x.Entities)
                    .ThenInclude(x => x.Properties)
                .AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Token == appToken);

            // Exact match, whatever collation the database compares with.
            if (application is null || !string.Equals(application.Token, appToken, StringComparison.Ordinal))
            {
                throw PortalException.NotFound(EntityName);
            }

            return new AdminApplicationDetailResponse
            {
                Application = application.ToAdminResponse(),
                // By name and sorted in memory, like the entities of GET /applications/{appToken}/entities.
                Entities = application.Entities
                    .OrderBy(x => x.Name)
                    .Select(x => x.ToResponse(application, withProperties: true))
                    .ToList()
            };
        }
    }
}
