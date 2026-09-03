using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The migration definitions themselves: which index set each version expects, and the content of
/// every generated script. See <c>Build.DatabaseMigrations.cs</c> for the dialect-specific emitters.
/// </summary>
partial class Build
{
    sealed record IndexDef(string Name, string Table, string Columns);

    static readonly IndexDef[] MySqlOracleFirebird3X =
    [
        new("IDX_QRTZ_J_REQ_RECOVERY", TableJobs, "SCHED_NAME, REQUESTS_RECOVERY"),
        new("IDX_QRTZ_J_GRP", TableJobs, "SCHED_NAME, JOB_GROUP"),
        new("IDX_QRTZ_T_J", TableTriggers, "SCHED_NAME, JOB_NAME, JOB_GROUP"),
        new("IDX_QRTZ_T_JG", TableTriggers, "SCHED_NAME, JOB_GROUP"),
        new("IDX_QRTZ_T_C", TableTriggers, "SCHED_NAME, CALENDAR_NAME"),
        new("IDX_QRTZ_T_N_STATE", TableTriggers, "SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, TRIGGER_STATE"),
        new("IDX_QRTZ_T_N_G_STATE", TableTriggers, "SCHED_NAME, TRIGGER_GROUP, TRIGGER_STATE"),
        new("IDX_QRTZ_T_NEXT_FIRE_TIME", TableTriggers, "SCHED_NAME, NEXT_FIRE_TIME"),
        new("IDX_QRTZ_T_NFT_ST", TableTriggers, "SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME"),
        new("IDX_QRTZ_T_NFT_ST_MISFIRE", TableTriggers, "SCHED_NAME, MISFIRE_INSTR, NEXT_FIRE_TIME, TRIGGER_STATE"),
        new("IDX_QRTZ_T_NFT_ST_MISFIRE_GRP", TableTriggers, "SCHED_NAME, MISFIRE_INSTR, NEXT_FIRE_TIME, TRIGGER_GROUP, TRIGGER_STATE"),
        new("IDX_QRTZ_FT_INST_JOB_REQ_RCVRY", TableFired, "SCHED_NAME, INSTANCE_NAME, REQUESTS_RECOVERY"),
        new("IDX_QRTZ_FT_J_G", TableFired, "SCHED_NAME, JOB_NAME, JOB_GROUP"),
        new("IDX_QRTZ_FT_JG", TableFired, "SCHED_NAME, JOB_GROUP"),
        new("IDX_QRTZ_FT_T_G", TableFired, "SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP"),
        new("IDX_QRTZ_FT_TG", TableFired, "SCHED_NAME, TRIGGER_GROUP"),
    ];

