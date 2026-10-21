using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RiskAlerts",
                columns: table => new
                {
                    RiskId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskAlerts", x => x.RiskId);
                    table.CheckConstraint("CK_RiskAlert_Score", "\"Score\" >= 0 AND \"Score\" <= 1");
                    table.ForeignKey(
                        name: "FK_RiskAlerts_TelemetryReadings_SourceEventId",
                        column: x => x.SourceEventId,
                        principalTable: "TelemetryReadings",
                        principalColumn: "EventId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiskAlerts_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RiskAlerts_SourceEventId",
                table: "RiskAlerts",
                column: "SourceEventId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAlerts_VehicleId_DetectedAt",
                table: "RiskAlerts",
                columns: new[] { "VehicleId", "DetectedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RiskAlerts");
        }
    }
}
