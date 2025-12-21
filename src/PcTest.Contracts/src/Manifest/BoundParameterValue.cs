namespace PcTest.Contracts.Manifest;

/// <summary>
/// Represents a parameter value that has been bound against a manifest definition.
/// </summary>
/// <param name="Definition">The parameter definition describing the expected type and metadata.</param>
/// <param name="Value">The normalized value after conversion.</param>
/// <param name="IsSupplied">Indicates whether the value came from user input or a default.</param>
public record BoundParameterValue(ParameterDefinition Definition, object? Value, bool IsSupplied);
