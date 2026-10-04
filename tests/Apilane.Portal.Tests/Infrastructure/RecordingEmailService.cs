using Apilane.Common.Abstractions;
using Apilane.Common.Models;
using System.Collections.Generic;

namespace Apilane.Portal.Tests.Infrastructure
{
    /// <summary>
    /// Stands in for the SMTP sender: keeps every mail the Portal wanted to send, sends nothing.
    /// </summary>
    public class RecordingEmailService : IEmailService
    {
        private readonly object _lock = new object();
        private readonly List<EmailInfo> _sent = new List<EmailInfo>();

        public void SendMail(EmailInfo info)
        {
            lock (_lock)
            {
                _sent.Add(info);
            }
        }

        /// <summary>
        /// The mails sent to one recipient. Tests use a new e-mail address each, so this is
        /// what the test itself caused.
        /// </summary>
        public List<EmailInfo> SentTo(string recipient)
        {
            lock (_lock)
            {
                return _sent.FindAll(x => x.Recipients.Length == 1 && x.Recipients[0] == recipient);
            }
        }
    }
}
