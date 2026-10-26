namespace App.Web.Options;

/// <summary>
/// The embedded feedback control, bound from the <c>Feedback</c> section (environment: <c>Feedback__*</c>).
///
/// The control is a button on every page that opens a dialog, and a POST endpoint that files what the person
/// typed, with the page they were on, as a ticket in the organization's help desk. With no intake URL
/// configured the feedback goes to the audit-event sink (<c>IEventLog</c>) instead, so nothing is lost and
/// the control can be tried before the help desk is wired up.
/// </summary>
public sealed class FeedbackOptions
{
    public const string SectionName = "Feedback";

    /// <summary>
    /// Feature flag. Unset means "on in Development, off everywhere else", so a deployed app shows the
    /// control only when the host says <c>Feedback__Enabled=true</c>.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>Name of this app as the help desk should see it (the ticket is filed against it).</summary>
    public string AppName { get; set; } = "My App";

    /// <summary>
    /// Absolute URL of the help desk's feedback intake endpoint. Empty means "no help desk": feedback is
    /// written to the audit-event sink. Never a hostname in source; set it on the host.
    /// </summary>
    public Uri? ServiceHubIntakeUrl { get; set; }

    /// <summary>
    /// Shared key the intake endpoint expects as a bearer token. Required when <see cref="ServiceHubIntakeUrl"/>
    /// is set; the readiness check names it when missing. A secret: environment variable on the host only.
    /// </summary>
    public string? ServiceHubIntakeKey { get; set; }

    /// <summary>How many submissions one person may make per window. Spam and runaway scripts, not people.</summary>
    public int MaxPerWindow { get; set; } = 5;

    /// <summary>Length of the rate-limit window in minutes.</summary>
    public int WindowMinutes { get; set; } = 10;

    /// <summary>
    /// Whether the dialog offers the "include a screenshot" checkbox at all. Screenshots can capture
    /// student data that is on screen, so an organization may decide to switch the option off entirely.
    /// </summary>
    public bool AllowScreenshots { get; set; } = true;

    public bool IsServiceHubConfigured => ServiceHubIntakeUrl is not null;

    /// <summary>True when the intake URL is absolute and http or https: the only shape the sink can send to.</summary>
    public bool HasUsableIntakeUrl =>
        ServiceHubIntakeUrl is { IsAbsoluteUri: true } url
        && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp);

    /// <summary>Resolves the tri-state flag for the current environment.</summary>
    public bool IsEnabled(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return Enabled ?? environment.IsDevelopment();
    }
}
