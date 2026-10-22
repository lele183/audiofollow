# Changelog

## 1.2.0 — 2026-10-22

- Rename the application to 声随 · AudioFollow, with a new executable, data directory, startup entry, and IPC identity.
- Follow the headset route for all three microphone roles, with separate headset and desktop input selections.
- Preserve current input when its configured microphone is missing or ambiguous; keep playback switching independent from input failures.
- Add an option to disable microphone following, independent input/output cards, keyboard focus, named selectors, and scrollable device configuration.
- Adapt foundational control and accessibility ideas from Appica UI without adding a web framework or runtime dependency.
- Migrate legacy device settings without overwriting new preferences; gracefully stop the old app and replace its known login entry.
- Add ten microphone/legacy-settings regression checks and expand UI validation to nine layouts and microphone controls.

## 1.1.0 — 2026-10-22

- Keep an overlapped HID read armed and reuse its event and unmanaged buffers.
- Wake on HID and audio notifications; reduce fallback audio enumeration to 30 seconds.
- Cache the process ID, reduce idle status writes to 30 seconds, and expose diagnostic work counters.
- Replace tray/window polling timers with coalesced state notifications.
- Redesign the Chinese settings window around current output, mode selection, and expandable device settings.
- Add deterministic UI previews, installed-app smoke checks, performance measurements, and reproducible packaging.
- Add a Windows build/test workflow, documentation, and SHA-256 release assets.

## 1.0.0 — 2026-10-22

- Initial verified Windows utility for Razer Barracuda X 2022 receiver 1532:0552.
- Read-only E3 queries and passive wireless events; all three playback roles switch together.
- Tray controls, Chinese settings, global shortcuts, current-user startup, installer and uninstaller.
