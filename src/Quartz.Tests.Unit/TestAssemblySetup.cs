using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace Quartz.Tests.Unit
{
    [SetUpFixture]
    public class TestAssemblySetup
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            // set default directory to make sure file loading works
            // (https://youtrack.jetbrains.com/issue/RSRP-451142) 
            string codeBase = Assembly.GetExecutingAssembly().CodeBase;
            UriBuilder uri = new UriBuilder(codeBase);
            string path = Uri.UnescapeDataString(uri.Path);
            var pathToUse = Path.GetDirectoryName(path);

            Directory.SetCurrentDirectory(pathToUse);
        }
    }
}