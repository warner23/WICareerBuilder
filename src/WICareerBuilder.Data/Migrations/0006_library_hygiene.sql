ALTER TABLE resources ADD COLUMN discovery_decision TEXT NOT NULL DEFAULT 'Include';
ALTER TABLE resources ADD COLUMN discovery_reason TEXT NOT NULL DEFAULT 'Legacy discovered resource';
ALTER TABLE resources ADD COLUMN is_policy_excluded INTEGER NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS idx_resources_policy_excluded
    ON resources(library_source_id, is_policy_excluded);

CREATE TABLE IF NOT EXISTS discovery_exclusions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    library_source_id INTEGER NOT NULL,
    relative_path TEXT NOT NULL COLLATE NOCASE,
    file_name TEXT NOT NULL,
    extension TEXT NOT NULL,
    reason TEXT NOT NULL,
    last_scan_token TEXT NULL,
    last_seen_utc TEXT NOT NULL,
    UNIQUE(library_source_id, relative_path),
    FOREIGN KEY (library_source_id) REFERENCES library_sources(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_discovery_exclusions_source
    ON discovery_exclusions(library_source_id);
