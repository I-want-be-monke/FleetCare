
using System.Text.Json.Serialization;
using FleetCare.Api.Contracts;
using FleetCare.Domain.Entities;
using FleetCare.Domain.Enums;
using FleetCare.Domain.Services;
using FleetCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddProblemDetails();

// Enum в HTTP будет представлен строкой: "New", "Completed".
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter());
});

// Получаем строку подключения из конфигурации.
var connectionString = builder.Configuration
    .GetConnectionString("FleetCareDb")
    ?? throw new InvalidOperationException(
        "Не задана строка подключения FleetCareDb.");

builder.Services.AddDbContext<FleetCareDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<MaintenanceService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// 1. Создать автомобиль
app.MapPost("/vehicles", async (
    CreateVehicleRequest request,
    FleetCareDbContext db) =>
{
    if (request.Name?.Trim().Length > 200)
    {
        return Results.Problem(
            statusCode: 400,
            title: "Некорректное название автомобиля",
            detail: "Название не должно превышать 200 символов.");
    }
    try
    {
        var vehicle = new Vehicle(
            request.Name,
            request.CurrentOdometerKm,
            request.LastServiceOdometerKm,
            request.ServiceIntervalKm);

        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();

        return Results.Created(
            $"/vehicles/{vehicle.Id}",
            vehicle);
    }
    catch (ArgumentException ex)
    {
        return Results.Problem(
            statusCode: 400,
            title: "Некорректные данные автомобиля",
            detail: ex.Message);
    }
});

// 2. Получить автомобиль
app.MapGet("/vehicles/{id:guid}", async (
    Guid id,
    FleetCareDbContext db) =>
{
    var vehicle = await db.Vehicles
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == id);

    if (vehicle is null)
    {
        return Results.Problem(
            statusCode: 404,
            title: "Автомобиль не найден");
    }

    return Results.Ok(vehicle);
});

// 3. Принять показание телеметрии
app.MapPost("/telemetry", async (
    CreateTelemetryRequest request,
    FleetCareDbContext db) =>
{
    if (request.FaultCode?.Trim().Length > 50)
    {
        return Results.Problem(
            statusCode: 400,
            title: "Некорректный код неисправности",
            detail: "Код неисправности не должен превышать 50 символов.");
    }
    var vehicleExists = await db.Vehicles
        .AnyAsync(x => x.Id == request.VehicleId);

    if (!vehicleExists)
    {
        return Results.Problem(
            statusCode: 404,
            title: "Автомобиль не найден");
    }

    var eventExists = await db.TelemetryReadings
        .AnyAsync(x => x.EventId == request.EventId);

    if (eventExists)
    {
        return Results.Problem(
            statusCode: 409,
            title: "Событие уже существует",
            detail: "Показание с таким eventId уже принято.");
    }

    TelemetryReading reading;

    try
    {
        reading = new TelemetryReading(
            request.EventId,
            request.VehicleId,
            request.OccurredAt,
            request.OdometerKm,
            request.EngineTempC,
            request.FaultCode);
    }
    catch (ArgumentException ex)
    {
        return Results.Problem(
            statusCode: 400,
            title: "Некорректная телеметрия",
            detail: ex.Message);
    }

    db.TelemetryReadings.Add(reading);

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateException ex)
        when (ex.InnerException is PostgresException pg &&
              pg.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        return Results.Problem(
            statusCode: 409,
            title: "Событие уже существует",
            detail: "Показание с таким eventId уже принято.");
    }

    return Results.Created(
        $"/vehicles/{reading.VehicleId}/telemetry",
        reading);
});

// 4. История показаний автомобиля
app.MapGet("/vehicles/{id:guid}/telemetry", async (
    Guid id,
    FleetCareDbContext db) =>
{
    var vehicleExists = await db.Vehicles
        .AnyAsync(x => x.Id == id);

    if (!vehicleExists)
    {
        return Results.Problem(
            statusCode: 404,
            title: "Автомобиль не найден");
    }

    var readings = await db.TelemetryReadings
        .AsNoTracking()
        .Where(x => x.VehicleId == id)
        .OrderByDescending(x => x.OccurredAt)
        .ToListAsync();

    return Results.Ok(readings);
});

// 5. Получить заявки с необязательным фильтром
app.MapGet("/cases", async (
    string? status,
    FleetCareDbContext db) =>
{
    IQueryable<MaintenanceCase> query =
        db.MaintenanceCases.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(status))
    {
        if (!Enum.TryParse<CaseStatus>(
                status, true, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Недопустимый статус заявки");
        }

        query = query.Where(x => x.Status == parsed);
    }

    var cases = await query
        .OrderByDescending(x => x.CreatedAt)
        .ToListAsync();

    return Results.Ok(cases);
});

// 6. Изменить статус заявки
app.MapPatch("/cases/{id:guid}/status", async (
    Guid id,
    UpdateCaseStatusRequest request,
    FleetCareDbContext db,
    MaintenanceService service) =>
{
    var maintenanceCase = await db.MaintenanceCases
        .FirstOrDefaultAsync(x => x.Id == id);

    if (maintenanceCase is null)
    {
        return Results.Problem(
            statusCode: 404,
            title: "Заявка не найдена");
    }

    try
    {
        if (request.Status == CaseStatus.InProgress)
        {
            maintenanceCase.Start();
        }
        else if (request.Status == CaseStatus.Completed)
        {
            var vehicle = await db.Vehicles
                .FirstAsync(
                    x => x.Id == maintenanceCase.VehicleId);

            service.CompleteCase(
                vehicle,
                maintenanceCase,
                DateTimeOffset.UtcNow);
        }
        else
        {
            return Results.Problem(
                statusCode: 400,
                title: "Недопустимый переход статуса",
                detail: "Можно выбрать InProgress или Completed.");
        }

        await db.SaveChangesAsync();

        return Results.Ok(maintenanceCase);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(
            statusCode: 409,
            title: "Невозможно изменить статус",
            detail: ex.Message);
    }
    catch (ArgumentException ex)
    {
        return Results.Problem(
            statusCode: 400,
            title: "Некорректное изменение статуса",
            detail: ex.Message);
    }
});

app.Run();
