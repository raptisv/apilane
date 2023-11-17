namespace Apilane.Net.Request
{
    public class DataPutRequest : ApilaneRequestBase
    {
        public static DataPutRequest New(string entity) => new(entity);

        private DataPutRequest(string entity) : base(entity, "Data", "Put")
        {

        }

        public DataPutRequest WithAuthToken(string authToken)
        {
            _authToken = authToken;
            return this;
        }
    }
}
