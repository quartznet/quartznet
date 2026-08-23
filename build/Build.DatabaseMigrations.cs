using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Fallout.Common;
using Fallout.Common.IO;

using Serilog;

/// <summary>
/// Generates the per-dialect migration scripts under <c>database/migrations/</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every migration ships one directly-runnable file per supported database. Writing six variants
/// of the same change by hand is how they drift, so the change is described once here and the
/// dialect-specific guard syntax is applied mechanically.
/// </para>
/// <para>
/// Only the folders listed in <see cref="GeneratedVersions"/> are generated. The 2.0 and 3.0
/// migrations are SQL Server-only historical scripts that are maintained by hand.
/// </para>
/// <para>
/// The output is checked in, so <c>dotnet fallout GenerateMigrations</c> must leave the working
/// tree clean unless a definition here changed. <c>VerifyMigrations</c> asserts exactly that — it
/// compares this branch's scripts with this branch's definitions and nothing else, so keeping a
/// migration in step with the other branch is a review obligation rather than something CI checks.
/// </para>
/// <para>
/// A migration both branches can run is mirrored to <c>3.x</c> byte for byte, so a documented path
/// resolves whichever branch a reader lands on (#3218). The <c>4.0</c> folder is the exception: it is
/// the 3.x-to-4.0 upgrade path, its content moves whenever 4.x's schema moves, so it is generated
/// here and nowhere else. <c>3.x</c> carries no copy at all and its <c>database/README.md</c> links
/// to this one — a mirror there would go stale silently, and a wrong upgrade script is worse than an
/// absent one.
/// </para>
/// </remarks>
partial class Build
{
    const string TableJobs = "QRTZ_JOB_DETAILS";
    const string TableTriggers = "QRTZ_TRIGGERS";
    const string TableFired = "QRTZ_FIRED_TRIGGERS";
    const string TablePausedJobGroups = "QRTZ_PAUSED_JOB_GRPS";

    static readonly string[] Dialects = ["sqlServer", "postgres", "mysql_innodb", "oracle", "sqlite", "firebird"];

    static readonly Dictionary<string, string> DialectLabel = new()
    {
        ["sqlServer"] = "SQL Server",
        ["postgres"] = "PostgreSQL",
        ["mysql_innodb"] = "MySQL",
        ["oracle"] = "Oracle",
        ["sqlite"] = "SQLite",
        ["firebird"] = "Firebird",
    };

    /// <summary>Version folders this target owns. Anything else under migrations/ is hand-written.</summary>
    static readonly string[] GeneratedVersions = ["2.2", "2.6", "3.17", "3.18", "3.19", "3.20", "4.0"];

    AbsolutePath MigrationsDirectory => RootDirectory / "database" / "migrations";

    Target GenerateMigrations => _ => _
        .Description("Regenerates database/migrations from the definitions in build/Build.DatabaseMigrations.cs")
        .Executes(() =>
        {
            foreach ((string path, string content) in BuildMigrationScripts())
            {
                AbsolutePath file = MigrationsDirectory / path;
                file.Parent.CreateDirectory();
                file.WriteAllText(Normalize(content));
            }

            Log.Information("Generated {Count} migration scripts under {Directory}",
                BuildMigrationScripts().Count, MigrationsDirectory);
        });

    Target VerifyMigrations => _ => _
        .Description("Fails when database/migrations differs from what GenerateMigrations produces")
        .Executes(() =>
        {
            List<string> stale = [];

            foreach ((string path, string content) in BuildMigrationScripts())
            {
                AbsolutePath file = MigrationsDirectory / path;
                if (!file.FileExists() || file.ReadAllText().Replace("\r\n", "\n") != Normalize(content))
                {
                    stale.Add(path);
                }
            }

            if (stale.Count > 0)
            {
                throw new Exception(
                    "These migration scripts are out of date with build/Build.DatabaseMigrations.cs. "
                    + "Run 'dotnet fallout GenerateMigrations' and commit the result:"
                    + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", stale));
            }

            Log.Information("All generated migration scripts are up to date");
        });

    /// <summary>Writes LF line endings, no trailing whitespace, exactly one trailing newline.</summary>
    static string Normalize(string content)
    {
        IEnumerable<string> lines = content.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd());
        return string.Join("\n", lines).TrimEnd('\n') + "\n";
    }

    // ---------------------------------------------------------------------------------------
    // Column definitions, taken verbatim from the shape each database/tables/*.sql fresh-install
    // script declares, so that a migrated schema matches a freshly created one exactly.
    // ---------------------------------------------------------------------------------------

