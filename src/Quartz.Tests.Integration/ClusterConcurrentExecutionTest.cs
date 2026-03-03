#if NETCORE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using NUnit.Framework;

namespace Quartz.Tests.Integration;

/// <summary>
/// Integration test that spawns multiple cluster node processes to verify
/// that DisallowConcurrentExecution works correctly across cluster nodes.
/// </summary>
[TestFixture]
[Category("db-sqlserver")]
[Category("cluster")]
public class ClusterConcurrentExecutionTest
{
    private const int NodeCount = 2;
    private const int TestDurationSeconds = 30;
    private const int JobIntervalMs = 500;
    private const int JobDelayMs = 150;

    private List<Process> _clusterProcesses = [];
    private string _workerExecutablePath;
    private string _publishDirectory;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        // Publish worker executable to the temp directory
        _publishDirectory = Path.Combine(Path.GetTempPath(), "QuartzClusterTest", Guid.NewGuid().ToString("N"));
        var runtime = GetRuntimeIdentifier();

        var projectPath = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "Quartz.Tests.ClusterNode", "Quartz.Tests.ClusterNode.csproj"));

        await TestContext.Out.WriteLineAsync($"Publishing worker to: {_publishDirectory}");
        await TestContext.Out.WriteLineAsync($"Project path: {projectPath}");
        await TestContext.Out.WriteLineAsync($"Runtime: {runtime}");

        var publishArgs = $"publish \"{projectPath}\" -c Release -r {runtime} --self-contained -o \"{_publishDirectory}\"";

        using var publishProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = publishArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        publishProcess.Start();

        var outputTask = publishProcess.StandardOutput.ReadToEndAsync();
        var errorTask = publishProcess.StandardError.ReadToEndAsync();
        var exitTask = publishProcess.WaitForExitAsync();

        await Task.WhenAll(outputTask, errorTask, exitTask);

        var output = await outputTask;
        var error = await errorTask;

        if (publishProcess.ExitCode != 0)
        {
            await TestContext.Out.WriteLineAsync($"Publish output: {output}");
            await TestContext.Error.WriteLineAsync($"Publish error: {error}");
            Assert.Fail($"Worker publish failed with exit code {publishProcess.ExitCode}");
        }

        _workerExecutablePath = Path.Combine(_publishDirectory,
            runtime.StartsWith("win") ? "Quartz.Tests.ClusterNode.exe" : "Quartz.Tests.ClusterNode");

        if (!File.Exists(_workerExecutablePath))
        {
            Assert.Fail($"Worker executable not found at: {_workerExecutablePath}");
        }

        await TestContext.Out.WriteLineAsync($"Worker executable ready at: {_workerExecutablePath}");
    }

    [SetUp]
    public async Task SetUp()
    {
        // Clean results directory before test
        var resultsDir = Path.Combine(_publishDirectory, "results");
        if (Directory.Exists(resultsDir))
        {
            Directory.Delete(resultsDir, recursive: true);
        }

        _clusterProcesses.Clear();
        await Task.CompletedTask; // Keep async for consistency
    }

    [Test]
    [CancelAfter(90000)] // 90-second timeout (30s test + 60s buffer)
    public async Task TestDisallowConcurrentExecutionAcrossClusterNodes()
    {
        // Spawn cluster nodes
        for (var i = 1; i <= NodeCount; i++)
        {
            var nodeId = $"Node-{i}";
            var shouldScheduleJob = (i == 1); // Only the first node schedules the job

            _clusterProcesses.Add(SpawnWorkerProcess(nodeId, shouldScheduleJob));

            if (i < NodeCount)
            {
                await Task.Delay(500); // Stagger node startup
            }
        }

        // Wait for test duration
        await TestContext.Out.WriteLineAsync($"Waiting {TestDurationSeconds} seconds for test execution...");
        await Task.Delay(TimeSpan.FromSeconds(TestDurationSeconds));

        // Wait for processes to exit gracefully (they should exit after test duration)
        await TestContext.Out.WriteLineAsync("Waiting for worker processes to complete...");
        var processExitInfo = new List<(int ProcessId, int ExitCode, bool ExitedGracefully)>();

        foreach (var process in _clusterProcesses)
        {
            try
            {
                var timeout = TimeSpan.FromSeconds(10);
                var exitTask = process.WaitForExitAsync();
                if (await Task.WhenAny(exitTask, Task.Delay(timeout)) != exitTask)
                {
                    await TestContext.Out.WriteLineAsync($"Process {process.Id} did not exit gracefully within {timeout.TotalSeconds}s, forcing shutdown...");
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();  // Wait for kill to complete
                    }
                    processExitInfo.Add((process.Id, process.ExitCode, false));
                }
                else
                {
                    await TestContext.Out.WriteLineAsync($"Process {process.Id} exited gracefully with code {process.ExitCode}");
                    processExitInfo.Add((process.Id, process.ExitCode, true));
                }
            }
            catch (Exception ex)
            {
                await TestContext.Out.WriteLineAsync($"Error waiting for process {process.Id} exit: {ex.Message}");
            }
        }

        // Give a brief moment for final file writes (JSON export happens before process exit)
        await Task.Delay(1000);

        // Check if any processes crashed
        var failedProcesses = processExitInfo.Where(p => p.ExitCode != 0 && p.ExitCode != -1 && p.ExitCode != 137).ToList();
        if (failedProcesses.Any())
        {
            var failureInfo = string.Join(", ", failedProcesses.Select(p => $"PID {(object)p.ProcessId} (exit code {(object)p.ExitCode})"));
            await TestContext.Out.WriteLineAsync($"WARNING: Some processes exited with non-zero codes: {failureInfo}");
        }

        // Read and aggregate JSON results from all nodes
        var resultsDir = Path.Combine(_publishDirectory, "results");

        if (!Directory.Exists(resultsDir))
        {
            var diagnosticInfo = $"Results directory does not exist: {resultsDir}\n\n" +
                                 $"Process exit information:\n";
            foreach (var info in processExitInfo)
            {
                diagnosticInfo += $"  PID {(object)info.ProcessId}: ExitCode={(object)info.ExitCode}, Graceful={info.ExitedGracefully}\n";
            }
            diagnosticInfo += $"\nPublish directory: {_publishDirectory}\n";
            diagnosticInfo += $"Logs directory: {Path.Combine(_publishDirectory, "logs")}\n";

            // Check if logs directory exists and list log files
            var logsDir = Path.Combine(_publishDirectory, "logs");
            if (Directory.Exists(logsDir))
            {
                var logFiles = Directory.GetFiles(logsDir, "*.log");
                diagnosticInfo += $"Log files found: {(object)logFiles.Length}\n";
                foreach (var logFile in logFiles.Take(5))
                {
                    diagnosticInfo += $"  - {Path.GetFileName(logFile) ?? "unknown"}\n";
                }
            }
            else
            {
                diagnosticInfo += "Logs directory does not exist\n";
            }

            Assert.Fail(diagnosticInfo);
        }

        var resultFiles = Directory.GetFiles(resultsDir, "*-results.json");

        if (resultFiles.Length != NodeCount)
        {
            var diagnosticInfo = $"Expected {NodeCount} result files, found {resultFiles.Length}\n" +
                                 $"Results directory: {resultsDir}\n";
            if (resultFiles.Length > 0)
            {
                diagnosticInfo += "Files found:\n";
                foreach (var file in resultFiles)
                {
                    diagnosticInfo += $"  - {Path.GetFileName(file)}\n";
                }
            }
            Assert.Fail(diagnosticInfo);
        }

        var nodeResults = new List<NodeTestResults>();
        foreach (var file in resultFiles)
        {
            try
            {
                nodeResults.Add(TestResultsAggregator.ReadNodeResults(file));
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to read results from {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        var aggregated = TestResultsAggregator.AggregateResults(nodeResults);
        var statistics = TestResultsAggregator.FormatStatistics(aggregated);

        await TestContext.Out.WriteLineAsync(statistics);

        Assert.That(aggregated.TotalViolationPeriods, Is.EqualTo(0),
            $"Concurrent execution violations detected across cluster nodes. {statistics}");

        // Verify all processes exited (exit code 0 for success, -1 or 137 for killed)
        foreach (var process in _clusterProcesses)
        {
            await TestContext.Out.WriteLineAsync($"Process exit code: {process.ExitCode}");
            // Exit code -1 (killed on Windows), 137 (killed on Linux), or 0 (graceful) are acceptable
            Assert.That(process.ExitCode, Is.EqualTo(0).Or.EqualTo(-1).Or.EqualTo(137),
                $"Worker process exited with unexpected code {process.ExitCode}");
        }
    }

    private Process SpawnWorkerProcess(string nodeId, bool shouldScheduleJob)
    {
        // Build System.CommandLine format arguments
        var args = $"node {nodeId} --duration {TestDurationSeconds} --job-interval {JobIntervalMs} --job-delay {JobDelayMs} --database-provider SqlServer";
        if (shouldScheduleJob)
        {
            args += " --schedule --init";  // Add --init flag for the first node
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _workerExecutablePath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _publishDirectory,
            EnvironmentVariables =
            {
                // Pass connection string via environment variable - nodes still need it for clustering
                ["ConnectionString"] = TestConstants.SqlServerConnectionString,
                ["InitialScheduleDelaySeconds"] = "3"
            }
        };

        var process = Process.Start(startInfo);

        if (process == null)
        {
            throw new InvalidOperationException($"Failed to start worker process for {nodeId}");
        }

        // Capture output for debugging
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) TestContext.Out.WriteLine($"[{nodeId}] {e.Data}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) TestContext.Error.WriteLine($"[{nodeId}] ERROR: {e.Data}");
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        TestContext.Out.WriteLine($"Started worker process: {nodeId} (PID: {process.Id})");

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
        foreach (var process in _clusterProcesses)
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

        _clusterProcesses.Clear();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        // Clean up the published directory
        if (!string.IsNullOrEmpty(_publishDirectory) && Directory.Exists(_publishDirectory))
        {
            try
            {
                Directory.Delete(_publishDirectory, recursive: true);
                TestContext.Out.WriteLine($"Cleaned up publish directory: {_publishDirectory}");
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine($"Warning: Could not delete publish directory: {ex.Message}");
            }
        }
    }
}
#endif