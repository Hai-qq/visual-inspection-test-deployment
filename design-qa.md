# Test Sequence Setting V2 Design QA

- Source visual truth: local review reference only; the source image is not stored in this repository.
- Implementation screenshots:
  - `%LocalAppData%\VisualInspectionTestDeployment\v2-wizard-preview.png`
  - `%LocalAppData%\VisualInspectionTestDeployment\v2-wizard-pose-preview.png`
  - `%LocalAppData%\VisualInspectionTestDeployment\v2-wizard-source-preview.png`
  - `%LocalAppData%\VisualInspectionTestDeployment\v2-wizard-models-preview.png`
  - `%LocalAppData%\VisualInspectionTestDeployment\v2-wizard-roi-preview.png`
  - `%LocalAppData%\VisualInspectionTestDeployment\v2-wizard-rule-preview.png`
  - `%LocalAppData%\VisualInspectionTestDeployment\v2-wizard-trigger-preview.png`
- Source pixels / density: 1872 × 1120 at 72 DPI
- Implementation pixels / density: 1380 × 860 at 96 DPI
- WPF viewport: 1380 × 860 device-independent pixels, light theme; the centered five-step strip plus step 2 USB-source selected, step 3 multi-model library, and step 4 test-step editor were captured in basic-information, target/ROI, pose-content, live-rule-summary and external-trigger states
- Normalization: the source is a low-detail structural wireframe rather than a pixel specification. The full desktop frames were fitted to the same visual scale; differences caused only by density or the source's missing product detail were not filed as defects.

## Findings

No actionable P0, P1, or P2 findings remain.

- Fonts and typography: Segoe UI with Chinese fallback, restrained industrial hierarchy and readable compact labels. Required-field stars, pose-action order numbers and selected-item headings do not collide or truncate at the supported viewport.
- Spacing and layout rhythm: the implementation preserves the source's ordered wizard-step row plus one dominant content region. The five current workflow steps are centered as one group across the top; former steps 4–7 are represented as four equal-width, unnumbered function tabs inside the selected test step, so one business object is not split across the primary navigation or visually restated as a second wizard. The model library and test-step editors retain a clear left-list/right-current-item rhythm, and primary actions remain visible above the persistent footer.
- Colors and visual tokens: the source's outline structure is mapped to the project's Schneider-style pale green, dark green and neutral borders. Red is reserved for required markers; amber is reserved for non-persistent or explanatory notices.
- Image quality and assets: the source contains no raster imagery, logo or decorative asset. Plus, minus and information controls use the Windows Segoe MDL2 icon family; pose-action sorting uses plain left/right arrows whose direction matches the horizontal action order.
- Copy and content: all visible product copy is Simplified Chinese. “目标检测（单张图）” and “姿态动作（连续帧）” explain the type distinction at the point of choice; the persistent preview notice prevents mock state from being mistaken for saved configuration.
- Accessibility and behavior: semantic WPF radio cards, buttons, list items, check boxes, combo boxes and inputs are keyboard-focusable. Help controls expose their ToolTip text as accessible descriptions. The Release construction smoke exercises centered five-step geometry at the 1120-pixel minimum width, USB source selection/green state, multi-model add/remove/type switching, dynamic model binding, unordered test-step add/remove with stable function identifiers, unnumbered function-tab navigation and bounds, explicit pose-action sorting with continuous renumbering, per-step ROI and rule state, external-signal visibility, Signal Tag validation, debounce/delay/timeout summary updates, ToolTip presence and final confirmation.
- Step semantics: pale green completion is reserved for a validated, explicitly confirmed step. Direct navigation changes only the current-step outline; skipped cards stay neutral, and a confirmed card returns to neutral as soon as one of its required values becomes invalid.
- Viewport resilience: the 1380 × 860 target has no clipped field, overlapping action, or footer collision. Automated geometry checks also confirm that the centered step group and all four test-step tabs remain inside their containers at the supported 1120-pixel minimum width.

## Full-view comparison evidence

The reference, test-step editor, target/ROI state, pose-content state, live-rule-summary state and external-trigger state were opened as comparison evidence. All retain the same interaction model: one centered row of ordered wizard cards across the top and one large, focused setting canvas below. The implementation adds only product-required hierarchy: actual step names, statuses, one selected-function editor, four unnumbered local function tabs and persistent previous/next navigation.

## Focused region comparison evidence

The source-selection region was inspected at full resolution with USB selected: the entire USB card has pale-green fill, dark-green border and a check glyph while the other two cards are neutral. The multi-model region was inspected with three different models visible at once: the current model is pale-green selected, add/remove controls are in stable header positions, and the right panel clearly scopes name, task type, file and label source to the selected model. The test-step basic region was inspected at full resolution because it contains the highest control density. The list explicitly says it has no execution order; every row shows name, detection type, required/optional state, invocation mode and a minus action, with no order badge or move controls. The pose screenshot verifies continuous `01/02/03` action numbers and left/right sorting controls. The ROI screenshot verifies the crosshair preview, dashed selection and redraw action; the rule screenshot verifies that target, ROI coordinates, operator and count appear together. The trigger screenshot verifies that stable identifier `TS-FAN-PRESENT`, Signal Tag, rising edge, debounce, delay, timeout and runtime source form one readable contract without clipping; lower execution fields remain reachable through the visible vertical scrollbar.

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
- [P1] Test-step cards still exposed `01/02/03` order badges and up/down controls even though these independently triggered functions do not have a local execution order.
  - Fix: converted the left panel to an explicitly unordered test-step collection, removed step order badges and move controls, and made function identifiers stable across add/delete operations.
- [P1] Pose actions are the level that requires ordering, but the horizontal execution direction and sorting controls were not explicit enough.
  - Fix: retained continuous pose-action numbers, labeled the direction “from left to right”, changed sorting controls to left/right arrows, and added smoke coverage that moves, renumbers and restores an action.
- Re-captured the 1380 × 860 test-step basic and pose-content states. No remaining P0/P1/P2 mismatch was found.

## Open Questions

- Formal Schneider brand typography and exact token values have not been provided; the current replaceable project tokens remain the accepted interim baseline.
- This remains a frontend-review build: dynamic model and trigger selection are only UI state; persistence, real model-file import/runtime validation, PLC/IO/sensor adapters and publication stay intentionally disconnected until the user approves the UI and supplies the first hardware/protocol contract.

## Implementation Checklist

- [x] Preserve the reference's ordered step strip and single content canvas.
- [x] Use the current five-step flow and group former parts 4–7 as one test-step setting.
- [x] Center the five primary steps as one group and use unnumbered, bounded function tabs inside step 4.
- [x] Present test steps as an unordered function collection with selection, plus and minus controls only.
- [x] Keep explicit continuous ordering and sorting controls inside pose-action sequences.
- [x] Show required/optional state and red stars for mandatory input.
- [x] Merge pose into detection type and switch following settings by type.
- [x] Support multiple independently configured model cards and reuse the collection in detection-item binding.
- [x] Provide ToolTips for concepts and destructive/ordering controls.
- [x] Support real pointer-drag ROI selection with live reference-coordinate backfill.
- [x] Keep target and pose final-judgment summaries synchronized with current inputs.
- [x] Keep ROI, rule and trigger/runtime values independent for each selected test step.
- [x] Expose and validate the frontend contract for sequence, external-signal and manual invocation.
- [x] Keep the prototype frontend-only and visibly non-persistent.
- [x] Build and capture the WPF implementation without XAML construction errors.

## Follow-up Polish

- [P3] Revisit small-caption contrast only after the formal Schneider token and target monitor specification are supplied.

final result: passed
