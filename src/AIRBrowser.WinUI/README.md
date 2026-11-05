# AIR Browser — Native Windows

AIR Browser is being rebuilt as a native Windows 11 desktop application using C#/.NET, WinUI 3 and Microsoft Edge WebView2.

- WinUI 3 renders the browser chrome and controls natively.
- WebView2 (Chromium) renders websites.
- Browser profile data is stored separately under %LOCALAPPDATA%\\AIR Browser\\Profiles.
- The Vercel root website remains the information and download page; it is not the browser application.

## Build locally

Install Visual Studio 2022 with the .NET desktop and Windows App SDK workloads, then run:

```powershell
dotnet restore src/AIRBrowser.WinUI/AIRBrowser.WinUI.csproj
dotnet build src/AIRBrowser.WinUI/AIRBrowser.WinUI.csproj -c Release -p:Platform=x64
```

WebView2 Runtime is required. Windows 11 devices commonly already have the Evergreen Runtime installed; the installer will check for it in a later packaging step.

This is the first native foundation. Sync, account providers, password manager, tab groups, full theme editing, and configurable widgets are planned modules and are not yet implemented.