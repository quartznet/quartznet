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
/// The one place a job carried by a request becomes an <see cref="IJobDetail" />.
/// </summary>
/// <remarks>
/// <para>
/// Three endpoints take a job in their body — add-job, schedule-job and schedule-jobs — and each used to
/// read the job detail out of <see cref="JobDetailDto.AsIJobDetail" />'s result pair and null-forgive it,
/// throwing away the reason the conversion gives when it fails. Here the reason is used, and the throw is
/// the same <see cref="BadHttpRequestException" /> a validation failure raises.
/// </para>
/// <para>
/// Gathering the three in one place also gathers what they have in common for trimming:
/// <see cref="JobDetailDto.AsIJobDetail" /> is <c>[RequiresUnreferencedCode]</c>, because the job type on
/// the wire is a string, and this type is where that statement stops travelling — the endpoints reach it
/// from delegates the routing table holds, so carrying the attribute any further would put it on
/// <c>MapQuartzHttpApi</c>, which every application serving this API calls. It is recorded in
/// <c>TrimAnalysisBaseline.cs</c> instead, which says the rest.
/// </para>
/// </remarks>
internal static class RequestedJobDetail
{
    public static IJobDetail From(JobDetailDto dto)
    {
        var (jobDetail, errorReason) = dto.AsIJobDetail();
        return jobDetail ?? throw new BadHttpRequestException("Request validation failed: " + errorReason);
    }
}
