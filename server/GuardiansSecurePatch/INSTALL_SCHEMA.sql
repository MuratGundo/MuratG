-- U-CREW Guardians güvenli yama tabloları
-- Hedef veritabanı: yeni MySQL tabanlı U-CREW sistemi
-- Eski UUM/SQLite kullanılmaz.

SET NAMES utf8mb4;
SET time_zone = '+00:00';

CREATE TABLE IF NOT EXISTS secure_patch_files (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    game_id BIGINT UNSIGNED NOT NULL,
    version VARCHAR(64) NOT NULL,
    file_path VARCHAR(1024) NOT NULL,
    file_name VARCHAR(255) NOT NULL,
    file_size BIGINT UNSIGNED NOT NULL DEFAULT 0,
    sha256 CHAR(64) NOT NULL,
    key_base64 VARCHAR(128) NOT NULL,
    iv_base64 VARCHAR(64) NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'active',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    KEY idx_secure_patch_files_game_status (game_id, status),
    KEY idx_secure_patch_files_sha256 (sha256)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS secure_patch_tickets (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    user_id BIGINT UNSIGNED NOT NULL,
    game_id BIGINT UNSIGNED NOT NULL,
    patch_id BIGINT UNSIGNED NOT NULL,
    token CHAR(64) NOT NULL,
    hwid CHAR(64) NOT NULL,
    expires_at DATETIME NOT NULL,
    used_at DATETIME NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uq_secure_patch_tickets_token (token),
    KEY idx_secure_patch_tickets_user_game (user_id, game_id),
    KEY idx_secure_patch_tickets_patch (patch_id),
    KEY idx_secure_patch_tickets_expiry (expires_at),
    KEY idx_secure_patch_tickets_hwid (hwid)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Mevcut tablolar daha eski bir şemayla oluşturulduysa eksik sütunları ekle.
DROP PROCEDURE IF EXISTS ucrew_add_column_if_missing;
DELIMITER $$
CREATE PROCEDURE ucrew_add_column_if_missing(
    IN p_table VARCHAR(64),
    IN p_column VARCHAR(64),
    IN p_definition TEXT
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = p_table
          AND COLUMN_NAME = p_column
    ) THEN
        SET @sql_text = CONCAT(
            'ALTER TABLE `', REPLACE(p_table, '`', '``'),
            '` ADD COLUMN `', REPLACE(p_column, '`', '``'),
            '` ', p_definition
        );
        PREPARE stmt FROM @sql_text;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END$$
DELIMITER ;

CALL ucrew_add_column_if_missing('secure_patch_files', 'game_id', 'BIGINT UNSIGNED NOT NULL DEFAULT 0');
CALL ucrew_add_column_if_missing('secure_patch_files', 'version', 'VARCHAR(64) NOT NULL DEFAULT ''1.0.0''');
CALL ucrew_add_column_if_missing('secure_patch_files', 'file_path', 'VARCHAR(1024) NOT NULL DEFAULT ''''');
CALL ucrew_add_column_if_missing('secure_patch_files', 'file_name', 'VARCHAR(255) NOT NULL DEFAULT ''''');
CALL ucrew_add_column_if_missing('secure_patch_files', 'file_size', 'BIGINT UNSIGNED NOT NULL DEFAULT 0');
CALL ucrew_add_column_if_missing('secure_patch_files', 'sha256', 'CHAR(64) NOT NULL DEFAULT ''''');
CALL ucrew_add_column_if_missing('secure_patch_files', 'key_base64', 'VARCHAR(128) NOT NULL DEFAULT ''''');
CALL ucrew_add_column_if_missing('secure_patch_files', 'iv_base64', 'VARCHAR(64) NOT NULL DEFAULT ''''');
CALL ucrew_add_column_if_missing('secure_patch_files', 'status', 'VARCHAR(20) NOT NULL DEFAULT ''active''');
CALL ucrew_add_column_if_missing('secure_patch_files', 'created_at', 'DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP');
CALL ucrew_add_column_if_missing('secure_patch_files', 'updated_at', 'DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP');

CALL ucrew_add_column_if_missing('secure_patch_tickets', 'user_id', 'BIGINT UNSIGNED NOT NULL DEFAULT 0');
CALL ucrew_add_column_if_missing('secure_patch_tickets', 'game_id', 'BIGINT UNSIGNED NOT NULL DEFAULT 0');
CALL ucrew_add_column_if_missing('secure_patch_tickets', 'patch_id', 'BIGINT UNSIGNED NOT NULL DEFAULT 0');
CALL ucrew_add_column_if_missing('secure_patch_tickets', 'token', 'CHAR(64) NOT NULL DEFAULT ''''');
CALL ucrew_add_column_if_missing('secure_patch_tickets', 'hwid', 'CHAR(64) NOT NULL DEFAULT ''''');
CALL ucrew_add_column_if_missing('secure_patch_tickets', 'expires_at', 'DATETIME NOT NULL DEFAULT ''1970-01-01 00:00:01''');
CALL ucrew_add_column_if_missing('secure_patch_tickets', 'used_at', 'DATETIME NULL');
CALL ucrew_add_column_if_missing('secure_patch_tickets', 'created_at', 'DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP');

DROP PROCEDURE IF EXISTS ucrew_add_column_if_missing;

-- Kurulum doğrulaması
SELECT TABLE_NAME, ENGINE, TABLE_COLLATION
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME IN ('secure_patch_files', 'secure_patch_tickets');

SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME IN ('secure_patch_files', 'secure_patch_tickets')
ORDER BY TABLE_NAME, ORDINAL_POSITION;
