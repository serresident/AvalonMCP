using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using System.IO;

namespace AvalonMCP;

public sealed class HeadlessTreeDumper : IDisposable
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTreeDumper));

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia();

    public async Task<string> DumpAsync(string xaml, double width = 1024, double height = 768)
    {
        return (await InspectAsync(xaml, width, height, false)).Tree;
    }

    public Task<(string Tree, string PngBase64, int Width, int Height)> InspectAsync(string xaml, double width = 1024, double height = 768, bool capture = true)
    {
        if (string.IsNullOrWhiteSpace(xaml) || xaml.Length > 1_000_000)
            throw new ArgumentException("xaml must contain 1 to 1000000 characters.");
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 1 || height < 1 || width > 4096 || height > 4096)
            throw new ArgumentException("Viewport dimensions must be finite and between 1 and 4096.");
        return _session.Dispatch(() => Render(xaml, width, height, capture), CancellationToken.None);
    }

    private static (string Tree, string PngBase64, int Width, int Height) Render(string xaml, double width, double height, bool capture)
    {
        if (AvaloniaRuntimeXamlLoader.Parse(xaml) is not Control root)
            throw new InvalidOperationException("AXAML root must be an Avalonia Control.");
        var pixelWidth = Math.Max(1, (int)Math.Round(width));
        var pixelHeight = Math.Max(1, (int)Math.Round(height));
        var window = root as Window ?? new Window { Content = root };
        try
        {
            window.Width = pixelWidth;
            window.Height = pixelHeight;
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var tree = JsonSerializer.Serialize(ToNode(root, window));
            if (!capture) return (tree, "", pixelWidth, pixelHeight);
            using var bitmap = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame available.");
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            if (stream.Length == 0) throw new InvalidOperationException("Renderer produced an empty PNG.");
            return (tree, Convert.ToBase64String(stream.ToArray()), pixelWidth, pixelHeight);
        }
        finally { window.Close(); }
    }

    public void Dispose() => _session.Dispose();

    private static object ToNode(Visual visual, Visual root)
    {
        var b = visual.Bounds;
        var topLeft = visual.TranslatePoint(default, root) ?? default;
        return new
        {
            type = visual.GetType().FullName,
            name = (visual as Control)?.Name,
            classes = (visual as Control)?.Classes?.ToArray() ?? Array.Empty<string>(),
            bounds = new { x = b.X, y = b.Y, width = b.Width, height = b.Height },
            absoluteBounds = new { x = topLeft.X, y = topLeft.Y, width = b.Width, height = b.Height },
            children = visual.GetVisualChildren().Select(child => ToNode(child, root)).ToArray()
        };
    }
}
