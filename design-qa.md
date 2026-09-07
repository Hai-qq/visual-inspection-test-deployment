# Test Sequence Setting V2 Design QA

> Current status (2026-08-29): Passes 1–16 below are retained as historical review evidence. Pass 17 and the current requirements/design/acceptance documents supersede their earlier product-model, optional-step, ToolTip, ROI-scaling and final-action decisions.

- Source visual truth: local review reference only; the source image is not stored in this repository.
- Historical implementation screenshots (Pass 1–10 evidence; not the current acceptance artifact):
  - `%LocalAppData%\VisualInspectionTestDeployment\v2-wizard-preview.png`
  - `%LocalAppData%\VisualInspectionTestDeployment\v2-wizard-pose-preview.png`
  - `%LocalAppData%\VisualInspectionTestDeployment\v2-wizard-source-preview.png`
  - `%LocalAppData%\VisualInspectionTestDeployment\v2-wizard-models-preview.png`
  - `%LocalAppData%\VisualInspectionTestDeployment\v2-wizard-roi-preview.png`
  - `%LocalAppData%\VisualInspectionTestDeployment\v2-wizard-rule-preview.png`
  - `%LocalAppData%\VisualInspectionTestDeployment\v2-wizard-trigger-preview.png`
- Source pixels / density: 1872 × 1120 at 72 DPI
- Current live review: the isolated Release R2 WPF demo was opened on 2026-08-29 with the in-app Windows control surface. Live checks covered visible hover ToolTips, a real `5712 × 4284` ROI background rendered at its original ratio, and `设置 → 应用到当前操作台` rebuilding an ONNX-ready `FAN-A01` Operator while retaining the current Folder source. The packaged Fan acceptance smoke also completed the real ONNX Runtime CPU path.
- Current WPF evidence: centered five-step strip; production-style model `FAN-A01`; mutually exclusive image/video Folder sources and disabled pending camera cards; three visible model task types; all test steps participating in the total verdict without a required/optional selector; add/remove/direct-edit labels; compatible-model selection dialog; one default ROI gated by an imported, aspect-ratio-preserved background image; visible dark hover ToolTips; normal/pose Basic Information and Custom Function tabs; temporary pose action-order modal; independent Apply and Export actions; Folder filename serial mapping and Camera-only serial dialog; sequence import/export; sequence-driven rule/result table; original-image Detection overlay containing boxes and original English Output Labels without Target translations or confidence text; qualification-rate naming.
- Normalization: the source is a low-detail structural wireframe rather than a pixel specification. The full desktop frames were fitted to the same visual scale; differences caused only by density or the source's missing product detail were not filed as defects.

## Findings

No actionable P0, P1, or P2 findings remain.

- Fonts and typography: Segoe UI with Chinese fallback, restrained industrial hierarchy and readable compact labels. Required-field stars, pose-action order numbers and selected-item headings do not collide or truncate at the supported viewport.
- Spacing and layout rhythm: the implementation preserves the source's ordered wizard-step row plus one dominant content region. The five workflow steps are centered as one group across the top. Both normal and pose test steps keep only Basic Information and Custom Function; their extra configuration appears as task-specific temporary dialogs. The model library and test-step editors retain a clear left-list/right-current-item rhythm, and primary actions remain visible above the persistent footer.
- Colors and visual tokens: the source's outline structure is mapped to the project's Schneider-style pale green, dark green and neutral borders. Red is reserved for required markers; amber is reserved for non-persistent or explanatory notices.
- Image quality and assets: the source contains no raster imagery, logo or decorative asset. Plus, minus and information controls use the Windows Segoe MDL2 icon family; pose-action sorting uses plain left/right arrows whose direction matches the horizontal action order.
- Copy and content: all visible product copy is Simplified Chinese. “目标检测（单张图）”、“姿态动作（连续帧）” and “图像分割（单张图）” explain the type distinction at the point of choice. Frontend-only notices explicitly state that video, segmentation and custom functions are not connected runtime capabilities; Folder filename serial mapping and the supported ONNX Detection path are implemented rather than presented as placeholders.
- Accessibility and behavior: semantic WPF radio cards, buttons, list items, check boxes, combo boxes and inputs are keyboard-focusable. Help controls expose their ToolTip text as accessible descriptions and use a minimum `22 × 22` transparent hit target, short initial delay and long visible duration. The Release construction smoke exercises the five-step geometry, mutually exclusive Folder sources, disabled cameras, three-task model UI, label add/remove/direct-edit, one top-to-bottom test-step list, compatible-model dialogs, one default ROI plus aspect-ratio background-image gate, Basic Information/Custom Function tabs, pose-action popup, per-step custom-function state, independent Apply/Export actions, Folder filename serial mapping, Camera serial dialog construction, sequence import/export, original-English-label-only image overlay, sequence-driven rule/result table, qualification-rate copy and ToolTips.
- Step semantics: pale green completion is reserved for a validated, explicitly confirmed step. Direct navigation changes only the current-step outline; skipped cards stay neutral, and a confirmed card returns to neutral as soon as one of its required values becomes invalid.
- Viewport resilience: the current 1368 × 855 live windows have no clipped primary action, overlapping modal, or footer collision. Automated geometry checks also confirm that the centered step group and all visible type-specific test-step tabs remain inside their containers at the supported 1120-pixel minimum width.

