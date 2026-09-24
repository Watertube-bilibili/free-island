---
name: "浮岛 / Free Island"
description: "A clear native Windows teaching console with a compact desktop scene."
colors:
  cobalt: "#4F66E8"
  cobalt-hover: "#4258D4"
  cobalt-pressed: "#3448BA"
  cobalt-focus: "#152D9E"
  selected: "#EEF1FF"
  selected-ink: "#384FC2"
  selection: "#CDD6FF"
  paper: "#F6F7FB"
  surface: "#FFFFFF"
  ink: "#18243A"
  muted: "#58657A"
  line: "#DCE2EB"
  input-line: "#AAB6C8"
  neutral-hover: "#EDF1F9"
  neutral-pressed: "#DDE5F5"
  warning-paper: "#FFF5E4"
  warning-ink: "#80540A"
  warning-line: "#F0D6A4"
  error: "#B72B38"
  complete: "#276956"
  brand-sky: "#B9CDFF"
  edge-paper: "#EDF0FF"
typography:
  display-classroom:
    fontFamily: "Segoe UI"
    fontSize: "112px"
    fontWeight: 600
    lineHeight: "1.19"
    fontFeature: "tnum"
  display-desktop:
    fontFamily: "Segoe UI"
    fontSize: "79px"
    fontWeight: 600
    lineHeight: "1.19"
    fontFeature: "tnum"
  headline-classroom:
    fontFamily: "Microsoft YaHei UI"
    fontSize: "28px"
    fontWeight: 600
    lineHeight: "1.45"
  headline-desktop:
    fontFamily: "Microsoft YaHei UI"
    fontSize: "25px"
    fontWeight: 600
    lineHeight: "1.45"
  title-classroom:
    fontFamily: "Microsoft YaHei UI"
    fontSize: "22px"
    fontWeight: 600
    lineHeight: "1.45"
  title-desktop:
    fontFamily: "Microsoft YaHei UI"
    fontSize: "18px"
    fontWeight: 600
    lineHeight: "1.45"
  body-classroom:
    fontFamily: "Microsoft YaHei UI"
    fontSize: "20px"
    fontWeight: 400
    lineHeight: "1.45"
  body-desktop:
    fontFamily: "Microsoft YaHei UI"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: "1.45"
  action-classroom:
    fontFamily: "Microsoft YaHei UI"
    fontSize: "20px"
    fontWeight: 500
  action-desktop:
    fontFamily: "Microsoft YaHei UI"
    fontSize: "14px"
    fontWeight: 500
  label-classroom:
    fontFamily: "Microsoft YaHei UI"
    fontSize: "16px"
    fontWeight: 500
    lineHeight: "1.45"
  label-desktop:
    fontFamily: "Microsoft YaHei UI"
    fontSize: "12px"
    fontWeight: 500
    lineHeight: "1.45"
rounded:
  check: "5px"
  edge: "6px"
  focus: "8px"
  button: "10px"
  surface: "12px"
  stage-button: "14px"
  toggle: "16px"
  island: "32px"
spacing:
  inline-small: "8px"
  inline: "10px"
  compact: "12px"
  action-gap: "14px"
  section-small: "20px"
  section: "24px"
  workspace-classroom: "28px"
components:
  button-primary:
    backgroundColor: "{colors.cobalt}"
    textColor: "{colors.surface}"
    rounded: "{rounded.button}"
    padding: "10px 22px"
    typography: "{typography.action-classroom}"
  button-primary-hover:
    backgroundColor: "{colors.cobalt-hover}"
  button-primary-active:
    backgroundColor: "{colors.cobalt-pressed}"
  button-secondary:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.button}"
    padding: "10px 22px"
  button-ghost:
    backgroundColor: "transparent"
    textColor: "{colors.cobalt}"
    rounded: "{rounded.button}"
    padding: "0 10px"
  input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    padding: "9px 14px"
    typography: "{typography.body-classroom}"
  navigation-active:
    backgroundColor: "{colors.selected}"
    textColor: "{colors.selected-ink}"
    rounded: "{rounded.button}"
    padding: "12px 10px"
  scene-choice:
    backgroundColor: "{colors.selected}"
    textColor: "{colors.ink}"
    rounded: "{rounded.button}"
    padding: "8px 13px"
  surface:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.surface}"
  island:
    textColor: "{colors.ink}"
    rounded: "{rounded.island}"
    padding: "10px 12px 10px 16px"
