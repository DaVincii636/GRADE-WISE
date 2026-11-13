
using System.Web.Mvc;
using GradeWise.Filters;

namespace GradeWise
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
            filters.Add(new NoCacheAttribute());
        }
    }
}
