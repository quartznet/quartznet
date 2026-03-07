using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Logging;

using Serilog;
using Serilog.Events;

namespace Quartz.Tests.ClusterNode;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Quartz.NET Cluster Node Test - Tests concurrent execution in clustered Quartz.NET environments.");

        rootCommand.Subcommands.Add(CreateCleanupCommand());
        rootCommand.Subcommands.Add(CreateOrchestratorCommand());
        rootCommand.Subcommands.Add(CreateNodeCommand());

        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync();
    }

    private static Command CreateCleanupCommand()
    {
        var cleanupCommand = new Command("cleanup", "Clean up previous test data from the database");

        var databaseProviderOption = new Option<string>("--database-provider")
        {
            Description = "Database provider to use (SqlServer, Npgsql, MySql)",
            DefaultValueFactory = _ => "SqlServer"
        };
        var connectionStringOption = new Option<string>("--connection-string")
        {
            Description = "Connection string to use",
            Required = true
        };

        cleanupCommand.Options.Add(databaseProviderOption);
        cleanupCommand.Options.Add(connectionStringOption);

        cleanupCommand.SetAction(async (parseResult, _) => await RunCleanup(
            parseResult.GetValue(databaseProviderOption)!,
            parseResult.GetValue(connectionStringOption)!));
        return cleanupCommand;
    }

    private static Command CreateOrchestratorCommand()
    {
        var orchestratorCommand = new Command("orchestrator", "Launch multiple cluster nodes in separate windows");

        var nodeCountOption = new Option<int>("--node-count", "-n")
        {
            Description = "Number of cluster nodes to start",
            DefaultValueFactory = _ => 2
        };
        var durationOption = new Option<int>("--duration", "-d")
        {
            Description = "Test duration in seconds",
            DefaultValueFactory = _ => 30
        };
        var jobIntervalOption = new Option<int>("--job-interval", "-i")
        {
            Description = "Job trigger interval in milliseconds",
            DefaultValueFactory = _ => 500
        };
        var jobDelayOption = new Option<int>("--job-delay", "-j")
        {
            Description = "Job execution delay in milliseconds",
            DefaultValueFactory = _ => 100
        };
        var noPauseOption = new Option<bool>("--no-pause")
        {
            Description = "Do not pause after node completion (auto-close windows)",
            DefaultValueFactory = _ => false
        };
        var databaseProviderOption = new Option<string>("--database-provider")
        {
            Description = "Database provider to use (SqlServer, Npgsql, MySql)",
            DefaultValueFactory = _ => "SqlServer"
        };
        var connectionStringOption = new Option<string>("--connection-string")
        {
            Description = "Connection string to use",
            Required = true
        };

        orchestratorCommand.Options.Add(nodeCountOption);
        orchestratorCommand.Options.Add(durationOption);
        orchestratorCommand.Options.Add(jobIntervalOption);
        orchestratorCommand.Options.Add(jobDelayOption);
        orchestratorCommand.Options.Add(noPauseOption);
        orchestratorCommand.Options.Add(databaseProviderOption);
        orchestratorCommand.Options.Add(connectionStringOption);

        orchestratorCommand.SetAction(async (parseResult, _) =>
        {
            var args = new OrchestratorArgs(
                parseResult.GetValue(nodeCountOption),
                parseResult.GetValue(durationOption),
                parseResult.GetValue(jobIntervalOption),
                parseResult.GetValue(jobDelayOption),
                parseResult.GetValue(noPauseOption),
                parseResult.GetValue(databaseProviderOption)!,
                parseResult.GetValue(connectionStringOption)!
            );
            return await RunOrchestrator(args);
        });

        return orchestratorCommand;
    }

    private static Command CreateNodeCommand()
    {
        var nodeCommand = new Command("node", "Run as a single cluster node");

        var nodeIdArgument = new Argument<string>("node-id")
        {
            Description = "Node identifier (e.g., Node-1)"
        };
        var durationOption = new Option<int>("--duration", "-d")
        {
            Description = "Test duration in seconds",
            DefaultValueFactory = _ => 120
        };
        var jobIntervalOption = new Option<int>("--job-interval", "-i")
        {
            Description = "Job trigger interval in milliseconds",
            DefaultValueFactory = _ => 500
        };
        var jobDelayOption = new Option<int>("--job-delay", "-j")
        {
            Description = "Job execution delay in milliseconds",
            DefaultValueFactory = _ => 100
        };
        var scheduleOption = new Option<bool>("--schedule")
        {
            Description = "This node should schedule the job",
            DefaultValueFactory = _ => false
        };
        var initOption = new Option<bool>("--init")
        {
            Description = "Initialize test database tables (run on first node only)",
            DefaultValueFactory = _ => false
        };
        var databaseProviderOption = new Option<string>("--database-provider")
        {
            Description = "Database provider to use (SqlServer, Npgsql, MySql)",
            DefaultValueFactory = _ => "SqlServer"
        };
        var connectionStringOption = new Option<string>("--connection-string")
        {
            Description = "Connection string to use",
            Required = true
        };

        nodeCommand.Arguments.Add(nodeIdArgument);
        nodeCommand.Options.Add(durationOption);
        nodeCommand.Options.Add(jobIntervalOption);
        nodeCommand.Options.Add(jobDelayOption);
        nodeCommand.Options.Add(scheduleOption);
        nodeCommand.Options.Add(initOption);
        nodeCommand.Options.Add(databaseProviderOption);
        nodeCommand.Options.Add(connectionStringOption);

        nodeCommand.SetAction(async (parseResult, _) =>
        {
            var args = new CommandLineArgs(
                parseResult.GetValue(nodeIdArgument)!,
                parseResult.GetValue(durationOption),
                parseResult.GetValue(jobIntervalOption),
                parseResult.GetValue(jobDelayOption),
                parseResult.GetValue(scheduleOption),
                parseResult.GetValue(initOption),
                parseResult.GetValue(databaseProviderOption)!,
                parseResult.GetValue(connectionStringOption)!
            );
            return await RunSingleNode(args);
        });

        return nodeCommand;
    }

    private static async Task<int> RunSingleNode(CommandLineArgs commandLineArgs)
    {
        // Load configuration from appsettings.json and environment variables
        var config = LoadConfiguration();

        var databaseProvider = commandLineArgs.DatabaseProvider;
        var connectionString = commandLineArgs.ConnectionString;

        // Create database provider
        var provider = DatabaseProviderFactory.GetProvider(databaseProvider);

        // Create the logs directory if it doesn't exist
        var logsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        Directory.CreateDirectory(logsDirectory);

        // Configure Serilog with per-node file logging
        var loggerSetup = SetupNodeLogger(logsDirectory, commandLineArgs.NodeId);

        using (loggerSetup.LoggerFactory) // Ensure disposal
        {
        loggerSetup.Logger.LogInformation("QUARTZ.NET CLUSTER NODE TEST - {NodeId}", commandLineArgs.NodeId);
        loggerSetup.Logger.LogInformation("Process ID: {ProcessId}", Environment.ProcessId);
        loggerSetup.Logger.LogInformation("Node ID: {NodeId}", commandLineArgs.NodeId);
        loggerSetup.Logger.LogInformation("Database Provider: {Provider}", databaseProvider);
        if (commandLineArgs.DatabaseProvider != null)
        {
            loggerSetup.Logger.LogInformation("  (Overridden via --database-provider command line option)");
        }
        loggerSetup.Logger.LogInformation("Duration: {Duration}s | Job Interval: {Interval}ms | Job Delay: {Delay}ms",
            commandLineArgs.DurationSeconds, commandLineArgs.JobIntervalMs, commandLineArgs.JobDelayMs);
        loggerSetup.Logger.LogInformation("Will schedule job: {ShouldSchedule}", commandLineArgs.ShouldScheduleJob);
        loggerSetup.Logger.LogInformation("Log file: {LogFile}", loggerSetup.LogFileName);

        // Verify database connectivity and initialize test tables
        loggerSetup.Logger.LogInformation("Verifying database connection...");
        try
        {
            await using var connection = provider.CreateConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            loggerSetup.Logger.LogInformation("✓ Database connection successful");
        }
        catch (Exception ex)
        {
            loggerSetup.Logger.LogError(ex, "✗ Database connection failed");
            return 1;
        }

        // Initialize database tables for concurrent execution tracking
        if (commandLineArgs.ShouldInitialize)
        {
            loggerSetup.Logger.LogInformation("Initializing test tables...");
            try
            {
                await ClusterTestHelper.InitializeDatabase(connectionString, provider, loggerSetup.LoggerFactory).ConfigureAwait(false);
                loggerSetup.Logger.LogInformation("✓ Test tables initialized");
            }
            catch (Exception ex)
            {
                loggerSetup.Logger.LogError(ex, "✗ Failed to initialize test tables");
                return 1;
            }
        }

        // Configure DI container
        var services = new ServiceCollection();

        // Add logging with Serilog
        services.AddSingleton(loggerSetup.LoggerFactory);
        services.AddLogging(builder => builder.AddSerilog(Log.Logger));

        // Configure Quartz with DI
        services.AddQuartz(q =>
        {
            q.SchedulerId = commandLineArgs.NodeId;
            q.SchedulerName = "ClusterTestScheduler";

            const string jobName = "TestJob";
            const string groupName = "ClusterTest";

            // Register the job with DI
            q.AddJob<ConcurrentExecutionTestJob>(opts => opts
                .WithIdentity(jobName, groupName)
                .StoreDurably()
                .UsingJobData("jobDelayMs", commandLineArgs.JobDelayMs.ToString(CultureInfo.InvariantCulture))
                .UsingJobData("connectionString", connectionString)
                .UsingJobData("databaseProvider", databaseProvider));

            // Only schedule the trigger if this node should schedule
            if (commandLineArgs.ShouldScheduleJob)
            {
                q.AddTrigger(opts => opts
                    .ForJob(jobName, groupName)
                    .WithIdentity("TestTrigger1", groupName)
                    .StartAt(DateBuilder.FutureDate(1, IntervalUnit.Second))
                    .WithSimpleSchedule(x => x
                        .WithInterval(TimeSpan.FromMilliseconds(commandLineArgs.JobIntervalMs))
                        .RepeatForever()));

                q.AddTrigger(opts => opts
                    .ForJob(jobName, groupName)
                    .WithIdentity("TestTrigger2", groupName)
                    .StartAt(DateBuilder.FutureDate(2, IntervalUnit.Second))
                    .WithSimpleSchedule(x => x
                        .WithInterval(TimeSpan.FromMilliseconds(commandLineArgs.JobIntervalMs))
                        .RepeatForever()));
            }

            // Configure a persistent store with clustering
            q.UsePersistentStore(store =>
            {
                store.UseProperties = true;
                store.UseSystemTextJsonSerializer();

                // Use the appropriate database provider
                if (databaseProvider == "SqlServer")
                {
                    store.UseSqlServer(connectionString);
                }
                else if (databaseProvider == "PostgreSQL")
                {
                    store.UsePostgres(connectionString);
                }
                else if (databaseProvider == "MySQL")
                {
                    store.UseMySqlConnector(c => c.ConnectionString = connectionString);
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported database provider: {databaseProvider}");
                }

                store.UseClustering(c =>
                {
                    c.CheckinInterval = TimeSpan.FromSeconds(1); // Aggressive checkin
                    c.CheckinMisfireThreshold = TimeSpan.FromSeconds(3);
                });
            });

            q.UseDefaultThreadPool(x => x.MaxConcurrency = 10);
        });

        // Build the service provider
        await using var serviceProvider = services.BuildServiceProvider();

        // Get the scheduler factory from DI
        var schedulerFactory = serviceProvider.GetRequiredService<ISchedulerFactory>();

        IScheduler? scheduler = null;
        using var cancellationTokenSource = new CancellationTokenSource();

        loggerSetup.Logger.LogInformation("Starting cluster node: {NodeId}", commandLineArgs.NodeId);

        try
        {
            // Get scheduler from the factory
            scheduler = await schedulerFactory.GetScheduler(cancellationTokenSource.Token).ConfigureAwait(false);

            await scheduler.Start(cancellationTokenSource.Token).ConfigureAwait(false);

            loggerSetup.Logger.LogInformation("✓ Node started: {NodeId}", commandLineArgs.NodeId);

            // Only the first instance (or when explicitly requested) schedules the job
            if (commandLineArgs.ShouldScheduleJob)
            {
                loggerSetup.Logger.LogInformation("Waiting {Delay}s before scheduling so other nodes can join...",
                    config.InitialScheduleDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(config.InitialScheduleDelaySeconds), cancellationTokenSource.Token).ConfigureAwait(false);
                loggerSetup.Logger.LogInformation("✓ Job and trigger configured via DI");
            }

            loggerSetup.Logger.LogInformation("MONITORING FOR VIOLATIONS - Node {NodeId} - Press Ctrl+C to stop", commandLineArgs.NodeId);

            // Set up Ctrl+C handler
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cancellationTokenSource.Cancel();
                loggerSetup.Logger.LogInformation("Interrupt received, shutting down...");
            };

            // Run for a specified duration
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(commandLineArgs.DurationSeconds), cancellationTokenSource.Token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // Normal cancellation
            }

            loggerSetup.Logger.LogInformation("Test duration completed. Shutting down...");
        }
        catch (Exception ex)
        {
            loggerSetup.Logger.LogError(ex, "ERROR during node execution");
            return 1;
        }
        finally
        {
            if (scheduler != null)
            {
                try
                {
                    await scheduler.Shutdown(waitForJobsToComplete: false, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                    loggerSetup.Logger.LogInformation("✓ {NodeId} shutdown complete", commandLineArgs.NodeId);
                }
                catch (Exception ex)
                {
                    loggerSetup.Logger.LogError(ex, "✗ Error shutting down {NodeId}", commandLineArgs.NodeId);
                }
            }

            // Print final statistics from the database
            try
            {
                var statistics = await ClusterTestHelper.GetFinalStatistics(connectionString, provider, loggerSetup.LoggerFactory).ConfigureAwait(false);
                loggerSetup.Logger.LogInformation("{Statistics}", statistics);
            }
            catch (Exception ex)
            {
                loggerSetup.Logger.LogError(ex, "Error retrieving final statistics");
            }

            // Export results to JSON
            try
            {
                var resultsDir = Path.Combine(Directory.GetCurrentDirectory(), "results");
                Directory.CreateDirectory(resultsDir);

                var outputPath = Path.Combine(resultsDir, $"{commandLineArgs.NodeId}-results.json");
                await TestResultsExporter.ExportTestResultsAsync(
                    connectionString, provider, commandLineArgs.NodeId,
                    outputPath, loggerSetup.LoggerFactory).ConfigureAwait(false);

                loggerSetup.Logger.LogInformation("✓ Test results exported to {Path}", outputPath);
            }
            catch (Exception ex)
            {
                loggerSetup.Logger.LogError(ex, "✗ Failed to export test results");
            }

            loggerSetup.Logger.LogInformation("{NodeId} exited.", commandLineArgs.NodeId);
            await Log.CloseAndFlushAsync();
        }

        return 0;
        } // End using loggerFactory
    }

    private static async Task<int> RunOrchestrator(OrchestratorArgs orchestratorArgs)
    {
        // Create the logs directory if it doesn't exist
        var logsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        Directory.CreateDirectory(logsDirectory);

        // Setup orchestrator logging
        var loggerSetup = SetupOrchestratorLogger(logsDirectory);

        using (loggerSetup.LoggerFactory) // Ensure disposal
        {
            // Display test header
            Console.WriteLine();
        Console.WriteLine("Quartz.NET Cluster Concurrent Execution Test Orchestrator");
        Console.WriteLine();
        Console.WriteLine("Configuration:");
        Console.WriteLine($"  Node Count:      {orchestratorArgs.NodeCount}");
        Console.WriteLine($"  Duration:        {orchestratorArgs.DurationSeconds} seconds");
        Console.WriteLine($"  Job Interval:    {orchestratorArgs.JobIntervalMs} ms");
        Console.WriteLine($"  Job Delay:       {orchestratorArgs.JobDelayMs} ms");
        Console.WriteLine();
        Console.WriteLine($"Orchestrator log: {loggerSetup.LogFileName}");
        Console.WriteLine();

        loggerSetup.Logger.LogInformation("Orchestrator started with {NodeCount} nodes", orchestratorArgs.NodeCount);

        // Run cleanup first
        Console.WriteLine("Cleaning up previous test data...");
        await RunCleanup(orchestratorArgs.DatabaseProvider, orchestratorArgs.ConnectionString);
        Console.WriteLine();

        // Get the current executable path
        var exePath = Environment.ProcessPath
                      ?? throw new InvalidOperationException("Could not determine current executable path");

        loggerSetup.Logger.LogInformation("Using executable: {ExePath}", exePath);

        // Spawn all nodes
        Console.WriteLine($"Starting {orchestratorArgs.NodeCount} cluster nodes...");
        Console.WriteLine("Each node will run in a separate window.");
        Console.WriteLine();

        var processes = new List<Process>();
        for (int i = 1; i <= orchestratorArgs.NodeCount; i++)
        {
            var nodeId = $"Node-{i}";
            var shouldScheduleJob = i == 1; // Only the first node schedules the job

            // Build System.CommandLine format arguments
            var arguments = $"node {nodeId} --duration {orchestratorArgs.DurationSeconds} --job-interval {orchestratorArgs.JobIntervalMs} --job-delay {orchestratorArgs.JobDelayMs} --database-provider {orchestratorArgs.DatabaseProvider} --connection-string \"{orchestratorArgs.ConnectionString}\"";
            if (shouldScheduleJob)
            {
                arguments += " --schedule --init";  // First node schedules and initializes
            }

            try
            {
                var process = StartNodeProcess(exePath, arguments, orchestratorArgs.NoPause);
                if (process != null)
                {
                    processes.Add(process);
                    Console.WriteLine($"  Starting {nodeId}...");
                    loggerSetup.Logger.LogInformation("Started {NodeId} (PID: {ProcessId})", nodeId, process.Id);
                }
                else
                {
                    Console.WriteLine($"  Warning: failed to start {nodeId}");
                    loggerSetup.Logger.LogWarning("Failed to start {NodeId}", nodeId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: failed to start {nodeId}: {ex.Message}");
                loggerSetup.Logger.LogWarning(ex, "Failed to start {NodeId}", nodeId);
            }

            // Delay between spawns
            await Task.Delay(500);
        }

        Console.WriteLine();
        Console.WriteLine($"[OK] All {processes.Count} nodes started");
        Console.WriteLine();

        if (processes.Count == 0)
        {
            Console.WriteLine("[ERROR] No nodes could be started");
            return 1;
        }

        // Monitor nodes
        await MonitorNodeProcesses(processes, orchestratorArgs.DurationSeconds, loggerSetup.Logger);

        // Display final statistics
        Console.WriteLine();
        Console.WriteLine("Retrieving final statistics...");
        try
        {
            var provider = DatabaseProviderFactory.GetProvider(orchestratorArgs.DatabaseProvider);
            var statistics = await ClusterTestHelper.GetFinalStatistics(orchestratorArgs.ConnectionString, provider, loggerSetup.LoggerFactory);
            Console.WriteLine(statistics);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Could not retrieve statistics: {ex.Message}");
            loggerSetup.Logger.LogWarning(ex, "Failed to retrieve statistics");
        }

            Console.WriteLine();
            Console.WriteLine("Test complete!");
            Console.WriteLine();

            await Log.CloseAndFlushAsync();
            return 0;
        } // End using loggerFactory
    }

    private static async Task MonitorNodeProcesses(List<Process> processes, int durationSeconds, Microsoft.Extensions.Logging.ILogger logger)
    {
        Console.WriteLine("Monitoring cluster test...");
        Console.WriteLine("  - Watch the separate windows for execution logs");
        Console.WriteLine("  - RED text indicates concurrent execution violations");
        Console.WriteLine($"  - Test will run for {durationSeconds} seconds");
        Console.WriteLine();
        Console.WriteLine("Press Ctrl+C to stop all nodes early...");
        Console.WriteLine();

        var cancellationTokenSource = new CancellationTokenSource();
        var interrupted = false;

        // Setup Ctrl+C handler
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            interrupted = true;
            cancellationTokenSource.Cancel();
            Console.WriteLine();
            Console.WriteLine("[WARN] Interrupted - stopping all nodes...");
            logger.LogInformation("Interrupt received, stopping all nodes");
        };

        try
        {
            // Poll for process completion
            while (true)
            {
                // Check if cancelled
                if (cancellationTokenSource.Token.IsCancellationRequested)
                {
                    break;
                }

                // Refresh process list and check if any are still running
                var activeProcesses = processes.Where(p =>
                {
                    try
                    {
                        return !p.HasExited;
                    }
                    catch
                    {
                        return false;
                    }
                }).ToList();

                if (activeProcesses.Count == 0)
                {
                    // All processes completed
                    break;
                }

                await Task.Delay(1000, cancellationTokenSource.Token);
            }
        }
        catch (TaskCanceledException)
        {
            // Normal cancellation
        }

        // Stop all remaining processes
        if (interrupted)
        {
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        logger.LogInformation("Stopping process {ProcessId}", process.Id);
                        process.Kill(entireProcessTree: true);

                        // Wait up to 5 seconds for a graceful shutdown
                        process.WaitForExit(5000);

                        if (!process.HasExited)
                        {
                            logger.LogWarning("Force killing process {ProcessId}", process.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error stopping process {ProcessId}", process.Id);
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("[OK] All nodes completed");
        Console.WriteLine();
    }

    private static Process? StartNodeProcess(string exePath, string arguments, bool noPause = false)
    {
        var launcher = ProcessLauncherFactory.GetLauncher();
        return launcher.LaunchInNewWindow(exePath, arguments, noPause);
    }

    private static async Task<int> RunCleanup(string databaseProvider, string connectionString)
    {
        Console.WriteLine("Cleaning up previous test data...");

        try
        {
            // Create database provider
            var provider = DatabaseProviderFactory.GetProvider(databaseProvider);

            // Setup minimal logging for cleanup
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });

            // Clean up Quartz tables
            await ClusterTestHelper.CleanupQuartzTables(connectionString, provider, loggerFactory);

            Console.WriteLine("[OK] Database cleaned");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Could not clean database (this is OK if first run): {ex.Message}");
            return 0; // Return success even if cleanup fails
        }
    }

    private static ClusterNodeConfiguration LoadConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var initialScheduleDelaySecondsStr = configuration["InitialScheduleDelaySeconds"]
            ?? throw new InvalidOperationException("InitialScheduleDelaySeconds not found in configuration");

        if (!int.TryParse(initialScheduleDelaySecondsStr, out var initialScheduleDelaySeconds))
        {
            throw new InvalidOperationException($"InitialScheduleDelaySeconds '{initialScheduleDelaySecondsStr}' is not a valid integer");
        }

        return new ClusterNodeConfiguration
        {
            InitialScheduleDelaySeconds = initialScheduleDelaySeconds
        };
    }

    private static LoggerSetupResult SetupLogger(
        string logsDirectory,
        string mode,
        string? contextProperty = null,
        LogEventLevel minimumLevel = LogEventLevel.Debug,
        bool enableQuartzLogging = false)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var logFileName = Path.Combine(logsDirectory, $"{mode}-{timestamp}.log");

        var config = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning);

        // Add enrichment if context property is provided (for nodes)
        if (contextProperty != null)
        {
            config = config
                .Enrich.FromLogContext()
                .Enrich.WithProperty("NodeId", contextProperty);
        }

        // Configure output templates based on whether we have context
        var consoleTemplate = contextProperty != null
            ? "[{Timestamp:HH:mm:ss.fff}] [{Level:u3}] [{NodeId}] {Message:lj}{NewLine}{Exception}"
            : "[{Timestamp:HH:mm:ss}] {Message:lj}{NewLine}{Exception}";

        var fileTemplate = contextProperty != null
            ? "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{NodeId}] {Message:lj}{NewLine}{Exception}"
            : "[{Timestamp:yyyy-MM-dd HH:mm:ss}] {Message:lj}{NewLine}{Exception}";

        config = config
            .WriteTo.Console(outputTemplate: consoleTemplate)
            .WriteTo.File(
                logFileName,
                outputTemplate: fileTemplate,
                rollingInterval: RollingInterval.Infinite,
                fileSizeLimitBytes: null);

        Log.Logger = config.CreateLogger();

        var loggerFactory = new LoggerFactory().AddSerilog(Log.Logger);
        var logger = loggerFactory.CreateLogger<Program>();

        // Set Quartz.NET to use the same logger factory (only for nodes)
        if (enableQuartzLogging)
        {
            LogContext.SetCurrentLogProvider(loggerFactory);
        }

        return new LoggerSetupResult(logFileName, loggerFactory, logger);
    }

    private static LoggerSetupResult SetupNodeLogger(
        string logsDirectory,
        string nodeId)
    {
        return SetupLogger(logsDirectory, $"cluster-test-{nodeId}",
            contextProperty: nodeId, minimumLevel: LogEventLevel.Debug, enableQuartzLogging: true);
    }

    private static LoggerSetupResult SetupOrchestratorLogger(string logsDirectory)
    {
        return SetupLogger(logsDirectory, "orchestrator",
            contextProperty: null, minimumLevel: LogEventLevel.Information, enableQuartzLogging: false);
    }
}

