ALTER TABLE "Loans" ADD COLUMN "RenewalCount"   INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "Loans" ADD COLUMN "LastRenewedAt"  TEXT NULL;
INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260401000000_AddLoanRenewal', '7.0.20');
INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260513210000_FixupBorrowRequestAndDepositTables', '7.0.20');
