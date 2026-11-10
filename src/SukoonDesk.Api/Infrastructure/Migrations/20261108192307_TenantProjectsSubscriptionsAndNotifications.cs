using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SukoonDesk.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantProjectsSubscriptionsAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingNotes",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MonthlyComplaintLimit",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 5000);

            migrationBuilder.AddColumn<int>(
                name: "ResidentLimit",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 2000);

            migrationBuilder.AddColumn<int>(
                name: "StaffLimit",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubscriptionEndsAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionPlan",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionStatus",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrialEndsAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanReceiveAssignments",
                table: "TenantRoles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyByEmail",
                table: "PublicMembers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyByWhatsApp",
                table: "PublicMembers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "PublicMembers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppNumber",
                table: "PublicMembers",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "TenantProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantProjects_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublicMembers_ProjectId",
                table: "PublicMembers",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantProjects_TenantId_Code",
                table: "TenantProjects",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantProjects_TenantId_Name",
                table: "TenantProjects",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PublicMembers_TenantProjects_ProjectId",
                table: "PublicMembers",
                column: "ProjectId",
                principalTable: "TenantProjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql("UPDATE \"PublicMembers\" SET \"WhatsAppNumber\" = \"Phone\" WHERE \"WhatsAppNumber\" = ''; ");
            migrationBuilder.Sql("""
                INSERT INTO "TenantProjects" ("Id", "TenantId", "Name", "Code", "Description", "SortOrder", "IsActive", "CreatedAt")
                SELECT (md5(t."Id"::text || p.code)::uuid), t."Id", p.name, p.code, 'Migrated from the original phase list.', p.sort, TRUE, NOW()
                FROM "Tenants" t
                CROSS JOIN (VALUES ('JGI','Jinnah Gardens I',1),('JGII','Jinnah Gardens II',2),('KT','Korang Town',3),('KTExt','Korang Town Extension',4)) p(code,name,sort)
                ON CONFLICT DO NOTHING;
                """);
            migrationBuilder.Sql("""
                UPDATE "PublicMembers" member
                SET "ProjectId" = md5(member."TenantId"::text || CASE member."Phase" WHEN 0 THEN 'JGI' WHEN 1 THEN 'JGII' WHEN 2 THEN 'KT' ELSE 'KTExt' END)::uuid;
                """);
            migrationBuilder.Sql("""
                UPDATE "TenantRoles" SET "CanReceiveAssignments" = TRUE
                WHERE "Name" IN ('Society Administrator', 'Manager', 'Supervisor');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PublicMembers_TenantProjects_ProjectId",
                table: "PublicMembers");

            migrationBuilder.DropTable(
                name: "TenantProjects");

            migrationBuilder.DropIndex(
                name: "IX_PublicMembers_ProjectId",
                table: "PublicMembers");

            migrationBuilder.DropColumn(
                name: "BillingNotes",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "MonthlyComplaintLimit",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ResidentLimit",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "StaffLimit",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SubscriptionEndsAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SubscriptionPlan",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SubscriptionStatus",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrialEndsAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CanReceiveAssignments",
                table: "TenantRoles");

            migrationBuilder.DropColumn(
                name: "NotifyByEmail",
                table: "PublicMembers");

            migrationBuilder.DropColumn(
                name: "NotifyByWhatsApp",
                table: "PublicMembers");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "PublicMembers");

            migrationBuilder.DropColumn(
                name: "WhatsAppNumber",
                table: "PublicMembers");
        }
    }
}
