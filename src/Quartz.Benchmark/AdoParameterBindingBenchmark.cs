using System.Data.Common;

using BenchmarkDotNet.Attributes;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Benchmark;

/// <summary>
/// Preparing one statement and binding its parameters, which is what every trigger acquisition, every
/// trigger write and every check-in does before it reaches the database.
/// </summary>
/// <remarks>
/// The statement is the real acquisition SQL — around a kilobyte once the table prefix is folded in —
/// and it binds the seven parameters acquisition binds. The interesting case is a driver that does not
/// spell parameters with '@': Npgsql and Oracle use ':', MySQL '?'. Binding used to rewrite the whole
/// statement once per parameter; it is now rewritten once per statement and remembered.
/// </remarks>
[MemoryDiagnoser]
public class AdoParameterBindingBenchmark
{
    /// <summary>'@' is the no-op case, ':' the one that has to rewrite.</summary>
    [Params("@", ":")]
    public string ParameterNamePrefix { get; set; } = "@";

    private static readonly (string Name, object Value)[] acquisitionParameters =
    [
        ("schedulerName", "TestScheduler"),
        ("state", "WAITING"),
        ("noLaterThan", 638000000000000000L),
        ("noEarlierThan", 637000000000000000L),
        ("instanceId", "NODE-01"),
        ("autoPinSentinel", "*"),
        ("liveNodeCutoff", 637900000000000000L)
    ];

    private AdoUtil adoUtil = null!;
    private ConnectionAndTransactionHolder holder = null!;
    private string sql = null!;

    [GlobalSetup]
    public void Setup()
    {
        DbMetadata metadata = new()
        {
            ParameterNamePrefix = ParameterNamePrefix,
            BindByName = true
        };

        adoUtil = new AdoUtil(new StubDbProvider(metadata));
        holder = new ConnectionAndTransactionHolder(new StubDbConnection(), null);
        sql = AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectNextTriggerToAcquire, "QRTZ_");
    }

    /// <summary>
    /// What binding used to do: copy and scan the whole statement once for every parameter bound to it.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int RewritePerParameter()
    {
        DbCommand command = new StubDbCommand { CommandText = sql };
        holder.Attach(command);

        foreach ((string name, object value) in acquisitionParameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = ParameterNamePrefix + name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
            command.CommandText = LegacyRewrite(command.CommandText, name, ParameterNamePrefix);
        }

        return command.CommandText.Length;
    }

    /// <summary>
    /// What it does now: the statement is rewritten as the command is prepared, and the result is
    /// remembered against the text it came from, so this scan happens once per statement per process.
    /// </summary>
    [Benchmark]
    public int RewriteOncePerStatement()
    {
        DbCommand command = adoUtil.PrepareCommand(holder, sql);

        foreach ((string name, object value) in acquisitionParameters)
        {
            adoUtil.AddCommandParameter(command, name, value);
        }

        return command.CommandText.Length;
    }

    private static string LegacyRewrite(string commandText, string parameterName, string prefix)
    {
        return prefix == "@" ? commandText : commandText.Replace("@" + parameterName, prefix + parameterName);
    }
}
