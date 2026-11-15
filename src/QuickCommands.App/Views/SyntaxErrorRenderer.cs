using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using QuickCommands.Core;

namespace QuickCommands.App.Views;

/// <summary>Draws a red wavy line under each syntax error, from the error column to the end of the line.</summary>
internal sealed class SyntaxErrorRenderer : IBackgroundRenderer
{
    private readonly Pen _pen = new(new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)), 1.2);

    public IReadOnlyList<SyntaxError> Errors { get; set; } = [];

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Errors.Count == 0 || textView.Document is not { } document || !textView.VisualLinesValid)
            return;

        foreach (var error in Errors)
        {
            if (error.Line < 1 || error.Line > document.LineCount)
                continue;
            var line = document.GetLineByNumber(error.Line);
            var text = document.GetText(line.Offset, line.Length);
            var start = Math.Clamp(error.Column - 1, 0, line.Length);
            // Errors at the end of a line ("missing }") point behind the text – underline the whole line then.
            if (start >= text.TrimEnd().Length)
                start = text.Length - text.TrimStart().Length;
            var length = Math.Max(line.Length - start, 0);
            var segment = new TextSegment { StartOffset = line.Offset + start, Length = Math.Max(length, 0) };

            var rects = length > 0 ? BackgroundGeometryBuilder.GetRectsForSegment(textView, segment).ToList() : [];
            if (rects.Count == 0)
            {
                // Empty line: mark its start.
                var position = textView.GetVisualPosition(new TextViewPosition(error.Line, 1), VisualYPosition.LineBottom) - textView.ScrollOffset;
                rects.Add(new Rect(position.X, position.Y - 2, 12, 2));
            }
            foreach (var rect in rects)
                DrawWave(drawingContext, rect.BottomLeft, rect.Width);
        }
    }

    private void DrawWave(DrawingContext context, Point start, double width)
    {
        const double step = 2.5;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(start.X, start.Y - 1), false);
            var up = true;
            for (var x = start.X + step; x <= start.X + width + step; x += step)
            {
                ctx.LineTo(new Point(x, start.Y + (up ? -2.5 : -0.5)));
                up = !up;
            }
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, _pen, geometry);
    }
}
