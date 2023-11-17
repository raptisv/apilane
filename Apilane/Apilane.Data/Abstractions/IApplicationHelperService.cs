using Apilane.Common.Enums;
using Apilane.Common.Models.Dto;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Data.Abstractions
{
    public interface IApplicationHelperService
    {
        Task<string> EnsureHelperDatabaseAsync(string appToken);
        Task<bool> EmailConfirmationTokensForUserIdExistAsync(string appToken, long userId, uint inTheLastMinutes);
        Task<List<EmailTemplateDto>> GetEmailsAsync(string appToken, long? templateId);
        Task<EmailTemplateDto?> GetEmailAsync(string appToken, EmailEventsCodes eventCode);
        Task UpdateEmailAsync(string appToken, EmailTemplateDto template);
        Task<(List<Dictionary<string, object?>> Data, long Total)> GetHistoryForRecordPagedAsync(string appToken, string entity, long recordId, int pageIndex, int pageSize);
        Task<long> GetHistoryCountForEntityAsync(string appToken, string entity);
        Task DeleteHistoryAsync(string appToken, string entity, List<long>? recordIds);
        Task CreateHistoryAsync(string appToken, string entity, long recordId, long? userId, Dictionary<string, object?> data);
        Task CreateEmailConfirmationTokenAsync(string appToken, long userId, string confirmationToken);
        Task CreatePasswordResetTokenAsync(string appToken, long userId, string resetToken);
        Task<long?> GetUserIdFromEmailConfitmationTokenAsync(string appToken, string confirmationToken);
        Task<long?> GetUserIdFromPasswordResetTokenAsync(string appToken, string resetToken);
        Task DeletePasswordResetTokenAsync(string appToken, string resetToken);
    }
}
