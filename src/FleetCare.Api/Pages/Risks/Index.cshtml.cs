using FleetCare.Domain.Enums;
using FleetCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FleetCare.Api.Pages.Risks;

public sealed class IndexModel(FleetCareDbContext db) : PageModel
{
    public List<RiskRow> Items { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var risks = await db.RiskAlerts.AsNoTracking()
            .OrderBy(x => x.Status)
            .ThenByDescending(x => x.DetectedAt)
            .ToListAsync(ct);
        var names = await db.Vehicles.AsNoTracking()
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        Items = risks.Select(x => new RiskRow(x.RiskId, x.VehicleId,
            names.GetValueOrDefault(x.VehicleId, "Неизвестный автомобиль"),
            x.Kind, x.Score, x.Explanation, x.Status, x.DetectedAt,
            x.ReviewedAt)).ToList();
    }

    public async Task<IActionResult> OnPostReviewAsync(Guid riskId, CancellationToken ct)
    {
        var alert = await db.RiskAlerts.SingleOrDefaultAsync(x => x.RiskId == riskId, ct);
        if (alert is null)
            return NotFound();
        try
        {
            alert.Review(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
            TempData["Notice"] = "Предупреждение проверено.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToPage("/Risks/Index");
    }
}

public sealed record RiskRow(Guid RiskId, Guid VehicleId, string VehicleName,
    string Kind, double Score, string Explanation, RiskAlertStatus Status,
    DateTimeOffset DetectedAt, DateTimeOffset? ReviewedAt);
