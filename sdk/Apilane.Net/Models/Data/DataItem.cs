using Apilane.Net.Extensions;
using System;

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
        public long? Created { get; set; }

        /// <summary>
        /// The date the record was created as a DateTime object
        /// </summary>
        public DateTime? Created_Date
        {
            get
            {
                return Created?.UnixTimestampToDatetime();
            }
        }
    }
}
