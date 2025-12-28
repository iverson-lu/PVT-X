namespace PcTest.Contracts;

/// <summary>
/// Defines error codes for validation and execution errors per spec.
/// </summary>
public static class ErrorCodes
{
    // Discovery errors
    public const string DuplicateIdentity = "Discovery.DuplicateIdentity";
    
    // Suite ref resolution errors (section 5.2)
    public const string SuiteTestCaseRefInvalid = "Suite.TestCaseRef.Invalid";
    
    // Plan resolution errors
    public const string PlanSuiteRefNotFound = "Plan.SuiteRef.NotFound";
    public const string PlanSuiteRefNonUnique = "Plan.SuiteRef.NonUnique";
    
    // RunRequest errors
    public const string RunRequestIdentityNotFound = "RunRequest.Identity.NotFound";
    public const string RunRequestIdentityNonUnique = "RunRequest.Identity.NonUnique";
    public const string RunRequestInvalidFormat = "RunRequest.Identity.InvalidFormat";
    public const string RunRequestUnknownNodeId = "RunRequest.NodeOverrides.UnknownNodeId";
    public const string RunRequestPlanInputOverride = "RunRequest.Plan.InputOverrideNotAllowed";
    
    // Validation errors
    public const string ManifestSchemaInvalid = "Manifest.Schema.Invalid";
    public const string ManifestRequiredFieldMissing = "Manifest.RequiredField.Missing";
    public const string ParameterUnknown = "Parameter.Unknown";
    public const string ParameterRequired = "Parameter.Required";
    public const string ParameterTypeInvalid = "Parameter.Type.Invalid";
    public const string ParameterEnumInvalid = "Parameter.Enum.Invalid";
    public const string EnvironmentKeyEmpty = "Environment.Key.Empty";
    public const string PlanEnvironmentInvalidKey = "Plan.Environment.InvalidKey";
    
    // EnvRef errors
    public const string EnvRefResolveFailed = "EnvRef.ResolveFailed";
    public const string EnvRefSecretOnCommandLine = "EnvRef.SecretOnCommandLine";
    
    // Execution errors
    public const string PrivilegeRequired = "Privilege.Required";
    public const string WorkingDirContainmentFailed = "WorkingDir.Containment.Failed";
    
    // Warnings
    public const string ControlsMaxParallelIgnored = "Controls.MaxParallel.Ignored";
}

/// <summary>
/// Reasons for Suite.TestCaseRef.Invalid errors per spec section 5.2.
/// </summary>
public static class RefInvalidReasons
{
    public const string OutOfRoot = "OutOfRoot";
    public const string NotFound = "NotFound";
    public const string MissingManifest = "MissingManifest";
}
