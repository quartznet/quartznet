using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

using NUnit.Framework;

namespace Quartz.Tests.Integration;

[SetUpFixture]
public class TestAssemblySetup
{
    [OneTimeSetUp]
    public async Task SetUp()
    {
        // set a default directory to make sure file loading works
        // (https://youtrack.jetbrains.com/issue/RSRP-451142)
        string codeBase = GetType().GetTypeInfo().Assembly.Location;
        string pathToUse = codeBase;
        if (!codeBase.StartsWith("/"))
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
