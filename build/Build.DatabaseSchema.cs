using System;
using System.Collections.Generic;
using System.Linq;

using Fallout.Common;
using Fallout.Common.IO;

using Serilog;

/// <summary>
/// Generates the per-dialect schema scripts the job store ships as embedded resources and runs itself
/// when <c>SchemaProvisioning.CreateIfMissing</c> is configured.
/// </summary>
/// <remarks>
/// <para>
/// These say the same thing <c>database/tables/tables_&lt;dialect&gt;.sql</c> says, in the shape a
/// running scheduler can execute: every object is created only if it is missing, nothing is ever
/// dropped, the table prefix is a <c>{0}</c> placeholder rather than a literal <c>QRTZ_</c>, and the
/// statements are separated by a fixed sentinel line so the job store can split the file without
/// lexing SQL. The fresh-install scripts stay hand-written and are not generated from this model —
/// they are read by a person with a database client, and they drop before they create, which is the
/// opposite of what a scheduler starting up may do.
/// </para>
/// <para>
/// The two are kept honest by <c>SchemaScriptTest</c>, which compares the tables, columns and indexes
/// this model names with the ones the fresh-install script names, and by
/// <c>SchemaProvisioningTest</c>, which provisions into a real database of each dialect and compares
/// the resulting catalog with the one the fresh-install script produced.
/// </para>
/// <para>
/// The output is checked in, so <c>dotnet fallout GenerateSchema</c> must leave the working tree clean
/// unless this model changed. <c>VerifySchema</c> asserts exactly that, beside <c>VerifyMigrations</c>.
/// </para>
/// </remarks>
partial class Build
{
    /// <summary>
    /// The line that separates one statement from the next. The job store splits on it rather than
    /// looking for a terminator, which is what lets the same mechanism carry a PL/SQL block, a
    /// Firebird <c>EXECUTE BLOCK</c> and a bare <c>CREATE TABLE</c> without any of them needing a
    /// dialect-specific batch separator.
    /// </summary>
    const string StatementSeparator = "--;;";

    AbsolutePath SchemaDirectory => SourceDirectory / "Quartz" / "Impl" / "AdoJobStore" / "Schema";

    Target GenerateSchema => _ => _
        .Description("Regenerates the embedded schema scripts from the model in build/Build.DatabaseSchema.cs")
        .Executes(() =>
        {
            foreach ((string path, string content) in BuildSchemaScripts())
            {
                AbsolutePath file = SchemaDirectory / path;
                file.Parent.CreateDirectory();
                file.WriteAllText(Normalize(content));
            }

            Log.Information("Generated {Count} schema scripts under {Directory}",
                BuildSchemaScripts().Count, SchemaDirectory);
        });

