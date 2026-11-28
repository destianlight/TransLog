using Microsoft.EntityFrameworkCore;
using TransLog.Web.Data;

namespace TransLog.Web.Ai;

/// <summary>
/// The built-in prompt entries (Part 5a1), added at startup only when no entry with that key exists.
/// Edits made in the console are never overwritten. Written from the lessons of the 27 Nov 2026 DeepSeek tests.
/// </summary>
public static class PromptSeeds
{
    public static async Task<int> SeedAsync(AppDbContext db)
    {
        var existing = await db.CaptureTypes.Select(t => t.Key).Distinct().ToListAsync();
        var added = 0;
        foreach (var seed in All())
        {
            if (existing.Contains(seed.Key))
                continue;

            var now = DateTime.UtcNow;
            seed.Scope = PromptScope.Global;
            seed.IsActive = true;
            seed.CurrentVersion = 1;
            seed.UpdatedAt = now;
            db.CaptureTypes.Add(seed);
            await db.SaveChangesAsync();

            db.CaptureTypeVersions.Add(new CaptureTypeVersion
            {
                CaptureTypeId = seed.Id,
                Version = 1,
                Name = seed.Name,
                Description = seed.Description,
                FieldsJson = seed.FieldsJson,
                Prompt = seed.Prompt,
                Scope = seed.Scope,
                CompanyIds = "",
                IsActive = true,
                CreatedAt = now,
            });
            await db.SaveChangesAsync();
            added++;
        }
        return added;
    }

    private static PromptField F(string key, string label, string type = "text", string? unit = null, bool isRequired = false, string? core = null) =>
        new() { Key = key, Label = label, Type = type, Unit = unit, Required = isRequired, Core = core };

