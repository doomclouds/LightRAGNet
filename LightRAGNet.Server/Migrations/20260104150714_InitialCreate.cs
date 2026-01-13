using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightRAGNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarkdownDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    UploadTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsInRagSystem = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    RagAddedTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RagStatus = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, defaultValue: "Pending"),
                    RagErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RagProgress = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    RagDocumentId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarkdownDocuments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocuments_FileName",
                table: "MarkdownDocuments",
                column: "FileName");

            migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocuments_IsInRagSystem",
                table: "MarkdownDocuments",
                column: "IsInRagSystem");

            migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocuments_RagDocumentId",
                table: "MarkdownDocuments",
                column: "RagDocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarkdownDocuments");
        }
    }
}
