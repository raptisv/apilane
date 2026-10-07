namespace Apilane.Portal.Models
{
    /// <summary>
    /// Persistent first-administrator setup state. The temporary password itself is never stored.
    /// </summary>
    public class PortalBootstrapState
    {
        public int ID { get; set; }
        public string? UserId { get; set; }
        public bool Completed { get; set; }
        public bool TemporaryPasswordIssued { get; set; }
    }
}
