using System.ComponentModel.DataAnnotations;
using FleetCare.Domain.Entities;
using FleetCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FleetCare.Api.Pages;

public sealed class IndexModel : PageModel
{
    private readonly FleetCareDbContext _db;

    public IndexModel(FleetCareDbContext db)
    {
        _db = db;
    }

    public List<Vehicle> Vehicles { get; private set; } = new();

    [BindProperty]
    public CreateVehicleForm Form { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadVehiclesAsync(ct);
    }

    public async Task<IActionResult> OnPostCreateAsync(
        CancellationToken ct)
    {
        if (Form.CurrentOdometerKm <
            Form.LastServiceOdometerKm)
        {
            ModelState.AddModelError(
                string.Empty,
                "Текущий пробег не может быть меньше пробега последнего ТО.");
        }

        if (!ModelState.IsValid)
        {
            await LoadVehiclesAsync(ct);
            return Page();
        }

        try
        {
            var vehicle = new Vehicle(
                Form.Name,
                Form.CurrentOdometerKm,
                Form.LastServiceOdometerKm,
                Form.ServiceIntervalKm);

            _db.Vehicles.Add(vehicle);

            await _db.SaveChangesAsync(ct);

            return RedirectToPage(
                "/Vehicles/Details",
                new { id = vehicle.Id });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            await LoadVehiclesAsync(ct);
            return Page();
        }
    }

    private async Task LoadVehiclesAsync(
        CancellationToken ct)
    {
        Vehicles = await _db.Vehicles
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
    }
}

public sealed class CreateVehicleForm
{
    [Required(ErrorMessage = "Введите название.")]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [Range(0, int.MaxValue)]
    public int CurrentOdometerKm { get; set; }

    [Range(0, int.MaxValue)]
    public int LastServiceOdometerKm { get; set; }

    [Range(1, int.MaxValue)]
    public int ServiceIntervalKm { get; set; }
}