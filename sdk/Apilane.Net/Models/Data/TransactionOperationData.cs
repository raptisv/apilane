using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Apilane.Net.Models.Data
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TransactionAction
    {
        Post,
        Put,
        Delete,
        Custom
    }

    public class InTransactionOperationData
    {
        public List<InTransactionOperation> Operations { get; set; } = null!;
    }

    public class InTransactionOperation
    {
        /// <summary>
        /// The action to perform: Post, Put, Delete, or Custom.
        /// </summary>
        public TransactionAction Action { get; set; }

        /// <summary>
        /// The entity name for Post/Put/Delete operations, or the custom endpoint name for Custom operations.
        /// </summary>
        public string Entity { get; set; } = null!;

        /// <summary>
        /// Optional operation identifier used for referencing this operation's result in subsequent operations.
        /// Use the format "$ref:{Id}" in data values to reference the first ID returned by this operation.
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// The data object for Post and Put operations.
        /// For Custom operations, a key-value object whose values will be used as endpoint parameters.
        /// String values matching "$ref:{OperationId}" will be resolved server-side to the first ID
        /// returned by the referenced operation.
        /// </summary>
        public object? Data { get; set; }

        /// <summary>
        /// Comma-separated IDs for Delete operations.
        /// </summary>
        public string? Ids { get; set; }
    }

    public class OutTransactionOperationData
    {
        public List<OutTransactionOperationResult> Results { get; set; } = new();
    }

    public class OutTransactionOperationResult
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TransactionAction Action { get; set; }
        public string Entity { get; set; } = null!;

        /// <summary>
        /// The IDs of newly created records (Post operations).
        /// </summary>
        public List<long>? Created { get; set; }

        /// <summary>
        /// The number of affected records (Put operations).
        /// </summary>
        public long? Affected { get; set; }

        /// <summary>
        /// The IDs of deleted records (Delete operations).
        /// </summary>
        public List<long>? Deleted { get; set; }

        /// <summary>
        /// The result of a Custom endpoint operation.
        /// </summary>
        public List<List<Dictionary<string, object?>>>? CustomResult { get; set; }
    }
}
