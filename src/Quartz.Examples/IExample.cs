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
/// One stop on the console tour.
/// </summary>
/// <remarks>
/// An example schedules something, starts a scheduler, waits while it fires, and shuts down. The
/// waiting is the point — a reader runs one of these and watches what Quartz does — so every example
/// takes the token <c>Ctrl+C</c> cancels and stops on it rather than making the reader wait out a
/// delay they have already seen enough of.
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public interface IExample
{
    /// <summary>
    /// Runs the example to completion, or until <paramref name="cancellationToken" /> is cancelled.
    /// </summary>
    ValueTask Run(CancellationToken cancellationToken = default);
}
