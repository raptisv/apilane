namespace Apilane.Net.Request
{
    public class DataTransactionOperationsRequest : ApilaneRequestBase<DataTransactionOperationsRequest>
    {
        public static DataTransactionOperationsRequest New() => new();

        private DataTransactionOperationsRequest() : base(null, "Data", "TransactionOperations")
        {

        }
    }
}
