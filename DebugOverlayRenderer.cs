using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

namespace AvalonMCP;

public static class DebugOverlayRenderer
{
    public static string RenderAnnotatedPngBase64(byte[] rawPngBytes, List<LayoutDiagnostic> diagnostics)
    {
        if (rawPngBytes == null || rawPngBytes.Length == 0 || diagnostics == null || diagnostics.Count == 0)
        {
            return rawPngBytes != null ? Convert.ToBase64String(rawPngBytes) : "";
        }

        using var inputStream = new MemoryStream(rawPngBytes);
        using var originalBitmap = SKBitmap.Decode(inputStream);
        if (originalBitmap == null) return Convert.ToBase64String(rawPngBytes);

        using var surface = SKSurface.Create(new SKImageInfo(originalBitmap.Width, originalBitmap.Height));
        var canvas = surface.Canvas;
        canvas.DrawBitmap(originalBitmap, 0, 0);

        using var errorStroke = new SKPaint
        {
            Color = new SKColor(235, 50, 50, 240),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };

        using var errorFill = new SKPaint
        {
            Color = new SKColor(235, 50, 50, 45),
            Style = SKPaintStyle.Fill
        };

        using var warnStroke = new SKPaint
        {
            Color = new SKColor(245, 160, 20, 240),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };

        using var warnFill = new SKPaint
        {
            Color = new SKColor(245, 160, 20, 45),
            Style = SKPaintStyle.Fill
        };

        using var badgeBg = new SKPaint
        {
            Color = new SKColor(30, 30, 30, 220),
            Style = SKPaintStyle.Fill
        };

        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = 11
        };

        foreach (var diag in diagnostics)
        {
            bool isError = diag.Severity == DiagnosticSeverity.Error;
            var stroke = isError ? errorStroke : warnStroke;
            var fill = isError ? errorFill : warnFill;

            float x = (float)diag.AbsoluteBounds.X;
            float y = (float)diag.AbsoluteBounds.Y;
            float w = (float)Math.Max(diag.AbsoluteBounds.Width, 14);
            float h = (float)Math.Max(diag.AbsoluteBounds.Height, 14);

            var rect = new SKRect(x, y, x + w, y + h);

            // Draw filled bounding box and border
            canvas.DrawRect(rect, fill);
            canvas.DrawRect(rect, stroke);

            // Draw badge label above or inside rect
            string label = $"[{diag.Code}] {diag.TargetName ?? diag.TargetType}";
            float textWidth = textPaint.MeasureText(label);
            float badgeHeight = 16f;
            float badgeY = y >= badgeHeight + 2 ? y - badgeHeight - 2 : y + 2;
            var badgeRect = new SKRect(x, badgeY, x + textWidth + 8, badgeY + badgeHeight);

            canvas.DrawRect(badgeRect, badgeBg);
            canvas.DrawRect(badgeRect, stroke);
            canvas.DrawText(label, x + 4, badgeY + badgeHeight - 4, textPaint);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var outputStream = new MemoryStream();
        data.SaveTo(outputStream);

        return Convert.ToBase64String(outputStream.ToArray());
    }
}
