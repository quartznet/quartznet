#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Why a firing round takes <see cref="SchedulerLock.TriggerAccess" /> (#4067): the store's startup
/// line names the configured one, the acquisition metric tags each round with its own, and an
/// escalation event says what the round found.
/// </summary>
/// <remarks>
/// In the order the rule asks them. The first six are a property of the store and are decided once;
/// <see cref="OlderPeer" />, <see cref="NoLivePeer" /> and <see cref="ClusterLimit" /> are decided per round
/// before its first read, though <see cref="NoLivePeer" /> is the store's for good when it is not clustered;
/// <see cref="SerialJob" />, <see cref="OverlapPolicy" /> and <see cref="Delegate" /> are what the
/// candidate read can show; <see cref="DefinitionChanged" /> is what the post-claim read can show; and
/// <see cref="Hysteresis" /> is a round locked because the one before it had to be.
/// </remarks>
internal enum RoundLockReason
{
    /// <summary><c>LockAllOperations</c>: SQLite serializes every operation.</summary>
    LockAllOperations,

    /// <summary><c>AdoJobStoreOptions.LockFreeFiringRounds</c> is off.</summary>
    OptionOff,

    /// <summary><c>AdoJobStoreOptions.AcquireTriggersWithinLock</c> is on.</summary>
    AcquireTriggersWithinLock,

    /// <summary>The store's transactions run above read committed, where a lost claim is a serialization failure.</summary>
    IsolationLevel,

    /// <summary>The driver delegate does not take part in fire-on-acquire rounds, or did not read what the rule decides by.</summary>
    Delegate,

    /// <summary>
    /// SQL Server, and the database does not read committed snapshots — or could not be asked — so a round's
    /// read would take shared locks and wait on, or deadlock with, another node's claim (#4117). Its rounds
    /// work as with the option off.
    /// </summary>
    ReadCommittedSnapshotOff,

    /// <summary>
    /// A live node is older than the release whose rounds this one can run beside — 4.6, or 4.7 on SQL
    /// Server — or no check-in has read the state table yet.
    /// </summary>
    OlderPeer,

    /// <summary>
    /// The dialect's lock-free rounds help only beside another live node, and none is live: the store is not
    /// clustered, or the last check-in found no other live node (#4117). SQL Server: a single node has no
    /// <c>TRIGGER_ACCESS</c> contention to save, and would pay the split round's extra statements for nothing.
    /// </summary>
    NoLivePeer,

    /// <summary>The request carries a cluster-scoped execution limit, whose count is read under the lock.</summary>
    ClusterLimit,

    /// <summary>A candidate's job disallows concurrent execution.</summary>
    SerialJob,

    /// <summary>
    /// A candidate's overlap policy is one the fire decides under the lock, or the candidate measures its
    /// interval from completion, which holds it while it runs as <c>BufferOne</c> does.
    /// </summary>
    OverlapPolicy,

    /// <summary>
    /// A job or a trigger was replaced between the candidate read and the claim, into something the lock
    /// decides or into an execution group this node does not serve.
    /// </summary>
    DefinitionChanged,

    /// <summary>The round before this one escalated, and this one starts where it ended.</summary>
    Hysteresis,

    /// <summary>
    /// A round that took no lock lost a claim to another node, and the rounds after it take the lock until
    /// the backlog clears: a locked round comes back short, or the cool-down passes.
    /// </summary>
    Contention,
}

/// <summary>
/// How a <see cref="RoundLockReason" /> is spelled where an operator reads it.
/// </summary>
internal static class RoundLockReasons
{
    /// <summary>The value of the <c>quartz.jobstore.lock.reason</c> tag.</summary>
    public static string ToTag(this RoundLockReason reason) => reason switch
    {
        RoundLockReason.LockAllOperations => "lock_all_operations",
        RoundLockReason.OptionOff => "option_off",
        RoundLockReason.AcquireTriggersWithinLock => "acquire_within_lock",
        RoundLockReason.IsolationLevel => "isolation_level",
        RoundLockReason.Delegate => "delegate",
        RoundLockReason.ReadCommittedSnapshotOff => "read_committed_snapshot_off",
        RoundLockReason.OlderPeer => "older_peer",
        RoundLockReason.NoLivePeer => "no_live_peer",
        RoundLockReason.ClusterLimit => "cluster_limit",
        RoundLockReason.SerialJob => "serial_job",
        RoundLockReason.OverlapPolicy => "overlap_policy",
        RoundLockReason.DefinitionChanged => "definition_changed",
        RoundLockReason.Hysteresis => "hysteresis",
        RoundLockReason.Contention => "contention",
        _ => "unknown",
    };

    /// <summary>The reason as a log line reads it.</summary>
    public static string Describe(this RoundLockReason reason) => reason switch
    {
        RoundLockReason.LockAllOperations => "SQLite serializes every operation",
        RoundLockReason.OptionOff => "LockFreeFiringRounds is off",
        RoundLockReason.AcquireTriggersWithinLock => "AcquireTriggersWithinLock is on",
        RoundLockReason.IsolationLevel => "the transaction isolation level is above ReadCommitted",
        RoundLockReason.Delegate => "the driver delegate does not take part in fire-on-acquire rounds",
        RoundLockReason.ReadCommittedSnapshotOff =>
            "the SQL Server database does not have READ_COMMITTED_SNAPSHOT on, so firing rounds work as in 4.5; "
            + "turn it on with ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE and restart the scheduler",
        RoundLockReason.OlderPeer => "a live node is older than lock-free rounds allow (4.6, or 4.7 on SQL Server), or no check-in has read the state table yet",
        RoundLockReason.NoLivePeer =>
            "the store is not clustered, or no other live node is in its cluster, and SQL Server's lock-free rounds help only beside one, so firing rounds work as in 4.5",
        RoundLockReason.ClusterLimit => "the round counts against a cluster-scoped execution limit",
        RoundLockReason.SerialJob => "a candidate's job disallows concurrent execution",
        RoundLockReason.OverlapPolicy => "a candidate's overlap policy, or its hold while it runs, is decided under the lock",
        RoundLockReason.DefinitionChanged => "a job or trigger changed between the read and the claim",
        RoundLockReason.Hysteresis => "the previous round had to take it",
        RoundLockReason.Contention => "nodes are claiming the same triggers, until the backlog clears",
        _ => "unknown",
    };
}
