# Quartz.NET Cluster Node Concurrent Execution Tester

This console application tests for concurrent execution violations in Quartz.NET clustering scenarios. Each instance runs as an independent cluster node competing to execute a job marked with `[DisallowConcurrentExecution]` to detect potential bugs where the attribute is not properly enforced.

## Purpose

This test is designed to detect a reported bug where jobs marked with `[DisallowConcurrentExecution]` may still be executed concurrently across different cluster nodes. This violates the expected behavior and can lead to data corruption or other issues in production environments.

## Database Setup

Use the docker containers.

**Database Provider Override:**
By default, the application reads the `DatabaseProvider` setting from `appsettings.json`. However, you can override this at runtime using the `--database-provider` command-line option:

```bash
# Use SQL Server (overriding appsettings.json)
dotnet run -- node Node-1 --database-provider SqlServer

# Use PostgreSQL (overriding appsettings.json)
dotnet run -- node Node-1 --database-provider Npgsql

# Use MySQL (overriding appsettings.json)
dotnet run -- node Node-1 --database-provider MySql
```

This is useful for:
- Testing different database providers without modifying configuration files
- CI/CD pipelines that dynamically select database providers
- Integration tests that programmatically specify the database

**Note:** The connection string must still be present in `appsettings.json` for the specified provider.

## Running the Test

### Getting Help

View available commands and options:

```bash
# Show main help
Quartz.Tests.ClusterNode --help

# Show help for specific command
Quartz.Tests.ClusterNode orchestrator --help
Quartz.Tests.ClusterNode node --help

# Show version
Quartz.Tests.ClusterNode --version
```

### Quick Start (Recommended) - Orchestrator Mode

Use the built-in orchestrator mode to automatically start multiple cluster nodes:

```bash
cd src/Quartz.Tests.ClusterNode

# Build the project first
dotnet build --configuration Release

# Run with defaults (2 nodes, 120 seconds)
Quartz.Tests.ClusterNode orchestrator

# Or on Windows
Quartz.Tests.ClusterNode.exe orchestrator

# Run with custom settings (full names or short aliases)
Quartz.Tests.ClusterNode orchestrator --node-count 4 --duration 60 --job-interval 300 --job-delay 50

# Using short aliases
Quartz.Tests.ClusterNode orchestrator -n 4 -d 60 -i 300 -j 50
```

**Orchestrator Options:**
- `--node-count, -n <int>` - Number of cluster nodes to start (default: 2)
- `--duration, -d <int>` - Test duration in seconds (default: 120)
- `--job-interval, -i <int>` - Job trigger interval in milliseconds (default: 500)
- `--job-delay, -j <int>` - Job execution delay in milliseconds (default: 100)
- `--no-pause` - Windows automatically close when nodes complete (no pause prompt) (default: false)

This will:
1. Clean the Quartz.NET database tables (runs `cleanup` command)
2. Launch N separate windows, each running an independent cluster node
3. The first node is automatically started with `--schedule --init` flags
4. Monitor for violations across all nodes
5. Display final statistics when complete
6. Each node exports results to JSON on shutdown

**Note:** By default, node windows remain open after completion with a "Press any key to close..." message for reviewing output. Use the `--no-pause` flag to automatically close all windows when nodes complete (no manual interaction required), which is useful for:
- **Automated testing and CI/CD pipelines** - No human intervention needed
- **Integration test scenarios** - Programmatic test execution
- **Unattended test runs** - Start the test and walk away
- **Batch processing** - Run multiple test configurations sequentially

**Platform behavior with `--no-pause`:**
- **Windows**: PowerShell windows close immediately on node exit
- **Linux**: Terminal windows close on node exit (behavior may vary by terminal emulator settings)
- **macOS**: Terminal.app windows close on node exit (unless user has modified Terminal preferences)

### Database Cleanup Mode

To clean the database before running tests:

```bash
Quartz.Tests.ClusterNode cleanup
```

This removes all data from the Quartz.NET tables and test tracking tables. The orchestrator mode automatically runs cleanup before starting nodes.

### Manual - Individual Nodes

To manually start nodes one at a time:

```bash
# Terminal 1 - First node (schedules the job and initializes tables)
dotnet run -- node Node-1 --schedule --init

# Terminal 2 - Second node
dotnet run -- node Node-2

# Terminal 3 - Third node with custom parameters
dotnet run -- node Node-3 --duration 60 --job-interval 300 --job-delay 50

# Using short aliases
dotnet run -- node Node-4 -d 60 -i 300 -j 50

# Override database provider (useful for testing different databases)
dotnet run -- node Node-1 --schedule --init --database-provider SqlServer
dotnet run -- node Node-2 --database-provider SqlServer
```

