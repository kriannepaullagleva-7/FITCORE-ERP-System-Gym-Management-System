-- SQL Script to fix Member table if needed
-- This script can be run directly on the TenantErp database if migrations fail

-- Add default constraint to Phone column
IF NOT EXISTS (
	SELECT * FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS 
	WHERE CONSTRAINT_NAME = 'DF_Members_Phone'
)
BEGIN
	ALTER TABLE Members ADD CONSTRAINT DF_Members_Phone DEFAULT '' FOR Phone;
END

-- Add default constraint to Email column
IF NOT EXISTS (
	SELECT * FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS 
	WHERE CONSTRAINT_NAME = 'DF_Members_Email'
)
BEGIN
	ALTER TABLE Members ADD CONSTRAINT DF_Members_Email DEFAULT '' FOR Email;
END

-- Add default constraint to Status column
IF NOT EXISTS (
	SELECT * FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS 
	WHERE CONSTRAINT_NAME = 'DF_Members_Status'
)
BEGIN
	ALTER TABLE Members ADD CONSTRAINT DF_Members_Status DEFAULT 'Active' FOR Status;
END

-- Add default constraint to JoinDate column
IF NOT EXISTS (
	SELECT * FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS 
	WHERE CONSTRAINT_NAME = 'DF_Members_JoinDate'
)
BEGIN
	ALTER TABLE Members ADD CONSTRAINT DF_Members_JoinDate DEFAULT GETUTCDATE() FOR JoinDate;
END

-- Add default constraint to CreatedAt column
IF NOT EXISTS (
	SELECT * FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS 
	WHERE CONSTRAINT_NAME = 'DF_Members_CreatedAt'
)
BEGIN
	ALTER TABLE Members ADD CONSTRAINT DF_Members_CreatedAt DEFAULT GETUTCDATE() FOR CreatedAt;
END

-- Validate the table structure
SELECT * FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'Members'
ORDER BY ORDINAL_POSITION;
