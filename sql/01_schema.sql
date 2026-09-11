-- Jadeer Exam — MySQL 8 schema
-- Database: jadeer_exam
-- Generated to match EF Core AppDbContext (Pomelo / MySQL)

CREATE DATABASE IF NOT EXISTS jadeer_exam
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

USE jadeer_exam;

-- Optional app user (change password before production):
-- CREATE USER IF NOT EXISTS 'jadeer'@'localhost' IDENTIFIED BY 'CHANGE_ME';
-- GRANT ALL PRIVILEGES ON jadeer_exam.* TO 'jadeer'@'localhost';
-- FLUSH PRIVILEGES;

SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS departments (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Name VARCHAR(120) NOT NULL,
  City LONGTEXT NULL,
  Sector LONGTEXT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY IX_departments_Name (Name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS users (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Username LONGTEXT NOT NULL,
  Email LONGTEXT NULL,
  NationalId LONGTEXT NULL,
  FullName LONGTEXT NOT NULL,
  Password LONGTEXT NOT NULL,
  Role LONGTEXT NOT NULL,
  MfaEnabled TINYINT(1) NOT NULL DEFAULT 0,
  MfaSecret LONGTEXT NULL,
  DepartmentId BIGINT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY IX_users_Username (Username(191)),
  UNIQUE KEY IX_users_Email (Email(191)),
  KEY IX_users_DepartmentId (DepartmentId),
  CONSTRAINT FK_users_departments_DepartmentId
    FOREIGN KEY (DepartmentId) REFERENCES departments (Id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS exams (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Title LONGTEXT NOT NULL,
  Subject LONGTEXT NOT NULL,
  DepartmentId BIGINT NULL,
  AssignedExaminerId BIGINT NULL,
  AssignedGraderId BIGINT NULL,
  DurationMinutes INT NULL,
  Randomized TINYINT(1) NOT NULL DEFAULT 1,
  OpportunityType LONGTEXT NULL,
  JobTitle LONGTEXT NULL,
  CompanyName LONGTEXT NULL,
  City LONGTEXT NULL,
  OpportunityYear INT NULL,
  TotalMarks INT NULL,
  PracticalEnabled TINYINT(1) NOT NULL DEFAULT 0,
  PracticalMaxMarks INT NULL,
  PassThreshold INT NOT NULL DEFAULT 70,
  Published TINYINT(1) NOT NULL DEFAULT 0,
  Template TINYINT(1) NOT NULL DEFAULT 0,
  CreationMode LONGTEXT NOT NULL,
  CreatedAt DATETIME(6) NOT NULL,
  ScheduledAt DATETIME(6) NULL,
  Instructions VARCHAR(4000) NULL,
  ExaminerReminderSentAt DATETIME(6) NULL,
  ExaminerCompletionReportSentAt DATETIME(6) NULL,
  PRIMARY KEY (Id),
  KEY IX_exams_DepartmentId (DepartmentId),
  KEY IX_exams_AssignedExaminerId (AssignedExaminerId),
  KEY IX_exams_AssignedGraderId (AssignedGraderId),
  CONSTRAINT FK_exams_departments_DepartmentId
    FOREIGN KEY (DepartmentId) REFERENCES departments (Id) ON DELETE SET NULL,
  CONSTRAINT FK_exams_users_AssignedExaminerId
    FOREIGN KEY (AssignedExaminerId) REFERENCES users (Id) ON DELETE SET NULL,
  CONSTRAINT FK_exams_users_AssignedGraderId
    FOREIGN KEY (AssignedGraderId) REFERENCES users (Id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS questions (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  ExamId BIGINT NULL,
  Text VARCHAR(2000) NOT NULL,
  OptionA LONGTEXT NULL,
  OptionB LONGTEXT NULL,
  OptionC LONGTEXT NULL,
  OptionD LONGTEXT NULL,
  CorrectOption LONGTEXT NULL,
  Mark INT NULL,
  Essay TINYINT(1) NOT NULL DEFAULT 0,
  DurationSeconds INT NULL,
  Topic LONGTEXT NULL,
  Difficulty LONGTEXT NULL,
  Rubric VARCHAR(1000) NULL,
  ImageFileName LONGTEXT NULL,
  ImageData LONGBLOB NULL,
  ImageContentType VARCHAR(64) NULL,
  PRIMARY KEY (Id),
  KEY IX_questions_ExamId (ExamId),
  CONSTRAINT FK_questions_exams_ExamId
    FOREIGN KEY (ExamId) REFERENCES exams (Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS exam_allowed_candidates (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  ExamId BIGINT NOT NULL,
  Email VARCHAR(190) NOT NULL,
  FullName VARCHAR(200) NULL,
  NationalId VARCHAR(50) NULL,
  InvitationSentAt DATETIME(6) NULL,
  ReminderSentAt DATETIME(6) NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY uk_exam_allowed_candidate_exam_email (ExamId, Email),
  CONSTRAINT FK_exam_allowed_candidates_exams_ExamId
    FOREIGN KEY (ExamId) REFERENCES exams (Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS exam_attempts (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  ExamId BIGINT NULL,
  CandidateUsername LONGTEXT NULL,
  StartedAt DATETIME(6) NOT NULL,
  SubmittedAt DATETIME(6) NULL,
  Score INT NULL,
  ObjectiveScore INT NOT NULL DEFAULT 0,
  EssayScore INT NOT NULL DEFAULT 0,
  PracticalScore INT NULL,
  Suspicious TINYINT(1) NOT NULL DEFAULT 0,
  AnswersJson LONGTEXT NULL,
  EssayScoreJson LONGTEXT NULL,
  PRIMARY KEY (Id),
  KEY IX_exam_attempts_ExamId (ExamId),
  CONSTRAINT FK_exam_attempts_exams_ExamId
    FOREIGN KEY (ExamId) REFERENCES exams (Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS suspicious_events (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  Username LONGTEXT NULL,
  EventType LONGTEXT NULL,
  Severity LONGTEXT NULL,
  Details VARCHAR(2000) NULL,
  ExamId BIGINT NULL,
  ConfidenceScore INT NULL,
  EventTime DATETIME(6) NOT NULL,
  PRIMARY KEY (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS exam_session_state (
  Id BIGINT NOT NULL AUTO_INCREMENT,
  CandidateUsername VARCHAR(120) NOT NULL,
  ExamId BIGINT NOT NULL,
  ClientSessionId VARCHAR(64) NOT NULL,
  CreatedAt DATETIME(6) NOT NULL,
  LastHeartbeat DATETIME(6) NOT NULL,
  Submitted TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (Id),
  UNIQUE KEY IX_exam_session_state_CandidateUsername_ExamId (CandidateUsername, ExamId),
  KEY IX_exam_session_state_ExamId (ExamId),
  CONSTRAINT FK_exam_session_state_exams_ExamId
    FOREIGN KEY (ExamId) REFERENCES exams (Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
