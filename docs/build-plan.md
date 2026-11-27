# TransLog build plan

Drivers photograph fuel slips, weighbridge tickets, odometers and receipts; one AI vision call reads them; owners review, approve and report. South Africa only.

ASP.NET Core 10 Razor Pages · SQL Server · Bootstrap 5.3.9 (self-hosted) · SignalR · Chart.js

This file replaces the old `TrokLogix2-build-plan.md` and `TransLog-Part3-fleet.md`, which were lost. It was rebuilt from the code on 2026-11-27.

## Status

| Part | What | State |
| --- | --- | --- |
| 1 | Login, SysAdmin area (companies, users, TransLog admins, audit log), Owner home page | Done |
| 2 | Company users (Admin, Capturer, Viewer) with copyable login details, company settings, activity log, tenant filter | Done |
| 3a | Vehicles (with slip aliases) and drivers, expiry warnings, SA plate / cell / ID checks | Done |
| 3b | Driver–vehicle linking, take-over confirm, link history, "Give login" for drivers, My vehicle page, "company only" setting | Done |
| Deploy | Test site on Interserver: https://trans-log.duckdns.org | Live (test) |
| 4 | Capture: to discuss | Next |

## How we work

- João works only from an Android phone. Code arrives as one zip with a top `TransLog/` folder; fix zips are `fixN.zip` with only the changed files.
- Apply zip → Build → (Add migration when the data model changed, which starts Test) → Test.
- GitHub Build and Test are the only compile check. Claude cannot compile, so C# is syntax-checked by hand before zipping.
- On failure, João pastes the first red error line.
- Each part is talked through before it is built.
- Workflow files (`.github/workflows/`) cannot be changed by Apply zip; they are pasted by hand.

## Decisions made

- One fixed culture, en-ZA. Times stored in UTC, shown in South African time. AI output, JSON and stored data parsed with the invariant culture.
- Companies do not self-register in version 1: Register opens WhatsApp. Owners and company names are managed by TransLog (SysAdmin).
- Roles: SysAdmin (TransLog staff, no company), Owner, Admin, Capturer, Viewer. The Owner can give Admin, Capturer, Viewer.
- Passwords: at least 6 characters, no other rules. 5 wrong tries lock the account for 5 minutes.
- Every company row carries `CompanyId`; a global query filter keeps companies apart. Ids from another company answer "not found".
- Companies, vehicles, drivers are never deleted, only switched off. History stays.
- Plates and aliases stored upper case, letters and digits only (`llt 587-nw` → `LLT587NW`), unique per company.
- Cell numbers stored as 10 digits starting with 0, unique per company. SA ID numbers checked (13 digits, real birth date, check digit), unique per company, shown only on the edit page (POPIA), never in lists or the activity log.
- Expiry dates (licence disc, roadworthy, licence, PrDP) show a badge in lists when due or expired.
- Linking: one open link per driver and one per vehicle (enforced by filtered unique indexes). A link is closed (`UnlinkedAt`), never deleted, so the history shows who drove what when.
- Taking a vehicle that another driver has needs a confirm; the other driver's link is closed.
- Company setting "Who may link": DriversAndCompany (default; drivers pick their own vehicle on My vehicle) or CompanyOnly.
- A driver's login is a Capturer user of the same company, created with "Give login". The email is the login and cannot be changed afterwards.
- Switching a driver off closes their link and switches their login off (logged out within 5 minutes via the security stamp). Switching back on turns the login back on.
- Secrets never go in the repository. The server holds `appsettings.Production.json`.
- The first system admin is created at startup from `SysAdmin:Email` / `SysAdmin:Password` when none exists; afterwards those values are ignored.
- The app applies database migrations itself at startup.

## Data model

| Table | Key points |
| --- | --- |
| Company | Name (unique), contact details, `DriverLinking`, IsActive |
| AppUser (Identity) | FullName, `CompanyId` (null for SysAdmin), IsActive |
| AppRole (Identity) | The five roles |
| AuditLog | Who did what; indexed by CreatedAt and (CompanyId, CreatedAt) |
| Vehicle | RegistrationNo (unique per company), FleetNo, make/model/year, type, fuel, tank, odometer, disc and roadworthy expiry |
| VehicleAlias | Another way a slip prints the plate; unique per company |
| Driver | Names, Cell (unique per company), Email, IdNumber (unique per company when set), licence code/no/expiry, PrDP expiry, `UserId` (unique when set) |
| DriverVehicleLink | DriverId, VehicleId, LinkedAt, UnlinkedAt (open while empty), LinkedBy (Driver/Company), LinkedByUserId, UnlinkedByUserId |

