using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace explAInedCommentService.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorCreatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Comments_AuthorId_CreatedAt",
                table: "Comments",
                columns: new[] { "AuthorId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Comments_AuthorId_CreatedAt",
                table: "Comments");
        }
    }
}
