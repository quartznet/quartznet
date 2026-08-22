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

public class FileScanJobTest
{
    [Test]
    public async Task ShouldNotifyTheListenerNamedByTypedOptions()
    {
        string fileName = Path.Combine(Path.GetTempPath(), $"QuartzTest_{Guid.NewGuid()}.txt");
        await File.WriteAllTextAsync(fileName, "content");

        try
        {
            var listener = new TestFileScanListener();
            var schedulerContext = new SchedulerContext { ["listener"] = listener };

            var job = new FileScanJob();
            var context = TestUtil.NewJobExecutionContextFor(job, schedulerContext);

            JobDataMap data = new FileScanOptions
            {
                FileName = fileName,
                ScanListenerName = "listener",
                MinimumUpdateAge = TimeSpan.FromSeconds(1),
            }.ToJobData();

            foreach (var pair in data)
            {
                context.MergedJobDataMap[pair.Key] = pair.Value;
            }

            // What a previous run of the job would have left behind. The key is the job's own
            // bookkeeping, not something the options say.
            context.MergedJobDataMap["LAST_MODIFIED_TIME"] = DateTimeOffset.UtcNow.AddDays(-1);

            await job.Execute(context);

            listener.UpdatedFiles.Should().Equal(fileName);
            context.JobDetail.JobDataMap.Should().ContainKey("LAST_MODIFIED_TIME",
                "the job records what it saw so the next run can tell whether the file moved on");
        }
        finally
        {
            File.Delete(fileName);
        }
    }

    [Test]
    public async Task ShouldSayWhichSettingIsMissing()
    {
        var job = new FileScanJob();
        var context = TestUtil.NewJobExecutionContextFor(job, new SchedulerContext());

        Func<Task> act = async () => await job.Execute(context);

        await act.Should().ThrowAsync<JobExecutionException>().WithMessage($"*{FileScanJob.FileName}*");
    }

    private sealed class TestFileScanListener : IFileScanListener
    {
        public List<string> UpdatedFiles { get; } = new();

        public ValueTask FileUpdated(string fileName, CancellationToken cancellationToken = default)
        {
            UpdatedFiles.Add(fileName);
            return default;
        }
    }
}
