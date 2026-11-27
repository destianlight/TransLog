using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace TransLog.Web;

/// <summary>
/// Login details shown once after a user is added or given a new password, with a Copy button,
/// so the admin can paste them into WhatsApp, SMS or email. The password is never stored.
/// </summary>
public record LoginDetails(string FullName, string? CompanyName, string LoginUrl, string Email, string Password)
{
    private const string Key = "LoginDetails";

    public string Text =>
        $"Hi {FullName},\n\n" +
        (CompanyName is null ? "You have a TransLog login.\n\n" : $"You have a TransLog login for {CompanyName}.\n\n") +
        $"Log in: {LoginUrl}\n" +
        $"Log in with: {Email}\n" +
        $"Password: {Password}\n\n" +
        "You can change your password under Account after you log in.";

    /// <summary>Keeps the details for the next page (after the redirect). Read once, then gone.</summary>
    public static void Keep(PageModel page, string fullName, string? companyName, string email, string password)
    {
        var url = page.Url.Page("/Login", pageHandler: null, values: new { area = "" }, protocol: page.Request.Scheme) ?? "/login";
        page.TempData[Key] = JsonSerializer.Serialize(new LoginDetails(fullName, companyName, url, email, password));
    }

    /// <summary>Reads and removes the details kept by <see cref="Keep"/>; null when there are none.</summary>
    public static LoginDetails? Take(ITempDataDictionary tempData) =>
        tempData[Key] is string json ? JsonSerializer.Deserialize<LoginDetails>(json) : null;
}