Enums are stored as numbers; never reorder them.

Migrations: `Initial`, `Part2`, `Part3a`, `Part3b`.

## Code map

```
TransLog.Web/
  Program.cs                 services, Identity, policies; startup crash → logs\startup-error.txt
  AppCulture.cs              en-ZA culture and SA time
  Contact.cs                 WhatsApp contact settings
  LoginDetails.cs            copyable login details after creating a login
  Security/Security.cs       Roles, Policies, claims, CurrentCompany, AuditLogger
  Data/
    Entities.cs              Company, AppUser, AppRole, AuditLog, DriverLinking
    Fleet.cs                 Vehicle, VehicleAlias, Driver, DriverVehicleLink, LinkSource
    AppDbContext.cs          indexes and tenant filters
    DbInitializer.cs         migrate, roles, first SysAdmin
    Migrations/
  Fleet/
    FleetRules.cs            plate, cell, ID number rules; expiry status; choice lists
    ExpiryHtml.cs            expiry line with badge for lists
  Pages/                     /, /login, /logout, /account, /go, error
  Areas/SysAdmin/            /sysadmin: companies, users, admins, audit
  Areas/Company/             /company (namespace CompanyArea)
    Forms.cs, FleetForms.cs  form models
    FleetChecks.cs           shared vehicle/driver checks
    LinkService.cs           link, unlink, take-over, pick lists
    Pages/Users/             company users
    Pages/Vehicles/          vehicles and aliases
    Pages/Drivers/           drivers, linking, Give login
    Pages/Links/             link history
    Pages/MyVehicle          driver picks their vehicle
    Pages/Settings           company settings
    Pages/Activity           company activity log
TransLog.Tests/              Part1, Part2, Part3a, Part3b, FleetRules tests (temporary SQL Server)
```

## Deployment (test site)

- Host: Interserver ASP.NET Web Hosting (Windows, Plesk), .NET 10 installed, SQL Server instance `.\MSSQLSERVER2022`.
- Site: https://trans-log.duckdns.org, files in `/httpdocs`.
- Workflow **Deploy**: publishes self-contained `win-x64`, writes `appsettings.Production.json` from secrets, uploads by FTP with `app_offline.htm`. Never deletes server files (logs, App_Data).
- Repository secrets: `FTP_USER`, `FTP_PASSWORD`, `DB_CONNECTION`, `SYSADMIN_EMAIL`, `SYSADMIN_PASSWORD`.
- Manual fallback: download the Build artifact, extract into `httpdocs` in Plesk Files, create `appsettings.Production.json` by hand.
- Saving `appsettings.Production.json` does not restart the app; save `web.config` to restart.
- The repo is public, so artifacts must never contain secrets.

Open before real go-live:
- Persist Data Protection keys (under `App_Data`) so logins survive restarts.
- Turn off the stdout and ANCM debug logs in `web.config`.
- Change the database and FTP passwords (they were shared in chat).
- SSL certificate (Let's Encrypt) in Plesk.

## Lessons learned

- A form switch (checkbox) sends a hidden `false` when off. Tests that post forms must send `false` too; leaving the field out keeps the default (`true`).
- On IIS the stdout log can stay empty when the app crashes at startup. `logs\startup-error.txt` (written by Program.cs) shows the real exception.
- Phone autocorrect changes passwords and adds new lines when pasting secrets. Turn it off and check lengths.
- FTP error 530 is the login, not the folder.
- SQL error 18456 is a wrong database user or password; the server name was right.
- JSON needs `\\` for the backslash in `.\MSSQLSERVER2022`.
- Open item: `Part2Tests.Activity_shows_only_the_own_companys_entries` failed once and was never explained. Look at it only if it comes back.

## Build parts

1. Login and SysAdmin. Done.
2. Company users, settings, activity log. Done.
3. Fleet: 3a vehicles and drivers, 3b linking and driver logins. Done.
4. Capture (to discuss): photo of fuel slip, weighbridge ticket, odometer or receipt; client-side compression; one AI vision call; review screen; linked to driver and vehicle; location at capture time; consistency checks (litres × price = total).
5. Review and approve (to discuss).
6. Reports with Chart.js (to discuss).

## Progress log

| Date | What |
| --- | --- |
| 2026-11-26 | Part 1 done (migration Initial). |
| 2026-11-27 | Parts 2 and 3a done (migrations Part2, Part3a). |
| 2026-11-27 | Part 3b done (migration Part3b); fix1 corrected the switch-off test. |
| 2026-11-27 | Deploy workflow added; fix2 writes startup crashes to a file. First test deploy live on Interserver, admin login works. Build plan rebuilt into `docs/build-plan.md`. |
