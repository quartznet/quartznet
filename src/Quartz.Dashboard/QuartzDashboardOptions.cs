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

public class QuartzDashboardOptions
{
    public string DashboardPath { get; set; } = "/quartz";

    public string? AuthorizationPolicy { get; set; }

    public IDashboardAuthorizationFilter? AuthorizationFilter { get; set; }

    public bool ReadOnly { get; set; }

    public string ApiPath { get; set; } = "/quartz-api";

    internal string TrimmedDashboardPath => DashboardPath.TrimEnd('/');

    internal string TrimmedApiPath => ApiPath.TrimEnd('/');
}