## Full-view comparison evidence

The current wizard, source page, model page, test-step basic page, detection modal, pose action page and custom-function page were opened as live comparison evidence. All retain the same interaction model: one centered row of ordered wizard cards across the top and one large, focused setting canvas below. Type-specific tabs reduce irrelevant controls without changing the primary five-step navigation or persistent previous/next actions.

## Focused region comparison evidence

The source-selection region keeps Image Folder and Video Folder mutually exclusive, while both camera choices remain disabled pending adapters. The model region shows detection, pose/temporal and segmentation and supports label add, remove and direct editing without a label-source dropdown. The normal-step basic region opens a compatible-model dialog and then a Label list; each Label uses whole image or one default ROI, and ROI drawing remains blocked until a background image is imported. Imported ROI imagery uses `Uniform` scaling, centered letterboxing and coordinate mapping only inside the actual image viewport. The pose flow keeps Basic Information and Custom Function tabs and opens the action-order popup only when required. The final page keeps Apply and Export as independent actions. The Operator live view verifies Folder filename serial mapping, Camera-only serial dialog behavior, “合格率” copy, sequence-driven logic/measurement/result rows, and the original Fan image with Detection boxes plus `Labell / Black_wire / white_wire`. Target Chinese names and confidence values are not drawn on the image.

## Comparison history

### Pass 1

- [P2] The first ordered-item implementation placed the add and delete-current actions below the initial viewport.
  - Fix: moved the add action into the ordered-list header, moved delete-current beside the selected-item sequence badge, tightened row padding and kept all primary actions visible.
- [P2] Pose was initially represented as its own wizard step, forcing ordinary projects through an irrelevant decision page.
  - Fix: reduced the flow from nine to eight actual steps and made pose an option in the detection-type field. Steps 5 and 6 now switch between target and pose-specific settings.

### Pass 2

- Re-captured the 1380 × 860 ordered-item editor and pose-content state.
- Verified red required markers, information descriptions, ordered cards, plus/minus controls and target/pose content switching.
- No remaining P0/P1/P2 mismatch was found.

### Pass 3

- [P2] The original image-source choices used a permanently green outer folder border, so choosing USB changed the radio value without moving the visible selected-card state.
  - Fix: replaced each border-wrapped radio with one full-card `RadioButton` template whose background, border and check glyph are driven by `IsChecked`.
- Captured the 1380 × 860 source-selection page with USB selected and added a Release UI smoke assertion for both the checked value and the green background token.
- No remaining P0/P1/P2 mismatch was found.

### Pass 4

- [P1] The first model-import page represented only one model, while a project can require multiple independent detection and posture models.
  - Fix: replaced the single form with a project model library. The left list supports repeated add/select/delete, the right form owns the current model's independent settings, and detection items read the same dynamic collection.
- Captured the 1380 × 860 multi-model page and added Release UI smoke checks for initial multi-model visibility, add/remove, task-type switching and dynamic detection-item binding.
- No remaining P0/P1/P2 mismatch was found.