/// <summary>
/// Result of logger setup containing all configured logging components.
/// </summary>
public record LoggerSetupResult(
    string LogFileName,
    ILoggerFactory LoggerFactory,
    ILogger<Program> Logger);

/// <summary>
/// Command line arguments parsed from the application input.
/// </summary>
public record CommandLineArgs(
    string NodeId,
    int DurationSeconds,
    int JobIntervalMs,
    int JobDelayMs,
    bool ShouldScheduleJob,
    bool ShouldInitialize,
    string DatabaseProvider,
    string ConnectionString);

/// <summary>
/// Orchestrator mode arguments for managing multiple cluster nodes.
/// </summary>
public record OrchestratorArgs(
    int NodeCount,
    int DurationSeconds,
    int JobIntervalMs,
    int JobDelayMs,
    bool NoPause,
    string DatabaseProvider,
    string ConnectionString);

/// <summary>
/// Application configuration options loaded from appsettings.json.
/// </summary>
public record ClusterNodeConfiguration
{
    public required int InitialScheduleDelaySeconds { get; init; }
}

/// <summary>
/// Interface for platform-specific process launching.
/// </summary>
public interface IProcessLauncher
{
    /// <summary>
    /// Launches a process in a new window with the specified executable and arguments.
    /// </summary>
    /// <param name="exePath">Path to the executable to launch.</param>
    /// <param name="arguments">Command-line arguments to pass.</param>
    /// <param name="noPause">If true, do not pause for user input after completion.</param>
    /// <returns>The started process, or null if launch failed.</returns>
    Process? LaunchInNewWindow(string exePath, string arguments, bool noPause = false);
}

