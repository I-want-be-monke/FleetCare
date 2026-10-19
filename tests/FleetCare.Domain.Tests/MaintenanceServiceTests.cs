
using FleetCare.Domain.Entities;
using FleetCare.Domain.Enums;
using FleetCare.Domain.Services;

namespace FleetCare.Domain.Tests;

public sealed class MaintenanceServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly MaintenanceService _service = new();

    private static Vehicle CreateVehicle()
    {
        return new Vehicle(
            name: "Volvo FH",
            currentOdometerKm: 24_000,
            lastServiceOdometerKm: 10_000,
            serviceIntervalKm: 15_000);
    }

    private static TelemetryReading CreateReading(
        Vehicle vehicle,
        int odometerKm,
        string? faultCode = null,
        Guid? eventId = null)
    {
        return new TelemetryReading(
            eventId ?? Guid.NewGuid(),
            vehicle.Id,
            Now,
            odometerKm,
            90.0,
            faultCode);
    }

    [Fact]
    public void MileageReachesInterval_CreatesScheduledCase()
    {
        var vehicle = CreateVehicle();
        var reading = CreateReading(vehicle, 25_000);

        var result = _service.Evaluate(
            vehicle,
            reading,
            Array.Empty<MaintenanceCase>(),
            Now);

        var maintenanceCase = Assert.Single(result);

        Assert.Equal(
            MaintenanceReason.ScheduledService,
            maintenanceCase.Reason);

        Assert.Equal(CaseStatus.New, maintenanceCase.Status);
        Assert.Equal(25_000, vehicle.CurrentOdometerKm);
        Assert.Equal(reading.EventId, maintenanceCase.SourceEventId);
    }

    [Fact]
    public void MileageBelowInterval_DoesNotCreateCase()
    {
        var vehicle = CreateVehicle();
        var reading = CreateReading(vehicle, 24_999);

        var result = _service.Evaluate(
            vehicle,
            reading,
            Array.Empty<MaintenanceCase>(),
            Now);

        Assert.Empty(result);
        Assert.False(vehicle.IsServiceDue);
    }

    [Fact]
    public void FaultCode_CreatesFaultCase()
    {
        var vehicle = CreateVehicle();
        var reading = CreateReading(vehicle, 24_100, "P0420");

        var result = _service.Evaluate(
            vehicle,
            reading,
            Array.Empty<MaintenanceCase>(),
            Now);

        var maintenanceCase = Assert.Single(result);

        Assert.Equal(
            MaintenanceReason.FaultCode,
            maintenanceCase.Reason);

        Assert.Equal("P0420", maintenanceCase.FaultCode);
    }

    [Fact]
    public void ServiceDueAndFaultCode_CreateTwoDifferentCases()
    {
        var vehicle = CreateVehicle();
        var reading = CreateReading(vehicle, 25_000, "P0420");

        var result = _service.Evaluate(
            vehicle,
            reading,
            Array.Empty<MaintenanceCase>(),
            Now);

        Assert.Equal(2, result.Count);

        Assert.Contains(
            result,
            c => c.Reason == MaintenanceReason.ScheduledService);

        Assert.Contains(
            result,
            c => c.Reason == MaintenanceReason.FaultCode &&
                 c.FaultCode == "P0420");
    }

    [Fact]
    public void RepeatedEvent_DoesNotCreateDuplicateCase()
    {
        var vehicle = CreateVehicle();
        var reading = CreateReading(vehicle, 25_000);
        var existingCases = new List<MaintenanceCase>();

        var firstResult = _service.Evaluate(
            vehicle,
            reading,
            existingCases,
            Now);

        existingCases.AddRange(firstResult);

        var secondResult = _service.Evaluate(
            vehicle,
            reading,
            existingCases,
            Now.AddSeconds(1));

        Assert.Single(existingCases);
        Assert.Empty(secondResult);
    }

    [Fact]
    public void OpenCaseWithSameReason_PreventsAnotherCase()
    {
        var vehicle = CreateVehicle();
        var existingCases = new List<MaintenanceCase>();

        var firstReading = CreateReading(vehicle, 25_000);

        existingCases.AddRange(
            _service.Evaluate(vehicle, firstReading, existingCases, Now));

        var secondReading = CreateReading(vehicle, 25_100);

        var result = _service.Evaluate(
            vehicle,
            secondReading,
            existingCases,
            Now.AddMinutes(1));

        Assert.Empty(result);
        Assert.Single(existingCases);
    }

    [Fact]
    public void DifferentFaultCodes_CreateSeparateCases()
    {
        var vehicle = CreateVehicle();
        var existingCases = new List<MaintenanceCase>();

        var firstReading = CreateReading(vehicle, 24_100, "P0420");

        existingCases.AddRange(
            _service.Evaluate(vehicle, firstReading, existingCases, Now));

        var secondReading = CreateReading(vehicle, 24_200, "P0300");

        var result = _service.Evaluate(
            vehicle,
            secondReading,
            existingCases,
            Now.AddMinutes(1));

        var maintenanceCase = Assert.Single(result);

        Assert.Equal("P0300", maintenanceCase.FaultCode);
    }

    [Fact]
    public void CompletingScheduledService_UpdatesLastServiceMileage()
    {
        var vehicle = CreateVehicle();
        var reading = CreateReading(vehicle, 25_000);

        var maintenanceCase = Assert.Single(
            _service.Evaluate(
                vehicle,
                reading,
                Array.Empty<MaintenanceCase>(),
                Now));

        _service.CompleteCase(
            vehicle,
            maintenanceCase,
            Now.AddMinutes(10));

        Assert.Equal(CaseStatus.Completed, maintenanceCase.Status);
        Assert.Equal(25_000, vehicle.LastServiceOdometerKm);

        var nextReading = CreateReading(vehicle, 25_100);

        var nextResult = _service.Evaluate(
            vehicle,
            nextReading,
            new[] { maintenanceCase },
            Now.AddMinutes(11));

        Assert.Empty(nextResult);
    }

    [Fact]
    public void CompletingFaultCase_DoesNotChangeLastServiceMileage()
    {
        var vehicle = CreateVehicle();
        var reading = CreateReading(vehicle, 24_100, "P0420");

        var maintenanceCase = Assert.Single(
            _service.Evaluate(
                vehicle,
                reading,
                Array.Empty<MaintenanceCase>(),
                Now));

        _service.CompleteCase(
            vehicle,
            maintenanceCase,
            Now.AddMinutes(5));

        Assert.Equal(CaseStatus.Completed, maintenanceCase.Status);
        Assert.Equal(10_000, vehicle.LastServiceOdometerKm);
    }

    [Fact]
    public void CompletedFaultCase_ReplayedEventDoesNotCreateDuplicate()
    {
        var vehicle = CreateVehicle();
        var reading = CreateReading(vehicle, 24_100, "P0420");

        var maintenanceCase = Assert.Single(
            _service.Evaluate(
                vehicle,
                reading,
                Array.Empty<MaintenanceCase>(),
                Now));

        _service.CompleteCase(
            vehicle,
            maintenanceCase,
            Now.AddMinutes(1));

        var result = _service.Evaluate(
            vehicle,
            reading,
            new[] { maintenanceCase },
            Now.AddMinutes(2));

        Assert.Empty(result);
    }

    [Fact]
    public void OdometerCannotDecrease()
    {
        var vehicle = CreateVehicle();

        Assert.Throws<ArgumentException>(
            () => vehicle.UpdateOdometer(23_999));
    }

    [Fact]
    public void CaseCanMoveToInProgressAndCompleted()
    {
        var vehicle = CreateVehicle();
        var reading = CreateReading(vehicle, 25_000);

        var maintenanceCase = Assert.Single(
            _service.Evaluate(
                vehicle,
                reading,
                Array.Empty<MaintenanceCase>(),
                Now));

        maintenanceCase.Start();

        Assert.Equal(CaseStatus.InProgress, maintenanceCase.Status);
        Assert.True(maintenanceCase.IsOpen);

        _service.CompleteCase(
            vehicle,
            maintenanceCase,
            Now.AddMinutes(5));

        Assert.Equal(CaseStatus.Completed, maintenanceCase.Status);
        Assert.False(maintenanceCase.IsOpen);
        Assert.Equal(Now.AddMinutes(5), maintenanceCase.CompletedAt);
    }
}
