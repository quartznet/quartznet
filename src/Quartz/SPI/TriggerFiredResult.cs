using System;

namespace Quartz.Spi
{
    public class TriggerFiredResult
    {
        private readonly TriggerFiredBundle triggerFiredBundle;
        private readonly Exception exception;

        public TriggerFiredResult(TriggerFiredBundle triggerFiredBundle)
        {
            this.triggerFiredBundle = triggerFiredBundle;
        }

        public TriggerFiredResult(Exception exception)
        {
            this.exception = exception;
        }

        public TriggerFiredBundle TriggerFiredBundle
        {
            get { return triggerFiredBundle; }
        }

        public Exception Exception
        {
            get { return exception; }
        }
    }
}