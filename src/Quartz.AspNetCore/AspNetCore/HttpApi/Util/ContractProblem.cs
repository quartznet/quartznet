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

using Microsoft.AspNetCore.Http;

using Quartz.HttpApiContract;

namespace Quartz.AspNetCore.HttpApi.Util;

/// <summary>
/// Every problem details body the HTTP API writes, through <c>Results.Problem</c> with the <c>type</c> and
/// <c>title</c> the contract names for the status.
/// </summary>
/// <remarks>
/// Left to choose, <c>Results.Problem</c> takes them from the running ASP.NET Core's own table, which is not
/// the same table on every runtime: 11 titles a <c>500</c> <c>Internal Server Error</c> where 10 wrote
/// <c>An error occurred while processing your request.</c> (#4234). <see cref="ProblemDetailsFactory.Defaults" />
/// is ASP.NET Core 10's, and the dashboard agent writes its bodies from the same table, so one failure is one
/// body on every runtime and every carrier. A status outside the table is still left to ASP.NET Core.
/// </remarks>
internal static class ContractProblem
{
    public static IResult Problem(string detail, int statusCode, Dictionary<string, object?>? extensions = null)
    {
        (string? type, string? title) = ProblemDetailsFactory.Defaults(statusCode);

        return Results.Problem(detail: detail, statusCode: statusCode, title: title, type: type, extensions: extensions);
    }
}