    Target VerifySchema => _ => _
        .Description("Fails when the embedded schema scripts differ from what GenerateSchema produces")
        .Executes(() =>
        {
            List<string> stale = [];

            foreach ((string path, string content) in BuildSchemaScripts())
            {
                AbsolutePath file = SchemaDirectory / path;
                if (!file.FileExists() || file.ReadAllText().Replace("\r\n", "\n") != Normalize(content))
                {
                    stale.Add(path);
                }
            }

            if (stale.Count > 0)
            {
                throw new Exception(
                    "These schema scripts are out of date with build/Build.DatabaseSchema.cs. "
                    + "Run 'dotnet fallout GenerateSchema' and commit the result:"
                    + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", stale));
            }

            Log.Information("All generated schema scripts are up to date");
        });

    // ---------------------------------------------------------------------------------------
    // The model
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// One column, and what each dialect declares after its name — type, nullability and default,
    /// verbatim from that dialect's fresh-install script.
    /// </summary>
    /// <remarks>
    /// Spelled out per dialect rather than derived from a portable type, because the fresh-install
    /// scripts are not consistent with each other and this has to match them: PostgreSQL says
    /// <c>TEXT</c> for most strings but <c>VARCHAR(200)</c> for the three newest columns, SQL Server
    /// says <c>int</c> where PostgreSQL says <c>BIGINT</c> for a repeat count, and
    /// <c>IS_NONCONCURRENT</c> on <c>QRTZ_FIRED_TRIGGERS</c> is nullable on four dialects and not on
    /// two. Deriving them would mean choosing which script to be wrong about.
    /// </remarks>
    sealed record SchemaColumn(string Name, Dictionary<string, string> Definition);

    /// <summary>A foreign key, and the Oracle constraint name its fresh-install script gives it.</summary>
    /// <param name="Columns">The columns in this table.</param>
    /// <param name="ReferencedTable">The unprefixed name of the table referenced.</param>
    /// <param name="ReferencedColumns">The columns referenced there.</param>
    /// <param name="Cascade">
    /// Whether deleting the parent row deletes this one. Honoured only where the fresh-install script
    /// honours it — SQL Server, PostgreSQL and SQLite — because on MySQL, Oracle and Firebird the
    /// child rows are deleted by Quartz's own statements and adding a cascade here would make a
    /// provisioned schema differ from a scripted one.
    /// </param>
    /// <param name="OracleName">The constraint name, unprefixed, that <c>tables_oracle.sql</c> uses.</param>
    sealed record SchemaForeignKey(
        string[] Columns,
        string ReferencedTable,
        string[] ReferencedColumns,
        bool Cascade,
        string OracleName);

    /// <param name="Name">The table name without the <c>QRTZ_</c> prefix.</param>
    /// <param name="PrimaryKey">The primary key columns.</param>
    /// <param name="Columns">Every column, in the order the fresh-install scripts declare them.</param>
    /// <param name="ForeignKey">The foreign key, where the table has one.</param>
    /// <param name="OracleStem">
    /// The stem <c>tables_oracle.sql</c> builds this table's constraint names from, which is not
    /// always the table name — Oracle's identifiers were capped at 30 characters until 12.2, so its
    /// script abbreviates (<c>SIMPLE_TRIG</c>, <c>PAUSED_TRIG_GRPS</c>) and singularizes
    /// (<c>FIRED_TRIGGER</c>). Kept rather than reinvented so that a provisioned Oracle schema names
    /// its constraints what a scripted one does.
    /// </param>
    sealed record SchemaTable(
        string Name,
        string[] PrimaryKey,
        SchemaColumn[] Columns,
        SchemaForeignKey ForeignKey = null,
        string OracleStem = null);

    static SchemaColumn Column(
        string name,
        string sqlServer,
        string postgres,
        string mysql,
        string oracle,
        string sqlite,
        string firebird)
    {
        return new SchemaColumn(name, new Dictionary<string, string>
        {
            ["sqlServer"] = sqlServer,
            ["postgres"] = postgres,
            ["mysql_innodb"] = mysql,
            ["oracle"] = oracle,
            ["sqlite"] = sqlite,
            ["firebird"] = firebird,
        });
    }

    /// <summary>A string column that PostgreSQL declares as <c>TEXT</c>, which is most of them.</summary>
    static SchemaColumn Text(string name, int sqlServerSize, int otherSize, bool required)
    {
        string ss = required ? "NOT NULL" : "NULL";
        string fb = required ? "NOT NULL" : "DEFAULT NULL";

        return Column(name,
            sqlServer: $"nvarchar({sqlServerSize}) {ss}",
            postgres: $"TEXT {ss}",
            mysql: $"VARCHAR({otherSize}) {ss}",
            oracle: $"VARCHAR2({otherSize}) {ss}",
            sqlite: $"NVARCHAR({sqlServerSize}) {ss}",
            firebird: $"VARCHAR({sqlServerSize}) {fb}");
    }

    /// <summary>A 64-bit epoch-milliseconds column.</summary>
    static SchemaColumn Timestamp(string name, bool required)
    {
        string ss = required ? "NOT NULL" : "NULL";
        string fb = required ? "NOT NULL" : "DEFAULT NULL";

        return Column(name,
            sqlServer: $"bigint {ss}",
            postgres: $"BIGINT {ss}",
            mysql: $"BIGINT {ss}",
            oracle: $"NUMBER(19) {ss}",
            sqlite: $"BIGINT {ss}",
            firebird: $"BIGINT {fb}");
    }

    /// <summary>A binary column holding a serialized object.</summary>
    static SchemaColumn Blob(string name, bool required)
    {
        string ss = required ? "NOT NULL" : "NULL";
        string fb = required ? "NOT NULL" : "DEFAULT NULL";

        return Column(name,
            sqlServer: $"varbinary(max) {ss}",
            postgres: $"BYTEA {ss}",
            mysql: $"BLOB {ss}",
            oracle: $"BLOB {ss}",
            sqlite: $"BLOB {ss}",
            firebird: $"BLOB {fb}");
    }

    /// <summary>A boolean, in whichever of the six spellings the dialect has for one.</summary>
    static SchemaColumn Flag(string name, bool required)
    {
        return Column(name,
            sqlServer: required ? "bit NOT NULL" : "bit NULL",
            postgres: required ? "BOOL NOT NULL" : "BOOL NULL",
            mysql: required ? "BOOLEAN NOT NULL" : "BOOLEAN NULL",
            oracle: required ? "VARCHAR2(1) NOT NULL" : "VARCHAR2(1) NULL",
            sqlite: required ? "BIT NOT NULL" : "BIT NULL",
            firebird: required ? "SMALLINT NOT NULL" : "SMALLINT DEFAULT NULL");
    }

    /// <summary>The three columns that name a trigger, sized as each dialect's script sizes them.</summary>
    static SchemaColumn[] TriggerKeyColumns() =>
    [
        Text("SCHED_NAME", 120, 120, required: true),
        Text("TRIGGER_NAME", 150, 200, required: true),
        Text("TRIGGER_GROUP", 150, 200, required: true),
    ];

    static readonly string[] TriggerKey = ["SCHED_NAME", "TRIGGER_NAME", "TRIGGER_GROUP"];
    static readonly string[] JobKey = ["SCHED_NAME", "JOB_NAME", "JOB_GROUP"];

    static SchemaForeignKey TriggerReference(bool cascade, string oracleName) =>
        new(TriggerKey, "TRIGGERS", TriggerKey, cascade, oracleName);

    /// <summary>
    /// Every table Quartz reads or writes, in an order that satisfies the foreign keys: a table is
    /// created after the one it references.
    /// </summary>
    static readonly SchemaTable[] SchemaTables =
    [
        new("JOB_DETAILS",
            JobKey,
            [
                Text("SCHED_NAME", 120, 120, required: true),
                Text("JOB_NAME", 150, 200, required: true),
                Text("JOB_GROUP", 150, 200, required: true),
                Text("DESCRIPTION", 250, 250, required: false),
                Text("JOB_CLASS_NAME", 250, 250, required: true),
                Flag("IS_DURABLE", required: true),
                Flag("IS_NONCONCURRENT", required: true),
                Flag("IS_UPDATE_DATA", required: true),
                Flag("REQUESTS_RECOVERY", required: true),
                Blob("JOB_DATA", required: false),
            ]),

        new("TRIGGERS",
            TriggerKey,
            [
                Text("SCHED_NAME", 120, 120, required: true),
                Text("TRIGGER_NAME", 150, 200, required: true),
                Text("TRIGGER_GROUP", 150, 200, required: true),
                Text("JOB_NAME", 150, 200, required: true),
                Text("JOB_GROUP", 150, 200, required: true),
                Text("DESCRIPTION", 250, 250, required: false),
                Timestamp("NEXT_FIRE_TIME", required: false),
                Timestamp("PREV_FIRE_TIME", required: false),
                Column("PRIORITY",
                    sqlServer: "int NULL",
                    postgres: "INTEGER NULL",
                    mysql: "INTEGER NULL",
                    oracle: "NUMBER(13) NULL",
                    sqlite: "INTEGER NULL",
                    firebird: "INTEGER DEFAULT NULL"),
                Text("TRIGGER_STATE", 16, 16, required: true),
                Text("TRIGGER_TYPE", 8, 8, required: true),
                Timestamp("START_TIME", required: true),
                Timestamp("END_TIME", required: false),
                Text("CALENDAR_NAME", 200, 200, required: false),
                Column("MISFIRE_INSTR",
                    sqlServer: "int NULL",
                    postgres: "SMALLINT NULL",
                    mysql: "SMALLINT NULL",
                    oracle: "NUMBER(2) NULL",
                    sqlite: "INTEGER NULL",
                    firebird: "SMALLINT DEFAULT NULL"),
                // SQLite says INTEGER where the other dialects say a 64-bit type, which is the same
                // storage class there and is what tables_sqlite.sql and the 3.17 migration both say.
                Column("MISFIRE_ORIG_FIRE_TIME",
                    sqlServer: "bigint NULL",
                    postgres: "BIGINT NULL",
                    mysql: "BIGINT NULL",
                    oracle: "NUMBER(19) NULL",
                    sqlite: "INTEGER NULL",
                    firebird: "BIGINT DEFAULT NULL"),
                // The three newest string columns are VARCHAR on PostgreSQL rather than TEXT, because
                // that is what the migration that introduced each of them added.
                Column("EXECUTION_GROUP",
                    sqlServer: "nvarchar(200) NULL",
                    postgres: "VARCHAR(200) NULL",
                    mysql: "VARCHAR(200) NULL",
                    oracle: "VARCHAR2(200) NULL",
                    sqlite: "NVARCHAR(200) NULL",
                    firebird: "VARCHAR(200)"),
                Column("PREFERRED_NODE",
                    sqlServer: "nvarchar(200) NULL",
                    postgres: "VARCHAR(200) NULL",
                    mysql: "VARCHAR(200) NULL",
                    oracle: "VARCHAR2(200) NULL",
                    sqlite: "NVARCHAR(200) NULL",
                    firebird: "VARCHAR(200)"),
                Column("PREFERRED_NODE_AUTO",
                    sqlServer: "bit NOT NULL DEFAULT 0",
                    postgres: "BOOL NOT NULL DEFAULT FALSE",
                    mysql: "BOOLEAN NOT NULL DEFAULT FALSE",
                    oracle: "VARCHAR2(1) DEFAULT '0' NOT NULL",
                    sqlite: "BIT NOT NULL DEFAULT 0",
                    firebird: "SMALLINT DEFAULT 0 NOT NULL"),
                Column("RETRY_POLICY",
                    sqlServer: "nvarchar(250) NULL",
                    postgres: "VARCHAR(250) NULL",
                    mysql: "VARCHAR(250) NULL",
                    oracle: "VARCHAR2(250) NULL",
                    sqlite: "NVARCHAR(250) NULL",
                    firebird: "VARCHAR(250)"),
                Column("RETRY_ATTEMPT",
                    sqlServer: "int NULL",
                    postgres: "INTEGER NULL",
                    mysql: "INTEGER NULL",
                    oracle: "NUMBER(13) NULL",
                    sqlite: "INTEGER NULL",
                    firebird: "INTEGER DEFAULT NULL"),
                Blob("JOB_DATA", required: false),
            ],
            new SchemaForeignKey(JobKey, "JOB_DETAILS", JobKey, Cascade: false, OracleName: "TRIGGER_TO_JOBS_FK")),

        new("SIMPLE_TRIGGERS",
            TriggerKey,
            [
                .. TriggerKeyColumns(),
                Column("REPEAT_COUNT",
                    sqlServer: "int NOT NULL",
                    postgres: "BIGINT NOT NULL",
                    mysql: "BIGINT NOT NULL",
                    oracle: "NUMBER(7) NOT NULL",
                    sqlite: "BIGINT NOT NULL",
                    firebird: "BIGINT NOT NULL"),
                Column("REPEAT_INTERVAL",
                    sqlServer: "bigint NOT NULL",
                    postgres: "BIGINT NOT NULL",
                    mysql: "BIGINT NOT NULL",
                    oracle: "NUMBER(12) NOT NULL",
                    sqlite: "BIGINT NOT NULL",
                    firebird: "BIGINT NOT NULL"),
                Column("TIMES_TRIGGERED",
                    sqlServer: "int NOT NULL",
                    postgres: "BIGINT NOT NULL",
                    mysql: "BIGINT NOT NULL",
                    oracle: "NUMBER(10) NOT NULL",
                    sqlite: "BIGINT NOT NULL",
                    firebird: "BIGINT NOT NULL"),
            ],
            TriggerReference(cascade: true, oracleName: "SIMPLE_TRIG_TO_TRIG_FK"),
            OracleStem: "SIMPLE_TRIG"),

        new("CRON_TRIGGERS",
            TriggerKey,
            [
                .. TriggerKeyColumns(),
                Column("CRON_EXPRESSION",
                    sqlServer: "nvarchar(120) NOT NULL",
                    postgres: "TEXT NOT NULL",
                    mysql: "VARCHAR(120) NOT NULL",
                    oracle: "VARCHAR2(120) NOT NULL",
                    sqlite: "NVARCHAR(250) NOT NULL",
                    firebird: "VARCHAR(250) NOT NULL"),
                // Nullable, and written without the keyword on every dialect, exactly as the
                // fresh-install scripts and the 2.6 migration that added it write it.
                Column("TIME_ZONE_ID",
                    sqlServer: "nvarchar(80)",
                    postgres: "TEXT",
                    mysql: "VARCHAR(80)",
                    oracle: "VARCHAR2(80)",
                    sqlite: "NVARCHAR(80)",
                    firebird: "VARCHAR(80)"),
            ],
            TriggerReference(cascade: true, oracleName: "CRON_TRIG_TO_TRIG_FK"),
            OracleStem: "CRON_TRIG"),

        new("SIMPROP_TRIGGERS",
            TriggerKey,
            [
                .. TriggerKeyColumns(),
                Text("STR_PROP_1", 512, 512, required: false),
                Text("STR_PROP_2", 512, 512, required: false),
                Text("STR_PROP_3", 512, 512, required: false),
                SimpropInt("INT_PROP_1"),
                SimpropInt("INT_PROP_2"),
                Column("LONG_PROP_1",
                    sqlServer: "bigint NULL",
                    postgres: "BIGINT NULL",
                    mysql: "BIGINT NULL",
                    oracle: "NUMBER(19) NULL",
                    sqlite: "BIGINT NULL",
                    firebird: "BIGINT DEFAULT NULL"),
                Column("LONG_PROP_2",
                    sqlServer: "bigint NULL",
                    postgres: "BIGINT NULL",
                    mysql: "BIGINT NULL",
                    oracle: "NUMBER(19) NULL",
                    sqlite: "BIGINT NULL",
                    firebird: "BIGINT DEFAULT NULL"),
                SimpropDecimal("DEC_PROP_1"),
                SimpropDecimal("DEC_PROP_2"),
                Flag("BOOL_PROP_1", required: false),
                Flag("BOOL_PROP_2", required: false),
                Text("TIME_ZONE_ID", 80, 80, required: false),
            ],
            TriggerReference(cascade: true, oracleName: "SIMPROP_TRIG_TO_TRIG_FK"),
            OracleStem: "SIMPROP_TRIG"),

        new("BLOB_TRIGGERS",
            TriggerKey,
            [
                .. TriggerKeyColumns(),
                Blob("BLOB_DATA", required: false),
            ],
            TriggerReference(cascade: true, oracleName: "BLOB_TRIG_TO_TRIG_FK"),
            OracleStem: "BLOB_TRIG"),

        new("CALENDARS",
            ["SCHED_NAME", "CALENDAR_NAME"],
            [
                Text("SCHED_NAME", 120, 120, required: true),
                Text("CALENDAR_NAME", 200, 200, required: true),
                Blob("CALENDAR", required: true),
            ]),

        new("PAUSED_TRIGGER_GRPS",
            ["SCHED_NAME", "TRIGGER_GROUP"],
            [
                Text("SCHED_NAME", 120, 120, required: true),
                Text("TRIGGER_GROUP", 150, 200, required: true),
            ],
            OracleStem: "PAUSED_TRIG_GRPS"),

        new("PAUSED_JOB_GRPS",
            ["SCHED_NAME", "JOB_GROUP"],
            [
                Text("SCHED_NAME", 120, 120, required: true),
                Text("JOB_GROUP", 150, 200, required: true),
            ]),

        new("FIRED_TRIGGERS",
            ["SCHED_NAME", "ENTRY_ID"],
            [
                Text("SCHED_NAME", 120, 120, required: true),
                Text("ENTRY_ID", 140, 140, required: true),
                Text("TRIGGER_NAME", 150, 200, required: true),
                Text("TRIGGER_GROUP", 150, 200, required: true),
                Text("INSTANCE_NAME", 200, 200, required: true),
                Timestamp("FIRED_TIME", required: true),
                Timestamp("SCHED_TIME", required: true),
                Column("PRIORITY",
                    sqlServer: "int NOT NULL",
                    postgres: "INTEGER NOT NULL",
                    mysql: "INTEGER NOT NULL",
                    oracle: "NUMBER(13) NOT NULL",
                    sqlite: "INTEGER NOT NULL",
                    firebird: "INTEGER NOT NULL"),
                Text("STATE", 16, 16, required: true),
                Text("JOB_NAME", 150, 200, required: false),
                Text("JOB_GROUP", 150, 200, required: false),
                // Nullable on four dialects and not on two. The store always writes it, so the two
                // that require it are not wrong -- but a provisioned schema has to be the schema the
                // fresh-install script produces, disagreements included.
                Column("IS_NONCONCURRENT",
                    sqlServer: "bit NULL",
                    postgres: "BOOL NOT NULL",
                    mysql: "BOOLEAN NULL",
                    oracle: "VARCHAR2(1) NULL",
                    sqlite: "BIT NULL",
                    firebird: "SMALLINT NOT NULL"),
                Flag("REQUESTS_RECOVERY", required: false),
                Column("EXECUTION_GROUP",
                    sqlServer: "nvarchar(200) NULL",
                    postgres: "VARCHAR(200) NULL",
                    mysql: "VARCHAR(200) NULL",
                    oracle: "VARCHAR2(200) NULL",
                    sqlite: "NVARCHAR(200) NULL",
                    firebird: "VARCHAR(200)"),
            ],
            OracleStem: "FIRED_TRIGGER"),

        new("SCHEDULER_STATE",
            ["SCHED_NAME", "INSTANCE_NAME"],
            [
                Text("SCHED_NAME", 120, 120, required: true),
                Text("INSTANCE_NAME", 200, 200, required: true),
                Timestamp("LAST_CHECKIN_TIME", required: true),
                Column("CHECKIN_INTERVAL",
                    sqlServer: "bigint NOT NULL",
                    postgres: "BIGINT NOT NULL",
                    mysql: "BIGINT NOT NULL",
                    oracle: "NUMBER(13) NOT NULL",
                    sqlite: "BIGINT NOT NULL",
                    firebird: "BIGINT NOT NULL"),
            ]),

        new("LOCKS",
            ["SCHED_NAME", "LOCK_NAME"],
            [
                Text("SCHED_NAME", 120, 120, required: true),
                Text("LOCK_NAME", 40, 40, required: true),
            ]),
    ];

    static SchemaColumn SimpropInt(string name) => Column(name,
        sqlServer: "int NULL",
        postgres: "INTEGER NULL",
        mysql: "INT NULL",
        oracle: "NUMBER(10) NULL",
        sqlite: "INT NULL",
        firebird: "INTEGER DEFAULT NULL");

    static SchemaColumn SimpropDecimal(string name) => Column(name,
        sqlServer: "numeric(13,4) NULL",
        postgres: "NUMERIC NULL",
        mysql: "NUMERIC(13,4) NULL",
        oracle: "NUMERIC(13,4) NULL",
        sqlite: "NUMERIC NULL",
        firebird: "NUMERIC(9,0) DEFAULT NULL");

    /// <summary>
    /// The referential-integrity triggers SQLite needs, as (trigger name, table it clears) pairs.
    /// </summary>
    /// <remarks>
    /// SQLite does not enforce foreign keys unless the connection asks it to, so the fresh-install
    /// script deletes the child row itself. They are prefixed like every other object, so two Quartz
    /// schemas can share one SQLite database.
    /// </remarks>
    static readonly (string Trigger, string Table)[] SqliteDeleteTriggers =
    [
        ("DELETE_SIMPLE_TRIGGER", "SIMPLE_TRIGGERS"),
        ("DELETE_SIMPROP_TRIGGER", "SIMPROP_TRIGGERS"),
        ("DELETE_CRON_TRIGGER", "CRON_TRIGGERS"),
        ("DELETE_BLOB_TRIGGER", "BLOB_TRIGGERS"),
    ];

    // ---------------------------------------------------------------------------------------
    // Rendering
    // ---------------------------------------------------------------------------------------

    /// <summary>Every generated script, as a file name under the schema directory and its content.</summary>
    static List<(string Path, string Content)> BuildSchemaScripts()
    {
        List<(string, string)> files = [];

        foreach (string dialect in Dialects)
        {
            List<string> statements = [SchemaHeader(dialect)];

            foreach (SchemaTable table in SchemaTables)
            {
                statements.Add($"-- {{0}}{table.Name}\n" + CreateTableIfMissing(dialect, table));
            }

            if (dialect == "sqlite")
            {
                foreach ((string trigger, string child) in SqliteDeleteTriggers)
                {
                    statements.Add($"-- {{0}}{trigger}\n" + CreateSqliteDeleteTrigger(trigger, child));
                }
            }

            // MySQL declares its indexes inside CREATE TABLE, so it has none to add here. See
            // TableBody for why.
            foreach (IndexDef index in SchemaIndexes(dialect))
            {
                statements.Add($"-- IDX_{{1}}{IndexSuffix(index)}\n" + CreateIndexIfMissing(dialect, index));
            }

            files.Add(($"create_{dialect}.sql",
                string.Join($"\n{StatementSeparator}\n", statements)));
        }

        return files;
    }

    /// <summary>
    /// The indexes this dialect creates with a statement of their own. The set is
    /// <see cref="Target4X" /> — the same one the 3.x-to-4.0 migration converges onto, so a
    /// provisioned schema and a migrated one cannot drift apart — with the <c>QRTZ_</c> in each name
    /// turned into a placeholder.
    /// </summary>
    static IEnumerable<IndexDef> SchemaIndexes(string dialect)
    {
        // MySQL has no CREATE INDEX IF NOT EXISTS, and the guarded form its migrations use needs a
        // user variable, which MySqlConnector reads as a parameter placeholder unless the connection
        // string opts in. Its indexes are declared inside the guarded CREATE TABLE instead.
        if (dialect == "mysql_innodb")
        {
            yield break;
        }

        foreach (IndexDef index in Target4X(dialect))
        {
            yield return index;
        }
    }

    /// <summary>The part of an index's name after the <c>IDX_QRTZ_</c> that every one of them starts with.</summary>
    static string IndexSuffix(IndexDef index) => index.Name["IDX_QRTZ_".Length..];

    /// <summary>The table an index sits on, without the prefix the definition spells out.</summary>
    static string IndexTable(IndexDef index) => index.Table["QRTZ_".Length..];

    /// <summary>
    /// The column and constraint lines of one table, with the prefix left as a placeholder.
    /// </summary>
    static List<string> TableBody(string dialect, SchemaTable table)
    {
        List<string> body = table.Columns.Select(c => $"{c.Name} {c.Definition[dialect]}".TrimEnd()).ToList();

        body.Add($"{PrimaryKeyName(dialect, table)}PRIMARY KEY ({string.Join(",", table.PrimaryKey)})");

        if (table.ForeignKey is { } foreignKey)
        {
            string name = ForeignKeyName(dialect, table, foreignKey);
            string cascade = foreignKey.Cascade && dialect is "sqlServer" or "postgres" or "sqlite"
                ? " ON DELETE CASCADE"
                : "";

            body.Add($"{name}FOREIGN KEY ({string.Join(",", foreignKey.Columns)}) "
                     + $"REFERENCES {{0}}{foreignKey.ReferencedTable} ({string.Join(",", foreignKey.ReferencedColumns)}){cascade}");
        }

        if (dialect == "mysql_innodb")
        {
            body.AddRange(Target4X(dialect)
                .Where(i => IndexTable(i) == table.Name)
                .Select(i => $"KEY IDX_{{1}}{IndexSuffix(i)} ({TightColumns(i.Columns)})"));
        }

        return body;
    }

    /// <summary>
    /// A named constraint clause, or nothing at all where the fresh-install script leaves the name to
    /// the database. Only the three dialects whose scripts name their constraints get names here.
    /// </summary>
    static string PrimaryKeyName(string dialect, SchemaTable table) => dialect switch
    {
        "sqlServer" or "firebird" => $"CONSTRAINT PK_{{1}}{table.Name} ",
        "oracle" => $"CONSTRAINT {{1}}{table.OracleStem ?? table.Name}_PK ",
        _ => "",
    };

    static string ForeignKeyName(string dialect, SchemaTable table, SchemaForeignKey foreignKey) => dialect switch
    {
        // Long enough to have needed shortening on Firebird, whose identifiers were capped at 31
        // characters before 4.0, so its script numbers them instead of naming both ends.
        "sqlServer" => $"CONSTRAINT FK_{{1}}{table.Name}_{{1}}{foreignKey.ReferencedTable} ",
        "firebird" => $"CONSTRAINT FK_{{1}}{table.Name}_1 ",
        "oracle" => $"CONSTRAINT {{1}}{foreignKey.OracleName} ",
        _ => "",
    };

    /// <summary>
    /// A <c>CREATE TABLE</c> that does nothing when the table is already there, in whichever form
    /// the dialect has for one and can execute through an ADO.NET command.
    /// </summary>
    /// <remarks>
    /// This is deliberately not <see cref="CreateTable" />: the migration emitters write for a
    /// database's own command-line client, so they end SQL Server in <c>GO</c>, Oracle in <c>/</c> and
    /// Firebird in a <c>SET TERM</c> pair, and they qualify SQL Server with <c>[dbo]</c>. None of
    /// those can be sent to a provider, and the last would ignore a schema-qualified table prefix,
    /// which the rest of Quartz's SQL honours.
    /// </remarks>
    static string CreateTableIfMissing(string dialect, SchemaTable table)
    {
        List<string> body = TableBody(dialect, table);
        string oneLine = string.Join(", ", body).Replace("'", "''");
        string indented = string.Join(",\n", body.Select(l => "  " + l));

        switch (dialect)
        {
            case "sqlServer":
                return $"IF OBJECT_ID(N'{{0}}{table.Name}', N'U') IS NULL\n"
                     + "BEGIN\n"
                     + $"  CREATE TABLE {{0}}{table.Name} (\n"
                     + string.Join(",\n", body.Select(l => "    " + l)) + "\n"
                     + "  );\n"
                     + "END";

            case "postgres":
                return $"CREATE TABLE IF NOT EXISTS {{0}}{table.Name.ToLowerInvariant()} (\n"
                     + string.Join(",\n", body.Select(l => "  " + l.ToLowerInvariant())) + "\n);";

            case "mysql_innodb":
                return $"CREATE TABLE IF NOT EXISTS {{0}}{table.Name} (\n"
                     + indented + "\n) ENGINE=InnoDB;";

            case "sqlite":
                return $"CREATE TABLE IF NOT EXISTS {{0}}{table.Name} (\n"
                     + indented + "\n);";

            case "oracle":
                return "DECLARE\n  table_exists NUMBER;\nBEGIN\n"
                     + "  SELECT COUNT(*) INTO table_exists FROM user_tables\n"
                     + $"  WHERE table_name = UPPER('{{1}}{table.Name}');\n"
                     + "  IF table_exists = 0 THEN\n"
                     + $"    EXECUTE IMMEDIATE 'CREATE TABLE {{0}}{table.Name} ({oneLine})';\n"
                     + "  END IF;\nEND;";

            case "firebird":
                return "EXECUTE BLOCK AS\nBEGIN\n"
                     + "  IF (NOT EXISTS(SELECT 1 FROM RDB$RELATIONS\n"
                     + $"                 WHERE TRIM(RDB$RELATION_NAME) = UPPER('{{1}}{table.Name}'))) THEN\n"
                     + $"    EXECUTE STATEMENT 'CREATE TABLE {{0}}{table.Name} ({oneLine})'\n"
                     + "      WITH AUTONOMOUS TRANSACTION;\n"
                     + "END";

            default:
                throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "unknown dialect");
        }
    }

