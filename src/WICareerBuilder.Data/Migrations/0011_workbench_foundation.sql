PRAGMA foreign_keys = ON;

-- v0.6 corrects the meaning of the old "career_paths" hierarchy.
-- It was really a knowledge-area hierarchy (Engineering -> ONC -> subject -> topic).
ALTER TABLE career_paths RENAME TO knowledge_areas;
ALTER TABLE courses RENAME COLUMN career_path_id TO knowledge_area_id;

-- Real career paths now have their own table and are intentionally separate from archive organisation.
CREATE TABLE IF NOT EXISTS career_paths (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL COLLATE NOCASE UNIQUE,
    target_role TEXT NOT NULL DEFAULT '',
    description TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'Considering',
    notes TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS career_path_requirements (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    career_path_id INTEGER NOT NULL,
    requirement_kind TEXT NOT NULL DEFAULT 'Knowledge',
    knowledge_area_id INTEGER NULL,
    course_id INTEGER NULL,
    curriculum_subject_id INTEGER NULL,
    topic_id INTEGER NULL,
    skill_name TEXT NOT NULL DEFAULT '',
    importance TEXT NOT NULL DEFAULT 'Useful',
    notes TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    UNIQUE(career_path_id, requirement_kind, curriculum_subject_id, topic_id, skill_name),
    FOREIGN KEY(career_path_id) REFERENCES career_paths(id) ON DELETE CASCADE,
    FOREIGN KEY(knowledge_area_id) REFERENCES knowledge_areas(id) ON DELETE SET NULL,
    FOREIGN KEY(course_id) REFERENCES courses(id) ON DELETE SET NULL,
    FOREIGN KEY(curriculum_subject_id) REFERENCES curriculum_subjects(id) ON DELETE SET NULL,
    FOREIGN KEY(topic_id) REFERENCES curriculum_topics(id) ON DELETE SET NULL
);

-- What a file *is* remains in resources. What it can be *used for* is many-to-many.
CREATE TABLE IF NOT EXISTS resource_roles (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    role_key TEXT NOT NULL COLLATE NOCASE UNIQUE,
    label TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    sort_order INTEGER NOT NULL DEFAULT 0,
    is_active INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS resource_role_assignments (
    resource_id INTEGER NOT NULL,
    role_id INTEGER NOT NULL,
    assignment_source TEXT NOT NULL DEFAULT 'Suggested',
    confidence REAL NOT NULL DEFAULT 0.5,
    status TEXT NOT NULL DEFAULT 'Suggested',
    notes TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    PRIMARY KEY(resource_id, role_id),
    FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE CASCADE,
    FOREIGN KEY(role_id) REFERENCES resource_roles(id) ON DELETE CASCADE
);

INSERT OR IGNORE INTO resource_roles(role_key,label,description,sort_order) VALUES
('Reference','Reference','General source material worth retaining.',10),
('Book','Book','Book or substantial reading source.',20),
('Manual','Manual','Technical manual or handbook.',30),
('Datasheet','Datasheet','Component/equipment datasheet.',40),
('OldCoursework','Old Coursework','Retained college/university coursework.',50),
('Assignment','Assignment','Assignment or assessment source.',60),
('Worksheet','Worksheet','Worksheet or exercise source.',70),
('RevisionMaterial','Revision Material','Revision notes, questions or recap material.',80),
('TeachingPresentation','Teaching Presentation','Presentation containing teaching/source material.',90),
('ImageDiagram','Image / Diagram','Visual material for reference, projects or composed learning.',100),
('ProjectMaterial','Project Material','Files that belong to a project.',110),
('ProjectEvidence','Project Evidence','Evidence produced by or supporting a project.',120),
('LessonSource','Lesson Source','Source that may contribute to a composed lesson.',130),
('QuestionSource','Question Source','Source that may contribute questions or practice.',140),
('FormulaSource','Formula Source','Source that may contribute formulas/calculations.',150),
('CareerEvidenceCandidate','Career Evidence Candidate','Potential evidence for a skill/career claim; review required.',160),
('CvEvidenceCandidate','CV Evidence Candidate','Potential evidence for a CV statement; review required.',170);

-- Preserve useful hints from the v0.5 organisation pass as *suggested roles*, not truth.
INSERT OR IGNORE INTO resource_role_assignments(resource_id, role_id, assignment_source, confidence, status, notes, created_utc, updated_utc)
SELECT r.id, rr.id, 'v0.5 migration', 0.85, 'Suggested', 'Migrated from v0.5 resource type.', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resources r
JOIN resource_organisation ro ON ro.resource_id=r.id
JOIN resource_roles rr ON rr.role_key = CASE
    WHEN r.resource_type='Old Assignment' THEN 'OldCoursework'
    WHEN r.resource_type='Worksheet / Exercises' THEN 'Worksheet'
    WHEN r.resource_type='Revision Material' THEN 'RevisionMaterial'
    WHEN r.resource_type='Teaching Presentation' THEN 'TeachingPresentation'
    WHEN r.resource_type='Datasheet' THEN 'Datasheet'
    WHEN r.resource_type='Manual / Technical Reference' THEN 'Manual'
    WHEN r.resource_type='Project Material' THEN 'ProjectMaterial'
    ELSE 'Reference'
END
WHERE r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include';

INSERT OR IGNORE INTO resource_role_assignments(resource_id, role_id, assignment_source, confidence, status, notes, created_utc, updated_utc)
SELECT r.id, rr.id, 'v0.6 media role', 1.0, 'Accepted', 'Image media is a visual resource; this does not imply lesson use.', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resources r
JOIN resource_roles rr ON rr.role_key='ImageDiagram'
WHERE r.media_kind='Image' AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include';

-- Metadata/path-only role suggestions are safe during migration because they do not read file contents.
INSERT OR IGNORE INTO resource_role_assignments(resource_id, role_id, assignment_source, confidence, status, notes, created_utc, updated_utc)
SELECT r.id, rr.id, 'v0.6 metadata suggestion', 0.82, 'Suggested', 'Assignment/coursework role suggested from filename/path only.', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resources r JOIN resource_roles rr ON rr.role_key='OldCoursework'
WHERE (lower(r.file_name) LIKE '%assignment%' OR lower(r.relative_path) LIKE '%old assignment%' OR lower(r.relative_path) LIKE '%completed assignment%')
  AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include';

INSERT OR IGNORE INTO resource_role_assignments(resource_id, role_id, assignment_source, confidence, status, notes, created_utc, updated_utc)
SELECT r.id, rr.id, 'v0.6 metadata suggestion', 0.78, 'Suggested', 'Assignment role suggested from filename/path only.', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resources r JOIN resource_roles rr ON rr.role_key='Assignment'
WHERE (lower(r.file_name) LIKE '%assignment%' OR lower(r.file_name) LIKE '%assessment%' OR lower(r.relative_path) LIKE '%/assignments/%')
  AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include';

INSERT OR IGNORE INTO resource_role_assignments(resource_id, role_id, assignment_source, confidence, status, notes, created_utc, updated_utc)
SELECT r.id, rr.id, 'v0.6 metadata suggestion', 0.84, 'Suggested', 'Project role suggested from path only.', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resources r JOIN resource_roles rr ON rr.role_key='ProjectMaterial'
WHERE (lower(r.relative_path) LIKE 'projects/%' OR lower(r.relative_path) LIKE '%/projects/%')
  AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include';

INSERT OR IGNORE INTO resource_role_assignments(resource_id, role_id, assignment_source, confidence, status, notes, created_utc, updated_utc)
SELECT r.id, rr.id, 'v0.6 metadata suggestion', 0.86, 'Suggested', 'Datasheet role suggested from filename/path only.', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resources r JOIN resource_roles rr ON rr.role_key='Datasheet'
WHERE (lower(r.file_name) LIKE '%datasheet%' OR lower(r.file_name) LIKE '%data sheet%' OR lower(r.relative_path) LIKE '%datasheet%')
  AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include';

INSERT OR IGNORE INTO resource_role_assignments(resource_id, role_id, assignment_source, confidence, status, notes, created_utc, updated_utc)
SELECT r.id, rr.id, 'v0.6 metadata suggestion', 0.78, 'Suggested', 'Revision role suggested from filename/path only.', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resources r JOIN resource_roles rr ON rr.role_key='RevisionMaterial'
WHERE (lower(r.file_name) LIKE '%revision%' OR lower(r.relative_path) LIKE '%revision%')
  AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include';

INSERT OR IGNORE INTO resource_role_assignments(resource_id, role_id, assignment_source, confidence, status, notes, created_utc, updated_utc)
SELECT r.id, rr.id, 'v0.6 metadata suggestion', 0.76, 'Suggested', 'Teaching-presentation role suggested from media type only.', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resources r JOIN resource_roles rr ON rr.role_key='TeachingPresentation'
WHERE r.media_kind='Presentation' AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include';

INSERT OR IGNORE INTO resource_role_assignments(resource_id, role_id, assignment_source, confidence, status, notes, created_utc, updated_utc)
SELECT r.id, rr.id, 'v0.6 metadata suggestion', 0.68, 'Suggested', 'Coursework may support reviewed career evidence; no skill claim is inferred automatically.', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resources r JOIN resource_roles rr ON rr.role_key='CareerEvidenceCandidate'
WHERE (lower(r.file_name) LIKE '%assignment%' OR lower(r.relative_path) LIKE '%old assignment%' OR lower(r.relative_path) LIKE '%completed assignment%')
  AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include';

INSERT OR IGNORE INTO resource_role_assignments(resource_id, role_id, assignment_source, confidence, status, notes, created_utc, updated_utc)
SELECT r.id, rr.id, 'v0.6 metadata suggestion', 0.68, 'Suggested', 'Coursework may support a reviewed CV statement; no CV claim is generated automatically.', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resources r JOIN resource_roles rr ON rr.role_key='CvEvidenceCandidate'
WHERE (lower(r.file_name) LIKE '%assignment%' OR lower(r.relative_path) LIKE '%old assignment%' OR lower(r.relative_path) LIKE '%completed assignment%')
  AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include';

INSERT OR IGNORE INTO resource_role_assignments(resource_id, role_id, assignment_source, confidence, status, notes, created_utc, updated_utc)
SELECT ro.resource_id, rr.id, 'v0.5 migration', 0.72, 'Suggested', 'Old coursework can support evidence after review; no skill claim is inferred automatically.', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resource_organisation ro
JOIN resources r ON r.id=ro.resource_id
JOIN resource_roles rr ON rr.role_key='CareerEvidenceCandidate'
WHERE r.resource_type='Old Assignment' AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include';

INSERT OR IGNORE INTO resource_role_assignments(resource_id, role_id, assignment_source, confidence, status, notes, created_utc, updated_utc)
SELECT ro.resource_id, rr.id, 'v0.5 migration', 0.72, 'Suggested', 'Old coursework can support CV evidence after review; no CV claim is generated automatically.', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resource_organisation ro
JOIN resources r ON r.id=ro.resource_id
JOIN resource_roles rr ON rr.role_key='CvEvidenceCandidate'
WHERE r.resource_type='Old Assignment' AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include';

INSERT OR IGNORE INTO resource_role_assignments(resource_id, role_id, assignment_source, confidence, status, notes, created_utc, updated_utc)
SELECT ro.resource_id, rr.id, 'v0.5 migration', 0.72, 'Suggested', 'Structured learning source suggested from v0.5 classification.', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resource_organisation ro
JOIN resources r ON r.id=ro.resource_id
JOIN resource_roles rr ON rr.role_key='LessonSource'
WHERE r.resource_type IN ('Teaching Presentation','Course Notes / Exercises','Revision Material','Worksheet / Exercises','Interactive / Calculation Sheet')
  AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include';

INSERT OR IGNORE INTO resource_role_assignments(resource_id, role_id, assignment_source, confidence, status, notes, created_utc, updated_utc)
SELECT ro.resource_id, rr.id, 'v0.5 migration', 0.68, 'Suggested', 'Question-source potential suggested from v0.5 classification.', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resource_organisation ro
JOIN resources r ON r.id=ro.resource_id
JOIN resource_roles rr ON rr.role_key='QuestionSource'
WHERE r.resource_type IN ('Worksheet / Exercises','Course Notes / Exercises','Revision Material','Interactive / Calculation Sheet','Old Assignment')
  AND r.is_policy_excluded=0 AND r.is_ignored=0 AND r.discovery_decision='Include';

-- A resource can belong to multiple knowledge nodes. The old single-path organisation is retained
-- for compatibility but migrated into this many-to-many table as an Accepted legacy link.
CREATE TABLE IF NOT EXISTS resource_knowledge_links (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    resource_id INTEGER NOT NULL,
    knowledge_area_id INTEGER NOT NULL,
    course_id INTEGER NULL,
    curriculum_subject_id INTEGER NULL,
    topic_id INTEGER NULL,
    confidence REAL NOT NULL DEFAULT 1.0,
    status TEXT NOT NULL DEFAULT 'Accepted',
    link_source TEXT NOT NULL DEFAULT 'Manual',
    notes TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    UNIQUE(resource_id, course_id, curriculum_subject_id, topic_id),
    FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE CASCADE,
    FOREIGN KEY(knowledge_area_id) REFERENCES knowledge_areas(id) ON DELETE CASCADE,
    FOREIGN KEY(course_id) REFERENCES courses(id) ON DELETE SET NULL,
    FOREIGN KEY(curriculum_subject_id) REFERENCES curriculum_subjects(id) ON DELETE SET NULL,
    FOREIGN KEY(topic_id) REFERENCES curriculum_topics(id) ON DELETE SET NULL
);

-- Ensure v0.5 topic strings have real topic nodes before linking them.
INSERT OR IGNORE INTO curriculum_topics(curriculum_subject_id,parent_topic_id,name,sort_order,is_active,created_utc,updated_utc)
SELECT DISTINCT cs.id, NULL, trim(ro.topic), 100, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resource_organisation ro
JOIN resources r ON r.id=ro.resource_id
JOIN source_courses sc ON sc.library_source_id=r.library_source_id
JOIN curriculum_subjects cs ON cs.course_id=sc.course_id AND cs.name=ro.learning_subject COLLATE NOCASE
WHERE trim(ro.topic)<>'';

INSERT OR IGNORE INTO curriculum_topics(curriculum_subject_id,parent_topic_id,name,sort_order,is_active,created_utc,updated_utc)
SELECT DISTINCT cs.id, pt.id, trim(ro.subtopic), 100, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resource_organisation ro
JOIN resources r ON r.id=ro.resource_id
JOIN source_courses sc ON sc.library_source_id=r.library_source_id
JOIN curriculum_subjects cs ON cs.course_id=sc.course_id AND cs.name=ro.learning_subject COLLATE NOCASE
JOIN curriculum_topics pt ON pt.curriculum_subject_id=cs.id AND pt.parent_topic_id IS NULL AND pt.name=ro.topic COLLATE NOCASE
WHERE trim(ro.subtopic)<>'';

INSERT OR IGNORE INTO resource_knowledge_links(resource_id,knowledge_area_id,course_id,curriculum_subject_id,topic_id,confidence,status,link_source,notes,created_utc,updated_utc)
SELECT r.id, c.knowledge_area_id, c.id, cs.id,
       COALESCE(st.id, pt.id),
       1.0, 'Accepted', 'Primary organisation',
       'Migrated from v0.5 organisation without changing original files; may be expanded to multiple links in v0.6.',
       CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
FROM resources r
JOIN source_courses sc ON sc.library_source_id=r.library_source_id
JOIN courses c ON c.id=sc.course_id
LEFT JOIN resource_organisation ro ON ro.resource_id=r.id
LEFT JOIN curriculum_subjects cs ON cs.course_id=c.id AND cs.name=ro.learning_subject COLLATE NOCASE
LEFT JOIN curriculum_topics pt ON pt.curriculum_subject_id=cs.id AND pt.parent_topic_id IS NULL AND trim(ro.topic)<>'' AND pt.name=ro.topic COLLATE NOCASE
LEFT JOIN curriculum_topics st ON st.curriculum_subject_id=cs.id AND st.parent_topic_id=pt.id AND trim(ro.subtopic)<>'' AND st.name=ro.subtopic COLLATE NOCASE;

CREATE INDEX IF NOT EXISTS idx_resource_knowledge_resource ON resource_knowledge_links(resource_id,status);
CREATE INDEX IF NOT EXISTS idx_resource_knowledge_context ON resource_knowledge_links(course_id,curriculum_subject_id,topic_id,status);
CREATE INDEX IF NOT EXISTS idx_resource_roles_status ON resource_role_assignments(role_id,status,resource_id);

-- Source Analysis staging: extraction is allowed to be ugly here. It is explicitly not finished learning.
CREATE TABLE IF NOT EXISTS source_analysis_runs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    resource_id INTEGER NOT NULL,
    analyser_kind TEXT NOT NULL,
    analyser_version TEXT NOT NULL DEFAULT '',
    analysis_profile TEXT NOT NULL DEFAULT 'Selected',
    source_size_bytes INTEGER NULL,
    source_last_write_utc TEXT NULL,
    status TEXT NOT NULL DEFAULT 'Queued',
    warning_count INTEGER NOT NULL DEFAULT 0,
    error_message TEXT NOT NULL DEFAULT '',
    started_utc TEXT NOT NULL,
    completed_utc TEXT NULL,
    FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS source_fragments (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    analysis_run_id INTEGER NOT NULL,
    sequence_no INTEGER NOT NULL,
    fragment_kind TEXT NOT NULL DEFAULT 'Unknown',
    raw_text TEXT NOT NULL DEFAULT '',
    normalised_text TEXT NOT NULL DEFAULT '',
    locator_kind TEXT NOT NULL DEFAULT '',
    locator_value TEXT NOT NULL DEFAULT '',
    confidence REAL NOT NULL DEFAULT 0.5,
    review_status TEXT NOT NULL DEFAULT 'Raw',
    reconstruction_group TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    FOREIGN KEY(analysis_run_id) REFERENCES source_analysis_runs(id) ON DELETE CASCADE,
    UNIQUE(analysis_run_id, sequence_no)
);

CREATE TABLE IF NOT EXISTS source_assets (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    analysis_run_id INTEGER NOT NULL,
    resource_id INTEGER NOT NULL,
    asset_kind TEXT NOT NULL DEFAULT 'Image',
    locator_kind TEXT NOT NULL DEFAULT '',
    locator_value TEXT NOT NULL DEFAULT '',
    original_name TEXT NOT NULL DEFAULT '',
    derived_path TEXT NOT NULL DEFAULT '',
    thumbnail_path TEXT NOT NULL DEFAULT '',
    caption TEXT NOT NULL DEFAULT '',
    asset_hash TEXT NOT NULL DEFAULT '',
    review_status TEXT NOT NULL DEFAULT 'Raw',
    created_utc TEXT NOT NULL,
    FOREIGN KEY(analysis_run_id) REFERENCES source_analysis_runs(id) ON DELETE CASCADE,
    FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS fragment_knowledge_links (
    fragment_id INTEGER NOT NULL,
    knowledge_area_id INTEGER NOT NULL,
    course_id INTEGER NULL,
    curriculum_subject_id INTEGER NULL,
    topic_id INTEGER NULL,
    confidence REAL NOT NULL DEFAULT 0.5,
    status TEXT NOT NULL DEFAULT 'Suggested',
    reason TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    PRIMARY KEY(fragment_id, knowledge_area_id, course_id, curriculum_subject_id, topic_id),
    FOREIGN KEY(fragment_id) REFERENCES source_fragments(id) ON DELETE CASCADE,
    FOREIGN KEY(knowledge_area_id) REFERENCES knowledge_areas(id) ON DELETE CASCADE,
    FOREIGN KEY(course_id) REFERENCES courses(id) ON DELETE SET NULL,
    FOREIGN KEY(curriculum_subject_id) REFERENCES curriculum_subjects(id) ON DELETE SET NULL,
    FOREIGN KEY(topic_id) REFERENCES curriculum_topics(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_analysis_runs_resource ON source_analysis_runs(resource_id,started_utc);
CREATE INDEX IF NOT EXISTS idx_source_fragments_run ON source_fragments(analysis_run_id,sequence_no);
CREATE INDEX IF NOT EXISTS idx_source_assets_resource ON source_assets(resource_id,asset_kind);

-- Bring deliberately-scanned v0.5 resources into the staging model without pretending their text is composed learning.
INSERT INTO source_analysis_runs(resource_id,analyser_kind,analyser_version,analysis_profile,source_size_bytes,source_last_write_utc,status,warning_count,error_message,started_utc,completed_utc)
SELECT ra.resource_id,ra.scanner_kind,ra.scanner_version,'Legacy v0.5 selected scan',ra.source_size_bytes,ra.source_last_write_utc,
       CASE WHEN ra.analysis_status='Failed' THEN 'Failed' ELSE 'Completed' END,0,COALESCE(ra.error_message,''),ra.scanned_utc,ra.scanned_utc
FROM resource_analysis ra
WHERE ra.analysis_status IN ('Analysed','Limited','Failed');

INSERT INTO source_fragments(analysis_run_id,sequence_no,fragment_kind,raw_text,normalised_text,locator_kind,locator_value,confidence,review_status,reconstruction_group,created_utc)
SELECT sar.id,1,'LegacyExtract',COALESCE(ra.extracted_text,''),COALESCE(ra.extracted_text,''),'','',0.50,'Raw','',CURRENT_TIMESTAMP
FROM source_analysis_runs sar
JOIN resource_analysis ra ON ra.resource_id=sar.resource_id
WHERE sar.analysis_profile='Legacy v0.5 selected scan' AND trim(COALESCE(ra.extracted_text,''))<>'';

-- v0.5 Study Builder material remains available, but is explicitly marked as prototype material
-- needing recomposition before it is considered a proper v0.6 lesson.
ALTER TABLE learning_modules ADD COLUMN composition_state TEXT NOT NULL DEFAULT 'PrototypeNeedsRecomposition';
ALTER TABLE learning_modules ADD COLUMN origin_kind TEXT NOT NULL DEFAULT 'LegacyV05Workflow';
UPDATE learning_modules
SET composition_state='PrototypeNeedsRecomposition', origin_kind='LegacyV05'
WHERE id IN (SELECT id FROM learning_modules);

CREATE TABLE IF NOT EXISTS output_suggestions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    output_kind TEXT NOT NULL,
    title TEXT NOT NULL DEFAULT '',
    subject TEXT NOT NULL DEFAULT '',
    course_level TEXT NOT NULL DEFAULT '',
    learning_subject TEXT NOT NULL DEFAULT '',
    topic TEXT NOT NULL DEFAULT '',
    subtopic TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'Suggested',
    confidence REAL NOT NULL DEFAULT 0.5,
    reason TEXT NOT NULL DEFAULT '',
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS output_suggestion_sources (
    suggestion_id INTEGER NOT NULL,
    resource_id INTEGER NOT NULL,
    analysis_run_id INTEGER NULL,
    fragment_id INTEGER NULL,
    created_utc TEXT NOT NULL,
    PRIMARY KEY(suggestion_id,resource_id,fragment_id),
    FOREIGN KEY(suggestion_id) REFERENCES output_suggestions(id) ON DELETE CASCADE,
    FOREIGN KEY(resource_id) REFERENCES resources(id) ON DELETE CASCADE,
    FOREIGN KEY(analysis_run_id) REFERENCES source_analysis_runs(id) ON DELETE SET NULL,
    FOREIGN KEY(fragment_id) REFERENCES source_fragments(id) ON DELETE SET NULL
);

UPDATE settings SET setting_value='11' WHERE setting_key='data_format_version';
INSERT OR IGNORE INTO settings(setting_key,setting_value) VALUES ('product_mode','Workbench');
