using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using TransLog.Web;
using TransLog.Web.Captures;
using TransLog.Web.Data;
using TransLog.Web.Security;
using TransLog.Web.Storage;

// South Africa only: one fixed culture for the whole app (see AppCulture.cs).
CultureInfo.DefaultThreadCurrentCulture = AppCulture.ZA;
CultureInfo.DefaultThreadCurrentUICulture = AppCulture.ZA;

// A crash during startup is written to logs\startup-error.txt next to the app (the stdout log can stay empty on IIS).
AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteStartupError(e.ExceptionObject);

var builder = WebApplication.CreateBuilder(args);

// ---- Database: the connection string lives only on the server (appsettings.Production.json) ----
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("Default");
    if (string.IsNullOrWhiteSpace(cs))
        throw new InvalidOperationException("ConnectionStrings:Default is not set. Put it in appsettings.Production.json on the server.");
    options.UseSqlServer(cs, sql => sql.UseCompatibilityLevel(150)); // SQL Server 2019 behaviour
});

// ---- Login ----
builder.Services
    .AddIdentity<AppUser, AppRole>(o =>
    {
        // Simple passwords: at least 6 characters, no other rules.
        o.Password.RequiredLength = 6;
        o.Password.RequiredUniqueChars = 1;
        o.Password.RequireDigit = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;

        // 5 wrong tries locks the account for 5 minutes.
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        o.Lockout.AllowedForNewUsers = true;

        // Drivers may have no email, so Identity's own email check is off; AppUserValidator checks that
        // emails and cell numbers are unique (Part 3c).
        o.User.RequireUniqueEmail = false;
        o.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders()
    .AddUserValidator<AppUserValidator>()
    .AddClaimsPrincipalFactory<AppClaimsFactory>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "translog.auth";
    o.LoginPath = "/login";
    o.LogoutPath = "/logout";
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    o.SlidingExpiration = true;
    // Logged in but not allowed (e.g. an Owner opening /sysadmin): answer "not found".
    o.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    };
});

// The login cookie is checked against the database every minute (setting Login:RecheckSeconds, default 60).
// A deactivated user or company, a changed password, or a login on another device logs this one out
// within that time: each login gets a new security stamp (Login.cshtml.cs), so one account = one device.
builder.Services.AddOptions<SecurityStampValidatorOptions>()
    .Configure<IConfiguration>((o, config) =>
        o.ValidationInterval = TimeSpan.FromSeconds(Math.Max(0, config.GetValue("Login:RecheckSeconds", 60))));

// ---- Keys that lock the login cookie ----
// Saved as files in App_Data/keys (setting DataProtection:KeysPath), so logins survive app restarts and
// deploys ("Keep me logged in"). Without this the keys live only in memory and every app pool recycle
// on Plesk logs everybody out. Deploy never deletes App_Data; IIS never serves it. Never commit the keys.
builder.Services.AddDataProtection().SetApplicationName("TransLog");
builder.Services.AddOptions<KeyManagementOptions>()
    .Configure<IConfiguration, IWebHostEnvironment, ILoggerFactory>((o, config, env, logs) =>
    {
        var path = config["DataProtection:KeysPath"];
        var folder = string.IsNullOrWhiteSpace(path) ? Path.Combine(env.ContentRootPath, "App_Data", "keys") : path;
        o.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(folder), logs);
    });

// ---- Who may open what: everything needs a login unless a page says [AllowAnonymous] ----
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Policies.SysAdmin, p => p.RequireRole(Roles.SysAdmin))
    .AddPolicy(Policies.CompanyMember, p => p.RequireRole(Roles.Company).RequireClaim(AppClaims.CompanyId))
    .AddPolicy(Policies.CompanyOwner, p => p.RequireRole(Roles.Owner).RequireClaim(AppClaims.CompanyId))
    .AddPolicy(Policies.CompanyAdmin, p => p.RequireRole(Roles.Owner, Roles.Admin).RequireClaim(AppClaims.CompanyId))
    .AddPolicy(Policies.CompanyCapture, p => p.RequireRole(Roles.Owner, Roles.Admin, Roles.Capturer).RequireClaim(AppClaims.CompanyId))
    .AddPolicy(Policies.DriverMember, p => p.RequireRole(Roles.Driver).RequireClaim(AppClaims.CompanyId));

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeAreaFolder("SysAdmin", "/", Policies.SysAdmin);
    o.Conventions.AuthorizeAreaFolder("Company", "/", Policies.CompanyMember);
    o.Conventions.AuthorizeAreaFolder("Driver", "/", Policies.DriverMember);
    // Only the Owner manages users and settings (both rules apply).
    o.Conventions.AuthorizeAreaFolder("Company", "/Users", Policies.CompanyOwner);
    o.Conventions.AuthorizeAreaPage("Company", "/Settings", Policies.CompanyOwner);
    o.Conventions.AuthorizeAreaPage("Company", "/Activity", Policies.CompanyOwner);
    // The Owner and Admins manage drivers and vehicles.
    o.Conventions.AuthorizeAreaFolder("Company", "/Drivers", Policies.CompanyAdmin);
    o.Conventions.AuthorizeAreaFolder("Company", "/Vehicles", Policies.CompanyAdmin);
    o.Conventions.AuthorizeAreaFolder("Company", "/Links", Policies.CompanyAdmin);
    // Office capture (Part 4b): the Owner, Admins and Capturers add photos; every office role sees the Captures list.
    o.Conventions.AuthorizeAreaPage("Company", "/Capture", Policies.CompanyCapture);
});
builder.Services.AddRouting(o => o.LowercaseUrls = true);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditLogger>();
builder.Services.AddScoped<CurrentCompany>();
builder.Services.AddScoped<TransLog.Web.Areas.CompanyArea.LinkService>();
builder.Services.AddScoped<AreaBadges>();
// Part 4a: uploaded photos on disk (outside wwwroot) and saving captures.
builder.Services.AddSingleton<IFileStore, LocalFileStore>();
builder.Services.AddScoped<CaptureService>();
// Part 4c: driver profile photos.
builder.Services.AddScoped<TransLog.Web.Fleet.DriverPhotos>();

builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.DefaultRequestCulture = new RequestCulture(AppCulture.ZA);
    o.SupportedCultures = [AppCulture.ZA];
    o.SupportedUICultures = [AppCulture.ZA];
    o.RequestCultureProviders.Clear(); // the visitor's browser language cannot change it
});

var app = builder.Build();

// Migrations, roles and the first system admin.
try
{
    await DbInitializer.RunAsync(app.Services, app.Configuration, app.Logger);
}
catch (Exception ex)
{
    WriteStartupError(ex);
    throw;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseRequestLocalization();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages().WithStaticAssets();
app.MapPhotoFiles();

app.Run();

static void WriteStartupError(object error)
{
    try
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(folder);
        File.AppendAllText(Path.Combine(folder, "startup-error.txt"),
            $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
    }
    catch
    {
        // Nowhere left to write it.
    }
}

// Lets the test project start the app.
public partial class Program { }
