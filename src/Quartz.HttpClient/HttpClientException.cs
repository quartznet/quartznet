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

namespace Quartz;

/// <summary>
/// A failure <see cref="HttpScheduler" /> could not attribute to a Quartz exception the server named.
/// </summary>
/// <remarks>
/// The API answers an error with problem details naming the exception type it came from, and the client
/// rebuilds that type where it recognises the name — a <see cref="SchedulerException" />,
/// <see cref="ObjectAlreadyExistsException" /> or any of the other six reach the caller as themselves.
/// This is what everything else becomes: a request the endpoint rejected before it reached a scheduler,
/// a scheduler name the server does not hold, a response that carried no problem details, or a body that
/// could not be deserialized. It derives from <see cref="SchedulerException" /> so that one
/// <c>catch</c> covers both halves.
/// </remarks>
public sealed class HttpClientException : SchedulerException
{
    /// <summary>
    /// Creates the exception with the message a caller sees, which carries the server's problem detail
    /// where there was one.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public HttpClientException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates the exception over the one that caused it.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The failure underneath, if there was one.</param>
    public HttpClientException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}