<div align="center">

 <h1> <img src="docs/icon.png" width="200px">
 <br><br/>
 MeetingBar for Windows</h1>


 <img src="https://custom-icon-badges.demolab.com/badge/Windows 11-0078D6?logo=windows11&logoColor=white"/> 
 <img src="https://custom-icon-badges.demolab.com/badge/C%23-%23239120.svg?logo=cshrp&logoColor=white"/> 


</div>
<br/>



Native Windows edition of MeetingBar. It shows the current or next meeting in the notification area and, optionally, in a taskbar-aligned widget. Every surface opens the same meeting popup. 

> [!IMPORTANT]
> The code and concept are based on the macOS-only [MeetingBar](https://meetingbar.app/).

## What it looks like

<p align="center">
  <img src="docs/screenshots/agenda.png" alt="MeetingBar agenda popup with a timeline, current meeting, upcoming events, meeting links, and bookmarks" width="410">
  <br>
  <sub>The agenda popup puts the current meeting, the rest of the day, tomorrow, and recurring rooms in one place. Timeline window is customisable.</sub>
</p>


## Run

The portable build is `dist/MeetingBar-Windows-portable.zip`. Extract the complete archive and run `MeetingBar.exe`; keep its `Settings` folder beside it. No installer or administrator rights are required. Windows 10 or 11 with .NET Framework 4.7.2+ is required. The zero-extraction, self-contained WinUI 3 settings host carries its own .NET and Windows App SDK runtime.

First launch adds Classic Outlook as the initial source. Right-click the tray icon, choose **Settings**, then add, remove, rename, or temporarily disable any number of calendar sources. Outlook, ICS, and Google sources can be combined in the same agenda:

- **Outlook** reads local Classic Outlook calendars through its local COM interface. New Outlook does not expose that interface.
- **ICS** sources accept one local `.ics` path, HTTP(S) calendar feed URL, or `webcal://`/`webcals://` subscription link per line. Webcal links are fetched over HTTPS. Add multiple ICS source cards when separate naming, visibility, or enable/disable controls are useful.
- **Google** uses direct Calendar API access. Follow the four-step guide in Settings. Besides enabling Google Calendar API, the OAuth consent configuration must include `https://www.googleapis.com/auth/calendar.readonly` under **Data Access**. Create one OAuth client of type **Desktop app** and enter its client ID once, then add and connect each Google account separately. Every connection uses the same OAuth client and asks which Google account to add; each account receives its own refresh token, encrypted with Windows Data Protection for the signed-in Windows user. OAuth uses PKCE and a temporary loopback callback on `127.0.0.1`.

Each source has its own visible-calendar selection. If one source fails to refresh, MeetingBar keeps that source's last-known-good events while continuing to update healthy sources. Existing single-provider settings are migrated automatically on first launch.

## Surfaces

- **Tray only**: icon only; Windows controls whether it is directly visible or inside the overflow menu.
- **Taskbar right**: compact meeting title/time overlay immediately left of the notification area, plus tray icon.
- **Taskbar left**: compact overlay at the far-left edge, plus tray icon.
- **Windows Widgets Board (beta)**: optional native `Win+W` agenda card with current/next meeting, today's events, and Join/Open/Refresh actions. Enable it under **Settings > Widget**, then pin MeetingBar from the Windows widget picker.

The taskbar versions are borderless topmost overlays. Windows 11 has no supported third-party text-widget slot inside its taskbar. Widget width shrinks to its event content; its maximum width and X/Y offsets are configurable for taskbar size, alignment, display scaling, and third-party taskbar tools. The full taskbar-height surface is interactive. At rest the widget has no border or background; its rounded hover surface uses the measured taskbar hover intensity and automatic light/dark text contrast, while the custom-color override applies to that hover surface. On Windows 11 22H2 or newer the popup uses always-on native acrylic composition, rounded DWM corners, the current Windows accent, and a DWM-synchronized 167 ms whole-window taskbar slide; older systems receive a solid fallback. A loading surface covers the calendar content until the first provider sync completes. Event lists use display-synchronized eased mouse-wheel scrolling, direct precision-touchpad scrolling, and a neutral auto-hiding overlay scrollbar. Settings is a native WinUI 3 window with NavigationView, Mica, Fluent cards and controls, composition scrolling, and automatic Windows light/dark, accent, localization, and high-contrast resources. Save applies and reloads changes without closing the window.

## Features

- Current/next meeting, event-aware tray icon and upcoming-event dot, configurable ongoing time remaining, independently configurable past/future hour windows and day boundaries shared by the event list and timeline, dimmed past events, muted-but-visible dismissed events, hover detail flyouts, countdowns, event colors, transparent full-color emoji titles, meeting-provider join icons, and Open calendar action
- Google Meet, Zoom, Teams, Webex, Jitsi, Slack Huddles, and the full MeetingBar provider catalogue; arbitrary links remain joinable
- Meeting-link precedence, HTML cleanup, custom regex model, and nested Outlook SafeLink decoding
- Event filters: all-day, declined, tentative, free/non-busy, linkless, calendar selection
- Preferred browser, create-meeting URL, recurring meeting bookmarks
- Start with Windows, periodic/manual refresh, multiple simultaneous providers/accounts, and per-source last-known-good events after provider failure
- Multiple selectable meeting notifications (including at start), optional full-screen reminder, end notification, auto-join
- Configurable taskbar highlight before and during meetings, using the native hover surface or an animated in-progress shine
- Configurable end-of-day taskbar behavior: hide the widget, show a standard or custom message, or continue showing the next event
- Optional fullscreen-aware taskbar hiding, plus an app list for borderless games populated from running applications or an executable picker
- Global `Ctrl+Alt+J` join shortcut
- English, German, Spanish, French, Italian, Dutch, Polish, Brazilian Portuguese, Turkish, Russian, Japanese, Korean, and Simplified and Traditional Chinese. MeetingBar follows the Windows display language; choose a different one under **Settings > General > Language**
- Local command forwarding for Raycast for Windows, PowerToys Run, scripts, and desktop shortcuts
- Native WinUI Debug page for local-only injected events, test notifications, and full-screen reminder testing without modifying a real calendar

Mac-only AppleScript, Shortcuts, EventKit, StoreKit, and experimental features are intentionally absent.

Full-color emoji use an offline Twemoji atlas. See `THIRD-PARTY-NOTICES.md` in the portable package for attribution.

## Launcher commands

Commands forward to the already-running instance:

```powershell
MeetingBar.exe --popup
MeetingBar.exe --join
MeetingBar.exe --create
MeetingBar.exe --calendar
MeetingBar.exe --refresh
MeetingBar.exe --settings
MeetingBar.exe --quit
```

For Raycast for Windows, import the ready-made commands from the packaged `raycast` folder. See the complete [Raycast setup guide](docs/RAYCAST.md). The guide also covers manual Run Commands, hotkeys, paths containing spaces, PowerShell policy, and troubleshooting.

## Build and test

Visual Studio 2022 or Build Tools with the .NET Framework 4.7.2 targeting pack, plus the .NET 8 SDK, is required. The build restores stable Microsoft Windows App SDK 2.2 for the WinUI settings host and native widget provider.

```powershell
.\build.ps1 -Configuration Release -Test -Package
```

Source structure follows feature ownership: calendar providers and sync, meeting detection/opening, notifications, settings, then surface controllers.

## Known platform limits

- The taskbar overlay targets the primary Windows taskbar. Multi-monitor secondary taskbar selection is not implemented yet.
- Outlook support requires Classic Outlook. Google and ICS work without Outlook.
- Notification balloons use the built-in notification-area API; full-screen reminders provide the richer join action.
- The portable Win+W provider is a loose unsigned app package. Windows Developer Mode must be enabled before its settings toggle can register it, and Windows requires manual pinning from the Win+W picker.
- Windows Widgets use host-rendered Adaptive Cards and cannot embed the full WPF popup or its external hover submenu.
- The portable binary is unsigned. Production distribution should add Authenticode signing and an MSIX or installer.

## Diagnostics

Provider refresh results and crash information are written without event titles or URLs to `%APPDATA%\MeetingBar\meetingbar.log`.

If Google reports missing Calendar permission, open **Settings → Calendars**, complete step 3 under **Data Access**, click **Disconnect**, then connect again so Google issues a token with the required scope.

## License

Licensed under the Apache License 2.0. See [`LICENSE`](LICENSE).
