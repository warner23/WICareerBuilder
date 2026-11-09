# WICareerBuilder v0.9.2 — Hierarchical Confidence & Concept-Aware Placement

- Added independent subject/topic/subtopic placement confidence.
- Added partial hierarchy application: confident subject/topic may be saved while a weaker subtopic remains review-only.
- Added shared concept anchors for DC starters, DC motor types, DC generators, torque/operation, AC induction/synchronous motors and stepper types.
- Increased explicit title-purpose authority over generic body vocabulary.
- Re-evaluates all prior `ContentAnalysis:*` automatic organisation while preserving manual/legacy organisation.
- Persistent placement reports now store and show hierarchy confidence levels.
- Added migration `0019_hierarchical_confidence_concept_placement.sql` and data format 19.
- Bumped structured analyser and placement proposal version to 0.9.2.

## Previous milestone

v0.9.1 separated machine-family and purpose scoring, added contradiction caps, and safely corrected v0.9.0 auto-placements.
