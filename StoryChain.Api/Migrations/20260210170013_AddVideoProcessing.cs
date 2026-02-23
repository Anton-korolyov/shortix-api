using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryChain.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Processing",
                table: "Videos",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Processing",
                table: "Videos");
        }
    }
}
