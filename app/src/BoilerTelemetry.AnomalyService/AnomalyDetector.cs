using BoilerTelemetry.Domain.Entities;

namespace BoilerTelemetry.AnomalyService;

public static class AnomalyDetector
{
    public static List<AnomalyEvent> DetectAnomalies(TelemetryReading reading, Boiler boiler)
    {
        var anomalies = new List<AnomalyEvent>();

        if (reading.Temperature > boiler.TemperatureThreshold)
        {
            anomalies.Add(new AnomalyEvent
            {
                BoilerId = reading.BoilerId,
                AnomalyType = "temperature_exceeded",
                ActualValue = reading.Temperature,
                Threshold = boiler.TemperatureThreshold,
                DetectedAt = DateTime.UtcNow
            });
        }

        if (reading.Pressure > boiler.PressureThreshold)
        {
            anomalies.Add(new AnomalyEvent
            {
                BoilerId = reading.BoilerId,
                AnomalyType = "pressure_exceeded",
                ActualValue = reading.Pressure,
                Threshold = boiler.PressureThreshold,
                DetectedAt = DateTime.UtcNow
            });
        }

        return anomalies;
    }
}
