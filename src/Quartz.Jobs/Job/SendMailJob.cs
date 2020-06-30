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

using System;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

using Quartz.Logging;

namespace Quartz.Job
{
    /// <summary>
    /// A Job which sends an e-mail with the configured content to the configured
    /// recipient.
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class SendMailJob : IJob
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof(SendMailJob));

        /// <summary> The host name of the smtp server. REQUIRED.</summary>
        public const string PropertySmtpHost = "smtp_host";

        /// <summary> The port of the smtp server. Optional.</summary>
        public const string PropertySmtpPort = "smtp_port";

        /// <summary> Username for authenticated session. Password must also be set if username is used. Optional.</summary>
        public const string PropertyUsername = "smtp_username";

        /// <summary> Password for authenticated session. Optional.</summary>
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
        /// Executes the job.
        /// </summary>
        /// <param name="context">The job execution context.</param>
        public virtual Task Execute(IJobExecutionContext context)
        {
            JobDataMap data = context.MergedJobDataMap;

            MailMessage message = BuildMessageFromParameters(data);

            try
            {
                var portString = GetOptionalParameter(data, PropertySmtpPort);
                int? port = null;
                if (!string.IsNullOrEmpty(portString))
                {
                    port = int.Parse(portString);
                }

                var info = new MailInfo
                {
                    MailMessage = message,
                    SmtpHost = GetRequiredParameter(data, PropertySmtpHost),
                    SmtpPort = port,
                    SmtpUserName = GetOptionalParameter(data, PropertyUsername),
                    SmtpPassword = GetOptionalParameter(data, PropertyPassword)
                };
                Send(info);
            }
            catch (Exception ex)
            {
                throw new JobExecutionException($"Unable to send mail: {GetMessageDescription(message)}", ex, false);
            }
            return Task.FromResult(true);
        }

        protected virtual MailMessage BuildMessageFromParameters(JobDataMap data)
        {
            string to = GetRequiredParameter(data, PropertyRecipient);
            string from = GetRequiredParameter(data, PropertySender);
            string subject = GetRequiredParameter(data, PropertySubject);
            string message = GetRequiredParameter(data, PropertyMessage);

            string? cc = GetOptionalParameter(data, PropertyCcRecipient);
            string? replyTo = GetOptionalParameter(data, PropertyReplyTo);

            string? encoding = GetOptionalParameter(data, PropertyEncoding);

            MailMessage mailMessage = new MailMessage();
            mailMessage.To.Add(to);

            if (!string.IsNullOrEmpty(cc))
            {
                mailMessage.CC.Add(cc);
            }
            mailMessage.From = new MailAddress(from);

            if (!string.IsNullOrEmpty(replyTo))
            {
                mailMessage.ReplyToList.Add(new MailAddress(replyTo));
            }

            mailMessage.Subject = subject;
            mailMessage.Body = message;

            if (!string.IsNullOrEmpty(encoding))
            {
                var encodingToUse = Encoding.GetEncoding(encoding);
                mailMessage.BodyEncoding = encodingToUse;
                mailMessage.SubjectEncoding = encodingToUse;
            }

            return mailMessage;
        }

        protected virtual string GetRequiredParameter(JobDataMap data, string propertyName)
        {
            var value = data.GetString(propertyName);
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException(propertyName + " not specified.");
            }
            return value!;
        }

        protected virtual string? GetOptionalParameter(JobDataMap data, string propertyName)
        {
            var value = data.GetString(propertyName);
            return string.IsNullOrEmpty(value) ? null : value;
        }

        protected virtual void Send(MailInfo mailInfo)
        {
            log.Info($"Sending message {GetMessageDescription(mailInfo.MailMessage)}");

            using (var client = new SmtpClient(mailInfo.SmtpHost))
            {
                if (mailInfo.SmtpUserName != null)
                {
                    client.Credentials = new NetworkCredential(mailInfo.SmtpUserName, mailInfo.SmtpPassword);
                }

                if (mailInfo.SmtpPort != null)
                {
                    client.Port = mailInfo.SmtpPort.Value;
                }

                client.Send(mailInfo.MailMessage);
            }
        }

        private static string GetMessageDescription(MailMessage message)
        {
            string mailDesc = $"'{message.Subject}' to: {string.Join(", ", message.To.Select(x => x.Address).ToArray())}";
            return mailDesc;
        }

        public class MailInfo
        {
            public MailMessage MailMessage { get; set; } = null!;

            public string SmtpHost { get; set; } = null!;

            public int? SmtpPort { get; set; }

            public string? SmtpUserName { get; set; }

            public string? SmtpPassword { get; set; }
        }
    }
}