namespace Quartz.Tests.Integration;

[SetUpFixture]
public class TestAssemblySetup
{
    [OneTimeSetUp]
    public async Task SetUp()
    {
        // set default directory to make sure file loading works
        // (https://youtrack.jetbrains.com/issue/RSRP-451142)
        string codeBase = GetType().Assembly.Location;
        string pathToUse = codeBase;
        if (!codeBase.StartsWith('/'))
        {
            UriBuilder uri = new UriBuilder(codeBase);
            string path = Uri.UnescapeDataString(uri.Path);
            pathToUse = path;
        }

        pathToUse = Path.GetDirectoryName(pathToUse);
        Directory.SetCurrentDirectory(pathToUse);

        await TestcontainersDatabaseEnvironment.InitializeAsync();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        await TestcontainersDatabaseEnvironment.DisposeAsync();
    }
}
