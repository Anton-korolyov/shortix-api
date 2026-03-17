using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryChain.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BloodScore",
                table: "Videos",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DisgustScore",
                table: "Videos",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModeratedAt",
                table: "Videos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModerationStatus",
                table: "Videos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "NsfwScore",
                table: "Videos",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "WeaponScore",
                table: "Videos",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoTags_Tag",
                table: "VideoTags",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_VideoCategories_Name",
                table: "VideoCategories",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VideoTags_Tag",
                table: "VideoTags");

            migrationBuilder.DropIndex(
                name: "IX_VideoCategories_Name",
                table: "VideoCategories");

            migrationBuilder.DropColumn(
                name: "BloodScore",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "DisgustScore",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "ModeratedAt",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "ModerationStatus",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "NsfwScore",
                table: "Videos");

            migrationBuilder.DropColumn(
                name: "WeaponScore",
                table: "Videos");
        }
    }
}