    /// <summary>The index set the current 3.x <c>tables_&lt;dialect&gt;.sql</c> creates.</summary>
    static readonly Dictionary<string, IndexDef[]> Indexes3X = new()
    {
        ["sqlServer"] =
        [
            new("IDX_QRTZ_T_G_J", TableTriggers, "SCHED_NAME, JOB_GROUP, JOB_NAME"),
            new("IDX_QRTZ_T_C", TableTriggers, "SCHED_NAME, CALENDAR_NAME"),
            new("IDX_QRTZ_T_N_G_STATE", TableTriggers, "SCHED_NAME, TRIGGER_GROUP, TRIGGER_STATE"),
            new("IDX_QRTZ_T_N_STATE", TableTriggers, "SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, TRIGGER_STATE"),
            new("IDX_QRTZ_T_NEXT_FIRE_TIME", TableTriggers, "SCHED_NAME, NEXT_FIRE_TIME"),
            new("IDX_QRTZ_T_NFT_ST", TableTriggers, "SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME"),
            new("IDX_QRTZ_T_NFT_ST_MISFIRE", TableTriggers, "SCHED_NAME, MISFIRE_INSTR, NEXT_FIRE_TIME, TRIGGER_STATE"),
            new("IDX_QRTZ_T_NFT_ST_MISFIRE_GRP", TableTriggers, "SCHED_NAME, MISFIRE_INSTR, NEXT_FIRE_TIME, TRIGGER_GROUP, TRIGGER_STATE"),
            new("IDX_QRTZ_FT_INST_JOB_REQ_RCVRY", TableFired, "SCHED_NAME, INSTANCE_NAME, REQUESTS_RECOVERY"),
            new("IDX_QRTZ_FT_G_J", TableFired, "SCHED_NAME, JOB_GROUP, JOB_NAME"),
            new("IDX_QRTZ_FT_G_T", TableFired, "SCHED_NAME, TRIGGER_GROUP, TRIGGER_NAME"),
        ],
        ["postgres"] =
        [
            new("IDX_QRTZ_J_REQ_RECOVERY", TableJobs, "SCHED_NAME, REQUESTS_RECOVERY"),
            new("IDX_QRTZ_J_G_N", TableJobs, "SCHED_NAME, JOB_GROUP, JOB_NAME"),
            new("IDX_QRTZ_T_J", TableTriggers, "SCHED_NAME, JOB_NAME, JOB_GROUP"),
            new("IDX_QRTZ_T_C", TableTriggers, "SCHED_NAME, CALENDAR_NAME"),
            new("IDX_QRTZ_T_G_N", TableTriggers, "SCHED_NAME, TRIGGER_GROUP, TRIGGER_NAME"),
            new("IDX_QRTZ_T_NEXT_FIRE_TIME", TableTriggers, "SCHED_NAME, NEXT_FIRE_TIME"),
            new("IDX_QRTZ_T_NFT_ST", TableTriggers, "SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME"),
            new("IDX_QRTZ_FT_INST_JOB_REQ_RCVRY", TableFired, "SCHED_NAME, INSTANCE_NAME, REQUESTS_RECOVERY"),
            new("IDX_QRTZ_FT_J_G", TableFired, "SCHED_NAME, JOB_NAME, JOB_GROUP"),
            new("IDX_QRTZ_FT_T_G", TableFired, "SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP"),
        ],
        ["sqlite"] =
        [
            new("IDX_QRTZ_J_REQ_RECOVERY", TableJobs, "SCHED_NAME, REQUESTS_RECOVERY"),
            new("IDX_QRTZ_J_G_N", TableJobs, "SCHED_NAME, JOB_GROUP, JOB_NAME"),
            new("IDX_QRTZ_T_J", TableTriggers, "SCHED_NAME, JOB_NAME, JOB_GROUP"),
            new("IDX_QRTZ_T_C", TableTriggers, "SCHED_NAME, CALENDAR_NAME"),
            new("IDX_QRTZ_T_G_N", TableTriggers, "SCHED_NAME, TRIGGER_GROUP, TRIGGER_NAME"),
            new("IDX_QRTZ_T_NEXT_FIRE_TIME", TableTriggers, "SCHED_NAME, NEXT_FIRE_TIME"),
            new("IDX_QRTZ_T_NFT_ST", TableTriggers, "SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME"),
            new("IDX_QRTZ_FT_INST_JOB_REQ_RCVRY", TableFired, "SCHED_NAME, INSTANCE_NAME, REQUESTS_RECOVERY"),
            new("IDX_QRTZ_FT_J_G", TableFired, "SCHED_NAME, JOB_NAME, JOB_GROUP"),
            new("IDX_QRTZ_FT_T_G", TableFired, "SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP"),
        ],
        // MySQL, Oracle and Firebird share one 3.x shape.
        ["mysql_innodb"] = MySqlOracleFirebird3X,
        ["oracle"] = MySqlOracleFirebird3X,
        ["firebird"] = MySqlOracleFirebird3X,
    };

