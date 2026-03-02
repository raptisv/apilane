using System.Collections.Generic;
using System.Linq;

namespace Apilane.Net.Models.Data
{
    /// <summary>
    /// Fluent builder for constructing transaction requests with ordered operations
    /// and cross-reference support.
    /// <para>
    /// Example usage:
    /// <code>
    /// var transaction = new TransactionBuilder()
    ///     .Post("Orders", new { Name = "Test" }, out var orderRef)
    ///     .Post("OrderItems", new { OrderId = orderRef.Id(), Product = "Widget" })
    ///     .Put("Orders", new { ID = orderRef.Id(), Status = "Active" })
    ///     .Delete("OldOrders", "1,2,3")
    ///     .Build();
    /// </code>
    /// </para>
    /// </summary>
    public class TransactionBuilder
    {
        private readonly List<InTransactionOperation> _operations = new();
        private int _autoIdCounter = 0;

        /// <summary>
        /// Adds a Post operation and returns a <see cref="TransactionRef"/> that can be used
        /// to reference the created record's ID in subsequent operations.
        /// </summary>
        /// <param name="entity">The entity name</param>
        /// <param name="data">The data to create</param>
        /// <param name="reference">A reference to this operation's result, for use in subsequent operations</param>
        /// <returns>This builder for chaining</returns>
        public TransactionBuilder Post(string entity, object data, out TransactionRef reference)
        {
            var operationId = $"auto_{_autoIdCounter++}";
            _operations.Add(new InTransactionOperation
            {
                Action = TransactionAction.Post,
                Entity = entity,
                Id = operationId,
                Data = data
            });
            reference = new TransactionRef(operationId);
            return this;
        }

        /// <summary>
        /// Adds a Post operation without capturing a reference.
        /// </summary>
        /// <param name="entity">The entity name</param>
        /// <param name="data">The data to create</param>
        /// <returns>This builder for chaining</returns>
        public TransactionBuilder Post(string entity, object data)
        {
            return Post(entity, data, out _);
        }

        /// <summary>
        /// Adds a Put operation.
        /// </summary>
        /// <param name="entity">The entity name</param>
        /// <param name="data">The data to update (must include the record ID)</param>
        /// <returns>This builder for chaining</returns>
        public TransactionBuilder Put(string entity, object data)
        {
            _operations.Add(new InTransactionOperation
            {
                Action = TransactionAction.Put,
                Entity = entity,
                Data = data
            });
            return this;
        }

        /// <summary>
        /// Adds a Delete operation with explicit comma-separated IDs.
        /// </summary>
        /// <param name="entity">The entity name</param>
        /// <param name="ids">Comma-separated IDs to delete (e.g. "1,2,3")</param>
        /// <returns>This builder for chaining</returns>
        public TransactionBuilder Delete(string entity, string ids)
        {
            _operations.Add(new InTransactionOperation
            {
                Action = TransactionAction.Delete,
                Entity = entity,
                Ids = ids
            });
            return this;
        }

        /// <summary>
        /// Builds the <see cref="InTransactionOperationData"/> with all operations in declared order.
        /// </summary>
        public InTransactionOperationData Build()
        {
            return new InTransactionOperationData
            {
                Operations = _operations.ToList()
            };
        }
    }
}
