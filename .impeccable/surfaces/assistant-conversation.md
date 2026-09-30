# Island conversation extension

## Current refinement contract · v1.0.12

The latest user request, “问浮岛的可读性稍微增强”, supersedes the fully transparent v1.0.11 resting text treatment only slightly. Preserve the sharp water lens and layout; use medium body weight and local 16–19% adaptive tints behind text, input and secondary actions, without a full-window wash or text blur. Typed text, caret and secondary labels follow the sampled light/dark ink. Keep primary cobalt actions and existing touch sizes. The user also requests faster glass refresh: target 16 ms while interacting and 50 ms at rest when visible; preserve no sampling in Lite and no work while hidden. These are scheduling targets, not a hardware frame-rate guarantee. Review both desktop/classroom scenes with synthetic light/dark background fixtures. Historical evidence below remains version-specific.

The same update adds a local skill library after the user selected combinations of existing Free Island tools. Generated action panels can be named and explicitly saved, listed, expanded for review and removed inside the conversation. Limit twelve saved skills to one to three unique existing operations each; reuse does not execute, and each real operation still needs its normal explicit gesture. Keep the existing chat composer, cancellation and urgent-notice precedence. No script runner or external application/file automation is part of this selected scope. Automatic suggestions are restricted to recognized players or Chrome titles ending in supported media-site labels, while manual conversation can suggest any of the existing supported actions. Chrome title text is used locally for automatic recognition and withheld from the model by default; other titles remain opt-in. A media title is not proof of playback.

Mode: Operate. Extend the existing WPF island and teaching-console identity for desktop and classroom touch. The user requests Siri-like conversation in the island, useful application-aware suggestions, and the existing water glass. This is a precisely scoped extension; no new visual-world selection or comp round.

## Direction contract

THESIS: Ask and act in the floating island, with the recognized context visible and proposed actions requiring a deliberate gesture.

OWN-WORLD: Existing clear glass edges, cobalt controls, adaptive Chinese text and authored outline icons. The current v1.0.12 conversation adds slight local text support to the transparent v1.0.11 foundation while preserving sharp refraction and the visible surrounding desktop. A small flowing blue-violet line belongs only to an active model response.

Historical conversation glass refinement · v1.0.11: the user explicitly requested “极致玻璃，不用可读性了” after finding this surface too foggy. That version replaced earlier pale backing with clear resting title, context, message, secondary-control and composer surfaces in Lite / Water, without a text halo. The v1.0.12 contract above now supersedes the fully transparent resting treatment. Send / Stop retains its cobalt action treatment, Off retains normal solid input/control treatment, and Windows high contrast retains system colors. The Water conversation keeps its bounded physical-resolution lens so a tall conversation is no longer stretched from a 180-pixel-high material. Other island surfaces retain their existing material preferences and backing.

The v1.0.11 conversation suite passed 160 assertions using synthetic replies and a fixed in-memory backdrop in both scenes. Historical evidence is in `artifacts/conversation-ui-1.0.11`; the Water fixtures show that version's transparent treatment and sharper material without reading the user's desktop. Checks covered clear panels and input, no text halo, Off / Lite / Water behavior, the bounded physical-resolution lens, and sampling cleanup when hidden.

STORY: Open from the radial center or assistant settings, see what the assistant recognized, choose a touch prompt or type a request, read a reply, and confirm a real action. Missing models and failed inference have explicit recovery controls.

FIRST VIEWPORT: One compact glass surface: title and close at top, two-line context, scrollable conversation, suggested action area, touch prompt row and a bottom input with Send/Stop. Classroom scaling retains 44-DIP targets. Existing timer cards remain separate beneath it; urgent notices interrupt conversation.

FORM: Local addition inside the established island. Preserve its docking and material system; no seed required. Signature interaction is the island opening into a focused conversation, with a gently flowing indicator only while a bounded request is in flight. Closing cancels inference and removes all motion.

FINISH: Ship for v1.0.12. The fresh finish review accepted all eight current Water light/dark, skill-library and skill-preview captures across desktop/classroom, with no material fixes. PRODUCT.md and DESIGN.md preserve the incumbent system and record the slight local text support, faster refresh scheduling and saved-skill controls. No shipping raster assets were added; HTML/CSS detector checks do not apply to WPF. The v1.0.11 transparency/snooze and v1.0.10 Send/Stop/directory-copy reviews remain historical evidence below.

## Implemented behavior and finish evidence · v1.0.12

Conversation body text uses medium weight. Local tints use alpha 42/255 for a light backing and 48/255 for a dark backing; input, caret and secondary labels adapt with the sampled ink. These remain local component treatments, not global tokens. The sharp Water lens stays bounded to 1024 × 1024 physical pixels. Visible Water refresh targets 16 ms during interaction and 50 ms at rest; the early-Windows route requests at 75 ms and reuses source pixels for 100 ms. Hidden sampling stops; Lite still does not sample. The response indicator keeps its independent 24-FPS cap.

The skill library saves one to three validated unique existing actions per skill, at most twelve skills, to `%APPDATA%\FreeIsland\assistant-skills.json`. Names accept 1–24 characters. Saving is explicit, the library lists and removes entries, and preview states “技能已展开 · 尚未执行”. Each click or slider gesture still performs only its existing supported operation; expansion does not execute and an active countdown is preserved. The composer, cancellation and urgent notices retain their existing behavior. Corrupt or externally changed storage is preserved with an error; switching model directories does not remove skills. This initial local agent scope adds no arbitrary code or shell execution.

