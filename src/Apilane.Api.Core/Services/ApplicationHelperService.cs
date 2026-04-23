using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using Apilane.Data.Abstractions;
using Apilane.Data.Helper.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Services
{
    public class ApplicationHelperService : IApplicationHelperService
    {
        private readonly IApplicationDataStoreFactory _factory;

        public ApplicationHelperService(IApplicationDataStoreFactory applicationDataStoreFactory)
        {
            _factory = applicationDataStoreFactory;
        }

        public async Task<List<EmailTemplateDto>> GetEmailsAsync(long? templateId)
        {
            FilterData? filter = templateId.HasValue
                ? new FilterData(nameof(H_Email_Templates.ID), FilterData.FilterOperators.equal, templateId.Value, PropertyType.Number)
                : null;

            var rows = await _factory.GetPagedDataAsync(nameof(H_Email_Templates), null, filter, null, -1, -1);

            return rows.Select(x => new EmailTemplateDto()
            {
                ID = Utils.GetInt(x[nameof(H_Email_Templates.ID)]),
                EventCode = Utils.GetString(x[nameof(H_Email_Templates.EventCode)]),
                Active = Utils.GetBool(x[nameof(H_Email_Templates.Active)]),
                Subject = Utils.GetString(x[nameof(H_Email_Templates.Subject)]),
                Content = Utils.GetString(x[nameof(H_Email_Templates.Content)])
            }).ToList();
        }

        public async Task<EmailTemplateDto?> GetEmailAsync(EmailEventsCodes eventCode)
        {
            var filter = new FilterData(nameof(H_Email_Templates.EventCode), FilterData.FilterOperators.equal, eventCode.ToString(), PropertyType.String);

            var rows = await _factory.GetPagedDataAsync(nameof(H_Email_Templates), null, filter, null, 1, 1);

            return rows.Count > 0
                ? new EmailTemplateDto()
                {
                    ID = Utils.GetInt(rows[0][nameof(H_Email_Templates.ID)]),
                    EventCode = Utils.GetString(rows[0][nameof(H_Email_Templates.EventCode)]),
                    Active = Utils.GetBool(rows[0][nameof(H_Email_Templates.Active)]),
                    Subject = Utils.GetString(rows[0][nameof(H_Email_Templates.Subject)]),
                    Content = Utils.GetString(rows[0][nameof(H_Email_Templates.Content)])
                }
                : null;
        }

        public async Task UpdateEmailAsync(EmailTemplateDto template)
        {
            await _factory.UpdateDataAsync(
                nameof(H_Email_Templates),
                new Dictionary<string, object?>
                {
                    { nameof(H_Email_Templates.Active), template.Active },
                    { nameof(H_Email_Templates.Subject), template.Subject },
                    { nameof(H_Email_Templates.Content), template.Content }
                },
                new FilterData(nameof(H_Email_Templates.ID), FilterData.FilterOperators.equal, template.ID, PropertyType.Number));
        }

        public async Task<(List<Dictionary<string, object?>> Data, long Total)> GetHistoryForRecordPagedAsync(
            string entity, long recordId, int pageIndex, int pageSize)
        {
            var filter = new FilterData(FilterData.FilterLogic.AND, new List<FilterData>()
            {
                new FilterData(nameof(H_Entity_Change_Tracking.Entity), FilterData.FilterOperators.equal, entity, PropertyType.String),
                new FilterData(nameof(H_Entity_Change_Tracking.RecordID), FilterData.FilterOperators.equal, recordId, PropertyType.Number)
            });

            var sort = new List<SortData>
            {
                new SortData { Property = nameof(H_Entity_Change_Tracking.Created), Direction = "DESC" }
            };

            var data = await _factory.GetPagedDataAsync(nameof(H_Entity_Change_Tracking), null, filter, sort, pageIndex, pageSize);
            var total = await _factory.GetDataCountAsync(nameof(H_Entity_Change_Tracking), filter);

            return (data, total);
        }

        public async Task<long> GetHistoryCountForEntityAsync(string entity)
        {
            var filter = new FilterData(nameof(H_Entity_Change_Tracking.Entity), FilterData.FilterOperators.equal, entity, PropertyType.String);

            return await _factory.GetDataCountAsync(nameof(H_Entity_Change_Tracking), filter);
        }

        public async Task DeleteHistoryAsync(string entity, List<long>? recordIds)
        {
            var entityFilter = new FilterData(nameof(H_Entity_Change_Tracking.Entity), FilterData.FilterOperators.equal, entity, PropertyType.String);

            FilterData filter;
            if (recordIds is not null && recordIds.Any())
            {
                var idFilters = recordIds
                    .Select(id => new FilterData(nameof(H_Entity_Change_Tracking.RecordID), FilterData.FilterOperators.equal, id, PropertyType.Number))
                    .ToList();

                filter = new FilterData(FilterData.FilterLogic.AND, new List<FilterData>
                {
                    entityFilter,
                    new FilterData(FilterData.FilterLogic.OR, idFilters)
                });
            }
            else
            {
                filter = entityFilter;
            }

            await _factory.DeleteDataAsync(nameof(H_Entity_Change_Tracking), filter);
        }

        public async Task CreateHistoryAsync(string entity, long recordId, long? userId, Dictionary<string, object?> data)
        {
            await _factory.CreateDataAsync(
                nameof(H_Entity_Change_Tracking),
                new Dictionary<string, object?>
                {
                    { nameof(H_Entity_Change_Tracking.Entity), entity },
                    { nameof(H_Entity_Change_Tracking.RecordID), recordId },
                    { nameof(H_Entity_Change_Tracking.Owner), userId.HasValue ? (object?)userId.Value : null },
                    { nameof(H_Entity_Change_Tracking.Data), JsonSerializer.Serialize(data) },
                    { nameof(H_Entity_Change_Tracking.Created), Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow) }
                },
                allowInsertIdentity: false);
        }

        public async Task CreateEmailConfirmationTokenAsync(long userId, string confirmationToken)
        {
            // Delete expired tokens (older than 24 hours)
            var expiryMs = Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow) - (1440L * 60 * 1000);
            await _factory.DeleteDataAsync(
                nameof(H_Auth_Email_Confirmation_Tokens),
                new FilterData(nameof(H_Auth_Email_Confirmation_Tokens.Created), FilterData.FilterOperators.less, expiryMs, PropertyType.Date));

            // Insert new token
            await _factory.CreateDataAsync(
                nameof(H_Auth_Email_Confirmation_Tokens),
                new Dictionary<string, object?>
                {
                    { nameof(H_Auth_Email_Confirmation_Tokens.Owner), userId },
                    { nameof(H_Auth_Email_Confirmation_Tokens.Token), confirmationToken },
                    { nameof(H_Auth_Email_Confirmation_Tokens.Created), Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow) }
                },
                allowInsertIdentity: false);
        }

        public async Task CreatePasswordResetTokenAsync(long userId, string resetToken)
        {
            // Delete expired tokens (older than 24 hours)
            var expiryMs = Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow) - (1440L * 60 * 1000);
            await _factory.DeleteDataAsync(
                nameof(H_Auth_Password_Reset_Tokens),
                new FilterData(nameof(H_Auth_Password_Reset_Tokens.Created), FilterData.FilterOperators.less, expiryMs, PropertyType.Date));

            // Insert new token
            await _factory.CreateDataAsync(
                nameof(H_Auth_Password_Reset_Tokens),
                new Dictionary<string, object?>
                {
                    { nameof(H_Auth_Password_Reset_Tokens.Owner), userId },
                    { nameof(H_Auth_Password_Reset_Tokens.Token), resetToken },
                    { nameof(H_Auth_Password_Reset_Tokens.Created), Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow) }
                },
                allowInsertIdentity: false);
        }

        public async Task<long?> GetUserIdFromEmailConfitmationTokenAsync(string confirmationToken)
        {
            var oneHourAgoMs = Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow.AddHours(-1));

            var filter = new FilterData(FilterData.FilterLogic.AND, new List<FilterData>()
            {
                new FilterData(nameof(H_Auth_Email_Confirmation_Tokens.Token), FilterData.FilterOperators.equal, confirmationToken, PropertyType.String),
                new FilterData(nameof(H_Auth_Email_Confirmation_Tokens.Created), FilterData.FilterOperators.greaterorequal, oneHourAgoMs, PropertyType.Date)
            });

            var rows = await _factory.GetPagedDataAsync(nameof(H_Auth_Email_Confirmation_Tokens), null, filter, null, 1, 1);

            return rows.Count > 0
                ? Utils.GetNullLong(rows[0][nameof(H_Auth_Email_Confirmation_Tokens.Owner)])
                : null;
        }

        public async Task<long?> GetUserIdFromPasswordResetTokenAsync(string resetToken)
        {
            var filter = new FilterData(nameof(H_Auth_Password_Reset_Tokens.Token), FilterData.FilterOperators.equal, resetToken, PropertyType.String);

            var rows = await _factory.GetPagedDataAsync(nameof(H_Auth_Password_Reset_Tokens), null, filter, null, 1, 1);

            return rows.Count > 0
                ? Utils.GetNullLong(rows[0][nameof(H_Auth_Password_Reset_Tokens.Owner)])
                : null;
        }

        public async Task DeletePasswordResetTokenAsync(string resetToken)
        {
            await _factory.DeleteDataAsync(
                nameof(H_Auth_Password_Reset_Tokens),
                new FilterData(nameof(H_Auth_Password_Reset_Tokens.Token), FilterData.FilterOperators.equal, resetToken, PropertyType.String));
        }
    }
}
