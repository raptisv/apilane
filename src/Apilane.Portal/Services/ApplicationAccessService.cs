using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class ApplicationAccessService : IApplicationAccessService
    {
        private const string EntityName = "Application";

        private readonly ApplicationDbContext _dbContext;
        private readonly IPortalAccessService _portalAccessService;

        public ApplicationAccessService(
            ApplicationDbContext dbContext,
            IPortalAccessService portalAccessService)
        {
            _dbContext = dbContext;
            _portalAccessService = portalAccessService;
        }

        public async Task<List<DBWS_Application>> GetVisibleApplicationsAsync()
        {
            var user = await _portalAccessService.GetCurrentUserAsync();

            var applications = await Visible(_dbContext.Applications.AsNoTracking(), user).ToListAsync();

            // Sorted in memory: SQLite would order names by byte value ('Zeta' before 'alpha').
            return applications
                .OrderByDescending(x => x.UserID == user.Id)
                .ThenBy(x => x.Name)
                .ToList();
        }

        public async Task<DBWS_Application> GetApplicationAsync(string appToken, bool requireOwner = false)
        {
            var user = await _portalAccessService.GetCurrentUserAsync();

            var application = await FindAsync(Visible(_dbContext.Applications, user), appToken);

            if (requireOwner && application.UserID != user.Id)
            {
                throw PortalException.Forbidden();
            }

            return application;
        }

        public async Task<DBWS_Application> GetApplicationOrAnyForAdminAsync(string appToken)
        {
            if (!_portalAccessService.IsAdmin)
            {
                return await GetApplicationAsync(appToken);
            }

            return await FindAsync(WithIncludes(_dbContext.Applications), appToken);
        }

        public async Task<DBWS_Application> GetApplicationWithEntitiesAsync(string appToken)
        {
            var application = await GetApplicationAsync(appToken);

            // The context links the loaded entities to the application it tracks.
            await _dbContext.Entities
                .Where(x => x.AppID == application.ID)
                .Include(x => x.Properties)
                .LoadAsync();

            // Stays null when the application has no entities at all.
            application.Entities ??= new List<DBWS_Entity>();

            return application;
        }

        public DBWS_Entity GetEntity(DBWS_Application application, string entityName)
        {
            return application.Entities.FirstOrDefault(x => string.Equals(x.Name, entityName, StringComparison.Ordinal))
                ?? throw PortalException.NotFound("Entity");
        }

        public DBWS_EntityProperty GetProperty(DBWS_Entity entity, string propertyName)
        {
            return entity.Properties.FirstOrDefault(x => string.Equals(x.Name, propertyName, StringComparison.Ordinal))
                ?? throw PortalException.NotFound("Property");
        }

        // The owner, or anyone the application is shared with by e-mail.
        private static IQueryable<DBWS_Application> Visible(IQueryable<DBWS_Application> applications, ApplicationUser user)
        {
            return WithIncludes(applications)
                .Where(x => x.UserID == user.Id || x.Collaborates.Any(c => c.UserEmail == user.Email));
        }

        // What ApplicationResponse needs. Entities, properties and reports are loaded by the services that need them.
        private static IQueryable<DBWS_Application> WithIncludes(IQueryable<DBWS_Application> applications)
        {
            return applications
                .Include(x => x.Server)
                .Include(x => x.Collaborates)
                .Include(x => x.CustomEndpoints)
                // Two lists: one query each, instead of one query with every combination of their rows.
                .AsSplitQuery();
        }

        // Unknown and not visible give the same answer, so a token can not be probed.
        private static async Task<DBWS_Application> FindAsync(IQueryable<DBWS_Application> applications, string appToken)
        {
            var application = await applications.FirstOrDefaultAsync(x => x.Token == appToken);

            // Exact match, whatever collation the database compares with.
            if (application is null || !string.Equals(application.Token, appToken, StringComparison.Ordinal))
            {
                throw PortalException.NotFound(EntityName);
            }

            return application;
        }
    }
}
