-- v0.8.5 learning / project-artifact separation.
-- A tracked resource can now belong to the learner-facing knowledge/library lane, the project/evidence
-- artifact lane, or both. Project artifacts do not enter the staged learning fingerprint/deep-analysis
-- queue merely because they are stored under a project folder.

ALTER TABLE resources ADD COLUMN catalogue_lane TEXT NOT NULL DEFAULT 'Learning';
ALTER TABLE resources ADD COLUMN catalogue_lane_reason TEXT NOT NULL DEFAULT 'Legacy learning/reference catalogue';

CREATE INDEX IF NOT EXISTS idx_resources_catalogue_lane
    ON resources(library_source_id, catalogue_lane, is_policy_excluded, discovery_decision, is_available);

ALTER TABLE discovery_scan_runs ADD COLUMN learning_catalogue_rows_after INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN project_artifact_rows_after INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN dual_lane_rows_after INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN low_value_project_noise_skipped INTEGER NOT NULL DEFAULT 0;

ALTER TABLE discovery_scan_changes ADD COLUMN catalogue_lane TEXT NOT NULL DEFAULT 'Learning';
ALTER TABLE discovery_scan_changes ADD COLUMN lane_reason TEXT NOT NULL DEFAULT '';

-- v0.8.4 role assignments were explicitly low-confidence discovery suggestions. Remove only those
-- suggestions so the refined v0.8.5 lane policy can recreate them; Accepted/user/analysis roles remain.
DELETE FROM resource_role_assignments
WHERE status='Suggested' AND assignment_source='v0.8.4 discovery context';

UPDATE settings SET setting_value = '17' WHERE setting_key = 'data_format_version';
