using System.Data.Common;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Documentation.Samples.HowTos;

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

    // append (PostgreSQL, Firebird)
    protected override string GetSelectNextTriggerToAcquireSql(int maxCount, int excludedJobTypeBucket)
        => base.GetSelectNextTriggerToAcquireSql(maxCount, excludedJobTypeBucket) + " LIMIT " + maxCount;

    // splice a prefix (SQL Server: SELECT TOP n)
    // wrap the whole statement (Oracle: SELECT * FROM ( … ) WHERE rownum <= n)
    // append with an index hint (MySQL: FORCE INDEX (…) … LIMIT n)

    #endregion

    #region sample_dialect_delegate_paging

    protected override string ApplyPaging(string sql, bool takeLimited)
        => takeLimited
            ? sql + " LIMIT @pageTake OFFSET @pageSkip"
            : sql + " LIMIT -1 OFFSET @pageSkip";

    protected override void AddPagingParameters(DbCommand cmd, int skip, int take, bool takeLimited)
    {
        if (takeLimited)
        {
            AddCommandParameter(cmd, "pageTake", take);
        }

        AddCommandParameter(cmd, "pageSkip", skip);
    }

    #endregion

    #region sample_dialect_delegate_booleans

    public override object GetDbBooleanValue(bool booleanValue) => booleanValue ? "1" : "0";

    public override bool GetBooleanFromDbValue(object columnValue) => Convert.ToInt32(columnValue) == 1;

    #endregion
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
