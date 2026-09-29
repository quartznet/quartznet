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

using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// How a job's reported metrics become the JSON object its history row keeps.
/// </summary>
public sealed class ExecutionMetricsWriterTest
{
    private static readonly JobKey job = new("reconcile", "billing");

    private FakeLogger logger = null!;

    [SetUp]
    public void SetUp()
    {
        logger = new FakeLogger();
    }

    [Test]
    public void NoMetricsIsNoJson()
    {
        ExecutionMetricsWriter.Write(null, logger, job).Should().BeNull();
        ExecutionMetricsWriter.Write(new Dictionary<string, object?>(), logger, job).Should().BeNull(
            "an empty object says nothing a null does not, and costs a column write");
    }

    [TestCase("text", "\"text\"")]
    [TestCase(true, "true")]
    [TestCase(false, "false")]
    [TestCase(null, "null")]
    [TestCase(42, "42")]
    [TestCase(-42L, "-42")]
    [TestCase((short) 7, "7")]
    [TestCase((byte) 8, "8")]
    [TestCase((sbyte) -9, "-9")]
    [TestCase((ushort) 10, "10")]
    [TestCase(11u, "11")]
    [TestCase(18446744073709551615ul, "18446744073709551615")]
    [TestCase(1.5d, "1.5")]
    [TestCase(2.25f, "2.25")]
    [TestCase(double.NaN, "\"NaN\"")]
    [TestCase(double.PositiveInfinity, "\"Infinity\"")]
    [TestCase(float.NegativeInfinity, "\"-Infinity\"")]
    [TestCase(DayOfWeek.Friday, "\"Friday\"")]
    public void EachValueIsWrittenAsItsJsonKind(object? value, string expected)
    {
        Write(value).Should().Be("{\"value\":" + expected + "}");
    }

    [Test]
    public void ADecimalStaysANumber()
    {
        Write(1234.5678m).Should().Be("{\"value\":1234.5678}");
    }

    [Test]
    public void InstantsAreRoundTripStrings()
    {
        DateTimeOffset instant = new(2026, 9, 29, 12, 30, 0, TimeSpan.FromHours(3));

        Write(instant).Should().Be("{\"value\":\"2026-09-29T12:30:00.0000000\\u002B03:00\"}",
            "the round-trip format keeps the offset, and the encoder escapes the '+' like every other HTML-sensitive character");
        Write(new DateTime(2026, 9, 29, 9, 30, 0, DateTimeKind.Utc)).Should().Be("{\"value\":\"2026-09-29T09:30:00.0000000Z\"}");
    }

    [Test]
    public void ADurationIsItsConstantForm()
    {
        Write(new TimeSpan(1, 2, 3, 4, 500)).Should().Be("{\"value\":\"1.02:03:04.5000000\"}");
    }

    [Test]
    public void AGuidIsItsHyphenatedForm()
    {
        Guid id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        Write(id).Should().Be("{\"value\":\"0f8fad5b-d9cb-469f-a165-70867728950e\"}");
    }

    [Test]
    public void AnythingElseIsItsText()
    {
        Write(new Version(4, 4)).Should().Be("{\"value\":\"4.4\"}");
        Write(new Uri("https://example.com/a")).Should().Be("{\"value\":\"https://example.com/a\"}",
            "a type the writer does not know is written as its ToString, never serialized by reflection");
    }

    [Test]
    [SetCulture("de-DE")]
    public void NumbersAndFormattablesAreWrittenInTheInvariantCulture()
    {
        Write(1.5d).Should().Be("{\"value\":1.5}", "a JSON number has one decimal separator whatever the culture");
        Write(double.NegativeInfinity).Should().Be("{\"value\":\"-Infinity\"}");
        Write(new DateOnly(2026, 9, 29)).Should().Be("{\"value\":\"09/29/2026\"}",
            "an IFormattable is asked for its invariant text, not the host's German one");
        Write(Half.One + Half.One / (Half) 2).Should().Be("{\"value\":\"1.5\"}");
    }

    [Test]
    public void TheNamesAreKeptInTheOrderTheJobGaveThem()
    {
        JobRunReport report = JobRunReport.Succeeded().With("scanned", 1200).With("released", 3).With("tenant", "acme");

        string json = ExecutionMetricsWriter.Write(report.Metrics, logger, job)!;

        json.Should().Be("{\"scanned\":1200,\"released\":3,\"tenant\":\"acme\"}");
        using JsonDocument document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("released").GetInt32().Should().Be(3);
    }