    /// <summary>
    /// The 4.x index set, before the one per-dialect difference <see cref="Target4X" /> applies: the
    /// acquisition index, which Firebird keeps at three columns because it can express nothing wider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>IDX_QRTZ_T_NFT_ST</c> is the one acquisition runs on, and its last two columns are there
    /// for a plan rather than for a predicate (#3510). Acquisition orders by
    /// <c>NEXT_FIRE_TIME ASC, PRIORITY DESC</c>, so an index whose two directions match is one the
    /// engine can take the first entry of instead of reading every candidate and sorting it —
    /// measured on SQL Server at 5,000 due triggers, 21.6 ms and 20,395 logical reads become 0.6 ms
    /// and 8. <c>MISFIRE_INSTR</c> then keeps that ordered seek from looking up a backlogged row it
    /// is only going to reject: it makes the statement's <c>MISFIRE_INSTR</c>/<c>NEXT_FIRE_TIME</c>
    /// disjunction index-resident, which is 20,401 reads down to 84 against a 5,000-row backlog.
    /// The <c>ORDER BY</c> is unchanged; this is DDL alone.
    /// </para>
    /// <para>
    /// There is no misfire index here, and the absence is the decision.
    /// <c>IDX_QRTZ_T_NFT_ST_MISFIRE</c> led with <c>MISFIRE_INSTR</c> — the column both misfire
    /// statements compare with <c>&lt;&gt; -1</c>, which no btree can seek past — while the two
    /// statements filter on <c>SCHED_NAME</c> and <c>TRIGGER_STATE</c> by equality and
    /// <c>NEXT_FIRE_TIME</c> by range, which is exactly what the acquisition index leads with since
    /// #3510 reshaped it. PostgreSQL and SQLite never created it; on the four dialects that did, no
    /// optimizer picked it once the acquisition index had that shape — measured plan by plan on SQL
    /// Server, MySQL, Oracle and Firebird in #3608 and #3656. MySQL's apparent use of it was
    /// <c>MySQLDelegate</c>'s own <c>FORCE INDEX</c> hint, which #3655 pointed at the acquisition
    /// index. It is therefore write cost with no reader, and the 4.0 script's optional index section
    /// drops it — <see cref="AllLegacyIndexes" /> is what puts it in that section's drop list, after
    /// the creates that bring the acquisition index to its 4.x shape.
    /// </para>
    /// </remarks>
    static readonly IndexDef[] Target4XAll =
    [
        new("IDX_QRTZ_J_G_N", TableJobs, "SCHED_NAME, JOB_GROUP, JOB_NAME"),
        new("IDX_QRTZ_T_J", TableTriggers, "SCHED_NAME, JOB_NAME, JOB_GROUP"),
        new("IDX_QRTZ_T_G_N", TableTriggers, "SCHED_NAME, TRIGGER_GROUP, TRIGGER_NAME"),
        new("IDX_QRTZ_T_C", TableTriggers, "SCHED_NAME, CALENDAR_NAME"),
        new("IDX_QRTZ_T_NFT_ST", TableTriggers, "SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME ASC, PRIORITY DESC, MISFIRE_INSTR"),
        new("IDX_QRTZ_FT_INST_JOB_REQ_RCVRY", TableFired, "SCHED_NAME, INSTANCE_NAME, REQUESTS_RECOVERY"),
        new("IDX_QRTZ_FT_J_G", TableFired, "SCHED_NAME, JOB_NAME, JOB_GROUP"),
        new("IDX_QRTZ_FT_T_G", TableFired, "SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP"),
    ];

    /// <summary>The acquisition index as it ships on 3.x, which is the shape Firebird keeps.</summary>
    const string AcquisitionIndexColumns3X = "SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME";

    static IndexDef[] Target4X(string dialect) => Target4XAll
        // Firebird's indexes are ascending or descending as a whole, with no per-column direction, so
        // it cannot express NEXT_FIRE_TIME ASC, PRIORITY DESC -- CREATE INDEX rejects the ASC token
        // outright, and a COMPUTED BY column standing in for the negated priority cannot be indexed
        // either. MISFIRE_INSTR is there to keep an ordered seek from looking a backlogged row up, and
        // without the ordered seek it is only a wider entry, so Firebird keeps the index it ships with.
        .Select(i => i.Name == "IDX_QRTZ_T_NFT_ST" && dialect == "firebird"
            ? i with { Columns = AcquisitionIndexColumns3X }
            : i)
        .ToArray();

