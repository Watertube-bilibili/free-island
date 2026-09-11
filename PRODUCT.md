# 浮岛

<!-- impeccable:product-schema 1 -->

## Platform

Windows desktop with two separately built variants. The original Windows 10/11 variant uses WPF and .NET Framework 4.8 (`src/`). The Windows 7 variant uses native Win32 C++ and GDI+ (`win7/src/`), builds as x86 for 32-bit and 64-bit Windows 7, and needs neither .NET Framework nor Visual C++ Redistributable. Its C++ support libraries are statically linked and its runtime DLL imports are Windows system libraries. Neither variant is a web or mobile app.

## Users

Primary context: a classroom large screen, operated primarily by touch and also by mouse (confirmed by the user). A selectable desktop/computer scene must remain available.

## Product Purpose

Keep classroom and personal time management accessible without permanently covering teaching material or desktop work.

## Operating Context

Windows 10/11 for the original WPF variant; Windows 7 for the separate native variant. Both serve a classroom display or personal computer. Classroom is the default scene. Classroom controls must be readable at distance and easy to touch, while retaining mouse support. Desktop is an explicitly selectable, more compact scene. The application runs quietly after login, with a floating launcher and minimal edge handles.

## Capabilities and Constraints

- Stopwatch, countdown, one-time/daily schedule reminders, and scheduled shutdown.
- A fullscreen presentation window for stopwatch/countdown values, intended for viewing across a classroom.
- Floating ball with a radial function menu; movable and edge-collapsible.
- Island appears when useful, collapses to a solid black dot, supports top/left/right docking, and can be dragged by its body or collapsed dot. Release snaps it to the nearest supported edge; monitor and position along that edge are remembered.
- Classroom collapsed dots remain visually tiny, with a transparent touch target of at least 44 by 44 device-independent pixels. Desktop uses compact mouse targets.
- Consistent code-native vector icons and brief state-transition animations support the same action vocabulary in both scenes.
- Installer, per-user automatic startup, quiet login startup, local data persistence, tray controls.
- Preserve all completed behavior during redesign. Shutdown remains cancellable and never forces applications closed. Shutdown plans are not restored after application restart; missed or un-warned deadlines are cancelled.
- User explicitly rejected the previous dark mint card dashboard as ugly and requested use of the official Impeccable design skill.

## Installation and Data Boundaries

Both variants install per user without administrator privileges and store settings and reminders locally without an account or network requirement. The WPF variant installs to `%LOCALAPPDATA%\Programs\FreeIsland`, stores data in `%APPDATA%\FreeIsland`, and requires .NET Framework 4.8; its uninstall flow preserves data unless deletion is explicitly selected. The native Win7 variant installs to `%LOCALAPPDATA%\Programs\FreeIslandWin7`, stores data in `%APPDATA%\FreeIslandWin7`, and always preserves user data during uninstall. The variants use separate startup entries and do not migrate each other's data. Closing the control window leaves the application running; tray Exit ends it.

Native distribution files are `dist/FreeIsland-Win7-Setup-1.0.1.exe` and `dist/FreeIsland-Win7-Portable-1.0.1.zip`. Build with `win7/build-native.ps1`; toolchain setup and checks are documented in `tools/TOOLCHAIN.md`. These development tools are not runtime dependencies.

Scheduled shutdown uses an app-managed deadline and normal Windows shutdown (direct ExitWindowsEx in the native edition) without forced application termination. Core tests use an injected action or safe mode. No real Windows shutdown has been performed as a test.

## Evidence on Hand

The original WPF application and sources are in `src/`. Its fourteen core tests pass, including scene persistence, backward defaults, docking settings and shutdown boundaries. Its UI regression artifacts are generated in `dist/FreeIsland/smoke-artifacts`; `--smoke-test` captures both usage scenes and the fullscreen stage using isolated test data. Scene-adaptation UI verification belongs to the current release build and must not be inferred from older captures.

The native Win7 variant has a standalone core behavior suite and a safe --smoke-test flow covering desktop/classroom control pages, the fullscreen stage, island, radial menu and material modes. Current release results are recorded in VERIFICATION.md. The safe test uses isolated data and does not perform real shutdown, installation, or startup-registry changes. The installer has also passed extraction and embedded-payload SHA-256 verification; full installation and uninstallation have not been exercised.

No physical Windows 7 computer or classroom touchscreen has been tested in this development environment. Native rendering and safe-mode interaction checks do not establish Windows 7 hardware compatibility, touch-driver behavior, visibility at a particular classroom viewing distance, or compatibility with every display model. No real Windows shutdown has been performed as a test.

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

The user has confirmed touch-first classroom use with mouse support and an alternate desktop scene. The light visual direction is the implementation team's working assumption, not a user-confirmed preference. Do not record an optional unanswered visual-preference question as approval.
