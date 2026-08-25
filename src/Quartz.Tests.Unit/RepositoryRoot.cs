namespace Quartz.Tests.Unit;

/// <summary>
/// Where the repository begins, for the tests that assert something about the tree rather than about
/// the code in it.
/// </summary>
internal static class RepositoryRoot
{
    /// <summary>
    /// Walks up from the test assembly's directory to the directory holding <c>Quartz.slnx</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The assembly is running from somewhere with no solution file above it, so there is no tree to
    /// look at.
    /// </exception>
    public static DirectoryInfo Find()
    {
        DirectoryInfo directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Quartz.slnx")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException(
            $"No Quartz.slnx above {AppContext.BaseDirectory}, so the repository root cannot be found.");
    }
}
