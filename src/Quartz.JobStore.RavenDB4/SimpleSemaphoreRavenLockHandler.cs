using System;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Impl.RavenDB
{
    internal class SimpleSemaphoreRavenLockHandler : IRavenLockHandler
    {
        private readonly SimpleSemaphore simpleSemaphore = new SimpleSemaphore();

        public Task<bool> ObtainLock(
            Guid requestorId, 
            RavenConnection connection, 
            LockType lockType,
            CancellationToken cancellationToken = default)
        {
            return simpleSemaphore.ObtainLock(requestorId, null, GetLockName(lockType), cancellationToken);
        }

        public Task ReleaseLock(
            Guid requestorId, 
            LockType lockType,
            CancellationToken cancellationToken = default)
        {
            return simpleSemaphore.ReleaseLock(requestorId, GetLockName(lockType), cancellationToken);
        }

        private static string GetLockName(LockType lockType)
        {
            string lockName;
            switch (lockType)
            {
                case LockType.None:
                    lockName = null;
                    break;
                case LockType.TriggerAccess:
                    lockName = "TriggerAccess";
                    break;
                case LockType.StateAccess:
                    lockName = "StateAccess";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lockType), lockType, null);
            }
            return lockName;
        }
    }
}