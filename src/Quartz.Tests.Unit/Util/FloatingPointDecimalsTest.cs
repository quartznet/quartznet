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

using System;
using System.Data;
using System.Globalization;

using Quartz.Util;

namespace Quartz.Tests.Unit.Util;

/// <summary>
/// A double or a float read as a decimal comes back as the number that was written, on every runtime
/// (#4210). .NET 11 converts the binary value exactly, so without the rounding <c>12.34d</c> reads back
/// as <c>12.339999999999999857891452848m</c>.
/// </summary>
public class FloatingPointDecimalsTest
{
    [TestCase(12.34d, "12.34")]
    [TestCase(0.1d + 0.2d, "0.3")]
    [TestCase(-123.4567d, "-123.4567")]
    [TestCase(1234567.891d, "1234567.891")]
    [TestCase(1e20d, "100000000000000000000")]
    [TestCase(0d, "0")]
    public void ADoubleKeepsFifteenSignificantDigits(double stored, string expected)
    {
        FloatingPointDecimals.ToDecimal(stored).Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [TestCase(14.1f, "14.1")]
    [TestCase(0.1f, "0.1")]
    [TestCase(-2.5f, "-2.5")]
    public void AFloatKeepsSevenSignificantDigits(float stored, string expected)
    {
        FloatingPointDecimals.ToDecimal(stored).Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Test]
    public void AnythingElseConvertsAsBefore()
    {
        FloatingPointDecimals.ToDecimal(12.34m).Should().Be(12.34m);
        FloatingPointDecimals.ToDecimal(42).Should().Be(42m);
        FloatingPointDecimals.ToDecimal(42L).Should().Be(42m);
        FloatingPointDecimals.ToDecimal("56.78").Should().Be(56.78m);
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(1e30d)]
    public void ADoubleOutsideTheRangeStillOverflows(double stored)
    {
        Action act = () => FloatingPointDecimals.ToDecimal(stored);
        act.Should().Throw<OverflowException>();
    }

    [Test]
    public void ADecimalColumnADriverReturnsAsADoubleReadsBackAsWritten()
    {
        using DataTable table = new();
        table.Columns.Add("DEC_PROP_1", typeof(double));
        table.Columns.Add("DEC_PROP_2", typeof(float));
        table.Columns.Add("DEC_PROP_3", typeof(decimal));
        table.Rows.Add(12.34d, 14.1f, 56.78m);

        using IDataReader reader = table.CreateDataReader();
        reader.Read().Should().BeTrue();

        reader.GetDecimal("DEC_PROP_1").Should().Be(12.34m);
        reader.GetDecimal("DEC_PROP_2").Should().Be(14.1m);
        reader.GetDecimal("DEC_PROP_3").Should().Be(56.78m);
    }
}
