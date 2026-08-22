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

using System.Net;
using System.Net.Mail;

namespace Quartz.Jobs;

/// <summary>
/// The message <see cref="SendMailJob" /> built, and the SMTP server it is to be sent through.
/// </summary>
/// <remarks>
/// Handed to <see cref="SendMailJob.Send" />, which is the seam for routing mail through something
/// other than <see cref="SmtpClient" />. A message and a host are always present; the rest of the
/// SMTP settings are optional, and absent means "whatever the client defaults to".
/// </remarks>
public sealed class MailInfo
{
    /// <summary>
    /// The message to send.
    /// </summary>
    public required MailMessage MailMessage { get; init; }

    /// <summary>
    /// The host name of the SMTP server to send through.
    /// </summary>
    public required string SmtpHost { get; init; }

    /// <summary>
    /// The port to reach the SMTP server on, or <see langword="null" /> for the client's default.
    /// </summary>
    public int? SmtpPort { get; init; }

    /// <summary>
    /// What to authenticate to the SMTP server with, or <see langword="null" /> to send
    /// unauthenticated.
    /// </summary>
    /// <remarks>
    /// The credential registered with the container, or — for a job scheduled before there was one —
    /// the <c>smtp_username</c> and <c>smtp_password</c> job data entries as a
    /// <see cref="System.Net.NetworkCredential" />.
    /// </remarks>
    public ICredentialsByHost? Credentials { get; init; }
}
