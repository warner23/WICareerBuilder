PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS career_cvs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL COLLATE NOCASE UNIQUE,
    target_role TEXT NOT NULL DEFAULT '',
    focus_key TEXT NOT NULL DEFAULT 'General',
    status TEXT NOT NULL DEFAULT 'Draft',
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS career_cv_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    cv_id INTEGER NOT NULL,
    source_kind TEXT NOT NULL,
    source_id INTEGER NOT NULL,
    heading TEXT NOT NULL DEFAULT '',
    statement TEXT NOT NULL DEFAULT '',
    sort_order INTEGER NOT NULL DEFAULT 0,
    user_approved INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    FOREIGN KEY(cv_id) REFERENCES career_cvs(id) ON DELETE CASCADE,
    UNIQUE(cv_id, source_kind, source_id)
);

CREATE INDEX IF NOT EXISTS idx_career_cv_items_cv ON career_cv_items(cv_id,sort_order,id);
UPDATE settings SET setting_value='24' WHERE setting_key='data_format_version';
