# AIR Browser

**A native Windows 11 browser from AIR.**

Website: https://air-browser.vercel.app  
Source: https://github.com/betabeeswax/air-browser

## Download

Every update to `main` starts a Windows build and publishes a GitHub Release.

- **Installer:** `AIR-Browser-Setup.exe` installs AIR Browser.
- **Portable:** `AIR-Browser-Portable.zip` contains the app folder. Extract it somewhere you can write files, then run `AIRBrowser.exe`.

Downloads: https://github.com/betabeeswax/air-browser/releases

## Technology

- C# and .NET 8
- WinForms shell with an HTML, CSS and JavaScript interface
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

## Current browser features

- Profile-specific bookmarks, history, saved passwords and startup preferences
- Startup options: new page, chosen startup page, or continue where you left off
- Manual update checks and optional automatic update checks with an Update now / Later prompt
- Reading mode for simplified page reading
- Import bookmarks directly from Edge, Chrome, Brave and Firefox; password CSV import remains separate
- Unpacked Chromium extension loading through WebView2 (some toolbar popup interfaces are limited by WebView2)
- QuietChat shortcut to https://quietchat-rho.vercel.app
- Tab groups, context-menu tab actions and smooth interface transitions

AIR Account sign-in is optional. Google and Microsoft provider sign-in are not included. Browsing does not require an AIR Account.


## AIR Browser updates and data

- Installing a newer AIR Browser setup over an existing installation updates the program in place.
- Browser profiles and data are stored separately under `%LOCALAPPDATA%\AIR Browser`, not inside the installation folder.
- Normal updates do **not** erase bookmarks, history, saved passwords, or WebView profiles.
- Uninstalling the app also leaves this browser data folder in place. Remove it manually only if you want to erase your data.
- The portable build stores its data in an `AIR-Browser-Data` folder beside the portable app. Keep that folder when moving or updating the portable app; deleting it deletes that portable profile.


## Android app

The Android app is maintained separately at https://github.com/betabeeswax/air-browser-android. It has its own APK release workflow, update checks with an Update now / Later prompt, a setting to enable or disable automatic checks, reading mode and a QuietChat shortcut. Android WebView does not provide the same Chrome extension installation APIs as desktop WebView2.

## Bundled browser engine

AIR Browser includes the Microsoft WebView2 Fixed Version runtime (156.0.4314.56, x64), so a separate Evergreen WebView2 Runtime installation is not required. This adds roughly 300 MB of runtime files to the download. The runtime is pinned to a tested version and must be updated by AIR releases; it does not update itself. AIR's build verifies the downloaded runtime CAB against its pinned SHA-256 checksum before packaging it.
