-- Application-owned tables for PersonalEconomicCoach.
-- IMPORTANT: Run this in the EXISTING spankki database.
-- This script only creates our application tables. It does not ALTER, UPDATE,
-- DELETE, rename, or create views over the existing S-Pankki banking tables.

USE spankki;

CREATE TABLE IF NOT EXISTS planned_events (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    customer_id VARCHAR(32) NOT NULL,
    name VARCHAR(120) NOT NULL,
    event_date DATE NOT NULL,
    amount_eur DECIMAL(15,2) NOT NULL DEFAULT 0.00,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX ix_planned_events_customer_date (customer_id, event_date)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS user_plans (
    customer_id VARCHAR(32) NOT NULL PRIMARY KEY,
    irregular_monthly_adjusted DECIMAL(15,2) NULL,
    safety_buffer_eur DECIMAL(15,2) NOT NULL DEFAULT 50.00,
    savings_goal_eur DECIMAL(15,2) NOT NULL DEFAULT 100.00,
    payday DATE NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS basket_overrides (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    customer_id VARCHAR(32) NOT NULL,
    match_type ENUM('merchant','category') NOT NULL,
    match_value VARCHAR(120) NOT NULL,
    basket ENUM('regular','irregular','leaks') NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY ux_basket_override_customer_match (customer_id, match_type, match_value),
    INDEX ix_basket_override_customer (customer_id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS conversation_threads (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    customer_id VARCHAR(32) NOT NULL,
    title VARCHAR(150) NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX ix_conversation_threads_customer_updated (customer_id, updated_at)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS conversation_messages (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    conversation_id BIGINT NOT NULL,
    customer_id VARCHAR(32) NOT NULL,
    role VARCHAR(20) NOT NULL,
    content LONGTEXT NOT NULL,
    insight_type VARCHAR(40) NULL,
    data_json JSON NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX ix_conversation_messages_customer_conversation (customer_id, conversation_id, id),
    CONSTRAINT fk_conversation_messages_thread
        FOREIGN KEY (conversation_id) REFERENCES conversation_threads(id)
        ON DELETE CASCADE
) ENGINE=InnoDB;
