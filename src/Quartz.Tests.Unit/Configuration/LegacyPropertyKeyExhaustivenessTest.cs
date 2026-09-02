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

#nullable enable

using System.Collections.Specialized;
using System.Reflection;

using Quartz.Configuration;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// The completeness property behind "a misspelled key throws".
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LegacyPropertyKeys.Validate" /> rejects any <c>quartz.*</c> key it does not recognise,
/// which is only a kindness while the recognised list is exhaustive. If a reader consults a key the
/// list has never heard of, the guard turns a working configuration into a startup failure — and it
/// does so for exactly the 3.x-migration shape the guard was written for. The other tests around
/// here check that the guard fires; this one checks that it fires only on real mistakes.
/// </para>
/// <para>
/// The key inventory is extracted mechanically rather than curated, by asking
/// <see cref="MethodBodyStrings" /> for the literals every type in the <c>Quartz.Configuration</c>
/// namespace names and keeping the ones that begin with <c>quartz.</c>. That is where every reader of
/// the flat format lives — the bridge, the plugin factory, the execution-limit parser, the property
/// binder — and
/// because <c>const</c> strings are inlined at their use sites, a key named through
/// <see cref="LegacyPropertyKeys" /> shows up at the reader just as a literal one does.
/// <see cref="LegacyPropertyKeys" /> itself is skipped: it declares the lists rather than reading
/// them, and it is the thing under test. <c>QuartzConfigurationHelper</c> is skipped for the mirror
/// reason: the keys it names are the ones it refuses to synthesize because a typed binding owns them,
/// so it is the one type in the namespace where naming a key means the opposite of consulting it. Two
/// of those keys a reader does consult, and both stay in the scan because the bridge names them too;
/// the rest are keys nothing reads by design, which is exactly what the validator would reject.
/// </para>
/// <para>
/// A curated list of the keys the documentation promises backs the scan up, because a key the
/// documentation teaches and no reader consults is a different bug with the same symptom.
/// </para>
/// </remarks>
public class LegacyPropertyKeyExhaustivenessTest
{
    private const string Prefix = "quartz.";
    private const string ConfigurationNamespace = "Quartz.Configuration";

    private static readonly Assembly quartzAssembly = typeof(IScheduler).Assembly;

    /// <summary>
    /// Every <c>quartz.*</c> key or key prefix the flat-format readers name, as found in their IL.
    /// </summary>
    private static readonly IReadOnlyList<string> keysTheReadersConsult = ScanConfigurationReaders();

    /// <summary>
    /// Keys the 4.x documentation tells a reader to write. Placeholders in the documentation
    /// (<c>NAME</c>) are filled in with a plausible name, because these keys are matched by prefix.
    /// </summary>
    private static readonly string[] documentedKeys =
    [
        // configuration/reference.md
        "quartz.checkConfiguration",
        "quartz.context.key.environment",
        "quartz.dataSource.myDs.provider",
        "quartz.dataSource.myDs.connectionString",
        "quartz.dataSource.myDs.connectionStringName",
        "quartz.dataSource.myDs.connectionProvider.type",
        "quartz.dbprovider.MyDatabase.productName",
        "quartz.jobStore.acceptEnlistedTransactions",
        "quartz.jobStore.clusterCheckinInterval",
        "quartz.jobStore.clusterCheckinMisfireThreshold",
        "quartz.jobStore.clustered",
        "quartz.jobStore.dataSource",
        "quartz.jobStore.driverDelegateInitString",
        "quartz.jobStore.lockHandler.type",
        "quartz.jobStore.makeThreadsDaemons",
        "quartz.jobStore.misfireThreshold",
        "quartz.jobStore.tablePrefix",
        "quartz.jobStore.type",
        "quartz.jobStore.useProperties",
        "quartz.plugin.myPlugin.type",
        "quartz.scheduler.batchTriggerAcquisitionFireAheadTimeWindow",
        "quartz.scheduler.batchTriggerAcquisitionMaxCount",
        "quartz.scheduler.idleWaitTime",
        "quartz.scheduler.instanceId",
        "quartz.scheduler.instanceName",
        "quartz.scheduler.interruptJobsOnShutdown",
        "quartz.scheduler.interruptJobsOnShutdownWithWait",
        "quartz.scheduler.jobFactory.type",
        "quartz.scheduler.typeLoadHelper.type",
        "quartz.serializer.type",
        "quartz.threadExecutor",
        "quartz.threadPool.maxConcurrency",
        "quartz.threadPool.type",

        // tutorial/job-stores.md
        "quartz.jobStore.driverDelegateType",

        // tutorial/execution-groups.md
        "quartz.executionLimit.batch-jobs",

        // migration-guide.md
        "quartz.timeProvider.type",

        // migration-guide.md, the SchemaProvisioning row: the key that replaced
        // performSchemaValidation, and the one it replaced, which still bridges
        "quartz.jobStore.schemaProvisioning",
        "quartz.jobStore.performSchemaValidation"
    ];

