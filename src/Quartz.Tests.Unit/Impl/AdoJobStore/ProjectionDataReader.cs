#nullable enable

using System.Collections;
using System.Data.Common;
using System.Globalization;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A reader over exactly the columns a statement projects, which refuses any other and remembers
/// which of its own were asked for.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes "the named column list is the list the reader reads" a test rather than a
/// comment. A reader asking for a column the statement does not project fails here the way it would
/// against a database — <see cref="GetOrdinal" /> throws — and a statement projecting a column no
/// reader wants is left over in <see cref="Unread" /> at the end.
/// </para>
/// <para>
/// The values are made up but type-plausible, because the readers do real work with them: a cron
/// expression is parsed, a time zone id is resolved, and a stored state is mapped onto
/// <see cref="Quartz.Extensibility.StoredTriggerState" />.
/// </para>
/// </remarks>
internal sealed class ProjectionDataReader : DbDataReader
{
    private readonly string[] columns;
    private readonly bool[] wasRead;
    private int rowsLeft;

    public ProjectionDataReader(IReadOnlyCollection<string> columns, int rows = 1)
    {
        this.columns = [.. columns];
        wasRead = new bool[this.columns.Length];
        rowsLeft = rows;
    }

    /// <summary>
    /// A reader with no columns and no rows, for a statement a test drives past but does not measure.
    /// </summary>
    public static ProjectionDataReader Empty => new([], rows: 0);

    /// <summary>
    /// The projected columns nothing asked for, which are the ones the statement is fetching for
    /// nobody.
    /// </summary>
    public IReadOnlyList<string> Unread => [.. columns.Where((_, i) => !wasRead[i])];

    public override int GetOrdinal(string name)
    {
        int index = Array.IndexOf(columns, name);
        if (index < 0)
        {
            throw new IndexOutOfRangeException(
                $"The statement does not project '{name}'. It projects: {string.Join(", ", columns)}");
        }

        wasRead[index] = true;
        return index;
    }

    /// <summary>
    /// A value of the type the column holds, keyed by name so that one reader serves every statement.
    /// </summary>
    private static object ValueOf(string column) => column switch
    {
        AdoConstants.ColumnTriggerName => "trigger",
        AdoConstants.ColumnTriggerGroup => "group",
        AdoConstants.ColumnJobName => "job",
        AdoConstants.ColumnJobGroup => "jobs",
        AdoConstants.ColumnInstanceName => "node",
        AdoConstants.ColumnEntryId => "entry",

        // EXECUTING rather than ACQUIRED: an acquired row has no job yet, and the reader skips its
        // job columns, which would leave them looking like columns nothing reads.
        AdoConstants.ColumnEntryState => AdoConstants.StateExecuting,

        AdoConstants.ColumnCronExpression => "0 0 12 * * ?",
        AdoConstants.ColumnTimeZoneId => "UTC",

        // Stored as UTC ticks.
        AdoConstants.ColumnFiredTime or AdoConstants.ColumnScheduledTime or AdoConstants.ColumnLastCheckinTime => 638_000_000_000_000_000L,

        // Stored as whole milliseconds.
        AdoConstants.ColumnCheckinInterval or AdoConstants.ColumnRepeatInterval => 15_000L,

        AdoConstants.ColumnPriority or AdoConstants.ColumnRepeatCount or AdoConstants.ColumnTimesTriggered => 5,
        AdoConstants.ColumnIsNonConcurrent or AdoConstants.ColumnRequestsRecovery => true,

        // SIMPROP_TRIGGERS. STR_PROP_1 carries an IntervalUnit name for the calendar-interval type.
        "STR_PROP_1" => nameof(IntervalUnit.Day),
        "STR_PROP_2" or "STR_PROP_3" => "text",
        "INT_PROP_1" or "INT_PROP_2" => 1,
        "LONG_PROP_1" or "LONG_PROP_2" => 1L,
        "DEC_PROP_1" or "DEC_PROP_2" => 1m,
        "BOOL_PROP_1" or "BOOL_PROP_2" => true,

        _ => throw new InvalidOperationException(
            $"ProjectionDataReader has no value for '{column}'. Add one beside the columns it already knows.")
    };

    public override object GetValue(int ordinal) => ValueOf(columns[ordinal]);

    public override string GetString(int ordinal) => (string) GetValue(ordinal);

    public override bool GetBoolean(int ordinal) => Convert.ToBoolean(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override int GetInt32(int ordinal) => Convert.ToInt32(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override long GetInt64(int ordinal) => Convert.ToInt64(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override decimal GetDecimal(int ordinal) => Convert.ToDecimal(GetValue(ordinal), CultureInfo.InvariantCulture);

    public override bool IsDBNull(int ordinal) => false;

    public override string GetName(int ordinal) => columns[ordinal];

    public override Type GetFieldType(int ordinal) => GetValue(ordinal).GetType();

    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    public override int FieldCount => columns.Length;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool HasRows => rowsLeft > 0;

    public override bool Read() => rowsLeft-- > 0;

    public override int Depth => 0;

    public override bool IsClosed => false;

    public override int RecordsAffected => 0;

    public override bool NextResult() => false;

    public override IEnumerator GetEnumerator() => throw new NotSupportedException();

    public override int GetValues(object[] values) => throw new NotSupportedException();

    public override byte GetByte(int ordinal) => throw new NotSupportedException();

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

    public override char GetChar(int ordinal) => throw new NotSupportedException();

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

    public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();

    public override double GetDouble(int ordinal) => throw new NotSupportedException();

    public override float GetFloat(int ordinal) => throw new NotSupportedException();

    public override Guid GetGuid(int ordinal) => throw new NotSupportedException();

    public override short GetInt16(int ordinal) => throw new NotSupportedException();
}

/// <summary>
/// A command that answers with one prepared reader, so that a reader living inside
/// <see cref="Quartz.Impl.AdoJobStore.StdAdoDelegate" /> can be driven without a database.
/// </summary>
internal sealed class ReaderStubCommand : DbCommand
{
    private readonly ProjectionDataReader reader;

    public ReaderStubCommand(ProjectionDataReader reader) => this.reader = reader;

    protected override DbDataReader ExecuteDbDataReader(System.Data.CommandBehavior behavior) => reader;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string CommandText { get; set; } = "";

    public override int CommandTimeout { get; set; }
    public override System.Data.CommandType CommandType { get; set; }
    public override System.Data.UpdateRowSource UpdatedRowSource { get; set; }
    public override bool DesignTimeVisible { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbParameterCollection DbParameterCollection { get; } = new RecordingParameterCollection();
    protected override DbTransaction? DbTransaction { get; set; }
    protected override DbParameter CreateDbParameter() => new StubDbParameter();
    public override void Cancel() { }
    public override int ExecuteNonQuery() => 0;
    public override object? ExecuteScalar() => null;
    public override void Prepare() { }
}
