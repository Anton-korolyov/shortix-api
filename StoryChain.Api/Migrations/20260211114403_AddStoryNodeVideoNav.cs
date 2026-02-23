using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryChain.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryNodeVideoNav : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StoryNodes_VideoId",
                table: "StoryNodes",
                column: "VideoId");

            migrationBuilder.AddForeignKey(
                name: "FK_StoryNodes_Videos_VideoId",
                table: "StoryNodes",
                column: "VideoId",
                principalTable: "Videos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StoryNodes_Videos_VideoId",
                table: "StoryNodes");

            migrationBuilder.DropIndex(
                name: "IX_StoryNodes_VideoId",
                table: "StoryNodes");
        }
    }
}