    /// <summary>
    /// Keys the 4.x documentation says are rejected, and the section that says so.
    /// </summary>
    private static readonly string[] documentedRemovals =
    [
        // configuration/reference.md, "Removed in 4.x"
        "quartz.scheduler.proxy",
        "quartz.scheduler.exporter",
        "quartz.scheduler.threadName",
        "quartz.scheduler.makeSchedulerThreadDaemon",

        // migration-guide.md
        "quartz.jobStore.lockHandler.tablePrefix",
        "quartz.jobStore.lockHandler.schedName",
        "quartz.jobStore.lockHandler.schedulerName",

        // configuration/reference.md, the keys it says are rejected rather than ignored
        "quartz.jobListener.myListener.type",
        "quartz.triggerListener.myListener.type"
    ];

    /// <summary>
    /// The listener keys, whose advice has to name the registration that replaced them.
    /// </summary>
    /// <remarks>
    /// A listener named by configuration carried no matchers, which is the whole reason the keys went;
    /// advice that only said "removed" would leave the reader to guess where matchers now live.
    /// </remarks>
    private static readonly (string Key, string Replacement)[] listenerKeys =
    [
        ("quartz.jobListener.audit.type", "AddJobListener<T>(matchers)"),
        ("quartz.triggerListener.audit.type", "AddTriggerListener<T>(matchers)")
    ];

    /// <summary>
    /// The lock handler identity keys, spelled the way 3.x spelled them.
    /// </summary>
    /// <remarks>
    /// These are the two keys whose advice is aimed squarely at a file copied out of a 3.x
    /// application, so getting the spelling wrong costs the whole entry: a key nobody ever wrote is
    /// rejected by name, and the key they did write falls through to "unknown configuration
    /// property". 3.x derived both from the property names on <c>ITablePrefixAware</c> —
    /// <c>TablePrefix</c> and <c>SchedName</c> — which <c>StdSchedulerFactory</c> wrote into the lock
    /// handler's property group as <c>tablePrefix</c> and <c>schedName</c>.
    /// </remarks>
    private static readonly string[] threeXTablePrefixAwareKeys =
    [
        "quartz.jobStore.lockHandler.tablePrefix",
        "quartz.jobStore.lockHandler.schedName"
    ];

    [Test]
    public void TheScanFoundTheKeysTheReadersConsult()
    {
        // Guards the guard: a scanner that silently found nothing would make every assertion below
        // vacuous, and IL walking is exactly the kind of code that fails that way.
        keysTheReadersConsult.Should().HaveCountGreaterThan(30);

        keysTheReadersConsult.Should().Contain(
            [
                "quartz.jobStore.type",
                "quartz.jobStore.driverDelegateType",
                "quartz.threadPool.threadCount",
                "quartz.threadPool.maxConcurrency",
                "quartz.serializer.type",
                "quartz.scheduler.instanceName",
                "quartz.dbprovider"
            ],
            "a mix of inline literals and LegacyPropertyKeys constants, so the scan has to see both "
            + "spellings — a constant reaches the reader's IL inlined, exactly like a literal. The last "
            + "two are also named by the flattener's deny-list, which is skipped, so they prove that "
            + "skipping it costs the scan nothing a reader consults");
    }

    [Test]
    public void EveryKeyTheReadersConsultIsOneTheValidatorAccepts()
    {
        List<string> rejected = [];
        foreach (string key in keysTheReadersConsult)
        {
            if (Rejection(key) is { } message)
            {
                rejected.Add($"{key}: {message}");
            }
        }

        rejected.Should().BeEmpty(
            "a key some reader consults is by definition not a misspelling, so rejecting it would break "
            + "a configuration that works — this is the property that makes the unknown-key guard safe "
            + "to have at all");
    }

    [Test]
    public void NoKeyTheReadersConsultIsAlsoListedAsRemoved()
    {
        List<string> overlaps = [];
        foreach (string key in keysTheReadersConsult)
        {
            foreach ((string prefix, string _) in LegacyPropertyKeys.removedKeys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    overlaps.Add($"{key} is read but '{prefix}' is advertised as removed");
                }
            }
        }

