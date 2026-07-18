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

## Driving the UI headlessly

No Playwright browsers installed; use `npm install playwright-core` (no
download) + system Chrome:
`executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe'`.

Wait for readiness: poll `http://localhost:5090/` for 200, then in-page wait
for `.mud-progress-linear` to disappear (circuit up + TenantContext
initialized).

MudBlazor 9 quirks under headless Chrome (real users unaffected):

- **MudMenu won't open from a plain synthetic click.** Reliable sequence:
  real `click()` on `.mud-menu-activator` (priming), check for items, else
  `focus()` + `keyboard.press('Enter')`; retry the pair up to 4×.
- **Never wait for menu-item *visibility*** — the popover never gets
  `.mud-popover-open`; poll `document.querySelectorAll('.mud-popover
  .mud-menu-item').length > 0` and use `dispatchEvent('click')` on items.
- **Snackbars steal keyboard events** — wait for `.mud-snackbar` count 0
  before keyboard menu activation (auto-hide ≈ 5s).
- After the switcher's first-tenant branch swap (no-tenant button → menu),
  reload the page once before opening the menu.
- Dialog inputs: `.mud-dialog input` (nth 0 = Name, 1 = Slug), fill()
  triggers `Immediate` binding; buttons by text (`Create & switch`, `Cancel`).

Known noise: favicon.ico 404 in console — the app ships no favicon.
