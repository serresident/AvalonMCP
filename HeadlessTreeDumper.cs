using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using System.IO;

namespace AvalonMCP;

public class AvalonHeadlessApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
    }
}

public sealed class InspectionResult
{
    public string Tree { get; set; } = "";
    public string PngBase64 { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public LayoutReport Report { get; set; } = new();
}

public sealed class HeadlessTreeDumper : IDisposable
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTreeDumper));

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<AvalonHeadlessApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia();

    public async Task<string> DumpAsync(string xaml, double width = 1024, double height = 768, string? theme = null, string? assemblyPath = null)
    {
        return (await InspectAsync(xaml, width, height, false, theme, assemblyPath, false)).Tree;
    }

    public Task<LayoutReport> LintAsync(string xaml, double width = 1024, double height = 768, string? theme = null, string? assemblyPath = null)
    {
        ValidateArgs(xaml, width, height);
        return _session.Dispatch(() => RunLint(xaml, width, height, theme, assemblyPath), CancellationToken.None);
    }

    public Task<InspectionResult> InspectAsync(string xaml, double width = 1024, double height = 768, bool capture = true, string? theme = null, string? assemblyPath = null, bool annotateErrors = true)
    {
        ValidateArgs(xaml, width, height);
        return _session.Dispatch(() => Render(xaml, width, height, capture, theme, assemblyPath, annotateErrors), CancellationToken.None);
    }

    private static void ValidateArgs(string xaml, double width, double height)
    {
        if (string.IsNullOrWhiteSpace(xaml) || xaml.Length > 1_000_000)
            throw new ArgumentException("xaml must contain 1 to 1000000 characters.");
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 1 || height < 1 || width > 4096 || height > 4096)
            throw new ArgumentException("Viewport dimensions must be finite and between 1 and 4096.");
    }

    private static LayoutReport RunLint(string xaml, double width, double height, string? theme, string? assemblyPath)
    {
        var (root, window, pixelWidth, pixelHeight) = PrepareWindow(xaml, width, height, theme, assemblyPath);
        try
        {
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            return LayoutDiagnosticEngine.Analyze(window, pixelWidth, pixelHeight);
        }
        finally
        {
            window.Close();
        }
    }

    private static InspectionResult Render(string xaml, double width, double height, bool capture, string? theme, string? assemblyPath, bool annotateErrors)
    {
        var (root, window, pixelWidth, pixelHeight) = PrepareWindow(xaml, width, height, theme, assemblyPath);
        try
        {
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var report = LayoutDiagnosticEngine.Analyze(window, pixelWidth, pixelHeight);
            var tree = JsonSerializer.Serialize(ToNode(root, window));

            if (!capture)
            {
                return new InspectionResult
                {
                    Tree = tree,
                    PngBase64 = "",
                    Width = pixelWidth,
                    Height = pixelHeight,
                    Report = report
                };
            }

            using var bitmap = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame available.");
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            if (stream.Length == 0) throw new InvalidOperationException("Renderer produced an empty PNG.");
            var rawBytes = stream.ToArray();

            string finalPngBase64 = (annotateErrors && report.Diagnostics.Count > 0)
                ? DebugOverlayRenderer.RenderAnnotatedPngBase64(rawBytes, report.Diagnostics)
                : Convert.ToBase64String(rawBytes);

            return new InspectionResult
            {
                Tree = tree,
                PngBase64 = finalPngBase64,
                Width = pixelWidth,
                Height = pixelHeight,
                Report = report
            };
        }
        finally
        {
            window.Close();
        }
    }

    private static (Control Root, Window Window, int PixelWidth, int PixelHeight) PrepareWindow(string xaml, double width, double height, string? theme, string? assemblyPath)
    {
        System.Reflection.Assembly? localAssembly = null;
        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            if (!File.Exists(assemblyPath))
                throw new FileNotFoundException($"Assembly not found: {assemblyPath}");
            var fullPath = Path.GetFullPath(assemblyPath);
            var dir = Path.GetDirectoryName(fullPath);
            if (dir != null)
            {
                System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (ctx, name) =>
                {
                    var depPath = Path.Combine(dir, $"{name.Name}.dll");
                    if (File.Exists(depPath))
                    {
                        try
                        {
                            using var stream = new MemoryStream(File.ReadAllBytes(depPath));
                            return ctx.LoadFromStream(stream);
                        }
                        catch { return null; }
                    }
                    return null;
                };
            }
            using var mainStream = new MemoryStream(File.ReadAllBytes(fullPath));
            localAssembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(mainStream);
        }

        object? loaded = localAssembly != null
            ? AvaloniaRuntimeXamlLoader.Load(xaml, localAssembly)
            : AvaloniaRuntimeXamlLoader.Parse(xaml);

        if (loaded is not Control root)
            throw new InvalidOperationException("AXAML root must be an Avalonia Control.");

        var pixelWidth = Math.Max(1, (int)Math.Round(width));
        var pixelHeight = Math.Max(1, (int)Math.Round(height));
        var window = root as Window ?? new Window { Content = root };

        if (!string.IsNullOrWhiteSpace(theme))
        {
            window.RequestedThemeVariant = theme.Equals("dark", StringComparison.OrdinalIgnoreCase)
                ? Avalonia.Styling.ThemeVariant.Dark
                : Avalonia.Styling.ThemeVariant.Light;
        }

        window.Width = pixelWidth;
        window.Height = pixelHeight;

        return (root, window, pixelWidth, pixelHeight);
    }

    public void Dispose()
    {
        try { _session?.Dispose(); }
        catch { }
    }

    private static object ToNode(Visual visual, Visual root)
    {
        var b = visual.Bounds;
        var topLeft = visual.TranslatePoint(default, root) ?? default;
        var ctrl = visual as Control;

        string? text = null;
        if (ctrl is TextBlock tb) text = tb.Text;
        else if (ctrl is TextBox txt) text = txt.Text;
        else if (ctrl is ContentControl cc && cc.Content is string s) text = s;
        else if (ctrl is HeaderedContentControl hc && hc.Header is string h) text = h;

        return new
        {
            type = visual.GetType().FullName,
            name = ctrl?.Name,
            classes = ctrl?.Classes?.ToArray() ?? Array.Empty<string>(),
            text,
            isVisible = ctrl?.IsVisible ?? true,
            isEnabled = ctrl?.IsEnabled ?? true,
            bounds = new { x = b.X, y = b.Y, width = b.Width, height = b.Height },
            absoluteBounds = new { x = topLeft.X, y = topLeft.Y, width = b.Width, height = b.Height },
            margin = ctrl != null ? new { left = ctrl.Margin.Left, top = ctrl.Margin.Top, right = ctrl.Margin.Right, bottom = ctrl.Margin.Bottom } : null,
            horizontalAlignment = ctrl?.HorizontalAlignment.ToString(),
            verticalAlignment = ctrl?.VerticalAlignment.ToString(),
            children = visual.GetVisualChildren().Select(child => ToNode(child, root)).ToArray()
        };
    }
}