/// <summary>
/// Windows-specific process launcher using PowerShell.
/// </summary>
public sealed class WindowsProcessLauncher : IProcessLauncher
{
    public Process? LaunchInNewWindow(string exePath, string arguments, bool noPause = false)
    {
        // Determine the command based on whether it's an .exe or a .dll
        var command = exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? BuildPowerShellCommand(exePath, arguments, noPause)
            : BuildPowerShellCommand($"dotnet '{exePath}'", arguments, noPause);

        // When noPause is true, use -Command (window closes on exit)
        // When noPause is false, use -NoExit -Command (window stays open with pause prompt)
        var psArgs = noPause
            ? $"-Command \"{command}\""
            : $"-NoExit -Command \"{command}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments = psArgs,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };

        return Process.Start(startInfo);
    }

    private static string BuildPowerShellCommand(string executable, string arguments, bool noPause)
    {
        // Escape single quotes in the executable path for PowerShell
        var escapedExe = executable.Replace("'", "''");

        if (noPause)
        {
            return $"& '{escapedExe}' {arguments}";
        }

        return $"& '{escapedExe}' {arguments}; Write-Host ''; Write-Host 'Node completed. Press any key to close...' -ForegroundColor Yellow; $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')";
    }
}

/// <summary>
/// Linux-specific process launcher with terminal emulator detection.
/// </summary>
public sealed class LinuxProcessLauncher : IProcessLauncher
{
    private static readonly string[] supportedTerminals = ["gnome-terminal", "xterm", "konsole", "xfce4-terminal"];

