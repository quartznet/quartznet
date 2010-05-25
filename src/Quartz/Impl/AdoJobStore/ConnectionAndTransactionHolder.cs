using System.Data;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Utility class to keep track of both active transaction
    /// and connection.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class ConnectionAndTransactionHolder
    {
        private readonly IDbConnection connection;
        private IDbTransaction transaction;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionAndTransactionHolder"/> class.
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <param name="transaction">The transaction.</param>
        public ConnectionAndTransactionHolder(IDbConnection connection, IDbTransaction transaction)
        {
            this.connection = connection;
            this.transaction = transaction;
        }

        /// <summary>
        /// Gets or sets the connection.
        /// </summary>
        /// <value>The connection.</value>
        public IDbConnection Connection
        {
            get { return connection; }
        }

        /// <summary>
        /// Gets or sets the transaction.
        /// </summary>
        /// <value>The transaction.</value>
        public IDbTransaction Transaction
        {
            get { return transaction; }
            set { transaction = value; }
        }
    }
}