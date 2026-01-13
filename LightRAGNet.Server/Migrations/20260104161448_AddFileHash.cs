using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightRAGNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddFileHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileHash",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocuments_FileHash",
                table: "MarkdownDocuments",
                column: "FileHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MarkdownDocuments_FileHash",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "FileHash",
                table: "MarkdownDocuments");
        }
    }
}