    public static List<CaptureType> All() =>
    [
        new()
        {
            Kind = PromptKind.System,
            Key = PromptRules.SystemKey,
            Name = "Shared system prompt",
            Description = "Sent first with every AI call (detect and read), so the AI provider can cache it with the photo.",
            Prompt = """
                You read photos of South African transport documents for TransLog: fuel slips, weighbridge tickets, odometers, receipts and similar papers.

                Rules for every answer:
                - Read only what is printed or shown in the photo. Never guess, never fill in a usual value, never change a value to make the numbers add up.
                - When a value is missing, cut off or unreadable, give null.
                - Keep names, registrations, codes and numbers exactly as printed, character by character. Letters and digits that look alike are different: S is not 8, O is not 0, D is not 0, B is not 8, I is not 1, Z is not 2. When you cannot tell which one it is, give confidence "low".
                - Keep spelling mistakes as printed.
                - Dates as YYYY-MM-DD and times as HH:mm (24 hours). South African documents usually print dates day first (31/01/2026).
                - Numbers as plain numbers with "." as the decimal point, without thousands separators, currency signs or units: "R 1 234,50" becomes 1234.50.
                - Money is in rand unless printed otherwise. VAT in South Africa is 15%. Distances are in kilometres, fuel in litres and weights in kilograms unless printed otherwise.
                - Ignore stamps that a camera app draws on the photo (a date, time, road, town or coordinates in a corner). They are not part of the document.
                - Give a confidence for every value: "high", "medium" or "low".
                - Answer with one json object only, in exactly the shape of the example you are given. No text before or after it.
                """,
        },
        new()
        {
            Kind = PromptKind.Detect,
            Key = PromptRules.DetectKey,
            Name = "Type detection (step 1)",
            Description = "Step 1: which type of document the photo shows. {types} is replaced with the company's active types.",
            Prompt = """
                Step 1: say which type of document is in the photo. Do not read the values yet.

                The document types (key: when to choose it):
                {types}

                Choose the one key that fits best. Choose "other" when none fits or when the photo shows no document.
                confidence: "high" only when you are sure, "medium" when two types could fit, "low" when you cannot tell.
                description: at most 12 words about what the document is, for example the seller and what was bought.

                Answer in json like this example:
                {"type": "fuel", "confidence": "high", "description": "Diesel slip from Engen Vryburg, 412 litres"}
                """,
        },
        new()
        {
            Kind = PromptKind.Type,
            Key = "fuel",
            Name = "Fuel slip",
            Description = "A fuel or diesel slip from a filling station, truck stop or depot pump: litres of fuel, often a price per litre, a total in rand, a vehicle registration and an odometer reading.",
            FieldsJson = PromptRules.FieldsToJson(
            [
                F("registration", "Vehicle registration", isRequired: true),
                F("litres", "Litres", "number", "l", isRequired: true, core: "Quantity"),
                F("price_per_litre", "Price per litre", "money", "R/l"),
                F("amount", "Total amount", "money", "R", core: "Amount"),
                F("odometer", "Odometer", "number", "km", isRequired: true, core: "Odometer"),
                F("slip_date", "Slip date", "date", isRequired: true, core: "EventDate"),
                F("slip_time", "Slip time (HH:mm)"),
                F("vendor", "Filling station", core: "Vendor"),
                F("reference_no", "Reference number", core: "ReferenceNo"),
                F("slip_number", "Slip number"),
                F("pump", "Pump"),
                F("fuel_type", "Fuel type (diesel, petrol 95, …)"),
                F("driver_name", "Driver name"),
            ]),
            Prompt = """
                Step 2: read this fuel slip.

                Hints:
                - The truck registration is often not next to "Vehicle": on many slips "Vehicle" or "Veh" is the pump or tank id (for example TFNPUMP1). Also look under Notes, Reg, Registration, Fleet and Driver. South African registrations look like LLT587NW, CA123456 or ND 123-456.
                - reference_no and slip_number are different numbers: never copy one into the other. Give null for the one that is not printed.
                - litres is the fuel delivered, not the tank size. amount is the total paid in rand.
                - odometer is the vehicle's kilometre reading written or printed on the slip, not a pump meter reading.
                - vendor is the filling station or depot name as printed.

                {fields}
                """,
        },
        new()
        {
            Kind = PromptKind.Type,
            Key = "weighbridge",
            Name = "Weighbridge ticket",
            Description = "A weighbridge or weigh ticket from a mine, quarry, farm, mill or depot: gross, tare and net mass in kg or tonnes, a vehicle registration, a ticket number and often a product.",
            FieldsJson = PromptRules.FieldsToJson(
            [
                F("registration", "Vehicle registration", isRequired: true),
                F("trailer_registration", "Trailer registration"),
                F("ticket_number", "Ticket number", isRequired: true, core: "ReferenceNo"),
                F("ticket_date", "Ticket date", "date", isRequired: true, core: "EventDate"),
                F("time_in", "Time in (HH:mm)"),
                F("time_out", "Time out (HH:mm)"),
                F("gross_kg", "Gross mass", "number", "kg"),
                F("tare_kg", "Tare mass", "number", "kg"),
                F("net_kg", "Net mass", "number", "kg", isRequired: true, core: "Quantity"),
                F("product", "Product"),
                F("site", "Weighbridge site", core: "Vendor"),
                F("customer", "Customer"),
                F("order_number", "Order or delivery number"),
                F("driver_name", "Driver name"),
            ]),
            Prompt = """
                Step 2: read this weighbridge ticket.

                Hints:
                - Give the masses in kilograms. When the ticket prints tonnes (t), multiply by 1000: 34.52 t becomes 34520.
                - Gross is the loaded mass, tare the empty mass and net the difference. Read each one as printed; do not work one out from the others.
                - A ticket can show a truck and a trailer registration: put the truck in registration and the trailer in trailer_registration.
                - ticket_number is the ticket's own number, not an order or delivery number.
                - site is the weighbridge or company that printed the ticket.

                {fields}
                """,
        },
        new()
        {
            Kind = PromptKind.Type,
            Key = "odometer",
            Name = "Odometer photo",
            Description = "A photo of a vehicle's dashboard or odometer display showing the total distance driven, not a printed document.",
            FieldsJson = PromptRules.FieldsToJson(
            [
                F("odometer", "Odometer reading", "number", "km", isRequired: true, core: "Odometer"),
                F("unit", "Unit shown (km or mi)"),
                F("trip", "Trip meter", "number"),
                F("registration", "Vehicle registration (only when visible)"),
            ]),
            Prompt = """
                Step 2: read this odometer photo.

                Hints:
                - odometer is the total distance (the larger number), not the trip meter. Read every digit, including leading ones; ignore a decimal tenth digit when it is shown in a different colour.
                - unit: "km" or "mi" as shown; null when no unit is visible.
                - registration only when a registration or licence disc is readable in the photo; otherwise null.

                {fields}
                """,
        },
        new()
        {
            Kind = PromptKind.Type,
            Key = "receipt",
            Name = "Expense receipt",
            Description = "Any other till slip, invoice or receipt for an expense: tolls, food, accommodation or an overnight stay, parts, repairs, tyres, car wash, parking. Not a fuel slip.",
            FieldsJson = PromptRules.FieldsToJson(
            [
                F("vendor", "Seller", isRequired: true, core: "Vendor"),
                F("receipt_date", "Receipt date", "date", isRequired: true, core: "EventDate"),
                F("amount", "Total amount", "money", "R", isRequired: true, core: "Amount"),
                F("vat", "VAT amount", "money", "R"),
                F("vat_number", "Seller's VAT number"),
                F("receipt_number", "Receipt or invoice number", core: "ReferenceNo"),
                F("category", "Category (toll, food, accommodation, parts, repairs, tyres, other)"),
                F("description", "What was bought (short)"),
                F("payment_method", "Payment method (cash, card, account)"),
                F("registration", "Vehicle registration (only when printed)"),
            ]),
            Prompt = """
                Step 2: read this receipt.

                Hints:
                - amount is the total paid, including VAT. vat is the VAT amount only when it is printed; never work it out.
                - category: choose one of toll, food, accommodation, parts, repairs, tyres or other.
                - description: at most 8 words, for example "overnight truck parking" or "2 x tyre repair".
                - registration only when a vehicle registration is printed on the receipt.

                {fields}
                """,
        },
    ];
}
