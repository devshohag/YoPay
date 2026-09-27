using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YoPay.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceMatchingMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invoices_wallet_id",
                schema: "yopay",
                table: "invoices");

            migrationBuilder.AddColumn<int>(
                name: "mode",
                schema: "yopay",
                table: "invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_wallet_id_mode_status",
                schema: "yopay",
                table: "invoices",
                columns: new[] { "wallet_id", "mode", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invoices_wallet_id_mode_status",
                schema: "yopay",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "mode",
                schema: "yopay",
                table: "invoices");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_wallet_id",
                schema: "yopay",
                table: "invoices",
                column: "wallet_id");
        }
    }
}
