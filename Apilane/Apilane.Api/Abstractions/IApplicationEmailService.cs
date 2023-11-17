using Apilane.Common.Enums;
using Apilane.Common.Helpers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Api.Abstractions
{
    public interface IApplicationEmailService
    {
        Task<(string Subject, string Content)> CreateSubjectAndContentAsync(string appToken, string applicationServerUrl, EmailEvent Event, string subject, string content, Dictionary<string, object?> userThatAcceptsTheEmail, Dictionary<string, object?> UserThatTriggeredTheEmail);
        Task<bool> SendEmailFromApplicationAsync(string appToken, string applicationServerUrl, EmailSettings? emailSettings, EmailEventsCodes eventCode, Dictionary<string, object?> userThatAcceptsTheEmail, Dictionary<string, object?> userThatTriggeredTheEmail);
        Task SendEmailFromApplication_FireAndForgetAsync(string appToken, string applicationServerUrl, EmailSettings? emailSettings, EmailEventsCodes eventsCode, Dictionary<string, object?> userThatAcceptsTheEmail, Dictionary<string, object?> userThatTriggeredTheEmail);
    }
}
