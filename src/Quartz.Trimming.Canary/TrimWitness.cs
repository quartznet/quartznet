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

namespace Quartz.Trimming.Canary;

/// <summary>
/// The one trim warning a canary reports on purpose, so that a publish reporting nothing against Quartz
/// can be told apart from a publish whose trimmer never ran.
/// </summary>
/// <remarks>
/// <para>
/// <c>build/Build.cs</c> requires every canary publish — both canaries, trimmed and native — to report
/// <c>IL2057</c> against this type, and fails one that reports any other warning against a Quartz type.
/// Until #4082 the proof was one of Quartz's own recorded <c>ValueConverter</c> warnings. Quartz records
/// none now, so the witness has to be something no fix to Quartz can take away.
/// </para>
/// <para>
/// <see cref="Type.GetType(string, bool)" /> over a string the analysis cannot see is the oldest shape
/// the baseline ever recorded, and ILLink and ILCompiler both report it. The pragma silences only the
/// compile-time analyzer, which would otherwise fail the build on it: a pragma never reaches IL, so the
/// two tools that read IL still see the call. <c>Quartz.Trimming.Canary.Wire</c> compiles this file too,
/// and each canary calls <see cref="Run" /> first, because a method nothing reaches is trimmed before it
/// is analysed.
/// </para>
/// </remarks>
internal static class TrimWitness
{
    public static void Run()
    {
        // A variable, not a constant: the analysis resolves a constant name and has nothing to report.
        string name = Environment.GetEnvironmentVariable("QUARTZ_TRIM_WITNESS_TYPE") ?? "System.Object";

#pragma warning disable IL2057 // The witness build/Build.cs requires of every canary publish; see the remarks.
        Type? resolved = Type.GetType(name, throwOnError: false);
#pragma warning restore IL2057

        Console.WriteLine($"PASS trim-witness: the deliberate Type.GetType(\"{name}\") ran and found {resolved?.FullName ?? "nothing"}.");
    }
}
