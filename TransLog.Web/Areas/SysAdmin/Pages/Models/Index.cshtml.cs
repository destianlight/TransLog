using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Fleet;
using TransLog.Web.Security;

namespace TransLog.Web.Areas.SysAdmin.Pages.Models;

/// <summary>A make and model that companies typed under "Other", with how many vehicles and companies use it.</summary>
public record TypedModel(string? Make, string? Model, int Vehicles, int Companies);

/// <summary>
/// Part 3c: the global make and model catalogue. System admins add makes and models, switch models off, and add
/// models that companies typed under "Other" to the catalogue, which moves those vehicles onto it.
/// </summary>
public class IndexModel(AppDbContext db, AuditLogger audit) : PageModel
{
    public List<VehicleMake> Makes { get; private set; } = [];

    public List<TypedModel> Typed { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Makes = await db.VehicleMakes.AsNoTracking()
            .Include(m => m.Models)
            .OrderBy(m => m.Name)
            .ToListAsync();
        foreach (var make in Makes)
            make.Models = make.Models.OrderBy(x => x.Name).ToList();

        // Vehicles of every company: the tenant filter is switched off on purpose (system admins only).
        Typed = await db.Vehicles.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.VehicleModelId == null && (v.OtherMake != null || v.OtherModel != null))
            .GroupBy(v => new { v.OtherMake, v.OtherModel })
            .OrderByDescending(g => g.Count())
            .Select(g => new TypedModel(g.Key.OtherMake, g.Key.OtherModel, g.Count(), g.Select(v => v.CompanyId).Distinct().Count()))
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAddMakeAsync(string? name)
    {
        var clean = name?.Trim();
        if (string.IsNullOrEmpty(clean) || clean.Length > 50)
            TempData["Error"] = "Type a make of up to 50 characters.";
        else if (await db.VehicleMakes.AnyAsync(m => m.Name == clean))
            TempData["Error"] = $"{clean} is already in the list.";
        else
        {
            db.VehicleMakes.Add(new VehicleMake { Name = clean, IsActive = true });
            audit.Add("VehicleMakeAdded", clean);
            await db.SaveChangesAsync();
            TempData["Message"] = $"{clean} added.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddModelAsync(int makeId, string? name, string? type, string? fuel)
    {
        var make = await db.VehicleMakes.FindAsync(makeId);
        if (make is null)
            return NotFound();

        var (model, error) = await FindOrAddModelAsync(make, name, type, fuel);
        if (error is not null)
            TempData["Error"] = error;
        else
        {
            await db.SaveChangesAsync();
            TempData["Message"] = $"{make.Name} {model!.Name} is in the list.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleModelAsync(int modelId)
    {
        var model = await db.VehicleModels.Include(m => m.Make).FirstOrDefaultAsync(m => m.Id == modelId);
        if (model is null)
            return NotFound();

        model.IsActive = !model.IsActive;
        audit.Add(model.IsActive ? "VehicleModelSwitchedOn" : "VehicleModelSwitchedOff", $"{model.Make?.Name} {model.Name}");
        await db.SaveChangesAsync();
        TempData["Message"] = $"{model.Make?.Name} {model.Name} is {(model.IsActive ? "back in" : "hidden from")} the pick list. Vehicles that have it keep it.";
        return RedirectToPage();
    }

    /// <summary>Adds a typed make and model to the catalogue and moves every vehicle that typed it onto the catalogue model.</summary>
    public async Task<IActionResult> OnPostAdoptAsync(string? otherMake, string? otherModel, int? makeId, string? newMake, string? modelName, string? type, string? fuel)
    {
        VehicleMake? make;
        if (makeId is int id)
            make = await db.VehicleMakes.FindAsync(id);
        else
        {
            var clean = newMake?.Trim();
            if (string.IsNullOrEmpty(clean) || clean.Length > 50)
            {
                TempData["Error"] = "Choose a make, or type a new one.";
                return RedirectToPage();
            }
            make = await db.VehicleMakes.FirstOrDefaultAsync(m => m.Name == clean);
            if (make is null)
            {
                make = new VehicleMake { Name = clean, IsActive = true };
                db.VehicleMakes.Add(make);
                audit.Add("VehicleMakeAdded", clean);
            }
        }
        if (make is null)
            return NotFound();

        var (model, error) = await FindOrAddModelAsync(make, modelName, type, fuel);
        if (error is not null)
        {
            TempData["Error"] = error;
            return RedirectToPage();
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync(); // the make and model get their ids

        // SQL Server compares these without case, so "volvo fh" and "Volvo FH" move together.
        var vehicles = await db.Vehicles.IgnoreQueryFilters()
            .Where(v => v.VehicleModelId == null && v.OtherMake == otherMake && v.OtherModel == otherModel)
            .ToListAsync();
        foreach (var v in vehicles)
        {
            v.VehicleModelId = model!.Id;
            v.OtherMake = null;
            v.OtherModel = null;
        }
        audit.Add("VehicleModelAdopted", $"\"{otherMake} {otherModel}\" -> {make.Name} {model!.Name}: {vehicles.Count} vehicle(s)");
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        TempData["Message"] = $"{make.Name} {model.Name}: {vehicles.Count} vehicle(s) moved onto it.";
        return RedirectToPage();
    }

    /// <summary>The model of this make with that name, or a new one (not saved yet). Error text when the name is unusable.</summary>
    private async Task<(VehicleModel? Model, string? Error)> FindOrAddModelAsync(VehicleMake make, string? name, string? type, string? fuel)
    {
        var clean = name?.Trim();
        if (string.IsNullOrEmpty(clean) || clean.Length > 50)
            return (null, "Type a model of up to 50 characters.");

        var existing = make.Id == 0 ? null : await db.VehicleModels.FirstOrDefaultAsync(m => m.MakeId == make.Id && m.Name == clean);
        if (existing is not null)
            return (existing, null);

        var model = new VehicleModel
        {
            Make = make,
            Name = clean,
            VehicleType = VehicleOptions.Types.Contains(type) ? type : null,
            FuelType = VehicleOptions.FuelTypes.Contains(fuel) ? fuel : null,
            IsActive = true,
        };
        db.VehicleModels.Add(model);
        audit.Add("VehicleModelAdded", $"{make.Name} {clean}");
        return (model, null);
    }
}
