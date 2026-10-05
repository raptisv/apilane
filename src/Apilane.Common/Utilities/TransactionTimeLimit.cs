using System;
using System.Collections.Concurrent;
using System.Transactions;

namespace Apilane.Common.Utilities
{
    /// <summary>
    /// Remembers when the transactions that <see cref="Services.TransactionScopeService"/> opened time out, so a
    /// data store can stop a command before the transaction does (see <c>TransactionEnlistment.GetCommandTimeout</c>
    /// in Apilane.Data). A <see cref="Transaction"/> does not tell when it times out.
    /// </summary>
    public static class TransactionTimeLimit
    {
        // Keyed by the local identifier of the transaction. An entry is removed when its transaction ends.
        private static readonly ConcurrentDictionary<string, DateTime> _expiresUtc = new();

        /// <summary>
        /// Records that <paramref name="transaction"/>, just created by a scope, times out after
        /// <paramref name="timeout"/>. Does nothing for a transaction that is already recorded.
        /// </summary>
        public static void Start(Transaction transaction, TimeSpan timeout)
        {
            var id = transaction.TransactionInformation.LocalIdentifier;

            if (_expiresUtc.TryAdd(id, DateTime.UtcNow + timeout))
            {
                transaction.TransactionCompleted += (_, _) => _expiresUtc.TryRemove(id, out _);
            }
        }

        /// <summary>
        /// The time left before the current transaction times out, or null when there is no current
        /// transaction or its time limit is not known (it was not opened by <see cref="Services.TransactionScopeService"/>).
        /// It is zero or negative once the limit has passed.
        /// </summary>
        public static TimeSpan? GetRemaining()
        {
            var transaction = Transaction.Current;

            if (transaction is null)
            {
                return null;
            }

            return _expiresUtc.TryGetValue(transaction.TransactionInformation.LocalIdentifier, out var expiresUtc)
                ? expiresUtc - DateTime.UtcNow
                : null;
        }
    }
}
