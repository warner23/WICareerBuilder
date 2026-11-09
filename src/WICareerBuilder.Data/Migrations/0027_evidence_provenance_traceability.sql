PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS career_evidence_provenance (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    evidence_id INTEGER NOT NULL,
    source_kind TEXT NOT NULL,
    source_reference TEXT NOT NULL DEFAULT '',
    learning_subject TEXT NOT NULL DEFAULT '',
    topic TEXT NOT NULL DEFAULT '',
    subtopic TEXT NOT NULL DEFAULT '',
    notes TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    FOREIGN KEY(evidence_id) REFERENCES career_evidence(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_career_evidence_provenance_evidence
ON career_evidence_provenance(evidence_id,created_utc,id);

UPDATE settings SET setting_value='27' WHERE setting_key='data_format_version';
