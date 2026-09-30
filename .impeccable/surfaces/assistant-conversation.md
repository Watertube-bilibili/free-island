# Island conversation extension

Mode: Operate. Extend the existing WPF island and teaching-console identity for desktop and classroom touch. The user requests Siri-like conversation in the island, useful application-aware suggestions, and the existing water glass. This is a precisely scoped extension; no new visual-world selection or comp round.

## Direction contract

THESIS: Ask and act in the floating island, with the recognized context visible and proposed actions requiring a deliberate gesture.

OWN-WORLD: Existing clear glass edges, cobalt controls, dark Chinese type and authored outline icons; the v1.0.11 interactive conversation replaces its pale interior with the explicitly requested transparent treatment. Keep the surrounding desktop visible. A small flowing blue-violet line belongs only to an active model response.

Conversation glass refinement · v1.0.11: the user explicitly requested “极致玻璃，不用可读性了” after finding this surface too foggy. This overrides the earlier pale text-backing requirement for the interactive conversation: Lite / Water title, context, messages, secondary controls and composer have clear resting surfaces, with no added text halo. Send / Stop retains its cobalt action treatment, Off retains the normal solid input/control treatment, and Windows high contrast retains system colors. The Water conversation uses a bounded physical-resolution lens so a tall conversation is no longer stretched from a 180-pixel-high material. Other island surfaces retain their existing material preferences and backing.

The v1.0.11 conversation suite passes 160 assertions using synthetic replies and a fixed in-memory backdrop in both scenes. Current evidence is in `artifacts/conversation-ui-1.0.11`; the Water fixtures show the transparent treatment and sharper material without reading the user's desktop. Checks cover clear panels and input, no text halo, Off / Lite / Water behavior, the bounded physical-resolution lens, and sampling cleanup when hidden.

STORY: Open from the radial center or assistant settings, see what the assistant recognized, choose a touch prompt or type a request, read a reply, and confirm a real action. Missing models and failed inference have explicit recovery controls.

FIRST VIEWPORT: One compact glass surface: title and close at top, two-line context, scrollable conversation, suggested action area, touch prompt row and a bottom input with Send/Stop. Classroom scaling retains 44-DIP targets. Existing timer cards remain separate beneath it; urgent notices interrupt conversation.

FORM: Local addition inside the established island. Preserve its docking and material system; no seed required. Signature interaction is the island opening into a focused conversation, with a gently flowing indicator only while a bounded request is in flight. Closing cancels inference and removes all motion.

FINISH: Ship for v1.0.11. The fresh quiet_glass_finish review accepted all four current conversation/suggestion captures with no material fixes. PRODUCT.md and DESIGN.md preserve the incumbent system and record this local transparency override and one-hour snooze. No shipping raster assets were added. The earlier v1.0.10 Send/Stop and directory-copy review remains historical evidence below.

## Implemented behavior and evidence · v1.0.10

The radial center, assistant page and tray open text conversation; there is no microphone or voice flow. The first view is 408 DIPs high and the conversation view 548 before scene scaling and work-area fitting. Replies scroll while the composer remains accessible. Enter sends, Shift+Enter adds a line, and Stop, hiding or closing cancels the request. Secondary controls retain 44-DIP minimum targets; the composer and primary action retain 54 DIPs. History remains in application memory and is bounded to eight messages, with an explicit Clear action.

Recognized process and software context includes PotPlayer aliases. Window-title context is opt-in and off by default; Free Island focus preserves the last external context. Action proposals are bounded to the existing supported actions and require a deliberate gesture. Model settings expose a selectable local base directory; switching stops and disables the model, preserves old files, and tells the user to enable or install a model explicitly.

The v1.0.10 water surface used the existing Off / Lite / Water implementation and local readable panels; the explicit v1.0.11 conversation override above supersedes those panels. The response line is code-drawn, only active while busy, capped at 24 FPS, and respects reduced motion and cancellation. No new raster is shipped, so there is no new raster provenance requirement.

The release review reports all 134 UI checks passing. Evidence is in `artifacts/conversation-ui-1.0.10`: ten application screenshots covering classroom and desktop scenes, plus two explicitly synthetic pointer-state screenshots. The synthetic captures document the tested hover/pressed treatment; they are not physical pointer-interaction evidence. White Send/Stop labels use the existing hover and pressed tokens, with reviewed contrast ratios of 5.87:1 and 7.55:1. The directory-switch notification accurately describes the disabled state and required next step. Hardware compatibility and classroom touch validation are not established by these development captures. GitHub release publication remains a separate release step.
## Quieter suggestion behavior and current finish evidence · v1.0.11

Keep automatic suggestions enabled according to the user's existing preferences, with 15 seconds of stable context, a 10-minute global display interval and a 30-minute interval for the same application. Empty inference waits 60 seconds before retrying. Fullscreen is checked before and after inference. Native Win7 adopts the rule timing/fullscreen changes only.

The WPF suggestion footer offers “1小时内不再建议” with at least 44 DIPs of touch height. It dismisses the current suggestion and persists a UTC deadline for one hour of silence across restart. Only automatic AI and rule suggestions are paused; manual chat and real timing, reminder and shutdown notices stay available. Win7 has no snooze UI.

The bounded verification reports 160 conversation assertions, 48 snooze checks, 82 context/timing checks and 37 native rule checks. The fresh finish review returned **ship** after inspecting all four current captures:

- `artifacts/conversation-ui-1.0.11/desktop-conversation-water-fixture.png`
- `artifacts/conversation-ui-1.0.11/classroom-conversation-water-fixture.png`
- `artifacts/assistant-snooze-1.0.11/desktop-lite-suggestion.png`
- `artifacts/assistant-snooze-1.0.11/classroom-water-suggestion.png`

Conversation fixtures use a fixed in-memory backdrop, not the user's desktop. High-detail refraction is confined to interactive Water conversation and preserves physical-pixel proportions within 1024 × 1024; Lite does not sample the background. Transparency is the user's explicit local override, not a newly claimed text-contrast guarantee. Physical old-Windows/classroom hardware validation remains outstanding. GitHub release publication is a separate release step.
