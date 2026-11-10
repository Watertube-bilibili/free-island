# Island conversation extension

## Current refinement contract · v1.0.13

Extend the local skill library within the incumbent clear water conversation: rename skills, adjust countdown duration with touch sliders and presets, export a local JSON library, preview names and real actions before explicit import, and look up saved skills with “使用技能 名称” in the composer. These local commands can work without inference and only expand the action panel. Preserve explicit per-action execution and the existing media-only automatic suggestion boundary. Do not introduce arbitrary scripts or external application automation.

The user's latest glass requirement is at least 30 FPS. Visible dynamic Water now targets 16 ms at rest and during interaction through `CompositionTarget.Rendering` with a shared budgeted timer fallback. Optimize full-detail optics and source mapping caches instead of reducing the sharp lens resolution or adding blur. Measure achieved rates with changing in-memory fixtures, distinguish material-update throughput from compositor FPS and real legacy-window throughput, and retain no sampling while hidden or in Lite/Off. Native Win7 synchronizes the release version only. All v1.0.12 material/readability preferences below still apply; its 50 ms resting schedule is historical.

## Previous refinement contract · v1.0.12

The v1.0.12 user request, “问浮岛的可读性稍微增强”, superseded the fully transparent v1.0.11 resting text treatment only slightly. Preserve the sharp water lens and layout; use medium body weight and local 16–19% adaptive tints behind text, input and secondary actions, without a full-window wash or text blur. Typed text, caret and secondary labels follow the sampled light/dark ink. Keep primary cobalt actions and existing touch sizes. That release targeted 16 ms while interacting and 50 ms at rest when visible; the v1.0.13 contract above supersedes those scheduling values. Preserve no sampling in Lite and no work while hidden. Scheduling targets are not a hardware frame-rate guarantee. Review both desktop/classroom scenes with synthetic light/dark background fixtures. Historical evidence below remains version-specific.

The same update adds a local skill library after the user selected combinations of existing Free Island tools. Generated action panels can be named and explicitly saved, listed, expanded for review and removed inside the conversation. Limit twelve saved skills to one to three unique existing operations each; reuse does not execute, and each real operation still needs its normal explicit gesture. Keep the existing chat composer, cancellation and urgent-notice precedence. No script runner or external application/file automation is part of this selected scope. Automatic suggestions are restricted to recognized players or Chrome titles ending in supported media-site labels, while manual conversation can suggest any of the existing supported actions. Chrome title text is used locally for automatic recognition and withheld from the model by default; other titles remain opt-in. A media title is not proof of playback.

Mode: Operate. Extend the existing WPF island and teaching-console identity for desktop and classroom touch. The user requests Siri-like conversation in the island, useful application-aware suggestions, and the existing water glass. This is a precisely scoped extension; no new visual-world selection or comp round.

## Direction contract

THESIS: Ask and act in the floating island, with the recognized context visible and proposed actions requiring a deliberate gesture.

OWN-WORLD: Existing clear glass edges, cobalt controls, adaptive Chinese text and authored outline icons. The current v1.0.13 conversation retains v1.0.12's slight local text support over the transparent v1.0.11 foundation while preserving sharp refraction and the visible surrounding desktop. Skill editing and transfer reuse the same components and tints. A small flowing blue-violet line belongs only to an active model response.

Historical conversation glass refinement · v1.0.11: the user explicitly requested “极致玻璃，不用可读性了” after finding this surface too foggy. That version replaced earlier pale backing with clear resting title, context, message, secondary-control and composer surfaces in Lite / Water, without a text halo. The v1.0.12 contract above now supersedes the fully transparent resting treatment. Send / Stop retains its cobalt action treatment, Off retains normal solid input/control treatment, and Windows high contrast retains system colors. The Water conversation keeps its bounded physical-resolution lens so a tall conversation is no longer stretched from a 180-pixel-high material. Other island surfaces retain their existing material preferences and backing.

The v1.0.11 conversation suite passed 160 assertions using synthetic replies and a fixed in-memory backdrop in both scenes. Historical evidence is in `artifacts/conversation-ui-1.0.11`; the Water fixtures show that version's transparent treatment and sharper material without reading the user's desktop. Checks covered clear panels and input, no text halo, Off / Lite / Water behavior, the bounded physical-resolution lens, and sampling cleanup when hidden.

STORY: Open from the radial center or assistant settings, see what the assistant recognized, choose a touch prompt or type a request, read a reply, and confirm a real action. Missing models and failed inference have explicit recovery controls.

