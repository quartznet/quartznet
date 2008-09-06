#if NET_20

using System.Net.Mail;
using NUnit.Framework;
using NUnit.Framework.SyntaxHelpers;

using Quartz.Job;

namespace Quartz.Tests.Unit.Job
{
    /// <summary>
    /// Tests for SendMailJob.
    /// </summary>
    /// <author>Christian Crowhurst</author>
    [TestFixture]
    public class SendMailJobTest
    {
        [Test]
        public void ShouldSendMailWithMandatoryProperties()
        {
            //Given
            ExpectedMail expectedMail =
                new ExpectedMail("christian@acca.co.uk", "katie@acca.co.uk", "test mail", "test mail body");

            TestSendMailJob job = new TestSendMailJob();

            JobExecutionContext context = TestUtil.NewJobExecutionContextFor(job);
            context.JobDetail.JobDataMap.Put("smtp_host", "someserver");
            context.JobDetail.JobDataMap.Put("recipient", expectedMail.Recipient);
            context.JobDetail.JobDataMap.Put("sender", expectedMail.Sender);
            context.JobDetail.JobDataMap.Put("subject", expectedMail.Subject);
            context.JobDetail.JobDataMap.Put("message", expectedMail.Message);

            //When
            job.Execute(context);

            //Then
            expectedMail.IsEqualTo(job.ActualMailSent);
            Assert.That(job.ActualSmtpHost, Is.EqualTo("someserver"));
        }


        [Test]
        public void ShouldSendMailWithOptionalProperties()
        {
            //Given
            ExpectedMail expectedMail =
                new ExpectedMail("christian@acca.co.uk", "katie@acca.co.uk", "test mail", "test mail body");
            //optional properties
            expectedMail.CcRecipient = "anthony@acca.co.uk";
            expectedMail.ReplyTo = "therese@acca.co.uk";

            TestSendMailJob job = new TestSendMailJob();

            JobExecutionContext context = TestUtil.NewJobExecutionContextFor(job);
            context.JobDetail.JobDataMap.Put("smtp_host", "someserver");
            context.JobDetail.JobDataMap.Put("recipient", expectedMail.Recipient);
            context.JobDetail.JobDataMap.Put("cc_recipient", expectedMail.CcRecipient);
            context.JobDetail.JobDataMap.Put("sender", expectedMail.Sender);
            context.JobDetail.JobDataMap.Put("reply_to", expectedMail.ReplyTo);
            context.JobDetail.JobDataMap.Put("subject", expectedMail.Subject);
            context.JobDetail.JobDataMap.Put("message", expectedMail.Message);

            //When
            job.Execute(context);

            //Then
            expectedMail.IsEqualTo(job.ActualMailSent);
            Assert.That(job.ActualSmtpHost, Is.EqualTo("someserver"));
        }
    }

    internal class ExpectedMail
    {
        public string Recipient;
        public string Sender;
        public string Subject;
        public string Message;
        public string CcRecipient;
        public string ReplyTo;

        public ExpectedMail(string recipient, string sender, string subject, string message)
        {
            Recipient = recipient;
            Sender = sender;
            Subject = subject;
            Message = message;
        }

        public void IsEqualTo(MailMessage actualMail)
        {
            Assert.That(actualMail.To.Contains(new MailAddress(Recipient)), Is.True, "Recipient equals");
            Assert.That(actualMail.From, Is.EqualTo(new MailAddress(Sender)), "Sender equals");
            Assert.That(actualMail.Subject, Is.EqualTo(Subject), "Subject equals");
            Assert.That(actualMail.Body, Is.EqualTo(Message), "Message equals");
            if (!string.IsNullOrEmpty(CcRecipient))
                Assert.That(actualMail.CC.Contains(new MailAddress(CcRecipient)), Is.True, "CC equals");
            if (!string.IsNullOrEmpty(ReplyTo)) Assert.That(actualMail.ReplyTo, Is.EqualTo(new MailAddress(ReplyTo)));
        }
    }

    internal class TestSendMailJob : SendMailJob
    {
        public MailMessage ActualMailSent;
        public string ActualSmtpHost;

        protected override void Send(MailMessage mimeMessage, string smtpHost)
        {
            ActualMailSent = mimeMessage;
            ActualSmtpHost = smtpHost;
        }
    }
}

#endif