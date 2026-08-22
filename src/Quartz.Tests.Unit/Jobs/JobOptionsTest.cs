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

using Quartz.Jobs;

namespace Quartz.Tests.Unit.Job;

/// <summary>
/// The typed configuration of the jobs in <c>Quartz.Jobs</c>, which writes and reads the same job
/// data keys those jobs have always used.
/// </summary>
public class JobOptionsTest
{
    [Test]
    public void DirectoryScanOptions_WriteTheJobDataKeysTheJobReads()
    {
        JobDataMap data = new DirectoryScanOptions
        {
            Directories = ["/inbox", "/outbox"],
            ScanListenerName = "listener",
            SearchPattern = "*.csv",
            IncludeSubDirectories = true,
            MinimumUpdateAge = TimeSpan.FromSeconds(30),
        }.ToJobData();

        data[DirectoryScanJob.DirectoryNames].Should().Be("/inbox;/outbox",
            "the semicolon-separated list is the persisted form every version of the job has read");
        data[DirectoryScanJob.DirectoryScanListenerName].Should().Be("listener");
        data[DirectoryScanJob.SearchPattern].Should().Be("*.csv");
        data[DirectoryScanJob.IncludeSubDirectories].Should().Be(true);
        data[DirectoryScanJob.MinimumUpdateAge].Should().Be(30000L, "the key holds milliseconds");
        data.Should().NotContainKey(DirectoryScanJob.DirectoryProviderName, "no provider was named");
    }

    [Test]
    public void DirectoryScanOptions_RoundTripThroughJobData()
    {
        DirectoryScanOptions options = new DirectoryScanOptions
        {
            Directories = ["/inbox"],
            DirectoryProviderName = "provider",
            ScanListenerName = "listener",
            SearchPattern = "*.csv",
            IncludeSubDirectories = true,
            MinimumUpdateAge = TimeSpan.FromSeconds(30),
        };

        DirectoryScanOptions.FromJobData(options.ToJobData()).Should().Be(options);
    }

    [Test]
    public void DirectoryScanOptions_ReadTheKeysAnEarlierVersionWrote()
    {
        JobDataMap data = new JobDataMap
        {
            [DirectoryScanJob.DirectoryName] = "/inbox",
            [DirectoryScanJob.DirectoryNames] = "/outbox;/archive",
            [DirectoryScanJob.DirectoryScanListenerName] = "listener",
            ["SEARCH_PATTERN"] = "*.csv",
            ["INCLUDE_SUB_DIRECTORIES"] = "true",
            [DirectoryScanJob.MinimumUpdateAge] = "30000",
        };

        DirectoryScanOptions options = DirectoryScanOptions.FromJobData(data);

        options.Directories.Should().Equal(new[] { "/inbox", "/outbox", "/archive" },
            "both the singular and the plural key contribute, as they always have");
        options.ScanListenerName.Should().Be("listener");
        options.SearchPattern.Should().Be("*.csv");
        options.IncludeSubDirectories.Should().BeTrue();
        options.MinimumUpdateAge.Should().Be(TimeSpan.FromSeconds(30),
            "a job store in StoreJobDataAsStrings mode leaves the number as a string");
    }

    [Test]
    public void DirectoryScanOptions_TakeTheDocumentedDefaults()
    {
        DirectoryScanOptions options = DirectoryScanOptions.FromJobData(new JobDataMap
        {
            [DirectoryScanJob.DirectoryName] = "/inbox",
            [DirectoryScanJob.DirectoryScanListenerName] = "listener",
        });

        options.SearchPattern.Should().Be("*");
        options.IncludeSubDirectories.Should().BeFalse();
        options.MinimumUpdateAge.Should().Be(TimeSpan.FromSeconds(5));
        options.DirectoryProviderName.Should().BeNull();
    }

