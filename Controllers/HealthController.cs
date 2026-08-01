using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jadeer.Exam.Data;

namespace Jadeer.Exam.Controllers;

[AllowAnonymous]
public class HealthController : Controller
{
    private readonly AppDbContext _db;

    public HealthController(AppDbContext db) => _db = db;

    /// <summary>Liveness for Railway — must stay anonymous and not depend on MySQL.</summary>
    [HttpGet("/health")]
    public IActionResult Get() => Content("ok", "text/plain");

    /// <summary>Quick DB check (anonymous) to diagnose login/seed issues.</summary>
    [HttpGet("/health/db")]
    public async Task<IActionResult> Db()
    {
        try
        {
            var can = await _db.Database.CanConnectAsync();
            if (!can)
                return Json(new { ok = false, database = "unreachable" });

            var users = await _db.Users.CountAsync();
            return Json(new { ok = true, database = "ok", users });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, database = "error", message = ex.Message });
        }
    }
}
