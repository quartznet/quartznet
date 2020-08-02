using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Npgsql;

using NUnit.Framework;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Integration.Impl.AdoJobStore
{
    public class JobStoreSupportTest
    {
        
        [Test]
        public void CanDetectTransientException()
        {
            var jobStoreSupport = new TestJobStoreSupport();
            var npgsqlException = new NpgsqlException("timeout", new TimeoutException());
            Assert.That(jobStoreSupport.IsTransientPublic(npgsqlException), Is.True);
            
            var sqlException = new SqlExceptionSimulator();   
            Assert.That(jobStoreSupport.IsTransientPublic(sqlException), Is.True);
        }

        private class SqlExceptionSimulator : Exception
        {
            public IEnumerable<SqlErrorSimulator> Errors => new List<SqlErrorSimulator>()
            {
                new SqlErrorSimulator()
            };

            public class SqlErrorSimulator
            {
                public int Number => 49920;
            }
        }

        private class TestJobStoreSupport : JobStoreSupport
        {
            protected override ConnectionAndTransactionHolder GetNonManagedTXConnection()
            {
                throw new NotImplementedException();
            }

            protected override Task<T> ExecuteInLock<T>(string lockName, Func<ConnectionAndTransactionHolder, Task<T>> txCallback, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public bool IsTransientPublic(Exception ex) => IsTransient(ex);
        }
    }
}