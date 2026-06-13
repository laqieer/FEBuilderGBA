using System.Drawing;
using System.Drawing.Imaging;

// Render a terminal-text capture file to a PNG (dark theme, monospace).
// Args: <inputTextFile> <outputPng> [title]
if (args.Length < 2) { Console.Error.WriteLine("usage: TextToPng <in.txt> <out.png> [title]"); return 1; }
string[] lines;
try
{
    lines = File.ReadAllLines(args[0]);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: could not read input '{args[0]}': {ex.Message}");
    return 1;
}
string title = args.Length >= 3 ? args[2] : "FEBuilderGBA.CLI --migrate-diff (#1131)";

// Expand tabs to spaces for stable column rendering.
for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].Replace("\t", "    ");

using var font = new Font("Consolas", 13, FontStyle.Regular, GraphicsUnit.Pixel);
using var titleFont = new Font("Consolas", 15, FontStyle.Bold, GraphicsUnit.Pixel);
int lineH = 18, padX = 16, padTop = 44, padBottom = 16;

// Measure width.
int maxW = 900;
using (var tmp = new Bitmap(1, 1))
using (var g0 = Graphics.FromImage(tmp))
{
    foreach (var l in lines)
    {
        var sz = g0.MeasureString(l.Length == 0 ? " " : l, font);
        if (sz.Width > maxW) maxW = (int)sz.Width;
    }
}
int w = maxW + padX * 2;
int h = padTop + lines.Length * lineH + padBottom;

using var bmp = new Bitmap(w, h);
using var g = Graphics.FromImage(bmp);
g.Clear(Color.FromArgb(24, 26, 32));
g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

// Title bar.
using (var tb = new SolidBrush(Color.FromArgb(38, 42, 54)))
    g.FillRectangle(tb, 0, 0, w, 34);
using (var tt = new SolidBrush(Color.FromArgb(220, 223, 228)))
    g.DrawString(title, titleFont, tt, padX, 8);

using var colDefault = new SolidBrush(Color.FromArgb(208, 212, 220));
using var colPrompt  = new SolidBrush(Color.FromArgb(120, 200, 130));
using var colHigh    = new SolidBrush(Color.FromArgb(120, 200, 130));
using var colMedium  = new SolidBrush(Color.FromArgb(230, 200, 120));
using var colLow     = new SolidBrush(Color.FromArgb(230, 130, 120));
using var colHeader  = new SolidBrush(Color.FromArgb(130, 180, 240));

float y = padTop;
foreach (var l in lines)
{
    Brush b = colDefault;
    if (l.StartsWith("$ ")) b = colPrompt;
    else if (l.Contains(", High]") || l.Contains("\tHigh\t") || l.EndsWith("High")) b = colHigh;
    else if (l.Contains(", Medium]")) b = colMedium;
    else if (l.Contains(", Low]") || l.Contains("manual migration")) b = colLow;
    else if (l.StartsWith("StartAddr")) b = colHeader;
    g.DrawString(l.Length == 0 ? " " : l, font, b, padX, y);
    y += lineH;
}
bmp.Save(args[1], ImageFormat.Png);
Console.WriteLine($"wrote {args[1]} ({w}x{h})");
return 0;
