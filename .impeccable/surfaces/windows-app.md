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
- Edge arrows stay visually small while classroom hit areas are at least 44 DIPs.
- Radial choices fan out with short stagger; island expands and contracts; released drags animate toward their remembered edge. Timing digits remain stable while reading.
- Overlay dragging handles native touch capture directly and also supports mouse dragging. Keyboard focus, Esc on the stage, and explicit action buttons remain available.

## Verification scope

One batched round covers computer and classroom page captures, overlays, presentation stage, the core suite and native UI smoke behavior. A fix batch and one confirmation round follow only if evidence identifies a material issue. Physical classroom hardware is unavailable, so touch ergonomics are implemented and desktop-tested rather than claimed hardware-certified.

Water material revision (2026-09-14, supersedes the earlier liquid-glass paragraph): the user explicitly requested Apple-inspired clear water. Modern Windows mode 2 uses locally sampled background pixels through one rounded-rectangle distance field for silhouette, convex coordinate warping and thin neutral rim light. Capture exclusion prevents self-feedback; it also excludes these floating windows from compatible recording/sharing, disclosed in settings. The interior has no opaque white panel or decorative ellipse. Press/drag deforms the material independently of labels. Capture is bounded to visible water surfaces, about 10 Hz at rest / up to 30 Hz during input; static pixels avoid repeated bitmap upload. Lite has no capture or idle material timer and uses a clear neutral tint plus a thin rim and local label support. Win7 removes Aero blur and supplies clear material/interaction only, without desktop refraction. Black-dot geometry remains unchanged. Validation: actual production surfaces on owned light/dark grid fixtures in both scenes; desktop-capture behavior separately verified using owned checker/overlay windows. No real Win7 hardware validation is claimed.