    /// <summary>Every index name Quartz has ever created, plus PostgreSQL's older single-column ones.</summary>
    static readonly (string Name, string Table)[] AllLegacyIndexes =
    [
        ("IDX_QRTZ_J_GRP", TableJobs), ("IDX_QRTZ_J_REQ_RECOVERY", TableJobs),
        ("IDX_QRTZ_T_G_J", TableTriggers), ("IDX_QRTZ_T_JG", TableTriggers), ("IDX_QRTZ_T_G", TableTriggers),
        ("IDX_QRTZ_T_STATE", TableTriggers), ("IDX_QRTZ_T_N_STATE", TableTriggers), ("IDX_QRTZ_T_N_G_STATE", TableTriggers),
        ("IDX_QRTZ_T_NEXT_FIRE_TIME", TableTriggers), ("IDX_QRTZ_T_NFT_MISFIRE", TableTriggers),
        ("IDX_QRTZ_T_NFT_ST_MISFIRE_GRP", TableTriggers), ("IDX_QRTZ_T_NFT_ST_MISFIRE", TableTriggers),
        ("IDX_QRTZ_FT_G_J", TableFired), ("IDX_QRTZ_FT_G_T", TableFired), ("IDX_QRTZ_FT_JG", TableFired),
        ("IDX_QRTZ_FT_TG", TableFired), ("IDX_QRTZ_FT_TRIG_INST_NAME", TableFired),
        ("IDX_QRTZ_FT_TRIG_NM_GP", TableFired), ("IDX_QRTZ_FT_TRIG_NAME", TableFired),
        ("IDX_QRTZ_FT_TRIG_GROUP", TableFired), ("IDX_QRTZ_FT_JOB_NAME", TableFired),
        ("IDX_QRTZ_FT_JOB_GROUP", TableFired), ("IDX_QRTZ_FT_JOB_REQ_RECOVERY", TableFired),
    ];

    /// <summary>
    /// The 3.16-era index shapes, for the dialect that has any: PostgreSQL's 3.16 indexes omitted
    /// <c>sched_name</c>, and <c>idx_qrtz_t_nft_st</c> had its two columns the wrong way round.
    /// </summary>
    static readonly Dictionary<string, IndexDef[]> Historical316 = new()
    {
        ["postgres"] =
        [
            new("IDX_QRTZ_J_REQ_RECOVERY", TableJobs, "REQUESTS_RECOVERY"),
            new("IDX_QRTZ_T_NEXT_FIRE_TIME", TableTriggers, "NEXT_FIRE_TIME"),
            new("IDX_QRTZ_T_NFT_ST", TableTriggers, "NEXT_FIRE_TIME, TRIGGER_STATE"),
        ],
    };

    /// <summary>
    /// The indexes in <paramref name="target" /> whose name a database may already carry over a
    /// different column list, and which therefore have to be dropped before they are created.
    /// </summary>
    /// <remarks>
    /// Every <c>CREATE INDEX</c> here is guarded, and a guard keyed on the name alone cannot tell a
    /// right-shaped index from a wrong-shaped one: <c>CREATE INDEX IF NOT EXISTS</c> finds the name
    /// taken and silently keeps the old columns. Both index sets a database may be arriving from are
    /// consulted — what the current 3.x script creates, and what 3.16 created before it — because a
    /// shape that moved between 3.x and 4.x is as invisible to the guard as one that moved in 3.16.
    /// </remarks>
    static IEnumerable<(string Name, string Table)> ReshapedIndexes(string dialect, IndexDef[] target)
    {
        List<IndexDef> history = [];

        if (Indexes3X.TryGetValue(dialect, out IndexDef[] shipped3X))
        {
            history.AddRange(shipped3X);
        }

        if (Historical316.TryGetValue(dialect, out IndexDef[] historical))
        {
            history.AddRange(historical);
        }

        foreach (IndexDef t in target)
        {
            if (history.Any(h => h.Name == t.Name && TightColumns(h.Columns) != TightColumns(t.Columns)))
            {
                yield return (t.Name, t.Table);
            }
        }
    }

    const string MySqlBlobDuplicateDrop = """
        -- === Drop the auto-named duplicate index on QRTZ_BLOB_TRIGGERS ================
        -- The 3.x table was declared with an inline INDEX on the primary key's own columns, which
        -- InnoDB stores as a second copy of the primary key. It has no portable name -- InnoDB names
        -- it after its first column -- so look it up rather than guessing.
        SET @dupIndex = (SELECT INDEX_NAME FROM INFORMATION_SCHEMA.STATISTICS
                         WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'QRTZ_BLOB_TRIGGERS'
                           AND INDEX_NAME <> 'PRIMARY' LIMIT 1);
        SET @preparedStatement = IF(@dupIndex IS NULL,
          'SELECT 1',
          CONCAT('DROP INDEX `', @dupIndex, '` ON QRTZ_BLOB_TRIGGERS'));
        PREPARE stmt FROM @preparedStatement;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
        """;

