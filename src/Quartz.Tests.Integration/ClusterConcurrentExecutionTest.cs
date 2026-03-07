#if NETCORE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

namespace Quartz.Tests.Integration;

/// <summary>
/// Integration test that spawns multiple cluster node processes to verify
/// that DisallowConcurrentExecution works correctly across cluster nodes.
/// </summary>
[TestFixture]
[Category("cluster")]
public class ClusterConcurrentExecutionTest
{
    /// <summary>
    /// Arguments for spawning a cluster worker process.
    /// </summary>
    private record WorkerArgs(
        string NodeId,
        bool ShouldScheduleJob,
        string DatabaseProvider,
        string ConnectionString,
        int TestDurationSeconds,
        int JobIntervalMs,
        int JobDelayMs);

    private const int NodeCount = 2;
    private const int TestDurationSeconds = 30;
    private const int TestTimeoutBufferSeconds = 60;
    private const int TestTimeoutMs = (TestDurationSeconds + TestTimeoutBufferSeconds) * 1000;
    private const int JobIntervalMs = 500;
    private const int JobDelayMs = 150;
    private const int NodeStartupDelayMs = 500;
    private const int ProcessExitTimeoutSeconds = 10;
    private const int FileWriteGracePeriodMs = 1000;
    private const string InitialScheduleDelayEnvVar = "InitialScheduleDelaySeconds";
    private const string InitialScheduleDelayValue = "3";

    private const int GracefulExitCode = 0;
    private const int WindowsKilledExitCode = -1;
    private const int LinuxKilledExitCode = 137;
    private static readonly HashSet<int> acceptableExitCodes = [GracefulExitCode, WindowsKilledExitCode, LinuxKilledExitCode];

