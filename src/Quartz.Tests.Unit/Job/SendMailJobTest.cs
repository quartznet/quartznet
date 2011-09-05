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

using System.Net.Mail;

using NUnit.Framework;

using Quartz.Job;

namespace Quartz.Tests.Unit.Job
{
    /// <summary>
    /// Tests for SendMailJob.
    /// </summary>
    /// <author>Christian Crowhurst</author>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class SendMailJobTest
    {
        [Test]
        public void ShouldSendMailWithMandatoryProperties()
        {
            //Given
            var expectedMail = new ExpectedMail("christian@acca.co.uk", "katie@acca.co.uk", "test mail", "test mail body");
            var job = new TestSendMailJob();

            var context = TestUtil.NewJobExecutionContextFor(job);
            context.JobDetail.JobDataMap.Put("smtp_host", "someserver");
            context.JobDetail.JobDataMap.Put("recipient", expectedMail.recipient);
            context.JobDetail.JobDataMap.Put("sender", expectedMail.sender);
            context.JobDetail.JobDataMap.Put("subject", expectedMail.subject);
            context.JobDetail.JobDataMap.Put("message", expectedMail.message);

            //When
            job.Execute(context);

            //Then
            expectedMail.IsEqualTo(job.actualMailSent);
            Assert.AreEqual("someserver", job.actualSmtpHost);
        }


        [Test]
        public void ShouldSendMailWithOptionalProperties()
        {
            //Given
            var expectedMail = new ExpectedMail("christian@acca.co.uk", "katie@acca.co.uk", "test mail", "test mail body");

            //optional properties
            expectedMail.ccRecipient = "anthony@acca.co.uk";
            expectedMail.replyTo = "therese@acca.co.uk";

            var job = new TestSendMailJob();

            var context = TestUtil.NewJobExecutionContextFor(job);
            context.JobDetail.JobDataMap.Put("smtp_host", "someserver");
            context.JobDetail.JobDataMap.Put("recipient", expectedMail.recipient);
            context.JobDetail.JobDataMap.Put("cc_recipient", expectedMail.ccRecipient);
            context.JobDetail.JobDataMap.Put("sender", expectedMail.sender);
            context.JobDetail.JobDataMap.Put("reply_to", expectedMail.replyTo);
            context.JobDetail.JobDataMap.Put("subject", expectedMail.subject);
            context.JobDetail.JobDataMap.Put("message", expectedMail.message);

            //When
            job.Execute(context);

            //Then
            expectedMail.IsEqualTo(job.actualMailSent);
            Assert.AreEqual("someserver", job.actualSmtpHost);
        }
    }

    internal class ExpectedMail
    {
        public readonly string recipient;
        public readonly string sender;
        public readonly string subject;
        public readonly string message;
        public string ccRecipient;
        public string replyTo;

        public ExpectedMail(string recipient, string sender, string subject, string message)
        {
            this.recipient = recipient;
            this.sender = sender;
            this.subject = subject;
            this.message = message;
        }

        public void IsEqualTo(MailMessage actualMail)
        {
            Assert.Contains(new MailAddress(recipient), actualMail.To, "Recipient equals");
            Assert.AreEqual(new MailAddress(sender), actualMail.From, "Sender equals");
            Assert.AreEqual(subject, actualMail.Subject, "Subject equals");
            Assert.AreEqual(message, actualMail.Body, "Message equals");
            if (!string.IsNullOrEmpty(ccRecipient))
            {
                Assert.Contains(new MailAddress(ccRecipient), actualMail.CC, "CC equals");
            }
            if (!string.IsNullOrEmpty(replyTo))
            {
#if NET_40
                Assert.AreEqual(1, actualMail.ReplyToList.Count);
                Assert.AreEqual(new MailAddress(replyTo), actualMail.ReplyToList[0]);
#else
                Assert.AreEqual(new MailAddress(ReplyTo), actualMail.ReplyTo);
#endif
            }
        }
    }

    internal class TestSendMailJob : SendMailJob
    {
        public MailMessage actualMailSent = new MailMessage();
        public string actualSmtpHost = "ad";
        
        protected override void Send(MailMessage mimeMessage, string smtpHost)
        {
            actualMailSent = mimeMessage;
            actualSmtpHost = smtpHost;
        }
        
    }
}

