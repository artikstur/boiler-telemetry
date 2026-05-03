using BoilerTelemetry.Application.DTOs;
using BoilerTelemetry.Application.Validators;

namespace BoilerTelemetry.Tests.Validators;

public class TelemetryRequestValidatorTests
{
    private readonly TelemetryRequestValidator _validator = new();

    [Fact]
    public void Validate_WhenAllFieldsValid_ShouldPass()
    {
        var dto = new TelemetryRequestDto(Guid.NewGuid(), 75.0, 8.0, DateTime.UtcNow.AddMinutes(-1));
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenBoilerIdIsEmpty_ShouldFail()
    {
        var dto = new TelemetryRequestDto(Guid.Empty, 75.0, 8.0, DateTime.UtcNow);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "BoilerId");
    }

    [Theory]
    [InlineData(-51)]
    [InlineData(201)]
    [InlineData(500)]
    public void Validate_WhenTemperatureOutOfRange_ShouldFail(double temperature)
    {
        var dto = new TelemetryRequestDto(Guid.NewGuid(), temperature, 8.0, DateTime.UtcNow);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Temperature");
    }

    [Theory]
    [InlineData(-50)]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(200)]
    public void Validate_WhenTemperatureAtBoundary_ShouldPass(double temperature)
    {
        var dto = new TelemetryRequestDto(Guid.NewGuid(), temperature, 8.0, DateTime.UtcNow.AddMinutes(-1));
        var result = _validator.Validate(dto);
        result.Errors.Should().NotContain(e => e.PropertyName == "Temperature");
    }

    [Fact]
    public void Validate_WhenPressureIsNegative_ShouldFail()
    {
        var dto = new TelemetryRequestDto(Guid.NewGuid(), 75.0, -0.1, DateTime.UtcNow);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Pressure");
    }

    [Fact]
    public void Validate_WhenPressureExceeds50_ShouldFail()
    {
        var dto = new TelemetryRequestDto(Guid.NewGuid(), 75.0, 50.1, DateTime.UtcNow);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Pressure");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(50)]
    public void Validate_WhenPressureAtBoundary_ShouldPass(double pressure)
    {
        var dto = new TelemetryRequestDto(Guid.NewGuid(), 75.0, pressure, DateTime.UtcNow.AddMinutes(-1));
        var result = _validator.Validate(dto);
        result.Errors.Should().NotContain(e => e.PropertyName == "Pressure");
    }

    [Fact]
    public void Validate_WhenTimestampIsFarInFuture_ShouldFail()
    {
        var dto = new TelemetryRequestDto(Guid.NewGuid(), 75.0, 8.0, DateTime.UtcNow.AddMinutes(10));
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Timestamp");
    }

    [Fact]
    public void Validate_WhenTimestampIsInThePast_ShouldPass()
    {
        var dto = new TelemetryRequestDto(Guid.NewGuid(), 75.0, 8.0, DateTime.UtcNow.AddHours(-1));
        var result = _validator.Validate(dto);
        result.Errors.Should().NotContain(e => e.PropertyName == "Timestamp");
    }

    [Fact]
    public void Validate_WhenTimestampIsDefault_ShouldFail()
    {
        var dto = new TelemetryRequestDto(Guid.NewGuid(), 75.0, 8.0, default);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Timestamp");
    }
}
