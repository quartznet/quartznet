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
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Xml.Linq;

using Quartz.Core;

namespace Quartz.Tests.Unit;

/// <summary>
/// Which of <c>Quartz</c>'s two assets a test run loaded, and whether that asset is compiled with runtime
/// async (#4341).
/// </summary>
/// <remarks>
/// <c>Quartz</c> targets <c>net10.0</c> and <c>net11.0</c>, and only the <c>net11.0</c> asset is compiled with
/// <c>runtime-async=on</c>. These tests run in every build of this project. The <c>.NET 11</c> leg builds it
/// for <c>net11.0</c>, and the other legs build it for <c>net10.0</c>. The asset that was loaded decides what
/// each test expects; there is no <c>#if</c>.
/// </remarks>
public class RuntimeAsyncTest
{
    /// <summary>
    /// <c>MethodImplAttributes.Async</c>, which .NET 11 declares and <c>net10.0</c> does not. The compiler sets
    /// it on a method whose suspension and resumption it leaves to the runtime.
    /// </summary>
    private const MethodImplAttributes Async = (MethodImplAttributes) 0x2000;

    /// <summary>
    /// The tests load the asset NuGet picks for their own framework: the newest one <c>Quartz</c> ships that is
    /// not newer than it.
    /// </summary>
    /// <remarks>
    /// Without this check, the <c>.NET 11</c> leg could load the <c>net10.0</c> asset, rolled forward or copied
    /// in from another project's output, and <see cref="FirePathIsRuntimeAsyncOnlyInTheNet11Asset" /> would still
    /// pass by expecting a state machine.
    /// </remarks>
    [Test]
    public void TestsLoadTheAssetTheirFrameworkGets()
    {
        Version tests = FrameworkOf(typeof(RuntimeAsyncTest).Assembly);
        Version asset = FrameworkOf(typeof(JobRunShell).Assembly);

        Version[] shipped = ShippedTargets();
        Version expected = shipped.Where(x => x <= tests).Max();

        asset.Should().Be(expected,
            $"Quartz ships {string.Join(", ", shipped.Select(x => $"net{x}"))}, and a project built for net{tests} gets "
            + $"the net{expected} asset from NuGet and from a project reference alike. Loading net{asset} means "
            + "these tests are not running the code they were built against");
    }

    /// <summary>
    /// <see cref="JobRunShell.Run" />, which runs every job a trigger fires, is compiled for runtime async in
    /// the <c>net11.0</c> asset and as a compiler state machine in the <c>net10.0</c> one.
    /// </summary>
    [Test]
    public void FirePathIsRuntimeAsyncOnlyInTheNet11Asset()
    {
        Version asset = FrameworkOf(typeof(JobRunShell).Assembly);
        MethodInfo run = typeof(JobRunShell).GetMethod(nameof(JobRunShell.Run), [typeof(CancellationToken)]);
        run.Should().NotBeNull("JobRunShell.Run(CancellationToken) is the method that runs a fired job");

        bool runtimeAsync = (run!.MethodImplementationFlags & Async) == Async;
        AsyncStateMachineAttribute stateMachine = run.GetCustomAttribute<AsyncStateMachineAttribute>();

        if (asset.Major >= 11)
        {
            runtimeAsync.Should().BeTrue(
                $"the net{asset} asset is compiled with <Features>runtime-async=on</Features>, so the compiler leaves "
                + "the method to the runtime and sets MethodImplAttributes.Async on it");
            stateMachine.Should().BeNull(
                $"a runtime-async method has no compiler state machine, and the net{asset} asset's JobRunShell.Run has "
                + $"{stateMachine?.StateMachineType.Name}: src/Quartz/Quartz.csproj has stopped turning runtime async on");
        }
        else
        {
            runtimeAsync.Should().BeFalse(
                $"runtime async needs .NET 11, and the net{asset} asset is compiled without it");
            stateMachine.Should().NotBeNull(
                $"the net{asset} asset compiles an async method into a state machine, which this attribute names");
        }
    }

    /// <summary>
    /// The framework an assembly was built for, as its <see cref="TargetFrameworkAttribute" /> says.
    /// </summary>
    private static Version FrameworkOf(Assembly assembly)
    {
        TargetFrameworkAttribute attribute = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
        attribute.Should().NotBeNull($"{assembly.GetName().Name} is built by the .NET SDK, which records its target framework");

        FrameworkName framework = new(attribute!.FrameworkName);
        framework.Identifier.Should().Be(".NETCoreApp");

        return framework.Version;
    }

    /// <summary>
    /// The frameworks <c>src/Quartz/Quartz.csproj</c> targets, as versions.
    /// </summary>
    private static Version[] ShippedTargets()
    {
        string project = Path.Combine(RepositoryRoot.Find().FullName, "src", "Quartz", "Quartz.csproj");

        string targets = XDocument.Load(project).Descendants("TargetFrameworks").Select(x => x.Value.Trim()).SingleOrDefault();
        targets.Should().NotBeNullOrEmpty("Quartz names its frameworks in a single <TargetFrameworks>");

        return
        [
            .. targets!
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => Version.Parse(x["net".Length..]))
                .Order()
        ];
    }
}
