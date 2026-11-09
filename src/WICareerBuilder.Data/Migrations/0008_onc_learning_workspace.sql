PRAGMA foreign_keys = ON;

ALTER TABLE library_sources ADD COLUMN lifecycle_status TEXT NOT NULL DEFAULT 'Active';
ALTER TABLE library_sources ADD COLUMN removed_utc TEXT NULL;

ALTER TABLE resource_analysis ADD COLUMN source_size_bytes INTEGER NULL;
ALTER TABLE resource_analysis ADD COLUMN source_last_write_utc TEXT NULL;
ALTER TABLE resource_analysis ADD COLUMN analysis_profile TEXT NOT NULL DEFAULT 'Manual';
ALTER TABLE resource_analysis ADD COLUMN extracted_text TEXT NOT NULL DEFAULT '';

ALTER TABLE learning_modules ADD COLUMN canonical_key TEXT NOT NULL DEFAULT '';
ALTER TABLE learning_blocks ADD COLUMN canonical_key TEXT NOT NULL DEFAULT '';

-- Backfill a stable first-pass key for existing learning. Application code uses a stronger
-- canonicaliser for new/edited content, so this is only a migration aid.
UPDATE learning_modules SET canonical_key = lower(trim(title)) WHERE canonical_key = '' AND trim(title) <> '';
UPDATE learning_blocks SET canonical_key = lower(trim(title)) WHERE canonical_key = '' AND trim(title) <> '';

