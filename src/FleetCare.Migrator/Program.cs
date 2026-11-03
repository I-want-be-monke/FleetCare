using FleetCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__FleetCareDb");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings__FleetCareDb is required.");

var options = new DbContextOptionsBuilder<FleetCareDbContext>()
    .UseNpgsql(connectionString).Options;
await using var db = new FleetCareDbContext(options);
Console.WriteLine("Applying FleetCare migrations...");
await db.Database.MigrateAsync();

// PostgreSQL init creates this dedicated role; grant only telemetry SELECT.
await db.Database.ExecuteSqlRawAsync("GRANT CONNECT ON DATABASE fleetcare TO fleetcare_analytics_worker");
await db.Database.ExecuteSqlRawAsync("GRANT USAGE ON SCHEMA public TO fleetcare_analytics_worker");
await db.Database.ExecuteSqlRawAsync("GRANT SELECT ON TABLE public.\"TelemetryReadings\" TO fleetcare_analytics_worker");
Console.WriteLine("Migrations and analytics grants completed.");