    static readonly Dictionary<string, string> MisfireOrigFireTime = new()
    {
        ["sqlServer"] = "[MISFIRE_ORIG_FIRE_TIME] bigint NULL",
        ["postgres"] = "MISFIRE_ORIG_FIRE_TIME BIGINT NULL",
        ["mysql_innodb"] = "MISFIRE_ORIG_FIRE_TIME BIGINT NULL",
        ["oracle"] = "MISFIRE_ORIG_FIRE_TIME NUMBER(19) NULL",
        ["sqlite"] = "MISFIRE_ORIG_FIRE_TIME INTEGER NULL",
        ["firebird"] = "MISFIRE_ORIG_FIRE_TIME BIGINT DEFAULT NULL",
    };

    static readonly Dictionary<string, string> ExecutionGroup = new()
    {
        ["sqlServer"] = "[EXECUTION_GROUP] nvarchar(200) NULL",
        ["postgres"] = "EXECUTION_GROUP VARCHAR(200) NULL",
        ["mysql_innodb"] = "EXECUTION_GROUP VARCHAR(200) NULL",
        ["oracle"] = "EXECUTION_GROUP VARCHAR2(200) NULL",
        ["sqlite"] = "EXECUTION_GROUP NVARCHAR(200) NULL",
        ["firebird"] = "EXECUTION_GROUP VARCHAR(200)",
    };

    static readonly Dictionary<string, string> PreferredNode = new()
    {
        ["sqlServer"] = "[PREFERRED_NODE] nvarchar(200) NULL",
        ["postgres"] = "PREFERRED_NODE VARCHAR(200) NULL",
        ["mysql_innodb"] = "PREFERRED_NODE VARCHAR(200) NULL",
        ["oracle"] = "PREFERRED_NODE VARCHAR2(200) NULL",
        ["sqlite"] = "PREFERRED_NODE NVARCHAR(200) NULL",
        ["firebird"] = "PREFERRED_NODE VARCHAR(200)",
    };

    static readonly Dictionary<string, string> PreferredNodeAuto = new()
    {
        ["sqlServer"] = "[PREFERRED_NODE_AUTO] bit NOT NULL DEFAULT 0",
        ["postgres"] = "PREFERRED_NODE_AUTO BOOL NOT NULL DEFAULT FALSE",
        ["mysql_innodb"] = "PREFERRED_NODE_AUTO BOOLEAN NOT NULL DEFAULT FALSE",
        ["oracle"] = "PREFERRED_NODE_AUTO VARCHAR2(1) DEFAULT '0' NOT NULL",
        ["sqlite"] = "PREFERRED_NODE_AUTO BIT NOT NULL DEFAULT 0",
        ["firebird"] = "PREFERRED_NODE_AUTO SMALLINT DEFAULT 0 NOT NULL",
    };

    static readonly Dictionary<string, string> SchedTime = new()
    {
        ["sqlServer"] = "[SCHED_TIME] bigint NOT NULL",
        ["postgres"] = "SCHED_TIME BIGINT NOT NULL",
        ["mysql_innodb"] = "SCHED_TIME BIGINT(19) NOT NULL",
        ["oracle"] = "SCHED_TIME NUMBER(19) NOT NULL",
        ["sqlite"] = "SCHED_TIME INTEGER NOT NULL DEFAULT 0",
        ["firebird"] = "SCHED_TIME BIGINT NOT NULL",
    };

    static readonly Dictionary<string, string> TimeZoneId = new()
    {
        ["sqlServer"] = "[TIME_ZONE_ID] nvarchar(80) NULL",
        ["postgres"] = "TIME_ZONE_ID VARCHAR(80) NULL",
        ["mysql_innodb"] = "TIME_ZONE_ID VARCHAR(80) NULL",
        ["oracle"] = "TIME_ZONE_ID VARCHAR2(80) NULL",
        ["sqlite"] = "TIME_ZONE_ID NVARCHAR(80) NULL",
        ["firebird"] = "TIME_ZONE_ID VARCHAR(80)",
    };

    // ---------------------------------------------------------------------------------------
    // Table definitions, likewise verbatim from the fresh-install scripts. SQL Server declares its
    // primary key in a separate ALTER; naming the same constraint inline here reaches the same
    // schema in one statement, which is what a guarded CREATE TABLE needs.
    // ---------------------------------------------------------------------------------------

