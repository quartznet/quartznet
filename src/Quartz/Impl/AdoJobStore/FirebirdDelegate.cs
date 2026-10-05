namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Delegate implementation for Firebird.
/// </summary>
public class FirebirdDelegate : StdAdoDelegate
{
    /// <inheritdoc />
    protected override string? SchemaResourceName => "Quartz.Impl.AdoJobStore.Schema.create_firebird.sql";

    /// <summary>
    /// Firebird limits rows with a trailing <c>ROWS n</c>.
    /// </summary>
    protected override SqlRowLimit GetRowLimit(int count) => SqlRowLimit.AtStatementEnd("ROWS", count);

    /// <summary>
    /// The widths <c>create_firebird.sql</c> declares. A <c>VARCHAR</c> counts bytes in a database
    /// created without a default character set, and the store cannot tell which kind it is talking to.
    /// </summary>
    internal override int? TextColumnByteWidth(string column) => column switch
    {
        AdoConstants.ColumnPauseReason => 1000,
        AdoConstants.ColumnPausedBy => 800,
        AdoConstants.ColumnProgressMessage => 250,
        AdoConstants.ColumnErrorMessage => 1000,
        AdoConstants.ColumnSummary => 1000,
        AdoConstants.ColumnLastSummary => 1000,
        AdoConstants.ColumnLastFailureMessage => 1000,
        _ => null,
    };

    /// <summary>
    /// Firebird divides two integers as integers, but cannot type a bare parameter that divides a column inside
    /// a common table expression, which the run statistics read their rows in; so the bucket size is cast.
    /// </summary>
    internal override string HistoryStatisticsBucketExpression => StdAdoConstants.SqlStatisticsBucketByTypedDivision;
}
