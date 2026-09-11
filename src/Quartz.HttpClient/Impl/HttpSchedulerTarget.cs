#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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

namespace Quartz.Impl;

/// <summary>
/// The one <see cref="HttpClient" /> a registered remote scheduler is reached through.
/// </summary>
/// <remarks>
/// Keyed by the scheduler's name and resolved by everything that talks to that target — the scheduler
/// itself and its execution history — so <c>AddQuartzHttpClient</c>'s client factory still runs once per
/// registration, as its documentation says, rather than once per thing that needs a client. The client
/// belongs to whoever created it and is never disposed here: it is an
/// <see cref="IHttpClientFactory" /> client or one the application built.
/// </remarks>
/// <param name="Client">The client to call the remote scheduler with.</param>
internal sealed record HttpSchedulerTarget(HttpClient Client);
