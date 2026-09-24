---
title: 'A Driver Delegate for a New Database'
---

# A Driver Delegate for a New Database

Quartz ships driver delegates for SQL Server, PostgreSQL, MySQL, Oracle, SQLite and Firebird. Another
database, or one of those behind a provider that behaves differently, needs its own.

::: tip Start here
**Do not implement `IDriverDelegate`**: nothing in the product does, and most of its roughly hundred and ten
members are the same SQL everywhere. Subclass `StdAdoDelegate` and override what differs. The six shipped
dialects override **ten distinct members between them**; Firebird and PostgreSQL need two each.
:::

## The seam

`StdAdoDelegate` is `public`, unsealed, with a public parameterless constructor:

<!-- snippet: sample_dialect_delegate_subclass -->
```csharp
public sealed class MyDatabaseDelegate : StdAdoDelegate
{
    // override only what differs
}
```
<!-- endSnippet -->

These ten members are the dialect contract. Treat its other `protected virtual` members as private
implementation steps.

| Member | Override when |
|---|---|
| `protected virtual string? SchemaResourceName` | you want `ProvisionSchema()` to create your tables — see [Also needed](#also-needed-a-dbmetadata-and-a-schema) |
| `protected virtual SqlRowLimit GetRowLimit(int count)` | your database can limit the rows a statement returns |
| `protected virtual string GetSelectNextTriggerToAcquireSql(TriggerAcquisitionSqlShape shape)` | the acquisition statement needs more than a row limit (MySQL's index hint is the only shipped case) |
| `protected virtual string GetSelectMisfiredTriggersToRecoverSql(int count)` | the same, for the misfire scan; `count == -1` means "no limit" |
| `protected virtual string GetCountMisfiredTriggersInStateSql()` | the counting form needs a different shape |
| `protected virtual string ApplyPaging(string sql, bool takeLimited)` | `OFFSET … FETCH NEXT …` is not understood |
| `protected virtual void AddPagingParameters(DbCommand cmd, int skip, int take, bool takeLimited)` | your paging clause names the two parameters in a different order |
| `public virtual void AddCommandParameter(DbCommand cmd, string paramName, object? paramValue, Enum? dataType = null, int? size = null)` | the provider needs types or sizes set explicitly |
| `public virtual object GetDbBooleanValue(bool value)` | there is no boolean column type |
| `public virtual bool GetBooleanFromDbValue(object columnValue)` | the same, reading back |

### Row limiting

There is no ANSI row limit, so `StdAdoDelegate` applies none. Trigger acquisition and the misfire scan both
ask `GetRowLimit`:

<!-- snippet: sample_dialect_delegate_row_limiting -->
```csharp
// … LIMIT n (PostgreSQL, MySQL, SQLite) — or "ROWS" on Firebird
protected override SqlRowLimit GetRowLimit(int count)
    => SqlRowLimit.AtStatementEnd("LIMIT", count);

// SELECT TOP n …                              SqlRowLimit.InProjection("TOP", count)
// SELECT * FROM ( … ) WHERE rownum <= n       SqlRowLimit.InEnclosingSelect("rownum", count)
```
<!-- endSnippet -->

* `SqlRowLimit` names the three places a limit can go; the statement is built with it, not spliced.
* `count` is always at least one: "every row" (`-1`) arrives as `SqlRowLimit.Unlimited`.
* `TriggerAcquisitionSqlShape` carries what changes the statement text (the row limit, and how many
  job-type exclusion terms `NOT IN` needs). It is also the statement's cache key, so it holds a bucketed
  exclusion count, not the exact one.
* Override `GetSelectNextTriggerToAcquireSql(shape)` only for what a row limit cannot express, as MySQL does
  for `FORCE INDEX`, still calling `base` and leaving the limit alone.

### Paging

The default is ANSI, understood by SQL Server 2012+, Oracle 12c+, PostgreSQL and Firebird 3+:

```sql
 OFFSET @pageSkip ROWS FETCH NEXT @pageTake ROWS ONLY
```

MySQL and SQLite override `ApplyPaging` and `AddPagingParameters`: their clause names the parameters in the
other order, and positional providers bind in statement order. Use `AdoConstants.ParameterPageSkip` and
`AdoConstants.ParameterPageTake`, prefixed with `@` in SQL and bare when binding:

<!-- snippet: sample_dialect_delegate_paging -->
```csharp
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
```
<!-- endSnippet -->

* `takeLimited` is `false` for an unbounded page (`Take = int.MaxValue`). Without an offset-only form, MySQL
  uses a `LIMIT` of the largest `BIGINT UNSIGNED`, SQLite `LIMIT -1`.
* The base passes `take` as **the page size plus one**; the extra row makes `PagedResult<T>.HasMore` exact.

### Booleans

Oracle has no boolean column type, so its delegate maps both ways:

<!-- snippet: sample_dialect_delegate_booleans -->
```csharp
public override object GetDbBooleanValue(bool booleanValue) => booleanValue ? "1" : "0";

public override bool GetBooleanFromDbValue(object columnValue) => Convert.ToInt32(columnValue) == 1;
```
<!-- endSnippet -->

`IS_DURABLE`, `REQUESTS_RECOVERY` and similar columns bind through `GetDbBooleanValue`; the two must agree.

### Parameters

`AddCommandParameter` is the last resort. SQL Server's delegate converts booleans to `1`/`0`, sets
`size = -1` for varbinary, and pins strings to `size = 4000` so the server does not build a query plan per
string length.

## What the delegate cannot reach

::: warning
`StdAdoConstants` (the SQL text) is **internal**. The schema is the contract, public in `AdoConstants`.
:::

* You cannot write `StdAdoConstants.SqlSelectNextTriggerToAcquire`. Transform
  `base.GetSelectNextTriggerToAcquireSql(shape)` (as MySQL's `.Replace("{0}TRIGGERS t", …)` does), or write
  the statement whole.
* You can name the schema: `AdoConstants.TableTriggers`, `AdoConstants.ColumnTriggerName`,
  `AdoConstants.StateWaiting` and the rest.
* `{0}` is the table-prefix placeholder; `protected string ReplaceTablePrefix(string query)` substitutes
  it. Statements from the base still contain it; the caller substitutes.

::: tip
"Customize one statement" is not supported. The six SQL hooks cover what differs between databases; the
other ~76 statements are inlined `ReplaceTablePrefix(StdAdoConstants.X)` calls. Override the method that
issues the statement instead: every `IDriverDelegate` member on `StdAdoDelegate` is `virtual`, including
`UpdateTriggerStateFromOtherStateWithNextFireTime`, which the lock-free acquisition path uses to claim a
trigger. More `GetXxxSql()` hooks can be added compatibly; ask if you need one.
:::

::: warning The value conversions are not all seams
`GetDbBooleanValue` / `GetBooleanFromDbValue` are `virtual`. `GetDbDateTimeValue`, `GetDateTimeFromDbValue`,
`GetDbTimeSpanValue` and `GetTimeSpanFromDbValue` are not: UTC ticks and whole milliseconds are schema
contract, and the preferred-node liveness SQL does tick arithmetic on `LAST_CHECKIN_TIME` and
`CHECKIN_INTERVAL`, so changing them would silently break failover for pinned triggers. A database that must
store `DATETIME` natively implements `IDriverDelegate` directly and owns its SQL.
:::

## Initialization

`StdAdoDelegate.Initialize(DriverDelegateContext context)` is `public virtual`; no shipped dialect overrides
it. Override it only to register extra trigger persistence delegates or capture something from the context,
and call `base.Initialize(context)` first.

| `DriverDelegateContext` member | |
|---|---|
| `TablePrefix`, `SchedulerName`, `InstanceId` | required |
| `DbProvider`, `TypeLoader` | required |
| `ObjectSerializer` | nullable |
| `TriggerPersistenceDelegates` | the ones registered for this scheduler |
| `TimeProvider` | the scheduler's clock |
| `CommandTimeout` | from `AdoJobStoreOptions.CommandTimeout` |

It arrives after construction because a generated `InstanceId` does not exist when the container builds the
delegate. The base exposes `protected string SchedulerName { get; }` (which scopes nearly every statement)
and `protected IDbProvider DbProvider { get; }`, so you need no copies.

## Registering it

<!-- snippet: sample_dialect_delegate_registration -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(s =>
    {
        s.UseDriverDelegate<MyDatabaseDelegate>();
        s.UseGenericDatabase("MyProvider", connectionString);
    });
});
```
<!-- endSnippet -->

::: warning Order matters
Registration is **first-wins** (`TryAdd`). `UseSqlServer`, `UsePostgres` and the rest call
`UseDriverDelegate<…>()` internally, so `UseDriverDelegate<MyDatabaseDelegate>()` must come **before** the
database method or it is silently ignored.
:::

* The delegate is built with `ActivatorUtilities`, so **constructor dependencies work** (an
  `ILogger<MyDatabaseDelegate>`, anything in the container).
* `UseDriverDelegate(factory)` takes a delegate you build. The factory's provider resolves this scheduler's
  own parts; an `IDriverDelegate` registered on `Services` would be invisible to a named scheduler, which
  resolves under its own key. `UseSerializer`, `UseLockHandler`, `UseConnectionProvider` and
  `UseTriggerPersistenceDelegate` take factories the same way.
* The legacy `quartz.jobStore.driverDelegateType` key still selects a delegate by type name, even when the
  store is selected in code.

## Also needed: a DbMetadata and a schema

1. **The delegate** — this page.
2. **A provider registration.** `UseGenericDatabase(provider, connectionString, describeMetadata)` takes a
   `Func<DbMetadata>` describing the ADO.NET provider: connection, command and parameter types, parameter
   prefix, and `DbType` spelling. Provider and delegate are independent; the `Use…` shortcuts set both.
3. **DDL.** Copy the closest `database/tables/tables_<dialect>.sql` and adjust the column types.

Running the DDL by hand satisfies the default `SchemaProvisioning.Validate`. For `ProvisionSchema()` to
create the tables:

1. Embed the script (`<EmbeddedResource Include="MyDatabase.sql" />`).
2. Return its manifest resource name, `<RootNamespace>.<folder path>.<file name>`, from `SchemaResourceName`.
3. Separate statements with a line reading `--;;` (a semicolon may be inside a stored-procedure body), and
   write `{0}` for the table prefix.

<!-- snippet: sample_dialect_delegate_schema_resource -->
```csharp
// The schema script this delegate creates its tables from, embedded beside it with
// <EmbeddedResource Include="MyDatabase.sql" /> and named the way the compiler names one:
// <RootNamespace>.<folder path>.<file name>. Statements are separated by a line reading "--;;".
protected override string? SchemaResourceName => "MyCompany.Quartz.MyDatabase.sql";
```
<!-- endSnippet -->

The name is looked up in the delegate type's assembly, then up its base chain, so a subclass of a shipped
dialect inherits its script. Unset, `ProvisionSchema()` fails with a message naming this member.

## See also

* [Job Stores](../tutorial/job-stores.md) — how the ADO store is put together
* [A Job Store of Your Own](custom-job-store.md) — the layer above this one
* [Persisting a Custom Trigger Type](trigger-persistence-delegate.md) — the other delegate seam
