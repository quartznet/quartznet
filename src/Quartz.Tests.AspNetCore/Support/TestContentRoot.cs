namespace Quartz.Tests.AspNetCore.Support;

/// <summary>
/// Where <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}" /> should think
/// this test assembly's application lives.
/// </summary>
/// <remarks>
/// The factory otherwise derives the content root from a build-time manifest, which does not survive
/// every way this suite is run. Setting
/// <c>ASPNETCORE_TEST_CONTENTROOT_QUARTZ_TESTS_ASPNETCORE</c> to the answer is the documented override,
/// and the answer is found by walking up to the project rather than hard-coded, so it holds whatever the
/// output path is.
/// </remarks>
internal static class TestContentRoot
{
    internal const string EnvironmentVariable = "ASPNETCORE_TEST_CONTENTROOT_QUARTZ_TESTS_ASPNETCORE";

    /// <summary>
    /// Points the factory's content root at this test project, and answers where that is.
    /// </summary>
    public static string Apply()
    {
        string contentRoot = Resolve();
        Environment.SetEnvironmentVariable(EnvironmentVariable, contentRoot);
        return contentRoot;
    }

    private static string Resolve()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string projectFilePath = Path.Combine(directory.FullName, "src", "Quartz.Tests.AspNetCore", "Quartz.Tests.AspNetCore.csproj");
            if (File.Exists(projectFilePath))
            {
                return Path.Combine(directory.FullName, "src", "Quartz.Tests.AspNetCore");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate content root from base path {AppContext.BaseDirectory}.");
    }
}
