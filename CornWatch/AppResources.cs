using System.Reflection;

namespace CornWatch;

// Files embedded in the exe (see <EmbeddedResource> in the .csproj), so CornWatch runs from a
// single file — the portable build has nothing next to it, and the installer installs one exe.
internal static class AppResources
{
    public const string IconName = "cornwatch.ico";
    public const string DashboardName = "Dashboard.html";

    public static Stream? Open(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream("CornWatch." + name);

    // Icon at the requested size from the multi-size .ico (null if the resource is missing).
    public static Icon? LoadIcon(Size size)
    {
        using var stream = Open(IconName);
        return stream is null ? null : new Icon(stream, size);
    }

    // Writes the embedded dashboard for WebView2 to load; rewritten only when it differs,
    // so an upgrade replaces it and a normal start doesn't touch the disk.
    public static string ExtractDashboard()
    {
        using var stream = Open(DashboardName) ?? throw new FileNotFoundException("The dashboard is missing from the CornWatch executable.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();

        var path = AppPaths.DashboardFile;
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
        {
            AppPaths.EnsureDir(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        return path;
    }
}
