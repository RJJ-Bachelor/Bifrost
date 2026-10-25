using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GnaService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeliveryType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryType",
                table: "notifications",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "RealtimeOrPush");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryType",
                table: "notifications");
        }
    }
}
