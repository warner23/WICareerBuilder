PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS career_credentials (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    credential_type TEXT NOT NULL,
    title TEXT NOT NULL,
    issuer TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'Recorded',
    award_date TEXT NOT NULL DEFAULT '',
    expiry_date TEXT NOT NULL DEFAULT '',
    verification_status TEXT NOT NULL DEFAULT 'Unverified',
    user_confirmed INTEGER NOT NULL DEFAULT 0,
    evidence_id INTEGER NULL,
    notes TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    FOREIGN KEY(evidence_id) REFERENCES career_evidence(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_career_credentials_type_status ON career_credentials(credential_type,status,user_confirmed);
UPDATE settings SET setting_value='23' WHERE setting_key='data_format_version';
