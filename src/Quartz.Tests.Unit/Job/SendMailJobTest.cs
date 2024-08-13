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

using System.Net.Mail;

using Quartz.Job;

namespace Quartz.Tests.Unit.Job;

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
        context.MergedJobDataMap.Put("smtp_host", "someserver");
        context.MergedJobDataMap.Put("recipient", expectedMail.recipient);
        context.MergedJobDataMap.Put("sender", expectedMail.sender);
        context.MergedJobDataMap.Put("subject", expectedMail.subject);
        context.MergedJobDataMap.Put("message", expectedMail.message);

        //When
        job.Execute(context);

        //Then
        expectedMail.IsEqualTo(job.actualMailSent);
        Assert.That(job.actualSmtpHost, Is.EqualTo("someserver"));
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
        context.MergedJobDataMap.Put("smtp_host", "someserver");
        context.MergedJobDataMap.Put("recipient", expectedMail.recipient);
        context.MergedJobDataMap.Put("cc_recipient", expectedMail.ccRecipient);
        context.MergedJobDataMap.Put("sender", expectedMail.sender);
        context.MergedJobDataMap.Put("reply_to", expectedMail.replyTo);
        context.MergedJobDataMap.Put("subject", expectedMail.subject);
        context.MergedJobDataMap.Put("message", expectedMail.message);

        //When
        job.Execute(context);

        //Then
        expectedMail.IsEqualTo(job.actualMailSent);
        Assert.That(job.actualSmtpHost, Is.EqualTo("someserver"));
    }

    [Test]
    public void ShouldSetNetworkProperties()
    {
        //Given
        var expectedMail = new ExpectedMail("christian@acca.co.uk", "katie@acca.co.uk", "test mail", "test mail body");

        //optional properties
        expectedMail.ccRecipient = "anthony@acca.co.uk";
        expectedMail.replyTo = "therese@acca.co.uk";

        var job = new TestSendMailJob();

        var context = TestUtil.NewJobExecutionContextFor(job);
        context.MergedJobDataMap.Put("smtp_host", "someserver");
        context.MergedJobDataMap.Put("recipient", expectedMail.recipient);
        context.MergedJobDataMap.Put("sender", expectedMail.sender);
        context.MergedJobDataMap.Put("subject", expectedMail.subject);
        context.MergedJobDataMap.Put("message", expectedMail.message);
        context.MergedJobDataMap.Put("smtp_username", "user 123");
        context.MergedJobDataMap.Put("smtp_password", "pass 321");
        context.MergedJobDataMap.Put("smtp_port", "123");

        //When
        job.Execute(context);

        //Then
        Assert.Multiple(() =>
        {
            Assert.That(job.actualSmtpHost, Is.EqualTo("someserver"));
            Assert.That(job.actualSmtpUserName, Is.EqualTo("user 123"));
            Assert.That(job.actualSmtpPassword, Is.EqualTo("pass 321"));
            Assert.That(job.actualSmtpPort, Is.EqualTo(123));
        });
    }
}

internal sealed class ExpectedMail
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
        Assert.Multiple(() =>
        {
            Assert.That(actualMail.To, Does.Contain(new MailAddress(recipient)), "Recipient equals");
            Assert.That(actualMail.From, Is.EqualTo(new MailAddress(sender)), "Sender equals");
            Assert.That(actualMail.Subject, Is.EqualTo(subject), "Subject equals");
            Assert.That(actualMail.Body, Is.EqualTo(message), "Message equals");
        });
        if (!string.IsNullOrEmpty(ccRecipient))
        {
            Assert.That(actualMail.CC, Does.Contain(new MailAddress(ccRecipient)), "CC equals");
        }
        if (!string.IsNullOrEmpty(replyTo))
        {
            Assert.Multiple(() =>
            {
                Assert.That(actualMail.ReplyToList, Has.Count.EqualTo(1));
                Assert.That(actualMail.ReplyToList[0], Is.EqualTo(new MailAddress(replyTo)));
            });
        }
    }
}

internal sealed class TestSendMailJob : SendMailJob
{
    public MailMessage actualMailSent = new MailMessage();
    public string actualSmtpHost = "ad";
    public string actualSmtpUserName;
    public string actualSmtpPassword;
    public int? actualSmtpPort;

    protected override void Send(MailInfo info)
    {
        actualMailSent = info.MailMessage;
        actualSmtpHost = info.SmtpHost;
        actualSmtpUserName = info.SmtpUserName;
        actualSmtpPassword = info.SmtpPassword;
        actualSmtpPort = info.SmtpPort;
    }
}