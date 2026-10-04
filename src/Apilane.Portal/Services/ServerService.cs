using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class ServerService : IServerService
    {
        private const string EntityName = "Server";

        private readonly ApplicationDbContext _dbContext;

        public ServerService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<ServerResponse>> GetAllAsync()
        {
            // Same order as the Razor page: the order the servers were added.
            return await _dbContext.Servers
                .AsNoTracking()
                .OrderBy(x => x.ID)
                .Select(x => new ServerResponse
                {
                    ID = x.ID,
                    Name = x.Name,
                    ServerUrl = x.ServerUrl,
                    ApplicationCount = x.Applications.Count
                })
                .ToListAsync();
        }

        public async Task<List<ServerSummaryResponse>> GetSummariesAsync()
        {
            return await _dbContext.Servers
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new ServerSummaryResponse
                {
                    ID = x.ID,
                    Name = x.Name,
                    ServerUrl = x.ServerUrl
                })
                .ToListAsync();
        }

        public async Task<ServerResponse> GetAsync(long id)
        {
            var server = await FindAsync(id);

            return ToResponse(server, await CountApplicationsAsync(id));
        }

        public async Task<ServerResponse> CreateAsync(ServerRequest request)
        {
            ValidateUrl(request);

            var server = new DBWS_Server
            {
                Name = request.Name,
                ServerUrl = request.ServerUrl,
                DateModified = DateTime.UtcNow
            };

            _dbContext.Servers.Add(server);
            await _dbContext.SaveChangesAsync();

            return ToResponse(server, applicationCount: 0);
        }

        public async Task<ServerResponse> UpdateAsync(long id, ServerRequest request)
        {
            ValidateUrl(request);

            var server = await FindAsync(id);

            // DateModified is left as it is, like the Razor page does.
            server.Name = request.Name;
            server.ServerUrl = request.ServerUrl;

            await _dbContext.SaveChangesAsync();

            return ToResponse(server, await CountApplicationsAsync(id));
        }

        public async Task DeleteAsync(long id)
        {
            var server = await FindAsync(id);
            var applicationCount = await CountApplicationsAsync(id);

            if (applicationCount > 0)
            {
                throw PortalException.Conflict(
                    $"The server cannot be deleted since it has {applicationCount} Application bound on it",
                    EntityName);
            }

            _dbContext.Servers.Remove(server);
            await _dbContext.SaveChangesAsync();
        }

        // Tracked, so that a change to it is saved and audited.
        private async Task<DBWS_Server> FindAsync(long id)
        {
            return await _dbContext.Servers.FirstOrDefaultAsync(x => x.ID == id)
                ?? throw PortalException.NotFound(EntityName);
        }

        private Task<int> CountApplicationsAsync(long serverId)
        {
            return _dbContext.Applications.CountAsync(x => x.ServerID == serverId);
        }

        private static void ValidateUrl(ServerRequest request)
        {
            // Only http and https: the address is called by the Portal and opened as a link in the browser.
            if (!Uri.IsWellFormedUriString(request.ServerUrl, UriKind.Absolute)
                || !Uri.TryCreate(request.ServerUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw PortalException.Validation(nameof(ServerRequest.ServerUrl), "Not a valid url");
            }
        }

        private static ServerResponse ToResponse(DBWS_Server server, int applicationCount)
        {
            return new ServerResponse
            {
                ID = server.ID,
                Name = server.Name,
                ServerUrl = server.ServerUrl,
                ApplicationCount = applicationCount
            };
        }
    }
}