    static readonly string[] MySqlBlobNote =
    [
        "",
        "MySQL only: QRTZ_BLOB_TRIGGERS was created with an inline INDEX on",
        "(SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP), an exact duplicate of that table's primary key.",
        "The primary key already satisfies InnoDB's index requirement for the foreign key, so the",
        "extra copy is pure write overhead. InnoDB auto-names it, usually SCHED_NAME, so the script",
        "below looks the name up in INFORMATION_SCHEMA rather than guessing it.",
    ];

    /// <summary>
    /// Converges the index set onto <paramref name="target"/>: create everything it expects, then
    /// drop every legacy name that is not in it. Every statement is guarded, so this is idempotent
    /// and safe on a partially-migrated schema.
    /// </summary>
    static string Converge(string dialect, IndexDef[] target)
    {
        HashSet<string> keep = target.Select(t => t.Name).ToHashSet();
        List<string> o = [];

        (string Name, string Table)[] changed = ReshapedIndexes(dialect, target).ToArray();
        if (changed.Length > 0)
        {
            o.AddRange([
                "-- === Drop the indexes whose columns changed but whose name did not ============",
                "-- These have to go first: CREATE INDEX IF NOT EXISTS below would find the name",
                "-- already taken and silently keep the old, wrong column order.",
                ""]);
            o.AddRange(changed.Select(c => DropIndex(dialect, c.Name, c.Table) + "\n"));
        }

        o.AddRange(["-- === Create the indexes this version expects ===================================", ""]);
        o.AddRange(target.Select(t => CreateIndex(dialect, t.Name, t.Table, t.Columns) + "\n"));

        o.AddRange([
            "-- === Drop the ones it no longer uses ==========================================",
            "-- Guarded, so each is a no-op when that index is not present.",
            ""]);
        o.AddRange(AllLegacyIndexes.Where(l => !keep.Contains(l.Name))
            .Select(l => DropIndex(dialect, l.Name, l.Table) + "\n"));

        if (dialect == "mysql_innodb")
        {
            o.AddRange(["", MySqlBlobDuplicateDrop]);
        }

        return string.Join("\n", o).TrimEnd();
    }

