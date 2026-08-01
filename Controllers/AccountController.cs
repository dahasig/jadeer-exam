using System.Security.Claims;
using Jadeer.Exam.Data;
using Jadeer.Exam.Models;
using Jadeer.Exam.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jadeer.Exam.Controllers;

public class AccountController : AppControllerBase
{
    private const string MfaPendingKey = "MfaPendingUser";
    private const string MfaSetupSecretKey = "MfaSetupSecret";

    private readonly AppDbContext _db;
    private readonly PasswordHasher<UserAccount> _passwordHasher;
    private readonly MfaService _mfa;
    private readonly AuditLogger _audit;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        AppDbContext db,
        PasswordHasher<UserAccount> passwordHasher,
        MfaService mfa,
        AuditLogger audit,
        ILogger<AccountController> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _mfa = mfa;
        _audit = audit;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpGet("/Account/Login")]
    [HttpGet("/login")]
    public async Task<IActionResult> Login()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        await TryEnsureDemoUsersAsync();
        ViewBag.Oauth2Enabled = false;
        return View();
    }

    [AllowAnonymous]
    [HttpPost("/Account/Login")]
    [HttpPost("/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string username, string password)
    {
        username = (username ?? "").Trim();
        ViewBag.Oauth2Enabled = false;

        try
        {
            await TryEnsureDemoUsersAsync();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null
                || _passwordHasher.VerifyHashedPassword(user, user.Password, password ?? "")
                   == PasswordVerificationResult.Failed)
            {
                _audit.LoginFailure(username, ClientIp);
                ViewBag.Error = "invalid";
                return View();
            }

            if (user.MfaEnabled && !string.IsNullOrWhiteSpace(user.MfaSecret))
            {
                HttpContext.Session.SetString(MfaPendingKey, user.Username);
                return RedirectToAction(nameof(MfaVerify));
            }

            await SignInAsync(user);
            _audit.LoginSuccess(user.Username, ClientIp);
            return RedirectAfterLogin(user.Role);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for {Username}", username);
            ViewBag.Error = "database";
            return View();
        }
    }

    private async Task TryEnsureDemoUsersAsync()
    {
        try
        {
            if (!await _db.Database.CanConnectAsync())
                return;
            await _db.Database.EnsureCreatedAsync();
            if (await _db.Users.AnyAsync())
                return;
            DbSeeder.SeedUsersOnly(_db, _passwordHasher);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not ensure demo users before login.");
        }
    }

    [AllowAnonymous]
    [HttpGet("/Account/MfaVerify")]
    public IActionResult MfaVerify()
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString(MfaPendingKey)))
            return RedirectToAction(nameof(Login));
        return View();
    }

    [AllowAnonymous]
    [HttpPost("/Account/MfaVerify")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MfaVerify(string code)
    {
        var pending = HttpContext.Session.GetString(MfaPendingKey);
        if (string.IsNullOrEmpty(pending))
            return RedirectToAction(nameof(Login));

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == pending);
        if (user == null || !_mfa.VerifyCode(user.MfaSecret, code))
        {
            ViewBag.Error = "invalid";
            return View();
        }

        HttpContext.Session.Remove(MfaPendingKey);
        await SignInAsync(user);
        _audit.LoginSuccess(user.Username, ClientIp);
        return RedirectAfterLogin(user.Role);
    }

    [Authorize]
    [HttpGet("/Account/MfaSetup")]
    public IActionResult MfaSetup()
    {
        var secret = HttpContext.Session.GetString(MfaSetupSecretKey);
        if (string.IsNullOrEmpty(secret))
        {
            secret = _mfa.GenerateSecret();
            HttpContext.Session.SetString(MfaSetupSecretKey, secret);
        }

        ViewBag.Secret = secret;
        ViewBag.OtpAuthUri = _mfa.GetOtpAuthUri(CurrentUsername, secret);
        ViewBag.MfaEnabled = _db.Users.AsNoTracking()
            .FirstOrDefault(u => u.Username == CurrentUsername)?.MfaEnabled == true;
        return View();
    }

    [Authorize]
    [HttpPost("/Account/MfaSetup")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MfaSetup(string code)
    {
        var secret = HttpContext.Session.GetString(MfaSetupSecretKey);
        if (string.IsNullOrEmpty(secret) || !_mfa.VerifyCode(secret, code))
        {
            ViewBag.Secret = secret ?? _mfa.GenerateSecret();
            ViewBag.OtpAuthUri = _mfa.GetOtpAuthUri(CurrentUsername, (string)ViewBag.Secret);
            ViewBag.Error = "invalid";
            return View();
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == CurrentUsername);
        if (user == null)
            return RedirectToAction(nameof(Login));

        user.MfaSecret = secret;
        user.MfaEnabled = true;
        await _db.SaveChangesAsync();
        HttpContext.Session.Remove(MfaSetupSecretKey);
        ViewBag.Success = true;
        ViewBag.MfaEnabled = true;
        ViewBag.Secret = secret;
        ViewBag.OtpAuthUri = _mfa.GetOtpAuthUri(CurrentUsername, secret);
        return View();
    }

    [Authorize]
    [HttpPost("/Account/Logout")]
    [HttpPost("/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        _audit.Logout(CurrentUsername, ClientIp);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();
        return RedirectToAction(nameof(Login));
    }

    private async Task SignInAsync(UserAccount user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        if (!string.IsNullOrWhiteSpace(user.FullName))
            claims.Add(new Claim("full_name", user.FullName));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = true
            });
    }

    private IActionResult RedirectAfterLogin(Role role) => role switch
    {
        Role.GRADER => Redirect("/admin/grading"),
        Role.ADMIN or Role.EXAMINER => Redirect("/admin/operations"),
        Role.CANDIDATE => Redirect("/dashboard"),
        _ => Redirect("/dashboard")
    };
}
