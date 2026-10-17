using Apilane.Common.Abstractions;
using Apilane.Common.Utilities;
using System;
using System.Transactions;

namespace Apilane.Common.Services
{
    public class TransactionScopeService : ITransactionScopeService
    {
        /// <summary>
        /// How long after its time limit the transaction of a scope times out by itself. The time limit is kept by
        /// the commands (see <see cref="TransactionTimeLimit"/>); the transaction's own timeout is only a backstop.
        /// </summary>
        /// <remarks>
        /// When a transaction times out, System.Transactions rolls it back on a timer thread, while the request
        /// may still be using the connection: a command that ran into the limit is still being cancelled
        /// (MySqlConnector waits up to two seconds for that). MySqlConnector then throws on that thread, nobody
        /// can catch it, and the process ends with every tenant it serves. With the backstop well after the
        /// limit, the request has failed and rolled back on its own thread long before the timer fires.
        /// </remarks>
        public static readonly TimeSpan BackstopTimeout = TimeSpan.FromSeconds(30);

        public TransactionScope OpenTransactionScope(
            TransactionScopeOption transactionScopeOption = TransactionScopeOption.Required,
            IsolationLevel IsolationLevel = IsolationLevel.ReadCommitted,
            TimeSpan? timeout = null)
        {
            var scopeTimeout = timeout ?? TimeSpan.FromSeconds(5);
            var transactionTimeout = scopeTimeout + BackstopTimeout;

            // System.Transactions cuts every timeout to its maximum (10 minutes by default) without a word: the
            // time limit then ends a backstop earlier, so the backstop stays
            var maximumTimeout = TransactionManager.MaximumTimeout;
            if (maximumTimeout > BackstopTimeout && transactionTimeout > maximumTimeout)
            {
                transactionTimeout = maximumTimeout;
                scopeTimeout = maximumTimeout - BackstopTimeout;
            }

            var outerTransaction = Transaction.Current;

            var scope = new TransactionScope(
               transactionScopeOption,
               new TransactionOptions()
               {
                   IsolationLevel = IsolationLevel,
                   Timeout = transactionTimeout
               },
               TransactionScopeAsyncFlowOption.Enabled);

            // A scope that joined the transaction of an outer scope is bound by the time limit of that one
            var transaction = Transaction.Current;
            if (transaction is not null && transaction != outerTransaction)
            {
                TransactionTimeLimit.Start(transaction, scopeTimeout);
            }

            return scope;
        }
    }
}
