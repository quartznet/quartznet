using System;
using System.Runtime.Serialization;

namespace Quartz.Spi
{
    /// <summary>
    /// Result holder for trigger firing event.
    /// </summary>
#if BINARY_SERIALIZATION
    [Serializable]
#endif // BINARY_SERIALIZATION
    [DataContract]
    public class TriggerFiredResult
    {
        [DataMember] private readonly TriggerFiredBundle triggerFiredBundle;
        [DataMember] private readonly Exception exception;

        ///<summary>
        /// Constructor.
        ///</summary>
        ///<param name="triggerFiredBundle"></param>
        public TriggerFiredResult(TriggerFiredBundle triggerFiredBundle)
        {
            this.triggerFiredBundle = triggerFiredBundle;
        }

        ///<summary>
        /// Constructor.
        ///</summary>
        public TriggerFiredResult(Exception exception)
        {
            this.exception = exception;
        }

        ///<summary>
        /// Bundle.
        ///</summary>
        public TriggerFiredBundle TriggerFiredBundle
        {
            get { return triggerFiredBundle; }
        }

        /// <summary>
        /// Possible exception.
        /// </summary>
        public Exception Exception
        {
            get { return exception; }
        }
    }
}