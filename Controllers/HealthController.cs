using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jadeer.Exam.Controllers;

[AllowAnonymous]
[Route("health")]
public class HealthController : Controller
{
    /// <summary>Liveness for Railway — must stay anonymous and not depend on MySQL.</summary>
    [HttpGet]
    [HttpGet("/health")]
    public IActionResult Get() => Content("ok", "text/plain");
}
