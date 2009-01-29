using System.Web.Mvc;

using MbUnit.Framework;

using Quartz.Web.Controllers;

namespace Quartz.Tests.Web.Controllers
{
    /// <summary>
    /// Summary description for HomeControllerTest
    /// </summary>
    [TestFixture]
    public class HomeControllerTest
    {
        [Test]
        public void Index()
        {
            // Setup
            HomeController controller = new HomeController();

            // Execute
            ViewResult result = controller.Index() as ViewResult;

            // Verify
            ViewDataDictionary viewData = result.ViewData;

            Assert.AreEqual("Home Page", viewData["Title"]);
            Assert.AreEqual("Welcome to ASP.NET MVC!", viewData["Message"]);
        }

        [Test]
        public void About()
        {
            // Setup
            HomeController controller = new HomeController();

            // Execute
            ViewResult result = controller.About() as ViewResult;

            // Verify
            ViewDataDictionary viewData = result.ViewData;

            Assert.AreEqual("About Page", viewData["Title"]);
        }
    }
}