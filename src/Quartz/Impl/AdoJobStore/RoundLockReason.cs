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
/// In the order the rule asks them. The first five are a property of the store and are decided once;
/// <see cref="OlderPeer" /> and <see cref="ClusterLimit" /> are decided per round before its first read;
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

    /// <summary>A live node older than 4.6 is in the cluster, or no check-in has read the state table yet.</summary>
    OlderPeer,

    /// <summary>The request carries a cluster-scoped execution limit, whose count is read under the lock.</summary>
    ClusterLimit,

    /// <summary>A candidate's job disallows concurrent execution.</summary>
    SerialJob,

    /// <summary>A candidate's overlap policy is one the fire decides under the lock.</summary>
    OverlapPolicy,

    /// <summary>A job or a trigger was replaced between the candidate read and the claim, into something the lock decides.</summary>
    DefinitionChanged,

    /// <summary>The round before this one escalated, and this one starts where it ended.</summary>
    Hysteresis,
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
        RoundLockReason.OlderPeer => "older_peer",
        RoundLockReason.ClusterLimit => "cluster_limit",
        RoundLockReason.SerialJob => "serial_job",
        RoundLockReason.OverlapPolicy => "overlap_policy",
        RoundLockReason.DefinitionChanged => "definition_changed",
        RoundLockReason.Hysteresis => "hysteresis",
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
        RoundLockReason.OlderPeer => "a live node older than 4.6 is in the cluster, or no check-in has read the state table yet",
        RoundLockReason.ClusterLimit => "the round counts against a cluster-scoped execution limit",
        RoundLockReason.SerialJob => "a candidate's job disallows concurrent execution",
        RoundLockReason.OverlapPolicy => "a candidate's overlap policy is decided under the lock",
        RoundLockReason.DefinitionChanged => "a job or trigger changed between the read and the claim",
        RoundLockReason.Hysteresis => "the previous round had to take it",
        _ => "unknown",
    };
}
