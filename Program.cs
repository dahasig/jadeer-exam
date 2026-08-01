using System.Globalization;
using Jadeer.Exam.Data;
using Jadeer.Exam.Localization;
using Jadeer.Exam.Models;
using Jadeer.Exam.Security;
using Jadeer.Exam.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? "Server=127.0.0.1;Port=3306;Database=jadeer_exam;User=root;Password=;";
var sessionMinutes = builder.Configuration.GetValue("Jadeer:Security:SessionTimeoutMinutes", 15);

builder.Services.AddLocalization();
builder.Services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();
builder.Services.AddControllersWithViews()
    .AddViewLocalization();

// MySQL 8.x (Pomelo). Create DB first: CREATE DATABASE jadeer_exam CHARACTER SET utf8mb4;
var mySqlVersion = new MySqlServerVersion(new Version(8, 0, 36));
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, mySqlVersion));

builder.Services.AddSingleton<PasswordHasher<UserAccount>>();
builder.Services.AddScoped<QuestionImageStorage>();
builder.Services.AddScoped<ExamSessionService>();
builder.Services.AddScoped<ExamNotificationService>();
builder.Services.AddScoped<ExamService>();
builder.Services.AddScoped<MonitoringService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddSingleton<AuditLogger>();
builder.Services.AddSingleton<MfaService>();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(sessionMinutes);
    options.Cookie.Name = "RCEXAMSESSION.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.IsEssential = true;
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "RCEXAMSESSION";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
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
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.HttpOnly = true;
});

var app = builder.Build();

DbSeeder.Seed(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseMiddleware<SecurityHeadersMiddleware>();

if (!app.Environment.IsDevelopment()
    && app.Configuration.GetValue("Jadeer:Security:HstsEnabled", true))
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

var supportedCultures = new[] { "ar", "en" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("ar")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);
// Do not use Accept-Language — browser English was overriding Arabic on every navigation.
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

app.Run();
