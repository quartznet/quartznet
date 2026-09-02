using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz.Jobs;

namespace Quartz.Documentation.Samples.Packages;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/quartz-jobs.md.
/// </summary>
public static class QuartzJobsSamples
{
    public static void DirectoryScanJobSample()
    {
        #region sample_jobs_directory_scan

        IJobDetail job = JobBuilder.Create<DirectoryScanJob>()
            .WithIdentity("inboxScan")
            .UsingDirectoryScanOptions(new DirectoryScanOptions
            {
                Directories = ["/var/spool/inbox"],
                ScanListenerName = nameof(InboxListener),
                SearchPattern = "*.csv",
                IncludeSubDirectories = true,
                MinimumUpdateAge = TimeSpan.FromSeconds(30),
            })
            .Build();

        #endregion
    }

    public static void ScanListenerInSchedulerContext(IScheduler scheduler)
    {
        #region sample_jobs_scan_listener_context

        scheduler.Context["inboxListener"] = new InboxListener();

        #endregion
    }

    public static void FileScanJobSample()
    {
        #region sample_jobs_file_scan

        IJobDetail job = JobBuilder.Create<FileScanJob>()
            .WithIdentity("configWatch")
            .UsingFileScanOptions(new FileScanOptions
            {
                FileName = "/etc/app/settings.json",
                ScanListenerName = "settingsListener",
                MinimumUpdateAge = TimeSpan.FromSeconds(5),
            })
            .Build();

        #endregion
    }

    public static async ValueTask NativeJobSample(IScheduler scheduler)
    {
        #region sample_jobs_native

        IJobDetail job = JobBuilder.Create<NativeJob>()
            .WithIdentity("dumbJob")
            .UsingNativeJobOptions(new NativeJobOptions
            {
                Command = "echo",
                Parameters = "\"hi\" >> foobar.txt",
            })
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("dumbTrigger")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(5)).RepeatForever())
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        #endregion
    }

    public static void SendMailJobSample()
    {
        #region sample_jobs_send_mail

        IJobDetail job = JobBuilder.Create<SendMailJob>()
            .WithIdentity("nightlyDigest")
            .UsingSendMailOptions(new SendMailOptions
            {
                SmtpHost = "smtp.example.com",
                SmtpPort = 587,
                Sender = "scheduler@example.com",
                Recipient = "ops@example.com",
                Subject = "Nightly digest",
                Message = "Everything ran.",
            })
            .Build();

        #endregion
    }

    public static void SmtpCredentials(IServiceCollection services, string smtpPassword)
    {
        #region sample_jobs_smtp_credentials

        // Bound to the server it belongs to. The host to send through is job data, so a credential that
        // answers for every host would go to whatever that data names.
        CredentialCache credentials = new();
        credentials.Add("smtp.example.com", 587, "Basic", new NetworkCredential("mailer", smtpPassword));

        services.AddSingleton<ICredentialsByHost>(credentials);

        #endregion
    }

    public static void NativeJobUnderDependencyInjection(IHostApplicationBuilder builder)
    {
        #region sample_jobs_native_under_di

        builder.Services.AddQuartz(q =>
        {
            q.AddJob<NativeJob>(j => j
                .WithIdentity("nightlyReport")
                .StoreDurably()
                .UsingNativeJobOptions(new NativeJobOptions
                {
                    Command = "report.exe",
                    Parameters = "--nightly",
                    ConsumeStreams = true,
                }));

            q.AddTrigger<NativeJob>(t => t
                .ForJob("nightlyReport")
                .WithCronSchedule("0 0 2 * * ?"));
        });

        #endregion
    }
}
