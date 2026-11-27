using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;
using TransLog.Web.Fleet;

// The folder is Areas/Company, but the namespace is CompanyArea so it does not clash with the Company class.
namespace TransLog.Web.Areas.CompanyArea;

/// <summary>
/// Checks shared by the create and edit pages. The queries need no "where CompanyId": the tenant
/// filter in AppDbContext already limits them to the logged-in company.
/// </summary>
public static class FleetChecks
{
    /// <summary>Why this normalised plate cannot be a vehicle's registration, or null when it is free.</summary>
    public static async Task<string?> PlateProblemAsync(AppDbContext db, string plate, int exceptVehicleId)
    {
        if (!Plates.IsValid(plate))
            return "Enter a registration of 2 to 20 letters and digits.";

        var used = await db.Vehicles.AnyAsync(v => v.RegistrationNo == plate && v.Id != exceptVehicleId)
                   || await db.VehicleAliases.AnyAsync(a => a.Alias == plate && a.VehicleId != exceptVehicleId);
        return used ? $"{plate} is already used by another vehicle (as its registration or as a name on slips)." : null;
    }

    /// <summary>Why this normalised name cannot be added to the vehicle, or null when it is free.</summary>
    public static async Task<string?> AliasProblemAsync(AppDbContext db, string alias, Vehicle vehicle)
    {
        if (!Plates.IsValid(alias))
            return "Enter a name of 2 to 20 letters and digits.";
        if (alias == vehicle.RegistrationNo)
            return "That is already this vehicle's registration.";

        var used = await db.Vehicles.AnyAsync(v => v.RegistrationNo == alias)
                   || await db.VehicleAliases.AnyAsync(a => a.Alias == alias);
        return used ? $"{alias} is already used by a vehicle in your company." : null;
    }

    /// <summary>
    /// Part 3d: gives the make, model and variant their known spellings ("mercedes benz" becomes "Mercedes-Benz").
    /// A make or model that matches nothing but is close to a known one gets a "Did you mean …?" question, returned
    /// here, unless the user already chose: <c>AcceptSuggestions</c> takes the suggestions, <c>KeepTyped</c> keeps
    /// the typed text. The page shows the questions and saves nothing while there are any.
    /// </summary>
    public static async Task<List<NameSuggestion>> CheckNamesAsync(AppDbContext db, VehicleForm form)
    {
        var questions = new List<NameSuggestion>();
        if (string.IsNullOrWhiteSpace(form.Make) && string.IsNullOrWhiteSpace(form.Model) && string.IsNullOrWhiteSpace(form.Variant))
            return questions;

        var all = await VehicleNames.AllAsync(db);
        var nearMatch = !form.KeepTyped;

        string? Resolve(string field, string? typed, IReadOnlyList<string> known, bool near)
        {
            if (string.IsNullOrWhiteSpace(typed))
                return null;
            var (exact, suggestion) = VehicleNames.Find(typed, known, near);
            if (exact is not null)
                return exact;
            if (suggestion is null)
                return typed.Trim();
            if (form.AcceptSuggestions)
                return suggestion;
            questions.Add(new NameSuggestion(field, typed.Trim(), suggestion));
            return typed.Trim();
        }

        form.Make = Resolve("Make", form.Make, VehicleNames.Makes(all), nearMatch);
        // The model is looked up under the make as it will be stored (or as suggested, while the question is open).
        var makeForLookup = questions.FirstOrDefault(q => q.Field == "Make")?.Suggestion ?? form.Make;
        form.Model = Resolve("Model", form.Model, VehicleNames.Models(all, makeForLookup), nearMatch);
        var modelForLookup = questions.FirstOrDefault(q => q.Field == "Model")?.Suggestion ?? form.Model;
        // Variants are codes: only an exact match (apart from capitals and spaces) changes the spelling.
        form.Variant = Resolve("Variant", form.Variant, VehicleNames.Variants(all, makeForLookup, modelForLookup), near: false);
        return questions;
    }

    /// <summary>
    /// Normalises and checks the cell and ID number of a driver form. Problems are added to the model state.
    /// Returns the values to store (the ID number is null when left empty).
    /// </summary>
    public static async Task<(string Cell, string? IdNumber)> CheckDriverAsync(
        AppDbContext db, ModelStateDictionary modelState, DriverForm form, int exceptDriverId)
    {
        var cell = CellNumbers.Normalize(form.Cell);
        if (!string.IsNullOrWhiteSpace(form.Cell))
        {
            if (!CellNumbers.IsValid(cell))
                modelState.AddModelError("Form.Cell", "Enter a South African cell number, for example 082 123 4567.");
            else if (await db.Drivers.AnyAsync(d => d.Cell == cell && d.Id != exceptDriverId))
                modelState.AddModelError("Form.Cell", "A driver with this cell number already exists.");
        }

        string? idNumber = null;
        if (!string.IsNullOrWhiteSpace(form.IdNumber))
        {
            var typed = form.IdNumber.Trim();
            var digits = SaId.Normalize(typed);
            if (typed.Any(c => !char.IsAsciiDigit(c) && c != ' ') || !SaId.IsValid(digits))
                modelState.AddModelError("Form.IdNumber", "That is not a valid South African ID number (13 digits).");
            else if (await db.Drivers.AnyAsync(d => d.IdNumber == digits && d.Id != exceptDriverId))
                modelState.AddModelError("Form.IdNumber", "A driver with this ID number already exists.");
            else
                idNumber = digits;
        }

        return (cell, idNumber);
    }
}
