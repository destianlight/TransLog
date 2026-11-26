# TransLog

Drivers photograph fuel slips, weighbridge tickets, odometers and receipts; one AI vision call reads them; owners review, approve and report. South Africa only.

ASP.NET Core 10 Razor Pages · SQL Server · Bootstrap 5.3.9 (self-hosted) · SignalR · Chart.js

## Status

Skeleton: landing page only. Part 1 (company registration and login) is next.

## Build

Builds only run when requested: **Actions** tab > **Build** > **Run workflow**. The published site is under **Artifacts** on the run's page.

## Getting code in from a phone

1. Upload one `.zip` to the repository root (**Add file** > **Upload files**).
2. **Actions** > **Apply zip** > **Run workflow**. It unzips over the repository (dropping a single top folder such as `TransLog/`), deletes the zip, commits, and starts Build.

Workflows cannot change files in `.github/workflows/`; those are pasted by hand on GitHub. Visible copies are kept in `github-setup/` because folders starting with a dot are hidden on Android. Apply zip never deletes files; files to remove are listed with each part.

Run locally (needs the .NET 10 SDK): `dotnet run --project TransLog.Web`

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
