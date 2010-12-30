using System;
using System.Web.Mvc;

namespace Quartz.Web.Views
{
    public class BaseViewPage<T> : ViewPage<T> where T : class
    {
        protected string GetMessage(string key)
        {
            return GetLocalResourceObject(key) + "";
        }
    }
}