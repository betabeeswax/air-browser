# AIR Browser

**A native Windows 11 browser from AIR.**

Website: https://air-browser-rha-nine.vercel.app  
Source: https://github.com/betabeeswax/air-browser

## Download

Every update to `main` starts a Windows build and publishes a GitHub Release.

- **Installer:** `AIR-Browser-Setup.exe` installs AIR Browser.
- **Portable:** `AIR-Browser-Portable.zip` contains the app folder. Extract it somewhere you can write files, then run `AIRBrowser.WinUI.exe`.

Downloads: https://github.com/betabeeswax/air-browser/releases

## Technology

- C# and .NET 8
- WinUI 3 for the native Windows interface
- Microsoft Edge WebView2 Fixed Version 156.0.4314.56 (Chromium) bundled with the app
- Windows 11 x64 target
- Inno Setup installer and portable ZIP

## Current foundation

- Native dark AIR shell with black/blue styling
- WebView2-based browsing and navigation
- Tabs and pop-up-to-new-tab handling
- Bing, Google and DuckDuckGo search selection
- Separate Personal and Work browser data folders
- Profile-specific bookmarks and browsing history
- Native AIR start page concept

## Planned modules

AIR account sync, Microsoft/Google provider integrations, encrypted password sync, tab groups and cross-device group sync, configurable weather and other widgets, profile management, theme editor, richer history/bookmark management and privacy controls are planned follow-up work. They are not represented as complete in this first native foundation.

**Security note:** AIR does not ask for or store your Microsoft or Google passwords. Provider sign-in will use official OAuth flows when implemented.


## AIR Browser updates and data

- Installing a newer AIR Browser setup over an existing installation updates the program in place.
- Browser profiles and data are stored separately under `%LOCALAPPDATA%\AIR Browser`, not inside the installation folder.
- Normal updates do **not** erase bookmarks, history, saved passwords, or WebView profiles.
- Uninstalling the app also leaves this browser data folder in place. Remove it manually only if you want to erase your data.
- The portable build stores its data in an `AIR-Browser-Data` folder beside the portable app. Keep that folder when moving or updating the portable app; deleting it deletes that portable profile.


## Bundled browser engine

AIR Browser includes the Microsoft WebView2 Fixed Version runtime (156.0.4314.56, x64), so a separate Evergreen WebView2 Runtime installation is not required. This adds roughly 300 MB of runtime files to the download. The runtime is pinned to a tested version and must be updated by AIR releases; it does not update itself. AIR's build verifies the downloaded runtime CAB against its pinned SHA-256 checksum before packaging it.
