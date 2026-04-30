using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Seren.Modules.VoxMind.Speakers.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpeakerProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AliasesJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DetectionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakerProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeakerEmbeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    InitialConfidence = table.Column<float>(type: "REAL", nullable: false),
                    AudioDurationSeconds = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakerEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeakerEmbeddings_SpeakerProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "SpeakerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_embeddings_profile",
                table: "SpeakerEmbeddings",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "idx_profiles_lastseen",
                table: "SpeakerProfiles",
                column: "LastSeenAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpeakerEmbeddings");

            migrationBuilder.DropTable(
                name: "SpeakerProfiles");
        }
    }
}
