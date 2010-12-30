using System.Web.Mvc;

namespace Quartz.Web.Localization
{
    public class LocalizationWebFormViewEngine : WebFormViewEngine
    {
        protected override IView CreateView(ControllerContext controllerContext, string viewPath, string masterPath)
        {
            return new LocalizationWebFormView(viewPath, masterPath);
        }

        protected override IView CreatePartialView(ControllerContext controllerContext, string partialPath)
        {
            return new LocalizationWebFormView(partialPath, null);
        }
    }
}