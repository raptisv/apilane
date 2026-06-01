using Apilane.Common.Enums;
using Apilane.Common.Models.Dto;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Data.Abstractions
{
    public interface IApplicationHelperService
    {
        Task<List<EmailTemplateDto>> GetEmailsAsync(long? templateId);
        Task<EmailTemplateDto?> GetEmailAsync(EmailEventsCodes eventCode);
        Task UpdateEmailAsync(EmailTemplateDto template);
        Task<(List<Dictionary<string, object?>> Data, long Total)> GetHistoryForRecordPagedAsync(string entity, long recordId, int pageIndex, int pageSize);
        Task<long> GetHistoryCountForEntityAsync(string entity);
        Task DeleteHistoryAsync(string entity, List<long>? recordIds);
        Task CreateHistoryAsync(string entity, long recordId, long? userId, Dictionary<string, object?> data);
        Task CreateEmailConfirmationTokenAsync(long userId, string confirmationToken);
        Task CreatePasswordResetTokenAsync(long userId, string resetToken);
        Task<long?> GetUserIdFromEmailConfitmationTokenAsync(string confirmationToken);
        Task<long?> GetUserIdFromPasswordResetTokenAsync(string resetToken);
        Task DeletePasswordResetTokenAsync(string resetToken);
    }
}
