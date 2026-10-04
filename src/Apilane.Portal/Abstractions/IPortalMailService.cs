namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// Sends mail with the mail settings of the instance (Admin settings).
    /// </summary>
    public interface IPortalMailService
    {
        /// <summary>
        /// Whether every mail setting of the instance is filled in.
        /// </summary>
        bool IsConfigured();

        /// <summary>
        /// Sends an HTML mail to one recipient. Returns false, and sends nothing, when the instance
        /// mail is not configured. A failed delivery is logged, not reported.
        /// </summary>
        bool Send(string recipient, string subject, string htmlBody);
    }
}