        overlaps.Should().BeEmpty(
            "the removed list is checked before the supported list, so a key on both is rejected with "
            + "advice telling the reader to remove a key Quartz still reads");
    }

    [Test]
    public void EveryRemovedKeyIsRejectedByNameAndWithItsAdvice()
    {
        LegacyPropertyKeys.removedKeys.Should().NotBeEmpty();

        foreach ((string prefix, string advice) in LegacyPropertyKeys.removedKeys)
        {
            string? message = Rejection(prefix);

            message.Should().NotBeNull($"'{prefix}' is advertised as removed, so it must be rejected");
            message.Should().Contain(prefix, "the reader has to be told which key in their file is the problem");
            message.Should().Contain(advice,
                "'unknown property' reads like a typo; a key that was configuring something real earns "
                + "an explanation of what to do instead");
        }
    }

    [Test]
    public void TheSupportedListIsInternallyConsistent()
    {
        string[] supported = LegacyPropertyKeys.supportedKeys;

        supported.Should().NotBeEmpty();
        supported.Should().OnlyHaveUniqueItems("a key listed twice is a merge accident, not a policy");
        supported.Should().OnlyContain(key => key.StartsWith(Prefix, StringComparison.Ordinal) && key.Length > Prefix.Length,
            "the validator only ever looks at keys under the quartz. prefix, so an entry outside it can never match");
        supported.Should().OnlyContain(key => !key.StartsWith("quartz.server", StringComparison.Ordinal),
            "quartz.server.* belonged to 3.x's Quartz.Server host and is skipped before this list is consulted");
    }

    [Test]
    public void EveryDocumentedKeyIsOneTheValidatorAccepts()
    {
        List<string> rejected = [];
        foreach (string key in documentedKeys)
        {
            if (Rejection(key) is { } message)
            {
                rejected.Add($"{key}: {message}");
            }
        }

        rejected.Should().BeEmpty(
            "the documentation is the other half of the contract: a key it teaches and the validator "
            + "rejects fails a reader who did exactly what they were told");
    }

    [Test]
    public void EveryDocumentedRemovalIsRejected()
    {
        List<string> accepted = documentedRemovals.Where(key => Rejection(key) is null).ToList();

        accepted.Should().BeEmpty(
            "the documentation promises these are rejected rather than ignored, which is the whole point "
            + "of listing them by name");
    }

    [Test]
    public void TheLockHandlerIdentityKeysAreRejectedInTheSpelling3xUsed()
    {
        List<string> missed = threeXTablePrefixAwareKeys.Where(key => Rejection(key) is null).ToList();

        missed.Should().BeEmpty(
            "these are the keys a file copied out of a 3.x application actually contains, and advice "
            + "attached to a spelling nobody ever wrote reaches nobody — the reader gets 'unknown "
            + "configuration property' for the key they really have");

        foreach (string key in threeXTablePrefixAwareKeys)
        {
            Rejection(key).Should().Contain("ILockHandler.Initialize",
                "the point of naming the key is to say which seam replaced it");
        }
    }

    [Test]
    public void TheListenerKeysAreRejectedNamingTheRegistrationThatReplacedThem()
    {
        foreach ((string key, string replacement) in listenerKeys)
        {
            string? message = Rejection(key);

            message.Should().NotBeNull(
                $"'{key}' named a listener that Quartz no longer builds, so leaving the key merely unread "
                + "would silently stop attaching a listener that used to be attached");
            message.Should().Contain(replacement,
                "a listener named by configuration could carry no matchers, so the advice has to name the "
                + "registration that takes them rather than only saying the key is gone");
        }
    }

    [Test]
    public void QuartzServerKeysAreNotSchedulerKeys()
    {
        Rejection("quartz.server.scheduler.instanceName").Should().BeNull(
            "3.x's Quartz.Server host read its own settings out of the same file, and a configuration "
            + "carried over from it still has them - they are not misspelled scheduler keys");
    }

    /// <summary>
    /// Runs the validator over a bag holding just this key, and returns the complaint or
    /// <see langword="null" /> when the key is accepted.
    /// </summary>
    private static string? Rejection(string key)
    {
        NameValueCollection properties = new NameValueCollection { [key] = "value" };

        try
        {
            LegacyPropertyKeys.Validate(properties);
            return null;
        }
        catch (SchedulerConfigException exception)
        {
            return exception.Message;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Reading the keys back out of the readers
    // ---------------------------------------------------------------------------------------------

    private static IReadOnlyList<string> ScanConfigurationReaders()
    {
        SortedSet<string> keys = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Type type in quartzAssembly.GetTypes())
        {
            // Nested display classes carry the lambdas, and they report their declaring type's namespace.
            if (!string.Equals(type.Namespace, ConfigurationNamespace, StringComparison.Ordinal)
                || type == typeof(LegacyPropertyKeys)
                || string.Equals(type.Name, "QuartzConfigurationHelper", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string literal in MethodBodyStrings.In(type))
            {
                // The bare prefix is manufactured by QuartzConfigurationHelper rather than read.
                if (literal.Length > Prefix.Length && literal.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    keys.Add(literal);
                }
            }
        }

        return keys.ToList();
    }
}
