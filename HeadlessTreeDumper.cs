using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AvalonMCP;

public sealed class HeadlessTreeDumper
{
    private bool _initialized;

    public string Dump(string xaml, double width = 1024, double height = 768)
    {
        if (!_initialized)
        {
            AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
            _initialized = true;
        }
        if (string.IsNullOrWhiteSpace(xaml)) throw new ArgumentException("xaml must not be empty", nameof(xaml));
        if (AvaloniaRuntimeXamlLoader.Parse(xaml) is not Control root)
            throw new InvalidOperationException("AXAML root must be an Avalonia Control.");
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        return JsonSerializer.Serialize(ToNode(root, root), new JsonSerializerOptions { WriteIndented = true });
    }

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
