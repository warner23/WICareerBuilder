PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS settings (
    setting_key TEXT PRIMARY KEY,
    setting_value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS subjects (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL COLLATE NOCASE UNIQUE,
    created_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS library_sources (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    root_path TEXT NOT NULL COLLATE NOCASE UNIQUE,
    exclusions_json TEXT NOT NULL DEFAULT '[]',
    is_enabled INTEGER NOT NULL DEFAULT 1,
    last_discovered_utc TEXT NULL,
    created_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS source_subjects (
    library_source_id INTEGER NOT NULL,
    subject_id INTEGER NOT NULL,
    PRIMARY KEY (library_source_id, subject_id),
    FOREIGN KEY (library_source_id) REFERENCES library_sources(id) ON DELETE CASCADE,
    FOREIGN KEY (subject_id) REFERENCES subjects(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS resources (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    library_source_id INTEGER NOT NULL,
    relative_path TEXT NOT NULL COLLATE NOCASE,
    file_name TEXT NOT NULL,
    extension TEXT NOT NULL,
    size_bytes INTEGER NOT NULL,
    last_write_utc TEXT NOT NULL,
    media_kind TEXT NOT NULL,
    resource_type TEXT NOT NULL DEFAULT 'Unknown',
    discovery_status TEXT NOT NULL DEFAULT 'Discovered',
    analysis_status TEXT NOT NULL DEFAULT 'NotAnalysed',
    is_available INTEGER NOT NULL DEFAULT 1,
    is_ignored INTEGER NOT NULL DEFAULT 0,
    last_scan_token TEXT NULL,
    discovered_utc TEXT NOT NULL,
    last_seen_utc TEXT NOT NULL,
    UNIQUE (library_source_id, relative_path),
    FOREIGN KEY (library_source_id) REFERENCES library_sources(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_resources_file_name ON resources(file_name);
CREATE INDEX IF NOT EXISTS idx_resources_media_kind ON resources(media_kind);
CREATE INDEX IF NOT EXISTS idx_resources_source ON resources(library_source_id);
CREATE INDEX IF NOT EXISTS idx_resources_available ON resources(is_available, is_ignored);

CREATE TABLE IF NOT EXISTS resource_subjects (
    resource_id INTEGER NOT NULL,
    subject_id INTEGER NOT NULL,
    PRIMARY KEY (resource_id, subject_id),
    FOREIGN KEY (resource_id) REFERENCES resources(id) ON DELETE CASCADE,
    FOREIGN KEY (subject_id) REFERENCES subjects(id) ON DELETE CASCADE
);

INSERT OR IGNORE INTO settings(setting_key, setting_value) VALUES ('application_name', 'WICareerBuilder');
INSERT OR IGNORE INTO settings(setting_key, setting_value) VALUES ('data_format_version', '1');
