using System.Web.Mvc;

using Common.Logging;

namespace Quartz.Web.Controllers
{
    public class BaseController : Controller
    {
        private readonly ILog log;

        public BaseController()
        {
            log = LogManager.GetLogger(GetType());
        }

        protected ILog Log
        {
            get { return log; }
        }
    }
}