### Pass 5

- [P1] Navigating directly to a later step previously marked every skipped intermediate step green, even though no configuration had been confirmed.
  - Fix: removed position-based completion inference. A step now becomes complete only after its own required validation passes and “下一步” confirms it; invalidated required data removes completion immediately, and final confirmation requires every previous step.
- Re-captured the step 3 multi-model page after a direct jump. Steps 1 and 2 remain neutral while only step 3 carries the current-step green outline. Release UI smoke covers direct jump, required-field invalidation and invalid-model navigation blocking.
- No remaining P0/P1/P2 mismatch was found.

### Pass 6

- [P1] The V2 target-content page showed a fixed ROI rectangle and coordinates, but the preview did not handle pointer input and “重新框选区域” had no behavior.
  - Fix: added WPF mouse capture and drag handling, live `640 × 480` coordinate mapping, redraw guidance, minimum-size rejection, Escape/capture-loss recovery and step-validity refresh.
- Captured the 1380 × 860 ROI page, added Release UI construction-smoke coverage, and executed the full button-then-drag path through real Windows pointer input. The rectangle and both coordinate/status texts changed together.
- No remaining P0/P1/P2 mismatch was found.

### Pass 7

- [P1] The rule editor's “最终判定” was a fixed `fan / ROI / 等于 1` sentence, so changing the visible controls produced no feedback and could contradict the configured values.
  - Fix: generate target and pose summaries from the selected item and current controls, bind target choices to the selected model labels, expose the range maximum only for range mode, and reject inverted ranges.
- Captured the 1380 × 860 rule page and added Release UI construction-smoke checks for ROI inheritance, equal/range/greater switching, target/pose live summaries and invalid-range navigation blocking.
- No remaining P0/P1/P2 mismatch was found.

### Pass 8

- [P1] Wizard parts 4–7 described one test function but were split into four primary steps, and the former runtime page exposed only global delay/source values with no signal boundary.
  - Fix: reduced the primary flow to five steps, converted part 4 into a selected test-step editor with four local subpages, made ROI/rule/runtime values independent per step, and added an explicit external-signal contract with Signal Tag, edge/level condition, debounce, trigger delay, function timeout and runtime source.
- Captured the 1380 × 860 basic-information and external-trigger states. Release UI construction smoke now verifies five-step navigation, the four-subpage grouping, missing-Signal-Tag blocking, trigger-summary updates and final trigger aggregation.
- The screen explicitly labels the PLC/IO/sensor fields as a frontend contract and does not imply that a hardware adapter or persisted configuration already exists.
- No remaining P0/P1/P2 mismatch was found.

### Pass 9

- [P2] The five primary step cards were laid out at content width inside the scroll viewer, leaving the full sequence visibly biased to the left.
  - Fix: center the step collection whenever it fits the viewport, use symmetric card margins, and retain horizontal scrolling only for genuine overflow.
- [P1] The merged test-step workspace still displayed `1–4` on its local controls, visually recreating a second wizard and crowding the last label against its border.
  - Fix: remove local numbering and “subpage x / 4” copy, replace the controls with four equal-width function tabs, and clip content to each tab boundary.
- Re-captured the 1380 × 860 basic-information and external-trigger states. Release UI construction smoke verifies centered geometry plus unnumbered, in-bounds function tabs at the 1120-pixel minimum width.
- No remaining P0/P1/P2 mismatch was found.

### Pass 10

- [P1] The user-facing term was incorrectly written as “测试部”; the product object is a “测试步”.
  - Fix: changed all current V2 interface, validation, acceptance and design copy to “测试步” / Test Step.
- [P1] An earlier pass treated test steps as unordered, which conflicted with the later confirmed rule that the list itself defines execution order.
  - Fix: the current left panel explicitly executes from top to bottom, adds new steps at the end, and provides up/down controls; the separate Sequence Plan remains removed.
- [P1] Pose actions are the level that requires ordering, but the horizontal execution direction and sorting controls were not explicit enough.
  - Fix: retained continuous pose-action numbers, labeled the direction “from left to right”, changed sorting controls to left/right arrows, and added smoke coverage that moves, renumbers and restores an action.
