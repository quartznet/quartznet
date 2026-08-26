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

namespace Quartz.Examples;

/// <summary>
/// The pause in the middle of every example, while the scheduler does the thing being demonstrated.
/// </summary>
/// <remarks>
/// An application never sleeps like this: a scheduler under a host runs for as long as the host does.
/// The tour sleeps because it is a tour — the interesting part happens on its own, some seconds after
/// <c>Start()</c>, and the process has to still be alive to show it. So every wait says what there is
/// to see while it lasts, and ends the moment the reader has seen enough.
/// </remarks>
internal static class Watching
{
    public static async ValueTask For(TimeSpan duration, string whatToWatch, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"------- Watching for {Describe(duration)}: {whatToWatch}");
        Console.WriteLine("------- (Ctrl+C stops early)");

        try
        {
            await Task.Delay(duration, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("------- Seen enough, shutting down -------");
        }
    }

    private static string Describe(TimeSpan duration)
    {
        return duration.TotalSeconds < 120
            ? $"{duration.TotalSeconds:0} seconds"
            : $"{duration.TotalMinutes:0} minutes";
    }
}
