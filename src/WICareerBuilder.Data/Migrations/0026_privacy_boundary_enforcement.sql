PRAGMA foreign_keys = ON;

ALTER TABLE career_portfolio_entries ADD COLUMN no_web_access INTEGER NOT NULL DEFAULT 1;
UPDATE career_portfolio_entries
SET no_web_access = COALESCE((SELECT cp.no_web_access FROM career_projects cp WHERE cp.id=career_portfolio_entries.project_id),1);

CREATE TABLE IF NOT EXISTS career_privacy_access_audit (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_kind TEXT NOT NULL,
    source_id INTEGER NOT NULL,
    operation TEXT NOT NULL,
    privacy_mode TEXT NOT NULL,
    no_web_access INTEGER NOT NULL,
    allowed INTEGER NOT NULL,
    reason TEXT NOT NULL,
    created_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_career_privacy_access_audit_source
ON career_privacy_access_audit(source_kind,source_id,created_utc DESC);

UPDATE settings SET setting_value='26' WHERE setting_key='data_format_version';
