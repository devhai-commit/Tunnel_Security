using Microsoft.AspNetCore.Mvc;

namespace BackendV2.Controllers
{
    public class NodeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
