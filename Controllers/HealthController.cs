using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jadeer.Exam.Controllers;

[AllowAnonymous]
public class HealthController : Controller
{
    [HttpGet("/health")]
    public IActionResult Get() => Content("ok");
}
