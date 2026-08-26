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

using Microsoft.Extensions.Logging;

namespace Quartz.Jobs;

/// <summary>
/// Every event the send mail job logs, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// <para>
/// Event ids 7300-7399 belong to this area. An id, once given out, is what an operator filters and
/// alerts on, so it is never reused for a different event and never renumbered;
/// <c>LogEventCatalogTest</c> makes a change to one a reviewed diff.
/// </para>
/// </remarks>
internal static partial class SendMailJobLog
{
    [LoggerMessage(EventId = 7300, Level = LogLevel.Warning, Message = "SMTP credentials are being read from job data ('{UserNameKey}' / '{PasswordKey}'), which a persistent job store writes to the database and replicates to every node in the cluster. Register an ICredentialsByHost with the container instead.")]
    public static partial void CredentialsReadFromJobData(this ILogger logger, string userNameKey, string passwordKey);

    [LoggerMessage(EventId = 7301, Level = LogLevel.Information, Message = "Sending message {MailMessage}")]
    public static partial void SendingMessage(this ILogger logger, string mailMessage);
}
