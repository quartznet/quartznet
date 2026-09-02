namespace Quartz;

/// <summary>
/// A trigger, calendar or job data map that could not be written to, or read back out of, the
/// store's JSON.
/// </summary>
/// <remarks>
/// The cause carries what the serializer objected to — most often that no serializer is registered
/// for a trigger or calendar type of the application's own.
/// </remarks>
public sealed class JsonSerializationException : SchedulerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonSerializationException" /> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public JsonSerializationException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonSerializationException" /> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public JsonSerializationException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}