FIRST VIEWPORT: One compact glass surface: title and close at top, two-line context, scrollable conversation, suggested action area, touch prompt row and a bottom input with Send/Stop. Skill editing temporarily hides the prompt row and composer so Save and Cancel remain visible in both scenes, then restores them on exit. Classroom scaling retains 44-DIP targets. Existing timer cards remain separate beneath it; urgent notices interrupt conversation.

FORM: Local addition inside the established island. Preserve its docking and material system; no seed required. Signature interaction is the island opening into a focused conversation, with a gently flowing indicator only while a bounded request is in flight. Closing cancels inference and removes all motion.

FINISH: The v1.0.13 scope is editable and transferable local skills plus faster full-detail Water updates within the incumbent system. PRODUCT.md and DESIGN.md record the editor reachability, bounded import, local lookup and current refresh contract. Current finish disposition and release checks belong in VERIFICATION.md. No shipping raster assets were added; HTML/CSS detector checks do not apply to WPF. The v1.0.12 ship decision and earlier reviews remain historical evidence below.

## Implemented behavior and evidence · v1.0.13 current

The skill library adds Edit, Import and Export. Editing keeps a 1–24-character name field and concrete operation labels; an existing countdown supports a 60–14,400-second touch slider and 3/5/10/25-minute presets. Changes apply only after “保存修改”, while Cancel discards the draft. The prompt row and composer are hidden only while editing so Save and Cancel remain reachable in desktop/classroom; returning to the library or preview restores them. Local text tints, adaptive ink, cobalt primary actions and 44-DIP controls retain the v1.0.12 treatment.

Export writes a validated library to a user-selected JSON file. Import previews names and real actions before explicit “确认导入”. Validate the complete bounded document within 64 KB, twelve skills and one to three unique supported operations per skill. Skip identical content, give new entries fresh IDs and reject over-capacity batches without partial changes. Commit accepted additions atomically; cancellation or failure preserves existing skills, and imported IDs do not overwrite local entries. No import, edit, save or expansion executes an operation.

“使用技能 名称”, “打开技能 名称”, “展开技能 名称” and “运行技能 名称” perform local lookup without inference. Prefer exact names, otherwise find name matches; expand a unique result or show a selection for ambiguous results. Real actions still need their existing click or slider gesture, and an active countdown is preserved. Automatic media eligibility, title privacy, cooldowns, fullscreen exclusion and one-hour snooze remain unchanged.

Visible Water uses a 16 ms target at rest and during interaction, driven by `CompositionTarget.Rendering` and one shared budgeted low-priority timer fallback. Work already spent is deducted from the fallback delay. Full-detail optics and source mapping caches preserve the 1024 × 1024 physical-pixel lens budget. The early-Windows route retains 32 ms request/source-cache intervals and a single global worker. Hidden surfaces unsubscribe and release capture state; Lite and Off do not sample. The response line retains its independent 24-FPS cap and reduced-motion behavior.

The current-machine 660 × 822 changing in-memory fixture measured 51.12 material updates per second at rest and 46.58 during sustained deformation. This includes fixture generation/capture, full-detail optics and `WritePixels`, and excludes real desktop capture, compositor presentation and physical display output. It meets the requested 30-FPS target in that controlled test only. Evidence is in `artifacts/glass-performance-1.0.13`; the rates do not establish early-Windows, external-window or classroom-display throughput. Conversation captures and final checks are in `artifacts/conversation-ui-1.0.13` and VERIFICATION.md.

Transient skill-file I/O failures preserve the source and allow an explicit retry; corrupt or externally changed files remain protected. Failed assistant preference writes restore the in-memory/UI preference, and closing the model stops its process even when persisting the disabled preference fails. The base .NET 4.6 / earliest-Windows-10 target remains unchanged; Win7 receives the release version without the WPF conversation or refraction additions.

## Implemented behavior and finish evidence · v1.0.12

Conversation body text uses medium weight. Local tints use alpha 42/255 for a light backing and 48/255 for a dark backing; input, caret and secondary labels adapt with the sampled ink. These remain local component treatments, not global tokens. The sharp Water lens stays bounded to 1024 × 1024 physical pixels. In v1.0.12, visible Water refresh targeted 16 ms during interaction and 50 ms at rest; the early-Windows route requested at 75 ms and reused source pixels for 100 ms. These scheduling/cache values are historical and superseded by v1.0.13 above. Hidden sampling stops; Lite still does not sample. The response indicator keeps its independent 24-FPS cap.

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

The v1.0.10 water surface used the existing Off / Lite / Water implementation and local readable panels; the v1.0.11 transparency change and v1.0.12 readability refinement retained by v1.0.13 above supersede those panels. The response line is code-drawn, only active while busy, capped at 24 FPS, and respects reduced motion and cancellation. No new raster is shipped, so there is no new raster provenance requirement.

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
