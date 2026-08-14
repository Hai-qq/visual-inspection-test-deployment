# Test Sequence Setting V2 Design QA

- Source visual truth: `C:\Users\12252\Downloads\a9c9cdedcb032b58a4ea4b211e0bfa1d.png`
- Implementation screenshot: `C:\Users\12252\AppData\Local\VisualInspectionTestDeployment\v2-wizard-preview.png`
- Source pixels / density: 1872 × 1120 at 72 DPI
- Implementation pixels / density: 1380 × 860 at 96 DPI
- WPF viewport: 1380 × 860 device-independent pixels, light theme, first step selected
- Normalization: the source is a low-detail structural wireframe rather than a pixel specification. Both captures were compared as full-frame desktop views after fitting their outer frames to the same visual scale; no density-only difference was filed as a defect.

## Findings

No actionable P0, P1, or P2 findings remain.

- Fonts and typography: Segoe UI with Chinese fallback, restrained weight hierarchy, readable labels and no clipped or wrapped critical copy in all nine inspected steps.
- Spacing and layout rhythm: the implementation preserves the source's top ordered step row plus one large content region. Nine real workflow steps replace the source's five placeholders by explicit requirement; the step row remains a single ordered strip and supports horizontal scrolling.
- Colors and visual tokens: the source's blue outline is intentionally mapped to the project's Schneider-style pale green, dark green and neutral border tokens. Selected, completed, pending and optional states remain distinguishable without entertainment-style decoration.
- Image quality and assets: the source contains no raster imagery, icons, logo or decorative assets to reproduce. The ROI rectangle is a functional selection surface, not a replacement for source artwork.
- Copy and content: all visible product copy is Simplified Chinese and describes the actual configuration order. The persistent “V2 前端预览 · 不会保存配置” notice prevents the mock data from being mistaken for saved state.
- Accessibility and behavior: semantic WPF buttons, radio buttons, check boxes, combo boxes and text inputs are keyboard-focusable. Previous/next, top-step navigation, optional-pose skip and final confirmation states were exercised; the footer keeps persistent navigation visible.

## Full-view comparison evidence

The reference and implementation were opened together in one comparison pass. Both show an ordered row of step cards at the top and one dominant bordered content region beneath it. The implementation adds only product-required hierarchy: a compact application header, Chinese step names/statuses, a current-step heading and persistent footer navigation. These additions preserve rather than alter the reference interaction model.

## Focused region comparison evidence

A separate crop was not required because the source contains only outline rectangles and step numbers; it has no detailed typography, imagery, icons or control styling that would benefit from a tighter source crop. The implementation's detailed regions were nevertheless inspected individually through steps 1–9. The ROI page received a separate post-fix Windows capture to verify that its preview and coordinates no longer collapse.

## Comparison history

### Pass 1

- [P2] Primary action hover used the Windows accent treatment instead of the project green because the keyed button style did not inherit the base button template.
  - Fix: added a V2-specific primary button style based on the shared WPF button template and applied it to persistent next and ROI actions.
- [P2] The ROI preview initially sized to its minimum desired width, producing a narrow selection rectangle.
  - Fix: assigned the ROI step a 1000-DIP content width within the minimum supported desktop viewport; the final capture shows a wide, usable preview and readable coordinate summary.

### Pass 2

- Re-captured the 1380 × 860 first step and the corrected ROI state.
- Re-ran all nine visible steps, previous/next navigation, direct top-step navigation and final confirmation; automated construction smoke additionally covers the optional-pose skip.
- No remaining P0/P1/P2 mismatch was found.

## Open Questions

- Formal Schneider brand typography and exact token values have not been provided; the current replaceable project tokens remain the accepted interim baseline.
- The nine step names, ordering and field density require user visual approval before V1 persistence and validation logic is migrated.

## Implementation Checklist

- [x] Preserve the reference's ordered step strip and single content canvas.
- [x] Use the actual nine-step business flow instead of hard-coding five placeholders.
- [x] Keep the prototype frontend-only and visibly non-persistent.
- [x] Verify all primary navigation and optional-step states.
- [x] Build and capture the WPF implementation without XAML construction errors.

## Follow-up Polish

- [P3] Revisit small-caption contrast only after the formal Schneider token and target monitor specification are supplied.

final result: passed
