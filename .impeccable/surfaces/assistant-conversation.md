# Island conversation extension

Mode: Operate. Extend the existing WPF island and teaching-console identity for desktop and classroom touch. The user requests Siri-like conversation in the island, useful application-aware suggestions, and the existing water glass. This is a precisely scoped extension; no new visual-world selection or comp round.

## Direction contract

THESIS: Ask and act in the floating island, with the recognized context visible and proposed actions requiring a deliberate gesture.

OWN-WORLD: Existing clear glass edges, pale readable interior, cobalt controls, dark Chinese type and authored outline icons. Keep the surrounding desktop visible. A small flowing blue-violet line belongs only to an active model response.

STORY: Open from the radial center or assistant settings, see what the assistant recognized, choose a touch prompt or type a request, read a reply, and confirm a real action. Missing models and failed inference have explicit recovery controls.

FIRST VIEWPORT: One compact glass surface: title and close at top, two-line context, scrollable conversation, suggested action area, touch prompt row and a bottom input with Send/Stop. Classroom scaling retains 44-DIP targets. Existing timer cards remain separate beneath it; urgent notices interrupt conversation.

FORM: Local addition inside the established island. Preserve its docking and material system; no seed required. Signature interaction is the island opening into a focused conversation, with a gently flowing indicator only while a bounded request is in flight. Closing cancels inference and removes all motion.

FINISH: Ship. The fresh finish review accepted the final implementation after correction of primary Send/Stop hover and pressed fills and model-directory status copy. PRODUCT.md and DESIGN.md document the bounded extension; no shipping raster assets were added.

## Implemented behavior and evidence · v1.0.10

The radial center, assistant page and tray open text conversation; there is no microphone or voice flow. The first view is 408 DIPs high and the conversation view 548 before scene scaling and work-area fitting. Replies scroll while the composer remains accessible. Enter sends, Shift+Enter adds a line, and Stop, hiding or closing cancels the request. Secondary controls retain 44-DIP minimum targets; the composer and primary action retain 54 DIPs. History remains in application memory and is bounded to eight messages, with an explicit Clear action.

Recognized process and software context includes PotPlayer aliases. Window-title context is opt-in and off by default; Free Island focus preserves the last external context. Action proposals are bounded to the existing supported actions and require a deliberate gesture. Model settings expose a selectable local base directory; switching stops and disables the model, preserves old files, and tells the user to enable or install a model explicitly.

The water surface uses the existing Off / Lite / Water implementation and local readable panels. The response line is code-drawn, only active while busy, capped at 24 FPS, and respects reduced motion and cancellation. No new raster is shipped, so there is no new raster provenance requirement.

The release review reports all 134 UI checks passing. Evidence is in `artifacts/conversation-ui-1.0.10`: ten application screenshots covering classroom and desktop scenes, plus two explicitly synthetic pointer-state screenshots. The synthetic captures document the tested hover/pressed treatment; they are not physical pointer-interaction evidence. White Send/Stop labels use the existing hover and pressed tokens, with reviewed contrast ratios of 5.87:1 and 7.55:1. The directory-switch notification accurately describes the disabled state and required next step. Hardware compatibility and classroom touch validation are not established by these development captures. GitHub release publication remains a separate release step.
