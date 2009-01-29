using System.IO;
using System.Web.Mvc;

namespace Quartz.Web.Localization
{
    public class LocalizationWebFormView : WebFormView
    {
        internal const string ViewPathKey = "__ViewPath__";

        public LocalizationWebFormView(string viewPath) : base(viewPath)
        {
        }

        public LocalizationWebFormView(string viewPath, string masterPath) : base(viewPath, masterPath)
        {
        }

        public override void Render(ViewContext viewContext, TextWriter writer)
        {
            // there seems to be a bug with RenderPartial tainting the page's view data
            // so we should capture the current view path, and revert back after rendering
            string originalViewPath = (string) viewContext.ViewData[ViewPathKey];
            
            viewContext.ViewData[ViewPathKey] = ViewPath;
            base.Render(viewContext, writer);
            
            viewContext.ViewData[ViewPathKey] = originalViewPath;
        }
    }
}