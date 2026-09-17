# Windows control surface

Mode: Operate. Native desktop implementations: WPF for the original Windows 10/11 edition, and C++ Win32/GDI+ for the standalone Windows 7 edition. This session uses a code-first native redesign; no image comp was represented as approved and no standing build-path preference was saved.

## THESIS

A clear teaching console that helps a teacher operate time by touch and lets a whole class read it, while retaining a compact desktop scene.

## OWN-WORLD

Contemporary teaching console: pale paper field (#F6F7FB), white work surfaces, dark ink (#18243A), one cobalt action color (#4F66E8), muted blue-black secondary text (#58657A). This is a screen control language, without simulated chalk, printed textures, ornament, or marketing slogans. A restrained floating-capsule brand and a consistent rounded-stroke vector icon system connect the application, installer and overlays.

## STORY

Choose Classroom or Computer in the visible scene switch. Begin or resume a timer, add a reminder, or inspect a shutdown plan. Classroom offers an explicit full-screen display; ordinary overlays recede to small edge handles. All task data survives scene changes.

## FIRST VIEWPORT

Classroom: touch-size top navigation; an actual timing workspace and an upcoming-agenda area; direct presentation action. Computer: compact left rail with a dense task area. No marketing hero, decorative progress ring, or repeated feature-card scaffold.

## FORM

Concept seed 2378ea8f, direction assignment 5. Grounded candidates: classroom agenda workbook; digital blackboard; library catalog; station departure board; contemporary teaching console; interval instrument; school timetable. Direction 5 is the working world for the confirmed touch-operated classroom context. Ambient lighting and exact palette preference are unknown; the light palette is the working assumption rather than a claimed user preference.

Catalog challenges considered against audience identification and product clarity: deep-dive profile declined (keep strict alignment); alphabet storm declined (keep typographic commitment); HyperCard competitive for direct controls but declined on large-screen reading/modern teacher familiarity (keep visible routes); oscilloscope declined (keep explicit timer states); park poster declined (keep decisive single-accent palette); starship terminal declined (keep clear warning semantics). Their motifs are not copied into the application.

## Interaction and adaptation

- Classroom button targets at least 48 DIPs; full-screen stage controls 68 DIPs.
- Floating ball 80 DIP visible brand in a 104 DIP window in classroom; compact desktop brand 48 DIP.
- Ball edge arrows stay visually small. The island's idle collapsed handle is black with glass Off and a transparent droplet with Lite/Water enabled; its 3–20 physical-pixel size (default 20%, approximately 6 pixels) is separate from its interaction area. During timing it becomes a 30–50 physical-pixel thumbnail, default 48 classroom / 36 desktop. Interaction bounds remain at least 44 × 44 classroom / 24 × 24 desktop DIPs.
- Radial choices fan out with short stagger; island expands and contracts; released drags animate toward their remembered edge. Timing digits remain stable while reading.
- Overlay dragging handles native touch capture directly and also supports mouse dragging. Keyboard focus, Esc on the stage, and explicit action buttons remain available.

## Verification scope

One batched round covers computer and classroom page captures, overlays, presentation stage, the core suite and native UI smoke behavior. A fix batch and one confirmation round follow only if evidence identifies a material issue. Physical classroom hardware is unavailable, so touch ergonomics are implemented and desktop-tested rather than claimed hardware-certified.

Water material revision (v1.0.5, 2026-09-16): the user explicitly requested Apple-inspired clear water, including the collapsed island handle, and compatibility with the earliest Windows 10 for school displays mainly running 17xx releases. Both editions reuse Off / Lite / Water (Win7: Water Motion), with Lite still the default and existing preferences retained. Off keeps a black dot; enabling glass gives the collapsed handle a transparent droplet appearance. Click recovery, dragging, edge snapping, size settings and transparent interaction bounds remain available; no separate material switch or repeating blink/breathing effect is introduced.

The expanded island retains slightly larger title/detail text and local translucent text backing for varied backgrounds. Keep the surrounding water material visible, including edge refraction where available, while labels stay still during material deformation. WPF supports Windows 10 from Build 10240 with a .NET Framework 4.6 minimum. Builds 10240–19040 use asynchronous PrintWindow sampling of lower-window pixels without overlay hiding or capture exclusion; unsupported GPU/video/transparent sources retain clear material. Build 19041 onward keeps bounded desktop sampling and capture exclusion, which can omit floating windows from recording/sharing. Lite has no desktop capture. Win7 provides clear material and interaction deformation only, without desktop refraction or Aero blur. The six radial actions retain transparent gaps.

Both editions expose three 0–100% material settings with recommended values 50 / 65 / 55. WPF labels them refraction, transparency and edge highlight; Win7 labels its appearance-only curvature control surface thickness (曲面厚度). Use visible percent readouts, native touch/keyboard controls, immediate preview, persistence after adjustment and a recommended-value reset. WPF controls have at least 44-DIP interaction height (54 in classroom); Off keeps values while disabling the controls, and Lite disables refraction. Apply the material settings consistently to orb, island and droplet; preserve the local text backing's opacity independently of transparency settings.

This brief records the requested behavior and visual direction, not a new measured readability or hardware-validation claim. Current release evidence belongs in VERIFICATION.md. Physical Windows 10 1507, Windows 7 hardware and classroom touch-display validation remain outstanding.

Concurrent task and touch-input revision (v1.0.6, 2026-09-17): the separate collapsed island handle carries the selected timing thumbnail and a small Arabic task-count badge. The original launcher retains its six-option radial menu. One active stopwatch and one active countdown, including paused tasks, open together as independent stacked island cards with their own controls and draggable body. The countdown ring shows actual remaining proportion; the stopwatch ring shows seconds within a minute. Preserve the water material, stable readable numerals and the distinction between physical artwork size and DIP touch target. Reset to the idle dot when timing ends. WPF can follow scene defaults or a custom size; Win7 persists a custom value per scene. Do not represent the feature as unlimited independent countdowns.

Classroom custom time uses hours/minutes/seconds sliders with visible values, one-unit +/- adjustments and select-first presets. Explicit Start begins the timer. WPF keeps Start reachable below the scrolling controls; desktop keeps text fields. Reminder/shutdown scheduling gains touch date/time selection, with title presets for reminders. WPF uses an expandable calendar, today/tomorrow, day-step buttons and time sliders; Win7 uses a dedicated date/time panel with day-step buttons and time sliders. Keep Add Reminder and Confirm Shutdown explicit. Distant shutdown plans wait quietly at reduced check frequency and appear on the island only in the final 10 seconds; cancellation remains accessible earlier in the control center and tray. Keep the existing safety behavior for missed deadlines.