---

# Design System: 浮岛 / Free Island

## Overview

**Creative North Star: "The Teaching Console"**

A pale, orderly control surface for operating time. Dark readable text, stable timing figures and a single cobalt action color help people find a task quickly. Native Windows type and familiar controls serve an operating tool intended for touch and mouse.

Classroom and desktop are two densities of this same system. The floating capsule brand connects the control center, overlays, fullscreen stage and installer. The light palette records the finished implementation; it remains the team's working assumption, not a user-confirmed color preference. The prior dark mint dashboard was explicitly rejected.

**Key Characteristics:**

- Pale fields and white work surfaces, organized with quiet borders.
- Cobalt actions and explicit text states.
- Native Chinese UI type paired with stable Latin timing numerals.
- Classroom touch reach and a compact desktop density.
- Brief transitions for controls; steady figures while reading.

This is a scan of the native WPF implementation in `src/ControlWindow.cs`, `src/AppVisual.cs`, `src/Surfaces.cs`, `src/IslandHandleWindow.cs` and `src/PresentationWindow.cs`, with the shared installer and icon artwork checked in `installer/Setup.cs` and `assets/Generate-Icon.ps1`. It records reused visual rules. Surface composition and concept seed 2378ea8f remain in `.impeccable/surfaces/windows-app.md`.

## Colors

The palette combines cool paper, dark blue ink and a clear cobalt accent. Frontmatter values are normative; the sidecar's tonal strips are generated previews, not additional shipped colors.

### Primary

- **Cobalt:** primary actions, active controls, vector action icons and the circular brand.
- **Cobalt Hover / Pressed / Focus:** main control-center interaction states.
- **Selected / Selected Ink / Selection:** pale active navigation and choices, darker selected navigation text, and native text selection.
- **Brand Sky:** the small upper capsule inside the brand. It does not introduce a second action hierarchy.

### Neutral

- **Paper / Surface:** application field and white working containers.
- **Ink / Muted:** primary content and supporting text.
- **Line / Input Line:** quiet divisions and stronger editable-field boundaries.
- **Neutral Hover / Pressed:** feedback on secondary controls.
- **Edge Paper:** tiny recoverable handles over other Windows content.

### Status

- **Warning Paper / Ink / Line:** the shutdown confirmation area.
- **Error:** unsuccessful-action feedback.
- **Complete:** the finished fullscreen countdown and installer success text.

**The Action Color Rule.** Use cobalt for action, selection and the shared brand; express warning, error and completion with their named semantic colors and readable state text.

## Typography

**Display Font:** Segoe UI, for timing numerals.
**Body Font:** Microsoft YaHei UI, for Chinese interface text and controls.

**Character:** Native and direct. These Windows UI families are deliberate in this Operate surface. The hierarchy uses size and medium or semibold weight, without decorative display lettering.

### Hierarchy

- **Display:** paired classroom/desktop roles hold normal countdown values. Stopwatch values use slightly smaller local sizes (110/77 DIPs) to fit hours. Tabular numeral alignment and no wrapping keep time stable.
- **Headline:** page identity, using the paired headline roles.
- **Title:** sections inside the workspace, using the paired title roles.
- **Body:** ordinary content uses normal weight; actions and emphasized rows use medium weight at the same size.
- **Label:** field labels and supporting controls use medium weight; captions use normal weight at the same paired sizes.
- **Fullscreen figures:** a semibold 240-DIP source element scales uniformly in a Viewbox. Fullscreen state text uses 32 DIPs, reduced to 26 in compact layout.

The token unit `px` represents WPF device-independent units for portability. Installer font sizes remain native WinForms points; do not copy them as WPF DIPs. No custom fallback font stack is configured.

**The Stable Time Rule.** Keep timer numerals tabular, unwrapped and still; animate state changes around the reading surface.

## Layout

The application adapts through explicit scene choice plus available space. Classroom uses a six-choice horizontal navigation row. Desktop uses a 170-DIP left rail. The same task data and action vocabulary serve both.

The control window starts at up to 1280 × 840 DIPs in classroom or 1000 × 740 in desktop, bounded by the Windows work area minus 24 DIPs. Workspace side gutters are 28/24 DIPs. Reused spacing is practical rather than a strict mathematical scale; use the existing component helpers instead of inventing a universal grid.

