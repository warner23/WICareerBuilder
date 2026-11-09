PRAGMA foreign_keys = ON;

ALTER TABLE library_sources ADD COLUMN course_level TEXT NOT NULL DEFAULT '';

-- Conservative convenience migration for the existing ONC test source.
-- The level is still editable by the user and is never inferred from document contents.
UPDATE library_sources
SET course_level = 'ONC'
WHERE course_level = ''
  AND (name LIKE 'ONC %' OR name LIKE '% ONC %' OR name = 'ONC');

CREATE TABLE IF NOT EXISTS resource_organisation (
    resource_id INTEGER PRIMARY KEY,
    learning_subject TEXT NOT NULL DEFAULT '',
    topic TEXT NOT NULL DEFAULT '',
    subtopic TEXT NOT NULL DEFAULT '',
    study_status TEXT NOT NULL DEFAULT 'Unsorted',
    notes TEXT NOT NULL DEFAULT '',
    updated_utc TEXT NOT NULL,
    FOREIGN KEY (resource_id) REFERENCES resources(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_resource_organisation_learning_subject
    ON resource_organisation(learning_subject COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS idx_resource_organisation_topic
    ON resource_organisation(topic COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS idx_resource_organisation_status
    ON resource_organisation(study_status COLLATE NOCASE);

UPDATE settings SET setting_value = '4' WHERE setting_key = 'data_format_version';
