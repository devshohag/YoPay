using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YoPay.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WebhookDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "secret_encrypted",
                schema: "yopay",
                table: "webhook_endpoints",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "signature",
                schema: "yopay",
                table: "webhook_deliveries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "event_type",
                schema: "yopay",
                table: "webhook_deliveries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "invoice_id",
                schema: "yopay",
                table: "outbox_messages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "merchant_id",
                schema: "yopay",
                table: "outbox_messages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_invoice_id",
                schema: "yopay",
                table: "outbox_messages",
                column: "invoice_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_invoice_id",
                schema: "yopay",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "secret_encrypted",
                schema: "yopay",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "event_type",
                schema: "yopay",
                table: "webhook_deliveries");

            migrationBuilder.DropColumn(
                name: "invoice_id",
                schema: "yopay",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "merchant_id",
                schema: "yopay",
                table: "outbox_messages");

            migrationBuilder.AlterColumn<string>(
                name: "signature",
                schema: "yopay",
                table: "webhook_deliveries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
