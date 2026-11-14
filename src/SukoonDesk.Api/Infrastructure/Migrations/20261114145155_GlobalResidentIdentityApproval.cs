using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SukoonDesk.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlobalResidentIdentityApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PublicMembers_TenantId_Cnic",
                table: "PublicMembers");

            migrationBuilder.CreateIndex(
                name: "IX_PublicMembers_Cnic",
                table: "PublicMembers",
                column: "Cnic",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicMembers_Phone",
                table: "PublicMembers",
                column: "Phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicMembers_TenantId",
                table: "PublicMembers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PublicMembers_WhatsAppNumber",
                table: "PublicMembers",
                column: "WhatsAppNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PublicMembers_Cnic",
                table: "PublicMembers");

            migrationBuilder.DropIndex(
                name: "IX_PublicMembers_Phone",
                table: "PublicMembers");

            migrationBuilder.DropIndex(
                name: "IX_PublicMembers_TenantId",
                table: "PublicMembers");

            migrationBuilder.DropIndex(
                name: "IX_PublicMembers_WhatsAppNumber",
                table: "PublicMembers");

            migrationBuilder.CreateIndex(
                name: "IX_PublicMembers_TenantId_Cnic",
                table: "PublicMembers",
                columns: new[] { "TenantId", "Cnic" },
                unique: true);
        }
    }
}
