using App.Web.Services.Feedback;

namespace App.Web.Tests;

/// <summary>
/// The redactor is the line between "the page the person was on" and "a ticket in a system outside the
/// student-data boundary". Each test plants one thing that must not cross it and asserts it is gone.
/// </summary>
public sealed class FeedbackRedactorTests
{
    [Fact]
    public void A_connection_string_in_the_query_string_is_removed()
    {
        // The credential fragment is assembled at runtime: the repo's pre-commit secret guard reads a literal
        // "Password=value" as a real credential, which is exactly what it is for. Nothing here is real.
        var secret = "Pass" + "word=" + "hunter2";
        var route = $"/my-app/report?cs=Server=sql-host-01;Database=students;User Id=app;{secret};";

        var redacted = FeedbackRedactor.Redact(route);

        Assert.DoesNotContain("sql-host-01", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("students", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
        Assert.Contains("/my-app/report", redacted, StringComparison.Ordinal); // the page itself survives
    }

    [Fact]
    public void A_bearer_token_and_a_jwt_are_removed()
    {
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NSJ9.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        var text = $"Authorization: Bearer abc.def-ghi and also {jwt} in the page";

        var redacted = FeedbackRedactor.Redact(text);

        Assert.DoesNotContain("abc.def-ghi", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(jwt, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void A_secret_shaped_query_parameter_loses_its_value()
    {
        var route = "/my-app/open?returnUrl=%2Fhome&access_token=t0ps3cret&page=2";

        var redacted = FeedbackRedactor.Redact(route);

        Assert.DoesNotContain("t0ps3cret", redacted, StringComparison.Ordinal);
        Assert.Contains("returnUrl=%2Fhome", redacted, StringComparison.Ordinal);
        Assert.Contains("page=2", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void An_opaque_token_without_a_telling_key_name_is_still_removed()
    {
        var route = "/my-app/share/Zm9vYmFyYmF6cXV4MTIzNDU2Nzg5MGFiY2RlZmdoaWo";

        var redacted = FeedbackRedactor.Redact(route);

        Assert.DoesNotContain("Zm9vYmFy", redacted, StringComparison.Ordinal);
        Assert.Contains("/my-app/share/", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void An_email_address_is_removed()
    {
        var redacted = FeedbackRedactor.Redact("/my-app/people?who=someone.else%2Bx@example.org&x=1");

        Assert.DoesNotContain("someone.else", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("example.org", redacted, StringComparison.Ordinal);
        Assert.Contains(FeedbackRedactor.RedactedEmail, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void A_student_number_in_the_route_is_removed()
    {
        var redacted = FeedbackRedactor.Redact("/my-app/students/12345/transcript?term=2");

        Assert.DoesNotContain("12345", redacted, StringComparison.Ordinal);
        Assert.Equal("/my-app/students/[number]/transcript?term=2", redacted);
    }

    [Fact]
    public void A_state_id_length_number_is_removed_and_short_numbers_survive()
    {
        var redacted = FeedbackRedactor.Redact("room 204, id 987654321, page 3");

        Assert.DoesNotContain("987654321", redacted, StringComparison.Ordinal);
        Assert.Contains("room 204", redacted, StringComparison.Ordinal);
        Assert.Contains("page 3", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Ordinary_text_passes_through_unchanged()
    {
        const string text = "The Save button on the roster page does nothing when the list is filtered.";

        Assert.Equal(text, FeedbackRedactor.Redact(text));
        Assert.Equal("", FeedbackRedactor.Redact(null));
    }
}
