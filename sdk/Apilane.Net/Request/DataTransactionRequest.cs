namespace Apilane.Net.Request
{
    public class DataTransactionRequest : ApilaneRequestBase<DataTransactionRequest>
    {
        public static DataTransactionRequest New() => new();

        private DataTransactionRequest() : base(null, "Data", "Transaction")
        {

        }
    }
}
