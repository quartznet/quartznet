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
}
