PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS learning_question_bank (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    candidate_id INTEGER NOT NULL UNIQUE,
    prompt TEXT NOT NULL,
    subject TEXT NOT NULL DEFAULT '',
    course_level TEXT NOT NULL DEFAULT '',
    learning_subject TEXT NOT NULL DEFAULT '',
    topic TEXT NOT NULL DEFAULT '',
    subtopic TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'Draft',
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    FOREIGN KEY(candidate_id) REFERENCES learning_candidates(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS learning_formula_library (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    candidate_id INTEGER NOT NULL UNIQUE,
    title TEXT NOT NULL DEFAULT '',
    formula TEXT NOT NULL,
    subject TEXT NOT NULL DEFAULT '',
    course_level TEXT NOT NULL DEFAULT '',
    learning_subject TEXT NOT NULL DEFAULT '',
    topic TEXT NOT NULL DEFAULT '',
    subtopic TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'Draft',
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    FOREIGN KEY(candidate_id) REFERENCES learning_candidates(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS learning_candidate_duplicate_suggestions (
    candidate_id INTEGER PRIMARY KEY,
    existing_candidate_id INTEGER NOT NULL,
    similarity REAL NOT NULL,
    created_utc TEXT NOT NULL,
    FOREIGN KEY(candidate_id) REFERENCES learning_candidates(id) ON DELETE CASCADE,
    FOREIGN KEY(existing_candidate_id) REFERENCES learning_candidates(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS learning_candidate_module_links (
    candidate_id INTEGER NOT NULL,
    module_id INTEGER NOT NULL,
    block_id INTEGER NOT NULL,
    created_utc TEXT NOT NULL,
    PRIMARY KEY(candidate_id, module_id, block_id),
    FOREIGN KEY(candidate_id) REFERENCES learning_candidates(id) ON DELETE CASCADE,
    FOREIGN KEY(module_id) REFERENCES learning_modules(id) ON DELETE CASCADE,
    FOREIGN KEY(block_id) REFERENCES learning_blocks(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_learning_candidates_review
ON learning_candidates(course_level, status, candidate_type, learning_subject, updated_utc);

UPDATE settings SET setting_value = '9' WHERE setting_key = 'data_format_version';
