using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Fleet;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.CompanyArea.Pages.Drivers;

/// <summary>
/// The Owner or an Admin edits a driver of their own company, links the driver to a vehicle and gives the driver a login.
/// A driver of another company answers "not found", even when the id is typed into the address.
/// </summary>
[RequestSizeLimit(3 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 3 * 1024 * 1024)]
public class EditModel(AppDbContext db, AuditLogger audit, UserManager<AppUser> users, LinkService links, DriverPhotos photos) : PageModel
{
    /// <summary>Profile photo box (Part 4c): Owner and Admin add, replace or remove the driver's photo.</summary>
    public DriverPhotoBox? Photo { get; private set; }

    [BindProperty]
    public DriverForm Form { get; set; } = new();

    public int Id { get; private set; }

    public string DriverName { get; private set; } = "";

    public bool HasLogin { get; private set; }

    /// <summary>The saved email and cell number: both work as the login.</summary>
    public string? SavedEmail { get; private set; }

    public string SavedCell { get; private set; } = "";

    /// <summary>"0821234567" or "0821234567 or sipho@x.co.za".</summary>
    public string LoginWith => string.IsNullOrWhiteSpace(SavedEmail) ? SavedCell : $"{SavedCell} or {SavedEmail}";

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

        var (cell, idNumber) = await FleetChecks.CheckDriverAsync(db, ModelState, Form, driver.Id);
        if (!ModelState.IsValid)
            return Page();

        var changes = new List<string>();
        if (driver.Cell != cell)
            changes.Add($"cell {driver.Cell} -> {cell}");
        if (!string.Equals(driver.Email ?? "", Form.Email?.Trim() ?? "", StringComparison.OrdinalIgnoreCase))
            changes.Add($"email {driver.Email} -> {Form.Email?.Trim()}");
        if (driver.IsActive != Form.IsActive)
            changes.Add(Form.IsActive ? "switched on" : "switched off");
        var switchedOff = driver.IsActive && !Form.IsActive;

        // The login follows the driver: name, email, cell number and on or off. It is saved first, before the
        // driver changes, so a cell number or email used by another TransLog account stops the whole save.
        if (driver.UserId is int userId
            && await users.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture)) is { } login)
        {
            var wasActive = login.IsActive;
            login.FullName = $"{Form.FirstName.Trim()} {Form.Surname.Trim()}".Trim();
            Logins.Apply(login, Form.Email, cell);
            login.IsActive = Form.IsActive;
            var result = await users.UpdateAsync(login);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);
                return Page();
            }
            if (wasActive != login.IsActive)
                await users.UpdateSecurityStampAsync(login); // logged out within 1 minute
        }

        driver.Cell = cell;
        driver.IdNumber = idNumber;
        Form.CopyTo(driver);

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
        else
        {
            // Part 3c: a Driver login. The cell number always works as the login; the email too when there is one.
            var user = new AppUser
            {
                FullName = driver.FullName,
                CompanyId = driver.CompanyId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };
            Logins.Apply(user, driver.Email, driver.Cell);

            var result = await users.CreateAsync(user, password ?? "");
            if (result.Succeeded)
            {
                result = await users.AddToRoleAsync(user, Roles.Driver);
                if (!result.Succeeded)
                    await users.DeleteAsync(user); // no half-made login without a role
            }
            if (!result.Succeeded)
                TempData["Error"] = string.Join(" ", result.Errors.Select(e => e.Description).Distinct());
            else
            {
                driver.UserId = user.Id;
                audit.Add("DriverLoginCreated", $"{driver.FullName} ({Logins.Describe(user)})", driver.CompanyId);
                await db.SaveChangesAsync();

                var companyName = (await db.Companies.FindAsync(driver.CompanyId))?.Name;
                LoginDetails.Keep(this, driver.FullName, companyName, Logins.Describe(user), password!);
                TempData["Message"] = $"{driver.FullName} can now log in.";
            }
        }
        return RedirectToPage(new { id });
    }

    /// <summary>Receives a cropped photo from page-driver-photo.js (Part 4c). JSON { ok } or, with status 400, { error }.</summary>
    public async Task<IActionResult> OnPostPhotoAsync(int id)
    {
        if (await LoadAsync(id) is not { } driver)
            return NotFound();
        var (photo, thumb, error) = await DriverPhotos.ReadAsync(Request);
        if (photo is null)
            return BadRequest(new { error });
        error = await photos.SaveAsync(driver, photo, thumb, byDriver: false);
        if (error is not null)
            return BadRequest(new { error });
        TempData["Message"] = "Photo saved.";
        return new JsonResult(new { ok = true });
    }

    public async Task<IActionResult> OnPostRemovePhotoAsync(int id)
    {
        if (await LoadAsync(id) is not { } driver)
            return NotFound();
        await photos.RemoveAsync(driver);
        TempData["Message"] = "Photo removed.";
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
        SavedCell = driver.Cell;
        Current = await links.CurrentAsync(driver.Id);
        VehicleChoices = await links.VehicleChoicesAsync();
        Photo = new DriverPhotoBox(driver.Id, driver.PhotoPath, driver.FullName,
            Url.Page("/Drivers/Edit", "Photo", new { area = "Company", id = driver.Id })!,
            Url.Page("/Drivers/Edit", null, new { area = "Company", id = driver.Id })!, "environment");
        return driver;
    }
}
