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

using System.Reflection;

using Quartz.Diagnostics;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// The subset rule <see cref="OperationName.JobStore" /> documents, held to the code on both sides.
/// </summary>
/// <remarks>
/// <para>
/// A span name is a telemetry contract — a dashboard filters on it, an alert fires off it — so the set
/// of them is worth guarding as carefully as a public signature. Three things can go wrong and none of
/// them fails a build on its own: a constant is added and no store operation ever begins that span, a
/// span is begun under a name spelled inline instead of through a constant, and a mutating member is
/// added to <see cref="IJobStore" /> with no span at all. The first two are a bijection, the third is a
/// completeness property, and the exclusion list below is the written form of the rule that decides it.
/// </para>
/// <para>
/// The spans the tracing store begins are read out of its IL rather than from a list kept beside it: a
/// <c>const</c> reaches its use site inlined, so the literals in <c>TracingJobStore</c>'s method bodies
/// are exactly the names it passes to <c>Begin</c>, whether it named a constant or typed the string.
/// That is the point — a hand-typed name is the failure this catches.
/// </para>
/// </remarks>
public class OperationNameTest
{
    private const string JobStorePrefix = "Quartz.JobStore.";
    private const string DiagnosticsNamespace = "Quartz.Diagnostics";

    private static readonly Assembly quartzAssembly = typeof(IScheduler).Assembly;

    /// <summary>
    /// The constants, by the name they are declared under.
    /// </summary>
    private static readonly Dictionary<string, string> declaredOperations = typeof(OperationName.JobStore)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(field => field is { IsLiteral: true, IsInitOnly: false })
        .ToDictionary(field => field.Name, field => (string) field.GetRawConstantValue()!, StringComparer.Ordinal);

    /// <summary>
    /// Every <c>Quartz.JobStore.*</c> name the diagnostics types actually begin a span under.
    /// </summary>
    private static readonly SortedSet<string> spansBegun = ScanForStoreSpanNames();

    /// <summary>
    /// The members of <see cref="IJobStore" /> that are deliberately not traced, and why. This list is
    /// the rule <see cref="OperationName.JobStore" /> states, in a form the build can check: anything
    /// not on it is a mutating operation and must have a constant.
    /// </summary>
    private static readonly string[] membersDeliberatelyNotTraced =
    [
        // Lifecycle. Each happens once, outside any request, so its span would be a root of its own
        // with nothing to be a child of — and Initialize runs before the store knows its identity,
        // which is the tag every other span here carries.
        "Initialize",
        "Shutdown",
        "SchedulerStarted",
        "SchedulerPaused",
        "SchedulerResumed",

        // Reads. One database round trip, already inside the caller's span, and the store has nothing
        // to say about it that the caller does not already know it asked for.
        "Exists",
        "GetCalendar",
        "GetJob",
        "GetJobs",
        "GetTrigger",
        "GetTriggers",
        "GetTriggersForJob",
        "GetTriggerState",
        "QueryCalendarNames",
        "QueryClusterNodes",
        "QueryFireInstances",
        "QueryJobGroups",
        "QueryJobs",
        "QueryTriggerGroups",
        "QueryTriggers",

        // Advice, not an operation. It answers "how long should I wait before trying again" out of the
        // store's own configuration, touches nothing and cannot fail, so there is nothing to time.
        "GetAcquireRetryDelay"
    ];

    [Test]
    public void TheScanFoundTheSpansTheTracingStoreBegins()
    {
        // Guards the guard: a walk that silently found nothing would make the bijection below vacuous,
        // and reading IL is exactly the kind of code that fails that way.
        spansBegun.Should().HaveCountGreaterThan(20,
            "the tracing store begins a span for every mutating store operation, so a handful of names "
            + "means the IL walk lost its footing rather than that the store stopped tracing");
    }

    [Test]
    public void EveryDeclaredOperationNamesASpanTheTracingStoreBegins()
    {
        declaredOperations.Values.Except(spansBegun, StringComparer.Ordinal).Should().BeEmpty(
            "a constant nothing begins a span under is a name that will never appear in anyone's "
            + "telemetry, and it reads to the next person as an operation that is traced");
    }

    [Test]
    public void EverySpanTheTracingStoreBeginsIsDeclared()
    {
        spansBegun.Except(declaredOperations.Values, StringComparer.Ordinal).Should().BeEmpty(
            "the constants are what an application filters on, so a span begun under a name spelled "
            + "inline is a span nobody can find by reading this class");
    }

    [Test]
    public void EveryOperationIsNamedForTheOperation()
    {
        foreach ((string name, string value) in declaredOperations)
        {
            value.Should().Be(JobStorePrefix + name,
                "the constant is how the name is read and the string is how it is filtered, so the two "
                + "drifting apart hides the drift from everyone on both sides");
        }
    }

    [Test]
    public void EveryMutatingJobStoreMemberHasAnOperationName()
    {
        List<string> untraced = JobStoreMemberNames()
            .Where(name => !membersDeliberatelyNotTraced.Contains(name, StringComparer.Ordinal))
            .Where(name => !declaredOperations.ContainsKey(name))
            .ToList();

        untraced.Should().BeEmpty(
            "a store operation that changes state is traced, and adding one to the interface without a "
            + "span leaves a hole nobody notices until they go looking for the operation in a trace");
    }

    [Test]
    public void EveryOperationNameIsAJobStoreMember()
    {
        declaredOperations.Keys.Except(JobStoreMemberNames(), StringComparer.Ordinal).Should().BeEmpty(
            "a constant for an operation the interface no longer has outlives the span it named");
    }

    [Test]
    public void TheUntracedListNamesOnlyRealMembers()
    {
        membersDeliberatelyNotTraced.Should().OnlyHaveUniqueItems();

        membersDeliberatelyNotTraced.Except(JobStoreMemberNames(), StringComparer.Ordinal).Should().BeEmpty(
            "an entry that matches no member is an exemption that has stopped exempting anything, and "
            + "the list is the only written form of which operations are deliberately not traced");
    }

    /// <summary>
    /// The distinct method names on <see cref="IJobStore" />, overload pairs collapsed — an operation is
    /// one span whichever overload of it the scheduler called.
    /// </summary>
    private static HashSet<string> JobStoreMemberNames()
    {
        return typeof(IJobStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Walks the diagnostics types for the literals they name. The tracing store is the only thing that
    /// begins a store span, but scanning its whole namespace is what makes "somewhere else started one"
    /// a failure rather than an invisible second source of span names.
    /// </summary>
    private static SortedSet<string> ScanForStoreSpanNames()
    {
        SortedSet<string> names = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Type type in quartzAssembly.GetTypes())
        {
            // Nested display classes carry the lambdas, and they report their declaring type's namespace.
            if (!string.Equals(type.Namespace, DiagnosticsNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string literal in MethodBodyStrings.In(type))
            {
                if (literal.Length > JobStorePrefix.Length && literal.StartsWith(JobStorePrefix, StringComparison.Ordinal))
                {
                    names.Add(literal);
                }
            }
        }

        return names;
    }
}
