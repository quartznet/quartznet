namespace Quartz;

/// <summary>
/// Strongly typed configuration for a single named ADO.NET data source.
/// </summary>
/// <remarks>
/// Registered as named options keyed by data source name, so a scheduler with several data sources
/// configures each one under its own name. Typed replacement for the
/// <c>quartz.dataSource.&lt;name&gt;.*</c> property keys.
/// </remarks>
public sealed class DataSourceOptions
{
    /// <summary>
    /// The Quartz provider name identifying the ADO.NET driver, for example <c>SqlServer</c> or <c>Npgsql</c>.
    /// </summary>
    public string Provider { get; set; } = "";

    /// <summary>
    /// The connection string used to reach the database.
    /// </summary>
    /// <remarks>
    /// Takes precedence over <see cref="ConnectionStringName"/> when both are set.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The name of a connection string to resolve from <c>IConfiguration</c>'s connection strings.
    /// </summary>
    public string? ConnectionStringName { get; set; }
}
