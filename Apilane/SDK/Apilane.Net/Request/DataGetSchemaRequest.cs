namespace Apilane.Net.Request
{
    public class DataGetSchemaRequest : ApilaneRequestBase<DataGetSchemaRequest>
    {
        public static DataGetSchemaRequest New() => new();

        private DataGetSchemaRequest() : base(null, "Data", "Schema")
        {

        }
    }
}
