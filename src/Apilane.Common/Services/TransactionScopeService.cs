using Apilane.Common.Abstractions;
using Apilane.Common.Utilities;
using System;
using System.Transactions;

namespace Apilane.Common.Services
{
    public class TransactionScopeService : ITransactionScopeService
    {
        public TransactionScope OpenTransactionScope(
            TransactionScopeOption transactionScopeOption = TransactionScopeOption.Required,
            IsolationLevel IsolationLevel = IsolationLevel.ReadCommitted,
            TimeSpan? timeout = null)
        {
            var scopeTimeout = timeout ?? TimeSpan.FromSeconds(5);
            var outerTransaction = Transaction.Current;

            var scope = new TransactionScope(
               transactionScopeOption,
               new TransactionOptions()
               {
                   IsolationLevel = IsolationLevel,
                   Timeout = scopeTimeout
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
