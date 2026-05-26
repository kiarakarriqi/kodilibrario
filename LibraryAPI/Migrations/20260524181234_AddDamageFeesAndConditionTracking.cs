using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddDamageFeesAndConditionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConditionNotes",
                table: "Loans",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DamageFee",
                table: "Loans",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ReturnCondition",
                table: "Loans",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LostBookFee",
                table: "FinePolicies",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MajorDamageFee",
                table: "FinePolicies",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinorWearFee",
                table: "FinePolicies",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SignificantDamageFee",
                table: "FinePolicies",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                "UPDATE \"FinePolicies\" SET " +
                "\"MinorWearFee\" = 2.00, " +
                "\"SignificantDamageFee\" = 5.00, " +
                "\"MajorDamageFee\" = 10.00, " +
                "\"LostBookFee\" = 20.00;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConditionNotes",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "DamageFee",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "ReturnCondition",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "LostBookFee",
                table: "FinePolicies");

            migrationBuilder.DropColumn(
                name: "MajorDamageFee",
                table: "FinePolicies");

            migrationBuilder.DropColumn(
                name: "MinorWearFee",
                table: "FinePolicies");

            migrationBuilder.DropColumn(
                name: "SignificantDamageFee",
                table: "FinePolicies");
        }
    }
}
