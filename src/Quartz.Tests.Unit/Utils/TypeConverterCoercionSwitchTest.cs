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

using Quartz.Impl;
using Quartz.Util;

namespace Quartz.Tests.Unit.Utils;

/// <summary>
/// What a trimmed or native publish does with job data: <c>Quartz.JobData.IsTypeConverterCoercionSupported</c>
/// off, the built-in conversions still convert, anything else is refused naming the switch, a missing
/// value is a zeroed default, and a type name is the type-name switch's business rather than this one's.
/// </summary>
/// <remarks>
/// The switches are process-wide, so the fixture is non-parallelizable and puts each one back after the
/// test that flipped it.
/// </remarks>
[NonParallelizable]
public sealed class TypeConverterCoercionSwitchTest
{
    private FeatureSwitchScope converterOff = null!;

    [SetUp]
    public void TurnTheConverterOff()
    {
        converterOff = new FeatureSwitchScope(FeatureSwitches.TypeConverterCoercionSwitch, enabled: false);
    }

    [TearDown]
    public void TurnTheConverterBackOn()
    {
        converterOff.Dispose();
    }

    [Test]
    public void TheConversionsJobDataNeedsStillConvert()
    {
        ValueConverter.ConvertValueIfNecessary(typeof(int), "0x1F").Should().Be(31, "Int32Converter reads hexadecimal, and the fast path does too");
        ValueConverter.ConvertValueIfNecessary(typeof(DayOfWeek), "wednesday").Should().Be(DayOfWeek.Wednesday, "EnumConverter ignores case");
        ValueConverter.ConvertValueIfNecessary(typeof(DayOfWeek), 3).Should().Be(DayOfWeek.Wednesday, "a serializer may write an enum as its number");
        ValueConverter.ConvertValueIfNecessary(typeof(Guid), "d3b07384-d9a0-4c9f-8d6b-1b2b3c4d5e6f").Should().Be(new Guid("d3b07384-d9a0-4c9f-8d6b-1b2b3c4d5e6f"));
        ValueConverter.ConvertValueIfNecessary(typeof(TimeSpan), "00:01:30").Should().Be(TimeSpan.FromSeconds(90));
        ValueConverter.ConvertValueIfNecessary(typeof(DateTime), "  ").Should().Be(DateTime.MinValue, "DateTimeConverter reads blank as the minimum");
        ValueConverter.ConvertValueIfNecessary(typeof(int?), "").Should().BeNull("NullableConverter reads the empty string as no value");
        ValueConverter.ConvertValueIfNecessary(typeof(long), 42).Should().Be(42L);
        ValueConverter.ConvertValueIfNecessary(typeof(string), DayOfWeek.Wednesday).Should().Be("Wednesday");
    }

    [Test]
    public void AConversionOnlyATypeConverterCouldMakeIsRefusedNamingTheSwitch()
    {
        Action converting = () => ValueConverter.ConvertValueIfNecessary(typeof(Version), "1.2.3");

        converting.Should().Throw<NotSupportedException>()
            .WithMessage("*System.String*System.Version*")
            .WithMessage($"*{FeatureSwitches.TypeConverterCoercionSwitch} is false*")
            .WithMessage($"*<{FeatureSwitches.TypeConverterCoercionProperty}>true</{FeatureSwitches.TypeConverterCoercionProperty}>*",
                "VersionConverter would have read this, so the refusal has to say why it did not and how to turn it back on");
    }

    [Test]
    public void WithTheSwitchOnTheSameConversionGoesThroughTheTypeConverter()
    {
        converterOff.Dispose();

        ValueConverter.ConvertValueIfNecessary(typeof(Version), "1.2.3").Should().Be(new Version(1, 2, 3),
            "an untrimmed application converts through TypeDescriptor as it always has");
    }

    [Test]
    public void TheJobFactoryNamesThePropertyInFrontOfTheRefusal()
    {
        PropertySettingJobFactory factory = new() { PropertyMismatchBehavior = PropertyMismatchBehavior.Throw };
        JobDataMap data = new() { ["release"] = "1.2.3" };

        Action binding = () => factory.SetObjectProperties(new CoercedJob(), data);

        binding.Should().Throw<SchedulerException>()
            .WithMessage($"*{typeof(CoercedJob)}*'release'*System.Version*System.String*{FeatureSwitches.TypeConverterCoercionSwitch}*{FeatureSwitches.TypeConverterCoercionProperty}*",
                "one message names the job, the property, both types, the switch and the property that turns it back on");
    }

