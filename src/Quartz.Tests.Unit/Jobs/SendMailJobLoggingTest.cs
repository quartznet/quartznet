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

#nullable enable

using System.Net.Mail;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;

using Quartz.Diagnostics;
using Quartz.Jobs;

namespace Quartz.Tests.Unit.Job;

/// <summary>
/// The line <see cref="SendMailJob" /> writes before it hands a message to the SMTP client.
/// </summary>
/// <remarks>
/// <see cref="SendMailJobTest" /> replaces <c>Send</c> to look at what would have been sent, so the line,
/// which is in <c>Send</c> itself, needs a job that keeps it. The client is not reached: the token is
/// already cancelled, so the send ends before it opens a connection.
/// </remarks>
[NonParallelizable]
public sealed class SendMailJobLoggingTest
{
    [Test]
    public async Task TheMessageIsDescribedBeforeItIsSent()
    {
        using FakeLoggerProvider logs = new();
        using ILoggerFactory factory = new LoggerFactory([logs]);

        LogProvider.SetLogProvider(factory);
        try
        {
            BaseSendingJob job = new();
            using MailMessage message = new("katie@acca.co.uk", "christian@acca.co.uk", "test mail", "test mail body");
            using CancellationTokenSource cancelled = new();
            await cancelled.CancelAsync();

            Func<Task> send = async () => await job.SendUsingBase(new MailInfo { MailMessage = message, SmtpHost = "smtp.invalid" }, cancelled.Token);

            await send.Should().ThrowAsync<OperationCanceledException>();

            FakeLogRecord record = logs.Collector.GetSnapshot().Should().ContainSingle(x => x.Level == LogLevel.Information).Which;
            record.Message.Should().Contain("'test mail' to: christian@acca.co.uk");
        }
        finally
        {
            LogProvider.SetLogProvider(NullLoggerFactory.Instance);
        }
    }

    private sealed class BaseSendingJob : SendMailJob
    {
        public ValueTask SendUsingBase(MailInfo mailInfo, CancellationToken cancellationToken) => Send(mailInfo, cancellationToken);
    }
}
