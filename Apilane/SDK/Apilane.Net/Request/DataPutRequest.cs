namespace Apilane.Net.Request
{
    public class DataPutRequest : ApilaneRequestBase<DataPutRequest>
    {
        public static DataPutRequest New(string entity) => new(entity);

        private DataPutRequest(string entity) : base(entity, "Data", "Put")
        {

        }
    }
}
