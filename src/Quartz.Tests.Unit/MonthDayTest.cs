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

using System.Text;

namespace Quartz.Tests.Unit;

public class MonthDayTest
{
    [Test]
    public void TheTextFormIsTheIso8601RecurringMonthDay()
    {
        new MonthDay(12, 25).ToString().Should().Be("--12-25");
        new MonthDay(2, 29).ToString().Should().Be("--02-29");
    }

    [Test]
    public void ParseReadsWhatToStringWrote()
    {
        MonthDay value = new MonthDay(2, 29);

        MonthDay.Parse(value.ToString()).Should().Be(value);
        MonthDay.Parse(value.ToString().AsSpan()).Should().Be(value);
    }

    [TestCase("--12-25")]
    [TestCase("--01-01")]
    [TestCase("--02-29")]
    public void TryParseAcceptsTheFixedForm(string text)
    {
        MonthDay.TryParse(text, out MonthDay parsed).Should().BeTrue();
        parsed.ToString().Should().Be(text);
    }

    [TestCase(null, TestName = "TryParse rejects null")]
    [TestCase("")]
    [TestCase("12-25")]
    [TestCase("--12-25 ")]
    [TestCase("--1-25")]
    [TestCase("--12/25")]
    [TestCase("--13-01", TestName = "TryParse rejects a month out of range")]
    [TestCase("--02-30", TestName = "TryParse rejects a day the month does not have")]
    [TestCase("--00-01")]
    [TestCase("--01-00")]
    [TestCase("--ab-cd")]
    public void TryParseRejectsAnythingElse(string text)
    {
        MonthDay.TryParse(text, out MonthDay parsed).Should().BeFalse();
        parsed.Should().Be(default(MonthDay));
    }

    [Test]
    public void ParseThrowsWhatTheBclInterfacesPromise()
    {
        Action parseNull = () => MonthDay.Parse(null!);
        parseNull.Should().Throw<ArgumentNullException>();

        Action parseGarbage = () => MonthDay.Parse("25 December");
        parseGarbage.Should().Throw<FormatException>().WithMessage("*--MM-DD*");
    }

    [Test]
    public void TryFormatWritesSevenCharacters()
    {
        Span<char> destination = stackalloc char[7];

        new MonthDay(12, 25).TryFormat(destination, out int charsWritten, format: default, provider: null).Should().BeTrue();

        charsWritten.Should().Be(7);
        destination.ToString().Should().Be("--12-25");
    }

    [Test]
    public void TryFormatWritesSevenUtf8Bytes()
    {
        Span<byte> destination = stackalloc byte[7];

        new MonthDay(12, 25).TryFormat(destination, out int bytesWritten, format: default, provider: null).Should().BeTrue();

        bytesWritten.Should().Be(7);
        Encoding.UTF8.GetString(destination).Should().Be("--12-25");
    }

    [Test]
    public void TryFormatDeclinesADestinationThatIsTooShort()
    {
        Span<char> chars = stackalloc char[6];
        new MonthDay(12, 25).TryFormat(chars, out int charsWritten, format: default, provider: null).Should().BeFalse();
        charsWritten.Should().Be(0);

        Span<byte> bytes = stackalloc byte[6];
        new MonthDay(12, 25).TryFormat(bytes, out int bytesWritten, format: default, provider: null).Should().BeFalse();
        bytesWritten.Should().Be(0);
    }

    [Test]
    public void FormatStringsAndProvidersAreIgnoredBecauseTheFormIsFixed()
    {
        MonthDay value = new MonthDay(12, 25);

        value.ToString("anything", System.Globalization.CultureInfo.GetCultureInfo("fi-FI")).Should().Be("--12-25");
        MonthDay.Parse("--12-25", System.Globalization.CultureInfo.GetCultureInfo("fi-FI")).Should().Be(value);
    }
}