    /// <summary>Every generated script, as a repo-relative path under database/migrations and its content.</summary>
    static List<(string Path, string Content)> BuildMigrationScripts()
    {
        List<(string, string)> files = [];

        foreach (string d in Dialects)
        {
            // --- 2.2: SCHED_TIME on QRTZ_FIRED_TRIGGERS ---
            files.Add(($"2.2/schema_20_to_22_upgrade_{d}.sql",
                Header(d, "2.0 to 2.2", null, null,
                    ["REQUIRED when upgrading from 2.0/2.1 to 2.2 or later with AdoJobStore."],
                    [
                        "Adds SCHED_TIME to QRTZ_FIRED_TRIGGERS so recovery jobs see both the scheduled and",
                        "the actual fire time (#113).",
                        "",
                        "The column is NOT NULL with no default, so the ALTER fails on a table that already",
                        "holds rows. QRTZ_FIRED_TRIGGERS only ever holds in-flight entries, so stop the",
                        "scheduler and clear it first:",
                        "",
                        "  DELETE FROM QRTZ_FIRED_TRIGGERS;",
                    ],
                    sqliteNotIdempotent: true)
                + "\n\n" + AddColumn(d, TableFired, "SCHED_TIME", SchedTime[d])));

            // --- 2.6: TIME_ZONE_ID on the two trigger property tables ---
            files.Add(($"2.6/schema_25_to_26_upgrade_{d}.sql",
                Header(d, "2.5 to 2.6", null, null,
                    ["REQUIRED when upgrading from 2.5 or earlier to 2.6 or later with AdoJobStore."],
                    [
                        "Adds TIME_ZONE_ID to QRTZ_SIMPROP_TRIGGERS and QRTZ_CRON_TRIGGERS so a trigger's",
                        "time zone survives a restart (#136). Both tables need it (#1985).",
                    ],
                    sqliteNotIdempotent: true)
                + "\n\n" + AddColumn(d, "QRTZ_SIMPROP_TRIGGERS", "TIME_ZONE_ID", TimeZoneId[d])
                + "\n\n" + AddColumn(d, "QRTZ_CRON_TRIGGERS", "TIME_ZONE_ID", TimeZoneId[d])));

            // --- 3.17: MISFIRE_ORIG_FIRE_TIME ---
            files.Add(($"3.17/add_misfire_orig_fire_time_{d}.sql",
                Header(d, "add MISFIRE_ORIG_FIRE_TIME", "3.17.0", "#2899",
                    [
                        "3.x  OPTIONAL. Without it AdoJobStore keeps working, but ScheduledFireTimeUtc",
                        "     equals FireTimeUtc for misfired triggers (the pre-3.17 behavior). The job",
                        "     store probes at startup and logs a warning when the column is absent.",
                        "     RAMJobStore is unaffected.",
                        .. RequiredOn4X(d),
                    ],
                    [
                        "Stores the original scheduled fire time before misfire handling overwrites it, which",
                        "is what makes ScheduledFireTimeUtc correct for misfired triggers under the \"fire",
                        "now\" misfire policies (FireOnceNow, FireNow, etc.).",
                    ],
                    sqliteNotIdempotent: true)
                + "\n\n" + AddColumn(d, TableTriggers, "MISFIRE_ORIG_FIRE_TIME", MisfireOrigFireTime[d])));

            // --- 3.18: EXECUTION_GROUP on both tables ---
            files.Add(($"3.18/add_execution_group_{d}.sql",
                Header(d, "add EXECUTION_GROUP", "3.18.0", "#3004",
                    [
                        "3.x  OPTIONAL. Without it execution groups still work, but the per-node limit is",
                        "     applied by in-memory filtering after acquisition rather than in the acquire",
                        "     query. The job store probes at startup.",
                        .. RequiredOn4X(d),
                    ],
                    [
                        "Carries the execution group tag that per-node thread limits are enforced against.",
                        "Both tables must be altered together.",
                    ],
                    sqliteNotIdempotent: true)
                + "\n\n" + AddColumn(d, TableTriggers, "EXECUTION_GROUP", ExecutionGroup[d])
                + "\n\n" + AddColumn(d, TableFired, "EXECUTION_GROUP", ExecutionGroup[d])));

            // --- 3.19: PREFERRED_NODE and PREFERRED_NODE_AUTO ---
            files.Add(($"3.19/add_preferred_node_{d}.sql",
                Header(d, "add PREFERRED_NODE and PREFERRED_NODE_AUTO", "3.19.0", "#3013, #3144",
                    [
                        "3.x  OPTIONAL. Without the columns node affinity is unavailable; the scheduler",
                        "     logs a warning at startup and otherwise behaves exactly as before 3.19.",
                        .. RequiredOn4X(d),
                    ],
                    [
                        "These back node affinity (pinning a trigger to a preferred cluster node).",
                        "",
                        "PREFERRED_NODE holds the target node's instance id verbatim, or the \"*\" sentinel",
                        "requesting auto-pin. PREFERRED_NODE_AUTO records whether that pin was claimed",
                        "automatically by the node that first fired the trigger -- auto-claimed pins are",
                        "released back to \"*\" when their node dies, explicit pins are preserved.",
                        "",
                        "BOTH COLUMNS MUST BE ADDED TOGETHER. Quartz probes for both and only enables node",
                        "affinity when both are present, so adding just one leaves the feature off.",
                        "",
                        "The 3.x and 4.x representations are identical, so no data migration is needed.",
                    ],
                    sqliteNotIdempotent: true)
                + "\n\n" + AddColumn(d, TableTriggers, "PREFERRED_NODE", PreferredNode[d])
                + "\n\n" + AddColumn(d, TableTriggers, "PREFERRED_NODE_AUTO", PreferredNodeAuto[d])));

            // --- 3.20: align the index set with what 3.x tables_<dialect>.sql creates ---
            List<string> indexExtra =
            [
                "Brings an existing database's index set in line with what the current",
                $"database/tables/tables_{d}.sql creates. A database created from the current",
                "script already matches and needs nothing from this file.",
                "",
                "Every Quartz statement filters SCHED_NAME first, so every index here leads with it.",
                "Indexes that are a leftmost prefix of a wider one, or that no statement can drive a",
                "scan from, are dropped.",
            ];

            if (d == "postgres")
            {
                indexExtra.AddRange([
                    "",
                    "On a busy database use CREATE INDEX CONCURRENTLY / DROP INDEX CONCURRENTLY instead;",
                    "neither can run inside a transaction block, so run those statements one at a time.",
                ]);
            }

            if (d == "mysql_innodb")
            {
                indexExtra.AddRange(MySqlBlobNote);
            }

            files.Add(($"3.20/index_alignment_{d}.sql",
                Header(d, "align indexes with the 3.x schema", "3.20.0", "#3203",
                    [
                        "3.x  OPTIONAL, performance only. Nothing stops working if it is not applied, but",
                        "     several of these indexes could not serve a single-scheduler lookup at all.",
                        "",
                        $"4.x  Superseded. ../4.0/schema_30_to_40_upgrade_{d}.sql converges the same index",
                        "     set onto the 4.x shape -- run that instead when upgrading to 4.x.",
                    ],
                    indexExtra)
                + "\n\n" + Converge(d, Indexes3X[d])));

            // --- 4.0: everything above, plus the 4.x index shape ---
            files.Add(($"4.0/schema_30_to_40_upgrade_{d}.sql", Build40Script(d)));
        }

        return files;
    }

