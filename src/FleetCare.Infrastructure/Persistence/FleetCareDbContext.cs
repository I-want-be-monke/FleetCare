
using FleetCare.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FleetCare.Infrastructure.Persistence;

public sealed class FleetCareDbContext : DbContext
{
    public FleetCareDbContext(
        DbContextOptions<FleetCareDbContext> options)
        : base(options)
    {
    }

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<TelemetryReading> TelemetryReadings => Set<TelemetryReading>();
    public DbSet<MaintenanceCase> MaintenanceCases => Set<MaintenanceCase>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_Vehicle_Odometer",
                    "\"CurrentOdometerKm\" >= \"LastServiceOdometerKm\" " +
                    "AND \"LastServiceOdometerKm\" >= 0");

                table.HasCheckConstraint(
                    "CK_Vehicle_ServiceInterval",
                    "\"ServiceIntervalKm\" > 0");
            });
        });

        modelBuilder.Entity<TelemetryReading>(entity =>
        {
            // EventId одновременно является первичным ключом.
            entity.HasKey(x => x.EventId);

            entity.Property(x => x.FaultCode)
                .HasMaxLength(50);

            entity.HasOne<Vehicle>()
                .WithMany()
                .HasForeignKey(x => x.VehicleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new
            {
                x.VehicleId,
                x.OccurredAt
            });

            entity.ToTable(table =>
                table.HasCheckConstraint(
                    "CK_Telemetry_Odometer",
                    "\"OdometerKm\" >= 0"));
        });

        modelBuilder.Entity<MaintenanceCase>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.FaultCode)
                .HasMaxLength(50);

            entity.HasOne<Vehicle>()
                .WithMany()
                .HasForeignKey(x => x.VehicleId)
                .OnDelete(DeleteBehavior.Restrict);

            // Одно открытое плановое ТО для машины.
            entity.HasIndex(x => x.VehicleId)
                .IsUnique()
                .HasDatabaseName("UX_Cases_OpenScheduled")
                .HasFilter("\"Status\" <> 2 AND \"Reason\" = 0");

            // Одна открытая заявка на конкретный код ошибки.
            entity.HasIndex(x => new
            {
                x.VehicleId,
                x.FaultCode
            })
                .IsUnique()
                .HasDatabaseName("UX_Cases_OpenFault")
                .HasFilter("\"Status\" <> 2 AND \"Reason\" = 1");

            entity.ToTable(table =>
                table.HasCheckConstraint(
                    "CK_Case_ReasonFault",
                    "(\"Reason\" = 0 AND \"FaultCode\" IS NULL) " +
                    "OR (\"Reason\" = 1 AND \"FaultCode\" IS NOT NULL)"));
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.EventType)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(x => x.PayloadJson)
                .IsRequired();
        });

        modelBuilder.Entity<ProcessedMessage>(entity =>
        {
            // У одного сообщения могут быть разные обработчики.
            entity.HasKey(x => new
            {
                x.MessageId,
                x.HandlerName
            });

            entity.Property(x => x.HandlerName)
                .IsRequired()
                .HasMaxLength(150);
        });
    }
}