    static readonly Dictionary<string, string[]> PausedJobGroupsTable = new()
    {
        ["sqlServer"] =
        [
            "[SCHED_NAME] nvarchar(120) NOT NULL",
            "[JOB_GROUP] nvarchar(150) NOT NULL",
            "CONSTRAINT [PK_QRTZ_PAUSED_JOB_GRPS] PRIMARY KEY CLUSTERED ([SCHED_NAME], [JOB_GROUP])",
        ],
        ["postgres"] =
        [
            "sched_name TEXT NOT NULL",
            "job_group TEXT NOT NULL",
            "PRIMARY KEY (sched_name, job_group)",
        ],
        ["mysql_innodb"] =
        [
            "SCHED_NAME VARCHAR(120) NOT NULL",
            "JOB_GROUP VARCHAR(200) NOT NULL",
            "PRIMARY KEY (SCHED_NAME,JOB_GROUP)",
        ],
        ["oracle"] =
        [
            "SCHED_NAME VARCHAR2(120) NOT NULL",
            "JOB_GROUP VARCHAR2(200) NOT NULL",
            "CONSTRAINT QRTZ_PAUSED_JOB_GRPS_PK PRIMARY KEY (SCHED_NAME,JOB_GROUP)",
        ],
        ["sqlite"] =
        [
            "SCHED_NAME NVARCHAR(120) NOT NULL",
            "JOB_GROUP NVARCHAR(150) NOT NULL",
            "PRIMARY KEY (SCHED_NAME,JOB_GROUP)",
        ],
        ["firebird"] =
        [
            "SCHED_NAME VARCHAR(120) NOT NULL",
            "JOB_GROUP VARCHAR(150) NOT NULL",
            "CONSTRAINT PK_QRTZ_PAUSED_JOB_GRPS PRIMARY KEY (SCHED_NAME, JOB_GROUP)",
        ],
    };

    // ---------------------------------------------------------------------------------------
    // Guarded DDL emitters. SQLite has no conditional DDL for ADD COLUMN; everything else it can
    // guard, and every other dialect can guard everything.
    // ---------------------------------------------------------------------------------------

