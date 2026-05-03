using BoilerTelemetry.Application.DTOs;
using BoilerTelemetry.Application.Validators;

namespace BoilerTelemetry.Tests.Validators;

public class CreateBoilerValidatorTests
{
    private readonly CreateBoilerValidator _validator = new();

    [Fact]
    public void Validate_WhenAllFieldsValid_ShouldPass()
    {
        var dto = new CreateBoilerDto("Boiler-1", "Engine Room", 85, 10);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenNameIsEmpty_ShouldFailWithNameError(string name)
    {
        var dto = new CreateBoilerDto(name, "Room", 85, 10);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_WhenNameExceeds200Characters_ShouldFail()
    {
        var dto = new CreateBoilerDto(new string('A', 201), "Room", 85, 10);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_WhenNameIs200Characters_ShouldPass()
    {
        var dto = new CreateBoilerDto(new string('A', 200), "Room", 85, 10);
        var result = _validator.Validate(dto);
        result.Errors.Should().NotContain(e => e.PropertyName == "Name");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenLocationIsEmpty_ShouldFailWithLocationError(string location)
    {
        var dto = new CreateBoilerDto("Boiler", location, 85, 10);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Location");
    }

    [Fact]
    public void Validate_WhenLocationExceeds500Characters_ShouldFail()
    {
        var dto = new CreateBoilerDto("Boiler", new string('L', 501), 85, 10);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Location");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_WhenTemperatureThresholdIsNotPositive_ShouldFail(double threshold)
    {
        var dto = new CreateBoilerDto("Boiler", "Room", threshold, 10);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TemperatureThreshold");
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(85)]
    [InlineData(200)]
    public void Validate_WhenTemperatureThresholdIsPositive_ShouldNotFailOnThatField(double threshold)
    {
        var dto = new CreateBoilerDto("Boiler", "Room", threshold, 10);
        var result = _validator.Validate(dto);
        result.Errors.Should().NotContain(e => e.PropertyName == "TemperatureThreshold");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(-0.1)]
    public void Validate_WhenPressureThresholdIsNotPositive_ShouldFail(double threshold)
    {
        var dto = new CreateBoilerDto("Boiler", "Room", 85, threshold);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PressureThreshold");
    }

    [Fact]
    public void Validate_WhenMultipleFieldsInvalid_ReturnsAllErrors()
    {
        var dto = new CreateBoilerDto("", "", 0, 0);
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(4);
    }
}
