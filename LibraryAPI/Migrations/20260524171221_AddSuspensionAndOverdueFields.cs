using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddSuspensionAndOverdueFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. New column on Users
            migrationBuilder.AddColumn<DateTime>(
                name: "BorrowSuspendedUntil",
                table: "Users",
                type: "TEXT",
                nullable: true);

            // 2. New columns on FinePolicies
            migrationBuilder.AddColumn<int>(
                name: "SuspensionDays",
                table: "FinePolicies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<bool>(
                name: "AutoBanAfterSuspension",
                table: "FinePolicies",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            // NOTE: The following items are intentionally NOT included
            // because they already exist in the database from prior manual SQL:
            //   - Loans.RenewalCount column
            //   - Loans.LastRenewedAt column
            //   - BorrowRequests table + indexes
            //   - Deposits table + indexes
            // The EF snapshot was out of sync with the DB. This migration
            // only adds what is actually missing.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BorrowSuspendedUntil",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SuspensionDays",
                table: "FinePolicies");

            migrationBuilder.DropColumn(
                name: "AutoBanAfterSuspension",
                table: "FinePolicies");
        }
    }
}