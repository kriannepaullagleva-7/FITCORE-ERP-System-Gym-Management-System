using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP_infrastructure.Migrations.TenantErpDb
{
    /// <inheritdoc />
    public partial class AddPaymentCategoryAndProcessedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProcessedBy",
                table: "Sales",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ProcessedByUserId",
                table: "Sales",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessedBy",
                table: "Payrolls",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ProcessedByUserId",
                table: "Payrolls",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Payments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "General");

            migrationBuilder.AddColumn<string>(
                name: "ProcessedBy",
                table: "Payments",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ProcessedByUserId",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Category",
                table: "Payments",
                column: "Category");

            // Classify the takings that predate the column. Every existing payment already
            // records what it settles, so the category is recoverable rather than guessed:
            // a payment against a sale is Sales, one against a subscription is Membership,
            // and money taken from a member for neither stays General.
            //
            // Done here rather than in code so the history is correct in every tenant
            // database the moment the column exists, including any restored from backup.
            migrationBuilder.Sql(@"
                UPDATE Payments SET Category = 'Sales'      WHERE SaleId IS NOT NULL;
                UPDATE Payments SET Category = 'Membership' WHERE SaleId IS NULL AND SubscriptionId IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_Category",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProcessedBy",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ProcessedByUserId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ProcessedBy",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "ProcessedByUserId",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProcessedBy",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProcessedByUserId",
                table: "Payments");
        }
    }
}
