using Apilane.Common.Abstractions;
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
            return new TransactionScope(
               transactionScopeOption,
               new TransactionOptions()
               {
                   IsolationLevel = IsolationLevel,
                   Timeout = timeout ?? TimeSpan.FromSeconds(5)
               },
               TransactionScopeAsyncFlowOption.Enabled);
        }
    }
}
