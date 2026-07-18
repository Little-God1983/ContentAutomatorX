---
name: verify
description: Build, launch, and drive the ContentAutomatorX Blazor Server UI to verify changes end-to-end
---

# Verifying ContentAutomatorX

## Gotcha: `dotnet run` serves 500s for framework static assets

On this machine (SDK 10.0.302), running via `dotnet run` makes
`/_framework/blazor.web.js` and `/_content/MudBlazor/*` return 500
(`StaticAssetDevelopmentRuntimeHandler` FileNotFoundException resolving them
under `wwwroot\`). The Blazor circuit never starts. This is a dev-time asset
handler issue, not app code — **publish instead**:

```bash
dotnet publish src/ContentAutomatorX.Web -c Release -o <scratch>/publish
cd <scratch>/publish && dotnet ContentAutomatorX.Web.dll   # background it
```

The published app uses `<scratch>/publish` as content root → fresh throwaway
`data/contentx.db` (empty DB = good for empty-state flows; delete `data/` to
reset). It binds http://localhost:5090 (from appsettings). Stop any other
instance first — port clash, and running instances lock build DLLs
(MSB3026/MSB3027). Kill by command line, the process is `dotnet.exe`:

```powershell
Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
  Where-Object { $_.CommandLine -like '*ContentAutomatorX.Web.dll*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Get-Process ContentAutomatorX.Web -EA SilentlyContinue | Stop-Process -Force  # dotnet-run instances
```

Running a second instance next to a dev one: `appsettings.json` pins
`"Urls": "http://localhost:5090"`, which beats `ASPNETCORE_URLS`; pass
`--urls http://localhost:5091` on the command line instead (that wins).

## Driving the UI headlessly

No Playwright browsers installed; use `npm install playwright-core` (no
download) + system Chrome:
`executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe'`.

Full tenant-switcher walkthrough script: [drive.cjs](drive.cjs). Copy it
into a scratch dir that has `playwright-core` installed and run
`node drive.cjs` there (`BASE`/`OUT_DIR` env vars override port 5090 /
screenshot dir).

Wait for readiness: poll `http://localhost:5090/` for 200, then in-page wait
for `.mud-progress-linear` to disappear (circuit up + TenantContext
initialized).

Drive notes:

- **A single plain `click()` must open MudMenus.** Do NOT add keyboard or
  retry fallbacks: MudBlazor 9 custom `ActivatorContent` needs explicit
  `OnClick="context.ToggleAsync"` wiring, and a keyboard fallback once
  masked exactly that bug (menu opened on Enter, never on click — broken
  for real users while E2E "passed").
- Menu content renders only while open: wait for `.mud-popover
  .mud-menu-item` count > 0 after the click, then normal `click()` on items
  works.
- Snackbars stack over the top-right menu area for ~5s (cosmetic in
  screenshots; clicks still land).
- Dialog inputs: `.mud-dialog input` (nth 0 = Name, 1 = Slug), fill()
  triggers `Immediate` binding; buttons by text (`Create & switch`, `Cancel`).

Known noise: favicon.ico 404 in console — the app ships no favicon.
