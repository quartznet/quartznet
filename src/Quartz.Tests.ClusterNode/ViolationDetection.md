# Violation Detection Mechanism

The violation detection mechanism in the cluster test works by using **database-level concurrent execution tracking** to detect when Quartz.NET's `[DisallowConcurrentExecution]` attribute is violated across cluster nodes. Here's how it works:

## Core Mechanism

**Database Tables:**
1. **ClusterTestExecutions** - tracks every job execution with `StartTime` and `EndTime`
2. **ClusterTestViolations** - records violation **periods** with `DetectedAt` and `EndedAt`

**Detection Flow** (`ConcurrentExecutionTestJob.cs:89-170`):

1. **Atomic Check-and-Insert** (using `Serializable` transaction):
   ```
   - Query: COUNT(*) WHERE EndTime IS NULL  → currentCount (active executions)
   - Query: COUNT(*) FROM ClusterTestViolations WHERE EndedAt IS NULL → wasInCleanState check
   - Insert: New execution record with EndTime = NULL
   - Commit transaction
   ```

2. **State Classification**:
   - `currentCount == 0`: Valid state (no other executions running)
   - `currentCount == 1 AND wasInCleanState`: **NEW VIOLATION PERIOD** (second execution starting after being clean)
   - `currentCount > 0 AND NOT wasInCleanState`: Continuing existing violation period
   - `currentCount == 0 AND NOT wasInCleanState`: **END OF VIOLATION PERIOD** (returning to clean state)

3. **Violation Period Recording**:
   - When transitioning from clean state to violated state (0→1→2), insert a record into `ClusterTestViolations` with `EndedAt = NULL`
   - When returning to clean state (count goes to 0), update the violation record setting `EndedAt = NOW()`
   - This ensures violation **periods** are tracked, not individual overlapping executions

4. **Execution Completion**:
   - Sets `EndTime` on the execution record
   - Decrements the active execution count
   - If count reaches 0 and we were in a violation period, ends the violation period

## Why It Works

**Serializable Isolation Level**: The transaction ensures the COUNT query and INSERT happen atomically without interleaving from other nodes, preventing race conditions in the detection logic itself.

**Cross-Node Detection**: Since all nodes share the same database, executions from any node in the cluster are visible to all other nodes immediately.

## Example Scenario

### Scenario 1: Single Violation Period

```
Time  | Node-1        | Node-2        | Count | Clean? | Detection
------|---------------|---------------|-------|--------|-------------------------
T1    | Start Exec-A  |               | 0→1   | Yes    | ✓ Valid
T2    |   running...  | Start Exec-B  | 1→2   | Yes    | ⚠️ VIOLATION PERIOD STARTS
T3    |   running...  |   running...  | 2     | No     | (in violation period)
T4    | End Exec-A    |   running...  | 2→1   | No     | (still in period)
T5    |               | End Exec-B    | 1→0   | No     | ✓ VIOLATION PERIOD ENDS
```

At T2, the system transitions from clean state (0 executions) to violated (2 concurrent). This starts a **violation period**.
At T5, the system returns to clean state (0 executions), ending the violation period.

### Scenario 2: Continuous Overlapping (Single Period)

```
Time  | Active Execs  | Event         | Count | Clean? | Detection
------|---------------|---------------|-------|--------|-------------------------
T1    | A             | A starts      | 0→1   | Yes    | ✓ Valid
T2    | A, B          | B starts      | 1→2   | Yes    | ⚠️ VIOLATION PERIOD STARTS
T3    | B             | A completes   | 2→1   | No     | (continuing period)
T4    | B, C          | C starts      | 1→2   | No     | (continuing period - NOT new)
T5    | C             | B completes   | 2→1   | No     | (continuing period)
T6    | C, D          | D starts      | 1→2   | No     | (continuing period - NOT new)
T7    | D             | C completes   | 2→1   | No     | (continuing period)
T8    | -             | D completes   | 1→0   | No     | ✓ VIOLATION PERIOD ENDS
```

At T4 and T6, even though the count transitions from 1→2, these are **NOT** new violations because the system never returned to a clean state (count = 0). They're part of the same continuous violation period that started at T2.

## Logging Strategy

- **Error**: Only when a new **violation period** starts (transitioning from clean state to violated state)
- **Info**: When a violation period ends (returning to clean state)
- **Debug**: When executing during an ongoing violation period
- **Info**: Normal execution (clean state)

This prevents log spam while clearly indicating:
1. When a new violation period begins (ERROR - shown in red)
2. When a violation period ends (INFO - shown in normal color)
3. The duration of each violation period in final statistics

You'll see **one error message per violation period**, not one per overlapping execution.
