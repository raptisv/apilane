using Apilane.Api.Core.Abstractions;
using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Common.Utilities;
using Apilane.Data.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Apilane.Api.Core
{
    public class EntityHistoryAPI : IEntityHistoryAPI
    {
        private readonly IApplicationHelperService _applicationHelperService;
        private readonly IApplicationService _applicationService;

        public EntityHistoryAPI(
            IApplicationHelperService applicationHelperService,
            IApplicationService applicationService)
        {
            _applicationHelperService = applicationHelperService;
            _applicationService = applicationService;
        }

        public async Task<DataTotalResponse> GetPagedAsync(
            string appToken,
            long recordId,
            string entity,
            int? pageIndex,
            int? pageSize)
        {
            if (pageIndex is null || pageIndex < 1)
            {
                pageIndex = 1;
            }

            var historyRecords = await _applicationHelperService.GetHistoryForRecordPagedAsync(entity, recordId, pageIndex.Value, Utils.GetPageSize(pageSize));

            if (historyRecords.Data.Count > 0)
            {
                await DecryptSnapshotsAsync(appToken, entity, historyRecords.Data);
            }

            return new DataTotalResponse()
            {
                Data = historyRecords.Data,
                Total = historyRecords.Total
            };
        }

        /// <summary>
        /// A snapshot is the record as it was stored, so its encrypted properties are ciphertext. They are decrypted
        /// the way Data/Get decrypts them, and a value that does not decrypt becomes null, as there.
        /// </summary>
        private async Task DecryptSnapshotsAsync(string appToken, string entityName, List<Dictionary<string, object?>> rows)
        {
            var application = await _applicationService.GetAsync(appToken);

            var entity = application.Entities.SingleOrDefault(x => x.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase));

            var encryptedProperties = entity is null
                ? new List<string>()
                : entity.Properties.Where(x => x.Encrypted).Select(x => x.Name).ToList();

            if (encryptedProperties.Count == 0)
            {
                return;
            }

            var appEncryptionKey = application.EncryptionKey.Decrypt(Globals.EncryptionKey);

            foreach (var row in rows)
            {
                var json = row.TryGetValue(Globals.EntityHistoryDataColumn, out var data) ? Utils.GetString(data) : string.Empty;

                if (string.IsNullOrEmpty(json) || JsonNode.Parse(json) is not JsonObject snapshot)
                {
                    continue;
                }

                foreach (var propertyName in encryptedProperties)
                {
                    if (snapshot.TryGetPropertyValue(propertyName, out var value) && value is not null)
                    {
                        snapshot[propertyName] = Encryptor.TryDecrypt(Utils.GetString(value), appEncryptionKey, out string? output)
                            ? output
                            : null;
                    }
                }

                row[Globals.EntityHistoryDataColumn] = snapshot.ToJsonString();
            }
        }

        public Task DeleteAsync( string appToken, string entity, List<long>? recordIds)
            => _applicationHelperService.DeleteHistoryAsync(entity, recordIds);
    }
}
