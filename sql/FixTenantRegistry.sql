-- ---------------------------------------------------------------------------------------
-- FitCore ERP - repair the tenant registry in the MASTER database (MasterErp / db68434).
--
-- Run this against the MASTER database only. It touches no tenant business data and makes
-- no schema change, so no EF Core migration is involved.
--
-- Why this is needed
-- ------------------
-- The API resolves a tenant like this:
--     CompanyId -> CompanyDatabases row -> ServerName + DatabaseName + CredentialKey
--     CredentialKey -> appsettings "TenantCredentials:<key>" -> UserId + Password
--
-- As shipped, no row resolves:
--   * every CompanyDatabases row points at CompanyId 1, but the application serves company 3
--   * rows 1 and 2 have an empty CredentialKey, so no login can be looked up
--   * row 3 has ServerName 'db68521.databaseasp.net', which is missing the '.public'
--     segment and does not resolve to the SQL host
--
-- Until this is fixed the API falls back to ConnectionStrings:TenantErp and logs a warning
-- on every request. That fallback is deliberate, so nothing is broken today, but it means
-- every company is served the same database.
-- ---------------------------------------------------------------------------------------

SET NOCOUNT ON;

PRINT '--- BEFORE ---';
SELECT CompanyDatabaseId, CompanyId, ServerName, DatabaseName, CredentialKey, IsActive
FROM   dbo.CompanyDatabases
ORDER  BY CompanyDatabaseId;

BEGIN TRANSACTION;

-- 1. Correct the host name on any row that is missing the '.public' segment.
UPDATE dbo.CompanyDatabases
SET    ServerName = REPLACE(ServerName, '.databaseasp.net', '.public.databaseasp.net')
WHERE  ServerName LIKE '%.databaseasp.net'
  AND  ServerName NOT LIKE '%.public.databaseasp.net';

-- 2. Retire the rows that point at the dead db66559 server and carry no credential key.
--    They are deactivated rather than deleted so the history is kept.
UPDATE dbo.CompanyDatabases
SET    IsActive = 0
WHERE  (CredentialKey IS NULL OR LTRIM(RTRIM(CredentialKey)) = '')
  AND  IsActive = 1;

-- 3. Give company 3 (COMP002), the company the application actually serves, an active row
--    pointing at the database that currently holds the data. 'TenantA' is the credential key
--    whose TenantCredentials entry matches db68433.
IF NOT EXISTS (SELECT 1 FROM dbo.CompanyDatabases WHERE CompanyId = 3 AND IsActive = 1)
BEGIN
    INSERT INTO dbo.CompanyDatabases (CompanyId, ServerName, DatabaseName, CredentialKey, IsActive)
    VALUES (3, 'db68433.public.databaseasp.net', 'db68433', 'TenantA', 1);
END

-- 4. Optional: point company 1 at its own database so the two companies are genuinely
--    isolated. Uncomment once db68484 is provisioned and migrated, and confirm the
--    TenantCredentials:TenantB entry matches it.
-- IF NOT EXISTS (SELECT 1 FROM dbo.CompanyDatabases WHERE CompanyId = 1 AND IsActive = 1)
-- BEGIN
--     INSERT INTO dbo.CompanyDatabases (CompanyId, ServerName, DatabaseName, CredentialKey, IsActive)
--     VALUES (1, 'db68484.public.databaseasp.net', 'db68484', 'TenantB', 1);
-- END

COMMIT TRANSACTION;

PRINT '--- AFTER ---';
SELECT CompanyDatabaseId, CompanyId, ServerName, DatabaseName, CredentialKey, IsActive
FROM   dbo.CompanyDatabases
ORDER  BY CompanyDatabaseId;

-- ---------------------------------------------------------------------------------------
-- After running this:
--   1. Restart ERP_api and call GET /api/tenant/current.
--      Expect "usingFallbackConnection": false.
--   2. When every company you serve has a correct row, set
--      "Tenancy:AllowConnectionStringFallback": false in ERP_api/appsettings.json so a bad
--      tenant configuration fails loudly instead of silently serving the default database.
-- ---------------------------------------------------------------------------------------