    [Test]
    public void DirectoryScanOptions_RefuseADirectoryHoldingTheSeparator()
    {
        Action act = () => new DirectoryScanOptions
        {
            Directories = ["/in;box"],
            ScanListenerName = "listener",
        }.ToJobData();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*semicolon*",
                "silently splitting the path into two directories that do not exist is worse than saying so");
    }

    [Test]
    public void DirectoryScanOptions_RequireAListener()
    {
        Action act = () => DirectoryScanOptions.FromJobData(new JobDataMap { [DirectoryScanJob.DirectoryName] = "/inbox" });

        act.Should().Throw<JobExecutionException>().WithMessage($"*{DirectoryScanJob.DirectoryScanListenerName}*");
    }

    [Test]
    public void DirectoryScanOptions_ReachTheJobThroughTheBuilder()
    {
        IJobDetail job = JobBuilder.Create<DirectoryScanJob>()
            .WithIdentity("scan")
            .UsingDirectoryScanOptions(new DirectoryScanOptions
            {
                Directories = ["/inbox"],
                ScanListenerName = "listener",
                SearchPattern = "*.csv",
            })
            .Build();

        job.JobDataMap[DirectoryScanJob.DirectoryNames].Should().Be("/inbox");
        job.JobDataMap[DirectoryScanJob.SearchPattern].Should().Be("*.csv");
        job.Key.Name.Should().Be("scan", "the extension hands the builder back, so the chain keeps its type");
    }

    [Test]
    public void FileScanOptions_RoundTripThroughJobData()
    {
        FileScanOptions options = new FileScanOptions
        {
            FileName = "/var/log/app.log",
            ScanListenerName = "listener",
            MinimumUpdateAge = TimeSpan.FromSeconds(15),
        };

        JobDataMap data = options.ToJobData();

        data[FileScanJob.FileName].Should().Be("/var/log/app.log");
        data[FileScanJob.FileScanListenerName].Should().Be("listener");
        data[FileScanJob.MinimumUpdateAge].Should().Be(15000L);
        FileScanOptions.FromJobData(data).Should().Be(options);
    }

    [Test]
    public void FileScanOptions_ReadTheKeysAnEarlierVersionWrote()
    {
        FileScanOptions options = FileScanOptions.FromJobData(new JobDataMap
        {
            [FileScanJob.FileName] = "/var/log/app.log",
            [FileScanJob.FileScanListenerName] = "listener",
            [FileScanJob.MinimumUpdateAge] = 15000L,
        });

        options.FileName.Should().Be("/var/log/app.log");
        options.MinimumUpdateAge.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Test]
    public void FileScanOptions_RequireAFileAndAListener()
    {
        Action noFile = () => FileScanOptions.FromJobData(new JobDataMap { [FileScanJob.FileScanListenerName] = "listener" });
        noFile.Should().Throw<JobExecutionException>().WithMessage($"*{FileScanJob.FileName}*");

        Action noListener = () => FileScanOptions.FromJobData(new JobDataMap { [FileScanJob.FileName] = "/var/log/app.log" });
        noListener.Should().Throw<JobExecutionException>().WithMessage($"*{FileScanJob.FileScanListenerName}*");
    }

    [Test]
    public void FileScanOptions_ReachTheJobThroughTheBuilder()
    {
        IJobDetail job = JobBuilder.Create<FileScanJob>()
            .WithIdentity("watch")
            .UsingFileScanOptions(new FileScanOptions
            {
                FileName = "/var/log/app.log",
                ScanListenerName = "listener",
            })
            .Build();

        job.JobDataMap[FileScanJob.FileName].Should().Be("/var/log/app.log");
        job.JobDataMap[FileScanJob.MinimumUpdateAge].Should().Be(5000L, "the default is written out, not left implied");
    }

    [Test]
    public void NativeJobOptions_RoundTripThroughJobData()
    {
        NativeJobOptions options = new NativeJobOptions
        {
            Command = "report.exe",
            Parameters = "--nightly",
            WaitForProcess = false,
            ConsumeStreams = true,
            WorkingDirectory = "/opt/reports",
        };

        JobDataMap data = options.ToJobData();

        data[NativeJob.PropertyCommand].Should().Be("report.exe");
        data[NativeJob.PropertyParameters].Should().Be("--nightly");
        data[NativeJob.PropertyWaitForProcess].Should().Be(false);
        data[NativeJob.PropertyConsumeStreams].Should().Be(true);
        data[NativeJob.PropertyWorkingDirectory].Should().Be("/opt/reports");
        NativeJobOptions.FromJobData(data).Should().Be(options);
    }

    [Test]
    public void NativeJobOptions_ReadTheKeysAnEarlierVersionWrote()
    {
        NativeJobOptions options = NativeJobOptions.FromJobData(new JobDataMap
        {
            [NativeJob.PropertyCommand] = "report.exe",
            [NativeJob.PropertyWaitForProcess] = "false",
            [NativeJob.PropertyConsumeStreams] = "true",
        });

        options.Command.Should().Be("report.exe");
        options.WaitForProcess.Should().BeFalse();
        options.ConsumeStreams.Should().BeTrue();
        options.Parameters.Should().BeNull();
    }

    [Test]
    public void NativeJobOptions_WaitForTheProcessUnlessToldOtherwise()
    {
        NativeJobOptions options = NativeJobOptions.FromJobData(new JobDataMap { [NativeJob.PropertyCommand] = "report.exe" });

        options.WaitForProcess.Should().BeTrue("that has always been the default, and it is what makes the exit code the job result");
        options.ConsumeStreams.Should().BeFalse();
    }

    [Test]
    public void NativeJobOptions_RequireACommand()
    {
        Action act = () => NativeJobOptions.FromJobData(new JobDataMap { [NativeJob.PropertyParameters] = "--nightly" });

        act.Should().Throw<JobExecutionException>();
    }

    [Test]
    public void SendMailOptions_RoundTripThroughJobData()
    {
        SendMailOptions options = new SendMailOptions
        {
            SmtpHost = "smtp.example.com",
            SmtpPort = 587,
            Recipient = "katie@example.com",
            CcRecipient = "anthony@example.com",
            Sender = "christian@example.com",
            ReplyTo = "therese@example.com",
            Subject = "test mail",
            Message = "test mail body",
            Encoding = "utf-8",
        };

        JobDataMap data = options.ToJobData();

        data[SendMailJob.PropertySmtpHost].Should().Be("smtp.example.com");
        data[SendMailJob.PropertySmtpPort].Should().Be(587);
        data[SendMailJob.PropertyRecipient].Should().Be("katie@example.com");
        data[SendMailJob.PropertySender].Should().Be("christian@example.com");
        SendMailOptions.FromJobData(data).Should().Be(options);
    }

    [Test]
    public void SendMailOptions_ReadTheKeysAnEarlierVersionWrote()
    {
        SendMailOptions options = SendMailOptions.FromJobData(new JobDataMap
        {
            ["smtp_host"] = "smtp.example.com",
            ["smtp_port"] = "587",
            ["recipient"] = "katie@example.com",
            ["sender"] = "christian@example.com",
            ["subject"] = "test mail",
            ["message"] = "test mail body",
        });

        options.SmtpHost.Should().Be("smtp.example.com");
        options.SmtpPort.Should().Be(587, "the port was documented as a string and is still read as one");
        options.CcRecipient.Should().BeNull();
        options.Encoding.Should().BeNull();
    }

    [Test]
    public void SendMailOptions_CarryNoCredential()
    {
        typeof(SendMailOptions).GetProperties().Select(x => x.Name)
            .Should().NotContain(name => name.Contains("Password", StringComparison.OrdinalIgnoreCase) || name.Contains("UserName", StringComparison.OrdinalIgnoreCase),
                "job data is persisted, cluster-replicated and dashboard-visible, so the credential comes from the container");
    }

    [Test]
    public void SendMailOptions_RequireTheAddressesAndTheBody()
    {
        Action act = () => SendMailOptions.FromJobData(new JobDataMap { [SendMailJob.PropertySmtpHost] = "smtp.example.com" });

        act.Should().Throw<ArgumentException>().WithMessage($"*{SendMailJob.PropertyRecipient}*");
    }

    [Test]
    public void SendMailOptions_ReachTheJobThroughTheBuilder()
    {
        IJobDetail job = JobBuilder.Create<SendMailJob>()
            .WithIdentity("mail")
            .UsingSendMailOptions(new SendMailOptions
            {
                SmtpHost = "smtp.example.com",
                Recipient = "katie@example.com",
                Sender = "christian@example.com",
                Subject = "test mail",
                Message = "test mail body",
            })
            .Build();

        job.JobDataMap[SendMailJob.PropertySmtpHost].Should().Be("smtp.example.com");
        job.JobDataMap.Should().NotContainKey(SendMailJob.PropertyCcRecipient, "an optional setting nobody set is not written");
        job.JobDataMap.Should().NotContainKey(SendMailJob.PropertyPassword);
    }
}
