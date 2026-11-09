-- v0.8.2 rescan reconciliation fix.
-- Keep hard filesystem noise out of the persistent review catalogue while preserving
-- existing catalogue exclusions, manual decisions, and Learning Collection package content.

ALTER TABLE discovery_scan_runs ADD COLUMN files_inspected INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN hard_noise_skipped INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN persistent_exclusions INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN catalogue_rows_after INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN included_after INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN review_after INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN excluded_after INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN exclusion_records_after INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN learning_collections_after INTEGER NOT NULL DEFAULT 0;
ALTER TABLE discovery_scan_runs ADD COLUMN learning_collection_items_after INTEGER NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS discovery_scan_changes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    discovery_scan_run_id INTEGER NOT NULL,
    library_source_id INTEGER NOT NULL,
    relative_path TEXT NOT NULL,
    file_name TEXT NOT NULL,
    change_type TEXT NOT NULL,
    discovery_decision TEXT NOT NULL DEFAULT '',
    discovery_reason TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    FOREIGN KEY(discovery_scan_run_id) REFERENCES discovery_scan_runs(id) ON DELETE CASCADE,
    FOREIGN KEY(library_source_id) REFERENCES library_sources(id) ON DELETE CASCADE,
    UNIQUE(discovery_scan_run_id, relative_path, change_type)
);

CREATE INDEX IF NOT EXISTS idx_discovery_scan_changes_run
    ON discovery_scan_changes(discovery_scan_run_id, change_type, relative_path);
CREATE INDEX IF NOT EXISTS idx_discovery_scan_changes_source
    ON discovery_scan_changes(library_source_id, discovery_scan_run_id DESC);

-- v0.8.1 could persist every raw software/config/web file as an exclusion record during
-- a rescan. Those rows are not useful catalogue decisions. Retain only exclusions that
-- correspond to a real catalogue resource, a saved manual override, or package content
-- under a Learning Collection candidate.
DELETE FROM discovery_exclusions
WHERE NOT EXISTS (
        SELECT 1 FROM resources r
        WHERE r.library_source_id = discovery_exclusions.library_source_id
          AND r.relative_path = discovery_exclusions.relative_path
    )
  AND NOT EXISTS (
        SELECT 1 FROM discovery_overrides o
        WHERE o.library_source_id = discovery_exclusions.library_source_id
          AND o.relative_path = discovery_exclusions.relative_path
    )
  AND NOT EXISTS (
        SELECT 1 FROM learning_collection_candidates lc
        WHERE lc.library_source_id = discovery_exclusions.library_source_id
          AND (
              discovery_exclusions.relative_path = lc.root_relative_path
              OR substr(discovery_exclusions.relative_path,1,length(lc.root_relative_path)+1) = lc.root_relative_path || '/'
          )
    );


-- Recover the useful "what changed" state from the immediately preceding v0.8.1 scan where
-- possible. v0.8.1 already stamped resources with the scan token and inventory_state, so newly
-- catalogued/modified rows can be reconstructed instead of losing the evidence behind the +770 change.
INSERT OR IGNORE INTO discovery_scan_changes(
    discovery_scan_run_id, library_source_id, relative_path, file_name, change_type,
    discovery_decision, discovery_reason, created_utc)
SELECT dsr.id, r.library_source_id, r.relative_path, r.file_name, r.inventory_state,
       r.discovery_decision, r.discovery_reason, COALESCE(dsr.completed_utc, dsr.started_utc)
FROM discovery_scan_runs dsr
JOIN resources r
  ON r.library_source_id=dsr.library_source_id AND r.last_scan_token=dsr.scan_token
WHERE dsr.id=(SELECT MAX(x.id) FROM discovery_scan_runs x WHERE x.library_source_id=dsr.library_source_id)
  AND r.inventory_state IN ('New','Modified');

-- Backfill the latest run for each source so the first v0.8.2 screen can explain the most recent
-- v0.8.1 rescan even before another filesystem scan is started.
UPDATE discovery_scan_runs
SET files_inspected = resources_seen + policy_excluded_files + unsupported_files_skipped,
    persistent_exclusions = (
        SELECT COUNT(*) FROM discovery_exclusions de
        WHERE de.library_source_id=discovery_scan_runs.library_source_id
          AND de.last_scan_token=discovery_scan_runs.scan_token
    ),
    hard_noise_skipped = MAX(0,
        policy_excluded_files - (
            SELECT COUNT(*) FROM discovery_exclusions de
            WHERE de.library_source_id=discovery_scan_runs.library_source_id
              AND de.last_scan_token=discovery_scan_runs.scan_token
        )
    ) + unsupported_files_skipped,
    new_resources = (
        SELECT COUNT(*) FROM discovery_scan_changes dsc
        WHERE dsc.discovery_scan_run_id=discovery_scan_runs.id AND dsc.change_type='New'
    ),
    modified_resources = (
        SELECT COUNT(*) FROM discovery_scan_changes dsc
        WHERE dsc.discovery_scan_run_id=discovery_scan_runs.id AND dsc.change_type='Modified'
    ),
    catalogue_rows_after = (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=discovery_scan_runs.library_source_id),
    included_after = (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=discovery_scan_runs.library_source_id AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include'),
    review_after = (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=discovery_scan_runs.library_source_id AND r.is_available=1 AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Review'),
    excluded_after = (SELECT COUNT(*) FROM resources r WHERE r.library_source_id=discovery_scan_runs.library_source_id AND r.is_policy_excluded=1),
    exclusion_records_after = (SELECT COUNT(*) FROM discovery_exclusions de WHERE de.library_source_id=discovery_scan_runs.library_source_id),
    learning_collections_after = (SELECT COUNT(*) FROM learning_collection_candidates lc WHERE lc.library_source_id=discovery_scan_runs.library_source_id),
    learning_collection_items_after = (
        SELECT COUNT(*) FROM (
            SELECT r.relative_path FROM resources r
            WHERE r.library_source_id=discovery_scan_runs.library_source_id AND EXISTS (
                SELECT 1 FROM learning_collection_candidates lc
                WHERE lc.library_source_id=r.library_source_id
                  AND (r.relative_path=lc.root_relative_path OR substr(r.relative_path,1,length(lc.root_relative_path)+1)=lc.root_relative_path||'/'))
            UNION
            SELECT de.relative_path FROM discovery_exclusions de
            WHERE de.library_source_id=discovery_scan_runs.library_source_id AND EXISTS (
                SELECT 1 FROM learning_collection_candidates lc
                WHERE lc.library_source_id=de.library_source_id
                  AND (de.relative_path=lc.root_relative_path OR substr(de.relative_path,1,length(lc.root_relative_path)+1)=lc.root_relative_path||'/'))
        )
    )
WHERE id=(SELECT MAX(x.id) FROM discovery_scan_runs x WHERE x.library_source_id=discovery_scan_runs.library_source_id);

UPDATE settings SET setting_value = '14' WHERE setting_key = 'data_format_version';
