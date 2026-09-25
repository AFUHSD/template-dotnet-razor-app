namespace App.Web.Tests;

/// <summary>
/// A Razor Page binds its code-behind ONLY through the @model directive. Leave it out and the page still
/// compiles, routes and renders, but its PageModel never runs: no OnGet, no OnPost, no side effect. A
/// sign-out page shipped that way once and said "you have been signed out" while the session cookie stayed
/// intact. Nothing errors, so this test is the only thing that catches it.
/// </summary>
public sealed class PageModelDirectiveTests
{
    [Fact]
    public void Every_page_with_a_code_behind_declares_its_model()
    {
        var src = Path.Combine(RepoRoot.Find(), "src");
        var checkedPages = 0;
        var missing = new List<string>();

        foreach (var codeBehind in Directory.EnumerateFiles(src, "*.cshtml.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(codeBehind)) continue;
            var view = codeBehind[..^".cs".Length];
            if (!File.Exists(view)) continue;

            checkedPages++;
            var declaresModel = File.ReadLines(view)
                .Any(line => line.TrimStart().StartsWith("@model ", StringComparison.Ordinal));
            if (!declaresModel)
                missing.Add(Path.GetRelativePath(src, view));
        }

        // A guard that checked nothing would pass forever; make sure it found the pages.
        Assert.True(checkedPages > 0, "Found no .cshtml.cs files under src/. Has the layout changed?");
        Assert.True(missing.Count == 0,
            "These pages have a code-behind but no @model directive, so their handlers never run: "
            + string.Join(", ", missing));
    }

    private static bool IsBuildOutput(string path)
    {
        var p = path.Replace('\\', '/');
        return p.Contains("/bin/", StringComparison.Ordinal) || p.Contains("/obj/", StringComparison.Ordinal);
    }
}

internal static class RepoRoot
{
    public static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "App.slnx")))
                return dir.FullName;
        throw new InvalidOperationException("Could not find the repository root (App.slnx) above the test output folder.");
    }
}
