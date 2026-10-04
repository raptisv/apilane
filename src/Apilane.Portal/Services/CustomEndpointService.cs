using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class CustomEndpointService : ICustomEndpointService
    {
        private const string EntityName = "CustomEndpoint";

        private readonly ApplicationDbContext _dbContext;
        private readonly IApplicationAccessService _applicationAccessService;
        private readonly IApplicationWriteScope _applicationWriteScope;

        public CustomEndpointService(
            ApplicationDbContext dbContext,
            IApplicationAccessService applicationAccessService,
            IApplicationWriteScope applicationWriteScope)
        {
            _dbContext = dbContext;
            _applicationAccessService = applicationAccessService;
            _applicationWriteScope = applicationWriteScope;
        }

        public async Task<List<CustomEndpointResponse>> GetAllAsync(string appToken)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken);

            return application.CustomEndpoints
                .OrderBy(x => x.ID)
                .Select(x => ToResponse(x, application))
                .ToList();
        }

        public async Task<CustomEndpointResponse> GetAsync(string appToken, long id)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken);

            return ToResponse(Find(application, id), application);
        }

        public async Task<CustomEndpointResponse> CreateAsync(string appToken, CustomEndpointRequest request)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken);
            var name = Utils.GetString(request.Name);

            ThrowIfNameIsTaken(application, name, exceptId: null);

            var endpoint = new DBWS_CustomEndpoint
            {
                AppID = application.ID,
                Name = name,
                Description = EmptyToNull(request.Description),
                Query = request.Query,
                DateModified = DateTime.UtcNow
            };

            _dbContext.CustomEndpoints.Add(endpoint);

            // Nothing to call: the cache reset is how the API server learns about the endpoint.
            await _applicationWriteScope.SaveAsync(application);

            return ToResponse(endpoint, application);
        }

        public async Task<CustomEndpointResponse> UpdateAsync(string appToken, long id, CustomEndpointRequest request)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken);
            var endpoint = Find(application, id);
            var name = Utils.GetString(request.Name);

            ThrowIfNameIsTaken(application, name, exceptId: endpoint.ID);

            // Only these three values change; DateModified stays as it is.
            endpoint.Name = name;
            endpoint.Description = EmptyToNull(request.Description);
            endpoint.Query = request.Query;

            await _applicationWriteScope.SaveAsync(application);

            return ToResponse(endpoint, application);
        }

        public async Task DeleteAsync(string appToken, long id)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken);
            var endpoint = Find(application, id);

            // Security rules written for its name stay.
            _dbContext.CustomEndpoints.Remove(endpoint);

            await _applicationWriteScope.SaveAsync(application);
        }

        public async Task<CustomEndpointPreviewResponse> PreviewAsync(string appToken, CustomEndpointPreviewRequest request)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken);

            // Never added to the context, so nothing can be saved.
            var endpoint = new DBWS_CustomEndpoint
            {
                Name = Utils.GetString(request.Name),
                Query = request.Query ?? string.Empty
            };

            return new CustomEndpointPreviewResponse
            {
                Parameters = endpoint.GetParameters(),
                Url = endpoint.GetUrl(application.Server.ServerUrl, application.Token, appendAppToken: false),
                CallUrl = CallUrlOf(endpoint, application)
            };
        }

        // Inside the route application only, so an ID of another application is not found.
        private static DBWS_CustomEndpoint Find(DBWS_Application application, long id)
        {
            return application.CustomEndpoints.FirstOrDefault(x => x.ID == id)
                ?? throw PortalException.NotFound(EntityName);
        }

        // Case-insensitive: the API server finds an endpoint by name whatever its letter case.
        private static void ThrowIfNameIsTaken(DBWS_Application application, string name, long? exceptId)
        {
            if (application.CustomEndpoints.Any(x => x.ID != exceptId && string.Equals(Utils.GetString(x.Name), name, StringComparison.OrdinalIgnoreCase)))
            {
                throw PortalException.Conflict($"Custom endpoint '{name}' already exists", EntityName);
            }
        }

        private static CustomEndpointResponse ToResponse(DBWS_CustomEndpoint endpoint, DBWS_Application application)
        {
            return new CustomEndpointResponse
            {
                ID = endpoint.ID,
                Name = endpoint.Name,
                Description = endpoint.Description,
                Query = endpoint.Query,
                Parameters = endpoint.GetParameters(),
                Url = endpoint.GetUrl(application.Server.ServerUrl, application.Token, appendAppToken: false),
                CallUrl = CallUrlOf(endpoint, application),
                DateModified = endpoint.DateModified
            };
        }

        // Not GetUrl(..., appendAppToken: true): a query with {appToken} makes it add the same key
        // twice and throw. Same output otherwise, and the same as endpointAddress in the SPA.
        private static string CallUrlOf(DBWS_CustomEndpoint endpoint, DBWS_Application application)
        {
            var pairs = new[] { $"{Globals.ApplicationTokenQueryParam}={application.Token}" }
                .Concat(endpoint.GetParameters().Select(x => $"{x}={{{x}}}"));

            return $"{application.Server.ServerUrl.Trim('/')}/api/Custom/{endpoint.Name}?{string.Join("&", pairs)}";
        }

        // A field left empty is stored as null.
        private static string? EmptyToNull(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
