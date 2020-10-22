using System;
using System.Collections.Generic;

namespace Quartz.OpenTracing
{
    public class QuartzDiagnosticOptions
    {
        private string _componentName = "Quartz";
        private Func<string, IJobExecutionContext, string> _operationNameResolver = (eventName, job) =>
        {
            return $"{eventName} {job.JobDetail.Key.Name}";
        };

        /// <summary>
        /// Allows changing the "component" tag of created spans.
        /// </summary>
        public string ComponentName
        {
            get => _componentName;
            set => _componentName = value ?? throw new ArgumentNullException(nameof(ComponentName));
        }

        /// <summary>
        /// A list of delegates that define whether or not a given job should be ignored.
        /// <para/>
        /// If any delegate in the list returns <c>true</c>, the job will be ignored.
        /// </summary>
        public List<Func<IJobExecutionContext, bool>> IgnorePatterns { get; } = new List<Func<IJobExecutionContext, bool>>();

        /// <summary>
        /// A delegate that returns the OpenTracing "operation name" for the given job.
        /// </summary>
        public Func<string, IJobExecutionContext, string> OperationNameResolver
        {
            get => _operationNameResolver;
            set => _operationNameResolver = value ?? throw new ArgumentNullException(nameof(OperationNameResolver));
        }

        /// <summary>
        /// Whether to add exception details to logs. Defaults to false as they may contain
        /// Personally Identifiable Information (PII), passwords or usernames.
        /// </summary>
        public bool IncludeExceptionDetails { get; set; }
    }
}