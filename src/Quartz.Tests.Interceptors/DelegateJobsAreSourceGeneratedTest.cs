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

using System.Reflection;
using System.Text;

namespace Quartz.Tests.Interceptors;

/// <summary>
/// Holds what no other test here can say for itself: that the generator intercepted every
/// <c>AddJob(name, handler)</c> and <c>ScheduleJob(name, handler)</c> it should have, in this assembly.
/// </summary>
/// <remarks>
/// <para>
/// A call the generator declines is bound by reflection instead, and every other test in this project
/// passes either way — that is the promise. So the one thing a regression in the generator would not
/// break is the tests, and this counts the interceptions in the built assembly instead, the way
/// <c>RequestDelegatesAreSourceGeneratedTest</c> counts ASP.NET Core's.
/// </para>
/// <para>
/// Each count is every such call in the file less the ones the generator declines on purpose, and a
/// call added to or removed from the file changes the number here too.
/// </para>
/// </remarks>
public class DelegateJobsAreSourceGeneratedTest
{
    /// <summary>
    /// The intercepted calls in each file.
    /// </summary>
    /// <remarks>
    /// <c>DelegateJobTest.cs</c> has 24 calls, and <c>ScheduleJob("answer", () =&gt; Task.FromResult(42), …)</c>
    /// is the one declined: a result is refused by Quartz, in Quartz's words, when the call runs.
    /// </remarks>
    private static readonly Dictionary<string, int> interceptedCalls = new(StringComparer.Ordinal)
    {
        ["CompiledDelegateJobBindingTest.cs"] = 13,
        ["DelegateJobTest.cs"] = 23,
    };

    [Test]
    public void EveryDelegateJobCallIsInterceptedWhereItShouldBe()
    {
        Dictionary<string, int> intercepted = Interceptions()
            .GroupBy(FileOf)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        intercepted.Should().BeEquivalentTo(interceptedCalls,
            "a call the generator stops intercepting is bound by reflection instead, and passes every other test here; "
            + "counting per file is what tells one call going unintercepted apart from the total happening to add up");
    }

    /// <summary>
    /// The generated interceptors, which the compiler emits as a file-local type — so its metadata name
    /// carries a hash of the generated file's name and cannot be written down.
    /// </summary>
    private static Type GeneratedInterceptors()
    {
        Type[] candidates = typeof(DelegateJobsAreSourceGeneratedTest).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "Quartz.Generated")
            .Where(type => type.Name.EndsWith("DelegateJobInterceptors", StringComparison.Ordinal))
            .ToArray();

        candidates.Should().ContainSingle(
            "the generator emits one interceptor class per compilation, and none when the project does not list "
            + "Quartz.Generated in InterceptorsNamespaces - which buildTransitive/net10.0/Quartz.targets does");

        return candidates[0];
    }

    private static List<CustomAttributeData> Interceptions() => GeneratedInterceptors()
        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        .SelectMany(method => method.GetCustomAttributesData())
        .Where(attribute => attribute.AttributeType.Name.EndsWith("InterceptsLocationAttribute", StringComparison.Ordinal))
        .ToList();

    /// <summary>
    /// The source file an interception points at.
    /// </summary>
    /// <remarks>
    /// Roslyn's version 1 location is base64 of a 16-byte content checksum, a 4-byte position and the
    /// display file name. Nothing else decodes it, so a version this does not know about fails loudly
    /// rather than being read as if it were version 1.
    /// </remarks>
    private static string FileOf(CustomAttributeData interception)
    {
        interception.ConstructorArguments[0].Value.Should().Be(1,
            "this decodes Roslyn's version 1 interceptable location; a new version needs a new decoder here");

        byte[] data = Convert.FromBase64String((string) interception.ConstructorArguments[1].Value!);
        data.Length.Should().BeGreaterThan(20, "a version 1 location is a checksum, a position and a file name");

        return Encoding.UTF8.GetString(data, 20, data.Length - 20);
    }
}
