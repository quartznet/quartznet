using System.Globalization;
using System.Web;
using System.Web.Compilation;
using System.Web.Mvc;

namespace Quartz.Web.Localization
{
    public static class ResourceExtensions
    {
        public static string Resource(this Controller controller, string expression, params object[] args)
        {
            return GetGlobalResource(expression, args);
        }

        public static string Resource(this HtmlHelper htmlHelper, string expression, params object[] args)
        {
            return GetGlobalResource(expression , args);
        }


        static string GetGlobalResource(string expression, object[] args)
        {
            return string.Format((string)HttpContext.GetGlobalResourceObject("", expression, CultureInfo.CurrentUICulture), args);
        }

    }
}