On the home surface, the agenda moves below the timer when its content width falls below 940 DIPs in classroom or 705 in desktop. Long task content scrolls vertically. Required settings and shutdown actions occupy a separate bottom row outside that scroll region.

The fullscreen stage becomes compact below 1050 DIPs wide or 740 high. Figures scale uniformly; controls wrap. The keyboard hint is hidden below 620 DIPs high.

**The Reach Rule.** Preserve the distinction between visible artwork and its interactive bounds: primary classroom controls have at least 54-DIP height, navigation 56, scene choices 48 and fullscreen controls 68; collapsed classroom handles keep at least 44 DIPs on their short hit-area axis.

## Elevation & Depth

Main control surfaces use white/paper layering and single-DIP borders without card shadows. Floating overlays use soft shadows to separate themselves from arbitrary material underneath.

### Shadow Vocabulary

- **Floating ball:** WPF DropShadowEffect, blur 14, depth 3, color RGB(34,48,91), opacity 0.18.
- **Radial menu:** WPF DropShadowEffect, blur 18, depth 4, color RGB(35,51,83), opacity 0.16.
- **Island:** WPF DropShadowEffect, blur 15, depth 4, color RGB(35,51,83), opacity 0.16.

**The Floating Depth Rule.** Use ambient shadow to locate floating Windows overlays; organize the control center with tone, spacing and borders.

## Shapes

Buttons have softened corners, containers slightly broader corners, and the island a capsule silhouette. Tiny edge handles stay rounded without making their visible shape as large as their touch area. Native TextBox geometry is retained; there is no shared rounded-input token.

Icons are code-native paths in a normalized 24-unit frame, with 1.8-unit round strokes and joins. The brand is a cobalt circle holding a white 14 × 5 capsule at (5,12.5), plus a sky-colored 7 × 3 capsule at (10,6.5). Reuse this geometry across WPF, installer drawing and exported application icon.

## Components

### Buttons

Direct, clearly labeled actions. Primary buttons use cobalt and white; secondary buttons use white, ink and a quiet border. Control-center buttons have minimum height 54/40 DIPs and horizontal padding 22/17 DIPs in classroom/desktop, with medium body type. Hover and pressed states use the named variants; keyboard focus thickens the boundary to 2 DIPs. Disabled main buttons dim to 0.47 opacity.

Ghost text actions keep cobalt text, a transparent resting surface and the secondary hover/press treatment. Their minimum height is 48/32 DIPs.

Fullscreen buttons retain the same hierarchy with their own 14-DIP radius, 68-DIP height and 3-DIP focus boundary. Small local differences in stage state brushes are implementation details rather than new shared palette roles.

### Inputs / Fields

White editable fields use ink text, a stronger input boundary, cobalt caret and pale selection. Main fields match the 54/40-DIP control height and use 14/11-DIP horizontal padding. Focus changes the border to cobalt and 2 DIPs. Native editing behavior remains available.

Checkboxes use drawn vector ticks; their focus treatment surrounds the full label target. Toggle switches retain a visible cobalt boundary when keyboard focused.

### Navigation

Text and vector icons travel together. In classroom, choices are centered along the top; in desktop, left-aligned in the rail. Selected navigation uses Selected, Selected Ink and a pale selection border. Hover, pressed and keyboard-focus behavior comes from the common button template.

### Scene Choices

A paired explicit classroom/desktop selector stays in the window header. The chosen scene uses the pale Selected fill and outlined boundary. It is a mode choice, not a status badge.

### Cards / Containers

White surfaces use the shared surface radius and a single-DIP Line border. Padding follows content density, commonly 20–30 DIPs in classroom and 17–25 in desktop. Cards group related work; lists also use dividers and spacing.

### Floating Brand and Island

The ball displays the brand at 80 DIPs in a 104-DIP classroom window, or 48 in a 68-DIP desktop window. The radial menu and island scale their desktop geometry by 1.5 in classroom. The island's desktop surface is 440 × 102 DIPs; it carries an action icon, title, brief detail and direct controls.

The ball's edge handles show a small vector arrow over Edge Paper. The island has a separate collapsed handle: an idle black dot with liquid glass Off, or a transparent water droplet with Lite/Water enabled. During timing it grows into a 40–160 physical-pixel thumbnail with real time, a thin timing ring and a small numeric task count. Transparent bounds preserve reachability. Drag release snaps to a supported edge.