    public Process? LaunchInNewWindow(string exePath, string arguments, bool noPause = false)
    {
        // Try to find an available terminal emulator
        var terminal = FindAvailableTerminal();

        if (terminal != null)
        {
            return LaunchInTerminal(terminal, exePath, arguments, noPause);
        }

        // Fallback: Run without terminal (background process)
        return LaunchWithoutTerminal(exePath, arguments);
    }

    private static string? FindAvailableTerminal()
    {
        foreach (var terminal in supportedTerminals)
        {
            if (IsTerminalAvailable(terminal))
            {
                return terminal;
            }
        }
        return null;
    }

    private static bool IsTerminalAvailable(string terminalName)
    {
        try
        {
            var checkProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "which",
                Arguments = terminalName,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            checkProcess?.WaitForExit();
            return checkProcess?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static Process? LaunchInTerminal(string terminal, string exePath, string arguments, bool noPause)
    {
        var command = BuildCommand(exePath, arguments);
        var terminalArgs = BuildTerminalArguments(terminal, command, noPause);

        var startInfo = new ProcessStartInfo
        {
            FileName = terminal,
            Arguments = terminalArgs,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        // Note: Terminal close behavior when noPause=true depends on terminal emulator settings.
        // Some terminals (like xterm with -hold flag removed) will auto-close, others may require
        // manual configuration in the terminal emulator's preferences.
        return Process.Start(startInfo);
    }

    private static Process? LaunchWithoutTerminal(string exePath, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exePath : "dotnet",
            Arguments = exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? arguments : $"{exePath} {arguments}",
            UseShellExecute = false,
            CreateNoWindow = false
        };

        return Process.Start(startInfo);
    }

    private static string BuildCommand(string exePath, string arguments)
    {
        var executable = exePath.EndsWith(".exe") ? exePath : $"dotnet {exePath}";
        return $"{executable} {arguments}";
    }

    private static string BuildTerminalArguments(string terminal, string command, bool noPause)
    {
        var bashCommand = noPause
            ? command
            : $"{command}; echo ''; echo 'Node completed. Press any key to close...'; read -n 1";

        return terminal switch
        {
            // gnome-terminal closes on command exit by default (no special flags needed)
            "gnome-terminal" => $"-- bash -c \"{bashCommand}\"",

            // xterm closes on command exit without -hold flag (which we don't use)
            "xterm" => $"-e bash -c \"{bashCommand}\"",

            // konsole closes on command exit without --noclose flag (which we don't use)
            "konsole" => $"-e bash -c \"{bashCommand}\"",

            // xfce4-terminal closes on command exit by default
            "xfce4-terminal" => $"-e \"bash -c '{bashCommand}'\"",

            // Default for unknown terminals
            _ => $"-e bash -c \"{bashCommand}\""
        };
    }
}

/// <summary>
/// macOS-specific process launcher using Terminal.app via AppleScript.
/// </summary>
public sealed class MacOsProcessLauncher : IProcessLauncher
{
    public Process? LaunchInNewWindow(string exePath, string arguments, bool noPause = false)
    {
        var command = BuildCommand(exePath, arguments);
        var script = BuildAppleScript(command, noPause);

        var startInfo = new ProcessStartInfo
        {
            FileName = "osascript",
            Arguments = $"-e \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return Process.Start(startInfo);
    }

    private static string BuildCommand(string exePath, string arguments)
    {
        var executable = exePath.EndsWith(".exe") ? exePath : $"dotnet {exePath}";
        return $"{executable} {arguments}";
    }

    private static string BuildAppleScript(string command, bool noPause)
    {
        var bashCommand = noPause
            ? command
            : $"{command}; echo ''; echo 'Node completed. Press any key to close...'; read -n 1";

        return $"tell application \\\"Terminal\\\" to do script \\\"{bashCommand}\\\"";
    }
}

/// <summary>
/// Factory for creating platform-specific process launchers.
/// </summary>
public static class ProcessLauncherFactory
{
    /// <summary>
    /// Gets the appropriate process launcher for the current operating system.
    /// </summary>
    public static IProcessLauncher GetLauncher()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsProcessLauncher();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxProcessLauncher();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsProcessLauncher();
        }

        // Fallback for unknown platforms - use Linux launcher as default
        return new LinuxProcessLauncher();
    }
}