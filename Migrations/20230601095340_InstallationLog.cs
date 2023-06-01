using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace pvinstallations_floxder.Migrations
{
    /// <inheritdoc />
    public partial class InstallationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InstallationLogs",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreviousValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NextValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PvInstallationID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallationLogs", x => x.ID);
                    table.ForeignKey(
                        name: "FK_InstallationLogs_PvInstallations_PvInstallationID",
                        column: x => x.PvInstallationID,
                        principalTable: "PvInstallations",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstallationLogs_PvInstallationID",
                table: "InstallationLogs",
                column: "PvInstallationID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstallationLogs");
        }
    }
}
