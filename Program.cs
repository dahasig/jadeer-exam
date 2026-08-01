using System.Globalization;
using Jadeer.Exam.Data;
using Jadeer.Exam.Localization;
using Jadeer.Exam.Models;
using Jadeer.Exam.Security;
using Jadeer.Exam.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

var builder = WebApplication.CreateBuilder(args);

// Railway / cloud: bind to $PORT
var listenPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(listenPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{listenPort}");
}

var connectionString = ResolveMysqlConnectionString(builder.Configuration);
var sessionMinutes = builder.Configuration.GetValue("Jadeer:Security:SessionTimeoutMinutes", 15);

builder.Services.AddLocalization();
builder.Services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();
builder.Services.AddControllersWithViews()
    .AddViewLocalization();

var mySqlVersion = new MySqlServerVersion(new Version(8, 0, 36));
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, mySqlVersion, mySql =>
    {
        mySql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(3), null);
    }));

builder.Services.AddSingleton<PasswordHasher<UserAccount>>();
builder.Services.AddScoped<QuestionImageStorage>();
builder.Services.AddScoped<ExamSessionService>();
builder.Services.AddScoped<ExamNotificationService>();
builder.Services.AddScoped<ExamService>();
builder.Services.AddScoped<MonitoringService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddSingleton<AuditLogger>();
builder.Services.AddSingleton<MfaService>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(sessionMinutes);
    options.Cookie.Name = "RCEXAMSESSION.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.IsEssential = true;
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "RCEXAMSESSION";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(sessionMinutes);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.LogoutPath = "/logout";
        options.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/monitoring")
                || ctx.Request.Path.StartsWithSegments("/exam"))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ADMIN", p => p.RequireRole(nameof(Role.ADMIN)));
    options.AddPolicy("EXAMINER", p => p.RequireRole(nameof(Role.EXAMINER)));
    options.AddPolicy("GRADER", p => p.RequireRole(nameof(Role.GRADER)));
    options.AddPolicy("CANDIDATE", p => p.RequireRole(nameof(Role.CANDIDATE)));
    options.AddPolicy("Staff", p => p.RequireRole(nameof(Role.ADMIN), nameof(Role.EXAMINER)));
    options.AddPolicy("Grading", p => p.RequireRole(nameof(Role.ADMIN), nameof(Role.EXAMINER), nameof(Role.GRADER)));
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.Name = "RCEXAM.AF";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.HttpOnly = true;
});

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
logger.LogInformation("MySQL host hint: {Hint}", ConnectionHostHint(connectionString));

// لا نمنع فتح المنفذ بسبب MySQL — وإلا يفشل Healthcheck على Railway
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(() =>
    {
        try
        {
            if (!DbSeeder.TrySeed(app.Services, logger))
            {
                logger.LogCritical(
                    "Database seed failed. On Railway: add MySQL and set ConnectionStrings__DefaultConnection " +
                    "or MYSQLHOST/MYSQLUSER/MYSQLPASSWORD/MYSQLDATABASE.");
            }
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Unexpected database seed failure.");
        }
    });
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();

if (!app.Environment.IsDevelopment()
    && app.Configuration.GetValue("Jadeer:Security:HstsEnabled", true))
{
    app.UseHsts();
}

// Behind Railway TLS terminator — avoid forcing HTTPS redirect loops on health checks
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

var supportedCultures = new[] { "ar", "en" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("ar")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);
localizationOptions.RequestCultureProviders.Clear();
localizationOptions.RequestCultureProviders.Add(
    new QueryStringRequestCultureProvider { QueryStringKey = "lang", UIQueryStringKey = "lang" });
localizationOptions.RequestCultureProviders.Add(
    new CookieRequestCultureProvider { CookieName = "jadeer_lang" });
app.UseRequestLocalization(localizationOptions);

app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// /health عبر HealthController مع [AllowAnonymous] — لا تضف MapGet هنا لأنه يخضع لـ FallbackPolicy

