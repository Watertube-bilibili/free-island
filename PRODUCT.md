# 浮岛

<!-- impeccable:product-schema 1 -->

## Platform

Windows desktop with two separately built variants. The Windows 10/11 variant uses WPF with a .NET Framework 4.6 minimum (`src/`), targeting the runtime included with the first Windows 10 release, 1507 / Build 10240. It is compiled against Microsoft's .NET 4.6 reference assemblies. The Windows 7 variant uses native Win32 C++ and GDI+ (`win7/src/`), builds as x86 for 32-bit and 64-bit Windows 7, and needs neither .NET Framework nor Visual C++ Redistributable. Its C++ support libraries are statically linked and its runtime DLL imports are Windows system libraries. Neither variant is a web or mobile app.

## Users

Primary context: a classroom large screen, operated primarily by touch and also by mouse (confirmed by the user). A selectable desktop/computer scene must remain available.

## Product Purpose

Keep classroom and personal time management accessible without permanently covering teaching material or desktop work.

## Operating Context

Windows 10 from 1507 / Build 10240 onward and Windows 11 for the WPF variant; Windows 7 for the separate native variant. The user's school mainly uses Windows 10 17xx displays and confirmed compatibility with the earliest Windows 10 as the requirement. Both editions serve a classroom display or personal computer. Classroom is the default scene. Classroom controls must be readable at distance and easy to touch, while retaining mouse support. Desktop is an explicitly selectable, more compact scene. The application runs quietly after login, with a floating launcher and minimal edge handles.

## Capabilities and Constraints

- Stopwatch, countdown, one-time/daily schedule reminders, and scheduled shutdown.
- A fullscreen presentation window for stopwatch/countdown values, intended for viewing across a classroom.
- Floating ball with a radial function menu; movable and edge-collapsible.
- Island appears when useful and collapses to a tiny handle: solid black with liquid glass Off, a transparent water droplet with Lite or Water enabled. It supports top/left/right docking and can be dragged by its body or collapsed handle. Release snaps it to the nearest supported edge; monitor and position along that edge are remembered.
- The collapsed handle remains adjustable from 3 to 20 physical pixels through the 0–100% slider; default 20% is approximately 6 pixels. Its transparent interaction area remains 44 by 44 device-independent pixels in classroom and 24 by 24 on desktop. Click recovery and dragging remain available in every material mode.
- Consistent code-native vector icons and brief state-transition animations support the same action vocabulary in both scenes.
- Installer, per-user automatic startup, quiet login startup, local data persistence, tray controls.
- Preserve all completed behavior during redesign. Shutdown remains cancellable and never forces applications closed. Shutdown plans are not restored after application restart; missed or un-warned deadlines are cancelled.
- User explicitly rejected the previous dark mint card dashboard as ugly and requested use of the official Impeccable design skill.

## Installation and Data Boundaries

Both variants default to a per-user installation and store settings and reminders locally without an account or network requirement. The WPF variant defaults to `%LOCALAPPDATA%\Programs\FreeIsland`, stores data in `%APPDATA%\FreeIsland`, and requires .NET Framework 4.6 or newer, included with Windows 10; its uninstall flow preserves data unless deletion is explicitly selected. A chosen protected directory may require the user-initiated administrator retry. The native Win7 variant defaults to `%LOCALAPPDATA%\Programs\FreeIslandWin7`, stores data in `%APPDATA%\FreeIslandWin7`, and always preserves user data during uninstall. The variants use separate startup entries and do not migrate each other's data. Closing the control window leaves the application running; tray Exit ends it.

Version 1.0.5 build outputs are named `dist/FreeIsland-Setup-1.0.5.exe`, `dist/FreeIsland-Portable-1.0.5.zip`, `dist/FreeIsland-Win7-Setup-1.0.5.exe` and `dist/FreeIsland-Win7-Portable-1.0.5.zip`. Build the native edition with `win7/build-native.ps1`; toolchain setup and checks are documented in `tools/TOOLCHAIN.md`. WPF builds use `tools/Get-Net46References.ps1` to prefer an installed 4.6 Developer Pack or fetch Microsoft's fixed 1.0.3 reference-assembly package from NuGet and verify its SHA-256 before extraction. Those cached compile-time references are not application runtime dependencies and do not install a runtime.

