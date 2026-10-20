using FleetCare.Domain.Services;
using FleetCare.Infrastructure.Persistence;
using FleetCare.Worker;
using FleetCare.Worker.Services;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// PostgreSQL.
// Пароль берём из переменной среды или user-secrets.
string connectionString =
    builder.Configuration.GetConnectionString("FleetCareDb")
    ?? throw new InvalidOperationException(
        "Не задана строка подключения FleetCareDb.");

builder.Services.AddDbContext<FleetCareDbContext>(
    options => options.UseNpgsql(connectionString));

// Существующие бизнес-правила.
builder.Services.AddScoped<MaintenanceService>();

// Обработчик сообщений Kafka.
builder.Services.AddScoped<TelemetryEventProcessor>();

// Фоновый Kafka Consumer.
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

await host.RunAsync();