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

using System.Diagnostics.CodeAnalysis;

// The trim- and AOT-analysis warnings Quartz.Dashboard.Agent produces (https://github.com/quartznet/quartznet/issues/3431).
//
// Read src/Quartz/TrimAnalysisBaseline.cs first. The reasoning this file works to is written down there
// and is not repeated here: why a suppression is scoped to the type rather than the member, why
// SuppressMessage rather than the unconditional form, and - most of all - that adding an entry is the
// wrong first move.
//
// One warning, and it is the one the HTTP API and the HTTP client have too. A job arrives over the
// tunnel as a JobDetailDto whose JobType is a string - deliberately, and the type says why - and
// JobDetailDto.AsIJobDetail says [RequiresUnreferencedCode] for the consequence: whoever runs that job
// needs the type to have survived trimming. The carrier is where that statement stops travelling, for the
// reason RequestedJobDetail is where it stops in the API: carrying it further would put it on
// UseDashboardAgent, which every worker with an agent calls, and the worker is exactly the process that
// does hold the job's type.
//
// Everything else the agent reads and writes goes through the wire contract's generated metadata, so
// this package produces no IL3050 at all, and the single-file analyzer found nothing.

[assembly: SuppressMessage("Trimming", "IL2026", Scope = "type", Target = "T:Quartz.Impl.AgentCarrier", Justification = "A job sent to the agent names its type as a string; the worker running it holds the type, and registers it the way the trimming how-to says.")]
