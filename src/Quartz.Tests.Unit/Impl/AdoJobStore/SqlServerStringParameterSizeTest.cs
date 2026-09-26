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

using FakeItEasy;

using Microsoft.Data.SqlClient;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The size the SQL Server delegate binds a string parameter with.
/// </summary>
/// <remarks>
/// Every string is bound 4,000 wide so one statement stays one plan, and SqlClient <em>cuts</em> a value
/// to its parameter's size rather than refusing it. The execution history's captured log is the one
/// string that can be longer, and it has to reach its <c>nvarchar(max)</c> column whole.
/// </remarks>
public sealed class SqlServerStringParameterSizeTest
{
    [Test]
    public void AnOrdinaryStringIsBoundFourThousandWide()
    {
        SqlParameter parameter = Bind(new string('x', 200));

        parameter.Size.Should().Be(4000, "one size for every value keeps one statement one query plan");
    }

    [Test]
    public void AStringOfExactlyFourThousandIsStillBoundFourThousandWide()
    {
        Bind(new string('x', 4000)).Size.Should().Be(4000);
    }

    [Test]
    public void AStringLongerThanFourThousandIsBoundAsMax()
    {
        SqlParameter parameter = Bind(new string('é', 20_000));

        parameter.Size.Should().Be(-1,
            "SqlClient truncates a value to its parameter's size without a word, so a 20,000-character log "
            + "bound 4,000 wide would arrive as its first 4,000 characters");
        ((string) parameter.Value).Should().HaveLength(20_000);
    }

    private static SqlParameter Bind(string value)
    {
        IDbProvider dbProvider = A.Fake<IDbProvider>();
        A.CallTo(() => dbProvider.Metadata).Returns(new DbMetadata { BindByName = true, ParameterNamePrefix = "@" });

        SqlServerDelegate sqlServer = new();
        sqlServer.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            SchedulerName = "TESTSCHED",
            InstanceId = "INSTANCE",
            TypeLoader = new SimpleTypeLoader(),
            DbProvider = dbProvider
        });

        using SqlCommand command = new();
        sqlServer.AddCommandParameter(command, "executionLog", value);

        return command.Parameters.Cast<SqlParameter>().Single();
    }
}
