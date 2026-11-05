# AIR Browser

**A native Windows 11 browser from AIR.**

Website: https://air-browser-rha-nine.vercel.app  
Source: https://github.com/betabeeswax/air-browser

## Download

Every update to `main` starts a Windows build and publishes a GitHub Release.

- **Installer:** `AIR-Browser-Setup.exe` installs AIR Browser.
- **Portable:** `AIR-Browser-Windows11-Portable.zip` contains the app folder. Extract it, then run `AIRBrowser.WinUI.exe`.

Downloads: https://github.com/betabeeswax/air-browser/releases

## Technology

- C# and .NET 8
- WinUI 3 for the native Windows interface
- Microsoft Edge WebView2 (Chromium) for website rendering
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
