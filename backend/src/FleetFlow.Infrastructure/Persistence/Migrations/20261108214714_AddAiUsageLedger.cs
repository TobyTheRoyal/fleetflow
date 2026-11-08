using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetFlow.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAiUsageLedger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AiAttempts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                DayUtc = table.Column<DateOnly>(type: "date", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                SelectionJson = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: true),
                CacheExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ErrorCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                RetryAfterUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiAttempts", x => x.Id);
                table.CheckConstraint("CK_AiAttempts_Fingerprint", "length(\"Fingerprint\") = 64");
                table.CheckConstraint("CK_AiAttempts_Lease", "\"LeaseExpiresAtUtc\" > \"StartedAtUtc\"");
                table.ForeignKey(
                    name: "FK_AiAttempts_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AiAttempts_DayUtc_UserId",
            table: "AiAttempts",
            columns: new[] { "DayUtc", "UserId" });

        migrationBuilder.CreateIndex(
            name: "IX_AiAttempts_LeaseExpiresAtUtc",
            table: "AiAttempts",
            column: "LeaseExpiresAtUtc",
            filter: "\"CompletedAtUtc\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_AiAttempts_UserId_Fingerprint_CacheExpiresAtUtc",
            table: "AiAttempts",
            columns: new[] { "UserId", "Fingerprint", "CacheExpiresAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AiAttempts");
    }
}