- Re-captured the 1380 × 860 test-step basic and pose-content states. No remaining P0/P1/P2 mismatch was found.

### Pass 11 — 2026-08-21 approved frontend v0.2

- [P1] Switching test steps or rebinding a model could leave detection children from the previous model visible, and the Label dialog exposed a second model selector that did not write back to the test step.
  - Fix: made Basic Information the single model-binding source, changed the Label dialog to a read-only current-model display, reconciled incompatible children/rules on rebind, and added smoke coverage for two model-distinct test steps.
- [P1] Model removal existed only as a small icon in the current-model editor, so the model library did not clearly communicate that models can be added and removed.
  - Fix: placed explicit “添加模型 / 删除当前” controls together in the model-list header. Removal targets the selected model and retains the existing referenced-model and last-model guards.
- [P1] Local video was absent and the source UI could not express an image/camera source together with posture video.
  - Fix: retained the three mutually exclusive primary source cards and added Local Video as an independent checkable card with a conditional path editor. No decoding or runtime claim was added.
- [P1] Model import exposed Image Classification and gave manual labels too much permanent space.
  - Fix: the visible task choices are Detection, Pose/Temporal and Segmentation; Auto Detect / Manual Entry is a compact dropdown and the manual editor appears only when selected.
- [P1] Normal detection split one decision across Basic Information, Detection Content and Judgment tabs.
  - Fix: Basic Information opens a Label selector. Selecting one Label opens its own detail modal; that modal combines whole image or one/multiple named ROIs, the fixed-camera notice and that Label's judgment fields.
- [P1] The first modal implementation treated Label selection as one batch and rebuilt the entire child collection on Apply, so configuring or re-editing one Label could erase previously saved Labels.
  - Fix: Save now has add-or-replace semantics keyed by Label. It returns to the Label list after each save; adding a second Label preserves the first, and re-editing the first preserves the second and its ROI/rule summary. Release smoke covers both retention directions.
- [P1] Pose and runtime demonstration controls did not match the reviewed mental model.
  - Fix: the old visible Trigger & Runtime form is replaced with a per-step Custom Function page limited to type, name, file, delay and description. The existing production Trigger/Runner domain contracts remain untouched and are not represented as hardware integration.
- [P1] Pose still exposed Action Settings as a persistent third tab, although action order is only needed immediately after selecting a pose/temporal model or when explicitly re-editing it.
  - Fix: both inspection types now keep only Basic Information and Custom Function tabs. Pose model selection opens a temporary action-order popup; Save returns to a Basic Information summary/re-edit entry, while Cancel/Esc restores the pre-open state.
- [P1] Operator copy was ambiguous about what should be removed from ONNX output.
  - Fix: the image keeps Detection boxes and ROI names but omits model label/confidence text. A serial-number frontend entry was added above Start, still requiring an explicit click, and the statistics mode is named “合格率”.
- [P1] Draft/Validation/Publish/Assign/Activate/Rollback controls and Deployment/Lifecycle summaries mixed future architecture work into the current frontend review.
  - Fix: the footer now contains only Previous/Next, the final page only summarizes reviewed frontend content, and the top badge reads “前端界面确认稿”. Model SHA/Adapter/Runtime Profile and internal FunctionCode are also hidden from the visible UI while underlying code remains untouched.
- Verified with isolated Release build, Release construction smoke, 167 xUnit tests, format verification and live Windows inspection of both wizard and Operator windows. No actionable P0, P1 or P2 finding remains in this frontend-only increment.

### Pass 12 — 2026-08-21 segmentation test-step consistency

- [P1] Model import exposed Image Segmentation, but Test Step Basic Information still offered only Detection and Pose; binding a segmentation model also forced the step back to Detection.
  - Fix: added “图像分割（单张图）” as a distinct test-step type, synchronized bound model task changes to the selected step type, retained the same single-frame per-Label whole-image/multi-ROI and judgment dialogs, and preserved the type while switching test steps.
