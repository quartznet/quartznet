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

    /// <summary>
    /// A credential the container holds is handed only to a host the container vouched for. A
    /// <see cref="CredentialCache" /> bound to the server says which one that is.
    /// </summary>
    [Test]
    public void ShouldPreferTheCredentialFromTheContainer()
    {
        var registered = new NetworkCredential("registered", "secret");
        CredentialCache cache = new();
        cache.Add("someserver", 25, "Basic", registered);

        var job = new TestSendMailJob(cache);
        var context = TestUtil.NewJobExecutionContextFor(job);
        GivenAMessage(context, "someserver");
        context.MergedJobDataMap[SendMailJob.PropertyUsername] = "from job data";
        context.MergedJobDataMap[SendMailJob.PropertyPassword] = "also from job data";

        job.Execute(context);

        job.actualCredentials.Should().BeSameAs(registered,
            "a credential the application registered beats one that was left in the job store");
    }

    /// <summary>
    /// The host to send through is job data, which anyone who can schedule this job writes. A bare
    /// <see cref="NetworkCredential" /> answers for every host, so pairing the two would hand the
    /// operator's SMTP login to whatever host that data names — in base64, over <c>AUTH LOGIN</c>, to a
    /// listener the caller controls.
    /// </summary>
    [Test]
    public void AHostAgnosticCredentialIsNeverSentToAHostJobDataNamed()
    {
        var registered = new NetworkCredential("registered", "secret");
        var job = new TestSendMailJob(registered);
        var context = TestUtil.NewJobExecutionContextFor(job);
        GivenAMessage(context, "attacker.example.com");

        Func<Task> act = async () => await job.Execute(context);

        act.Should().ThrowAsync<JobExecutionException>()
            .GetAwaiter().GetResult()
            .WithMessage("*CredentialCache*", "the message names the way to bind the credential to a server")
            .WithMessage("*attacker.example.com*", "and the host it would otherwise have gone to");

        job.actualCredentials.Should().BeNull("nothing was sent, so nothing was handed to an SmtpClient");
        job.actualSmtpHost.Should().Be("ad", "Send was never reached");
    }

    /// <summary>
    /// A cache with no entry for the host in job data sends unauthenticated rather than refusing: the
    /// cache said what it vouches for, and this host is not it.
    /// </summary>
    [Test]
    public void ACacheThatDoesNotCoverTheHostSendsUnauthenticated()
    {
        CredentialCache cache = new();
        cache.Add("mail.acme.example", 25, "Basic", new NetworkCredential("registered", "secret"));

        var job = new TestSendMailJob(cache);
        var context = TestUtil.NewJobExecutionContextFor(job);
        GivenAMessage(context, "attacker.example.com");

        job.Execute(context);

        job.actualSmtpHost.Should().Be("attacker.example.com");
        job.actualCredentials.Should().BeNull(
            "the cache vouches for one server and this is not it, so the mail goes out with no credential "
            + "rather than authenticating to a stranger");
    }

    /// <summary>
    /// The credential is looked up under the port the job data names, so a cache bound to a submission
    /// port answers for it.
    /// </summary>
    [Test]
    public void TheCredentialIsLookedUpUnderTheHostAndPortTheJobNames()
    {
        var registered = new NetworkCredential("registered", "secret");
        CredentialCache cache = new();
        cache.Add("mail.acme.example", 587, "Basic", registered);

        var job = new TestSendMailJob(cache);
        var context = TestUtil.NewJobExecutionContextFor(job);
        GivenAMessage(context, "mail.acme.example");
        context.MergedJobDataMap[SendMailJob.PropertySmtpPort] = 587;

        job.Execute(context);

        job.actualCredentials.Should().BeSameAs(registered);
    }

    [Test]
    public void TlsIsOffByDefaultAndAskedForByJobData()
    {
        var job = new TestSendMailJob();
        var context = TestUtil.NewJobExecutionContextFor(job);
        GivenAMessage(context, "someserver");

        job.Execute(context);
        job.actualEnableSsl.Should().BeFalse("SmtpClient's own default, and a relay that offers no TLS still works");

        var withTls = new TestSendMailJob();
        var tlsContext = TestUtil.NewJobExecutionContextFor(withTls);
        GivenAMessage(tlsContext, "someserver");
        tlsContext.MergedJobDataMap[SendMailJob.PropertyEnableSsl] = true;

        withTls.Execute(tlsContext);
        withTls.actualEnableSsl.Should().BeTrue();
    }

    [Test]
    public void TypedOptionsRoundTripTls()
    {
        JobDataMap data = new SendMailOptions
        {
            SmtpHost = "someserver",
            Recipient = "christian@acca.co.uk",
            Sender = "katie@acca.co.uk",
            Subject = "test mail",
            Message = "test mail body",
            EnableSsl = true,
        }.ToJobData();

        SendMailOptions.FromJobData(data).EnableSsl.Should().BeTrue();
    }

    private static void GivenAMessage(IJobExecutionContext context, string smtpHost)
    {
        context.MergedJobDataMap[SendMailJob.PropertySmtpHost] = smtpHost;
        context.MergedJobDataMap[SendMailJob.PropertyRecipient] = "christian@acca.co.uk";
        context.MergedJobDataMap[SendMailJob.PropertySender] = "katie@acca.co.uk";
        context.MergedJobDataMap[SendMailJob.PropertySubject] = "test mail";
        context.MergedJobDataMap[SendMailJob.PropertyMessage] = "test mail body";
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
    public bool actualEnableSsl;

    public TestSendMailJob(ICredentialsByHost credentials = null) : base(credentials)
    {
    }

    protected override ValueTask Send(MailInfo mailInfo, CancellationToken cancellationToken = default)
    {
        actualMailSent = mailInfo.MailMessage;
        actualSmtpHost = mailInfo.SmtpHost;
        actualCredentials = mailInfo.Credentials;
        actualSmtpPort = mailInfo.SmtpPort;
        actualEnableSsl = mailInfo.EnableSsl;
        return default;
    }
}