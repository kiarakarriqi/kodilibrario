-- Manually apply the FixupBorrowRequestAndDepositTables migration
-- This is the same DDL as in 20260513210000_FixupBorrowRequestAndDepositTables.cs
-- Run with:  sqlite3 library.db < apply_fixup.sql
-- (Only needed if you want to fix an existing database WITHOUT rebuilding from scratch.
--  If you delete library.db and restart the API, the migration runs automatically.)

CREATE TABLE IF NOT EXISTS "BorrowRequests" (
    "Id"                  INTEGER NOT NULL CONSTRAINT "PK_BorrowRequests" PRIMARY KEY AUTOINCREMENT,
    "UserId"              INTEGER NOT NULL,
    "BookId"              INTEGER NOT NULL,
    "Days"                INTEGER NOT NULL DEFAULT 14,
    "Notes"               TEXT NULL,
    "Status"              INTEGER NOT NULL DEFAULT 0,
    "RequestedAt"         TEXT NOT NULL,
    "ExpiresAt"           TEXT NOT NULL,
    "ProcessedAt"         TEXT NULL,
    "ProcessedByStaffId"  INTEGER NULL,
    "RejectionReason"     TEXT NULL,
    "LoanId"              INTEGER NULL,
    CONSTRAINT "FK_BorrowRequests_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_BorrowRequests_Books_BookId"
        FOREIGN KEY ("BookId") REFERENCES "Books" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_BorrowRequests_Users_ProcessedByStaffId"
        FOREIGN KEY ("ProcessedByStaffId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_BorrowRequests_Loans_LoanId"
        FOREIGN KEY ("LoanId") REFERENCES "Loans" ("Id") ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS "IX_BorrowRequests_UserId"             ON "BorrowRequests" ("UserId");
CREATE INDEX IF NOT EXISTS "IX_BorrowRequests_BookId"             ON "BorrowRequests" ("BookId");
CREATE INDEX IF NOT EXISTS "IX_BorrowRequests_ProcessedByStaffId" ON "BorrowRequests" ("ProcessedByStaffId");
CREATE INDEX IF NOT EXISTS "IX_BorrowRequests_LoanId"             ON "BorrowRequests" ("LoanId");
CREATE INDEX IF NOT EXISTS "IX_BorrowRequests_Status"             ON "BorrowRequests" ("Status");

CREATE TABLE IF NOT EXISTS "Deposits" (
    "Id"                  INTEGER NOT NULL CONSTRAINT "PK_Deposits" PRIMARY KEY AUTOINCREMENT,
    "LoanId"              INTEGER NOT NULL,
    "MemberId"            INTEGER NOT NULL,
    "Amount"              TEXT NOT NULL,
    "Status"              INTEGER NOT NULL DEFAULT 0,
    "CollectedAt"         TEXT NOT NULL,
    "ReturnedAt"          TEXT NULL,
    "CollectedByStaffId"  INTEGER NOT NULL,
    "ReturnedByStaffId"   INTEGER NULL,
    "Notes"               TEXT NULL,
    CONSTRAINT "FK_Deposits_Loans_LoanId"
        FOREIGN KEY ("LoanId") REFERENCES "Loans" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Deposits_Users_MemberId"
        FOREIGN KEY ("MemberId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Deposits_Users_CollectedByStaffId"
        FOREIGN KEY ("CollectedByStaffId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Deposits_Users_ReturnedByStaffId"
        FOREIGN KEY ("ReturnedByStaffId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS "IX_Deposits_LoanId"              ON "Deposits" ("LoanId");
CREATE INDEX IF NOT EXISTS "IX_Deposits_MemberId"            ON "Deposits" ("MemberId");
CREATE INDEX IF NOT EXISTS "IX_Deposits_CollectedByStaffId"  ON "Deposits" ("CollectedByStaffId");
CREATE INDEX IF NOT EXISTS "IX_Deposits_ReturnedByStaffId"   ON "Deposits" ("ReturnedByStaffId");
CREATE INDEX IF NOT EXISTS "IX_Deposits_Status"              ON "Deposits" ("Status");

-- Mark the fixup migration as applied so EF Core does not try to run it again
INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260513210000_FixupBorrowRequestAndDepositTables', '7.0.20');
