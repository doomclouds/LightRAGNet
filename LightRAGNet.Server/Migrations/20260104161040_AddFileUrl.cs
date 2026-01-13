using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightRAGNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddFileUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileUrl",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileUrl",
                table: "MarkdownDocuments");
        }
    }
}
