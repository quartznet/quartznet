/*
* Copyright 2004-2005 OpenSymphony 
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

/*
* Previously Copyright (c) 2001-2004 James House
*/

using System;
using System.Web.Mail;
using log4net;

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
		private static readonly ILog Log = LogManager.GetLogger(typeof (SendMailJob));

    	/// <summary> The host name of the smtp server. REQUIRED.</summary>
		public const string PROP_SMTP_HOST = "smtp_host";

		/// <summary> The e-mail address to send the mail to. REQUIRED.</summary>
		public const string PROP_RECIPIENT = "recipient";

		/// <summary> The e-mail address to cc the mail to. Optional.</summary>
		public const string PROP_CC_RECIPIENT = "cc_recipient";

		/// <summary> The e-mail address to claim the mail is from. REQUIRED.</summary>
		public const string PROP_SENDER = "sender";

		/// <summary> The e-mail address the message should say to reply to. Optional.</summary>
		public const string PROP_REPLY_TO = "reply_to";

		/// <summary> The subject to place on the e-mail. REQUIRED.</summary>
		public const string PROP_SUBJECT = "subject";

		/// <summary> The e-mail message body. REQUIRED.</summary>
		public const string PROP_MESSAGE = "message";

		/// <summary>
		/// Executes the job.
		/// </summary>
		/// <param name="context">The job execution context.</param>
		public virtual void Execute(JobExecutionContext context)
		{
			JobDataMap data = context.JobDetail.JobDataMap;

			string smtpHost = data.GetString(PROP_SMTP_HOST);
			string to = data.GetString(PROP_RECIPIENT);
			string cc = data.GetString(PROP_CC_RECIPIENT);
			string from = data.GetString(PROP_SENDER);
			string replyTo = data.GetString(PROP_REPLY_TO);
			string subject = data.GetString(PROP_SUBJECT);
			string message = data.GetString(PROP_MESSAGE);

			if (smtpHost == null || smtpHost.Trim().Length == 0)
			{
				throw new ArgumentException("PROP_SMTP_HOST not specified.");
			}
			if (to == null || to.Trim().Length == 0)
			{
				throw new ArgumentException("PROP_RECIPIENT not specified.");
			}
			if (from == null || from.Trim().Length == 0)
			{
				throw new ArgumentException("PROP_SENDER not specified.");
			}
			if (subject == null || subject.Trim().Length == 0)
			{
				throw new ArgumentException("PROP_SUBJECT not specified.");
			}
			if (message == null || message.Trim().Length == 0)
			{
				throw new ArgumentException("PROP_MESSAGE not specified.");
			}

			if (cc != null && cc.Trim().Length == 0)
			{
				cc = null;
			}

			if (replyTo != null && replyTo.Trim().Length == 0)
			{
				replyTo = null;
			}

			string mailDesc = "'" + subject + "' to: " + to;

			Log.Info("Sending message " + mailDesc);

			try
			{
				SendMail(smtpHost, to, cc, from, replyTo, subject, message);
			}
			catch (Exception ex)
			{
				throw new JobExecutionException("Unable to send mail: " + mailDesc, ex, false);
			}
		}

		private void SendMail(string smtpHost, string to, string cc, string from, string replyTo, string subject,
		                      string message)
		{
			MailMessage mimeMessage = PrepareMimeMessage(to, cc, from, replyTo, subject);
			mimeMessage.Body = message;
			SmtpMail.SmtpServer = smtpHost;
			SmtpMail.Send(mimeMessage);
		}

		private MailMessage PrepareMimeMessage(string to, string cc, string from, string replyTo,
		                                       string subject)
		{
			MailMessage message = new MailMessage();

			message.From = from;
			message.To = to;
			message.Subject = subject;
			message.Cc = cc;

			return message;
		}
	}
}