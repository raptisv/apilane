namespace Apilane.Net.Request
{
    public class DataTransactionRequest : ApilaneRequestBase
    {
        public static DataTransactionRequest New() => new();

        private DataTransactionRequest() : base(null, "Data", "Transaction")
        {

        }

        public DataTransactionRequest WithAuthToken(string authToken)
        {
            _authToken = authToken;
            return this;
        }
    }
}
