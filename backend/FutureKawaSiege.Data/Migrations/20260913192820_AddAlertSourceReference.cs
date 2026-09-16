using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FutureKawaSiege.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertSourceReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceAlertId",
                table: "Alerts",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceAlertId",
                table: "Alerts");
        }
    }
}
