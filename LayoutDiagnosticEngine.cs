using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace AvalonMCP;

public enum DiagnosticSeverity
{
    Warning,
    Error
}

public sealed class LayoutRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public static LayoutRect FromRect(Rect r)
    {
        double x = double.IsFinite(r.X) ? Math.Round(r.X, 1) : 0;
        double y = double.IsFinite(r.Y) ? Math.Round(r.Y, 1) : 0;
        double w = double.IsFinite(r.Width) && r.Width > 0 ? Math.Round(r.Width, 1) : 0;
        double h = double.IsFinite(r.Height) && r.Height > 0 ? Math.Round(r.Height, 1) : 0;
        return new LayoutRect { X = x, Y = y, Width = w, Height = h };
    }
}

public sealed class LayoutDiagnostic
{
    public string Code { get; set; } = "";
    public DiagnosticSeverity Severity { get; set; }
    public string TargetType { get; set; } = "";
    public string? TargetName { get; set; }
    public LayoutRect Bounds { get; set; } = new();
    public LayoutRect AbsoluteBounds { get; set; } = new();
    public string Message { get; set; } = "";
    public string Suggestion { get; set; } = "";
}

public sealed class LayoutReport
{
    public bool Passed => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
    public int ErrorCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
    public List<LayoutDiagnostic> Diagnostics { get; set; } = new();
    public string Summary { get; set; } = "";
}

public static class LayoutDiagnosticEngine
{
    public static LayoutReport Analyze(Visual root, double viewportWidth, double viewportHeight)
    {
        var report = new LayoutReport();
        var allControls = root.GetVisualDescendants().OfType<Control>().ToList();
        if (root is Control rootCtrl && !allControls.Contains(rootCtrl))
        {
            allControls.Insert(0, rootCtrl);
        }

        foreach (var ctrl in allControls)
        {
            if (!ctrl.IsVisible) continue;

            CheckZeroBounds(ctrl, root, report);
            CheckTextClipping(ctrl, root, report);
            CheckViewportOverflow(ctrl, root, viewportWidth, viewportHeight, report);
            CheckAsymmetricMargins(ctrl, root, report);
        }

        CheckGridCollisions(allControls, root, report);

        if (report.Diagnostics.Count == 0)
        {
            report.Summary = "✅ Layout is clean! 0 errors, 0 warnings.";
        }
        else
        {
            report.Summary = $"Found {report.ErrorCount} error(s) and {report.WarningCount} warning(s).";
        }

        return report;
    }

