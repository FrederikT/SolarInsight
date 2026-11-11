using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AutomaticDeviceCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CounterAttempts",
                table: "HistoryScans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CounterData",
                table: "HistoryScans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CounterDay",
                table: "HistoryScans",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CounterDue",
                table: "HistoryScans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CounterProfile",
                table: "HistoryScans",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CounterUntil",
                table: "HistoryScans",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CounterAttempts",
                table: "HistoryScans");

            migrationBuilder.DropColumn(
                name: "CounterData",
                table: "HistoryScans");

            migrationBuilder.DropColumn(
                name: "CounterDay",
                table: "HistoryScans");

            migrationBuilder.DropColumn(
                name: "CounterDue",
                table: "HistoryScans");

            migrationBuilder.DropColumn(
                name: "CounterProfile",
                table: "HistoryScans");

            migrationBuilder.DropColumn(
                name: "CounterUntil",
                table: "HistoryScans");
        }
    }
}