- Draft restore now maps Segmentation back to the distinct frontend type instead of displaying it as Detection. This remains frontend state and does not claim a segmentation runtime, mask output or production judgment path.
- Verified with an isolated Release build and Release construction smoke covering the third option, model linkage, step switching and the per-Label popup.

### Pass 13 — 2026-08-26 test-step interaction repair

- [P1] The test-step model dropdown exposed models from incompatible task types, so a pose step could temporarily bind a detection model.
  - Fix: filter the dropdown by the selected detection type, automatically rebind an available compatible model on type change, show a clear Step 03 instruction when none exists, and retain the pose-only action-order popup.
- [P1] Opening and closing the current model dropdown without choosing a different model could clear the visible selection or open the Label chooser.
  - Fix: capture the model when the dropdown opens and apply model-dependent rebuilding only after an actual selection change.
- [P1] The per-Label ROI dialog did not expose the current multi-ROI selection beside the judgment fields, and the full-image layout left too much unused space.
  - Fix: add a live scope summary to the judgment panel, rebalance full-image versus ROI column widths, reduce the preview height, and remove the nested rule-panel scroll container.
- [P2] The Custom Function page had per-step state but no local way to change the current test step.
  - Fix: add a current-step selector to that page and preserve each step's independent function values while switching.
- Verified with an isolated Release build and the Release WPF construction smoke covering model filtering, pose auto-popup, live multi-ROI summary and direct Custom Function step switching.

### Pass 14 — 2026-08-26 source-card feedback repair

- [P1] Selecting USB Camera or Industrial Camera changed only the card highlight while leaving the Folder path form visible, making both choices appear non-functional.
  - Fix: switch the detail area to a camera-specific parameter panel, update the title and connection explanation for DirectShow versus vendor adapters, and preserve separate device identifiers while moving between the two camera types.
- The panel exposes only frontend fields (device, resolution, frame rate, pixel format, trigger mode and timeout) and explicitly states that enumeration, connection testing, preview and SDK integration remain pending.
- Verified with an isolated Release build and WPF construction smoke covering Folder → USB → Industrial → USB → Folder state transitions and Local Video coexistence.

### Pass 15 — 2026-08-26 source-mode simplification

- [P1] The separate Local Video checkbox caused Image Folder to remain selected, implying that both sources would always be combined.
  - Fix: make Image Folder and Video Folder mutually exclusive source cards, reuse one folder-browser panel with type-specific copy, and preserve one path per source while switching.
- [P1] Camera parameter panels suggested a configurable capability before the USB and industrial-camera scope had been approved.
  - Fix: remove the camera parameter panel and present both camera cards as disabled, restrained “待开发” placeholders.
- Pass 15 supersedes the Pass 14 camera-panel interaction. Camera enumeration, SDK integration, image/video reading and runtime behavior remain outside this frontend increment.

### Pass 16 — 2026-08-27 SE meeting alignment and original-label-only overlay

- [P1] The runtime image still appended confidence values to every Detection label, while the latest review requested a cleaner production view.
  - Fix: keep the original image and Detection boxes, resolve each Model Binding back to its original English Output Label, do not substitute the Target Chinese display name, and continue using confidence internally for filtering and rule evaluation.
- The Operator now derives Folder serial numbers from image filenames, requests a serial number only for Camera sources, and presents sequence-defined label logic, actual values and red/green Result states in a table.
- The V2 flow now supports model deletion to empty, label add/remove/direct edit, compatible-model selection dialogs, one default ROI gated by a background image, optional default-0.5 confidence, and portable sequence plus referenced-model export/import.
- Verified with 194 Release xUnit tests, format verification, WPF construction smoke, isolated demo publication and a live real Fan ONNX run showing 7 boxes with original English labels and no Target translations or confidence text.

### Pass 17 — 2026-08-29 production-model, ToolTip, ROI and final-action repair

- [P1] The sequence name still behaved like a descriptive test title instead of a production-line product model.
  - Fix: use `FAN-A01` as the built-in product model, prefix the representative project/model display names with the same code, and refresh stale built-in sample configuration. The descriptive “风扇检测” remains a test-step name rather than a model identifier.
