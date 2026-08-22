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

namespace Quartz.HttpApiContract;

internal static class HttpApiConstants
{
    /// <summary>
    /// The problem-details member naming the exception type the server raised.
    /// </summary>
    /// <remarks>
    /// Present on every error body, whichever layer produced it, so that the shape of an error does
    /// not depend on its cause. A client maps the Quartz exception names back to typed exceptions and
    /// treats every other value as opaque — the framework's own names appear here too.
    /// </remarks>
    public const string ProblemDetailsExceptionType = "Quartz-ExceptionType";

    /// <summary>
    /// The problem-details member carrying the exception's stack trace, present only when
    /// <c>QuartzHttpApiOptions.IncludeStackTraceInProblemDetails</c> asks for it.
    /// </summary>
    public const string ProblemDetailsStackTrace = "Quartz-ExceptionStackTrace";

    /// <summary>
    /// The <c>state</c> a fire-instance listing asks for when it wants every state.
    /// </summary>
    /// <remarks>
    /// <see cref="FireInstanceQuery.State" /> defaults to <see cref="FireInstanceState.Executing" /> and
    /// uses <see langword="null" /> for "every state", so an omitted query parameter cannot carry the
    /// "every state" meaning without also changing what a bare request means. The sentinel says it
    /// explicitly instead, and a request that names no state gets the record's default.
    /// </remarks>
    public const string AnyFireInstanceState = "Any";
}