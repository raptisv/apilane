namespace Apilane.Data.Helper.Models
{
    public class H_Entity_Change_Tracking 
    {
        public long ID { get; set; }
        public string Entity { get; set; } = null!;
        public string Data { get; set; } = null!;
        public long RecordID { get; set; }
        public long? Owner { get; set; }
        public long Created { get; set; }
    }
}
