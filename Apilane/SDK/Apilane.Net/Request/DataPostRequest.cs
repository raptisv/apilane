namespace Apilane.Net.Request
{
    public class DataPostRequest : ApilaneRequestBase
    {
        public static DataPostRequest New(string entity) => new(entity);

        private DataPostRequest(string entity) : base(entity, "Data", "Post")
        {

        }

        public DataPostRequest WithAuthToken(string authToken)
        {
            _authToken = authToken;
            return this;
        }
    }
}
