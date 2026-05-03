using BoilerTelemetry.AnomalyService;
using BoilerTelemetry.Domain.Entities;

namespace BoilerTelemetry.Tests.AnomalyService;

public class AnomalyDetectorTests
{
    private static Boiler MakeBoiler(double tempThreshold = 85, double pressThreshold = 10) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test",
        Location = "Room",
        TemperatureThreshold = tempThreshold,
        PressureThreshold = pressThreshold
    };

    private static TelemetryReading MakeReading(double temperature, double pressure, Guid? boilerId = null) => new()
    {
        BoilerId = boilerId ?? Guid.NewGuid(),
        Temperature = temperature,
        Pressure = pressure,
        Timestamp = DateTime.UtcNow
    };

    [Fact]
    public void DetectAnomalies_WhenBothValuesWithinThreshold_ReturnsEmpty()
    {
        var boiler = MakeBoiler(tempThreshold: 85, pressThreshold: 10);
        var reading = MakeReading(temperature: 75, pressure: 8);

        var result = AnomalyDetector.DetectAnomalies(reading, boiler);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectAnomalies_WhenTemperatureExceedsThreshold_ReturnsTemperatureAnomaly()
    {
        var boiler = MakeBoiler(tempThreshold: 85, pressThreshold: 10);
        var reading = MakeReading(temperature: 95, pressure: 8);

        var result = AnomalyDetector.DetectAnomalies(reading, boiler);

        result.Should().HaveCount(1);
        result[0].AnomalyType.Should().Be("temperature_exceeded");
        result[0].ActualValue.Should().Be(95);
        result[0].Threshold.Should().Be(85);
    }

    [Fact]
    public void DetectAnomalies_WhenPressureExceedsThreshold_ReturnsPressureAnomaly()
    {
        var boiler = MakeBoiler(tempThreshold: 85, pressThreshold: 10);
        var reading = MakeReading(temperature: 75, pressure: 12);

        var result = AnomalyDetector.DetectAnomalies(reading, boiler);

        result.Should().HaveCount(1);
        result[0].AnomalyType.Should().Be("pressure_exceeded");
        result[0].ActualValue.Should().Be(12);
        result[0].Threshold.Should().Be(10);
    }

    [Fact]
    public void DetectAnomalies_WhenBothExceedThreshold_ReturnsTwoAnomalies()
    {
        var boiler = MakeBoiler(tempThreshold: 85, pressThreshold: 10);
        var reading = MakeReading(temperature: 95, pressure: 12);

        var result = AnomalyDetector.DetectAnomalies(reading, boiler);

        result.Should().HaveCount(2);
        result.Select(a => a.AnomalyType).Should().BeEquivalentTo("temperature_exceeded", "pressure_exceeded");
    }

    [Fact]
    public void DetectAnomalies_WhenValueEqualsThreshold_DoesNotTriggerAnomaly()
    {
        // Граничный случай: ровно на пороге — не аномалия (строго больше >)
        var boiler = MakeBoiler(tempThreshold: 85, pressThreshold: 10);
        var reading = MakeReading(temperature: 85, pressure: 10);

        var result = AnomalyDetector.DetectAnomalies(reading, boiler);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectAnomalies_AnomalyEventContainsCorrectBoilerId()
    {
        var boiler = MakeBoiler();
        var reading = MakeReading(temperature: 95, pressure: 8, boilerId: boiler.Id);

        var result = AnomalyDetector.DetectAnomalies(reading, boiler);

        result[0].BoilerId.Should().Be(boiler.Id);
    }

    [Fact]
    public void DetectAnomalies_AnomalyEventHasDetectedAtSet()
    {
        var before = DateTime.UtcNow;
        var boiler = MakeBoiler();
        var reading = MakeReading(temperature: 95, pressure: 8);

        var result = AnomalyDetector.DetectAnomalies(reading, boiler);

        result[0].DetectedAt.Should().BeOnOrAfter(before);
    }

    [Theory]
    [InlineData(85.1, 8, 1)]   // только температура
    [InlineData(75, 10.1, 1)]  // только давление
    [InlineData(85.1, 10.1, 2)] // оба параметра
    [InlineData(84.9, 9.9, 0)] // ничего
    public void DetectAnomalies_VariousThresholdCombinations_ReturnsExpectedCount(
        double temperature, double pressure, int expectedCount)
    {
        var boiler = MakeBoiler(tempThreshold: 85, pressThreshold: 10);
        var reading = MakeReading(temperature, pressure);

        var result = AnomalyDetector.DetectAnomalies(reading, boiler);

        result.Should().HaveCount(expectedCount);
    }
}
