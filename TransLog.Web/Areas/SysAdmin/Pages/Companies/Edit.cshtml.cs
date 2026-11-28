using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.SysAdmin.Pages.Companies;

public class EditModel(AppDbContext db, UserManager<AppUser> users, AuditLogger audit, TransLog.Web.Ai.AiProviders aiProviders) : PageModel
{
    [BindProperty]
    public CompanyForm Form { get; set; } = new();

    [BindProperty]
    public bool IsActive { get; set; }

    public int Id { get; private set; }

    public string Name { get; private set; } = "";

    public List<UserRow> Users { get; private set; } = [];

    /// <summary>Part 5a2a: the company's AI provider (empty = the global one).</summary>
    [BindProperty]
    public int? AiProviderId { get; set; }

    public List<(int Id, string Name)> AiChoices { get; private set; } = [];

    public string GlobalProviderName { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null)
            return NotFound();

        Form = CompanyForm.From(company);
        IsActive = company.IsActive;
        await LoadAsync(company);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null)
            return NotFound();

        var name = Form.Name.Trim();
        if (await db.Companies.AnyAsync(c => c.Name == name && c.Id != id))
            ModelState.AddModelError("Form.Name", "A company with this name already exists.");
        if (!ModelState.IsValid)
        {
            await LoadAsync(company);
            return Page();
        }

        var wasActive = company.IsActive;
        Form.CopyTo(company);
        company.IsActive = IsActive;

        if (wasActive && !IsActive)
        {
            // Log out everyone in the company within 1 minute.
            foreach (var user in await db.Users.Where(u => u.CompanyId == id).ToListAsync())
                await users.UpdateSecurityStampAsync(user);
        }

        var action = wasActive == IsActive ? "CompanyUpdated" : IsActive ? "CompanySwitchedOn" : "CompanySwitchedOff";
        audit.Add(action, company.Name, company.Id);
        await db.SaveChangesAsync();

        TempData["Message"] = "Saved.";
        return RedirectToPage(new { id });
    }

    /// <summary>Part 5a2a: only SysAdmins choose a company's AI provider (it decides the cost).</summary>
    public async Task<IActionResult> OnPostAiProviderAsync(int id)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null)
            return NotFound();

        string label;
        if (AiProviderId is int providerId)
        {
            var provider = await db.AiProviders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == providerId);
            if (provider is null || (provider.Id != company.AiProviderId && !aiProviders.IsUsable(provider)))
            {
                TempData["Message"] = "That provider cannot be chosen: it is off or has no API key.";
                return RedirectToPage(new { id });
            }
            label = provider.Key;
        }
        else
            label = "global";

        if (company.AiProviderId != AiProviderId)
        {
            company.AiProviderId = AiProviderId;
            audit.Add("CompanyAiProviderChanged", $"{company.Name}: {label}", company.Id);
            await db.SaveChangesAsync();
        }
        TempData["Message"] = "AI provider saved.";
        return RedirectToPage(new { id });
    }

    private async Task LoadAsync(Company company)
    {
        Id = company.Id;
        Name = company.Name;
        AiProviderId = company.AiProviderId;
        AiChoices = (await aiProviders.UsableAsync()).Select(p => (p.Id, p.Name)).ToList();
        if (company.AiProviderId is int chosen && AiChoices.All(c => c.Id != chosen))
        {
            var current = await db.AiProviders.AsNoTracking().FirstOrDefaultAsync(p => p.Id == chosen);
            AiChoices.Insert(0, (chosen, (current?.Name ?? "?") + " (off or no key)"));
        }
        var globalKey = await TransLog.Web.Ai.PlatformSettings.GetAsync(db, TransLog.Web.Ai.PlatformSettings.ProviderKey)
            ?? TransLog.Web.Ai.PlatformSettings.DefaultProvider;
        GlobalProviderName = await db.AiProviders.AsNoTracking().Where(p => p.Key == globalKey).Select(p => p.Name).FirstOrDefaultAsync() ?? globalKey;
        var now = DateTimeOffset.UtcNow;
        Users = [];
        foreach (var u in await db.Users.Where(u => u.CompanyId == company.Id).OrderBy(u => u.FullName).ToListAsync())
        {
            var role = (await users.GetRolesAsync(u)).FirstOrDefault() ?? "";
            Users.Add(new UserRow(u.Id, u.FullName, u.Email ?? "", role, u.IsActive, u.LockoutEnd > now, u.PhoneNumber ?? ""));
        }
    }
}
