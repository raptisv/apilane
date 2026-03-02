namespace Apilane.Net.Models.Data
{
    /// <summary>
    /// Represents a reference to a prior transaction operation's result.
    /// Use <see cref="Id"/> to get a placeholder string that will be resolved server-side
    /// to the first ID returned by the referenced operation.
    /// </summary>
    public class TransactionRef
    {
        private readonly string _operationId;

        internal TransactionRef(string operationId)
        {
            _operationId = operationId;
        }

        /// <summary>
        /// Returns a placeholder string (e.g. "$ref:myOp") that the server resolves
        /// to the first ID returned by the referenced operation.
        /// Use this in data objects or as a Delete IDs value.
        /// </summary>
        public string Id() => $"$ref:{_operationId}";

        /// <summary>
        /// Returns the placeholder string for implicit conversion scenarios.
        /// </summary>
        public override string ToString() => Id();
    }
}
