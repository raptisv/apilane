using Apilane.Common.Helpers;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Api.Abstractions
{
    public interface IEmailAPI
    {
        Task ForgotPasswordAsync(DBWS_Application application, string email);
        Task RequestConfirmationAsync(DBWS_Application application, string email);
        Task SendAsync(string appToken, string applicationServerUrl, EmailSettings emailSettings, long[] userIds, int templateId);
        Task UpdateAsync(string appToken, EmailTemplateDto template);
        Task<List<EmailTemplateDto>> GetEmailsAsync(string appToken, long? templateId = null);
    }
}
