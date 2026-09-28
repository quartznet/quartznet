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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Weasel.CommandLine;
using Weasel.Core.CommandLine;

namespace Quartz.Tests.Unit.Weasel;

/// <summary>
/// JasperFx's Weasel commands against an application whose scheduler hands its SQLite schema to Weasel,
/// run the way <c>dotnet run -- db-apply</c> runs them: a host is built and never started.
/// </summary>
[NonParallelizable]
public sealed class SqliteWeaselCommandLineTest
{
    private SqliteTestDatabase database = null!;
    private string patchFile = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("weasel-cli");
        patchFile = Path.Combine(Path.GetTempPath(), $"quartz-weasel-{Guid.NewGuid():N}.sql");
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
        File.Delete(patchFile);
        File.Delete(patchFile.Replace(".sql", ".drop.sql", StringComparison.Ordinal));
    }

    [Test]
    public async Task AssertPatchApplyAndAssertAgain()
    {
        (await new AssertCommand().Execute(Input(new WeaselInput()))).Should().BeFalse("an empty database is not the model");

        (await new PatchCommand().Execute(Input(new PatchInput { FileName = patchFile }))).Should().BeTrue();
        (await File.ReadAllTextAsync(patchFile)).Should().Contain("CREATE TABLE IF NOT EXISTS QRTZ_JOB_DETAILS",
            "db-patch writes the DDL the apply would run, for a person to run by hand");

        (await new ApplyCommand().Execute(Input(new WeaselInput()))).Should().BeTrue();

        (await new AssertCommand().Execute(Input(new WeaselInput()))).Should().BeTrue("db-apply brought the database to the model");
        (await new AssertCommand().Execute(Input(new WeaselInput { DatabaseFlag = "weasel-cli" }))).Should().BeTrue(
            "the scheduler's name picks its database");
        (await new AssertCommand().Execute(Input(new WeaselInput { DatabaseFlag = "quartz://scheduler/weasel-cli" }))).Should().BeTrue(
            "and so does its subject URI");

        File.Delete(patchFile);
        (await new PatchCommand().Execute(Input(new PatchInput { FileName = patchFile }))).Should().BeTrue();
        File.Exists(patchFile).Should().BeFalse("there is nothing left to patch");
    }

    private T Input<T>(T input) where T : WeaselInput
    {
        input.HostBuilder = Host.CreateDefaultBuilder().ConfigureServices(services => services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "weasel-cli");
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.UseWeaselForSqlite();
            });
        }));

        return input;
    }
}
