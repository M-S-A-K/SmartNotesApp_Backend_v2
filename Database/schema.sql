/* Smart Notes V1 schema. Run the whole file in Rider (Query Console on the SQL Server connection) or SSMS.
   WARNING: it drops and recreates the SmartNotesV1 database, so all V1 data is wiped on every run.
   Delete rules: deleting a note really deletes it and everything inside it (ON DELETE CASCADE).
   User foreign keys use NO ACTION because V1 has no "delete account" feature. */

IF DB_ID(N'SmartNotesV1') IS NOT NULL
BEGIN
    ALTER DATABASE SmartNotesV1 SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE SmartNotesV1;
END;
GO
CREATE DATABASE SmartNotesV1;
GO
USE SmartNotesV1;
GO

/* ---------- Users and friends ---------- */

CREATE TABLE Users
(
    id INT IDENTITY(1,1) PRIMARY KEY,
    full_name NVARCHAR(200) NOT NULL,
    email NVARCHAR(320) NOT NULL UNIQUE,
    phone NVARCHAR(30) NULL,
    password NVARCHAR(500) NOT NULL,          -- plain text for now (wide enough to hash later)
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
CREATE UNIQUE INDEX UX_Users_phone ON Users(phone) WHERE phone IS NOT NULL;
GO

-- Friends are added instantly. Each friendship is stored twice (A->B and B->A).
CREATE TABLE Friendships
(
    user_id INT NOT NULL,
    friend_id INT NOT NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    PRIMARY KEY (user_id, friend_id),
    CHECK (user_id <> friend_id),
    FOREIGN KEY (user_id) REFERENCES Users(id),
    FOREIGN KEY (friend_id) REFERENCES Users(id)
);
GO

/* ---------- Notes (personal or group) ---------- */

CREATE TABLE Notes
(
    id INT IDENTITY(1,1) PRIMARY KEY,
    owner_id INT NOT NULL,
    title NVARCHAR(250) NOT NULL,
    content NVARCHAR(MAX) NOT NULL DEFAULT N'',   -- personal note text (group notes use contributions)
    note_type NVARCHAR(20) NOT NULL CHECK (note_type IN (N'personal', N'group')),
    category NVARCHAR(100) NULL,
    is_locked BIT NOT NULL DEFAULT 0,
    lock_password NVARCHAR(200) NULL,             -- plain text
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (owner_id) REFERENCES Users(id)
);
GO
CREATE INDEX IX_Notes_owner ON Notes(owner_id);
GO

CREATE TABLE Tags
(
    id INT IDENTITY(1,1) PRIMARY KEY,
    user_id INT NOT NULL,
    name NVARCHAR(100) NOT NULL,
    UNIQUE (user_id, name),
    FOREIGN KEY (user_id) REFERENCES Users(id)
);
GO

CREATE TABLE NoteTags
(
    note_id INT NOT NULL,
    tag_id INT NOT NULL,
    PRIMARY KEY (note_id, tag_id),
    FOREIGN KEY (note_id) REFERENCES Notes(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES Tags(id) ON DELETE CASCADE
);
GO

/* ---------- Group notes: members and contributions ---------- */

-- Group notes only. The owner also gets a row here. Leaving a group = delete your row.
CREATE TABLE NoteMembers
(
    note_id INT NOT NULL,
    user_id INT NOT NULL,
    joined_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    PRIMARY KEY (note_id, user_id),
    FOREIGN KEY (note_id) REFERENCES Notes(id) ON DELETE CASCADE,
    FOREIGN KEY (user_id) REFERENCES Users(id)
);
GO
CREATE INDEX IX_NoteMembers_user ON NoteMembers(user_id);
GO

-- One item added to a group note by a member. Own contributions only can be edited or deleted.
CREATE TABLE Contributions
(
    id INT IDENTITY(1,1) PRIMARY KEY,
    note_id INT NOT NULL,
    user_id INT NOT NULL,
    type NVARCHAR(20) NOT NULL CHECK (type IN (N'text', N'voice', N'reminder', N'location')),
    text NVARCHAR(MAX) NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UNIQUE (note_id, id),
    FOREIGN KEY (note_id) REFERENCES Notes(id) ON DELETE CASCADE,
    FOREIGN KEY (user_id) REFERENCES Users(id)
);
GO

-- Hides one contribution from one member (voice command "don't share with X").
CREATE TABLE NoteExclusions
(
    id INT IDENTITY(1,1) PRIMARY KEY,
    note_id INT NOT NULL,
    contribution_id INT NOT NULL,
    user_id INT NOT NULL,                          -- the member who must not see it
    created_by INT NOT NULL,
    UNIQUE (contribution_id, user_id),
    FOREIGN KEY (note_id) REFERENCES Notes(id) ON DELETE CASCADE,
    FOREIGN KEY (note_id, contribution_id) REFERENCES Contributions(note_id, id),
    FOREIGN KEY (user_id) REFERENCES Users(id),
    FOREIGN KEY (created_by) REFERENCES Users(id)
);
GO

/* ---------- Reminders, locations, voice notes ----------
   Personal note item: contribution_id is NULL. Group note item: contribution_id is set.
   Deleting the note deletes them all. Deleting ONE contribution: the service deletes its
   reminder/location/voice rows first, then the contribution. */

CREATE TABLE Reminders
(
    id INT IDENTITY(1,1) PRIMARY KEY,
    note_id INT NOT NULL,
    contribution_id INT NULL,
    created_by INT NOT NULL,
    title NVARCHAR(250) NOT NULL,
    remind_at DATETIME2 NOT NULL,
    is_done BIT NOT NULL DEFAULT 0,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (note_id) REFERENCES Notes(id) ON DELETE CASCADE,
    FOREIGN KEY (note_id, contribution_id) REFERENCES Contributions(note_id, id),
    FOREIGN KEY (created_by) REFERENCES Users(id)
);
GO
CREATE INDEX IX_Reminders_remind_at ON Reminders(remind_at);
CREATE UNIQUE INDEX UX_Reminders_one_per_personal_note ON Reminders(note_id) WHERE contribution_id IS NULL;
GO

CREATE TABLE Locations
(
    id INT IDENTITY(1,1) PRIMARY KEY,
    note_id INT NOT NULL,
    contribution_id INT NULL,
    created_by INT NOT NULL,
    place_name NVARCHAR(250) NOT NULL,
    sub_location NVARCHAR(250) NULL,
    latitude DECIMAL(9,6) NULL,
    longitude DECIMAL(9,6) NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (latitude IS NULL OR latitude BETWEEN -90 AND 90),
    CHECK (longitude IS NULL OR longitude BETWEEN -180 AND 180),
    CHECK ((latitude IS NULL AND longitude IS NULL) OR (latitude IS NOT NULL AND longitude IS NOT NULL)),
    FOREIGN KEY (note_id) REFERENCES Notes(id) ON DELETE CASCADE,
    FOREIGN KEY (note_id, contribution_id) REFERENCES Contributions(note_id, id),
    FOREIGN KEY (created_by) REFERENCES Users(id)
);
GO
CREATE UNIQUE INDEX UX_Locations_one_per_personal_note ON Locations(note_id) WHERE contribution_id IS NULL;
GO

CREATE TABLE VoiceNotes
(
    id INT IDENTITY(1,1) PRIMARY KEY,
    note_id INT NOT NULL,
    contribution_id INT NULL,
    created_by INT NOT NULL,
    display_name NVARCHAR(250) NOT NULL,
    file_path NVARCHAR(1000) NOT NULL,             -- file stays on disk, DB keeps the path
    duration_seconds INT NULL,
    transcript NVARCHAR(MAX) NULL,
    language NVARCHAR(50) NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (note_id) REFERENCES Notes(id) ON DELETE CASCADE,
    FOREIGN KEY (note_id, contribution_id) REFERENCES Contributions(note_id, id),
    FOREIGN KEY (created_by) REFERENCES Users(id)
);
GO

/* ---------- Messages (1:1 chat, can share a personal note) ----------
   Sharing copies the note's title and content into the message (a snapshot),
   so the message still shows it after the owner deletes the note from his library. */

CREATE TABLE Messages
(
    id INT IDENTITY(1,1) PRIMARY KEY,
    sender_id INT NOT NULL,
    receiver_id INT NOT NULL,
    message_text NVARCHAR(MAX) NULL,
    shared_note_id INT NULL,                       -- becomes NULL if the note is deleted
    shared_title NVARCHAR(250) NULL,               -- snapshot
    shared_content NVARCHAR(MAX) NULL,             -- snapshot
    is_read BIT NOT NULL DEFAULT 0,
    sent_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (sender_id <> receiver_id),
    CHECK (NULLIF(LTRIM(RTRIM(message_text)), N'') IS NOT NULL OR shared_title IS NOT NULL),
    FOREIGN KEY (sender_id) REFERENCES Users(id),
    FOREIGN KEY (receiver_id) REFERENCES Users(id),
    FOREIGN KEY (shared_note_id) REFERENCES Notes(id) ON DELETE SET NULL
);
GO
CREATE INDEX IX_Messages_pair ON Messages(sender_id, receiver_id);
GO

/* ---------- Voice commands (English and Urdu) ---------- */

CREATE TABLE CommandKeywords
(
    id INT IDENTITY(1,1) PRIMARY KEY,
    language NVARCHAR(10) NOT NULL CHECK (language IN (N'en', N'ur')),
    keyword NVARCHAR(100) NOT NULL,
    action NVARCHAR(30) NOT NULL CHECK (action IN (N'add_reminder', N'add_location', N'exclude_user')),
    UNIQUE (language, keyword)
);
GO

INSERT INTO CommandKeywords (language, keyword, action) VALUES
 (N'en', N'remind me',        N'add_reminder'),
 (N'en', N'set a reminder',   N'add_reminder'),
 (N'en', N'reminder',         N'add_reminder'),
 (N'en', N'remind',           N'add_reminder'),
 (N'ur', N'یاد دلاؤ',         N'add_reminder'),
 (N'ur', N'یاد دہانی',        N'add_reminder'),
 (N'en', N'add location',     N'add_location'),
 (N'en', N'location',         N'add_location'),
 (N'en', N'place',            N'add_location'),
 (N'ur', N'مقام',             N'add_location'),
 (N'ur', N'جگہ',              N'add_location'),
 (N'en', N'don''t share',     N'exclude_user'),
 (N'en', N'do not share',     N'exclude_user'),
 (N'en', N'don''t send',      N'exclude_user'),
 (N'ur', N'مت بھیجو',         N'exclude_user'),
 (N'ur', N'مت دکھاؤ',         N'exclude_user');
GO
