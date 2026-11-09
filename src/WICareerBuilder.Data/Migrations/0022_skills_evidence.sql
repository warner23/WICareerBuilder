PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS career_skills (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL COLLATE NOCASE UNIQUE,
    category TEXT NOT NULL DEFAULT 'General',
    description TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'Active',
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS career_evidence (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    evidence_kind TEXT NOT NULL DEFAULT 'Document',
    evidence_stage TEXT NOT NULL DEFAULT 'Mentioned',
    description TEXT NOT NULL DEFAULT '',
    project_id INTEGER NULL,
    resource_id INTEGER NULL,
    practical_evidence INTEGER NOT NULL DEFAULT 0,
    understanding_demonstrated INTEGER NOT NULL DEFAULT 0,
    verification_status TEXT NOT NULL DEFAULT 'Unverified',
    cv_ready_recommended INTEGER NOT NULL DEFAULT 0,
    cv_ready_user_approved INTEGER NOT NULL DEFAULT 0,
    privacy_mode TEXT NOT NULL DEFAULT 'Private',
    no_web_access INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    FOREIGN KEY(project_id) REFERENCES career_projects(id) ON DELETE SET NULL,
    FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS career_evidence_skills (
    evidence_id INTEGER NOT NULL,
    skill_id INTEGER NOT NULL,
    strength TEXT NOT NULL DEFAULT 'Supporting',
    notes TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    PRIMARY KEY(evidence_id, skill_id),
    FOREIGN KEY(evidence_id) REFERENCES career_evidence(id) ON DELETE CASCADE,
    FOREIGN KEY(skill_id) REFERENCES career_skills(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_career_evidence_project ON career_evidence(project_id, evidence_stage);
CREATE INDEX IF NOT EXISTS idx_career_evidence_cv ON career_evidence(cv_ready_recommended, cv_ready_user_approved);
CREATE INDEX IF NOT EXISTS idx_career_evidence_skills_skill ON career_evidence_skills(skill_id, evidence_id);

UPDATE settings SET setting_value='22' WHERE setting_key='data_format_version';
