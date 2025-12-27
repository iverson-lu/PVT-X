namespace PcTest.Contracts;

public static class ErrorCodes
{
    public const string SuiteTestCaseRefInvalid = "Suite.TestCaseRef.Invalid";
    public const string EnvRefResolveFailed = "EnvRef.ResolveFailed";
    public const string IdentityNotFound = "Identity.NotFound";
    public const string IdentityNonUnique = "Identity.NonUnique";
    public const string InputsUnknown = "Inputs.Unknown";
    public const string InputsMissingRequired = "Inputs.MissingRequired";
    public const string RunRequestInvalid = "RunRequest.Invalid";
}
