using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LightRAGNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentIntakePipelineFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveRagTaskId",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PipelineCancelledAt",
                table: "MarkdownDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PipelineCompletedAt",
                table: "MarkdownDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PipelineStartedAt",
                table: "MarkdownDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RagCurrentStage",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RagRetryCount",
                table: "MarkdownDocuments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TrackId",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocuments_ActiveRagTaskId",
                table: "MarkdownDocuments",
                column: "ActiveRagTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocuments_RagStatus",
                table: "MarkdownDocuments",
                column: "RagStatus");

            migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocuments_TrackId",
                table: "MarkdownDocuments",
                column: "TrackId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MarkdownDocuments_ActiveRagTaskId",
                table: "MarkdownDocuments");

            migrationBuilder.DropIndex(
                name: "IX_MarkdownDocuments_RagStatus",
                table: "MarkdownDocuments");

            migrationBuilder.DropIndex(
                name: "IX_MarkdownDocuments_TrackId",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "ActiveRagTaskId",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "PipelineCancelledAt",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "PipelineCompletedAt",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "PipelineStartedAt",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "RagCurrentStage",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "RagRetryCount",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "TrackId",
                table: "MarkdownDocuments");
        }
    }
}
