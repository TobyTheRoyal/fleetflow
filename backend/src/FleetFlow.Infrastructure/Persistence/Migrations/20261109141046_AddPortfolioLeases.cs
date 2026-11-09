using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetFlow.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddPortfolioLeases : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "public");

        migrationBuilder.AddColumn<bool>(
            name: "IsDemo",
            table: "AspNetUsers",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "PortfolioSlots",
            schema: "public",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                SchemaName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Ready = table.Column<bool>(type: "boolean", nullable: false),
                Writes = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PortfolioSlots", x => x.Id);
                table.CheckConstraint("CK_PortfolioSlots_Bounds", "\"Id\" BETWEEN 1 AND 13 AND \"Role\" IN ('Viewer','Dispatcher') AND \"Writes\" >= 0");
                table.ForeignKey(
                    name: "FK_PortfolioSlots_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PortfolioSlots_UserId",
            schema: "public",
            table: "PortfolioSlots",
            column: "UserId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
              IF EXISTS(SELECT FROM public."PortfolioSlots") OR EXISTS(SELECT FROM pg_namespace WHERE nspname LIKE 'portfolio\_%' ESCAPE '\') THEN
                RAISE EXCEPTION 'An initialized portfolio database cannot be downgraded to the unisolated schema';
              END IF;
            END $$;
            """);
        migrationBuilder.DropTable(
            name: "PortfolioSlots",
            schema: "public");

        migrationBuilder.DropColumn(
            name: "IsDemo",
            table: "AspNetUsers");
    }
}
