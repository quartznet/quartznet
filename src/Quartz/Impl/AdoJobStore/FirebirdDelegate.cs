namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Delegate implementation for Firebird.
/// </summary>
public class FirebirdDelegate : StdAdoDelegate
{
    /// <summary>
    /// Firebird limits rows with a trailing <c>ROWS n</c>.
    /// </summary>
    protected override SqlRowLimit GetRowLimit(int count) => SqlRowLimit.AtStatementEnd("ROWS", count);
}
