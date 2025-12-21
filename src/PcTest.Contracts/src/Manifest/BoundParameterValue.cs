namespace PcTest.Contracts.Manifest;

public record BoundParameterValue(ParameterDefinition Definition, object? Value, bool IsSupplied);