Motion uses brief scale, fade and position changes: 180 ms for page entry and fullscreen state fades, 220 ms for dock settling, 240 ms radial choices, and 290 ms island opening. WPF easing and exact transitions are preserved in the sidecar. Snapshot mode disables these animations. Main page and stage animation also respect the Windows client-area animation setting.

### Island Conversation · v1.0.10

A compact, readable conversation uses the incumbent island material, type family and action palette. Its empty view starts at 408 DIPs high and expands to 548 for a conversation, subject to the existing scene scale and work-area fitting. A scrollable message region leaves the composer and recovery actions reachable. The local title is 21 DIPs, message text 16/14 in classroom/desktop and context text 12; these are component sizes, not new global type roles.

Use the existing readable-panel treatment behind the heading, context, replies and footer, leaving water visible around them. User messages have a pale local backing and align right. The composer retains a white native field, input boundary, cobalt caret and selection treatment, with 54-DIP minimum height and a 600-character limit. Conversation secondary controls keep at least 44-DIP height. Send and Stop use the shared primary cobalt fill with white text, including Cobalt Hover and Cobalt Pressed; never substitute the neutral state fills beneath their white labels. Existing keyboard-focus treatment remains visible.

The code-drawn blue-violet response line animates only during an active request, at most 24 frames per second. It respects the Windows motion preference and stops when cancelled or hidden. It is a local activity cue, not a new brand color or an idle animation rule. Reuse Off / Lite / Water, refraction and readable backing without adding another material preference. This extension adds no shipping raster assets.

### Model Directory Controls · v1.0.10

The assistant page uses the incumbent settings components: a wrapping read-only path field, explicit directory chooser and Restore Default action. The current path stays readable, and controls are disabled during an active model operation or conversation request. After a successful switch, the notification states that files are preserved and the model needs explicit enabling or installation; changing a path does not imply the model is running.
## Do's and Don'ts

### Do:

- **Do** reuse the paired classroom and desktop type roles and native component helpers.
- **Do** keep timing figures tabular and unwrapped, with state expressed separately.
- **Do** retain visible keyboard focus and scene-appropriate interactive bounds.
- **Do** reuse the normalized vector icon and floating-capsule brand geometry.
- **Do** keep required bottom actions reachable when task content scrolls.
- **Do** treat the light palette as the implemented working direction until the user confirms a preference.

### Don't:

- **Don't** shrink a classroom touch target to the visible arrow or collapsed droplet's dimensions.
- **Don't** add control-center card shadows as a default surface treatment.
- **Don't** substitute text glyphs or emoji for the shared vector action icons.
- **Don't** turn synthesized tonal preview ramps or incidental local brush differences into new shared tokens.

## Windows 7 native edition

The `win7/` port preserves this teaching-console direction using native Win32 windows, native edit/date/time controls, and GDI+ vector geometry. It has no CLR dependency. Both scenes, fullscreen timer, island and radial menu have been captured from the actual native renderer; the bounded review inspected all 15 captures and requested one reminder-input affordance correction. The input now has a visible outline and Chinese cue text; native non-client borders are also included in capture output. This port has no approved image comp and no browser detector report, because it is code-led native UI. Hardware validation on Windows 7 and a classroom touch display remains outstanding.
Final confirmation: both native reminder captures now show a visible outlined field and Chinese cue; the bounded reviewer returned ship. The final production EXE also passed its safe --smoke-test launch and functional checks on the development Windows system.

### Radial menu refinement · 2026-09-11

Per the user's request, both native editions now present the six labeled action buttons around the central orb without the large circular backing disk or its broad shadow. The decorative center caption is removed. Button hit sizes, scene scaling, entry/dismiss animations and actions stay the same; gaps are transparent and let the desktop receive input. This preference applies to the floating launcher's expanded menu, not the island's task panel.

### Water material, controls and collapsed handle · v1.0.5 · 2026-09-16

The user explicitly requested Apple-inspired clear water on both the expanded island and its collapsed handle. Reuse the existing Off / Lite / Water modes (Win7: Water Motion), keeping Lite as the default and retaining stored preferences. Off draws an idle black collapsed dot; either glass mode draws a transparent small droplet. Idle diameter remains 3–20 physical pixels through the 0–100% size slider, default 20% (approximately 6 pixels). Its transparent interaction region remains at least 44 × 44 DIPs in classroom and 24 × 24 on desktop. Click recovery, dragging, docking and position memory remain available. Do not add a separate material preference or a repeating blink/breathing effect. Expanded scale remains 75–150% on top of the scene, with work-area fitting; ball edge arrows are unchanged.

