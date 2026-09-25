namespace App.Web.Options;

/// <summary>
/// Settings for the example access rule in <see cref="Services.Access.DomainAccessResolver"/>.
/// Bound from the <c>Access</c> section. Replace or delete along with the resolver.
/// </summary>
public sealed class AccessOptions
{
    public const string SectionName = "Access";

    /// <summary>
    /// Email domain allowed in (for example <c>example.org</c>). Empty means nobody: the example rule fails
    /// closed.
    /// </summary>
    public string? AllowedEmailDomain { get; set; }
}
