PRAGMA foreign_keys = ON;

ALTER TABLE resource_organisation ADD COLUMN organisation_source TEXT NOT NULL DEFAULT 'LegacyUnknown';
ALTER TABLE resource_organisation ADD COLUMN organisation_confidence REAL NOT NULL DEFAULT 0.0;

CREATE TABLE IF NOT EXISTS resource_classification_proposals (
    resource_id INTEGER PRIMARY KEY,
    learning_subject TEXT NOT NULL DEFAULT '',
    topic TEXT NOT NULL DEFAULT '',
    subtopic TEXT NOT NULL DEFAULT '',
    resource_type TEXT NOT NULL DEFAULT 'Unknown',
    confidence REAL NOT NULL DEFAULT 0.0,
    proposal_status TEXT NOT NULL DEFAULT 'Suggested',
    reason TEXT NOT NULL DEFAULT '',
    analyser_version TEXT NOT NULL DEFAULT '',
    source_fingerprint TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    applied_utc TEXT NULL,
    FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS content_placement_runs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    library_source_id INTEGER NULL,
    focus_text TEXT NOT NULL DEFAULT '',
    requested INTEGER NOT NULL DEFAULT 0,
    examined INTEGER NOT NULL DEFAULT 0,
    fingerprinted INTEGER NOT NULL DEFAULT 0,
    analysed INTEGER NOT NULL DEFAULT 0,
    cached INTEGER NOT NULL DEFAULT 0,
    auto_organised INTEGER NOT NULL DEFAULT 0,
    needs_review INTEGER NOT NULL DEFAULT 0,
    protected_existing INTEGER NOT NULL DEFAULT 0,
    limited INTEGER NOT NULL DEFAULT 0,
    failed INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'Running',
    started_utc TEXT NOT NULL,
    completed_utc TEXT NULL,
    FOREIGN KEY(library_source_id) REFERENCES library_sources(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS content_placement_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_id INTEGER NOT NULL,
    resource_id INTEGER NOT NULL,
    learning_subject TEXT NOT NULL DEFAULT '',
    topic TEXT NOT NULL DEFAULT '',
    subtopic TEXT NOT NULL DEFAULT '',
    resource_type TEXT NOT NULL DEFAULT 'Unknown',
    confidence REAL NOT NULL DEFAULT 0.0,
    disposition TEXT NOT NULL DEFAULT '',
    reason TEXT NOT NULL DEFAULT '',
    fingerprinted_this_run INTEGER NOT NULL DEFAULT 0,
    analysis_status TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    FOREIGN KEY(run_id) REFERENCES content_placement_runs(id) ON DELETE CASCADE,
    FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_classification_proposals_status ON resource_classification_proposals(proposal_status, confidence DESC);
CREATE INDEX IF NOT EXISTS idx_content_placement_runs_source ON content_placement_runs(library_source_id, id DESC);
CREATE INDEX IF NOT EXISTS idx_content_placement_items_run ON content_placement_items(run_id, confidence DESC, resource_id);

UPDATE settings SET setting_value = '18' WHERE setting_key = 'data_format_version';