    static string CreateIndexIfMissing(string dialect, IndexDef index)
    {
        string name = $"IDX_{{1}}{IndexSuffix(index)}";
        string table = $"{{0}}{IndexTable(index)}";
        string tight = TightColumns(index.Columns);

        switch (dialect)
        {
            case "sqlServer":
                return $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}' AND object_id = OBJECT_ID('{table}'))\n"
                     + $"BEGIN\n  CREATE INDEX {name} ON {table}({index.Columns});\nEND";

            case "postgres":
                return $"CREATE INDEX IF NOT EXISTS {name.ToLowerInvariant()} ON {table.ToLowerInvariant()} ({index.Columns.ToLowerInvariant()});";

            case "sqlite":
                return $"CREATE INDEX IF NOT EXISTS {name} ON {table}({tight});";

            case "oracle":
                return "DECLARE\n  index_exists NUMBER;\nBEGIN\n"
                     + $"  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = UPPER('{name}');\n"
                     + "  IF index_exists = 0 THEN\n"
                     + $"    EXECUTE IMMEDIATE 'CREATE INDEX {name} ON {table}({tight})';\n"
                     + "  END IF;\nEND;";

            case "firebird":
                return "EXECUTE BLOCK AS\nBEGIN\n"
                     + $"  IF (NOT EXISTS(SELECT 1 FROM RDB$INDICES WHERE TRIM(RDB$INDEX_NAME) = UPPER('{name}'))) THEN\n"
                     + $"    EXECUTE STATEMENT 'CREATE INDEX {name} ON {table}({tight})'\n"
                     + "      WITH AUTONOMOUS TRANSACTION;\n"
                     + "END";

            default:
                throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "unknown dialect");
        }
    }

    static string CreateSqliteDeleteTrigger(string trigger, string child)
    {
        return $"CREATE TRIGGER IF NOT EXISTS {{0}}{trigger} DELETE ON {{0}}TRIGGERS\n"
             + "BEGIN\n"
             + $"  DELETE FROM {{0}}{child} WHERE SCHED_NAME=OLD.SCHED_NAME AND TRIGGER_NAME=OLD.TRIGGER_NAME AND TRIGGER_GROUP=OLD.TRIGGER_GROUP;\n"
             + "END";
    }

    static string SchemaHeader(string dialect)
    {
        string label = DialectLabel[dialect];

        List<string> o =
        [
            "--",
            $"-- Quartz.NET schema -- {label}",
            "--",
            "-- GENERATED FILE. Describe the schema in build/Build.DatabaseSchema.cs and run",
            "-- 'dotnet fallout GenerateSchema'; edits made here are overwritten.",
            "--",
            "-- This is what AdoJobStore runs for itself when SchemaProvisioning.CreateIfMissing is",
            "-- configured. It is not the script to run by hand -- use",
            $"-- database/tables/tables_{dialect}.sql for that, which is written for a person with a",
            "-- database client and drops an existing schema before it recreates one.",
            "--",
            "-- Every statement creates only what is missing, and nothing here ever drops anything.",
            "-- So it is safe to run against a schema that already exists, and safe to run twice.",
            "--",
            "-- '{0}' is the configured table prefix and '{1}' is the same prefix with any schema",
            "-- qualifier removed, for the identifiers that cannot carry one -- index, constraint and",
            "-- catalog-lookup names. They are substituted at runtime, so a schema provisioned under a",
            "-- prefix of its own collides with nothing.",
            "--",
            $"-- Statements are separated by a line reading exactly '{StatementSeparator}'. The job store splits on",
            "-- it and sends each piece to the provider as one command, which is why no dialect's batch",
            "-- separator appears: no GO, no lone '/', no SET TERM.",
            "--",
        ];

        if (dialect == "sqlServer")
        {
            o.AddRange([
                "-- Tables are named without a [dbo] qualifier, exactly as the rest of Quartz's SQL names",
                "-- them, so they are created in the connection's own default schema and a table prefix",
                "-- that carries a schema of its own is honoured.",
                "--",
                "-- The memory-optimized (tables_sqlServerMOT.sql) and pre-2016 (tables_sqlServer_Below2016.sql)",
                "-- variants have no counterpart here: both are deliberate departures from the standard",
                "-- schema, chosen by a human for a particular deployment, and neither is something a",
                "-- scheduler should decide to create on its own. Run those by hand and leave",
                "-- SchemaProvisioning at Validate.",
                "--",
            ]);
        }

        if (dialect == "mysql_innodb")
        {
            o.AddRange([
                "-- The indexes are declared inside CREATE TABLE rather than as statements of their own:",
                "-- MySQL has no CREATE INDEX IF NOT EXISTS, and the guarded form the migration scripts",
                "-- use needs a user variable, which MySqlConnector reads as a parameter placeholder",
                "-- unless the connection string sets AllowUserVariables.",
                "--",
            ]);
        }

        if (dialect == "firebird")
        {
            o.AddRange([
                "-- Each CREATE runs through EXECUTE STATEMENT ... WITH AUTONOMOUS TRANSACTION, which is",
                "-- the COMMIT the fresh-install script writes between its statements: Firebird has to",
                "-- see a table committed before another table's foreign key can reference it, and the",
                "-- job store runs this whole file inside one transaction of its own.",
                "--",
            ]);
        }

        return string.Join("\n", o);
    }
}
