namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Delegate implementation for Firebird.
/// </summary>
public class FirebirdDelegate : StdAdoDelegate
{
    /// <summary>
    /// Gets the select next trigger to acquire SQL clause.
    /// FireBird version with ROWS support.
    /// </summary>
    /// <returns></returns>
    protected override string GetSelectNextTriggerToAcquireSql(TriggerAcquisitionSqlShape shape)
    {
        return base.GetSelectNextTriggerToAcquireSql(shape) + " ROWS " + shape.MaxCount;
    }

    protected override string GetSelectMisfiredTriggersToRecoverSql(int count)
    {
        if (count != -1)
        {
            return StdAdoConstants.SqlSelectMisfiredTriggersToRecover + " ROWS " + count;
        }
        return base.GetSelectMisfiredTriggersToRecoverSql(count);
    }
}
