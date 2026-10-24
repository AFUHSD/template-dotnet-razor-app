using System.Text.RegularExpressions;

namespace App.Web.Services.Feedback;

/// <summary>
/// Strips anything that must not leave the app from text that is about to be filed somewhere else: the page
/// route and query string (which can carry a token, a return URL with a token in it, or a student number)
/// and the free text a person typed.
///
/// Deliberately over-eager. A redacted zip code in a ticket costs nothing; a student number in a help desk
/// outside the student-data boundary costs a disclosure review. Patterns, in the order applied:
/// <list type="number">
/// <item>Connection-string fragments: <c>Server=</c>, <c>Password=</c>, <c>User Id=</c> and friends.</item>
/// <item>Bearer tokens and JSON Web Tokens.</item>
/// <item>Query-string values whose key looks secret (<c>token</c>, <c>key</c>, <c>secret</c>, <c>sig</c>, <c>code</c>, <c>session</c>, <c>password</c>, <c>auth</c>).</item>
/// <item>Any opaque run of 32 or more token characters (API keys, session ids, signed values).</item>
/// <item>Email addresses.</item>
/// <item>Any run of 5 to 9 digits (student numbers are 5 digits; state ids are longer).</item>
/// </list>
/// </summary>
public static partial class FeedbackRedactor
{
    public const string Redacted = "[redacted]";
    public const string RedactedEmail = "[email]";
    public const string RedactedNumber = "[number]";

    /// <summary>Returns the text with every match replaced. Null or empty comes back unchanged.</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? "";

        var result = ConnectionStringFragment().Replace(text, "$1=" + Redacted);
        result = BearerToken().Replace(result, "$1" + Redacted);
        result = JsonWebToken().Replace(result, Redacted);
        result = SecretQueryValue().Replace(result, "$1=" + Redacted);
        result = OpaqueToken().Replace(result, Redacted);
        result = EmailAddress().Replace(result, RedactedEmail);
        result = IdentifierShapedNumber().Replace(result, RedactedNumber);
        return result;
    }

    // Keys from SqlClient, ODBC and OLE DB connection strings. The value runs to the next ';', '&' or whitespace.
    [GeneratedRegex(@"\b(Server|Data Source|Address|Addr|Network Address|Initial Catalog|Database|User Id|User ID|Uid|Password|Pwd|AccountKey|SharedAccessSignature)\s*=\s*[^;&\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionStringFragment();

    [GeneratedRegex(@"(\bBearer\s+)[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerToken();

    // Three base64url segments, the first of which decodes to a JSON header ("eyJ" is '{"' in base64).
    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex JsonWebToken();

    // A query-string pair whose key contains a secret-shaped word. The value runs to the next '&' or '#'.
    [GeneratedRegex(@"([?&][^=&#]*(?:token|secret|key|sig|signature|code|session|password|passwd|pwd|auth)[^=&#]*)=[^&#]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretQueryValue();

    // 32+ characters of the alphabet tokens and signed values are built from, with no spaces: nothing a
    // person types on purpose looks like this.
    [GeneratedRegex(@"(?<![A-Za-z0-9_\-%])[A-Za-z0-9_\-%]{32,}(?![A-Za-z0-9_\-%])", RegexOptions.CultureInvariant)]
    private static partial Regex OpaqueToken();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"(?<![0-9])[0-9]{5,9}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierShapedNumber();
}
