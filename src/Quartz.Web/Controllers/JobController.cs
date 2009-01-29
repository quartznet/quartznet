using System.Web.Mvc;

namespace Quartz.Web.Controllers
{
    [HandleError]
    public class JobController : Controller
    {
        
        public ActionResult List()
        {


            return new ViewResult();
        }

    }
}