using Microsoft.AspNetCore.Mvc;
using Portal.Web.Models;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;

namespace Portal.Web.Controllers
{
    public class HomeController : Controller
    {
        [Authorize]
        [ResponseCache(
     Location = ResponseCacheLocation.None,
     NoStore = true)]
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
