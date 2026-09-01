---
title: 'A Driver Delegate for a New Database'
---

# A Driver Delegate for a New Database

Quartz ships driver delegates for SQL Server, PostgreSQL, MySQL, Oracle, SQLite and Firebird. A
database that is not one of those — or one of those behind a provider that behaves differently — needs
a delegate of its own.

::: tip Start here
**Do not implement `IDriverDelegate`.** Nothing in the product does; it is a hundred-odd members, and
almost all of them are the same SQL on every database. Subclass `StdAdoDelegate` and override the
handful that differ. The six shipped dialects override **nine distinct members between them**, out of
roughly a hundred and ten — and four of the six override two or fewer.
:::

## The seam

`StdAdoDelegate` is `public` and unsealed, with a public parameterless constructor:

<!-- snippet: sample_dialect_delegate_subclass -->
```csharp
public sealed class MyDatabaseDelegate : StdAdoDelegate
{
    // override only what differs
}
```
<!-- endSnippet -->

Nine members are the dialect contract. Everything else on `StdAdoDelegate` is an implementation step
that happens to be `protected virtual` so the class can be composed — treat them as private.

| Member | Override when |
|---|---|
| `protected virtual SqlRowLimit GetRowLimit(int count)` | your database can limit the rows a statement returns |
| `protected virtual string GetSelectNextTriggerToAcquireSql(TriggerAcquisitionSqlShape shape)` | the acquisition statement needs something else besides — MySQL's index hint is the only shipped case |
| `protected virtual string GetSelectMisfiredTriggersToRecoverSql(int count)` | the same, for the misfire scan; `count == -1` means "no limit" |
| `protected virtual string GetCountMisfiredTriggersInStateSql()` | the counting form needs a different shape |
| `protected virtual string ApplyPaging(string sql, bool takeLimited)` | `OFFSET … FETCH NEXT …` is not understood |
| `protected virtual void AddPagingParameters(DbCommand cmd, int skip, int take, bool takeLimited)` | your paging clause names the two parameters in a different order |
| `public virtual void AddCommandParameter(DbCommand cmd, string paramName, object? paramValue, Enum? dataType = null, int? size = null)` | the provider needs types or sizes set explicitly |
| `public virtual object GetDbBooleanValue(bool value)` | there is no boolean column type |
| `public virtual bool GetBooleanFromDbValue(object columnValue)` | the same, reading back |

### Row limiting

There is no ANSI row limit, so `StdAdoDelegate` applies none and a dialect that can limit rows says
where its clause goes. Two statements carry one — trigger acquisition and the misfire scan — and both
ask the same member, so a dialect says it once:

<!-- snippet: sample_dialect_delegate_row_limiting -->
```csharp
// … LIMIT n (PostgreSQL, MySQL, SQLite) — or "ROWS" on Firebird
protected override SqlRowLimit GetRowLimit(int count)
    => SqlRowLimit.AtStatementEnd("LIMIT", count);

// SELECT TOP n …                              SqlRowLimit.InProjection("TOP", count)
// SELECT * FROM ( … ) WHERE rownum <= n       SqlRowLimit.InEnclosingSelect("rownum", count)
```
<!-- endSnippet -->

`SqlRowLimit` names the three places a limit can sit, and the statement is built with the clause
already in it. Nothing is spliced into finished SQL, so a dialect no longer depends on the statement
starting with a particular keyword or ending with a particular clause. `count` is always at least
one: the `-1` that means "every row" is turned into `SqlRowLimit.Unlimited` before the member is
called, so an override never has to test for it.

`TriggerAcquisitionSqlShape` carries everything about an acquisition attempt that changes the text of
the statement — the row limit's count, and how many job-type exclusion terms the `NOT IN` clause
needs. It is also the key the finished statement is cached under, which is why it holds the bucketed
exclusion count rather than the caller's exact one. Override
`GetSelectNextTriggerToAcquireSql(shape)` only for something a row limit cannot express; MySQL is the
one shipped dialect that does, for its `FORCE INDEX` hint, and it still calls `base` and leaves the
limit alone.

### Paging

The default is the ANSI form, understood by SQL Server 2012+, Oracle 12c+, PostgreSQL and Firebird 3+:

```sql
 OFFSET @pageSkip ROWS FETCH NEXT @pageTake ROWS ONLY
```

MySQL and SQLite have no such clause, so they override both members — and they must override
`AddPagingParameters` too, because their clause names the parameters in the other order and providers
that bind positionally take them in the order the statement mentions them.

The two parameter names are the one thing the two overrides have to agree about, so they are constants
rather than literals: `AdoConstants.ParameterPageSkip` and `AdoConstants.ParameterPageTake`, spliced
into the statement with an `@` and bound by the bare name.

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

`takeLimited` is `false` when the caller asked for an unbounded page (`Take = int.MaxValue`), which is
the case a database with no offset-only form has to spell some other way — MySQL uses a `LIMIT` of the
largest `BIGINT UNSIGNED`, SQLite uses `LIMIT -1`.

One detail to preserve: the `take` the base class passes is **one more than the page size**. That extra
row is what tells the caller whether anything follows the page, which is how `PagedResult<T>.HasMore`
is exact without a second query.

### Booleans

Oracle has no boolean column type, so its delegate maps both directions:

