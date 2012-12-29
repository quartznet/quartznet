#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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
using System.Globalization;
using System.Net.Mail;

using Common.Logging;


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
        private static readonly ILog log = LogManager.GetLogger(typeof(SendMailJob));

        /// <summary> The host name of the smtp server. REQUIRED.</summary>
        public const string PropertySmtpHost = "smtp_host";

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

        /// <summary>
        /// Executes the job.
        /// </summary>
        /// <param name="context">The job execution context.</param>
        public virtual void Execute(IJobExecutionContext context)
        {
            JobDataMap data = context.JobDetail.JobDataMap;

            string smtpHost = data.GetString(PropertySmtpHost);
            string to = data.GetString(PropertyRecipient);
            string cc = data.GetString(PropertyCcRecipient);
            string from = data.GetString(PropertySender);
            string replyTo = data.GetString(PropertyReplyTo);
            string subject = data.GetString(PropertySubject);
            string message = data.GetString(PropertyMessage);

            if (smtpHost == null || smtpHost.Trim().Length == 0)
            {
                throw new ArgumentException("PropertySmtpHost not specified.");
            }
            if (to == null || to.Trim().Length == 0)
            {
                throw new ArgumentException("PropertyRecipient not specified.");
            }
            if (from == null || from.Trim().Length == 0)
            {
                throw new ArgumentException("PropertySender not specified.");
            }
            if (subject == null || subject.Trim().Length == 0)
            {
                throw new ArgumentException("PropertySubject not specified.");
            }
            if (message == null || message.Trim().Length == 0)
            {
                throw new ArgumentException("PropertyMessage not specified.");
            }

            if (cc != null && cc.Trim().Length == 0)
            {
                cc = null;
            }

            if (replyTo != null && replyTo.Trim().Length == 0)
            {
                replyTo = null;
            }

            string mailDesc = string.Format(CultureInfo.InvariantCulture, "'{0}' to: {1}", subject, to);

            log.Info(string.Format(CultureInfo.InvariantCulture, "Sending message {0}", mailDesc));

            try
            {
                SendMail(smtpHost, to, cc, from, replyTo, subject, message);
            }
            catch (Exception ex)
            {
                throw new JobExecutionException(string.Format(CultureInfo.InvariantCulture, "Unable to send mail: {0}", mailDesc), ex, false);
            }
        }


        private void SendMail(string smtpHost, string to, string cc, string from, string replyTo, string subject,
                              string message)
        {

            MailMessage mimeMessage = new MailMessage();
            mimeMessage.To.Add(to);
            if (!String.IsNullOrEmpty(cc))
            {
                mimeMessage.CC.Add(cc);
            }
            mimeMessage.From = new MailAddress(from);
            if (!String.IsNullOrEmpty(replyTo))
            {
#if NET_40
                mimeMessage.ReplyToList.Add(new MailAddress(replyTo));
#else
                mimeMessage.ReplyTo = new MailAddress(replyTo);
#endif
            }
            mimeMessage.Subject = subject;
            mimeMessage.Body = message;

            Send(mimeMessage, smtpHost);
        }

        protected virtual void Send(MailMessage mimeMessage, string smtpHost)
        {
            SmtpClient client = new SmtpClient(smtpHost);
            // Do not remove this using. In .NET 4.0 SmtpClient implements IDisposable.
            using (client as IDisposable)
            {
                client.Send(mimeMessage);
            } 
        }
    }
}