    private readonly List<Process> clusterProcesses = [];
    private string workerExecutablePath;
    private string publishDirectory;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var runtime = GetRuntimeIdentifier();
        publishDirectory = Path.Combine(Path.GetTempPath(), "QuartzClusterTest", runtime);
        workerExecutablePath = await PublishWorkerAsync(publishDirectory);
    }

    private static async Task<string> PublishWorkerAsync(string outputDirectory)
    {
        var runtime = GetRuntimeIdentifier();

        var projectPath = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "Quartz.Tests.ClusterNode", "Quartz.Tests.ClusterNode.csproj"));

        await TestContext.Out.WriteLineAsync($"Publishing worker to: {outputDirectory}");
        await TestContext.Out.WriteLineAsync($"Project path: {projectPath}");
        await TestContext.Out.WriteLineAsync($"Runtime: {runtime}");

        var publishArgs = $"publish \"{projectPath}\" -c Release -r {runtime} --self-contained -o \"{outputDirectory}\"";

        using var publishProcess = new Process();
        publishProcess.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = publishArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        publishProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) outputBuilder.AppendLine(e.Data);
        };
        publishProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) errorBuilder.AppendLine(e.Data);
        };

        publishProcess.Start();
        publishProcess.BeginOutputReadLine();
        publishProcess.BeginErrorReadLine();

        await publishProcess.WaitForExitAsync();

        if (publishProcess.ExitCode != 0)
        {
            await TestContext.Out.WriteLineAsync($"Publish output: {outputBuilder}");
            await TestContext.Error.WriteLineAsync($"Publish error: {errorBuilder}");
            Assert.Fail($"Worker publish failed with exit code {publishProcess.ExitCode}");
        }

        var executablePath = Path.Combine(outputDirectory,
            runtime.StartsWith("win") ? "Quartz.Tests.ClusterNode.exe" : "Quartz.Tests.ClusterNode");

        if (!File.Exists(executablePath))
        {
            Assert.Fail($"Worker executable not found at: {executablePath}");
        }

        await TestContext.Out.WriteLineAsync($"Worker executable ready at: {executablePath}");
        return executablePath;
    }

    [SetUp]
    public void SetUp()
    {
        ClearResultsDirectory();
        clusterProcesses.Clear();
    }

    private void ClearResultsDirectory()
    {
        var resultsDir = Path.Combine(publishDirectory, "results");
        if (Directory.Exists(resultsDir))
        {
            Directory.Delete(resultsDir, recursive: true);
        }
    }

    [Test]
    [TestCase("SqlServer", Category = "db-sqlserver")]
    [TestCase("PostgreSQL", Category = "db-postgres")]
    [TestCase("MySQL", Category = "db-mysql")]
    [CancelAfter(TestTimeoutMs)]
    public async Task TestDisallowConcurrentExecutionAcrossClusterNodes(string provider)
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var connectionString = GetConnectionString(provider);

        // Spawn cluster nodes
        for (var i = 1; i <= NodeCount; i++)
        {
            var shouldScheduleJob = i == 1; // Only the first node schedules the job

            var workerArgs = new WorkerArgs(
                NodeId: $"Node-{i}",
                ShouldScheduleJob: shouldScheduleJob,
                DatabaseProvider: provider,
                ConnectionString: connectionString,
                TestDurationSeconds: TestDurationSeconds,
                JobIntervalMs: JobIntervalMs,
                JobDelayMs: JobDelayMs);

            clusterProcesses.Add(SpawnWorkerProcess(workerArgs));

            if (i < NodeCount)
            {
                await Task.Delay(NodeStartupDelayMs, cancellationToken); // Stagger node startup
            }
        }

        // Wait for test duration
        await TestContext.Out.WriteLineAsync($"Waiting {TestDurationSeconds} seconds for test execution...");
        await Task.Delay(TimeSpan.FromSeconds(TestDurationSeconds), cancellationToken);

        var processExitInfo = await WaitForProcessesAsync(cancellationToken);

        // Give a brief moment for final file writes (JSON export happens before process exit)
        await Task.Delay(FileWriteGracePeriodMs, cancellationToken);

        LogCrashedProcesses(processExitInfo);

        var resultsDir = Path.Combine(publishDirectory, "results");
        AssertResultsDirectoryExists(resultsDir, processExitInfo);

        var resultFiles = Directory.GetFiles(resultsDir, "*-results.json");
        AssertExpectedResultFiles(resultFiles, resultsDir);

        var emptyFiles = resultFiles.Where(f => new FileInfo(f).Length == 0).ToArray();
        if (emptyFiles.Length > 0)
        {
            Assert.Fail($"Empty result files detected (truncated write?): {string.Join(", ", emptyFiles.Select(Path.GetFileName))}");
        }

        await AggregateAndAssertResultsAsync(resultFiles);
        await AssertAllProcessesExitedCleanlyAsync();
    }

    private async Task<List<(int ProcessId, int ExitCode, bool ExitedGracefully)>> WaitForProcessesAsync(CancellationToken cancellationToken = default)
    {
        await TestContext.Out.WriteLineAsync("Waiting for worker processes to complete...");
        var processExitInfo = new List<(int ProcessId, int ExitCode, bool ExitedGracefully)>();

        foreach (var process in clusterProcesses)
        {
            try
            {
                var timeout = TimeSpan.FromSeconds(ProcessExitTimeoutSeconds);
                var exitTask = process.WaitForExitAsync(cancellationToken);
                if (await Task.WhenAny(exitTask, Task.Delay(timeout, cancellationToken)) != exitTask)
                {
                    await TestContext.Out.WriteLineAsync($"Process {process.Id} did not exit gracefully within {timeout.TotalSeconds}s, forcing shutdown...");
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(cancellationToken);
                    }

                    processExitInfo.Add((process.Id, process.ExitCode, false));
                }
                else
                {
                    await TestContext.Out.WriteLineAsync($"Process {process.Id} exited gracefully with code {process.ExitCode}");
                    processExitInfo.Add((process.Id, process.ExitCode, true));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await TestContext.Out.WriteLineAsync($"Error waiting for process {process.Id} exit: {ex}");
                processExitInfo.Add((process.Id, -1, false));
            }
        }

        return processExitInfo;
    }

    private static void LogCrashedProcesses(List<(int ProcessId, int ExitCode, bool ExitedGracefully)> processExitInfo)
    {
        var failedProcesses = processExitInfo.Where(p => !acceptableExitCodes.Contains(p.ExitCode)).ToList();
        if (failedProcesses.Any())
        {
            var failureInfo = string.Join(", ", failedProcesses.Select(p => $"PID {p.ProcessId} (exit code {p.ExitCode})"));
            TestContext.Out.WriteLine($"WARNING: Some processes exited with non-zero codes: {failureInfo}");
        }
    }

    private void AssertResultsDirectoryExists(string resultsDir, List<(int ProcessId, int ExitCode, bool ExitedGracefully)> processExitInfo)
    {
        if (Directory.Exists(resultsDir))
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Results directory does not exist: {resultsDir}");
        sb.AppendLine();
        sb.AppendLine("Process exit information:");
        foreach (var info in processExitInfo)
        {
            sb.AppendLine($"  PID {info.ProcessId}: ExitCode={info.ExitCode}, Graceful={info.ExitedGracefully}");
        }

        sb.AppendLine($"\nPublish directory: {publishDirectory}");

        var logsDir = Path.Combine(publishDirectory, "logs");
        sb.AppendLine($"Logs directory: {logsDir}");

        if (Directory.Exists(logsDir))
        {
            var logFiles = Directory.GetFiles(logsDir, "*.log");
            sb.AppendLine($"Log files found: {logFiles.Length}");
            foreach (var logFile in logFiles.Take(5))
            {
                sb.AppendLine($"  - {Path.GetFileName(logFile) ?? "unknown"}");
            }
        }
        else
        {
            sb.AppendLine("Logs directory does not exist");
        }

        Assert.Fail(sb.ToString());
    }

    private static void AssertExpectedResultFiles(string[] resultFiles, string resultsDir)
    {
        if (resultFiles.Length == NodeCount)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Expected {NodeCount} result files, found {resultFiles.Length}");
        sb.AppendLine($"Results directory: {resultsDir}");
        if (resultFiles.Length > 0)
        {
            sb.AppendLine("Files found:");
            foreach (var file in resultFiles)
            {
                sb.AppendLine($"  - {Path.GetFileName(file)}");
            }
        }

        Assert.Fail(sb.ToString());
    }

    private async Task AggregateAndAssertResultsAsync(string[] resultFiles)
    {
        var nodeResults = new List<NodeTestResults>();
        foreach (var file in resultFiles)
        {
            try
            {
                nodeResults.Add(TestResultsAggregator.ReadNodeResults(file));
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to read results from {Path.GetFileName(file)}: {ex}");
            }
        }

        var aggregated = TestResultsAggregator.AggregateResults(nodeResults);
        var statistics = TestResultsAggregator.FormatStatistics(aggregated);

        await TestContext.Out.WriteLineAsync(statistics);

        Assert.That(aggregated.TotalViolationPeriods, Is.EqualTo(0),
            $"Concurrent execution violations detected across cluster nodes. {statistics}");
    }

    private async Task AssertAllProcessesExitedCleanlyAsync()
    {
        foreach (var process in clusterProcesses)
        {
            await TestContext.Out.WriteLineAsync($"Process exit code: {process.ExitCode}");
            Assert.That(acceptableExitCodes.Contains(process.ExitCode), Is.True,
                $"Worker process exited with unexpected code {process.ExitCode}");
        }
    }

    private static string GetConnectionString(string provider)
    {
        return provider switch
        {
            "SqlServer" => TestConstants.SqlServerConnectionString,
            "PostgreSQL" => TestConstants.PostgresConnectionString,
            "MySQL" => TestConstants.MySqlConnectionString,
            _ => throw new ArgumentException($"Unknown provider: {provider}")
        };
    }

    private ProcessStartInfo BuildWorkerStartInfo(WorkerArgs workerArgs)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = workerExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = publishDirectory,
            EnvironmentVariables =
            {
                [InitialScheduleDelayEnvVar] = InitialScheduleDelayValue
            }
        };

        startInfo.ArgumentList.Add("node");
        startInfo.ArgumentList.Add(workerArgs.NodeId);
        startInfo.ArgumentList.Add("--duration");
        startInfo.ArgumentList.Add(workerArgs.TestDurationSeconds.ToString());
        startInfo.ArgumentList.Add("--job-interval");
        startInfo.ArgumentList.Add(workerArgs.JobIntervalMs.ToString());
        startInfo.ArgumentList.Add("--job-delay");
        startInfo.ArgumentList.Add(workerArgs.JobDelayMs.ToString());
        startInfo.ArgumentList.Add("--database-provider");
        startInfo.ArgumentList.Add(workerArgs.DatabaseProvider);
        startInfo.ArgumentList.Add("--connection-string");
        startInfo.ArgumentList.Add(workerArgs.ConnectionString);

        if (workerArgs.ShouldScheduleJob)
        {
            startInfo.ArgumentList.Add("--schedule");
            startInfo.ArgumentList.Add("--init");
        }

        return startInfo;
    }

    private Process SpawnWorkerProcess(WorkerArgs workerArgs)
    {
        var startInfo = BuildWorkerStartInfo(workerArgs);
        var process = Process.Start(startInfo);

        if (process == null)
        {
            throw new InvalidOperationException($"Failed to start worker process for {workerArgs.NodeId}");
        }

        // Capture output for debugging
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) TestContext.Out.WriteLine($"[{workerArgs.NodeId}] {e.Data}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) TestContext.Error.WriteLine($"[{workerArgs.NodeId}] ERROR: {e.Data}");
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        TestContext.Out.WriteLine($"Started worker process: {workerArgs.NodeId} (PID: {process.Id})");

        return process;
    }

    private static string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "win-x64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "linux-x64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Check if ARM64
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                return "osx-arm64";
            return "osx-x64";
        }

        throw new PlatformNotSupportedException();
    }

    [TearDown]
    public void TearDown()
    {
        // Force kill any remaining processes
        foreach (var process in clusterProcesses)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                process.Dispose();
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine($"Error cleaning up process: {ex.Message}");
            }
        }

        clusterProcesses.Clear();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        // Publish directory is reused across runs as a build cache — only clean results
        ClearResultsDirectory();
    }
}
#endif