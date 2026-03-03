# Quick Start Guide

## Run the Test in 3 Steps

### 1. Ensure Database is Ready
```powershell
# Create database and schema (one-time setup)
sqlcmd -S localhost -Q "CREATE DATABASE quartznet"
sqlcmd -S localhost -d quartznet -E -i ..\..\database\tables\tables_sqlServer.sql
```

### 2. Navigate to Test Directory
```powershell
cd src\Quartz.Tests.ClusterNode
```

### 3. Launch the Test
```powershell
# Default test (4 nodes, 120 seconds, moderate contention)
.\run-cluster-test.ps1

# OR Quick aggressive test (6 nodes, 60 seconds, high contention)
.\run-cluster-test.ps1 -NodeCount 6 -Duration 60 -JobInterval 200 -JobDelay 50

# Auto-close windows after completion (for automated/unattended testing)
Quartz.Tests.ClusterNode orchestrator --no-pause
```

**Tip**: Use `--no-pause` for CI/CD pipelines or when you don't need to review the output in the windows. Without this flag, windows stay open with a "Press any key to close..." prompt.

## What to Watch For

After launching, you'll see **3-6 separate windows** open (one per node).

### ✅ Normal Behavior (No Bug)
```
[20:15:30.123] Job executing on Node: Node-1 | Execution: abc123 | Total: 42
[20:15:30.234]   -> Hello from Node-1 (execution abc123)
```
- **GREEN** text for normal execution
- Only one node executes at a time
- Final statistics show: `Concurrent Execution Violations: 0`

### ❌ Bug Detected (Violation)
```
[20:15:30.123] *** VIOLATION DETECTED *** Concurrent executions: 2 | Node: Node-2 | Execution: def456
```
- **RED** text for violations
- Multiple nodes executing simultaneously
- Final statistics show: `Concurrent Execution Violations: >0`

## Test Scenarios

### Conservative Test (Verify Normal Operation)
```powershell
.\run-cluster-test.ps1 -NodeCount 3 -Duration 30 -JobInterval 2000 -JobDelay 500
```
- 3 nodes, 30 seconds
- Job every 2 seconds, runs for 500ms
- Low contention, should always pass

### Aggressive Test (Maximum Violation Detection)
```powershell
.\run-cluster-test.ps1 -NodeCount 6 -Duration 120 -JobInterval 100 -JobDelay 50
```
- 6 nodes, 120 seconds
- Job every 100ms, runs for 50ms
- Very high contention, best chance to detect bugs

### Marathon Test (Long-term Stability)
```powershell
.\run-cluster-test.ps1 -NodeCount 4 -Duration 600 -JobInterval 500 -JobDelay 100
```
- 4 nodes, 10 minutes
- Medium contention
- Tests for race conditions that appear over time

## Understanding the Output

Each node window shows:
- **Process ID** - Unique OS process identifier
- **Node ID** - Quartz cluster node identifier
- **Execution logs** - Each job execution with timestamp
- **Summary statistics** - Every 20 executions
- **Final report** - Total executions, violations, active nodes

### Example Final Report
```
================================================================================
FINAL STATISTICS
================================================================================
Total Executions: 240
Concurrent Execution Violations: 0          ← Should be 0!
Active Nodes: 4

Node Last Execution Times:
  Node-1: 20:16:45.123
  Node-2: 20:16:42.456
  Node-3: 20:16:43.789
  Node-4: 20:16:44.012
================================================================================
```

## Troubleshooting

### "Database connection failed"
```powershell
# Verify SQL Server is running and database exists
sqlcmd -S localhost -Q "SELECT name FROM sys.databases WHERE name='quartznet'"
```

### "Unable to store Job: already exists"
```powershell
# Clean up previous test data
sqlcmd -S localhost -d quartznet -Q "DELETE FROM QRTZ_SIMPLE_TRIGGERS; DELETE FROM QRTZ_TRIGGERS; DELETE FROM QRTZ_JOB_DETAILS;"
```

### Windows don't stay open
The windows close automatically when the test completes. To keep them open:
- Reduce `-Duration` to see them while running
- Check event logs if crashes occur
- Run manually: `dotnet run -- Node-Test 60 500 100 true`

## Next Steps

- Review [README.md](README.md) for detailed documentation
- Adjust parameters to match your production environment
- Run tests before/after code changes
- Use aggressive settings to stress-test clustering logic
