using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryChain.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoCategoryNew : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CategoryId",
                table: "Videos",
                newName: "VideoCategoryId");

            migrationBuilder.CreateTable(
                name: "VideoCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Videos_VideoCategoryId",
                table: "Videos",
                column: "VideoCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Videos_VideoCategories_VideoCategoryId",
                table: "Videos",
                column: "VideoCategoryId",
                principalTable: "VideoCategories",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Videos_VideoCategories_VideoCategoryId",
                table: "Videos");

            migrationBuilder.DropTable(
                name: "VideoCategories");

            migrationBuilder.DropIndex(
                name: "IX_Videos_VideoCategoryId",
                table: "Videos");

            migrationBuilder.RenameColumn(
                name: "VideoCategoryId",
                table: "Videos",
                newName: "CategoryId");
        }
    }
}
