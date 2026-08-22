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

using Quartz.Jobs;

namespace Quartz.Tests.Unit.Job;

/// <summary>
/// Tests for SendMailJob.
/// </summary>
/// <author>Christian Crowhurst</author>
/// <author>Marko Lahma (.NET)</author>
public class SendMailJobTest
{
    [Test]
    public void ShouldSendMailWithMandatoryProperties()
    {
        //Given
        var expectedMail = new ExpectedMail("christian@acca.co.uk", "katie@acca.co.uk", "test mail", "test mail body");
        var job = new TestSendMailJob();

        var context = TestUtil.NewJobExecutionContextFor(job);
        context.MergedJobDataMap["smtp_host"] = "someserver";
        context.MergedJobDataMap["recipient"] = expectedMail.recipient;
        context.MergedJobDataMap["sender"] = expectedMail.sender;
        context.MergedJobDataMap["subject"] = expectedMail.subject;
        context.MergedJobDataMap["message"] = expectedMail.message;

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
        context.MergedJobDataMap["smtp_host"] = "someserver";
        context.MergedJobDataMap["recipient"] = expectedMail.recipient;
        context.MergedJobDataMap["cc_recipient"] = expectedMail.ccRecipient;
        context.MergedJobDataMap["sender"] = expectedMail.sender;
        context.MergedJobDataMap["reply_to"] = expectedMail.replyTo;
        context.MergedJobDataMap["subject"] = expectedMail.subject;
        context.MergedJobDataMap["message"] = expectedMail.message;

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
        context.MergedJobDataMap["smtp_host"] = "someserver";
        context.MergedJobDataMap["recipient"] = expectedMail.recipient;
        context.MergedJobDataMap["sender"] = expectedMail.sender;
        context.MergedJobDataMap["subject"] = expectedMail.subject;
        context.MergedJobDataMap["message"] = expectedMail.message;
        context.MergedJobDataMap["smtp_username"] = "user 123";
        context.MergedJobDataMap["smtp_password"] = "pass 321";
        context.MergedJobDataMap["smtp_port"] = "123";

        //When
        job.Execute(context);

        //Then
        job.actualSmtpHost.Should().Be("someserver");
        job.actualSmtpPort.Should().Be(123);
        job.actualCredentials.Should().BeOfType<NetworkCredential>()
            .Which.Should().Match<NetworkCredential>(x => x.UserName == "user 123" && x.Password == "pass 321",
                "job data written before the credential moved to the container still authenticates");
    }

    [Test]
    public void ShouldTakeItsMessageFromTypedOptions()
    {
        var job = new TestSendMailJob();
        var context = TestUtil.NewJobExecutionContextFor(job);

        JobDataMap data = new SendMailOptions
        {
            SmtpHost = "someserver",
            SmtpPort = 123,
            Recipient = "christian@acca.co.uk",
            CcRecipient = "anthony@acca.co.uk",
            Sender = "katie@acca.co.uk",
            ReplyTo = "therese@acca.co.uk",
            Subject = "test mail",
            Message = "test mail body",
        }.ToJobData();

        foreach (var pair in data)
        {
            context.MergedJobDataMap[pair.Key] = pair.Value;
        }

        job.Execute(context);

        job.actualSmtpHost.Should().Be("someserver");
        job.actualSmtpPort.Should().Be(123);
        job.actualMailSent.To.Should().ContainSingle().Which.Address.Should().Be("christian@acca.co.uk");
        job.actualMailSent.CC.Should().ContainSingle().Which.Address.Should().Be("anthony@acca.co.uk");
        job.actualMailSent.ReplyToList.Should().ContainSingle().Which.Address.Should().Be("therese@acca.co.uk");
        job.actualMailSent.Subject.Should().Be("test mail");
        job.actualMailSent.Body.Should().Be("test mail body");
        job.actualCredentials.Should().BeNull("nothing named a credential, in job data or in the container");
    }

    [Test]
    public void ShouldPreferTheCredentialFromTheContainer()
    {
        var registered = new NetworkCredential("registered", "secret");
        var job = new TestSendMailJob(registered);
        var context = TestUtil.NewJobExecutionContextFor(job);

        context.MergedJobDataMap[SendMailJob.PropertySmtpHost] = "someserver";
        context.MergedJobDataMap[SendMailJob.PropertyRecipient] = "christian@acca.co.uk";
        context.MergedJobDataMap[SendMailJob.PropertySender] = "katie@acca.co.uk";
        context.MergedJobDataMap[SendMailJob.PropertySubject] = "test mail";
        context.MergedJobDataMap[SendMailJob.PropertyMessage] = "test mail body";
        context.MergedJobDataMap[SendMailJob.PropertyUsername] = "from job data";
        context.MergedJobDataMap[SendMailJob.PropertyPassword] = "also from job data";

        job.Execute(context);

        job.actualCredentials.Should().BeSameAs(registered,
            "a credential the application registered beats one that was left in the job store");
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
    public ICredentialsByHost actualCredentials;
    public int? actualSmtpPort;

    public TestSendMailJob(ICredentialsByHost credentials = null) : base(credentials)
    {
    }

    protected override ValueTask Send(MailInfo mailInfo, CancellationToken cancellationToken = default)
    {
        actualMailSent = mailInfo.MailMessage;
        actualSmtpHost = mailInfo.SmtpHost;
        actualCredentials = mailInfo.Credentials;
        actualSmtpPort = mailInfo.SmtpPort;
        return default;
    }
}