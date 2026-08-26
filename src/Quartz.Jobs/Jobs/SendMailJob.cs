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
using System.Text;

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;

namespace Quartz.Jobs;

/// <summary>
/// A Job which sends an e-mail with the configured content to the configured
/// recipient.
/// </summary>
/// <remarks>
/// <para>
/// The message is configured through the job data keys below. <see cref="SendMailOptions" /> names
/// them all, and <see cref="JobConfiguratorExtensions.UsingSendMailOptions{TConfigurator}" /> writes
/// them, so the settings can be given as a value rather than as string keys; the keys stay the
/// persisted form either way.
/// </para>
/// <para>
/// The SMTP credential is the exception, and is taken from the container: register an
/// <see cref="ICredentialsByHost" /> and the job authenticates with it. Job data is durable,
/// cluster-replicated and visible in the dashboard, which is no place for a password. The
/// <see cref="PropertyUsername" /> and <see cref="PropertyPassword" /> keys are still read when
/// nothing is registered, so a job scheduled by an earlier version keeps sending — with a warning.
/// </para>
/// </remarks>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public class SendMailJob : IJob
{
    private readonly ILogger<SendMailJob> logger;
    private readonly ICredentialsByHost? credentials;

    /// <summary> The host name of the smtp server. REQUIRED.</summary>
    public const string PropertySmtpHost = "smtp_host";

    /// <summary> The port of the smtp server. Optional.</summary>
    public const string PropertySmtpPort = "smtp_port";

    /// <summary>
    /// Username for authenticated session. Password must also be set if username is used. Optional.
    /// <para>
    /// Legacy. Register an <see cref="ICredentialsByHost" /> with the container instead — job data is
    /// persisted, replicated to every node and readable from the dashboard.
    /// </para>
    /// </summary>
    public const string PropertyUsername = "smtp_username";

    /// <summary>
    /// Password for authenticated session. Optional.
    /// <para>
    /// Legacy, and a credential written to <c>QRTZ_JOB_DETAILS</c> in whatever the configured
    /// serializer emits. Register an <see cref="ICredentialsByHost" /> with the container instead.
    /// </para>
    /// </summary>
    public const string PropertyPassword = "smtp_password";

    /// <summary> The e-mail address to send the mail to. REQUIRED.</summary>
    public const string PropertyRecipient = "recipient";

    /// <summary> The e-mail address to cc the mail to. Optional.</summary>
    public const string PropertyCcRecipient = "cc_recipient";

    /// <summary> The e-mail address to claim the mail is from. REQUIRED.</summary>
    public const string PropertySender = "sender";

    /// <summary> The e-mail address the message should say to reply to. Optional.</summary>
    public const string PropertyReplyTo = "reply_to";

    /// <summary> The subject to place on the e-mail. REQUIRED.</summary>
    public const string PropertySubject = "subject";

    /// <summary> The e-mail message body. REQUIRED.</summary>
    public const string PropertyMessage = "message";

    /// <summary> The message subject and body content type. Optional.</summary>
    public const string PropertyEncoding = "encoding";

    /// <summary>
    /// Initializes a new instance of the <see cref="SendMailJob" /> class.
    /// </summary>
    /// <param name="credentials">
    /// What to authenticate to the SMTP server with, taken from the container. A
    /// <see cref="NetworkCredential" /> covers one account; a <see cref="CredentialCache" /> covers
    /// several servers. <see langword="null" /> falls back to the credential in job data, if there is
    /// one, and otherwise sends unauthenticated.
    /// </param>
    public SendMailJob(ICredentialsByHost? credentials = null)
    {
        this.credentials = credentials;
        logger = LogProvider.CreateLogger<SendMailJob>();
    }

    /// <summary>
    /// Executes the job.
    /// </summary>
    /// <param name="context">The job execution context.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public virtual async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobDataMap data = context.MergedJobDataMap;

        SendMailOptions options = SendMailOptions.FromJobData(data);
        MailMessage message = BuildMessage(options);

        try
        {
            var info = new MailInfo
            {
                MailMessage = message,
                SmtpHost = options.SmtpHost,
                SmtpPort = options.SmtpPort,
                Credentials = ResolveCredentials(data)
            };
            await Send(info, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new JobExecutionException($"Unable to send mail: {GetMessageDescription(message)}", ex);
        }
    }

    /// <summary>
    /// Builds the message to send. Override to add to it — an attachment, a header — or to build a
    /// different one entirely.
    /// </summary>
    protected virtual MailMessage BuildMessage(SendMailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        MailMessage mailMessage = new MailMessage();
        mailMessage.To.Add(options.Recipient);

        if (options.CcRecipient is not null)
        {
            mailMessage.CC.Add(options.CcRecipient);
        }

        mailMessage.From = new MailAddress(options.Sender);

        if (options.ReplyTo is not null)
        {
            mailMessage.ReplyToList.Add(new MailAddress(options.ReplyTo));
        }

        mailMessage.Subject = options.Subject;
        mailMessage.Body = options.Message;

        if (options.Encoding is not null)
        {
            var encodingToUse = Encoding.GetEncoding(options.Encoding);
            mailMessage.BodyEncoding = encodingToUse;
            mailMessage.SubjectEncoding = encodingToUse;
        }

        return mailMessage;
    }

    /// <summary>
    /// The credential registered with the container, or the one in job data when there is none.
    /// </summary>
    private ICredentialsByHost? ResolveCredentials(JobDataMap data)
    {
        if (credentials is not null)
        {
            return credentials;
        }

        NetworkCredential? fromJobData = SendMailOptions.ReadJobDataCredentials(data);
        if (fromJobData is not null)
        {
            logger.CredentialsReadFromJobData(PropertyUsername, PropertyPassword);
        }

        return fromJobData;
    }

    /// <summary>
    /// Sends the built message. Override to route mail through something other than
    /// <see cref="SmtpClient" />.
    /// </summary>
    protected virtual async ValueTask Send(MailInfo mailInfo, CancellationToken cancellationToken = default)
    {
        logger.SendingMessage(GetMessageDescription(mailInfo.MailMessage));

        using (var client = new SmtpClient(mailInfo.SmtpHost))
        {
            if (mailInfo.Credentials is not null)
            {
                client.Credentials = mailInfo.Credentials;
            }

            if (mailInfo.SmtpPort is not null)
            {
                client.Port = mailInfo.SmtpPort.Value;
            }

            await client.SendMailAsync(mailInfo.MailMessage, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string GetMessageDescription(MailMessage message)
    {
        return $"'{message.Subject}' to: {string.Join(", ", message.To.Select(x => x.Address).ToArray())}";
    }
}