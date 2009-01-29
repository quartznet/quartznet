using System.Net.Mail;

using MbUnit.Framework;

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
            Assert.AreEqual("someserver", job.ActualSmtpHost);
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
            Assert.AreEqual("someserver", job.ActualSmtpHost);
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
            Assert.Contains(actualMail.To, new MailAddress(Recipient), "Recipient equals");
            Assert.AreEqual(new MailAddress(Sender), actualMail.From, "Sender equals");
            Assert.AreEqual(Subject, actualMail.Subject, "Subject equals");
            Assert.AreEqual(Message, actualMail.Body, "Message equals");
            if (!string.IsNullOrEmpty(CcRecipient))
            {
                Assert.Contains(actualMail.CC, new MailAddress(CcRecipient), "CC equals");
            }
            if (!string.IsNullOrEmpty(ReplyTo))
            {
                Assert.AreEqual(new MailAddress(ReplyTo), actualMail.ReplyTo);
            }
        }
    }

    internal class TestSendMailJob : SendMailJob
    {
        public MailMessage ActualMailSent = new MailMessage();
        public string ActualSmtpHost = "ad";
        
        protected override void Send(MailMessage mimeMessage, string smtpHost)
        {
            ActualMailSent = mimeMessage;
            ActualSmtpHost = smtpHost;
        }
        
    }
}

