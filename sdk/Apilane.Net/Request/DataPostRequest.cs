namespace Apilane.Net.Request
{
    public class DataPostRequest : ApilaneRequestBase<DataPostRequest>
    {
        public static DataPostRequest New(string entity) => new(entity);

        private DataPostRequest(string entity) : base(entity, "Data", "Post")
        {

        }
    }
}
