using Apilane.Common.Utilities;
using System;
using System.Data.Common;
using System.Transactions;

namespace Apilane.Data.Utilities
{
    /// <summary>
    /// Makes a database connection take part in the ambient <see cref="Transaction"/> (a
    /// <see cref="TransactionScope"/>) of the calling code, whenever the connection was opened earlier.
    /// </summary>
    public static class TransactionEnlistment
    {
        private const double CommandTimeoutMarginSeconds = 0.5;

        /// <summary>
        /// Joins the current transaction, if there is one, on a connection that is already open.
        /// Repositories call it before every command.
        /// </summary>
        /// <remarks>
        /// A connection joins a transaction scope by itself only when it is opened inside that scope. A
        /// repository keeps one connection for its whole life (a request), and it may have been opened
        /// before the scope began, for example by a read that came first. The scope would then not cover
        /// its commands: they would be committed one by one, nothing would be rolled back when the scope
        /// is not completed, and a Test of a custom endpoint would keep its changes.
        /// <para>
        /// Joining the transaction the connection is already in does nothing, on every provider. When the
        /// transaction ends, the connection leaves it by itself and the next scope is joined again.
        /// </para>
        /// <para>
        /// Only one connection can take part in a transaction: a second connection that joins the same
        /// scope fails on SQL Server (it would need a distributed transaction), on MySQL and on PostgreSQL.
        /// Work that needs another connection must run outside the scope.
        /// </para>
        /// </remarks>
        /// <exception cref="TransactionAbortedException">
        /// The current transaction already ended, for example because the scope timed out.
        /// </exception>
        public static void JoinAmbientTransaction(DbConnection connection)
        {
            // No transaction: nothing to join. Never pass null to EnlistTransaction, it throws while the
            // connection is still enlisted.
            var currentTransaction = Transaction.Current;
            if (currentTransaction is null)
            {
                return;
            }

            // A transaction that already ended must be refused here, before the provider is asked to join
            // it. MySQL and PostgreSQL would otherwise carry on and run the command outside any
            // transaction, where it is committed at once; MySqlConnector also starts a transaction on the
            // server before it enlists, so enlisting an ended one leaves that transaction open and the
            // connection unusable for the rest of the request.
            if (currentTransaction.TransactionInformation.Status != TransactionStatus.Active)
            {
                throw new TransactionAbortedException();
            }

            connection.EnlistTransaction(currentTransaction);
        }

        /// <summary>
        /// The command timeout, in seconds, for a command that is about to run. Inside a transaction scope
        /// that <see cref="Apilane.Common.Services.TransactionScopeService"/> opened it is kept below the time
        /// the scope has left, so the command fails before the scope times out. Outside such a scope it is
        /// <paramref name="commandTimeoutSeconds"/> as it is (0 means no limit).
        /// </summary>
        /// <remarks>
        /// MySqlConnector rolls a transaction back, when its scope times out, on the timer thread of
        /// System.Transactions and on the connection that a running command is using. That throws, nobody
        /// catches it, and the process ends, with every tenant it serves. SQL Server and PostgreSQL handle
        /// the timeout of a running command themselves.
        /// </remarks>
        /// <exception cref="TransactionAbortedException">
        /// The scope has too little time left for another command.
        /// </exception>
        public static int GetCommandTimeout(int commandTimeoutSeconds)
        {
            var remaining = TransactionTimeLimit.GetRemaining();
            if (remaining is null)
            {
                return commandTimeoutSeconds;
            }

            // Whole seconds, as the providers take them, and a margin so that the provider has cancelled
            // the command before the scope's own timer fires.
            var seconds = (int)Math.Floor(remaining.Value.TotalSeconds - CommandTimeoutMarginSeconds);
            if (seconds < 1)
            {
                throw new TransactionAbortedException("The transaction is about to time out.");
            }

            return commandTimeoutSeconds <= 0 ? seconds : Math.Min(commandTimeoutSeconds, seconds);
        }
    }
}
