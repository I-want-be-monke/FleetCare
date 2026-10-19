namespace FleetCare.Domain.Entities;

public sealed class Vehicle
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Name { get; private set; }

    public int CurrentOdometerKm { get; private set; }

    public int LastServiceOdometerKm { get; private set; }

    public int ServiceIntervalKm { get; private set; }

    public Vehicle(
        string name,
        int currentOdometerKm,
        int lastServiceOdometerKm,
        int serviceIntervalKm)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Название обязательно.", nameof(name));

        if (currentOdometerKm < 0 || lastServiceOdometerKm < 0)
            throw new ArgumentOutOfRangeException(
                nameof(currentOdometerKm),
                "Пробег не может быть отрицательным.");

        if (currentOdometerKm < lastServiceOdometerKm)
            throw new ArgumentException(
                "Текущий пробег не может быть меньше пробега последнего ТО.");

        if (serviceIntervalKm <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(serviceIntervalKm),
                "Интервал ТО должен быть положительным.");

        Name = name.Trim();
        CurrentOdometerKm = currentOdometerKm;
        LastServiceOdometerKm = lastServiceOdometerKm;
        ServiceIntervalKm = serviceIntervalKm;
    }

    public bool IsServiceDue =>
        CurrentOdometerKm - LastServiceOdometerKm >= ServiceIntervalKm;

    public void UpdateOdometer(int odometerKm)
    {
        if (odometerKm < CurrentOdometerKm)
            throw new ArgumentException(
                "Пробег автомобиля не может уменьшаться.",
                nameof(odometerKm));

        CurrentOdometerKm = odometerKm;
    }

    internal void CompleteScheduledService()
    {
        LastServiceOdometerKm = CurrentOdometerKm;
    }
}