PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS career_portfolio_entries (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL,
    title TEXT NOT NULL,
    problem TEXT NOT NULL DEFAULT '',
    design_summary TEXT NOT NULL DEFAULT '',
    build_summary TEXT NOT NULL DEFAULT '',
    test_summary TEXT NOT NULL DEFAULT '',
    troubleshooting_summary TEXT NOT NULL DEFAULT '',
    result_summary TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'Draft',
    privacy_mode TEXT NOT NULL DEFAULT 'Private',
    user_approved_export INTEGER NOT NULL DEFAULT 0,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    FOREIGN KEY(project_id) REFERENCES career_projects(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS career_portfolio_evidence (
    portfolio_id INTEGER NOT NULL,
    evidence_id INTEGER NOT NULL,
    created_utc TEXT NOT NULL,
    PRIMARY KEY(portfolio_id,evidence_id),
    FOREIGN KEY(portfolio_id) REFERENCES career_portfolio_entries(id) ON DELETE CASCADE,
    FOREIGN KEY(evidence_id) REFERENCES career_evidence(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_career_portfolio_project ON career_portfolio_entries(project_id,status);
UPDATE settings SET setting_value='25' WHERE setting_key='data_format_version';