- [P1] “是否必选 / 设为必选项” exposed a schema compatibility detail without a clear operator meaning.
  - Fix: remove the selector and optional-state copy from the settings UI; every setting-side test step now participates in the product verdict, while the compatibility field remains in the underlying schema.
- [P1] Information glyphs had ToolTip descriptions but the visible popup did not reliably appear during pointer hover.
  - Fix: add a visible dark ToolTip style, minimum hit area, explicit hover delay/duration/placement and window-loaded propagation to all help controls. A live pointer leave/re-enter check displayed the product-model help text.
- [P1] The imported ROI background filled its wide preview surface and distorted the source image.
  - Fix: render with `Uniform`, center the real image viewport, reject clicks in letterbox space and map ROI coordinates against the imported image pixel dimensions. Live import of `IMG_1533.JPG` preserved its `5712 × 4284` ratio.
- [P1] The final action implied that export was the only completion path.
  - Fix: separate “应用到当前操作台” from “导出 Sequence 与模型”. Export prompts for a destination and keeps settings open; Apply validates and rebuilds the current Operator. The v1-to-v2 editor migration now retains the known local Folder/Camera address so direct Apply does not lose the current image source, while portable export still strips the machine-local Deployment Binding.
- Verified with 195 Release xUnit tests, format verification, Release WPF construction smoke, packaged acceptance/startup smoke, live ROI and ToolTip inspection, and a complete R2 `设置 → 应用` run that retained a ready one-file `5712 × 4284` Folder source plus the real ONNX Runtime model.

## Open Questions

- Formal Schneider brand typography and exact token values have not been provided; the current replaceable project tokens remain the accepted interim baseline.
- The factory still needs to provide the final serial-number validation/duplicate policy, custom-function samples/call contract, video runtime requirements and actual camera/PLC/IO/Line hardware/protocol details.
- Video decoding, segmentation/pose model execution, Custom Function execution and real camera/PLC/IO/Line adapters remain pending. Supported Folder plus static ONNX Detection, production TXT/image storage and portable sequence delivery are implemented and separately acceptance-tested.

## Implementation Checklist

- [x] Preserve the reference's ordered step strip and single content canvas.
- [x] Use the current five-step flow and group former parts 4–7 as one test-step setting.
- [x] Center the five primary steps as one group and use bounded, type-specific function tabs inside step 4.
- [x] Present test steps as one top-to-bottom ordered list with selection, add/remove and up/down controls; do not add a second Sequence Plan.
- [x] Keep explicit continuous ordering and sorting controls inside pose-action sequences.
- [x] Treat every setting-side test step as participating in the total verdict; do not expose a required/optional selector, and retain red stars for mandatory inputs.
- [x] Keep pose as a test-step type and expose its ordered actions only in a temporary popup opened from Basic Information.
- [x] Support multiple independently configured model cards and reuse the collection in detection-item binding.
- [x] Remove Image Classification from the current model UI and maintain labels through add, remove and direct editing without a source dropdown.
- [x] Keep Image Folder and Video Folder mutually exclusive with independent remembered paths; retain USB and industrial cameras as disabled pending-development cards.
- [x] Provide ToolTips for concepts and destructive/ordering controls.
- [x] Support real pointer-drag ROI selection with live reference-coordinate backfill.
- [x] Open one detail popup per selected Label, with independent whole-image/single-ROI scope and judgment; add or replace only that Label on save.
- [x] Keep one default ROI per Label and block drawing/saving until a background image is imported.
- [x] Keep Custom Function name, file and delay independent for each selected test step without executing Python.
- [x] Derive Folder serial numbers from image filenames, request serial numbers only for Camera sources, retain explicit Start and use qualification-rate naming.
- [x] Retain the original image and Detection/ROI geometry, draw original English Output Labels, and omit Target Chinese names and confidence text from the runtime image.
- [x] Keep the new frontend-only fields visibly separated from persistence, execution and hardware claims.
- [x] Build and capture the WPF implementation without XAML construction errors.

## Follow-up Polish

- [P3] Revisit small-caption contrast only after the formal Schneider token and target monitor specification are supplied.

final result: passed for the approved frontend v0.2 scope; backend/runtime items above remain pending
