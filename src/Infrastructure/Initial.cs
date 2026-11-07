using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Plants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ProviderId = table.Column<string>(type: "text", nullable: true),
                    Zone = table.Column<string>(type: "text", nullable: false),
                    Region = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Synthetic = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Rules = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Aggregates",
                columns: table => new
                {
                    PlantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Point = table.Column<string>(type: "text", nullable: false),
                    Start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    End = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Kwh = table.Column<decimal>(type: "numeric", nullable: false),
                    Coverage = table.Column<decimal>(type: "numeric", nullable: false),
                    Revision = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Aggregates", x => new { x.PlantId, x.Point, x.Start, x.Origin });
                    table.ForeignKey(
                        name: "FK_Aggregates_Plants_PlantId",
                        column: x => x.PlantId,
                        principalTable: "Plants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Configurations",
                columns: table => new
                {
                    PlantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Since = table.Column<DateOnly>(type: "date", nullable: false),
                    PvKwp = table.Column<decimal>(type: "numeric", nullable: false),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    Parameters = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configurations", x => new { x.PlantId, x.Since });
                    table.ForeignKey(
                        name: "FK_Configurations_Plants_PlantId",
                        column: x => x.PlantId,
                        principalTable: "Plants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Connections",
                columns: table => new
                {
                    PlantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ProtectedSettings = table.Column<string>(type: "text", nullable: false),
                    Capabilities = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connections", x => x.PlantId);
                    table.ForeignKey(
                        name: "FK_Connections_Plants_PlantId",
                        column: x => x.PlantId,
                        principalTable: "Plants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    PlantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => new { x.PlantId, x.ProviderId });
                    table.ForeignKey(
                        name: "FK_Devices_Plants_PlantId",
                        column: x => x.PlantId,
                        principalTable: "Plants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Imports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    End = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Checkpoint = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAttempt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccess = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Imports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Imports_Plants_PlantId",
                        column: x => x.PlantId,
                        principalTable: "Plants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Prices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    End = table.Column<DateOnly>(type: "date", nullable: true),
                    Price = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prices_Plants_PlantId",
                        column: x => x.PlantId,
                        principalTable: "Plants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Readings",
                columns: table => new
                {
                    PlantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    End = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Pv = table.Column<decimal>(type: "numeric", nullable: true),
                    Load = table.Column<decimal>(type: "numeric", nullable: true),
                    Import = table.Column<decimal>(type: "numeric", nullable: true),
                    Export = table.Column<decimal>(type: "numeric", nullable: true),
                    Charge = table.Column<decimal>(type: "numeric", nullable: true),
                    Discharge = table.Column<decimal>(type: "numeric", nullable: true),
                    Soc = table.Column<decimal>(type: "numeric", nullable: true),
                    OpeningSoc = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Readings", x => new { x.PlantId, x.Start });
                    table.ForeignKey(
                        name: "FK_Readings_Plants_PlantId",
                        column: x => x.PlantId,
                        principalTable: "Plants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Imports_PlantId",
                table: "Imports",
                column: "PlantId");

            migrationBuilder.CreateIndex(
                name: "IX_Plants_Active",
                table: "Plants",
                column: "Active",
                unique: true,
                filter: "\"Active\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_Prices_PlantId_Kind_Start",
                table: "Prices",
                columns: new[] { "PlantId", "Kind", "Start" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Aggregates");

            migrationBuilder.DropTable(
                name: "Configurations");

            migrationBuilder.DropTable(
                name: "Connections");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "Imports");

            migrationBuilder.DropTable(
                name: "Prices");

            migrationBuilder.DropTable(
                name: "Readings");

            migrationBuilder.DropTable(
                name: "Plants");
        }
    }
}
