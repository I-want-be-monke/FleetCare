using FleetCare.Domain.Enums;
using FleetCare.Domain.Services;
using FleetCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FleetCare.Api.Pages.Cases;

public sealed class IndexModel : PageModel
{
    private readonly FleetCareDbContext _db;
    private readonly MaintenanceService _service;

    public IndexModel(
        FleetCareDbContext db,
        MaintenanceService service)
    {
        _db = db;
        _service = service;
    }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public List<CaseRow> Items { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var query = _db.MaintenanceCases
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(Status))
        {
            if (Enum.TryParse<CaseStatus>(
                    Status, true, out var parsed)
                && Enum.IsDefined(parsed))
            {
                query = query.Where(x => x.Status == parsed);
            }
            else
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Недопустимый фильтр по статусу.");
            }
        }

        var cases = await query
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        var vehicleNames = await _db.Vehicles
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        Items = cases.Select(x => new CaseRow(
            x.Id,
            x.VehicleId,
            vehicleNames.GetValueOrDefault(
                x.VehicleId, "Неизвестный автомобиль"),
            x.Reason,
            x.FaultCode,
            x.Status,
            x.CreatedAt
        )).ToList();
    }

    public async Task<IActionResult> OnPostChangeAsync(
        Guid caseId,
        CaseStatus newStatus,
        CancellationToken ct)
    {
        var maintenanceCase = await _db.MaintenanceCases
            .FirstOrDefaultAsync(x => x.Id == caseId, ct);

        if (maintenanceCase is null)
            return NotFound();

        try
        {
            if (newStatus == CaseStatus.InProgress)
            {
                maintenanceCase.Start();
            }
            else if (newStatus == CaseStatus.Completed)
            {
                var vehicle = await _db.Vehicles
                    .FirstAsync(
                        x => x.Id == maintenanceCase.VehicleId,
                        ct);

                _service.CompleteCase(
                    vehicle,
                    maintenanceCase,
                    DateTimeOffset.UtcNow);
            }
            else
            {
                return BadRequest("Недопустимый статус.");
            }

            await _db.SaveChangesAsync(ct);

            TempData["Notice"] = "Статус заявки изменён.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (ArgumentException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToPage("/Cases/Index");
    }
}

public sealed record CaseRow(
    Guid Id,
    Guid VehicleId,
    string VehicleName,
    MaintenanceReason Reason,
    string? FaultCode,
    CaseStatus Status,
    DateTimeOffset CreatedAt
);