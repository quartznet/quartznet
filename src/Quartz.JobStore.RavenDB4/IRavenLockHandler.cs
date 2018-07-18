using System;
using System.Threading;
using System.Threading.Tasks;

namespace Quartz.Impl.RavenDB
{
    internal interface IRavenLockHandler
    {
        Task<bool> ObtainLock(
            Guid requestorId,
            RavenConnection connection,
            LockType lockType,
            CancellationToken cancellationToken = default);

        Task ReleaseLock(
            Guid requestorId,
            LockType lockType,
            CancellationToken cancellationToken = default);
    }
}