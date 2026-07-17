using System.Reflection;
using SkiaSharp;

namespace MyoTrack.Infrastructure.Ai;

/// <summary>Etiqueta a desenhar: texto e posição do alimento (escala 0-1000, opcional).</summary>
public record MealAnnotation(string Label, int? PosX, int? PosY);

/// <summary>
/// Renderizador local da análise ilustrada: desenha etiquetas apontando cada
/// alimento e um cartão de totais sobre a foto. Determinístico e gratuito —
/// é o fallback quando o modelo de imagem generativo não está disponível.
/// Fontes DejaVu embutidas (containers não têm fontes do sistema).
/// </summary>
public static class MealImageAnnotator
{
    private static readonly Lazy<SKTypeface> Regular = new(() => LoadFont("DejaVuSans.ttf"));
    private static readonly Lazy<SKTypeface> Bold = new(() => LoadFont("DejaVuSans-Bold.ttf"));

    private static SKTypeface LoadFont(string fileName)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"MyoTrack.Infrastructure.Assets.Fonts.{fileName}")
            ?? throw new InvalidOperationException($"Fonte embutida não encontrada: {fileName}");
        return SKTypeface.FromStream(stream)
            ?? throw new InvalidOperationException($"Falha ao carregar a fonte {fileName}");
    }

    public static byte[] Render(byte[] photoBytes, IReadOnlyList<MealAnnotation> items, string totals)
    {
        using var bitmap = SKBitmap.Decode(photoBytes)
            ?? throw new InvalidOperationException("Não foi possível decodificar a foto.");

        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height));
        var canvas = surface.Canvas;
        canvas.DrawBitmap(bitmap, 0, 0);

        // Todas as medidas escalam com a menor dimensão da foto.
        float unit = Math.Min(bitmap.Width, bitmap.Height);
        float fontSize = Math.Clamp(unit * 0.032f, 13f, 44f);
        float pad = fontSize * 0.55f;
        float corner = fontSize * 0.45f;
        float dotRadius = fontSize * 0.38f;

        using var font = new SKFont(Regular.Value, fontSize);
        using var boldFont = new SKFont(Bold.Value, fontSize);
        using var chipPaint = new SKPaint { Color = new SKColor(15, 23, 42, 200), IsAntialias = true };
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var accentPaint = new SKPaint { Color = new SKColor(16, 185, 129), IsAntialias = true };
        using var ringPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = fontSize * 0.14f,
        };
        using var linePaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 220),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = fontSize * 0.1f,
        };

        var placed = new List<SKRect>();
        float chipHeight = fontSize + pad * 1.5f;
        float stackY = pad; // pilha (canto superior esquerdo) para itens sem posição

        foreach (var item in items)
        {
            float textWidth = font.MeasureText(item.Label);
            float chipWidth = textWidth + pad * 2;

            SKRect chip;
            if (item is { PosX: not null, PosY: not null })
            {
                float px = item.PosX.Value / 1000f * bitmap.Width;
                float py = item.PosY.Value / 1000f * bitmap.Height;

                // Etiqueta deslocada do ponto; espelha para dentro se estourar a borda.
                float cx = px + fontSize * 1.6f;
                float cy = py - chipHeight - fontSize * 1.2f;
                if (cx + chipWidth > bitmap.Width - pad) cx = px - chipWidth - fontSize * 1.6f;
                if (cx < pad) cx = pad;
                if (cy < pad) cy = py + fontSize * 1.2f;
                if (cy + chipHeight > bitmap.Height - pad) cy = bitmap.Height - pad - chipHeight;

                chip = SKRect.Create(cx, cy, chipWidth, chipHeight);
                chip = Nudge(chip, placed, chipHeight, bitmap.Height, pad);

                canvas.DrawLine(px, py, chip.MidX, chip.MidY, linePaint);
                canvas.DrawCircle(px, py, dotRadius, accentPaint);
                canvas.DrawCircle(px, py, dotRadius, ringPaint);
            }
            else
            {
                chip = SKRect.Create(pad, stackY, chipWidth, chipHeight);
                chip = Nudge(chip, placed, chipHeight, bitmap.Height, pad);
                stackY = chip.Bottom + pad * 0.5f;
            }

            canvas.DrawRoundRect(chip, corner, corner, chipPaint);
            canvas.DrawText(item.Label, chip.Left + pad, chip.MidY + fontSize * 0.36f,
                SKTextAlign.Left, font, textPaint);
            placed.Add(chip);
        }

        // Cartão de totais no rodapé, largura completa da linha de texto.
        float totalsWidth = boldFont.MeasureText(totals) + pad * 2.6f;
        float totalsHeight = fontSize + pad * 1.8f;
        var totalsRect = SKRect.Create(
            bitmap.Width - totalsWidth - pad, bitmap.Height - totalsHeight - pad, totalsWidth, totalsHeight);
        canvas.DrawRoundRect(totalsRect, corner, corner, chipPaint);
        var accentBar = SKRect.Create(totalsRect.Left, totalsRect.Top, pad * 0.45f, totalsRect.Height);
        canvas.DrawRoundRect(accentBar, corner * 0.6f, corner * 0.6f, accentPaint);
        canvas.DrawText(totals, totalsRect.Left + pad * 1.4f, totalsRect.MidY + fontSize * 0.36f,
            SKTextAlign.Left, boldFont, textPaint);

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return encoded.ToArray();
    }

    /// <summary>Empurra a etiqueta para baixo até não sobrepor as já colocadas.</summary>
    private static SKRect Nudge(SKRect chip, List<SKRect> placed, float chipHeight, int imageHeight, float pad)
    {
        for (var attempt = 0; attempt < 12 && placed.Any(p => p.IntersectsWith(chip)); attempt++)
        {
            chip.Offset(0, chipHeight + pad * 0.4f);
            if (chip.Bottom > imageHeight - pad)
                chip.Offset(0, -(chip.Top - pad)); // volta ao topo se estourar embaixo
        }
        return chip;
    }
}
