CREATE TABLE IF NOT EXISTS learning_modules (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    summary TEXT NOT NULL DEFAULT '',
    subject TEXT NOT NULL DEFAULT '',
    course_level TEXT NOT NULL DEFAULT '',
    learning_subject TEXT NOT NULL DEFAULT '',
    topic TEXT NOT NULL DEFAULT '',
    subtopic TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'Draft',
    primary_resource_id INTEGER NULL,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    FOREIGN KEY(primary_resource_id) REFERENCES resources(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS learning_blocks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    module_id INTEGER NOT NULL,
    block_type TEXT NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    title TEXT NOT NULL DEFAULT '',
    body TEXT NOT NULL DEFAULT '',
    formula TEXT NOT NULL DEFAULT '',
    prompt TEXT NOT NULL DEFAULT '',
    answer_kind TEXT NOT NULL DEFAULT 'None',
    expected_answer TEXT NOT NULL DEFAULT '',
    numeric_tolerance REAL NULL,
    hint TEXT NOT NULL DEFAULT '',
    explanation TEXT NOT NULL DEFAULT '',
    config_json TEXT NOT NULL DEFAULT '{}',
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    FOREIGN KEY(module_id) REFERENCES learning_modules(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_learning_blocks_module_sort
ON learning_blocks(module_id, sort_order, id);

CREATE TABLE IF NOT EXISTS learning_source_refs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    block_id INTEGER NOT NULL,
    resource_id INTEGER NOT NULL,
    page_number INTEGER NULL,
    end_page_number INTEGER NULL,
    timestamp_seconds INTEGER NULL,
    note TEXT NOT NULL DEFAULT '',
    location_json TEXT NOT NULL DEFAULT '{}',
    created_utc TEXT NOT NULL,
    FOREIGN KEY(block_id) REFERENCES learning_blocks(id) ON DELETE CASCADE,
    FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_learning_source_refs_block
ON learning_source_refs(block_id);

UPDATE settings SET setting_value = '5' WHERE setting_key = 'data_format_version';
