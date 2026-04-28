using FluentValidation;
using Microsoft.Extensions.Options;

namespace Seren.Modules.VoxMind.Configuration;

/// <summary>
/// Adapter that bridges <see cref="VoxMindOptionsValidator"/> (FluentValidation)
/// to the .NET options pipeline (<see cref="IValidateOptions{TOptions}"/>) so
/// <c>.ValidateOnStart()</c> in <c>Program.cs</c> fails the host fast on
/// invalid VoxMind configuration instead of surfacing only at first use.
/// </summary>
public sealed class FluentValidationVoxMindOptionsValidator : IValidateOptions<VoxMindOptions>
{
    private readonly IValidator<VoxMindOptions> _validator;

    public FluentValidationVoxMindOptionsValidator(IValidator<VoxMindOptions> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        _validator = validator;
    }

    public ValidateOptionsResult Validate(string? name, VoxMindOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var result = _validator.Validate(options);
        if (result.IsValid)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = result.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}");
        return ValidateOptionsResult.Fail(failures);
    }
}