    static string AddColumn(string dialect, string table, string column, string definition)
    {
        switch (dialect)
        {
            case "sqlServer":
                return $"IF COL_LENGTH('{table}','{column}') IS NULL\n"
                     + "BEGIN\n"
                     + $"  ALTER TABLE [dbo].[{table}] ADD {definition};\n"
                     + "END\nGO";

            case "postgres":
                return "DO $$\n"
                     + "BEGIN\n"
                     + "  IF NOT EXISTS (SELECT 1 FROM information_schema.columns\n"
                     + $"                 WHERE table_name = '{table.ToLowerInvariant()}' AND column_name = '{column.ToLowerInvariant()}') THEN\n"
                     + $"    ALTER TABLE {table.ToLowerInvariant()} ADD COLUMN {definition.ToLowerInvariant()};\n"
                     + "  END IF;\n"
                     + "END $$;";

            case "mysql_innodb":
                return "SET @preparedStatement = (SELECT IF(\n"
                     + "  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS\n"
                     + $"   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}') > 0,\n"
                     + "  'SELECT 1',\n"
                     + $"  'ALTER TABLE {table} ADD COLUMN {definition}'\n"
                     + "));\nPREPARE alterIfNotExists FROM @preparedStatement;\n"
                     + "EXECUTE alterIfNotExists;\nDEALLOCATE PREPARE alterIfNotExists;";

            case "oracle":
                return "DECLARE\n  column_exists NUMBER;\nBEGIN\n"
                     + "  SELECT COUNT(*) INTO column_exists FROM user_tab_columns\n"
                     + $"  WHERE table_name = '{table}' AND column_name = '{column}';\n"
                     + "  IF column_exists = 0 THEN\n"
                     + $"    EXECUTE IMMEDIATE 'ALTER TABLE {table} ADD ({definition.Replace("'", "''")})';\n"
                     + "  END IF;\nEND;\n/";

            case "sqlite":
                return $"ALTER TABLE {table} ADD COLUMN {definition};";

            case "firebird":
                // isql needs the terminator switched while the block body uses ';'.
                return "SET TERM ^ ;\nEXECUTE BLOCK AS\nBEGIN\n"
                     + "  IF (NOT EXISTS(SELECT 1 FROM RDB$RELATION_FIELDS\n"
                     + $"                 WHERE TRIM(RDB$RELATION_NAME) = '{table}'\n"
                     + $"                   AND TRIM(RDB$FIELD_NAME) = '{column}')) THEN\n"
                     + $"    EXECUTE STATEMENT 'ALTER TABLE {table} ADD {definition.Replace("'", "''")}';\n"
                     + "END^\nSET TERM ; ^\nCOMMIT;";

            default:
                throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "unknown dialect");
        }
    }

    /// <summary>
    /// A guarded <c>CREATE TABLE</c>. <paramref name="body" /> holds the dialect's own column and
    /// constraint lines, verbatim from the shape <c>database/tables/tables_&lt;dialect&gt;.sql</c>
    /// declares, so the migrated table is the table a fresh install creates.
    /// </summary>
    /// <remarks>
    /// This one is guarded on every dialect, SQLite included: <c>CREATE TABLE IF NOT EXISTS</c> is the
    /// conditional DDL SQLite does have, and only <c>ALTER TABLE ... ADD COLUMN</c> is missing there.
    /// Oracle and Firebird have no such form at all, so they read the catalog and run the statement
    /// through dynamic SQL, which is why their table body is flattened onto one line inside a string
    /// literal.
    /// </remarks>
    static string CreateTable(string dialect, string table, IReadOnlyList<string> body)
    {
        string oneLine = string.Join(", ", body).Replace("'", "''");

        switch (dialect)
        {
            case "sqlServer":
                return $"IF OBJECT_ID(N'[dbo].[{table}]', N'U') IS NULL\n"
                     + "BEGIN\n"
                     + $"  CREATE TABLE [dbo].[{table}] (\n"
                     + string.Join(",\n", body.Select(l => "    " + l)) + "\n"
                     + "  );\n"
                     + "END\nGO";

            case "postgres":
                return $"CREATE TABLE IF NOT EXISTS {table.ToLowerInvariant()} (\n"
                     + string.Join(",\n", body.Select(l => "  " + l)) + "\n);";

            case "mysql_innodb":
                return $"CREATE TABLE IF NOT EXISTS {table} (\n"
                     + string.Join(",\n", body.Select(l => "  " + l)) + "\n) ENGINE=InnoDB;";

            case "sqlite":
                return $"CREATE TABLE IF NOT EXISTS {table} (\n"
                     + string.Join(",\n", body.Select(l => "  " + l)) + "\n);";

            case "oracle":
                return "DECLARE\n  table_exists NUMBER;\nBEGIN\n"
                     + "  SELECT COUNT(*) INTO table_exists FROM user_tables\n"
                     + $"  WHERE table_name = '{table}';\n"
                     + "  IF table_exists = 0 THEN\n"
                     + $"    EXECUTE IMMEDIATE 'CREATE TABLE {table} ({oneLine})';\n"
                     + "  END IF;\nEND;\n/";

            case "firebird":
                // isql needs the terminator switched while the block body uses ';'.
                return "SET TERM ^ ;\nEXECUTE BLOCK AS\nBEGIN\n"
                     + "  IF (NOT EXISTS(SELECT 1 FROM RDB$RELATIONS\n"
                     + $"                 WHERE TRIM(RDB$RELATION_NAME) = '{table}')) THEN\n"
                     + $"    EXECUTE STATEMENT 'CREATE TABLE {table} ({oneLine})';\n"
                     + "END^\nSET TERM ; ^\nCOMMIT;";

            default:
                throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "unknown dialect");
        }
    }

    static string CreateIndex(string dialect, string name, string table, string columns)
    {
        string tight = columns.Replace(" ", "");

        switch (dialect)
        {
            case "sqlServer":
                return $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}' AND object_id = OBJECT_ID('dbo.{table}'))\n"
                     + $"BEGIN\n  CREATE INDEX [{name}] ON [dbo].[{table}]({columns});\nEND\nGO";

            case "postgres":
                return $"CREATE INDEX IF NOT EXISTS {name.ToLowerInvariant()} ON {table.ToLowerInvariant()} ({columns.ToLowerInvariant()});";

            case "sqlite":
                return $"CREATE INDEX IF NOT EXISTS {name} ON {table}({tight});";

            case "mysql_innodb":
                return "SET @preparedStatement = (SELECT IF(\n"
                     + "  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS\n"
                     + $"   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}' AND INDEX_NAME = '{name}') > 0,\n"
                     + "  'SELECT 1',\n"
                     + $"  'CREATE INDEX {name} ON {table}({tight})'\n"
                     + "));\nPREPARE stmt FROM @preparedStatement;\nEXECUTE stmt;\nDEALLOCATE PREPARE stmt;";

            case "oracle":
                return "DECLARE\n  index_exists NUMBER;\nBEGIN\n"
                     + $"  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = '{name}';\n"
                     + "  IF index_exists = 0 THEN\n"
                     + $"    EXECUTE IMMEDIATE 'CREATE INDEX {name} ON {table}({tight})';\n"
                     + "  END IF;\nEND;\n/";

            case "firebird":
                return "SET TERM ^ ;\nEXECUTE BLOCK AS\nBEGIN\n"
                     + $"  IF (NOT EXISTS(SELECT 1 FROM RDB$INDICES WHERE TRIM(RDB$INDEX_NAME) = '{name}')) THEN\n"
                     + $"    EXECUTE STATEMENT 'CREATE INDEX {name} ON {table}({tight})';\n"
                     + "END^\nSET TERM ; ^\nCOMMIT;";

            default:
                throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "unknown dialect");
        }
    }

    static string DropIndex(string dialect, string name, string table)
    {
        switch (dialect)
        {
            case "sqlServer":
                return $"IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}' AND object_id = OBJECT_ID('dbo.{table}'))\n"
                     + $"BEGIN\n  DROP INDEX [{name}] ON [dbo].[{table}];\nEND\nGO";

            case "postgres":
                return $"DROP INDEX IF EXISTS {name.ToLowerInvariant()};";

            case "sqlite":
                return $"DROP INDEX IF EXISTS {name};";

            case "mysql_innodb":
                return "SET @preparedStatement = (SELECT IF(\n"
                     + "  (SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS\n"
                     + $"   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}' AND INDEX_NAME = '{name}') > 0,\n"
                     + $"  'DROP INDEX {name} ON {table}',\n"
                     + "  'SELECT 1'\n"
                     + "));\nPREPARE stmt FROM @preparedStatement;\nEXECUTE stmt;\nDEALLOCATE PREPARE stmt;";

            case "oracle":
                return "DECLARE\n  index_exists NUMBER;\nBEGIN\n"
                     + $"  SELECT COUNT(*) INTO index_exists FROM user_indexes WHERE index_name = '{name}';\n"
                     + $"  IF index_exists > 0 THEN\n    EXECUTE IMMEDIATE 'DROP INDEX {name}';\n  END IF;\nEND;\n/";

            case "firebird":
                return "SET TERM ^ ;\nEXECUTE BLOCK AS\nBEGIN\n"
                     + $"  IF (EXISTS(SELECT 1 FROM RDB$INDICES WHERE TRIM(RDB$INDEX_NAME) = '{name}')) THEN\n"
                     + $"    EXECUTE STATEMENT 'DROP INDEX {name}';\n"
                     + "END^\nSET TERM ; ^\nCOMMIT;";

            default:
                throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "unknown dialect");
        }
    }

    static string Header(
        string dialect,
        string title,
        string version,
        string pr,
        IEnumerable<string> status,
        IEnumerable<string> extra,
        bool sqliteNotIdempotent = false)
    {
        string label = DialectLabel[dialect];
        List<string> o = ["--", $"-- Quartz.NET schema migration -- {title}", "--"];

        if (!string.IsNullOrEmpty(version))
        {
            o.Add($"-- Introduced in Quartz.NET {version}{(string.IsNullOrEmpty(pr) ? "" : $" ({pr})")}");
            o.Add("--");
        }

        o.Add($"-- {label} only. Run the file matching your database; the other dialects live");
        o.Add("-- alongside this one in the same folder.");
        o.Add("--");
        o.Add("-- STATUS");
        o.AddRange(status.Select(s => $"--   {s}"));
        o.Add("--");
        o.AddRange(extra.Select(l => string.IsNullOrEmpty(l) ? "--" : $"-- {l}"));
        o.Add("--");
        o.Add("-- Replace 'QRTZ_' with your configured table prefix if different.");

        if (sqliteNotIdempotent && dialect == "sqlite")
        {
            o.Add($"-- NOT IDEMPOTENT: {label} has no conditional DDL, so re-running this fails with a");
            o.Add("-- duplicate-column error. Check PRAGMA table_info(<table>) before applying.");
        }
        else
        {
            o.Add("-- Every statement checks first, so this script is safe to run more than once.");
        }

        o.Add("--");
        o.Add("-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!");
        o.Add("--");

        return string.Join("\n", o);
    }

    /// <summary>The 4.x note appended to every column migration that 4.x turns into a requirement.</summary>
    static string[] RequiredOn4X(string dialect) =>
    [
        "",
        "4.x  REQUIRED. 4.x removed the startup probe and assumes the column exists. When",
        $"     upgrading from 3.x run ../4.0/schema_30_to_40_upgrade_{dialect}.sql instead -- it",
        "     folds this change in.",
    ];
}
