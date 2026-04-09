using BoilerTelemetry.Application.DTOs;
using FluentValidation;

namespace BoilerTelemetry.Application.Validators;

public class TelemetryRequestValidator : AbstractValidator<TelemetryRequestDto>
{
    public TelemetryRequestValidator()
    {
        RuleFor(x => x.BoilerId)
            .NotEmpty().WithMessage("field 'boiler_id' is required");

        RuleFor(x => x.Temperature)
            .InclusiveBetween(-50, 200).WithMessage("field 'temperature' must be between -50 and 200");

        RuleFor(x => x.Pressure)
            .GreaterThanOrEqualTo(0).WithMessage("field 'pressure' must be >= 0")
            .LessThanOrEqualTo(50).WithMessage("field 'pressure' must be <= 50");

        RuleFor(x => x.Timestamp)
            .NotEmpty().WithMessage("field 'timestamp' is required")
            .LessThanOrEqualTo(DateTime.UtcNow.AddMinutes(5)).WithMessage("field 'timestamp' cannot be in the future");
    }
}
