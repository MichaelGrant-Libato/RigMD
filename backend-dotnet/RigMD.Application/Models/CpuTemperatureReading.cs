namespace RigMD.Application.Models;

public sealed record CpuTemperatureReading(
    double Celsius,
    string Source,
    string SensorIdentifier,
    DateTimeOffset CapturedAtUtc);
