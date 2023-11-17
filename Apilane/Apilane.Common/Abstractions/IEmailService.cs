using Apilane.Common.Models;

namespace Apilane.Common.Abstractions
{
    public interface IEmailService
    {
        void SendMail(EmailInfo info);
    }
}
