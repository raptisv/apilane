using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class EntityDefaultOrderService : IEntityDefaultOrderService
    {
        private const string Ascending = "asc";
        private const string Descending = "desc";

        private readonly IApplicationAccessService _applicationAccessService;
        private readonly IApplicationWriteScope _applicationWriteScope;

        public EntityDefaultOrderService(
            IApplicationAccessService applicationAccessService,
            IApplicationWriteScope applicationWriteScope)
        {
            _applicationAccessService = applicationAccessService;
            _applicationWriteScope = applicationWriteScope;
        }

        public async Task<DefaultOrderResponse> GetAsync(string appToken, string entityName)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var entity = _applicationAccessService.GetEntity(application, entityName);

            return ToResponse(entity);
        }

        public async Task<DefaultOrderResponse> ReplaceAsync(string appToken, string entityName, ReplaceDefaultOrderRequest request)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var entity = _applicationAccessService.GetEntity(application, entityName);

            // [Required] on the contract already answers 400 for a missing list.
            var requested = request.Items
                ?? throw PortalException.Validation(nameof(ReplaceDefaultOrderRequest.Items), "Required");

            var order = new List<SortData>();
            var errors = new List<ErrorDetail>();

            for (var i = 0; i < requested.Count; i++)
            {
                var path = $"{nameof(ReplaceDefaultOrderRequest.Items)}[{i}]";
                var item = requested[i];

                if (item is null)
                {
                    errors.Add(Error(path, "Required"));
                    continue;
                }

                if (!entity.Properties.Any(x => string.Equals(x.Name, item.Property, StringComparison.Ordinal)))
                {
                    errors.Add(Error($"{path}.{nameof(DefaultOrderItem.Property)}", $"Property '{item.Property}' does not exist"));
                }
                else if (order.Any(x => x.Property == item.Property))
                {
                    errors.Add(Error($"{path}.{nameof(DefaultOrderItem.Property)}", "A property can be listed only once"));
                }

                if (item.Direction != Ascending && item.Direction != Descending)
                {
                    errors.Add(Error($"{path}.{nameof(DefaultOrderItem.Direction)}", $"Must be {Ascending} or {Descending}"));
                }

                order.Add(new SortData { Property = item.Property, Direction = item.Direction });
            }

            if (errors.Count > 0)
            {
                throw PortalException.Validation(errors);
            }

            // The format of the Razor page: an empty list is stored as '[]'.
            entity.EntDefaultOrder = JsonSerializer.Serialize(order);

            // Nothing to call: the cache reset is how the API server learns about the change.
            await _applicationWriteScope.SaveAsync(application);

            return ToResponse(entity);
        }

        private static DefaultOrderResponse ToResponse(DBWS_Entity entity)
        {
            var names = entity.Properties.OrderBy(x => x.ID).Select(x => x.Name).ToList();
            var items = new List<DefaultOrderItem>();

            // As the Razor page shows it: a property that no longer exists is left out, and
            // anything that is not 'asc' sorts descending. A property stored twice counts once,
            // so what is returned here can always be sent back.
            foreach (var stored in ReadStored(entity))
            {
                if (names.Contains(stored.Property) && !items.Any(x => x.Property == stored.Property))
                {
                    items.Add(new DefaultOrderItem
                    {
                        Property = stored.Property,
                        Direction = string.Equals(stored.Direction, Ascending, StringComparison.OrdinalIgnoreCase) ? Ascending : Descending
                    });
                }
            }

            return new DefaultOrderResponse
            {
                Items = items,
                Candidates = names
            };
        }

        // Tolerant: a stored value that cannot be read is no default sorting, not a failure.
        private static List<SortData> ReadStored(DBWS_Entity entity)
        {
            try
            {
                return (SortData.ParseList(entity.EntDefaultOrder) ?? Enumerable.Empty<SortData>())
                    .Where(x => x is not null && x.Property is not null)
                    .ToList();
            }
            catch (JsonException)
            {
                return new List<SortData>();
            }
        }

        private static ErrorDetail Error(string property, string message)
        {
            return new ErrorDetail { Property = property, Message = message };
        }
    }
}