<!-- snippet: sample_dialect_delegate_booleans -->
```csharp
public override object GetDbBooleanValue(bool booleanValue) => booleanValue ? "1" : "0";

public override bool GetBooleanFromDbValue(object columnValue) => Convert.ToInt32(columnValue) == 1;
```
<!-- endSnippet -->

`GetDbBooleanValue` is what every `IS_DURABLE`, `REQUESTS_RECOVERY` and similar column is bound
through, so the two must agree exactly.

### Parameters

`AddCommandParameter` is the last resort, and SQL Server's delegate shows why it is sometimes
necessary: it converts booleans to `1`/`0`, sets `size = -1` for varbinary, and pins string parameters
to `size = 4000` to stop the server inferring a size from the value and building a separate query plan
per length.

## What the delegate cannot reach

::: warning
The SQL statement constants — `StdAdoConstants` — are **internal**. The exact text of a statement is
not a contract; the schema it addresses is, and that lives in `AdoConstants`, which is public.
:::

For a delegate in your own assembly this means two things:

- You cannot write `StdAdoConstants.SqlSelectNextTriggerToAcquire`. Derive your statement from what the
  base returns — `base.GetSelectNextTriggerToAcquireSql(shape)` — and transform the string, which is
  exactly what MySQL's `.Replace("{0}TRIGGERS t", …)` does. Or write the statement whole.
- You *can* name tables, columns, trigger types and state values: `AdoConstants.TableTriggers`,
  `AdoConstants.ColumnTriggerName`, `AdoConstants.StateWaiting` and the rest are public precisely so a
  dialect can build its own SQL against the schema.

`{0}` is the table-prefix placeholder, and `protected string ReplaceTablePrefix(string query)`
substitutes it. Statements the base class returns still contain it; the caller substitutes.

::: tip
"Customize one statement" is not a supported operation, and that is deliberate. The six SQL hooks
above cover the statements that actually differ between databases; the other ~76 are inlined
`ReplaceTablePrefix(StdAdoConstants.X)` call sites, and the delegate *is* the seam — override the
method that issues the statement. Additional `GetXxxSql()` hooks can be added later without breaking
anyone, so if you need one, ask.
:::

## Initialization

`StdAdoDelegate.Initialize(DriverDelegateContext context)` is `public virtual`, and a dialect normally
does not override it — none of the six shipped ones do. Override it only to register extra trigger
persistence delegates or to capture something from the context, and call `base.Initialize(context)`
first.

`DriverDelegateContext` carries everything the delegate needs to issue statements:

| Member | |
|---|---|
| `TablePrefix`, `SchedulerName`, `InstanceId` | required |
| `DbProvider`, `TypeLoader` | required |
| `ObjectSerializer` | nullable |
| `TriggerPersistenceDelegates` | the ones registered for this scheduler |
| `TimeProvider` | the scheduler's clock |
| `CommandTimeout` | from `AdoJobStoreOptions.CommandTimeout` |

It arrives after construction rather than through the constructor because `InstanceId` is not settled
until the scheduler starts — a generated instance id does not exist when the container builds the
delegate.

Two of those the base class then hands back, so a delegate writing a statement of its own does not
override `Initialize` merely to keep a second copy: `protected string SchedulerName { get; }`, which
nearly every statement is scoped by, and `protected IDbProvider DbProvider { get; }`.

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
Registration is **first-wins** (`TryAdd`). `UseSqlServer`, `UsePostgres` and the rest each call
`UseDriverDelegate<…>()` internally, so `UseDriverDelegate<MyDatabaseDelegate>()` must come **before**
the database method or it is silently ignored.
:::

The delegate is constructed with `ActivatorUtilities`, so **constructor dependencies work** — take an
`ILogger<MyDatabaseDelegate>` or anything else in the container.

For a delegate that needs more than the container can supply — a constructor argument only the caller
has, or a property set before it is handed over — `UseDriverDelegate(factory)` takes the delegate you
built. The factory is given a provider that resolves this scheduler's own parts, which is what
registering `IDriverDelegate` against `Services` would not do: a named scheduler resolves its delegate
under its own key and would never see an unkeyed registration. `UseSerializer`, `UseLockHandler`,
`UseConnectionProvider` and `UseTriggerPersistenceDelegate` all take a factory the same way.

The legacy `quartz.jobStore.driverDelegateType` key still selects a delegate by type name, and stands
on its own: an application that has moved store selection into code can still name its delegate in a
configuration file.

## Also needed: a DbMetadata and a schema

The delegate is one of three things a new database needs:

1. **The delegate** — this page.
2. **A provider registration.** `UseGenericDatabase(provider, connectionString, describeMetadata)`
   takes a `Func<DbMetadata>` describing the ADO.NET provider: its connection, command and parameter
   types, the parameter prefix, and how it spells a `DbType`. The provider name and the delegate are
   independent axes — the `Use…` shortcut methods just set both at once.
3. **DDL.** Copy the closest `database/tables/tables_<dialect>.sql` and adjust the column types.

## See also

- [Job Stores](../tutorial/job-stores.md) — how the ADO store is put together
- [A Job Store of Your Own](custom-job-store.md) — the layer above this one
- [Persisting a Custom Trigger Type](trigger-persistence-delegate.md) — the other delegate seam
