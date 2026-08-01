-- Jadeer Exam — create MySQL user (optional)
-- Run as root, then update appsettings.Development.json

CREATE DATABASE IF NOT EXISTS jadeer_exam
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

CREATE USER IF NOT EXISTS 'jadeer'@'localhost' IDENTIFIED BY 'CHANGE_ME';
CREATE USER IF NOT EXISTS 'jadeer'@'127.0.0.1' IDENTIFIED BY 'CHANGE_ME';

GRANT ALL PRIVILEGES ON jadeer_exam.* TO 'jadeer'@'localhost';
GRANT ALL PRIVILEGES ON jadeer_exam.* TO 'jadeer'@'127.0.0.1';
FLUSH PRIVILEGES;
