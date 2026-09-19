using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP_infrastructure.Migrations.TenantErpDb
{
    /// <inheritdoc />
    public partial class AddSalePaymentLinkProductCostingAndStockAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_ProductId",
                table: "StockMovements");

            migrationBuilder.AddColumn<decimal>(
                name: "BalanceBefore",
                table: "StockMovements",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "RecordedByEmployeeId",
                table: "StockMovements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CashierEmployeeId",
                table: "Sales",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Discount",
                table: "Sales",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Sales",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Sales",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Completed");

            migrationBuilder.AddColumn<decimal>(
                name: "Subtotal",
                table: "Sales",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Products",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Other");

            migrationBuilder.AddColumn<decimal>(
                name: "CostPrice",
                table: "Products",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "SaleId",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductId_MovementDate",
                table: "StockMovements",
                columns: new[] { "ProductId", "MovementDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_RecordedByEmployeeId",
                table: "StockMovements",
                column: "RecordedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CashierEmployeeId",
                table: "Sales",
                column: "CashierEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_SaleDate",
                table: "Sales",
                column: "SaleDate");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaymentDate",
                table: "Payments",
                column: "PaymentDate");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_SaleId",
                table: "Payments",
                column: "SaleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Sales_SaleId",
                table: "Payments",
                column: "SaleId",
                principalTable: "Sales",
                principalColumn: "SaleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Employees_CashierEmployeeId",
                table: "Sales",
                column: "CashierEmployeeId",
                principalTable: "Employees",
                principalColumn: "EmployeeId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Employees_RecordedByEmployeeId",
                table: "StockMovements",
                column: "RecordedByEmployeeId",
                principalTable: "Employees",
                principalColumn: "EmployeeId",
                onDelete: ReferentialAction.SetNull);

            // Sales that predate the discount column were charged at their full line total, so
            // the new Subtotal is the existing TotalAmount. Without this backfill every
            // historical sale would report a subtotal of zero next to a non-zero total.
            migrationBuilder.Sql(
                "UPDATE Sales SET Subtotal = TotalAmount WHERE Subtotal = 0 AND TotalAmount <> 0;");

            // Reconstruct the opening balance of each historical movement from the balance it
            // left behind and the direction the movement went.
            migrationBuilder.Sql(@"
                UPDATE StockMovements
                SET BalanceBefore = CASE
                    WHEN MovementType = 'In' THEN BalanceAfter - Quantity
                    WHEN MovementType IN ('Out', 'Sale') THEN BalanceAfter + Quantity
                    ELSE BalanceAfter
                END
                WHERE BalanceBefore = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Sales_SaleId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Employees_CashierEmployeeId",
                table: "Sales");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Employees_RecordedByEmployeeId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_ProductId_MovementDate",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_RecordedByEmployeeId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_Sales_CashierEmployeeId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_SaleDate",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Payments_PaymentDate",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_SaleId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "BalanceBefore",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "RecordedByEmployeeId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "CashierEmployeeId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Discount",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Subtotal",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CostPrice",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SaleId",
                table: "Payments");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductId",
                table: "StockMovements",
                column: "ProductId");
        }
    }
}
