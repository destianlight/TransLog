# TransLog

Drivers photograph fuel slips, weighbridge tickets, odometers and receipts; one AI vision call reads them; owners review, approve and report. South Africa only.

ASP.NET Core 10 Razor Pages · SQL Server · Bootstrap 5.3.9 (self-hosted) · SignalR · Chart.js

## Status

Part 1: login, the SysAdmin area (companies, users, TransLog admins, audit log) and an Owner home page. Companies do not self-register in version 1: Register opens WhatsApp.

Part 2: the company Owner manages users (Admin, Capturer, Viewer) with copyable login details, company settings (contact details, who may link drivers to vehicles) and an activity log. A tenant filter keeps every company's data apart. Owners and company names are managed by TransLog.

## Build

Builds only run when requested: **Actions** tab > **Build** > **Run workflow**. The published site is under **Artifacts** on the run's page.

## Getting code in from a phone

1. Upload one `.zip` to the repository root (**Add file** > **Upload files**).
2. **Actions** > **Apply zip** > **Run workflow**. It unzips over the repository (dropping a single top folder such as `TransLog/`), deletes the zip, commits, and starts Build.

Workflows cannot change files in `.github/workflows/`; those are pasted by hand on GitHub. Visible copies are kept in `github-setup/` because folders starting with a dot are hidden on Android. Apply zip never deletes files; files to remove are listed with each part.

Run locally (needs the .NET 10 SDK): `dotnet run --project TransLog.Web`

## Workflows (Actions tab)

| Workflow | What it does |
| --- | --- |
| Apply zip | Unzips the uploaded zip over the repo, commits, starts Build |
| Build | Compiles and publishes the site (download under Artifacts) |
| Add migration | Creates a database migration after a data model change, commits it, starts Test |
| Test | Runs the automated tests against a temporary SQL Server |

## Server settings (never in the repository)

On the host, create `TransLog.Web/appsettings.Production.json` next to the site files once:

```json
{
  "ConnectionStrings": { "Default": "Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True" },
  "SysAdmin": { "Email": "you@example.com", "Password": "change-me", "FullName": "Your Name" }
}
```

The first system admin is created from `SysAdmin` at startup when none exists; after that those values are ignored.

## Areas

| Folder | Address | Who |
| --- | --- | --- |
| `Pages/` | `/`, `/login`, `/account` | Everyone |
| `Areas/SysAdmin/` | `/sysadmin` | TransLog staff |
| `Areas/Company/` | `/company` | A company's Owner and Admin |

Passwords: at least 6 characters, no other rules. Five wrong tries lock the account for 5 minutes.

## Layout

```
TransLog.Web/
  AppCulture.cs        en-ZA culture and South African time
  Program.cs
  Pages/               Razor Pages, one folder per section
  wwwroot/favicon.svg
  wwwroot/resources/   every CSS, JS, font, icon and image file, in one flat folder
                       lib-*  = third-party libraries
                       page-* = scripts for one page
```

Icons come from one sprite:

```html
<svg class="tl-icon" aria-hidden="true"><use href="/resources/icons.svg#fuel-pump"></use></svg>
```

Available: truck, fuel-pump, speedometer2, receipt, plus-lg, person, camera, check-lg, x-lg, scale, tl-mark.

## Rules

- No secrets in the repository. Use user secrets locally and environment settings on the host.
- Uploaded slip photos are never stored in `wwwroot`.
- Times are stored in UTC and shown in South African time. AI output, JSON and stored data are parsed with the invariant culture.
- Light and dark mode follow the phone's setting.

## Credits

Bootstrap and Bootstrap Icons (MIT). Saira font (SIL Open Font License).