The user confirmed an Apple Liquid Glass direction with a water-like appearance, including the collapsed island handle. Settings retain Off, Lite and Water (native: Water Motion); Lite remains the default and preserves existing preferences. WPF Water refracts the actual bounded background in memory and deforms on press/drag while labels stay still. Windows 10 Builds 10240–19040 use asynchronous PrintWindow rendering of lower windows, without temporarily hiding overlays or applying screen-capture exclusion. Unsupported GPU, video or transparent windows fall back to clear material when usable pixels cannot be obtained. Windows 10 2004 / Build 19041 onward keeps desktop sampling with capture exclusion to prevent self-feedback; this route may omit floating windows from recordings and sharing. Lite has no background sampling and remains the low-performance / recording alternative. No background image is saved or uploaded. The native Win7 edition has clear material and interaction deformation only, without desktop refraction.

Both editions add 0–100% material parameters, with recommended values of 50 for refraction/curvature, 65 for transparency and 55 for highlight. WPF refraction applies only in Water mode; transparency and highlight apply when glass is enabled. Controls preview immediately and persist after adjustment, with a one-action reset. Win7 calls the first setting surface thickness (曲面厚度): it changes the curved material's appearance, not actual background refraction. Missing old fields receive the recommended values, out-of-range values clamp to the valid interval and switching glass Off retains custom parameters. These parameters affect the ball, expanded island and collapsed droplet, without reducing the opacity of local text backing. Main content/forms stay solid; six radial actions retain transparent gaps. Collapsed handles retain their default 20% size position and have no repeating blink or breathing effect.

Scheduled shutdown uses an app-managed deadline and normal Windows shutdown (direct ExitWindowsEx in the native edition) without forced application termination. Core tests use an injected action or safe mode. No real Windows shutdown has been performed as a test.

## Evidence on Hand

The original WPF application and sources are in `src/`. Its core suite covers scene persistence, backward defaults, docking settings and shutdown boundaries. Its UI regression artifacts are generated in `dist/FreeIsland/smoke-artifacts`; `--smoke-test` captures both usage scenes and the fullscreen stage using isolated test data. Current release results are recorded in VERIFICATION.md and must not be inferred from older captures.

The native Win7 variant has a standalone core behavior suite and a safe --smoke-test flow covering desktop/classroom control pages, the fullscreen stage, island, radial menu and material modes. Current release results are recorded in VERIFICATION.md. The safe test uses isolated data and does not perform real shutdown, installation, or startup-registry changes. Installer checks cover extraction and embedded-payload SHA-256 verification; these are separate from exercising full installation and uninstallation.

No physical Windows 10 1507 computer, Windows 7 computer or classroom touchscreen has been tested in this development environment. Compilation, forced legacy-route tests and safe-mode interaction checks do not establish old-system hardware compatibility, graphics or touch-driver behavior, visibility at a particular classroom viewing distance, or compatibility with every display model. Current test outcomes belong in VERIFICATION.md. No real Windows shutdown has been performed as a test.

## Design Tooling

The official [Impeccable skill](https://github.com/pbakaus/impeccable) is installed at `~/.codex/skills/impeccable`. This is development tooling, not an application runtime dependency. The operative product mode is task completion; familiar controls, clarity and touch reach take priority over decoration.

## Product Principles

1. Classroom readability and touch operation determine the classroom scene.
2. Core actions remain visible and direct in both usage scenes.
3. Overlays must recede after use and remain easy to recover.
4. Scene choice persists and never changes the user's task data.
5. A tiny visible edge handle must still be reachable by touch in the classroom scene.

## Open Decisions

Exact physical display size and resolution are unknown; use adaptive layout and explicit classroom/desktop scene selection instead of device-model assumptions.

The user has confirmed touch-first classroom use with mouse support, an alternate desktop scene, and the clear water-like material direction. Physical display and driver behavior still require device validation.