CREATE TABLE IF NOT EXISTS career_paths (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL COLLATE NOCASE UNIQUE,
    description TEXT NOT NULL DEFAULT '',
    sort_order INTEGER NOT NULL DEFAULT 0,
    is_active INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS courses (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    career_path_id INTEGER NOT NULL,
    name TEXT NOT NULL COLLATE NOCASE,
    level_code TEXT NOT NULL DEFAULT '',
    description TEXT NOT NULL DEFAULT '',
    sort_order INTEGER NOT NULL DEFAULT 0,
    is_active INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    UNIQUE(career_path_id, name),
    FOREIGN KEY(career_path_id) REFERENCES career_paths(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS curriculum_subjects (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    course_id INTEGER NOT NULL,
    name TEXT NOT NULL COLLATE NOCASE,
    description TEXT NOT NULL DEFAULT '',
    sort_order INTEGER NOT NULL DEFAULT 0,
    is_active INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    UNIQUE(course_id, name),
    FOREIGN KEY(course_id) REFERENCES courses(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS curriculum_topics (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    curriculum_subject_id INTEGER NOT NULL,
    parent_topic_id INTEGER NULL,
    name TEXT NOT NULL COLLATE NOCASE,
    sort_order INTEGER NOT NULL DEFAULT 0,
    is_active INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    UNIQUE(curriculum_subject_id, parent_topic_id, name),
    FOREIGN KEY(curriculum_subject_id) REFERENCES curriculum_subjects(id) ON DELETE CASCADE,
    FOREIGN KEY(parent_topic_id) REFERENCES curriculum_topics(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS source_courses (
    library_source_id INTEGER PRIMARY KEY,
    course_id INTEGER NOT NULL,
    linked_utc TEXT NOT NULL,
    FOREIGN KEY(library_source_id) REFERENCES library_sources(id) ON DELETE CASCADE,
    FOREIGN KEY(course_id) REFERENCES courses(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS resource_relationships (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    from_resource_id INTEGER NOT NULL,
    to_resource_id INTEGER NOT NULL,
    relationship_type TEXT NOT NULL,
    confidence REAL NOT NULL DEFAULT 1.0,
    reason TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    UNIQUE(from_resource_id, to_resource_id, relationship_type),
    FOREIGN KEY(from_resource_id) REFERENCES resources(id) ON DELETE CASCADE,
    FOREIGN KEY(to_resource_id) REFERENCES resources(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_resource_relationships_from
    ON resource_relationships(from_resource_id, relationship_type);
CREATE INDEX IF NOT EXISTS idx_resources_duplicate_candidate
    ON resources(library_source_id, file_name COLLATE NOCASE, size_bytes);

CREATE TABLE IF NOT EXISTS learning_module_sources (
    module_id INTEGER NOT NULL,
    resource_id INTEGER NOT NULL,
    source_role TEXT NOT NULL DEFAULT 'Source',
    created_utc TEXT NOT NULL,
    PRIMARY KEY(module_id, resource_id),
    FOREIGN KEY(module_id) REFERENCES learning_modules(id) ON DELETE CASCADE,
    FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE CASCADE
);

INSERT OR IGNORE INTO learning_module_sources(module_id, resource_id, source_role, created_utc)
SELECT id, primary_resource_id, 'Primary', created_utc
FROM learning_modules
WHERE primary_resource_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS learning_candidates (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    candidate_type TEXT NOT NULL,
    title TEXT NOT NULL DEFAULT '',
    body TEXT NOT NULL DEFAULT '',
    canonical_key TEXT NOT NULL,
    subject TEXT NOT NULL DEFAULT '',
    course_level TEXT NOT NULL DEFAULT '',
    learning_subject TEXT NOT NULL DEFAULT '',
    topic TEXT NOT NULL DEFAULT '',
    subtopic TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'Draft',
    confidence REAL NOT NULL DEFAULT 0.5,
    target_module_id INTEGER NULL,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    UNIQUE(candidate_type, course_level, learning_subject, canonical_key),
    FOREIGN KEY(target_module_id) REFERENCES learning_modules(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS learning_candidate_sources (
    candidate_id INTEGER NOT NULL,
    resource_id INTEGER NOT NULL,
    location_note TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    PRIMARY KEY(candidate_id, resource_id),
    FOREIGN KEY(candidate_id) REFERENCES learning_candidates(id) ON DELETE CASCADE,
    FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS analysis_jobs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    job_type TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'Queued',
    total_items INTEGER NOT NULL DEFAULT 0,
    completed_items INTEGER NOT NULL DEFAULT 0,
    failed_items INTEGER NOT NULL DEFAULT 0,
    detail_json TEXT NOT NULL DEFAULT '{}',
    created_utc TEXT NOT NULL,
    started_utc TEXT NULL,
    completed_utc TEXT NULL
);

CREATE TABLE IF NOT EXISTS update_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    from_version TEXT NOT NULL DEFAULT '',
    to_version TEXT NOT NULL DEFAULT '',
    package_path TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL,
    message TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_learning_modules_canonical
    ON learning_modules(canonical_key COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS idx_learning_blocks_canonical
    ON learning_blocks(canonical_key COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS idx_learning_candidates_context
    ON learning_candidates(course_level, learning_subject, topic, candidate_type);

-- Seed a configurable ONC Engineering workspace. These are data rows, not hard-coded UI sections.
INSERT OR IGNORE INTO career_paths(name, description, sort_order, is_active, created_utc, updated_utc)
VALUES ('Engineering', 'Engineering career and learning path', 10, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

INSERT OR IGNORE INTO courses(career_path_id, name, level_code, description, sort_order, is_active, created_utc, updated_utc)
SELECT id, 'ONC Engineering', 'ONC', 'ONC Engineering learning workspace', 10, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM career_paths WHERE name='Engineering';

INSERT OR IGNORE INTO curriculum_subjects(course_id, name, sort_order, created_utc, updated_utc)
SELECT c.id, v.name, v.sort_order, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM courses c
JOIN career_paths cp ON cp.id=c.career_path_id
JOIN (
    SELECT 'Engineering Mathematics' AS name, 10 AS sort_order UNION ALL
    SELECT 'Electrical Principles', 20 UNION ALL
    SELECT 'Electronics', 30 UNION ALL
    SELECT 'Components', 40 UNION ALL
    SELECT 'Electrical Machines', 50 UNION ALL
    SELECT 'PLC & Control', 60 UNION ALL
    SELECT 'RF & Communications', 70 UNION ALL
    SELECT 'Test & Measurement', 80 UNION ALL
    SELECT 'Health & Safety', 90 UNION ALL
    SELECT 'Projects', 100
) v
WHERE cp.name='Engineering' AND c.name='ONC Engineering';

INSERT OR IGNORE INTO source_courses(library_source_id, course_id, linked_utc)
SELECT ls.id, c.id, CURRENT_TIMESTAMP
FROM library_sources ls
JOIN source_subjects ss ON ss.library_source_id=ls.id
JOIN subjects s ON s.id=ss.subject_id
JOIN career_paths cp ON cp.name=s.name
JOIN courses c ON c.career_path_id=cp.id AND c.level_code=ls.course_level
WHERE s.name='Engineering' AND ls.course_level='ONC';

UPDATE settings SET setting_value = '8' WHERE setting_key = 'data_format_version';
INSERT OR IGNORE INTO settings(setting_key, setting_value) VALUES ('update_feed_url', '');