    [Test]
    public void TheOutputIsAscii()
    {
        string json = ExecutionMetricsWriter.Write(new Dictionary<string, object?> { ["größe"] = "Grüße" }, logger, job)!;

        json.Should().MatchRegex("^[\\x20-\\x7E]*$",
            "escaping everything outside ASCII makes the limit in characters a limit in bytes too, which is what "
            + "a column that counts bytes needs");

        using JsonDocument document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("größe").GetString().Should().Be("Grüße", "escaping changes the text, not the value");
    }

    [Test]
    public void AnObjectAtTheLimitIsKept()
    {
        // {"v":"…"} is 8 characters of structure around the text.
        string json = ExecutionMetricsWriter.Write(
            new Dictionary<string, object?> { ["v"] = new string('a', JobRunReport.MaxMetricsLength - 8) },
            logger,
            job)!;

        json.Should().HaveLength(JobRunReport.MaxMetricsLength);
        logger.Collector.Count.Should().Be(0);
    }

    [Test]
    public void AnObjectOverTheLimitIsDroppedWholeAndSaysSoOnce()
    {
        Dictionary<string, object?> metrics = new()
        {
            ["small"] = 1,
            ["huge"] = new string('a', JobRunReport.MaxMetricsLength),
            ["after"] = 2
        };

        ExecutionMetricsWriter.Write(metrics, logger, job).Should().BeNull(
            "a JSON object cut short is not JSON, and dropping one metric of several would misreport the run");

        FakeLogRecord record = logger.Collector.GetSnapshot().Should().ContainSingle(
            "one report is one event, however many values it had").Which;
        record.Id.Id.Should().Be(1059, "the event id is what an operator filters and alerts on");
        record.Level.Should().Be(LogLevel.Warning);
        record.Message.Should().Contain("billing.reconcile").And.Contain("4000");
    }

    [Test]
    public void AnObjectThatOnlyItsClosingBraceTakesOverTheLimitIsDroppedToo()
    {
        ExecutionMetricsWriter.Write(
            new Dictionary<string, object?> { ["v"] = new string('a', JobRunReport.MaxMetricsLength - 7) },
            logger,
            job).Should().BeNull();

        logger.Collector.Count.Should().Be(1);
    }

    [Test]
    public void AValueThatThrowsWhenWrittenDropsTheMetricsAndSaysWhich()
    {
        Dictionary<string, object?> metrics = new()
        {
            ["scanned"] = 1200,
            ["tenant"] = new ThrowingValue(),
            ["released"] = 3
        };

        string? json = null;
        Action write = () => json = ExecutionMetricsWriter.Write(metrics, logger, job);

        write.Should().NotThrow("a value's ToString is the job's code, and what it throws must not cost the run its history row");
        json.Should().BeNull("a JSON object cut short is not JSON");

        FakeLogRecord record = logger.Collector.GetSnapshot().Should().ContainSingle().Which;
        record.Id.Id.Should().Be(1060, "a value that throws is a different fault from one that is too large");
        record.Level.Should().Be(LogLevel.Warning);
        record.Message.Should().Contain("billing.reconcile").And.Contain("tenant", "the event names the metric that threw");
        record.Exception.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("the tenant is not loaded");
    }

    [Test]
    public void AFormattableThatThrowsDropsTheMetricsToo()
    {
        ExecutionMetricsWriter.Write(new Dictionary<string, object?> { ["amount"] = new ThrowingFormattable() }, logger, job)
            .Should().BeNull();

        logger.Collector.GetSnapshot().Should().ContainSingle().Which.Id.Id.Should().Be(1060);
    }

    [Test]
    public void ADictionaryThatThrowsWhileReadDropsTheMetricsToo()
    {
        ExecutionMetricsWriter.Write(new ThrowingDictionary(), logger, job).Should().BeNull(
            "the dictionary is the job's code as well");

        logger.Collector.GetSnapshot().Should().ContainSingle().Which.Id.Id.Should().Be(1060);
    }

    private string? Write(object? value)
    {
        return ExecutionMetricsWriter.Write(new Dictionary<string, object?> { ["value"] = value }, logger, job);
    }

    /// <summary>A metric whose text the job cannot produce.</summary>
    internal sealed class ThrowingValue
    {
        public override string ToString() => throw new InvalidOperationException("the tenant is not loaded");
    }

    private sealed class ThrowingFormattable : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) => throw new FormatException("no culture");
    }

    private sealed class ThrowingDictionary : IReadOnlyDictionary<string, object?>
    {
        public int Count => 1;

        public IEnumerable<string> Keys => throw new NotSupportedException();

        public IEnumerable<object?> Values => throw new NotSupportedException();

        public object? this[string key] => throw new NotSupportedException();

        public bool ContainsKey(string key) => false;

        public bool TryGetValue(string key, out object? value)
        {
            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => throw new InvalidOperationException("the source was disposed");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
