using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saas.Admin.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionLinkToTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase(
                collation: "SQL_Latin1_General_CP1_CI_AS");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedTime",
                table: "Tenants",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(2026, 6, 9, 13, 45, 49, 167, DateTimeKind.Utc).AddTicks(2061),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValue: new DateTime(2022, 4, 5, 1, 23, 45, 159, DateTimeKind.Utc).AddTicks(9974));

            migrationBuilder.AddColumn<Guid>(
                name: "SubscriptionId",
                table: "Tenants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionStatus",
                table: "Tenants",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_SubscriptionId",
                table: "Tenants",
                column: "SubscriptionId",
                unique: true,
                filter: "[SubscriptionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tenants_SubscriptionId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SubscriptionId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SubscriptionStatus",
                table: "Tenants");

            migrationBuilder.AlterDatabase(
                oldCollation: "SQL_Latin1_General_CP1_CI_AS");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedTime",
                table: "Tenants",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(2022, 4, 5, 1, 23, 45, 159, DateTimeKind.Utc).AddTicks(9974),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValue: new DateTime(2026, 6, 9, 13, 45, 49, 167, DateTimeKind.Utc).AddTicks(2061));
        }
    }
}
