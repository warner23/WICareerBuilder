-- v0.8 staged scanner foundation.
-- Discovery remains metadata-first. Content fingerprinting and deeper analysis are separate, resumable stages.

ALTER TABLE resources ADD COLUMN inventory_state TEXT NOT NULL DEFAULT 'Unknown';
ALTER TABLE resources ADD COLUMN quick_fingerprint TEXT NOT NULL DEFAULT '';
ALTER TABLE resources ADD COLUMN fingerprint_kind TEXT NOT NULL DEFAULT '';
ALTER TABLE resources ADD COLUMN fingerprint_status TEXT NOT NULL DEFAULT 'Pending';
ALTER TABLE resources ADD COLUMN fingerprinted_utc TEXT NULL;
ALTER TABLE resources ADD COLUMN analysis_needed INTEGER NOT NULL DEFAULT 1;

CREATE TABLE IF NOT EXISTS discovery_scan_runs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    library_source_id INTEGER NOT NULL,
    scan_token TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'Running',
    resources_seen INTEGER NOT NULL DEFAULT 0,
    policy_excluded_files INTEGER NOT NULL DEFAULT 0,
    directories_visited INTEGER NOT NULL DEFAULT 0,
    directories_skipped INTEGER NOT NULL DEFAULT 0,
    infrastructure_directories_skipped INTEGER NOT NULL DEFAULT 0,
    unsupported_files_skipped INTEGER NOT NULL DEFAULT 0,
    error_count INTEGER NOT NULL DEFAULT 0,
    new_resources INTEGER NOT NULL DEFAULT 0,
    modified_resources INTEGER NOT NULL DEFAULT 0,
    unchanged_resources INTEGER NOT NULL DEFAULT 0,
    missing_resources INTEGER NOT NULL DEFAULT 0,
    analysis_needed INTEGER NOT NULL DEFAULT 0,
    started_utc TEXT NOT NULL,
    completed_utc TEXT NULL,
    FOREIGN KEY(library_source_id) REFERENCES library_sources(id) ON DELETE CASCADE,
    UNIQUE(library_source_id, scan_token)
);

CREATE INDEX IF NOT EXISTS idx_resources_inventory_state ON resources(library_source_id, inventory_state, is_available, is_policy_excluded);
CREATE INDEX IF NOT EXISTS idx_resources_analysis_needed ON resources(library_source_id, analysis_needed, is_available, is_policy_excluded);
CREATE INDEX IF NOT EXISTS idx_resources_fingerprint_status ON resources(library_source_id, fingerprint_status, is_available, is_policy_excluded);
CREATE INDEX IF NOT EXISTS idx_resources_quick_fingerprint ON resources(quick_fingerprint) WHERE quick_fingerprint <> '';
CREATE INDEX IF NOT EXISTS idx_discovery_scan_runs_source ON discovery_scan_runs(library_source_id, started_utc DESC);

-- Existing catalogues pre-date the staged scanner. Treat currently available items as requiring
-- one deliberate fingerprint/analysis decision rather than claiming they were already processed.
UPDATE resources
SET inventory_state = CASE WHEN is_available = 1 THEN 'Existing' ELSE 'Missing' END,
    fingerprint_status = CASE WHEN is_available = 1 THEN 'Pending' ELSE 'Unavailable' END,
    analysis_needed = CASE
        WHEN is_available = 0 OR is_policy_excluded = 1 OR is_ignored = 1 THEN 0
        WHEN analysis_status IN ('Analysed','Limited') THEN 0
        ELSE 1
    END;