WPF Water uses bounded in-memory background sampling and convex refraction where supported. Early Windows 10 Builds 10240–19040 use asynchronous PrintWindow sampling of lower windows, without hiding overlays to obtain pixels or excluding them from screen capture. Unsupported GPU/video/transparent sources keep clear material. Windows 10 Build 19041 onward uses desktop capture exclusion to prevent self-feedback and may omit floating windows from compatible recording/sharing, as disclosed in settings. Lite uses clear tint and a thin rim without desktop capture. Win7 supplies clear material and interaction deformation without desktop refraction or Aero blur. Preserve transparent gaps between the six radial targets.

Material parameters use familiar native sliders with visible 0–100% readouts, touch-sized interaction areas, keyboard adjustment and explicit mode semantics. WPF settings present refraction, transparency and edge highlight in one vertical group; recommended values are 50%, 65%, 55%. Preview immediately on existing overlays, persist after adjustment and offer one-action reset. Glass Off disables parameter controls and retains their values; Lite disables refraction. The Win7 parameter view uses the name surface thickness (曲面厚度) for a visual curvature parameter, not a claim of real background refraction. Do not add a complex optics editor or promotional comparison to this task interface.

The expanded island retains slightly larger title/detail text and local translucent backing to aid reading over light, dark or busy backgrounds. Material transparency changes must not reduce the opacity of that text backing. Keep the support local so water remains visible around it. Press/drag deforms the material independently of labels. Current release checks belong in VERIFICATION.md; earlier capture reviews above are historical evidence, not v1.0.5 validation. Targeting .NET Framework 4.6 and adding a legacy capture route do not establish physical Windows 10 1507 or classroom-hardware validation.

### Concurrent task view and touch time selection · v1.0.6 · 2026-09-17

Use the island's collapsed handle for task thumbnails, keeping the original radial-menu launcher unchanged. One stopwatch and one countdown may coexist; paused timers remain in the count. Show a small Arabic numeral when both tasks are present, and open their independent cards together on click. Give each card its own clear timing value, state and direct operations, while retaining body drag and edge snapping. This is a task stack, not a collection of arbitrarily many countdowns.

The active thumbnail defaults to 88 physical pixels in classroom and 64 on desktop, configurable within 40–160. WPF offers a follow-scene choice; Win7 saves a value for each scene with a reset to that scene's default. When timing ends, restore the idle dot diameter. Preserve at least a 44-DIP classroom hit area even when the visible geometry is smaller. Keep the water material visible around stable readable digits. The ring is functional: countdown remaining proportion, stopwatch seconds within a repeating minute. Do not suggest finite completion for a stopwatch or use the ring as an idle decorative animation. A scheduled shutdown only joins the island during its final 10 seconds and does not occupy the timing badge during distant waiting.

Classroom custom duration uses three named sliders for hours, minutes and seconds, visible numeric readouts and one-unit decrement/increment buttons. Touch targets use the existing 54-DIP control height. Presets select a duration; an explicit Start action begins it. The Start action remains outside the scrollable duration area in WPF. Preserve desktop text input and the seven-day maximum. Reuse the cobalt track, clear thumb, focus outline and disabled states from existing settings sliders.

Reminder and shutdown scheduling should be operable without typing a date or time. WPF supplies today/tomorrow, previous/next day, an expandable calendar with 44-DIP day targets, and hour/minute sliders. Native Win7 uses a dedicated date/time panel with previous/next day, hour/minute sliders and a Finish action. Preset reminder titles reduce text entry, while custom content remains editable. Selecting a date or shutdown preset never schedules by itself: retain explicit Add Reminder / Confirm Shutdown actions. Keep the shutdown warning and cancellation easy to reach. No new material or palette is introduced for these controls.

## Repeating shutdown controls

Keep a clear Single occurrence / Repeat choice. Repeat exposes Every day and Weekdays presets plus seven independently selected day controls, then hour/minute selection and one explicit activation button. Do not arm or overwrite the plan when a draft day or time changes. An active plan states the chosen weekdays, local time, and next occurrence. The cancel action disables the entire repeating plan. Classroom input uses large day targets and time sliders; the existing single-date workflow remains available.
