using BoilerTelemetry.Application.DTOs;
using FluentValidation;

namespace BoilerTelemetry.Application.Validators;

public class CreateBoilerValidator : AbstractValidator<CreateBoilerDto>
{
    public CreateBoilerValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("field 'name' is required")
            .MaximumLength(200);

        RuleFor(x => x.Location)
            .NotEmpty().WithMessage("field 'location' is required")
            .MaximumLength(500);

        RuleFor(x => x.TemperatureThreshold)
            .GreaterThan(0).WithMessage("field 'temperature_threshold' must be > 0");

        RuleFor(x => x.PressureThreshold)
            .GreaterThan(0).WithMessage("field 'pressure_threshold' must be > 0");
    }
}
