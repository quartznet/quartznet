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

using System.Collections.Specialized;
using System.Text.Json.Serialization;

using Quartz.Extensibility;

namespace Quartz.Serialization.SystemTextJson;

/// <summary>
/// The store format as metadata the compiler wrote: every type a persistent job store can ask
/// <see cref="Quartz.Impl.SystemTextJsonObjectSerializer" /> to resolve, listed so that writing a
/// trigger needs no reflection.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes an ADO job store survive a trimmed publish. <c>PublishTrimmed</c> sets
/// <c>JsonSerializer.IsReflectionEnabledByDefault</c> to false, and options carrying converters but no
/// resolver then have nothing to answer <c>GetTypeInfo</c> with — so the first trigger written threw
/// <c>Reflection-based serialization has been disabled for this application</c>. Listing the types
/// here answers them from generated metadata instead.
/// </para>
/// <para>
/// A listed type that the options carry a converter for is answered by that converter: the generated
/// code asks <c>options.Converters</c> before it reaches for the metadata it wrote, which is how a
/// <see cref="JobDataMap" /> still goes through <c>JobDataMapConverter</c>. So this list is not a
/// second description of the store format — the converters and serializers are still the only
/// description. It is the set of types that have to be answerable at all.
/// </para>
/// <para>
/// Three groups, each derived from a call site rather than from memory:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// What the store hands the serializer. <c>StdAdoDelegate.SerializeObject</c> goes through
/// <c>Serialize&lt;T&gt;</c>, which writes as <see cref="object" />, so the runtime type of every blob
/// has to be answerable; the reads name their type instead, and are
/// <c>GetObjectFromBlob&lt;JobDataMap&gt;</c>, <c>&lt;NameValueCollection&gt;</c>,
/// <c>&lt;ICalendar&gt;</c> and <c>&lt;IOperableTrigger&gt;</c>. <see cref="ITrigger" /> is here beside
/// them because <c>IObjectSerializer</c> is public and a caller may name the interface it holds.
/// </description>
/// </item>
/// <item>
/// <description>
/// What the converters dispatch on. <c>CronExpressionConverter</c>, <c>JobKeyConverter</c> and
/// <c>TriggerKeyConverter</c> own a type each, and those three are here. <c>TriggerConverter</c> and
/// <c>CalendarConverter</c> are asked for the <em>concrete</em> type at write time, and those are
/// <em>not</em> here: <see cref="SystemTextJsonSerializerRegistry" /> already holds the one list of
/// them, and answers for each out of the same generic registration that adds its serializer. A second
/// list here would be a list that can drift, and drift silently — a new built-in trigger left off it
/// would go back to reflecting with nothing to say so.
/// </description>
/// </item>
/// <item>
/// <description>
/// What a <see cref="JobDataMap" /> holds. Both sides are closed, over the one set
/// <see cref="JobDataValues" /> declares: the reader produces a string, a bool, an int, a long, a
/// double, null or a <c>Dictionary&lt;string, string&gt;</c> and nothing else, and the writer refuses
/// a value it could not produce. Every type in that set is listed here, so a trimmed application can
/// write all of it. Anything past them is the application's own choice, and reaches Quartz through
/// <see cref="SystemTextJsonSerializerRegistry.AddTypeInfoResolver" />.
/// </description>
/// </item>
/// </list>
/// <para>
/// <see cref="JsonSourceGenerationMode.Metadata" /> rather than the default, for the reason
/// <c>HttpApiJsonContext</c> gives: the generated write path bakes in the options it was declared with,
/// the store options never match them because they carry Quartz's converters, and so the generated
/// writers would be dead code.
/// </para>
/// <para>
/// <c>StoreFormatSourceGenerationTest</c> is what notices a type left out. Nothing else would: with
/// reflection on — which is every test host and every untrimmed application — the chain falls through
/// and the omission only shows up in a trimmed publish, which is where it hurts.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]

// What the store hands the serializer.
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(JobDataMap))]
[JsonSerializable(typeof(NameValueCollection))]
[JsonSerializable(typeof(ICalendar))]
[JsonSerializable(typeof(ITrigger))]
[JsonSerializable(typeof(IOperableTrigger))]

// What the converters dispatch on, minus the trigger and calendar types, which the registry answers for.
[JsonSerializable(typeof(CronExpression))]
[JsonSerializable(typeof(JobKey))]
[JsonSerializable(typeof(TriggerKey))]

// What a JobDataMap holds.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(char))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DateOnly))]
[JsonSerializable(typeof(TimeOnly))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class QuartzStoreJsonContext : JsonSerializerContext;
