namespace Apilane.Api.Models.AppModules.Files
{
    public class Files
    {
        public long ID { get; set; }
        public string Name { get; set; } = null!;
        public string UID { get; set; } = null!;
        public long? Owner { get; set; }
        public bool Public { get; set; }

        /// <summary>
        /// File size in MB
        /// </summary>
        public double Size { get; set; }
        public long Created { get; set; }
    }
}
