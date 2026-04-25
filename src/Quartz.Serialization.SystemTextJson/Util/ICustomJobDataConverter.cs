namespace Quartz.Util;

/// <summary>
/// Can be used to convert more complex objects in <see cref="JobDataMap"/>s.
/// </summary>
public interface ICustomJobDataConverter
{
    /// <summary>
    /// Whether this converter handles properties with the given name.
    /// </summary>
    /// <param name="propertyName">The job data property name to check</param>
    /// <returns><see langword="true"/> if this converter handles the given <paramref name="propertyName"/>,
    /// <see langword="false"/> if not.</returns>
    bool HandlesProperty(string propertyName);
}
