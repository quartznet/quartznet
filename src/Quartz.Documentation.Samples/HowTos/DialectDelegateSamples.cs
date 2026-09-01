using System.Data.Common;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Documentation.Samples.HowTos;

// This file has a second job, and it is worth knowing about before touching the project it lives in.
//
// Quartz.Documentation.Samples is NOT a friend assembly: Quartz grants it no InternalsVisibleTo. So a
// delegate written here is a delegate anyone could write, and a sample that stops compiling means the
// public driver-delegate kit has lost a type rather than that a sample went stale. That is the whole
// compile-time proof behind "the ADO namespace is a delegate-authoring kit"; adding an
// InternalsVisibleTo grant for this assembly would end it silently, with every sample still building.
// ConfiguredTypeNamesResolveTest asserts the grant is absent, so the guard cannot be lost by accident.

#region sample_dialect_delegate_subclass

public sealed class MyDatabaseDelegate : StdAdoDelegate
{
    // override only what differs
}

#endregion

/// <summary>
/// The individual overrides the page shows, each of which would be a member of a delegate like the
/// one above. They live on a second delegate so that each can be its own region.
/// </summary>
internal sealed class DialectDelegateOverrides : StdAdoDelegate
{
    #region sample_dialect_delegate_row_limiting

    // … LIMIT n (PostgreSQL, MySQL, SQLite) — or "ROWS" on Firebird
    protected override SqlRowLimit GetRowLimit(int count)
        => SqlRowLimit.AtStatementEnd("LIMIT", count);

    // SELECT TOP n …                              SqlRowLimit.InProjection("TOP", count)
    // SELECT * FROM ( … ) WHERE rownum <= n       SqlRowLimit.InEnclosingSelect("rownum", count)

    #endregion

    #region sample_dialect_delegate_paging

    protected override string ApplyPaging(string sql, bool takeLimited)
        => takeLimited
            ? sql + " LIMIT @" + AdoConstants.ParameterPageTake + " OFFSET @" + AdoConstants.ParameterPageSkip
            : sql + " LIMIT -1 OFFSET @" + AdoConstants.ParameterPageSkip;

    protected override void AddPagingParameters(DbCommand cmd, int skip, int take, bool takeLimited)
    {
        if (takeLimited)
        {
            AddCommandParameter(cmd, AdoConstants.ParameterPageTake, take);
        }

        AddCommandParameter(cmd, AdoConstants.ParameterPageSkip, skip);
    }

    #endregion

    #region sample_dialect_delegate_booleans

    public override object GetDbBooleanValue(bool booleanValue) => booleanValue ? "1" : "0";

    public override bool GetBooleanFromDbValue(object columnValue) => Convert.ToInt32(columnValue) == 1;

    #endregion

    // The rest of this class is not on the page. It is one override per category of statement hook, so
    // that a category which stops being reachable from outside Quartz breaks the build here rather than
    // in somebody's application.

    /// <summary>
    /// The acquisition statement, for a dialect that needs something a row limit cannot express — an
    /// index hint, say. Derive from the base rather than composing the statement, as MySQL does.
    /// </summary>
    protected override string GetSelectNextTriggerToAcquireSql(TriggerAcquisitionSqlShape shape)
        => base.GetSelectNextTriggerToAcquireSql(shape).Replace("{0}TRIGGERS t", "{0}TRIGGERS t /*+ index */");

    /// <inheritdoc cref="GetSelectNextTriggerToAcquireSql" />
    protected override string GetSelectMisfiredTriggersToRecoverSql(int count)
        => base.GetSelectMisfiredTriggersToRecoverSql(count).Replace("{0}TRIGGERS t", "{0}TRIGGERS t /*+ index */");

    /// <inheritdoc cref="GetSelectNextTriggerToAcquireSql" />
    protected override string GetCountMisfiredTriggersInStateSql()
        => base.GetCountMisfiredTriggersInStateSql().Replace("{0}TRIGGERS WHERE", "{0}TRIGGERS /*+ index */ WHERE");

    /// <summary>
    /// A whole <see cref="IDriverDelegate" /> member written out, rather than a statement handed back to
    /// the base. Everything it needs is reachable: the connection holder, the command preparation, the
    /// parameter binding, the table-prefix substitution, the column names, the scheduler name every
    /// statement is scoped by, and the mapping from the stored string back to a state.
    /// </summary>
    public override async ValueTask<StoredTriggerState> SelectTriggerState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        string sql = ReplaceTablePrefix(
            $"SELECT {AdoConstants.ColumnTriggerState} FROM {{0}}{AdoConstants.TableTriggers} "
            + $"WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName "
            + $"AND {AdoConstants.ColumnTriggerName} = @triggerName "
            + $"AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup");

        using DbCommand cmd = PrepareCommand(conn, sql);
        AddCommandParameter(cmd, "schedulerName", SchedulerName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        object? state = await cmd.ExecuteScalarAsync(cancellationToken);

        return StoredTriggerStates.FromStoredValue(state as string);
    }
}

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/dialect-delegate.md.
/// </summary>
public static class DialectDelegateSamples
{
    public static void Registration(IHostApplicationBuilder builder, string connectionString)
    {
        #region sample_dialect_delegate_registration

        builder.Services.AddQuartz(q =>
        {
            q.UsePersistentStore(s =>
            {
                s.UseDriverDelegate<MyDatabaseDelegate>();
                s.UseGenericDatabase("MyProvider", connectionString);
            });
        });

        #endregion
    }
}
