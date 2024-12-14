using Apilane.Net.Models.Data;

namespace Apilane.Net.Models.Files
{
    public class FileItem : DataItem
    {
        public bool Public { get; set; }
        public decimal Size { get; set; }
        public string Name { get; set; } = null!;
        public string UID { get; set; } = null!;
    }
}