**Node Command Options:**
- `<node-id>` - Node identifier (required, e.g., "Node-1")
- `--duration, -d <int>` - Test duration in seconds (default: 120)
- `--job-interval, -i <int>` - Job trigger interval in milliseconds (default: 500)
- `--job-delay, -j <int>` - Job execution delay in milliseconds (default: 100)
- `--schedule` - Flag to indicate this node should schedule the job (default: false)
- `--init` - Flag to initialize test database tables (default: false)
- `--database-provider <string>` - Database provider to use (SqlServer, Npgsql, MySql) - overrides appsettings.json (default: null)
- `--connection-string <string>` - Connection string to use - overrides appsettings.json (default: null)

**Important:**
- Only ONE node should use the `--schedule` flag (typically the first node)
- The `--init` flag should be used on the first node to create and clear test tracking tables
- The orchestrator mode automatically uses `--init` on the first node
- Use `--database-provider` to override the database provider from appsettings.json, useful for testing different databases or automated testing scenarios

## How It Works

1. **Node Startup**: Each process starts a single Quartz scheduler instance as an independent cluster node
2. **Database Initialization**: The first node (with `--init` flag) creates and clears test tracking tables
3. **Job Scheduling**: Only the first node (with `--schedule` flag) schedules the job (`ConcurrentExecutionTestJob`)
4. **Clustering**: All nodes connect to the same SQL Server database and use Quartz.NET's clustering features
5. **Execution Monitoring**: The job uses **database-backed state tracking** to detect concurrent execution across all nodes
6. **Violation Detection**: If the `[DisallowConcurrentExecution]` attribute is violated, it's immediately reported in RED
7. **Shutdown & Export**: On graceful shutdown, each node exports test results to JSON files in the `results/` directory

### Database-Backed Detection (Cross-Process)

Unlike static variables (which only work within a single process), this test uses **database tables for shared state**:

**ClusterTestExecutions Table:**
- Tracks all job executions with `StartTime` and `EndTime`
- Active executions have `EndTime = NULL`
- Used for atomic concurrent execution detection

**ClusterTestViolations Table:**
- Records every violation with timestamp and node details
- Provides detailed violation report at test completion

**Atomic Detection Logic:**
1. Before job starts: Begin SERIALIZABLE transaction
2. Count executions where `EndTime IS NULL` (currently running)
3. Insert new execution record with `EndTime = NULL`
4. If count > 0, record violation
5. Commit transaction
6. After job completes: Update with `EndTime`

This approach works **across multiple processes and even multiple machines**, making it a true cluster test.

## Configuration

### Orchestrator Mode Examples

```bash
# Default settings
Quartz.Tests.ClusterNode orchestrator

# With all options (full names)
Quartz.Tests.ClusterNode orchestrator \
    --node-count 6 \         # Number of cluster nodes
    --duration 120 \         # Test duration in seconds
    --job-interval 500 \     # Job trigger interval in ms
    --job-delay 100          # Job execution time in ms

# Using short aliases
Quartz.Tests.ClusterNode orchestrator -n 6 -d 120 -i 500 -j 100

# Auto-close windows after completion (for CI/CD or automated testing)
Quartz.Tests.ClusterNode orchestrator --no-pause

# Combine with other options
Quartz.Tests.ClusterNode orchestrator -n 4 -d 30 --no-pause
```

### Environment Tuning

For maximum contention (best chance to detect violations):

```bash
# Very aggressive settings (full names)
Quartz.Tests.ClusterNode orchestrator --node-count 6 --duration 60 --job-interval 200 --job-delay 50

# Or using short aliases
Quartz.Tests.ClusterNode orchestrator -n 6 -d 60 -i 200 -j 50
```

For minimal contention (verify normal operation):

```bash
# Conservative settings
Quartz.Tests.ClusterNode orchestrator -n 3 -d 30 -i 2000 -j 500
```

## Expected Output

### Normal Execution (No Violations)

```
[14:23:01.123] Job executing on Node: Node-1 | Execution: a1b2c3d4 | Total: 1
[14:23:01.125]   -> Hello from Node-1 (execution a1b2c3d4)
[14:23:03.456] Job executing on Node: Node-2 | Execution: e5f6g7h8 | Total: 2
[14:23:03.458]   -> Hello from Node-2 (execution e5f6g7h8)
[14:23:05.789] Job executing on Node: Node-3 | Execution: i9j0k1l2 | Total: 3
```

### Violation Detected (Bug Present)

```
[14:23:01.123] Job executing on Node: Node-1 | Execution: a1b2c3d4 | Total: 1
[14:23:01.125] *** VIOLATION DETECTED *** Concurrent executions: 2 | Node: Node-2 | Execution: e5f6g7h8
```

