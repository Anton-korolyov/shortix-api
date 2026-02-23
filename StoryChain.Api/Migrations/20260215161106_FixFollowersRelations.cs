using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryChain.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixFollowersRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Followers_Users_FollowerUserId",
                table: "Followers");

            migrationBuilder.DropForeignKey(
                name: "FK_Followers_Users_FollowingId",
                table: "Followers");

            migrationBuilder.DropIndex(
                name: "IX_Followers_FollowerUserId",
                table: "Followers");

            migrationBuilder.DropColumn(
                name: "FollowerId",
                table: "Followers");

            migrationBuilder.RenameColumn(
                name: "FollowingId",
                table: "Followers",
                newName: "FollowingUserId");

            migrationBuilder.RenameIndex(
                name: "IX_Followers_FollowingId",
                table: "Followers",
                newName: "IX_Followers_FollowingUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Followers_FollowerUserId_FollowingUserId",
                table: "Followers",
                columns: new[] { "FollowerUserId", "FollowingUserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Followers_Users_FollowerUserId",
                table: "Followers",
                column: "FollowerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Followers_Users_FollowingUserId",
                table: "Followers",
                column: "FollowingUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Followers_Users_FollowerUserId",
                table: "Followers");

            migrationBuilder.DropForeignKey(
                name: "FK_Followers_Users_FollowingUserId",
                table: "Followers");

            migrationBuilder.DropIndex(
                name: "IX_Followers_FollowerUserId_FollowingUserId",
                table: "Followers");

            migrationBuilder.RenameColumn(
                name: "FollowingUserId",
                table: "Followers",
                newName: "FollowingId");

            migrationBuilder.RenameIndex(
                name: "IX_Followers_FollowingUserId",
                table: "Followers",
                newName: "IX_Followers_FollowingId");

            migrationBuilder.AddColumn<Guid>(
                name: "FollowerId",
                table: "Followers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Followers_FollowerUserId",
                table: "Followers",
                column: "FollowerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Followers_Users_FollowerUserId",
                table: "Followers",
                column: "FollowerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Followers_Users_FollowingId",
                table: "Followers",
                column: "FollowingId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
