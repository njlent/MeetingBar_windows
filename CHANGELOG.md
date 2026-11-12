# Changelog

## Unreleased

### Added

<!-- Wrote each entry in past tense, for example: "Added ..." -->

### Fixed

<!-- Wrote each entry in past tense and appended its issue when available, for example: "Fixed ... (#123)" -->

## 0.1.3 - 2026-11-12

### Added

- Added a Settings > Widget > Monitor choice for the display whose taskbar shows the widget. The popup opens on that display, fullscreen hiding only reacts to apps on it, and the widget falls back to the primary display while the chosen one is disconnected.
- Added a Settings > Events toggle for all-day events in the taskbar.
- Added an expired Google sign-in alert that shows like an active event: the taskbar widget reads "Google Calendar sign-in expired" with a red marker, and the popup's top card names the account and offers Sign in again.

### Fixed

- Fixed all-day events such as holidays holding the taskbar widget and tray for the whole day; timed meetings now come first, and an all-day event shows only when no timed meeting is left today.
- Fixed the taskbar widget showing no next meeting when it fell outside the days shown in the popup, for example after a weekend; the next meeting is now searched 30 days ahead.
- Fixed Classic Outlook recurring meetings: occurrences are now read the way Outlook requires, and each occurrence has its own identity, so dismissing one or receiving its reminder no longer affects the others.
- Kept the previous Outlook events when Outlook could not be read, for example while busy, instead of clearing the widget.
- Fixed Open calendar using the legacy single-provider setting instead of each event's calendar source; Classic Outlook sources now open the event's calendar folder in Classic Outlook.
- Fixed saving in Settings undoing events dismissed in MeetingBar while Settings was open.
- Fixed the log file stopping after its second rotation.
- Stopped links from calendar data from opening anything other than web and meeting-app protocols, so an invite or ICS feed cannot launch `file:` or similar handlers.
- Reduced idle work in the taskbar widget by skipping unchanged appearance and size updates.
- Fixed expired Google sign-ins going unnoticed: a notification now names the account and reconnects it when clicked, repeating every two hours until fixed. The error message explains Google's 7-day limit for OAuth apps in Testing, and the Google setup guide says to publish the app instead of adding test users.

## 0.1.2 - 2026-10-30

### Added

- Added simultaneous multi-provider calendar sources, multiple Google and ICS accounts, shared Google OAuth client configuration with per-account encrypted tokens, per-source calendar selection, and automatic migration from the previous single-provider settings.
- Added `webcal://` and `webcals://` subscription links for ICS calendar sources, fetched securely over HTTPS.
- Added packaged Raycast for Windows Script Commands and a complete setup and troubleshooting guide.
- Added configurable end-of-day taskbar behavior to hide the widget, show a standard or custom message, or keep showing the next event.
- Added optional taskbar-widget hiding for fullscreen apps, with automatic foreground detection and an app list populated from open applications or an executable picker.
- Added translations for German, Spanish, French, Italian, Dutch, Polish, Brazilian Portuguese, Turkish, Russian, Japanese, Korean, and Simplified and Traditional Chinese across the taskbar widget, popup, notifications, reminders, Win+W widget, and Settings, with a language override under Settings > General > Language (#21).

### Fixed

- Prevented meetings marked as free from sending reminders when free events were ignored in the taskbar.
- Kept the popup's current meeting synchronized with the taskbar widget when events were ignored or filtered.
- Fixed timeline meeting-title emojis that rendered as white Unicode glyphs instead of full-color emoji.
- Fixed Settings text wrapping one letter per line in small or display-scaled windows: the navigation pane now collapses to an icon rail or menu button, setting controls move below their descriptions when space runs out, and the window opens at a DPI-aware size with a minimum size (#22).
- Fixed the release workflow rejecting changelog section headings in the CRLF checkout used by Windows runners.

## 0.1.0 - 2026-07-17

### Added

- Replaced inherited macOS automation and templates with Windows build, packaging, rolling prerelease, and dependency-audit workflows, and made portable packaging independent of a previously staged executable.
- Added native Windows tray, near-tray, and far-left taskbar surfaces.
- Added Classic Outlook, ICS, and direct Google Calendar providers.
- Added meeting detection, a shared popup, filters, reminders, auto-join, bookmarks, startup integration, a global hotkey, and launcher commands.
- Added selectable multi-point meeting notifications, including meeting start, and configurable taskbar active states with native-hover or animated-shine styling.
- Added an optional native Windows 11 Win+W provider with current and next meetings, today's agenda, live countdowns, meeting colors, and Join, Open, and Refresh actions.
- Added guided Google OAuth setup, missing-scope detection, actionable recovery guidance, and privacy-safe diagnostics.
- Embedded the MeetingBar icon in the executable, windows, and tray surface.
- Added a Mac-style event timeline, Google event colors, emoji-safe titles, and an Open calendar action.
- Added automatic and custom taskbar-matched colors, an optional event-colored icon, full-color emoji, and meeting-provider logos.
- Added configurable ongoing time remaining, taskbar-derived hover styling, auto-hiding overlay scrollbars, and event detail flyouts.
- Matched the taskbar widget and popup to Windows 11 with native acrylic, the current accent, rounded native corners, taskbar spacing, and open and close motion.
- Replaced utility glyphs with Lucide icons and simplified the event indicator.
- Added event-aware tray icon colors, a 30-minute upcoming indicator, a borderless taskbar widget, and native-style hover behavior.
- Moved popup motion to a DWM-synchronized native-window animation and added an always-on native acrylic composition tint with an opaque fallback.
- Pre-rendered the popup at startup and added a calendar-loading state for the first provider sync.
- Added display-synchronized eased mouse-wheel scrolling while preserving direct precision-touchpad deltas.
- Removed the popup header, moved Refresh and Settings into its footer, and made the full taskbar-widget height interactive.
- Replaced the WPF settings window with a WinUI 3 host using NavigationView, Mica, Fluent controls, composition scrolling, and native theme, accent, localization, and high-contrast resources.
- Added an animated overlay scrollbar, a provider logo in the primary Join button, shared past and future timeline controls, and a local-only WinUI Debug page.
- Tightened WinUI settings parity with centered equal-width pages, unified selectors, right-aligned Ein/Aus toggles, renamed taskbar sides, and an always-masked Google client secret.

### Fixed

- Kept dismissed meetings visible in expanded and Win+W agendas while excluding them from current-meeting surfaces, notifications, and auto-join; prevented muting ended events and restored muted events automatically when meetings ended.
- Improved settings dropdown contrast.
- Kept the taskbar widget visible after taskbar movement, auto-hide, accidental hiding, taskbar activation, and taskbar redraws.
- Distinguished a disabled Google Calendar API from missing OAuth permission in recovery guidance.
- Expanded long-title widgets and corrected dimmed past events, transparent emoji and provider artwork, and widget text clipping.
- Kept Desktop Acrylic active across focus and composition changes and reduced automatic widget hover to the measured Windows taskbar intensity.
- Made repeated tray and widget clicks reliably close the popup and neutralized the timeline background and current-time divider.
- Matched overlay scrollbar and event-detail hover colors to the neutral acrylic palette.
- Kept the Settings window open after Save while applying and reloading changes.
- Prevented the popup scrollbar from reserving icon space, hid empty bookmarks, and removed redundant Settings back and search controls.
