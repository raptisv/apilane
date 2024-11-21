using System.Collections.Specialized;

namespace Apilane.Net.Request
{
    public class DataGetSchemaRequest : ApilaneRequestBase<DataGetSchemaRequest>
    {
        public static DataGetSchemaRequest New(string encryptionKey) => new(encryptionKey);

        private string? _encryptionKey = null;

        private DataGetSchemaRequest(string encryptionKey) : base(null, "Data", "Schema")
        {
            _encryptionKey = encryptionKey;
        }

        protected override NameValueCollection GetExtraParams()
        {
            var extraParams = new NameValueCollection
            {
                { "encryptionKey", _encryptionKey }
            };

            return extraParams;
        }
    }
}
