namespace Quartz.Spi;

/// <summary>
/// Result holder for trigger firing event.
/// </summary>
[Serializable]
public sealed class TriggerFiredResult
{
    ///<summary>
    /// Constructor.
    ///</summary>
    ///<param name="triggerFiredBundle"></param>
    public TriggerFiredResult(TriggerFiredBundle? triggerFiredBundle)
    {
        TriggerFiredBundle = triggerFiredBundle;
    }

    ///<summary>
    /// Constructor.
    ///</summary>
    public TriggerFiredResult(Exception exception)
    {
        Exception = exception;
    }

    ///<summary>
    /// Bundle.
    ///</summary>
    public TriggerFiredBundle? TriggerFiredBundle { get; }

    /// <summary>
    /// Possible exception.
    /// </summary>
    public Exception? Exception { get; }
}