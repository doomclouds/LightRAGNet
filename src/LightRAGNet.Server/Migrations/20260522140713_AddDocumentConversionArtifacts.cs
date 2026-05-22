using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightRAGNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentConversionArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConversionCompletedAt",
                table: "MarkdownDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConversionErrorMessage",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConversionStartedAt",
                table: "MarkdownDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConversionStatus",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConversionTool",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConversionToolVersion",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConvertedMarkdownHash",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConvertedMarkdownPath",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalContentHash",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalContentType",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalFileName",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalFilePath",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocuments_ConversionStatus",
                table: "MarkdownDocuments",
                column: "ConversionStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MarkdownDocuments_ConversionStatus",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "ConversionCompletedAt",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "ConversionErrorMessage",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "ConversionStartedAt",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "ConversionStatus",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "ConversionTool",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "ConversionToolVersion",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "ConvertedMarkdownHash",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "ConvertedMarkdownPath",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "OriginalContentHash",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "OriginalContentType",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "OriginalFileName",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "OriginalFilePath",
                table: "MarkdownDocuments");
        }
    }
}
