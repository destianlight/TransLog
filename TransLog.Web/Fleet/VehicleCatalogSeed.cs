using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;

namespace TransLog.Web.Fleet;

/// <summary>
/// The first make and model list (Part 3c): common South African trucks, trailers and bakkies.
/// Added once, when the catalogue is empty; after that system admins manage it on the Vehicle models page.
/// Tank sizes are left empty on purpose: they differ per build, and a wrong number would break the litres check.
/// </summary>
public static class VehicleCatalogSeed
{
    private const string Tractor = "Truck tractor";
    private const string Rigid = "Rigid truck";
    private const string Trailer = "Trailer";
    private const string Bakkie = "Bakkie";
    private const string Car = "Car";
    private const string Other = "Other";
    private const string Diesel = "Diesel";
    private const string Petrol = "Petrol";

    private static readonly SeedMake[] Data =
    {
        new("Volvo", new SeedModel[] { new("FH", Tractor, Diesel), new("FM", Tractor, Diesel), new("FMX", Tractor, Diesel) }),
        new("Scania", new SeedModel[] { new("R-series", Tractor, Diesel), new("G-series", Tractor, Diesel), new("P-series", Rigid, Diesel), new("S-series", Tractor, Diesel) }),
        new("Mercedes-Benz", new SeedModel[] { new("Actros", Tractor, Diesel), new("Arocs", Tractor, Diesel), new("Axor", Tractor, Diesel), new("Atego", Rigid, Diesel), new("Sprinter", Other, Diesel) }),
        new("MAN", new SeedModel[] { new("TGX", Tractor, Diesel), new("TGS", Tractor, Diesel), new("TGM", Rigid, Diesel), new("TGL", Rigid, Diesel) }),
        new("DAF", new SeedModel[] { new("XF", Tractor, Diesel), new("CF", Tractor, Diesel), new("LF", Rigid, Diesel) }),
        new("Iveco", new SeedModel[] { new("S-Way", Tractor, Diesel), new("Stralis", Tractor, Diesel), new("Eurocargo", Rigid, Diesel), new("Daily", Rigid, Diesel) }),
        new("Isuzu", new SeedModel[] { new("FX", Tractor, Diesel), new("F-series", Rigid, Diesel), new("N-series", Rigid, Diesel), new("D-Max", Bakkie, Diesel) }),
        new("UD Trucks", new SeedModel[] { new("Quester", Tractor, Diesel), new("Quon", Tractor, Diesel), new("Croner", Rigid, Diesel) }),
        new("Hino", new SeedModel[] { new("700", Tractor, Diesel), new("500", Rigid, Diesel), new("300", Rigid, Diesel) }),
        new("FAW", new SeedModel[] { new("JH6", Tractor, Diesel), new("J5", Rigid, Diesel) }),
        new("Fuso", new SeedModel[] { new("Canter", Rigid, Diesel), new("FJ", Rigid, Diesel) }),
        new("Tata", new SeedModel[] { new("Prima", Tractor, Diesel), new("LPT", Rigid, Diesel) }),
        new("Powerstar", new SeedModel[] { new("VX", Tractor, Diesel) }),
        new("Sinotruk", new SeedModel[] { new("HOWO", Tractor, Diesel) }),
        new("Toyota", new SeedModel[] { new("Hilux", Bakkie, Diesel), new("Land Cruiser 79", Bakkie, Diesel), new("Quantum", Other, Diesel), new("Corolla Cross", Car, Petrol) }),
        new("Ford", new SeedModel[] { new("Ranger", Bakkie, Diesel) }),
        new("Nissan", new SeedModel[] { new("Navara", Bakkie, Diesel), new("NP200", Bakkie, Petrol) }),
        new("Volkswagen", new SeedModel[] { new("Amarok", Bakkie, Diesel), new("Polo", Car, Petrol) }),
        new("Mahindra", new SeedModel[] { new("Pik Up", Bakkie, Diesel) }),
        new("GWM", new SeedModel[] { new("P-Series", Bakkie, Diesel) }),
        new("Mitsubishi", new SeedModel[] { new("Triton", Bakkie, Diesel) }),
        new("Afrit", new SeedModel[] { new("Tautliner", Trailer, null), new("Flat deck", Trailer, null), new("Side tipper", Trailer, null) }),
        new("Henred Fruehauf", new SeedModel[] { new("Tautliner", Trailer, null), new("Flat deck", Trailer, null), new("Side tipper", Trailer, null), new("Tanker", Trailer, null) }),
        new("SA Truck Bodies", new SeedModel[] { new("Tautliner", Trailer, null), new("Refrigerated", Trailer, null) }),
        new("Serco", new SeedModel[] { new("Tautliner", Trailer, null), new("Refrigerated", Trailer, null) }),
        new("GRW", new SeedModel[] { new("Tanker", Trailer, null) }),
    };

    private sealed record SeedModel(string Name, string Type, string? Fuel);

    private sealed record SeedMake(string Name, SeedModel[] Models);

    public static async Task RunAsync(AppDbContext db)
    {
        if (await db.VehicleMakes.AnyAsync())
            return;

        foreach (var make in Data)
        {
            db.VehicleMakes.Add(new VehicleMake
            {
                Name = make.Name,
                IsActive = true,
                Models = make.Models.Select(m => new VehicleModel
                {
                    Name = m.Name,
                    VehicleType = m.Type,
                    FuelType = m.Fuel,
                    IsActive = true,
                }).ToList(),
            });
        }
        await db.SaveChangesAsync();
    }
}
