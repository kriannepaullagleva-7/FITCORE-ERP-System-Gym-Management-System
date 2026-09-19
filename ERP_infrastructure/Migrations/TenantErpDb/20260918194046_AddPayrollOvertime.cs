using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP_infrastructure.Migrations.TenantErpDb
{
    /// <inheritdoc />
    public partial class AddPayrollOvertime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OvertimeHours",
                table: "Payrolls",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OvertimePay",
                table: "Payrolls",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OvertimeRate",
                table: "Payrolls",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OvertimeHours",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "OvertimePay",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "OvertimeRate",
                table: "Payrolls");
        }
    }
}
