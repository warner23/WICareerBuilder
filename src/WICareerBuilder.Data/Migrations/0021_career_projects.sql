PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS career_projects (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL COLLATE NOCASE,
    category TEXT NOT NULL DEFAULT 'General',
    summary TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'Active',
    privacy_mode TEXT NOT NULL DEFAULT 'Private',
    no_web_access INTEGER NOT NULL DEFAULT 1,
    cv_eligible INTEGER NOT NULL DEFAULT 0,
    portfolio_eligible INTEGER NOT NULL DEFAULT 0,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_career_projects_name ON career_projects(name);

CREATE TABLE IF NOT EXISTS career_project_resources (
    project_id INTEGER NOT NULL,
    resource_id INTEGER NOT NULL,
    relationship_kind TEXT NOT NULL DEFAULT 'ProjectMaterial',
    notes TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    PRIMARY KEY(project_id, resource_id),
    FOREIGN KEY(project_id) REFERENCES career_projects(id) ON DELETE CASCADE,
    FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS career_project_journal (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL,
    entry_type TEXT NOT NULL DEFAULT 'Note',
    title TEXT NOT NULL DEFAULT '',
    body TEXT NOT NULL DEFAULT '',
    is_private INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    FOREIGN KEY(project_id) REFERENCES career_projects(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_career_project_resources_project ON career_project_resources(project_id);
CREATE INDEX IF NOT EXISTS idx_career_project_journal_project ON career_project_journal(project_id, created_utc DESC);

UPDATE settings SET setting_value='21' WHERE setting_key='data_format_version';
