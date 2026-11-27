using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.CompanyArea.Pages.Drivers;

/// <summary>
/// The Owner or an Admin edits a driver of their own company, links the driver to a vehicle and gives the driver a login.
/// A driver of another company answers "not found", even when the id is typed into the address.
/// </summary>
public class EditModel(AppDbContext db, AuditLogger audit, UserManager<AppUser> users, LinkService links) : PageModel
{
    [BindProperty]
    public DriverForm Form { get; set; } = new();

    public int Id { get; private set; }

    public string DriverName { get; private set; } = "";

    public bool HasLogin { get; private set; }

    /// <summary>The saved email, which is also the login.</summary>
    public string? SavedEmail { get; private set; }

    public CurrentLinkInfo? Current { get; private set; }

    public List<SelectListItem> VehicleChoices { get; private set; } = [];

    public TakeOverInfo? TakeOver { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (await LoadAsync(id) is not { } driver)
            return NotFound();

        Form = DriverForm.From(driver);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (await LoadAsync(id) is not { } driver)
            return NotFound();

        // The email is the login once there is one, so it cannot be changed here.
        if (driver.UserId is not null)
            Form.Email = driver.Email;

        var (cell, idNumber) = await FleetChecks.CheckDriverAsync(db, ModelState, Form, driver.Id);
        if (!ModelState.IsValid)
            return Page();

        var changes = new List<string>();
        if (driver.Cell != cell)
            changes.Add($"cell {driver.Cell} -> {cell}");
        if (driver.IsActive != Form.IsActive)
            changes.Add(Form.IsActive ? "switched on" : "switched off");
        var switchedOff = driver.IsActive && !Form.IsActive;

        driver.Cell = cell;
        driver.IdNumber = idNumber;
        Form.CopyTo(driver);

        // The login follows the driver: off when the driver is off, on again when the driver is back.
        if (driver.UserId is int userId
            && await users.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture)) is { } login
            && login.IsActive != Form.IsActive)
        {
            login.IsActive = Form.IsActive;
            await users.UpdateAsync(login);
            await users.UpdateSecurityStampAsync(login); // logged out within 5 minutes
        }

        // The ID number is never written to the activity log.
        audit.Add("DriverUpdated", $"{driver.FullName}: {(changes.Count == 0 ? "details" : string.Join(", ", changes))}", driver.CompanyId);
        await db.SaveChangesAsync();

        if (switchedOff)
            await links.UnlinkDriverAsync(driver.Id);

        TempData["Message"] = $"{driver.FullName} saved.";
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostLinkAsync(int id, int vehicleId, bool confirm)
    {
        if (await LoadAsync(id) is not { } driver)
            return NotFound();
        if (vehicleId == 0)
        {
            TempData["Error"] = "Choose a vehicle.";
            return RedirectToPage(new { id });
        }

        var outcome = await links.LinkAsync(driver.Id, vehicleId, LinkSource.Company, confirm);
        switch (outcome.Result)
        {
            case LinkResult.NotFound:
                return NotFound();
            case LinkResult.TakenByOther:
                TakeOver = await links.TakeOverAsync(vehicleId, outcome);
                // The empty driver form was bound and checked on this post; clear that so the saved details show.
                ModelState.Clear();
                Form = DriverForm.From(driver);
                return Page();
            case LinkResult.Inactive:
                TempData["Error"] = "The driver and the vehicle must both be switched on.";
                break;
            case LinkResult.AlreadyLinked:
                TempData["Message"] = "Already linked to that vehicle.";
                break;
            default:
                TempData["Message"] = $"{driver.FullName} is now linked.";
                break;
        }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUnlinkAsync(int id)
    {
        if (await LoadAsync(id) is not { } driver)
            return NotFound();

        TempData["Message"] = await links.UnlinkDriverAsync(driver.Id) ? $"{driver.FullName} is no longer linked." : "There was no link.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostGiveLoginAsync(int id, string? password)
    {
        if (await LoadAsync(id) is not { } driver)
            return NotFound();

        if (driver.UserId is not null)
            TempData["Error"] = "This driver already has a login.";
        else if (!driver.IsActive)
            TempData["Error"] = "Switch the driver on first.";
        else if (string.IsNullOrWhiteSpace(driver.Email))
            TempData["Error"] = "Add the driver's email and save first. The email is the login.";
        else
        {
            var email = driver.Email.Trim();
            var user = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = driver.FullName,
                CompanyId = driver.CompanyId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };

            var result = await users.CreateAsync(user, password ?? "");
            if (result.Succeeded)
            {
                result = await users.AddToRoleAsync(user, Roles.Capturer);
                if (!result.Succeeded)
                    await users.DeleteAsync(user); // no half-made login without a role
            }
            if (!result.Succeeded)
                TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description));
            else
            {
                driver.UserId = user.Id;
                audit.Add("DriverLoginCreated", $"{driver.FullName} ({email})", driver.CompanyId);
                await db.SaveChangesAsync();

                var companyName = (await db.Companies.FindAsync(driver.CompanyId))?.Name;
                LoginDetails.Keep(this, driver.FullName, companyName, email, password!);
                TempData["Message"] = $"{driver.FullName} can now log in.";
            }
        }
        return RedirectToPage(new { id });
    }

    /// <summary>The driver, only when they belong to this company (the tenant filter decides).</summary>
    private async Task<Driver?> LoadAsync(int id)
    {
        var driver = await db.Drivers.FirstOrDefaultAsync(d => d.Id == id);
        if (driver is null)
            return null;

        Id = driver.Id;
        DriverName = driver.FullName;
        HasLogin = driver.UserId is not null;
        SavedEmail = driver.Email;
        ViewData["EmailLocked"] = HasLogin;
        Current = await links.CurrentAsync(driver.Id);
        VehicleChoices = await links.VehicleChoicesAsync();
        return driver;
    }
}
