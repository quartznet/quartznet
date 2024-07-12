namespace Quartz.Tests.Unit;

[SetUpFixture]
public class TestAssemblySetup
{
    [OneTimeSetUp]
    public void SetUp()
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
    }
}