    private static void CheckZeroBounds(Control ctrl, Visual root, LayoutReport report)
    {
        var b = ctrl.Bounds;
        if (b.Width > 0.05 && b.Height > 0.05) return;

        bool isSignificant = false;
        string reason = "";

        if (ctrl is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
        {
            isSignificant = true;
            reason = $"TextBlock with text '{Truncate(tb.Text, 20)}' has zero rendered size ({b.Width:F0}x{b.Height:F0}).";
        }
        else if (ctrl is Image img)
        {
            isSignificant = true;
            reason = $"Image element collapsed to zero size ({b.Width:F0}x{b.Height:F0}).";
        }
        else if (ctrl is Button btn && btn.Content != null)
        {
            isSignificant = true;
            reason = $"Button '{ctrl.Name ?? "unnamed"}' with content collapsed to zero size ({b.Width:F0}x{b.Height:F0}).";
        }
        else if (ctrl is TextBox || ctrl is ComboBox || ctrl is Slider || ctrl is CheckBox)
        {
            isSignificant = true;
            reason = $"{ctrl.GetType().Name} '{ctrl.Name ?? "unnamed"}' collapsed to zero size ({b.Width:F0}x{b.Height:F0}).";
        }
        else if (ctrl is Panel p && p.Children.Count > 0 && p.Children.Any(c => c.IsVisible))
        {
            isSignificant = true;
            reason = $"{ctrl.GetType().Name} with {p.Children.Count} children collapsed to zero size ({b.Width:F0}x{b.Height:F0}).";
        }

        if (isSignificant)
        {
            var topLeft = ctrl.TranslatePoint(default, root) ?? default;
            report.Diagnostics.Add(new LayoutDiagnostic
            {
                Code = "ZERO_BOUNDS",
                Severity = DiagnosticSeverity.Error,
                TargetType = ctrl.GetType().Name,
                TargetName = ctrl.Name,
                Bounds = LayoutRect.FromRect(b),
                AbsoluteBounds = LayoutRect.FromRect(new Rect(topLeft, b.Size)),
                Message = reason,
                Suggestion = "Check parent Grid Row/Column definitions (e.g. Star vs Auto), HorizontalAlignment/VerticalAlignment, or set explicit MinWidth/MinHeight."
            });
        }
    }

    private static void CheckTextClipping(Control ctrl, Visual root, LayoutReport report)
    {
        if (ctrl is not TextBlock tb || string.IsNullOrEmpty(tb.Text)) return;
        if (tb.Bounds.Width <= 0 || tb.Bounds.Height <= 0) return;

        double unconstrainedWidth;
        double unconstrainedHeight;
        try
        {
            var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
            var ft = new FormattedText(
                tb.Text,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                tb.FontSize,
                null);
            unconstrainedWidth = ft.Width;
            unconstrainedHeight = ft.Height;
        }
        catch
        {
            unconstrainedWidth = tb.DesiredSize.Width;
            unconstrainedHeight = tb.DesiredSize.Height;
        }

        var actual = tb.Bounds;

        bool widthClipped = unconstrainedWidth > actual.Width + 3.0 && tb.TextWrapping == TextWrapping.NoWrap && tb.TextTrimming == TextTrimming.None;
        bool heightClipped = unconstrainedHeight > actual.Height + 3.0;

        if (widthClipped || heightClipped)
        {
            var topLeft = tb.TranslatePoint(default, root) ?? default;
            double diff = Math.Max(unconstrainedWidth - actual.Width, unconstrainedHeight - actual.Height);
            report.Diagnostics.Add(new LayoutDiagnostic
            {
                Code = "TEXT_CLIPPED",
                Severity = DiagnosticSeverity.Warning,
                TargetType = nameof(TextBlock),
                TargetName = tb.Name,
                Bounds = LayoutRect.FromRect(actual),
                AbsoluteBounds = LayoutRect.FromRect(new Rect(topLeft, actual.Size)),
                Message = $"Text '{Truncate(tb.Text, 25)}' is clipped by ~{diff:F0}px (Required: {unconstrainedWidth:F0}x{unconstrainedHeight:F0}, Actual: {actual.Width:F0}x{actual.Height:F0}).",
                Suggestion = widthClipped 
                    ? "Enable TextWrapping='Wrap', add TextTrimming='CharacterEllipsis', or increase container width." 
                    : "Increase container height or reduce font size/padding."
            });
        }
    }

    private static void CheckViewportOverflow(Control ctrl, Visual root, double vpWidth, double vpHeight, LayoutReport report)
    {
        if (ctrl.Bounds.Width <= 0 || ctrl.Bounds.Height <= 0) return;

        // Do not flag items that are intended to scroll
        if (ctrl.GetVisualAncestors().OfType<ScrollViewer>().Any()) return;

        var topLeft = ctrl.TranslatePoint(default, root) ?? default;
        var absBounds = new Rect(topLeft, ctrl.Bounds.Size);

        bool overflowX = absBounds.Right > vpWidth + 4.0;
        bool overflowY = absBounds.Bottom > vpHeight + 4.0;
        bool negativeX = absBounds.X < -4.0;
        bool negativeY = absBounds.Y < -4.0;

        if (overflowX || overflowY || negativeX || negativeY)
        {
            double overflowAmount = Math.Max(
                Math.Max(absBounds.Right - vpWidth, absBounds.Bottom - vpHeight),
                Math.Max(-absBounds.X, -absBounds.Y)
            );

            // Only report direct controls or top-level containers to avoid noise
            if (ctrl is Panel || ctrl is Button || ctrl is TextBlock || ctrl is TextBox || ctrl is Border)
            {
                report.Diagnostics.Add(new LayoutDiagnostic
                {
                    Code = "VIEWPORT_OVERFLOW",
                    Severity = DiagnosticSeverity.Warning,
                    TargetType = ctrl.GetType().Name,
                    TargetName = ctrl.Name,
                    Bounds = LayoutRect.FromRect(ctrl.Bounds),
                    AbsoluteBounds = LayoutRect.FromRect(absBounds),
                    Message = $"{ctrl.GetType().Name} '{ctrl.Name ?? "unnamed"}' extends outside the window viewport by {overflowAmount:F0}px (Absolute: {absBounds.X:F0},{absBounds.Y:F0} {absBounds.Width:F0}x{absBounds.Height:F0}).",
                    Suggestion = "Wrap content in a ScrollViewer, constrain parent dimensions, or verify margin values."
                });
            }
        }
    }

    private static void CheckAsymmetricMargins(Control ctrl, Visual root, LayoutReport report)
    {
        var m = ctrl.Margin;
        if (ctrl.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Center)
        {
            if (Math.Abs(m.Left - m.Right) > 24.0 && (m.Left > 40 || m.Right > 40))
            {
                var topLeft = ctrl.TranslatePoint(default, root) ?? default;
                report.Diagnostics.Add(new LayoutDiagnostic
                {
                    Code = "ASYMMETRIC_MARGIN",
                    Severity = DiagnosticSeverity.Warning,
                    TargetType = ctrl.GetType().Name,
                    TargetName = ctrl.Name,
                    Bounds = LayoutRect.FromRect(ctrl.Bounds),
                    AbsoluteBounds = LayoutRect.FromRect(new Rect(topLeft, ctrl.Bounds.Size)),
                    Message = $"Center-aligned {ctrl.GetType().Name} has asymmetric horizontal margins (Left={m.Left:F0}, Right={m.Right:F0}), causing off-center displacement.",
                    Suggestion = "Use uniform margins (e.g. Margin='10') or change HorizontalAlignment to Left/Right if asymmetry is intended."
                });
            }
        }
    }

    private static void CheckGridCollisions(List<Control> allControls, Visual root, LayoutReport report)
    {
        var grids = allControls.OfType<Grid>().ToList();
        foreach (var grid in grids)
        {
            var children = grid.Children.Where(c => c.IsVisible && c.Bounds.Width > 4 && c.Bounds.Height > 4).ToList();
            if (children.Count < 2) continue;

            for (int i = 0; i < children.Count; i++)
            {
                for (int j = i + 1; j < children.Count; j++)
                {
                    var a = children[i];
                    var b = children[j];

                    int rowA = Grid.GetRow(a);
                    int colA = Grid.GetColumn(a);
                    int rowB = Grid.GetRow(b);
                    int colB = Grid.GetColumn(b);

                    if (rowA == rowB && colA == colB)
                    {
                        // Ignore decorative background elements (e.g. a Border behind a TextBlock)
                        if (IsBackgroundElement(a) || IsBackgroundElement(b)) continue;

                        var intersect = a.Bounds.Intersect(b.Bounds);
                        double minArea = Math.Min(a.Bounds.Width * a.Bounds.Height, b.Bounds.Width * b.Bounds.Height);

                        if (intersect.Width * intersect.Height > 0.3 * minArea)
                        {
                            var topLeft = a.TranslatePoint(default, root) ?? default;
                            report.Diagnostics.Add(new LayoutDiagnostic
                            {
                                Code = "UNINTENDED_OVERLAP",
                                Severity = DiagnosticSeverity.Error,
                                TargetType = nameof(Grid),
                                TargetName = grid.Name,
                                Bounds = LayoutRect.FromRect(intersect),
                                AbsoluteBounds = LayoutRect.FromRect(new Rect(topLeft, intersect.Size)),
                                Message = $"Controls '{a.Name ?? a.GetType().Name}' and '{b.Name ?? b.GetType().Name}' overlap significantly in Grid cell (Row {rowA}, Col {colA}).",
                                Suggestion = $"Did you forget to specify Grid.Row or Grid.Column on one of the controls? Both currently share cell ({rowA}, {colA})."
                            });
                        }
                    }
                }
            }
        }
    }

    private static bool IsBackgroundElement(Control ctrl)
    {
        // A Border without content or a Shape is often intentional background styling
        if (ctrl is Border border && border.Child == null) return true;
        if (ctrl is Avalonia.Controls.Shapes.Shape) return true;
        return false;
    }

    private static string Truncate(string str, int maxLen)
    {
        if (string.IsNullOrEmpty(str) || str.Length <= maxLen) return str;
        return str.Substring(0, maxLen) + "...";
    }
}