Automatic suggestions require known players or Chrome media-site title suffixes; normal webpages and other app categories do not qualify. Chrome uses its title locally by default and exposes no raw title to the model without opt-in. Foreground eligibility is checked again after inference. Existing stability, cooldown, fullscreen and one-hour snooze rules remain; manual conversation retains all supported actions. Win7 receives the stricter media rule gate only, without WPF conversation or skills.

The final bounded verification reports 362 conversation UI assertions, 59 conversation-rule checks, 120 local-skill checks, 129 media-context checks, 18 automatic-action-filter checks, 62 native rule checks and 80 legacy-backdrop checks. UI tests use synthetic replies and safe mode without media, volume or shutdown effects. Separately, cached Qwen3 0.6B generated countdown:300, volume and open_reminders from a synthetic request, and the saved skill reread consistently. That probe made no downloads, executed no proposed actions and stopped the model afterward; evidence is in `artifacts/local-skills-1.0.12/probe-result.log`. The fresh finish review returned **ship** after these eight captures in `artifacts/conversation-ui-1.0.12`:

- `desktop-conversation-water-fixture.png` and `classroom-conversation-water-fixture.png`
- `desktop-conversation-water-dark-fixture.png` and `classroom-conversation-water-dark-fixture.png`
- `desktop-conversation-skill-library.png` and `classroom-conversation-skill-library.png`
- `desktop-conversation-skill-preview.png` and `classroom-conversation-skill-preview.png`

Water fixtures use fixed in-memory backgrounds, not the user's desktop. Model/runtime requirements are unchanged; physical old-Windows/classroom hardware, touch behavior and achieved frame rate remain unverified. Current release checks belong in VERIFICATION.md, and publication is a separate release step.

## Implemented behavior and evidence · v1.0.10

The radial center, assistant page and tray open text conversation; there is no microphone or voice flow. The first view is 408 DIPs high and the conversation view 548 before scene scaling and work-area fitting. Replies scroll while the composer remains accessible. Enter sends, Shift+Enter adds a line, and Stop, hiding or closing cancels the request. Secondary controls retain 44-DIP minimum targets; the composer and primary action retain 54 DIPs. History remains in application memory and is bounded to eight messages, with an explicit Clear action.

Recognized process and software context includes PotPlayer aliases. Window-title context is opt-in and off by default; Free Island focus preserves the last external context. Action proposals are bounded to the existing supported actions and require a deliberate gesture. Model settings expose a selectable local base directory; switching stops and disables the model, preserves old files, and tells the user to enable or install a model explicitly.

The v1.0.10 water surface used the existing Off / Lite / Water implementation and local readable panels; the v1.0.11 transparency change and current v1.0.12 refinement above supersede those panels. The response line is code-drawn, only active while busy, capped at 24 FPS, and respects reduced motion and cancellation. No new raster is shipped, so there is no new raster provenance requirement.

The release review reports all 134 UI checks passing. Evidence is in `artifacts/conversation-ui-1.0.10`: ten application screenshots covering classroom and desktop scenes, plus two explicitly synthetic pointer-state screenshots. The synthetic captures document the tested hover/pressed treatment; they are not physical pointer-interaction evidence. White Send/Stop labels use the existing hover and pressed tokens, with reviewed contrast ratios of 5.87:1 and 7.55:1. The directory-switch notification accurately describes the disabled state and required next step. Hardware compatibility and classroom touch validation are not established by these development captures. GitHub release publication remains a separate release step.
## Quieter suggestion behavior and historical finish evidence · v1.0.11

Keep automatic suggestions enabled according to the user's existing preferences, with 15 seconds of stable context, a 10-minute global display interval and a 30-minute interval for the same application. Empty inference waits 60 seconds before retrying. Fullscreen is checked before and after inference. Native Win7 adopts the rule timing/fullscreen changes only.

The WPF suggestion footer offers “1小时内不再建议” with at least 44 DIPs of touch height. It dismisses the current suggestion and persists a UTC deadline for one hour of silence across restart. Only automatic AI and rule suggestions are paused; manual chat and real timing, reminder and shutdown notices stay available. Win7 has no snooze UI.

The bounded verification reports 160 conversation assertions, 48 snooze checks, 82 context/timing checks and 37 native rule checks. The fresh finish review returned **ship** after inspecting all four current captures:

- `artifacts/conversation-ui-1.0.11/desktop-conversation-water-fixture.png`
- `artifacts/conversation-ui-1.0.11/classroom-conversation-water-fixture.png`
- `artifacts/assistant-snooze-1.0.11/desktop-lite-suggestion.png`
- `artifacts/assistant-snooze-1.0.11/classroom-water-suggestion.png`

Conversation fixtures use a fixed in-memory backdrop, not the user's desktop. High-detail refraction is confined to interactive Water conversation and preserves physical-pixel proportions within 1024 × 1024; Lite does not sample the background. Full transparency was the user's v1.0.11 local override, now superseded by v1.0.12's slight text support; neither is a text-contrast guarantee. Physical old-Windows/classroom hardware validation remains outstanding. GitHub release publication is a separate release step.
