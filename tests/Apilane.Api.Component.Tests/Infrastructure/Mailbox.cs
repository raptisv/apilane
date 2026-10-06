using Apilane.Common.Abstractions;
using Apilane.Common.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Apilane.Api.Component.Tests.Infrastructure
{
    /// <summary>
    /// The mail service of the API host in the tests: it keeps the mails instead of sending them, so that a
    /// test can see which mails an action sent and read the links in them. The host is shared by test
    /// classes that run in parallel, so a test looks up the mails of an address that only it uses.
    /// </summary>
    public sealed class Mailbox : IEmailService
    {
        private readonly ConcurrentQueue<EmailInfo> _mails = new();

        public void SendMail(EmailInfo info)
        {
            _mails.Enqueue(info);
        }

        public List<EmailInfo> SentTo(string address)
        {
            return _mails
                .Where(x => x.Recipients.Contains(address, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
    }
}