## Interpreting Results

### Statistics Output

At the end of the test run, you'll see:

```
================================================================================
FINAL STATISTICS (DATABASE STATE)
================================================================================
Total Executions: 240
Concurrent Execution Violations: 0
Active Nodes: 3

Node Execution Breakdown:
  Node-1: 80 executions (first: 14:23:01.123, last: 14:25:01.789)
  Node-2: 82 executions (first: 14:23:01.456, last: 14:25:01.901)
  Node-3: 78 executions (first: 14:23:01.789, last: 14:25:01.567)
```

If violations are detected, you'll also see:

```
Violation Details:
  [14:23:45.123] Node: Node-2 | Count: 2 | Execution: a3f4b5c6
  [14:23:45.678] Node: Node-3 | Count: 2 | Execution: d7e8f9a0
```

- **Total Executions**: Number of times the job ran across all nodes (from database)
- **Concurrent Execution Violations**: **Should be 0** - any value > 0 indicates a bug
- **Active Nodes**: Number of cluster nodes that participated
- **Node Breakdown**: Shows distribution of executions across nodes with timing

### What to Look For

✅ **PASS**: Violations = 0 (job never ran concurrently)
❌ **FAIL**: Violations > 0 (DisallowConcurrentExecution was violated)

## Troubleshooting

### Database Connection Errors

**Error: "The target principal name is incorrect. Cannot generate SSPI context"**

This occurs when using Windows Authentication (`Integrated Security=true`) with `127.0.0.1`. Solutions:
1. Use SQL Authentication instead (recommended for testing):
   ```json
   "SqlServer": "Server=127.0.0.1;Database=quartznet;User Id=sa;Password=YourPassword;Encrypt=True;TrustServerCertificate=true;"
   ```
2. Or change `127.0.0.1` to `localhost` when using Windows Auth
3. Ensure you don't mix both auth methods (remove `Integrated Security=true` if using User Id/Password)

**Other Connection Issues:**
- Ensure the database server is running
- Verify the connection string is correct in `appsettings.json`
- Check that the `quartznet` database exists
- Confirm credentials are correct (for SQL Auth) or user has access (for Windows Auth)

### Schema Errors

- Make sure you ran the schema creation script
- Check that all Quartz.NET tables exist in the database
- Verify table prefixes match (default: `QRTZ_`)

### No Job Executions

- Check that all nodes started successfully
- Verify the job was scheduled (look for "Job scheduled" message)
- Check SQL Server for locks or blocking queries
- Review the `QRTZ_SCHEDULER_STATE` table for node health

## Advanced Usage

### Increasing Load

To increase the likelihood of detecting violations:

1. Increase node count: `orchestrator --node-count 6`
2. Decrease job interval: `orchestrator --job-interval 200` (200ms between triggers)
3. Decrease job delay: `orchestrator --job-delay 50` (50ms execution time)
4. Combine all: `orchestrator -n 6 -i 200 -j 50`

The checkin interval is configured in the code at `c.CheckinInterval = TimeSpan.FromSeconds(1)` for aggressive contention testing.

### Database Cleanup

The test automatically cleans the tracking tables when the first node starts. To manually reset between test runs:

### Additional Database Tables

The test automatically creates two additional tables for tracking:

**ClusterTestExecutions:**
```sql
CREATE TABLE ClusterTestExecutions (
    ExecutionId NVARCHAR(50) PRIMARY KEY,
    NodeId NVARCHAR(200) NOT NULL,
    StartTime DATETIME2 NOT NULL,
    EndTime DATETIME2 NULL  -- NULL while executing
)
```

**ClusterTestViolations:**
```sql
CREATE TABLE ClusterTestViolations (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    ExecutionId NVARCHAR(50) NOT NULL,
    NodeId NVARCHAR(200) NOT NULL,
    DetectedAt DATETIME2 NOT NULL,
    ConcurrentCount INT NOT NULL
)
```

These tables are automatically created and cleaned when the first node starts with the `--init` flag.

### Test Results Export

On graceful shutdown, each node exports its test results to a JSON file:

**Export Location:** `results/{NodeId}-results.json`

**JSON Structure:**
```json
{
  "nodeId": "Node-1",
  "processId": 12345,
  "startTime": "2024-03-07T12:00:00Z",
  "endTime": "2024-03-07T12:02:00Z",
  "statistics": {
    "totalExecutions": 116,
    "totalViolationPeriods": 0,
    "nodeCount": 2
  },
  "executions": [...],
  "violations": [...],
  "nodeBreakdown": [...]
}
```

This JSON export enables integration tests to verify results without requiring direct database access.