app.Run();

static string ResolveMysqlConnectionString(IConfiguration config)
{
    // Explicit connection string (Railway Variables / appsettings)
    var direct = FirstNonEmpty(
        config.GetConnectionString("DefaultConnection"),
        Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"),
        Environment.GetEnvironmentVariable("MYSQL_URL"),
        Environment.GetEnvironmentVariable("DATABASE_URL"),
        Environment.GetEnvironmentVariable("JADEER_MYSQL"));

    if (!string.IsNullOrWhiteSpace(direct) && !LooksLikePlaceholder(direct))
    {
        if (direct.StartsWith("mysql://", StringComparison.OrdinalIgnoreCase)
            || direct.StartsWith("mysql2://", StringComparison.OrdinalIgnoreCase))
        {
            return MysqlUriToConnectionString(direct);
        }

        return EnsureSslDefaults(direct);
    }

    // Railway MySQL plugin discrete variables
    var host = FirstNonEmpty(
        Environment.GetEnvironmentVariable("MYSQLHOST"),
        Environment.GetEnvironmentVariable("MYSQL_HOST"));
    if (!string.IsNullOrWhiteSpace(host))
    {
        var user = FirstNonEmpty(
            Environment.GetEnvironmentVariable("MYSQLUSER"),
            Environment.GetEnvironmentVariable("MYSQL_USER")) ?? "root";
        var pass = FirstNonEmpty(
            Environment.GetEnvironmentVariable("MYSQLPASSWORD"),
            Environment.GetEnvironmentVariable("MYSQL_ROOT_PASSWORD"),
            Environment.GetEnvironmentVariable("MYSQL_PASSWORD")) ?? "";
        var database = FirstNonEmpty(
            Environment.GetEnvironmentVariable("MYSQLDATABASE"),
            Environment.GetEnvironmentVariable("MYSQL_DATABASE")) ?? "jadeer_exam";
        var port = FirstNonEmpty(
            Environment.GetEnvironmentVariable("MYSQLPORT"),
            Environment.GetEnvironmentVariable("MYSQL_PORT")) ?? "3306";

        return EnsureSslDefaults(
            $"Server={host};Port={port};Database={database};User={user};Password={pass};");
    }

    return EnsureSslDefaults(
        "Server=127.0.0.1;Port=3306;Database=jadeer_exam;User=jadeer;Password=CHANGE_ME;");
}

static bool LooksLikePlaceholder(string cs) =>
    cs.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
    && (cs.Contains("127.0.0.1", StringComparison.Ordinal)
        || cs.Contains("localhost", StringComparison.OrdinalIgnoreCase));

static string EnsureSslDefaults(string cs)
{
    if (cs.Contains("SslMode=", StringComparison.OrdinalIgnoreCase))
        return cs;
    // Preferred works for local; Required often needed for managed MySQL
    var ssl = Environment.GetEnvironmentVariable("MYSQL_SSL_MODE") ?? "Preferred";
    return cs.TrimEnd(';') + $";SslMode={ssl};AllowPublicKeyRetrieval=True;";
}

static string MysqlUriToConnectionString(string uriText)
{
    var uri = new Uri(uriText);
    var userInfo = uri.UserInfo.Split(':', 2);
    var user = Uri.UnescapeDataString(userInfo[0]);
    var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
    var database = uri.AbsolutePath.Trim('/');
    if (string.IsNullOrWhiteSpace(database))
        database = "jadeer_exam";
    var port = uri.Port > 0 ? uri.Port : 3306;
    return EnsureSslDefaults(
        $"Server={uri.Host};Port={port};Database={database};User={user};Password={pass};");
}

static string? FirstNonEmpty(params string?[] values) =>
    values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

static string ConnectionHostHint(string cs)
{
    try
    {
        foreach (var part in cs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("Server", StringComparison.OrdinalIgnoreCase))
                return kv[1];
        }
    }
    catch
    {
        /* ignore */
    }

    return "(unknown)";
}