    static string Build40Script(string dialect)
    {
        // Every dialect but SQLite guards its ADD COLUMN, so its script can land on a database that
        // already took some of the optional 3.x migrations. SQLite has no conditional DDL, so saying
        // the same thing there was a lie -- it fails on the first column that is already present
        // (#3322). Sections 1-4 are the unguarded ones; section 5 is guarded on every dialect.
        string[] supersedes = dialect == "sqlite"
            ?
            [
                "This script supersedes the optional per-feature migrations in ../3.17, ../3.18,",
                "../3.19 and ../3.20 -- it applies everything they do, and it assumes none of them",
                "were applied. Run it exactly once, against a database that took none of the optional",
                "3.x column migrations.",
                "",
                "On a partially-migrated database take the stepped route instead -- run the",
                "per-feature files you are still missing -- or check PRAGMA table_info(<table>) and",
                "apply only the sections whose columns are absent.",
            ]
            :
            [
                "This script supersedes the optional per-feature migrations in ../3.17, ../3.18,",
                "../3.19 and ../3.20 -- it applies everything they do. If you already ran some of",
                "them, run this anyway: every statement checks first, so it is safe on a",
                "partially-migrated database.",
            ];

        List<string> extra =
        [
            .. supersedes,
            "",
            "Sections, in order:",
            "  1. MISFIRE_ORIG_FIRE_TIME column                REQUIRED",
            "  2. EXECUTION_GROUP columns                      REQUIRED",
            "  3. PREFERRED_NODE / PREFERRED_NODE_AUTO         REQUIRED",
            "  4. RETRY_POLICY / RETRY_ATTEMPT                 REQUIRED",
            "  5. QRTZ_PAUSED_JOB_GRPS table                   REQUIRED",
            "  6. Index set aligned with the 4.x schema        optional",
            "",
            "Run the sections in order: the drops in section 6 assume the creates above them have",
            "already succeeded.",
            "",
            "Sections 1-5 are safe to run while 3.x nodes are still up. SECTION 6 IS NOT: it drops",
            "IDX_QRTZ_T_NFT_ST_MISFIRE, which 3.x drives its misfire sweep from and 4.x does not read",
            "at all (#3656). Run section 6 once the last 3.x node has shut down.",
            "",
            "Sections 4 and 5 have no 3.x counterpart at all, so nothing you ran on 3.x can have",
            "applied them.",
            "",
            "RETRY_POLICY holds a trigger's retry policy and RETRY_ATTEMPT how many retries of the",
            "occurrence being executed have already been made. Both are nullable with no default, so",
            "every existing row reads as \"no retry policy\" and no data migration is needed (#3520).",
            "",
            "3.x pauses a job group without recording it anywhere, so a paused job group could not be",
            "listed or asked about; 4.x keeps the group names in QRTZ_PAUSED_JOB_GRPS, which is what",
            "makes JobGroup.Paused answer truthfully and what carries the pause across a restart",
            "(#3336).",
        ];

        if (dialect == "mysql_innodb")
        {
            extra.AddRange(MySqlBlobNote);
        }

        string header = Header(dialect, "3.x to 4.0", null, null,
            [
                "MANDATORY. This is the one migration you cannot skip.",
                "",
                "Quartz.NET 3.x probes for MISFIRE_ORIG_FIRE_TIME, EXECUTION_GROUP, PREFERRED_NODE",
                "and PREFERRED_NODE_AUTO at startup and degrades gracefully when they are absent.",
                "4.x removed those probes and assumes all four exist, so a 3.x database that never",
                "ran the optional migrations will fail against 4.x until this script has run.",
                "",
                "4.x also adds columns and a table 3.x never had -- RETRY_POLICY and RETRY_ATTEMPT",
                "on QRTZ_TRIGGERS, and the whole QRTZ_PAUSED_JOB_GRPS table -- and validates its",
                "schema at startup, so this script is required even for a 3.x database that took",
                "every optional migration going.",
            ],
            extra,
            sqliteNotIdempotent: true);

        string[] sections =
        [
            "-- === 1. MISFIRE_ORIG_FIRE_TIME on QRTZ_TRIGGERS ===\n"
                + "-- REQUIRED for 4.x. Optional in 3.17, so it may already be present.\n\n"
                + AddColumn(dialect, TableTriggers, "MISFIRE_ORIG_FIRE_TIME", MisfireOrigFireTime[dialect]),

            "-- === 2. EXECUTION_GROUP on QRTZ_TRIGGERS and QRTZ_FIRED_TRIGGERS ===\n"
                + "-- REQUIRED for 4.x. Optional in 3.18, so it may already be present.\n\n"
                + AddColumn(dialect, TableTriggers, "EXECUTION_GROUP", ExecutionGroup[dialect])
                + "\n\n" + AddColumn(dialect, TableFired, "EXECUTION_GROUP", ExecutionGroup[dialect]),

            "-- === 3. PREFERRED_NODE and PREFERRED_NODE_AUTO on QRTZ_TRIGGERS ===\n"
                + "-- REQUIRED for 4.x. Optional in 3.19, so it may already be present.\n\n"
                + AddColumn(dialect, TableTriggers, "PREFERRED_NODE", PreferredNode[dialect])
                + "\n\n" + AddColumn(dialect, TableTriggers, "PREFERRED_NODE_AUTO", PreferredNodeAuto[dialect]),

            "-- === 4. RETRY_POLICY and RETRY_ATTEMPT on QRTZ_TRIGGERS ===\n"
                + "-- REQUIRED for 4.x, and new in it -- 3.x has no equivalent, so on a database coming\n"
                + "-- from 3.x both columns are always absent. Nullable with no default: an existing row\n"
                + "-- reads as \"no retry policy\".\n\n"
                + AddColumn(dialect, TableTriggers, "RETRY_POLICY", RetryPolicy[dialect])
                + "\n\n" + AddColumn(dialect, TableTriggers, "RETRY_ATTEMPT", RetryAttempt[dialect]),

            "-- === 5. QRTZ_PAUSED_JOB_GRPS ===\n"
                + "-- REQUIRED for 4.x, and new in it -- 3.x has no equivalent. One row per paused job\n"
                + "-- group, mirroring QRTZ_PAUSED_TRIGGER_GRPS. Guarded on every dialect, SQLite\n"
                + "-- included: CREATE TABLE IF NOT EXISTS is conditional DDL SQLite does have.\n\n"
                + CreateTable(dialect, TablePausedJobGroups, PausedJobGroupsTable[dialect]),

            "-- === 6. Index set ===\n"
                + "-- OPTIONAL: 4.x runs unchanged either way. The creates matter once a schema holds a\n"
                + "-- non-trivial number of triggers; the drops only reclaim write cost and storage.\n"
                + "--\n"
                + "-- RUN THIS SECTION ONLY ONCE THE LAST 3.x NODE HAS SHUT DOWN. Among the drops is\n"
                + "-- IDX_QRTZ_T_NFT_ST_MISFIRE, which 3.x drives its misfire sweep from. 4.x drives both\n"
                + "-- misfire statements from IDX_QRTZ_T_NFT_ST instead, which this section's creates put\n"
                + "-- at its 4.x shape before that drop runs -- so a schema still on the pre-4.x shape is\n"
                + "-- reshaped here first, and none is ever left with neither index. That ordering is the\n"
                + "-- whole precondition, so run the section top to bottom.\n"
                + "--\n"
                + "-- Every statement is guarded, so re-running the section changes nothing.\n\n"
                + Converge(dialect, Target4X(dialect)),
        ];

        return header + "\n\n" + string.Join("\n\n", sections);
    }
}
