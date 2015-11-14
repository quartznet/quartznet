using System;
using System.Collections.Generic;
using System.Linq;
using Quartz.Job;

namespace Quartz
{
    internal class DefaultDirectoryProvider : IDirectoryProvider
    {
        public IEnumerable<string> GetDirectoriesToScan(JobDataMap mergedJobDataMap)
        {
            List<string> directoriesToScan = new List<string>();
            string dirName = mergedJobDataMap.GetString(DirectoryScanJob.DirectoryName);
            string dirNames = mergedJobDataMap.GetString(DirectoryScanJob.DirectoryNames);

            if (dirName == null && dirNames == null)
            {
                throw new JobExecutionException($"The parameter '{DirectoryScanJob.DirectoryName}' or '{DirectoryScanJob.DirectoryNames}' " +
                                                "is required and was not found in merged JobDataMap");
            }

            if (dirName != null)
            {
                directoriesToScan.Add(dirName);
            }
            else
            {
                directoriesToScan.AddRange(
                    dirNames.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries)
                        .Distinct()); // just in case their are duplicates
            }

            return directoriesToScan;
        }
    }
}