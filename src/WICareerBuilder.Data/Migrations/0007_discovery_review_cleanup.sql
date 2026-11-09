PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS discovery_overrides (
    library_source_id INTEGER NOT NULL,
    relative_path TEXT NOT NULL COLLATE NOCASE,
    action TEXT NOT NULL CHECK(action IN ('Include','Exclude')),
    note TEXT NOT NULL DEFAULT '',
    updated_utc TEXT NOT NULL,
    PRIMARY KEY (library_source_id, relative_path),
    FOREIGN KEY (library_source_id) REFERENCES library_sources(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_discovery_overrides_source_action
    ON discovery_overrides(library_source_id, action);

CREATE TABLE IF NOT EXISTS learning_collection_candidates (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    library_source_id INTEGER NOT NULL,
    root_relative_path TEXT NOT NULL COLLATE NOCASE,
    display_name TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'Candidate',
    item_count INTEGER NOT NULL DEFAULT 0,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    UNIQUE(library_source_id, root_relative_path),
    FOREIGN KEY (library_source_id) REFERENCES library_sources(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_learning_collection_candidates_source
    ON learning_collection_candidates(library_source_id, status);

UPDATE settings SET setting_value = '7' WHERE setting_key = 'data_format_version';
