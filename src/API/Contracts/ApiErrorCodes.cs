namespace API.Contracts;

/// <summary>
/// Stable machine-readable error codes exposed by the HTTP boundary.
/// </summary>
public static class ApiErrorCodes
{
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string InvalidArgument = "INVALID_ARGUMENT";
    public const string InvalidOperation = "INVALID_OPERATION";
    public const string ResourceNotFound = "RESOURCE_NOT_FOUND";
    public const string VersionConflict = "VERSION_CONFLICT";
    public const string RuleViolation = "RULE_VIOLATION";
    public const string DependencyUnavailable = "DEPENDENCY_UNAVAILABLE";
    public const string InternalError = "INTERNAL_ERROR";
}
