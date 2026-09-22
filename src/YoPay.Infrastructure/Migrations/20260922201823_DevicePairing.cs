using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YoPay.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DevicePairing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_pairing_tokens",
                schema: "yopay",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_pairing_tokens", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_pairing_tokens_expires_at",
                schema: "yopay",
                table: "device_pairing_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_device_pairing_tokens_token_hash",
                schema: "yopay",
                table: "device_pairing_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_pairing_tokens",
                schema: "yopay");
        }
    }
}
