using System.Data.Common;

using BenchmarkDotNet.Attributes;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Benchmark;

/// <summary>
/// Preparing a statement the store <em>composes</em> rather than one it holds as a constant: the
/// key-set predicate a bulk trigger fetch builds for the chunk it is about to read.
/// </summary>
/// <remarks>
/// <para>
/// #3332 made the parameter-name rewrite happen once per statement instead of once per bound
/// parameter, and remembered the result against the statement <em>instance</em> it came from. A
/// composed statement is a fresh instance every time it is composed, so the open question was whether
/// the composed paths still rewrite on every call — which on a driver that binds positionally means
/// scanning and rebuilding a statement of a few kilobytes.
/// </para>
/// <para>
/// The two arms are the same composition and the same preparation, differing only in whether the
/// composed text passes through the table-prefix substitution's own cache on the way. That cache is
/// keyed by value and returns the instance it made the first time, so a hit there is also a hit in the
/// rewrite cache behind it. <see cref="ComposedWithoutInterning" /> is the miss the issue was worried
/// about; <see cref="ComposedThroughTheCaches" /> is what the store actually does.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class AdoDynamicStatementBenchmark
{
    /// <summary>
    /// The three parameter spellings the drivers use. <c>?</c> is the positional one — MySQL — where
    /// the rewrite also has to drop the name, so it is the case with the most work to redo.
    /// </summary>
    [Params("@", ":", "?")]
    public string ParameterNamePrefix { get; set; } = "@";

    /// <summary>Keys in the chunk, which is what decides how long the composed statement is.</summary>
    [Params(8, 128)]
    public int KeyCount { get; set; }

    private AdoUtil adoUtil = null!;
    private ConnectionAndTransactionHolder holder = null!;
    private string sqlPrefix = null!;
    private TriggerKey[] keys = null!;

    [GlobalSetup]
    public void Setup()
    {
        DbMetadata metadata = new()
        {
            ParameterNamePrefix = ParameterNamePrefix,
            // MySQL's '?' names nothing, so it binds by position; the others bind by name.
            BindByName = ParameterNamePrefix != "?"
        };

        adoUtil = new AdoUtil(new StubDbProvider(metadata));
        holder = new ConnectionAndTransactionHolder(new StubDbConnection(), null);
        sqlPrefix = StdAdoConstants.SqlSelectTriggersByKeysPrefix;

        keys = new TriggerKey[KeyCount];
        for (int i = 0; i < KeyCount; i++)
        {
            keys[i] = new TriggerKey("trigger" + i, "group");
        }
    }

    /// <summary>
    /// Composing the statement and substituting the table prefix without interning the result, so the
    /// rewrite cache is handed a string it has never seen and rescans the whole statement.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int ComposedWithoutInterning()
    {
        string composed = AdoJobStoreUtil.ReplaceTablePrefix(sqlPrefix + AdoUtil.BuildTriggerKeyPredicate(KeyCount, qualified: true), "QRTZ_");
        return Bind(composed);
    }

    /// <summary>
    /// What the store does: the composed text goes through the table-prefix cache, which hands back the
    /// instance it built the first time, so the rewrite behind it is a reference-keyed hit.
    /// </summary>
    [Benchmark]
    public int ComposedThroughTheCaches()
    {
        string composed = AdoJobStoreUtil.ReplaceTablePrefixCached(sqlPrefix + AdoUtil.BuildTriggerKeyPredicate(KeyCount, qualified: true), "QRTZ_");
        return Bind(composed);
    }

    private int Bind(string sql)
    {
        DbCommand command = adoUtil.PrepareCommand(holder, sql);
        adoUtil.AddCommandParameter(command, "schedulerName", "TestScheduler");

        for (int i = 0; i < keys.Length; i++)
        {
            adoUtil.AddCommandParameter(command, AdoUtil.TriggerKeyNameParameter(i), keys[i].Name);
            adoUtil.AddCommandParameter(command, AdoUtil.TriggerKeyGroupParameter(i), keys[i].Group);
        }

        return command.CommandText.Length;
    }
}
