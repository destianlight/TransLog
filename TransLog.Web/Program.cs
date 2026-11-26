using Microsoft.AspNetCore.Localization;
using TransLog.Web;

// South Africa only: one fixed culture for the whole app (see AppCulture.cs).
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = AppCulture.ZA;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = AppCulture.ZA;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.DefaultRequestCulture = new RequestCulture(AppCulture.ZA);
    o.SupportedCultures = [AppCulture.ZA];
    o.SupportedUICultures = [AppCulture.ZA];
    o.RequestCultureProviders.Clear(); // the visitor's browser language cannot change it
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseRequestLocalization();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();
