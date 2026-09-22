using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YoPay.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PaymentClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_claims",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    normalised_trx_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    client_ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_claims_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "yopay",
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_claims_invoice_id_normalised_trx_id",
                schema: "yopay",
                table: "payment_claims",
                columns: new[] { "invoice_id", "normalised_trx_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_claims_normalised_trx_id_state",
                schema: "yopay",
                table: "payment_claims",
                columns: new[] { "normalised_trx_id", "state" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_claims",
                schema: "yopay");
        }
    }
}
