using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransLog.Web.Ai;
using TransLog.Web.Data;

namespace TransLog.Tests;

/// <summary>Part 5a1: AI prompts and capture types managed by SysAdmins, with scope, layering and versions.</summary>
public class Part5a1Tests(TestApp app) : IClassFixture<TestApp>
{
    private static readonly string[] BuiltIn = ["system", "detect", "fuel", "weighbridge", "odometer", "receipt"];

    private readonly Scenario _s = new(app);

    private async Task<T> InDbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = app.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<CaptureType> GlobalAsync(string key) =>
        InDbAsync(db => db.CaptureTypes.AsNoTracking().Include(t => t.Companies).SingleAsync(t => t.Key == key && t.Scope == PromptScope.Global));

    private Task<int> CompanyIdAsync(string name) =>
        InDbAsync(db => db.Companies.Where(c => c.Name == name).Select(c => c.Id).SingleAsync());

    /// <summary>The edit form fields as the page posts them (lists numbered from 0, as page-ai-prompt.js does).</summary>
    private static Dictionary<string, string> FormFor(PromptInput input, string? key = null)
    {
        var d = new Dictionary<string, string>
        {
            ["Form.Name"] = input.Name,
            ["Form.Description"] = input.Description ?? "",
            ["Form.Prompt"] = input.Prompt,
            ["Form.Scope"] = input.Scope.ToString(),
            ["Form.IsActive"] = input.IsActive ? "true" : "false",
        };
        if (key is not null)
            d["Key"] = key;
        for (var i = 0; i < input.CompanyIds.Count; i++)
            d[$"Form.CompanyIds[{i}]"] = input.CompanyIds[i].ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < input.Fields.Count; i++)
        {
            var f = input.Fields[i];
            d[$"Form.Fields[{i}].Key"] = f.Key;
            d[$"Form.Fields[{i}].Label"] = f.Label;
            d[$"Form.Fields[{i}].Type"] = f.Type;
            d[$"Form.Fields[{i}].Unit"] = f.Unit ?? "";
            d[$"Form.Fields[{i}].Core"] = f.Core ?? "";
            if (f.Required)
                d[$"Form.Fields[{i}].Required"] = "true";
        }
        return d;
    }

    private static PromptInput NewType(string name, params int[] companies) => new()
    {
        Name = name,
        Description = "A proof of delivery for a load: client, load number and tonnes delivered.",
        Prompt = "Step 2: read this proof of delivery.\n\n{fields}",
        Scope = companies.Length == 0 ? PromptScope.Global : PromptScope.SelectedCompanies,
        CompanyIds = [.. companies],
        Fields =
        [
            new() { Key = "load_number", Label = "Load number", Core = "ReferenceNo", Required = true },
            new() { Key = "tonnes", Label = "Tonnes delivered", Type = "number", Unit = "t", Core = "Quantity" },
        ],
    };

    [Fact]
    public async Task Only_system_admins_open_the_prompt_pages()
    {
        var owner = await _s.CompanyWithOwnerAsync("Prompt Access Co", "owner@prompt-access.test");
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/sysadmin/prompts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/sysadmin/prompts/edit")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/sysadmin/prompts/effective")).StatusCode);

        var admin = await _s.AdminAsync();
        var page = await admin.GetAsync("/sysadmin/prompts");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        foreach (var key in BuiltIn)
            Assert.Contains($"data-prompt-key=\"{key}\"", html);
        Assert.Contains("AI prompts", html);
        Assert.Contains("href=\"/sysadmin/prompts\"", html);

        var fuel = await GlobalAsync("fuel");
        var edit = await (await admin.GetAsync($"/sysadmin/prompts/edit/{fuel.Id}")).Content.ReadAsStringAsync();
        Assert.Contains("/resources/page-ai-prompt", edit);
        Assert.Contains("data-name=\"Key\"", edit);
        Assert.Contains("data-tl-add-field", edit);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/sysadmin/prompts/history/{fuel.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_AI_help_page_is_for_system_admins_and_linked_from_the_prompts()
    {
        var owner = await _s.CompanyWithOwnerAsync("Prompt Help Co", "owner@prompt-help.test");
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/sysadmin/aihelp")).StatusCode);

        var admin = await _s.AdminAsync();
        var help = await admin.GetAsync("/sysadmin/aihelp");
        Assert.Equal(HttpStatusCode.OK, help.StatusCode);
        var html = await help.Content.ReadAsStringAsync();
        Assert.Contains("Global and selected companies", html);
        Assert.Contains("{fields}", html);
        Assert.Contains("href=\"/sysadmin/aihelp\"", await (await admin.GetAsync("/sysadmin/prompts")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Startup_seeds_the_six_built_in_entries_once_and_each_passes_the_rules()
    {
        var entries = await InDbAsync(db => db.CaptureTypes.AsNoTracking().Include(t => t.Companies).ToListAsync());
        foreach (var key in BuiltIn)
        {
            var e = Assert.Single(entries, x => x.Key == key && x.Scope == PromptScope.Global);
            Assert.True(e.IsActive);
            Assert.True(e.CurrentVersion >= 1);
            Assert.Empty(PromptRules.Check(e.Kind, e.Key, PromptService.InputFrom(e), PromptService.IsLockedGlobal(e)));
        }

        var receipt = entries.Single(x => x.Key == "receipt");
        Assert.Equal(1, receipt.CurrentVersion);
        Assert.Equal(1, await InDbAsync(db => db.CaptureTypeVersions.CountAsync(v => v.CaptureTypeId == receipt.Id)));
        Assert.Contains(PromptRules.FieldsFromJson(entries.Single(x => x.Key == "fuel").FieldsJson), f => f.Key == "litres" && f.Core == "Quantity");

        // Running the seed again adds nothing.
        Assert.Equal(0, await InDbAsync(db => PromptSeeds.SeedAsync(db)));
    }

    [Fact]
    public async Task Saving_makes_a_new_version_with_an_audit_entry_and_restore_brings_the_old_content_back()
    {
        var admin = await _s.AdminAsync();
        var before = await GlobalAsync("odometer");
        Assert.Equal(1, before.CurrentVersion);

        var input = PromptService.InputFrom(before);
        input.Prompt += "\n- A test hint for 5a1.";
        var r = await admin.PostFormAsync($"/sysadmin/prompts/edit/{before.Id}", FormFor(input));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        var after = await GlobalAsync("odometer");
        Assert.Equal(2, after.CurrentVersion);
        Assert.Contains("A test hint for 5a1.", after.Prompt);
        Assert.Equal(2, await InDbAsync(db => db.CaptureTypeVersions.CountAsync(v => v.CaptureTypeId == before.Id)));
        Assert.True(await InDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(a => a.Action == "PromptUpdated" && a.Details!.StartsWith("odometer"))));

        var restore = await Scenario.PostToAsync(admin, $"/sysadmin/prompts/history/{before.Id}",
            $"/sysadmin/prompts/history/{before.Id}?handler=Restore", new() { ["version"] = "1" });
        Assert.Equal(HttpStatusCode.Redirect, restore.StatusCode);

        var restored = await GlobalAsync("odometer");
        Assert.Equal(3, restored.CurrentVersion);
        Assert.Equal(before.Prompt, restored.Prompt);
        Assert.Equal(3, await InDbAsync(db => db.CaptureTypeVersions.CountAsync(v => v.CaptureTypeId == before.Id)));
        Assert.True(await InDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(a => a.Action == "PromptRestored" && a.Details!.StartsWith("odometer"))));

        var history = await (await admin.GetAsync($"/sysadmin/prompts/history/{before.Id}")).Content.ReadAsStringAsync();
        Assert.Contains("data-version=\"3\"", history);
        Assert.Contains("data-version=\"1\"", history);
    }

    [Fact]
    public async Task A_version_for_selected_companies_replaces_the_global_entry_only_for_them()
    {
        await _s.CompanyWithOwnerAsync("Layer Test One", "owner@layer-one.test");
        await _s.CompanyWithOwnerAsync("Layer Test Two", "owner@layer-two.test");
        var one = await CompanyIdAsync("Layer Test One");
        var two = await CompanyIdAsync("Layer Test Two");
        var admin = await _s.AdminAsync();

        // A weighbridge version for company one only.
        var global = await GlobalAsync("weighbridge");
        var copy = PromptService.InputFrom(global);
        copy.Scope = PromptScope.SelectedCompanies;
        copy.CompanyIds = [one];
        copy.Prompt = copy.Prompt.Replace("Step 2:", "Step 2 (Layer One):", StringComparison.Ordinal);
        var r = await admin.PostFormAsync($"/sysadmin/prompts/edit?from={global.Id}", FormFor(copy));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        // An extra type for company one only.
        r = await admin.PostFormAsync("/sysadmin/prompts/edit", FormFor(NewType("Loads (test)", one), "loads_test"));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        var forOne = await (await admin.GetAsync($"/sysadmin/prompts/effective?companyId={one}")).Content.ReadAsStringAsync();
        Assert.Contains("data-effective-type=\"weighbridge\" data-effective-scope=\"SelectedCompanies\"", forOne);
        Assert.Contains("Step 2 (Layer One):", forOne);
        Assert.Contains("data-effective-type=\"loads_test\"", forOne);
        Assert.Contains("- loads_test: A proof of delivery", forOne);

        var forTwo = await (await admin.GetAsync($"/sysadmin/prompts/effective?companyId={two}")).Content.ReadAsStringAsync();
        Assert.Contains("data-effective-type=\"weighbridge\" data-effective-scope=\"Global\"", forTwo);
        Assert.DoesNotContain("Step 2 (Layer One):", forTwo);
        Assert.DoesNotContain("loads_test", forTwo);
        Assert.Contains("data-effective-type=\"fuel\"", forTwo);

        // Company one cannot get a second selected weighbridge entry: refused, nothing saved.
        var count = await InDbAsync(db => db.CaptureTypes.CountAsync(t => t.Key == "weighbridge"));
        var again = await admin.PostFormAsync($"/sysadmin/prompts/edit?from={global.Id}", FormFor(copy));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Contains("already has an active", await again.Content.ReadAsStringAsync());
        Assert.Equal(count, await InDbAsync(db => db.CaptureTypes.CountAsync(t => t.Key == "weighbridge")));

        // Switched off, company one falls back to the global entry.
        var selected = await InDbAsync(db => db.CaptureTypes.AsNoTracking().Include(t => t.Companies)
            .SingleAsync(t => t.Key == "weighbridge" && t.Scope == PromptScope.SelectedCompanies));
        var off = PromptService.InputFrom(selected);
        off.IsActive = false;
        r = await admin.PostFormAsync($"/sysadmin/prompts/edit/{selected.Id}", FormFor(off));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        forOne = await (await admin.GetAsync($"/sysadmin/prompts/effective?companyId={one}")).Content.ReadAsStringAsync();
        Assert.Contains("data-effective-type=\"weighbridge\" data-effective-scope=\"Global\"", forOne);
    }

    [Fact]
    public async Task Save_refuses_bad_entries_and_saves_nothing()
    {
        var admin = await _s.AdminAsync();

        var dup = NewType("Duplicate fields");
        dup.Fields.Add(new() { Key = "tonnes", Label = "Tonnes again", Type = "number" });
        var r = await admin.PostFormAsync("/sysadmin/prompts/edit", FormFor(dup, "dup_fields"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("is used twice", await r.Content.ReadAsStringAsync());

        var twoCores = NewType("Two cores");
        twoCores.Fields.Add(new() { Key = "tonnes_net", Label = "Net tonnes", Type = "number", Core = "Quantity" });
        r = await admin.PostFormAsync("/sysadmin/prompts/edit", FormFor(twoCores, "two_cores"));
        Assert.Contains("is used by more than one field", await r.Content.ReadAsStringAsync());

        r = await admin.PostFormAsync("/sysadmin/prompts/edit", FormFor(NewType("Reserved"), "other"));
        Assert.Contains("are reserved", await r.Content.ReadAsStringAsync());

        r = await admin.PostFormAsync("/sysadmin/prompts/edit", FormFor(NewType("Second global fuel"), "fuel"));
        Assert.Contains("already a Global entry", await r.Content.ReadAsStringAsync());

        Assert.False(await InDbAsync(db => db.CaptureTypes.AnyAsync(t => t.Key == "dup_fields" || t.Key == "two_cores" || t.Key == "other")));

        // The global system and detect prompts stay on and keep their placeholders.
        var system = await GlobalAsync("system");
        var off = PromptService.InputFrom(system);
        off.IsActive = false;
        r = await admin.PostFormAsync($"/sysadmin/prompts/edit/{system.Id}", FormFor(off));
        Assert.Contains("cannot be switched off", await r.Content.ReadAsStringAsync());

        Assert.Equal(system.CurrentVersion, (await GlobalAsync("system")).CurrentVersion);
    }

    [Fact]
    public async Task Paste_fills_the_form_without_saving_and_copy_shows_the_block()
    {
        var admin = await _s.AdminAsync();

        // A block as Claude would write it in a chat, with text and fences around it; no key and no {fields}.
        const string pasted = """
            Here is your block:
            ```json
            {
              "name": "Pasted loads",
              "description": "A signed delivery note for a load: client, load number and tonnes.",
              "fields": [
                { "label": "Load number", "type": "text", "core": "ReferenceNo", "required": true },
                { "label": "Tonnes delivered", "type": "number", "unit": "t", "core": "Quantity" },
              ],
              "prompt": "Step 2: read this delivery note. The load number is often top right."
            }
            ```
            """;
        var fill = await Scenario.PostToAsync(admin, "/sysadmin/prompts/edit", "/sysadmin/prompts/edit?handler=Fill", new() { ["paste"] = pasted });
        Assert.Equal(HttpStatusCode.OK, fill.StatusCode);
        var html = await fill.Content.ReadAsStringAsync();
        Assert.Contains("data-fill-note", html);
        Assert.Contains("value=\"Pasted loads\"", html);
        Assert.Contains("value=\"Tonnes delivered\"", html);
        Assert.False(await InDbAsync(db => db.CaptureTypes.AnyAsync(t => t.Name == "Pasted loads")));

        // Saving what was filled in (the key and the field keys are made from the name and labels).
        var input = new PromptInput
        {
            Name = "Pasted loads",
            Description = "A signed delivery note for a load: client, load number and tonnes.",
            Prompt = "Step 2: read this delivery note. The load number is often top right.",
            Scope = PromptScope.Global,
            Fields =
            [
                new() { Key = "", Label = "Load number", Core = "ReferenceNo", Required = true },
                new() { Key = "", Label = "Tonnes delivered", Type = "number", Unit = "t", Core = "Quantity" },
            ],
        };
        var r = await admin.PostFormAsync("/sysadmin/prompts/edit", FormFor(input, ""));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var saved = await GlobalAsync("pasted_loads");
        Assert.Equal(new[] { "load_number", "tonnes_delivered" }, PromptRules.FieldsFromJson(saved.FieldsJson).Select(f => f.Key).ToArray());
        Assert.Contains("Fields to read:", PromptSet.TypeText(saved));

        // Copy shows the saved block; plain text pasted on the entry replaces only the prompt.
        var edit = await (await admin.GetAsync($"/sysadmin/prompts/edit/{saved.Id}")).Content.ReadAsStringAsync();
        Assert.Contains("data-copy-block", edit);
        Assert.Contains("tonnes_delivered", edit);
        fill = await Scenario.PostToAsync(admin, $"/sysadmin/prompts/edit/{saved.Id}", $"/sysadmin/prompts/edit/{saved.Id}?handler=Fill",
            new() { ["paste"] = "Just a new prompt line." });
        html = await fill.Content.ReadAsStringAsync();
        Assert.Contains("Just a new prompt line.</textarea>", html);
        Assert.Contains("value=\"Tonnes delivered\"", html);
        Assert.Equal(1, (await GlobalAsync("pasted_loads")).CurrentVersion);
    }
}

/// <summary>Part 5a1 rules that need no database: layering, filled-in placeholders and field checks.</summary>
public class PromptRuleTests
{
    private static int _id;

    private static CaptureType Entry(string key, PromptScope scope = PromptScope.Global, bool on = true, params int[] companies) => new()
    {
        Id = Interlocked.Increment(ref _id),
        Kind = PromptRules.KindForKey(key),
        Key = key,
        Name = key + " " + scope,
        Description = "When it is " + key,
        Prompt = key == "detect" ? "Types:\n{types}\njson" : "json {fields}",
        FieldsJson = PromptRules.FieldsToJson([new PromptField { Key = "amount", Label = "Amount", Type = "money" }]),
        Scope = scope,
        IsActive = on,
        CurrentVersion = 1,
        UpdatedAt = DateTime.UtcNow,
        Companies = companies.Select(c => new CaptureTypeCompany { CompanyId = c }).ToList(),
    };

    [Fact]
    public void Selected_entries_replace_the_global_one_and_off_entries_are_left_out()
    {
        var globalFuel = Entry("fuel");
        var fuelForOne = Entry("fuel", PromptScope.SelectedCompanies, true, 1);
        var offFuelForTwo = Entry("fuel", PromptScope.SelectedCompanies, false, 2);
        var loadsForOne = Entry("loads", PromptScope.SelectedCompanies, true, 1);
        var offReceipt = Entry("receipt", PromptScope.Global, false);
        var systemForOne = Entry("system", PromptScope.SelectedCompanies, true, 1);
        var all = new[] { Entry("system"), Entry("detect"), globalFuel, fuelForOne, offFuelForTwo, loadsForOne, offReceipt, systemForOne };

        var one = PromptSet.Resolve(all, 1);
        Assert.Same(fuelForOne, Assert.Single(one.Types, t => t.Key == "fuel"));
        Assert.Contains(one.Types, t => t.Key == "loads");
        Assert.DoesNotContain(one.Types, t => t.Key == "receipt");
        Assert.Same(systemForOne, one.SystemPrompt);

        var two = PromptSet.Resolve(all, 2);
        Assert.Same(globalFuel, Assert.Single(two.Types, t => t.Key == "fuel"));
        Assert.DoesNotContain(two.Types, t => t.Key == "loads");
        Assert.Equal(PromptScope.Global, two.SystemPrompt!.Scope);

        var three = PromptSet.Resolve(all, 3);
        Assert.Same(globalFuel, Assert.Single(three.Types));
    }

    [Fact]
    public void Detect_and_type_prompts_get_their_placeholders_filled()
    {
        var set = PromptSet.Resolve([Entry("system"), Entry("detect"), Entry("fuel"), Entry("receipt", PromptScope.Global, false)], 1);
        var detect = set.DetectText();
        Assert.Contains("- fuel: When it is fuel", detect);
        Assert.Contains("- other:", detect);
        Assert.DoesNotContain("receipt", detect);
        Assert.DoesNotContain("{types}", detect);

        var fuel = PromptSet.TypeText(set.Types[0]);
        Assert.DoesNotContain("{fields}", fuel);
        Assert.Contains("- amount (amount in rand, number): Amount", fuel);
        Assert.Contains("{\"fields\":{\"amount\":{\"value\":1234.56,\"confidence\":\"high\"}}}", fuel);
    }

    [Fact]
    public void Field_checks_catch_bad_keys_types_and_core_columns()
    {
        var errors = PromptRules.CheckFields(
        [
            new() { Key = "Bad Key", Label = "Bad" },
            new() { Key = "when", Label = "When", Type = "number", Core = "EventDate" },
            new() { Key = "odo", Label = "Odo", Type = "colour" },
        ]);
        Assert.Contains(errors, e => e.Message.Contains("the key must start", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Message.Contains("core column EventDate needs the type Date", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Message.Contains("unknown type", StringComparison.Ordinal));
        Assert.Contains("Add at least one field.", PromptRules.CheckFields([]).Select(e => e.Message));
    }

    [Fact]
    public void Keys_are_made_from_names_and_labels()
    {
        Assert.Equal("load_proof_of_delivery", PromptRules.MakeKey("Load / proof of delivery"));
        Assert.Equal("slip_number", PromptRules.MakeKey("  Slip number "));
        Assert.Equal("x_2_x_tyre", PromptRules.MakeKey("2 x tyre"));
        Assert.Equal("a_1", PromptRules.MakeKey("A"));
        Assert.True(PromptRules.MakeKey(new string('a', 60)).Length <= 40);

        var input = new PromptInput
        {
            Name = "x", Prompt = "p",
            Fields = [new() { Key = "", Label = "Litres" }, new() { Key = "", Label = "litres" }, new() { Key = "odo", Label = "Odometer" }],
        };
        PromptRules.Tidy(input);
        Assert.Equal(new[] { "litres", "litres_2", "odo" }, input.Fields.Select(f => f.Key).ToArray());
    }

    [Fact]
    public void Missing_placeholders_and_json_are_added_when_the_prompt_is_sent()
    {
        var detect = Entry("detect");
        detect.Prompt = "Say which document this is.";
        detect.FieldsJson = "[]";
        var system = Entry("system");
        system.Prompt = "Read only what is printed.";
        var fuel = Entry("fuel");
        fuel.Prompt = "Step 2: read this fuel slip.";
        var set = PromptSet.Resolve([system, detect, fuel], 1);

        Assert.Contains("- fuel: When it is fuel", set.DetectText());
        Assert.Contains("Answer in json like this example", set.DetectText());
        Assert.Contains("json", set.SystemText());
        Assert.StartsWith("Step 2: read this fuel slip.", PromptSet.TypeText(fuel));
        Assert.Contains("\"fields\":{\"amount\"", PromptSet.TypeText(fuel));
        Assert.Empty(PromptRules.Check(PromptKind.Detect, "detect", PromptService.InputFrom(detect), true));
    }

    [Fact]
    public void A_block_round_trips_and_is_found_inside_chat_text()
    {
        var fuel = PromptSeeds.All().Single(s => s.Key == "fuel");
        var block = PromptBlock.Read("Here you go:\n```json\n" + PromptBlock.Write(fuel) + "\n```\nGood luck!");
        Assert.NotNull(block);
        Assert.Equal("Fuel slip", block.Name);
        Assert.Equal(fuel.Prompt, block.Prompt);
        Assert.Equal(PromptRules.FieldsFromJson(fuel.FieldsJson).Select(f => f.Key), block.Fields!.Select(f => f.Key));
        Assert.Contains("look under Notes", PromptBlock.Write(fuel)); // readable, not \u escapes

        Assert.Null(PromptBlock.Read("Just a plain prompt without braces."));
        Assert.Null(PromptBlock.Read("{ not json }"));

        var input = new PromptInput { Name = "old", Prompt = "old" };
        PromptBlock.Apply(new PromptBlockData { Prompt = "new", Fields = [new() { Label = "X" }] }, input, PromptKind.System);
        Assert.Equal("new", input.Prompt);
        Assert.Equal("old", input.Name);
        Assert.Empty(input.Fields);
    }

    [Fact]
    public void Every_seeded_entry_passes_the_rules()
    {
        var seeds = PromptSeeds.All();
        Assert.Equal(6, seeds.Count);
        foreach (var s in seeds)
        {
            s.Scope = PromptScope.Global;
            s.IsActive = true;
            Assert.Empty(PromptRules.Check(s.Kind, s.Key, PromptService.InputFrom(s), PromptService.IsLockedGlobal(s)));
        }
    }
}
