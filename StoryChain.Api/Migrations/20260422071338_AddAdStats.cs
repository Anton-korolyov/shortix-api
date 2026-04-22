using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryChain.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Clicks",
                table: "Ads",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Views",
                table: "Ads",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Clicks",
                table: "Ads");

            migrationBuilder.DropColumn(
                name: "Views",
                table: "Ads");
        }
    }
}
