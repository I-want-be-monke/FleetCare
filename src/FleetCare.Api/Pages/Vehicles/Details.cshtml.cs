using System.ComponentModel.DataAnnotations;
using FleetCare.Domain.Entities;
using FleetCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using FleetCare.Infrastructure.Services;

namespace FleetCare.Api.Pages.Vehicles;

public sealed class DetailsModel : PageModel
{
    private readonly FleetCareDbContext _db;
    private readonly MaintenanceService _service;
    private readonly IWebHostEnvironment _environment;
    private readonly TelemetryIngestionService _ingestion;

    public DetailsModel(
        FleetCareDbContext db,
        MaintenanceService service,
        IWebHostEnvironment environment,
        TelemetryIngestionService ingestion)
    {
        _db = db;
        _ingestion = ingestion;
    }

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public TelemetryForm Form { get; set; } = new();

    public Vehicle? Vehicle { get; private set; }

    public List<TelemetryReading> Readings { get; private set; }
        = new();
    public async Task<IActionResult> OnGetAsync(
        CancellationToken ct)
    {
        await LoadAsync(ct);

        return Vehicle is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAddTelemetryAsync(
        CancellationToken ct)
    {
        await LoadAsync(ct);

        if (Vehicle is null)
            return NotFound();

        if (Form.OdometerKm < Vehicle.CurrentOdometerKm)
        {
            ModelState.AddModelError(
                string.Empty,
                "Пробег не может быть меньше текущего пробега машины.");
        }

        if (Form.FaultCode?.Trim().Length > 50)
        {
            ModelState.AddModelError(
                string.Empty,
                "Код неисправности слишком длинный.");
        }

        if (!ModelState.IsValid)
            return Page();

        try
        {
            var reading = new TelemetryReading(
                Guid.NewGuid(),
                Id,
                DateTimeOffset.UtcNow,
                Form.OdometerKm,
                Form.EngineTempC,
                Form.FaultCode);

            await _ingestion.SaveAsync(reading, ct);

            TempData["Notice"] = "Показание сохранено.";

            return RedirectToPage(
                "/Vehicles/Details",
                new { id = Id });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return Page();
        }
    }

    // ВРЕМЕННЫЙ обработчик для демонстрации.

    private async Task LoadAsync(CancellationToken ct)
    {
        Vehicle = await _db.Vehicles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == Id, ct);

        if (Vehicle is null)
            return;

        var recent = await _db.TelemetryReadings
            .AsNoTracking()
            .Where(x => x.VehicleId == Id)
            .OrderByDescending(x => x.OccurredAt)
            .Take(50)
            .ToListAsync(ct);

        Readings = recent
            .OrderBy(x => x.OccurredAt)
            .ToList();
    }

    // Подготавливаем координаты точек графика SVG.
    public string ChartPoints
    {
        get
        {
            if (Readings.Count < 2)
                return string.Empty;

            double min = Readings.Min(x => x.EngineTempC);
            double max = Readings.Max(x => x.EngineTempC);

            double range = Math.Max(1.0, max - min);

            return string.Join(" ",
                Readings.Select((reading, index) =>
                {
                    double x = 30 + index * 740.0
                        / (Readings.Count - 1);

                    double y = 190
                        - (reading.EngineTempC - min)
                        * 150.0 / range;

                    return FormattableString.Invariant(
                        $"{x:F1},{y:F1}");
                }));
        }
    }
}

public sealed class TelemetryForm
{
    [Range(0, int.MaxValue)]
    public int OdometerKm { get; set; }

    [Range(-100.0, 250.0)]
    public double EngineTempC { get; set; }

    public string? FaultCode { get; set; }
}