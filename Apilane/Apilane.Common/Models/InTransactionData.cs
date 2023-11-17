using System.Collections.Generic;

namespace Apilane.Common.Models
{
    public class InTransactionData
    {
        public List<InTransactionSet> Post { get; set; } = null!;
        public List<InTransactionSet> Put { get; set; } = null!;
        public List<InTransactionDelete> Delete { get; set; } = null!;

        public class InTransactionSet
        {
            public string Entity { get; set; } = null!;
            public object Data { get; set; } = null!;
        }

        public class InTransactionDelete
        {
            public string Entity { get; set; } = null!;
            public string Ids { get; set; } = null!;
        }
    }

    public class OutTransactionData
    {
        public OutTransactionData()
        {
            Post = new List<long>();
            Delete = new List<long>();
        }

        public List<long> Post { get; set; }
        public long Put { get; set; }
        public List<long> Delete { get; set; }
    }
}
