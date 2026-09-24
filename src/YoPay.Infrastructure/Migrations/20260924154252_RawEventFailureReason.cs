using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YoPay.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RawEventFailureReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "failure_reason",
                schema: "yopay",
                table: "raw_events",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "failure_reason",
                schema: "yopay",
                table: "raw_events");
        }
    }
}
