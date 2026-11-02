# Job stores and database scripts

The stores, the ADO.NET delegates, and the rules for the schema scripts and migrations.

`AGENTS.md` states each rule here in brief and routes to this document, which holds the full text
and wins where the two differ.

## Job Stores

- **`RAMJobStore`** (`Quartz.Impl`) — in-memory, volatile. Default.
- **`AdoJobStoreBase`** → `LocalTransactionJobStore` / `ExternalTransactionJobStore` (`Quartz.Impl.AdoJobStore`) — ADO.NET-based persistence with database-specific delegates (`SqlServerDelegate`, `PostgreSQLDelegate`, `OracleDelegate`, `MySQLDelegate`, `SQLiteDelegate`, `FirebirdDelegate`). On 3.x these are `JobStoreSupport` → `JobStoreTX` / `JobStoreCMT`. All three are **internal**: `UsePersistentStore` builds the local one and `UsePersistentStore(s => s.UseAmbientTransactions())` the other, the driver delegate is the seam, and `quartz.jobStore.type` still names either by string.

## Database scripts

- `database/tables/tables_<dialect>.sql` — fresh-install DDL, one per database.
- `database/migrations/<version>/<name>_<dialect>.sql` — schema changes, grouped by the Quartz.NET version that introduced them. One directly-runnable file per database; no commented-out dialect blocks. **Generated** — describe the change in `build/Build.DatabaseMigrations.Scripts.cs` and run `dotnet fallout GenerateMigrations`; never hand-edit the output. The `2.0` and `3.0` folders are the exception: hand-written, SQL Server-only historical scripts. `VerifyMigrations` runs in CI and fails when the two are out of step.
- `database/README.md` — the index: run order, per-version status, and old→new path mapping.

Rules when touching any of this:

- **A migration both branches can run must stay byte-identical on `main` and `3.x`.** It lands on both in a companion PR pair, or a documented path 404s on whichever branch lacks it — which is what #3218 reported. **`database/migrations/4.0/` is the exception and lives only here**: it is the 3.x → 4.0 upgrade path, its content is decided by 4.x's schema, and `main` is its single maintained home — `3.x` links to it rather than mirroring it, because a mirrored copy goes stale silently the next time 4.x's schema moves, and a confidently wrong upgrade script is worse than a missing one. `database/README.md` carries a **Branch** column saying which versions are on both; keep it accurate, since nothing in CI checks cross-branch identity (`VerifyMigrations` only compares a branch's scripts with its own generator). Each branch's `database/README.md` indexes what that branch carries. `database/tables/` is the *current* schema and differs by design.
- `database/migrations/4.2/` onward are the schema moves since 4.0; a 4.x migration adds nullable columns only, so the previous minor's nodes run beside the next's — a continuation is an `AWAITING` trigger its parent's completion settles in its transaction.
- Every migration ships a file for **every** supported dialect (`sqlServer`, `postgres`, `mysql_innodb`, `oracle`, `sqlite`, `firebird`), guarded so it is safe to re-run. SQLite `ADD COLUMN` is the one exception — it has no conditional DDL.
- 4.x has no `Supports*Column` probes, so anything **optional on 3.x is required on 4.x**. Fold every 3.x column migration into `database/migrations/4.0/schema_30_to_40_upgrade_<dialect>.sql` — the fold happens here even when the migration itself was written on `3.x`, because that script is generated on `main` alone. The one exception is a migration that adds a **table** only an opt-in feature reads: it goes in `AdoConstants.OptionalTableNames` rather than `AllTableNames`, is probed at startup only when that feature is on, and is created by a fresh install and by `ProvisionSchema()` all the same — `4.2/add_execution_history_<dialect>.sql` is the case.
- The migration chains read the folder (`MigrationChains.Since`), and `SchemaScriptTest` holds all eight `tables_*.sql` to `create_*.sql`: a forgotten place fails a test.
- Adding a migration also needs a version section in `docs/documentation/database/schema-changes.md`. The docs site is built from `main` only, and it documents both the 3.x and 4.x trees.