    [Test]
    public void TheJobFactoryStillBindsTheBuiltInConversions()
    {
        PropertySettingJobFactory factory = new() { PropertyMismatchBehavior = PropertyMismatchBehavior.Throw };
        CoercedJob job = new();
        JobDataMap data = new()
        {
            ["count"] = "42",
            ["day"] = "Friday",
            ["id"] = "d3b07384-d9a0-4c9f-8d6b-1b2b3c4d5e6f",
            ["delay"] = "00:00:05",
            ["limit"] = null!,
            ["origin"] = null!,
        };

        factory.SetObjectProperties(job, data);

        job.Count.Should().Be(42);
        job.Day.Should().Be(DayOfWeek.Friday);
        job.Id.Should().Be(new Guid("d3b07384-d9a0-4c9f-8d6b-1b2b3c4d5e6f"));
        job.Delay.Should().Be(TimeSpan.FromSeconds(5));
        job.Limit.Should().BeNull("a missing value for a Nullable<T> is null, with or without reflection");
        job.Origin.Should().Be(default(Point), "a missing value for a struct is its zeroed default");
    }

    [Test]
    public void AMissingValueIsTheZeroedDefault()
    {
        ValueConverter.ConvertValueIfNecessary(typeof(int), null).Should().Be(0);
        ValueConverter.ConvertValueIfNecessary(typeof(int?), null).Should().BeNull(
            "Activator.CreateInstance gives null for a Nullable<T>, and RuntimeHelpers.GetUninitializedObject would have given 0");
        ValueConverter.ConvertValueIfNecessary(typeof(DayOfWeek), null).Should().Be(DayOfWeek.Sunday);
        ValueConverter.ConvertValueIfNecessary(typeof(Point), null).Should().Be(default(Point));
        ValueConverter.ConvertValueIfNecessary(typeof(string), null).Should().BeNull();
    }

    [Test]
    public void AStructsOwnParameterlessConstructorRunsOnlyWithTheSwitchOn()
    {
        ValueConverter.ConvertValueIfNecessary(typeof(Seeded), null).Should().Be(default(Seeded),
            "with no reflection the default is all zeroes, which is the one documented difference from Activator.CreateInstance");

        converterOff.Dispose();

        ValueConverter.ConvertValueIfNecessary(typeof(Seeded), null).Should().Be(new Seeded(),
            "an untrimmed application gets what Activator.CreateInstance has always made, constructor and all");
    }

    [Test]
    public void ATypeNameIsTheTypeNameSwitchesBusiness()
    {
        ValueConverter.ConvertValueIfNecessary(typeof(Type), "System.String").Should().Be(typeof(string),
            "resolving a type name is not a TypeConverter coercion, so this switch does not refuse it");

        using FeatureSwitchScope namesOff = new(FeatureSwitches.TypeNameResolutionSwitch, enabled: false);

        RegisteredTypeNames.Register(typeof(RegisteredForValueConversion));
        ValueConverter.ConvertValueIfNecessary(typeof(Type), typeof(RegisteredForValueConversion).AssemblyQualifiedNameWithoutVersion())
            .Should().Be(typeof(RegisteredForValueConversion), "a registered name resolves from the table, as it does in SimpleTypeLoader");

        Action resolving = () => ValueConverter.ConvertValueIfNecessary(typeof(Type), "System.String");

        resolving.Should().Throw<TypeLoadException>()
            .WithMessage($"*{FeatureSwitches.TypeNameResolutionSwitch} is false*",
                "a name nothing registered is refused by the type-name switch, with the message every other refusal of a name carries");
    }

    private sealed class CoercedJob : IJob
    {
        public int Count { get; set; }

        public DayOfWeek Day { get; set; }

        public Guid Id { get; set; }

        public TimeSpan Delay { get; set; }

        public int? Limit { get; set; } = 7;

        public Point Origin { get; set; } = new(3, 4);

        public Version? Release { get; set; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    private readonly record struct Point(int X, int Y);

    private readonly struct Seeded : IEquatable<Seeded>
    {
        public Seeded()
        {
            Value = 42;
        }

        public int Value { get; }

        public bool Equals(Seeded other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is Seeded other && Equals(other);

        public override int GetHashCode() => Value;
    }

    public sealed class RegisteredForValueConversion : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
