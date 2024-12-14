using System;
using System.Transactions;

namespace Apilane.Common.Abstractions
{
    public interface ITransactionScopeService
    {
        TransactionScope OpenTransactionScope(
            TransactionScopeOption transactionScopeOption = TransactionScopeOption.Required,
            IsolationLevel IsolationLevel = IsolationLevel.ReadCommitted,
            TimeSpan? timeout = null);
    }
}
