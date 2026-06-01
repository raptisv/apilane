namespace Apilane.Net.Models.Data
{
    public class DataItem
    {
        /// <summary>
        /// The record ID
        /// </summary>
        public long ID { get; set; }

        /// <summary>
        /// The record Owner (UserID)
        /// </summary>
        public long? Owner { get; set; }

        /// <summary>
        /// The date the record was created
        /// </summary>
        public long Created { get; set; }
    }
}
