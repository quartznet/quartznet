#region License

/*
 * Copyright 2009- Marko Lahma
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using Spectre.Console;

namespace Quartz.Examples;

/// <summary>
/// Runs one example from the tour.
/// </summary>
/// <remarks>
/// Interactive when it is given nothing and has a terminal to ask on; otherwise it takes the example
/// number and the logger on the command line, so that a run can be scripted.
/// </remarks>
/// <author>Marko Lahma</author>
public static class Program
{
    private static bool CanPrompt => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    public static async Task<int> Main(string[] args)
    {
        string? choice = null;
        string? logger = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h" or "-?":
                    PrintUsage();
                    return 0;

                case "--list":
                    PrintCatalog();
                    return 0;

                case "--logger":
                    if (i + 1 == args.Length)
                    {
                        Console.Error.WriteLine("--logger needs a name: microsoft, serilog or nlog.");
                        return 1;
                    }

                    logger = args[++i];
                    break;

                default:
                    if (choice is not null)
                    {
                        Console.Error.WriteLine($"Unexpected argument '{args[i]}'.");
                        PrintUsage();
                        return 1;
                    }

                    choice = args[i];
                    break;
            }
        }

        if (!Logging.Configure(logger ?? SelectLogger()))
        {
            Console.Error.WriteLine($"Unknown logger '{logger}'. Pick one of: {string.Join(", ", Logging.Names)}.");
            return 1;
        }

        if (!TrySelectExample(choice, out ExampleEntry? example))
        {
            return 1;
        }

        // The tour is something to watch, and watching is over when the reader says it is: the first
        // Ctrl+C cancels the example, which unwinds its waits and shuts its scheduler down properly.
        // A second one is left to the runtime, which kills the process.
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            eventArgs.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("------- Ctrl+C ---------------------------- stopping the example");
            cancellation.Cancel();
        };

        Console.WriteLine();
        Console.WriteLine($"=== {example.Title} ===");
        Console.WriteLine(example.Summary);
        Console.WriteLine();

        try
        {
            await example.Create().Run(cancellation.Token);

            // "Finished", not "succeeded": what an example did is what it printed while it ran, and
            // one of them reports for itself that it could not reach the database it needs
            Console.WriteLine(cancellation.IsCancellationRequested ? "Example stopped." : "Example finished.");
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.WriteLine("Example stopped.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error running example: " + ex.Message);
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static bool TrySelectExample(string? choice, out ExampleEntry example)
    {
        if (choice is null && CanPrompt)
        {
            int picked = AnsiConsole.Prompt(
                new SelectionPrompt<int>()
                    .Title("Select example to run")
                    .PageSize(ExampleCatalog.All.Count + 2)
                    .UseConverter(number => $"{number,2}  {ExampleCatalog.All[number - 1].Title}")
                    .AddChoices(Enumerable.Range(1, ExampleCatalog.All.Count)));

            example = ExampleCatalog.All[picked - 1];
            return true;
        }

        if (choice is null)
        {
            PrintCatalog();
            Console.Write("> ");
            choice = Console.ReadLine();
        }

        if (choice is not null && ExampleCatalog.TryFind(choice, out example))
        {
            return true;
        }

        Console.Error.WriteLine($"'{choice}' is not one of the examples. Run with --list to see them.");
        example = default!;
        return false;
    }

    private static string SelectLogger()
    {
        if (!CanPrompt)
        {
            return Logging.Names[0];
        }

        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select logger")
                .AddChoices(Logging.Names));
    }

    private static void PrintCatalog()
    {
        Console.WriteLine("Quartz.NET examples:");
        Console.WriteLine();

        for (int i = 0; i < ExampleCatalog.All.Count; i++)
        {
            Console.WriteLine($"{i + 1,2}  {ExampleCatalog.All[i].Title}");
            Console.WriteLine($"    {ExampleCatalog.All[i].Summary}");
        }

        Console.WriteLine();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: dotnet run --project src/Quartz.Examples -- [<number>] [--logger <name>]");
        Console.WriteLine();
        Console.WriteLine("  <number>          the example to run; asked for interactively when omitted");
        Console.WriteLine($"  --logger <name>   {string.Join(", ", Logging.Names)}");
        Console.WriteLine("  --list            list the examples and exit");
        Console.WriteLine();
        Console.WriteLine("Ctrl+C stops a running example and shuts its scheduler down.");
    }
}
