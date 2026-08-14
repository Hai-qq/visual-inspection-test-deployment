# Test Sequence Setting V2 Design QA

- Source visual truth: `C:\Users\12252\Downloads\a9c9cdedcb032b58a4ea4b211e0bfa1d.png`
- Implementation screenshots:
  - `C:\Users\12252\AppData\Local\VisualInspectionTestDeployment\v2-wizard-preview.png`
  - `C:\Users\12252\AppData\Local\VisualInspectionTestDeployment\v2-wizard-pose-preview.png`
  - `C:\Users\12252\AppData\Local\VisualInspectionTestDeployment\v2-wizard-source-preview.png`
  - `C:\Users\12252\AppData\Local\VisualInspectionTestDeployment\v2-wizard-models-preview.png`
  - `C:\Users\12252\AppData\Local\VisualInspectionTestDeployment\v2-wizard-roi-preview.png`
  - `C:\Users\12252\AppData\Local\VisualInspectionTestDeployment\v2-wizard-rule-preview.png`
- Source pixels / density: 1872 × 1120 at 72 DPI
- Implementation pixels / density: 1380 × 860 at 96 DPI
- WPF viewport: 1380 × 860 device-independent pixels, light theme; step 2 USB-source selected, step 3 multi-model library, step 4 ordered-item editor, step 5 target/ROI state, step 5 pose-content state and step 6 live-rule-summary state
- Normalization: the source is a low-detail structural wireframe rather than a pixel specification. The full desktop frames were fitted to the same visual scale; differences caused only by density or the source's missing product detail were not filed as defects.

## Findings

No actionable P0, P1, or P2 findings remain.

- Fonts and typography: Segoe UI with Chinese fallback, restrained industrial hierarchy and readable compact labels. Required-field stars, list-order numbers and selected-item headings do not collide or truncate at the supported viewport.
- Spacing and layout rhythm: the implementation preserves the source's ordered step row plus one dominant content region. Eight actual workflow steps replace the source's five placeholders by explicit requirement. The model library and ordered-item editors both use a clear left-list/right-current-item rhythm, and primary actions remain visible above the persistent footer.
- Colors and visual tokens: the source's outline structure is mapped to the project's Schneider-style pale green, dark green and neutral borders. Red is reserved for required markers; amber is reserved for non-persistent or explanatory notices.
- Image quality and assets: the source contains no raster imagery, logo or decorative asset. Plus, minus, move and information controls use the Windows Segoe MDL2 icon family rather than handcrafted symbols or placeholder artwork.
- Copy and content: all visible product copy is Simplified Chinese. “目标检测（单张图）” and “姿态动作（连续帧）” explain the type distinction at the point of choice; the persistent preview notice prevents mock state from being mistaken for saved configuration.
- Accessibility and behavior: semantic WPF radio cards, buttons, list items, check boxes, combo boxes and inputs are keyboard-focusable. Help controls expose their ToolTip text as accessible descriptions. The Release construction smoke exercises USB source selection/green state, multi-model add/remove/type switching, dynamic model binding, ordered navigation, detection-item add/remove, target/pose type switching, ROI drag mapping/coordinate backfill, target/pose rule-summary updates, invalid-range blocking, ToolTip presence and final confirmation.
- Step semantics: pale green completion is reserved for a validated, explicitly confirmed step. Direct navigation changes only the current-step outline; skipped cards stay neutral, and a confirmed card returns to neutral as soon as one of its required values becomes invalid.
- Viewport resilience: the 1380 × 860 target has no clipped field, overlapping action, or footer collision. The existing minimum window width and outer scrolling remain available for smaller supported desktop windows.

## Full-view comparison evidence

The reference, ordered-item editor, target/ROI state, pose-content state and live-rule-summary state were opened as comparison evidence. All retain the same interaction model: ordered cards across the top and one large, focused setting canvas below. The implementation adds only product-required hierarchy: actual step names, statuses, a selected-item editor and persistent previous/next navigation.

## Focused region comparison evidence

The source-selection region was inspected at full resolution with USB selected: the entire USB card has pale-green fill, dark-green border and a check glyph while the other two cards are neutral. The multi-model region was inspected with three different models visible at once: the current model is pale-green selected, add/remove controls are in stable header positions, and the right panel clearly scopes name, task type, file and label source to the selected model. The ordered-item region was inspected at full resolution because it contains the highest control density. The add action is in the list header; every row shows order, detection type, required/optional state, move controls and a minus action. The ROI screenshot verifies the crosshair preview, dashed selection and redraw action; a real Windows pointer drag changed the visible reference coordinates from `181/125/482/360` to `211/180/432/323`. The rule screenshot verifies that the complete target, ROI coordinates, operator and count appear together in the final summary without clipping. The pose-content screenshot separately verifies that selecting the pose type replaces ROI/target inputs with an ordered action canvas and does not reintroduce a standalone pose step.

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

## Open Questions

- Formal Schneider brand typography and exact token values have not been provided; the current replaceable project tokens remain the accepted interim baseline.
- This remains a frontend-review build: the dynamic model selection is only UI state; persistence, real model-file import/runtime validation and publication stay intentionally disconnected until the user approves the UI.

## Implementation Checklist

- [x] Preserve the reference's ordered step strip and single content canvas.
- [x] Use the actual eight-step flow rather than hard-coding five placeholders.
- [x] Add ordered detection-item cards with plus, minus and move controls.
- [x] Show required/optional state and red stars for mandatory input.
- [x] Merge pose into detection type and switch following settings by type.
- [x] Support multiple independently configured model cards and reuse the collection in detection-item binding.
- [x] Provide ToolTips for concepts and destructive/ordering controls.
- [x] Support real pointer-drag ROI selection with live reference-coordinate backfill.
- [x] Keep target and pose final-judgment summaries synchronized with current inputs.
- [x] Keep the prototype frontend-only and visibly non-persistent.
- [x] Build and capture the WPF implementation without XAML construction errors.

## Follow-up Polish

- [P3] Revisit small-caption contrast only after the formal Schneider token and target monitor specification are supplied.

final result: passed
