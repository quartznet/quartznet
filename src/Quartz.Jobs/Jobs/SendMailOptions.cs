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

namespace Quartz.Jobs;

/// <summary>
/// The message <see cref="SendMailJob" /> sends, and the SMTP server it goes through.
/// </summary>
/// <remarks>
/// <para>
/// The job is configured through its <see cref="JobDataMap" />, and those keys are the persisted
/// contract — this type is the named way to write and read them.
/// <see cref="JobConfiguratorExtensions.UsingSendMailOptions{TConfigurator}" /> writes them,
/// <see cref="FromJobData" /> reads them back, and job data written by hand or by an earlier version
/// reads the same either way.
/// </para>
/// <para>
/// There is deliberately no SMTP user name or password here. Job data is durable: a persistent job
/// store writes it to <c>QRTZ_JOB_DETAILS</c>, every node in a cluster reads it, the dashboard shows
/// it, and any support-bundle export of that table carries it. Register an
/// <see cref="ICredentialsByHost" /> — a <see cref="NetworkCredential" /> or a
/// <see cref="CredentialCache" /> — with the container instead, and the job authenticates with it.
/// The <c>smtp_username</c> and <c>smtp_password</c> keys still work so that a job scheduled by an
/// earlier version keeps sending, and the job warns when it uses them.
/// </para>
/// </remarks>
public sealed record SendMailOptions
{
    /// <summary>
    /// The host name of the SMTP server to send through.
    /// </summary>
    public required string SmtpHost { get; init; }

    /// <summary>
    /// The port to reach the SMTP server on, or <see langword="null" /> for the client's default.
    /// </summary>
    public int? SmtpPort { get; init; }

    /// <summary>
    /// The address to send the mail to.
    /// </summary>
    public required string Recipient { get; init; }

    /// <summary>
    /// The address to copy the mail to, or <see langword="null" /> for none.
    /// </summary>
    public string? CcRecipient { get; init; }

    /// <summary>
    /// The address to claim the mail is from.
    /// </summary>
    public required string Sender { get; init; }

    /// <summary>
    /// The address the message asks replies to go to, or <see langword="null" /> to leave it to the
    /// sender.
    /// </summary>
    public string? ReplyTo { get; init; }

    /// <summary>
    /// The subject line.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// The message body.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// The name of the encoding the subject and body are sent in, such as <c>utf-8</c>, or
    /// <see langword="null" /> for the default.
    /// </summary>
    public string? Encoding { get; init; }

    /// <summary>
    /// Reads the options out of a job's data.
    /// </summary>
    /// <param name="data">
    /// The job data to read, normally <see cref="IJobExecutionContext.MergedJobDataMap" />.
    /// </param>
    /// <exception cref="ArgumentException">
    /// One of the required keys is absent or empty.
    /// </exception>
    public static SendMailOptions FromJobData(JobDataMap data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return new SendMailOptions
        {
            SmtpHost = Required(data, SendMailJob.PropertySmtpHost),
            SmtpPort = ReadPort(data),
            Recipient = Required(data, SendMailJob.PropertyRecipient),
            CcRecipient = Optional(data, SendMailJob.PropertyCcRecipient),
            Sender = Required(data, SendMailJob.PropertySender),
            ReplyTo = Optional(data, SendMailJob.PropertyReplyTo),
            Subject = Required(data, SendMailJob.PropertySubject),
            Message = Required(data, SendMailJob.PropertyMessage),
            Encoding = Optional(data, SendMailJob.PropertyEncoding),
        };
    }

    /// <summary>
    /// Writes the options as the job data keys <see cref="SendMailJob" /> reads.
    /// </summary>
    public JobDataMap ToJobData()
    {
        JobDataMap data = new JobDataMap
        {
            [SendMailJob.PropertySmtpHost] = SmtpHost,
            [SendMailJob.PropertyRecipient] = Recipient,
            [SendMailJob.PropertySender] = Sender,
            [SendMailJob.PropertySubject] = Subject,
            [SendMailJob.PropertyMessage] = Message,
        };

        if (SmtpPort is not null)
        {
            data[SendMailJob.PropertySmtpPort] = SmtpPort.Value;
        }

        if (CcRecipient is not null)
        {
            data[SendMailJob.PropertyCcRecipient] = CcRecipient;
        }

        if (ReplyTo is not null)
        {
            data[SendMailJob.PropertyReplyTo] = ReplyTo;
        }

        if (Encoding is not null)
        {
            data[SendMailJob.PropertyEncoding] = Encoding;
        }

        return data;
    }

    /// <summary>
    /// The credentials the job data carries, or <see langword="null" /> when it carries none.
    /// </summary>
    /// <remarks>
    /// The legacy path, kept working for job data an earlier version wrote. A credential registered
    /// with the container wins over this one.
    /// </remarks>
    internal static NetworkCredential? ReadJobDataCredentials(JobDataMap data)
    {
        string? userName = Optional(data, SendMailJob.PropertyUsername);
        return userName is null ? null : new NetworkCredential(userName, Optional(data, SendMailJob.PropertyPassword));
    }

    private static string Required(JobDataMap data, string key)
    {
        string? value = data.GetString(key);
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException(key + " not specified.", nameof(data));
        }

        return value;
    }

    private static string? Optional(JobDataMap data, string key)
    {
        data.TryGetString(key, out string? value);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static int? ReadPort(JobDataMap data)
    {
        if (!data.TryGetValue(SendMailJob.PropertySmtpPort, out object? raw) || raw is null || (raw is string text && text.Length == 0))
        {
            return null;
        }

        return data.GetInt(SendMailJob.PropertySmtpPort);
    }
}
