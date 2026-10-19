using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedMessages",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    HandlerName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedMessages", x => new { x.MessageId, x.HandlerName });
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CurrentOdometerKm = table.Column<int>(type: "integer", nullable: false),
                    LastServiceOdometerKm = table.Column<int>(type: "integer", nullable: false),
                    ServiceIntervalKm = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                    table.CheckConstraint("CK_Vehicle_Odometer", "\"CurrentOdometerKm\" >= \"LastServiceOdometerKm\" AND \"LastServiceOdometerKm\" >= 0");
                    table.CheckConstraint("CK_Vehicle_ServiceInterval", "\"ServiceIntervalKm\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    FaultCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceCases", x => x.Id);
                    table.CheckConstraint("CK_Case_ReasonFault", "(\"Reason\" = 0 AND \"FaultCode\" IS NULL) OR (\"Reason\" = 1 AND \"FaultCode\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_MaintenanceCases_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryReadings",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OdometerKm = table.Column<int>(type: "integer", nullable: false),
                    EngineTempC = table.Column<double>(type: "double precision", nullable: false),
                    FaultCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryReadings", x => x.EventId);
                    table.CheckConstraint("CK_Telemetry_Odometer", "\"OdometerKm\" >= 0");
                    table.ForeignKey(
                        name: "FK_TelemetryReadings_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Cases_OpenFault",
                table: "MaintenanceCases",
                columns: new[] { "VehicleId", "FaultCode" },
                unique: true,
                filter: "\"Status\" <> 2 AND \"Reason\" = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Cases_OpenScheduled",
                table: "MaintenanceCases",
                column: "VehicleId",
                unique: true,
                filter: "\"Status\" <> 2 AND \"Reason\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryReadings_VehicleId_OccurredAt",
                table: "TelemetryReadings",
                columns: new[] { "VehicleId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaintenanceCases");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "ProcessedMessages");

            migrationBuilder.DropTable(
                name: "TelemetryReadings");

            migrationBuilder.DropTable(
                name: "Vehicles");
        }
    }
}
