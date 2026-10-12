using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Content.Client.DeadSpace.Sectants;

/// <summary>
/// Дрожащий кроваво-красный текст с глитч-блоками и «просачиванием».
/// Рисуется поверх UI-корня — виден и в лобби, и в игре.
/// </summary>
public sealed class SectantAnnouncementOverlay
{
    private const string FontPath = "/Fonts/Bedstead/Bedstead.otf";
    private const int FontSize = 96;

    private static readonly Color Blood = new(0.75f, 0.05f, 0.05f);
    private static readonly Color Black = new(0f, 0f, 0f, 1f);
    private static readonly Color Pale = new(0.95f, 0.9f, 0.85f);

    private readonly Font _font;
    private readonly Random _rand = new();

    private string _text = string.Empty;
    private float _timeLeft;
    private float _duration;
    private float _elapsed;
    private float _flickerTimer;

    private readonly List<(float phase, float amp)> _charWobble = new();
    private readonly List<float> _charDelay = new();


    private UIBox2i _lastBounds;

    private sealed record GlitchBlock(Vector2 pos, Vector2 size, float alpha, int type, float phase);
    private readonly List<GlitchBlock> _blocks = new();


    public bool Active => _timeLeft > 0f;

    public SectantAnnouncementOverlay(IResourceCache cache)
    {
        _font = cache.GetFont(FontPath, FontSize);
    }

    public void Show(string text, float duration)
    {
        _text = text;
        _timeLeft = duration;
        _duration = duration;
        _elapsed = 0f;
        _flickerTimer = 0f;

        _charWobble.Clear();
        _charDelay.Clear();
        foreach (var _ in text)
        {
            _charWobble.Add((
                (float)(_rand.NextDouble() * Math.PI * 2),
                3f + (float)_rand.NextDouble() * 5f));
            // Появление букв каскадом: первые ~30% времени
            _charDelay.Add((float)_rand.NextDouble() * 0.6f);
        }
    }

    public void FrameUpdate(float frameTime)
    {
        if (_timeLeft <= 0) return;
        _timeLeft -= frameTime;
        _elapsed += frameTime;

        _flickerTimer -= frameTime;
        if (_flickerTimer <= 0)
        {
            _flickerTimer = 0.06f + (float)_rand.NextDouble() * 0.08f;
            RegenBlocks();
        }
    }

    private void RegenBlocks()
    {
        _blocks.Clear();

        if (_lastBounds.Width <= 0 || _lastBounds.Height <= 0)
            return;

        var w = _lastBounds.Width;
        var h = _lastBounds.Height;

        // 1) КРУПНЫЕ БЛОКИ — раскидываем по ВСЕЙ площади экрана
        var bigCount = _rand.Next(12, 22);
        for (var i = 0; i < bigCount; i++)
        {
            var bw = _rand.Next(w / 14, w / 4);      // от ~7% до ~25% ширины
            var bh = _rand.Next(18, 90);
            var bx = _rand.Next(0, Math.Max(1, w - bw));
            var by = _rand.Next(0, Math.Max(1, h - bh));
            var alpha = 0.15f + (float)_rand.NextDouble() * 0.55f;
            // type 0 — чёрный блок, type 1 — белый (палево-белый), type 2 — кровавый
            var type = _rand.NextDouble() switch
            {
                < 0.55 => 0,
                < 0.85 => 1,
                _ => 2,
            };
            _blocks.Add(new GlitchBlock(
                new Vector2(bx, by),
                new Vector2(bw, bh),
                alpha, type,
                (float)(_rand.NextDouble() * Math.PI * 2)));
        }

        // 2) ТОНКИЕ ГОРИЗОНТАЛЬНЫЕ ПОЛОСЫ — на всю ширину
        var stripCount = _rand.Next(6, 14);
        for (var i = 0; i < stripCount; i++)
        {
            var bh = _rand.Next(2, 8);
            var by = _rand.Next(0, Math.Max(1, h - bh));
            var alpha = 0.2f + (float)_rand.NextDouble() * 0.6f;
            var type = _rand.NextDouble() < 0.5 ? 0 : 1;
            _blocks.Add(new GlitchBlock(
                new Vector2(0, by),
                new Vector2(w, bh),
                alpha, type,
                (float)(_rand.NextDouble() * Math.PI * 2)));
        }

        // 3) ШУМОВЫЕ СТРОКИ — пиксельные полосы с оффсетом вправо-влево
        var noiseCount = _rand.Next(3, 8);
        for (var i = 0; i < noiseCount; i++)
        {
            var bh = _rand.Next(10, 40);
            var by = _rand.Next(0, Math.Max(1, h - bh));
            var alpha = 0.08f + (float)_rand.NextDouble() * 0.25f;
            var offsetX = (float)(_rand.NextDouble() * 60 - 30);
            _blocks.Add(new GlitchBlock(
                new Vector2(offsetX, by),
                new Vector2(w, bh),
                alpha, 3,
                (float)(_rand.NextDouble() * Math.PI * 2)));
        }
    }

    public void Draw(DrawingHandleScreen handle, UIBox2i bounds)
    {
        if (_timeLeft <= 0 || string.IsNullOrEmpty(_text)) return;

        _lastBounds = bounds;

        var w = bounds.Width;
        var h = bounds.Height;
        var center = new Vector2(bounds.Left + w / 2f, bounds.Top + h * 0.42f);

        // Появление/затухание
        var t = Math.Clamp(_timeLeft / _duration, 0f, 1f);
        var fade = MathF.Min(1f, _elapsed * 3f) * (t * t * (3f - 2f * t));

        // Глитч-блоки
        foreach (var b in _blocks)
        {
            var wobble = MathF.Sin(_elapsed * 8f + b.phase) * 3f;
            var jitterX = (float)(_rand.NextDouble() * 4 - 2);
            var jitterY = (float)(_rand.NextDouble() * 2 - 1);
            var pos = new Vector2(
                b.pos.X + jitterX + wobble,
                b.pos.Y + jitterY);

            Color color = b.type switch
            {
                0 => new Color(0f, 0f, 0f, b.alpha * fade),                    // чёрный
                1 => new Color(Pale.R, Pale.G, Pale.B, b.alpha * fade * 0.75f), // белый
                2 => new Color(Blood.R, Blood.G, Blood.B, b.alpha * fade * 0.85f), // кровавый
                _ => new Color(0f, 0f, 0f, b.alpha * fade * 0.5f),              // шум — тёмный
            };

            handle.DrawRect(new UIBox2(pos, pos + b.size), color);
        }

        var scale = 1f;
        var maxLineWidth = w * 0.82f;
        var lines = SplitLines(_text, scale, maxLineWidth);

        var ascent = _font.GetAscent(scale);
        var lineStep = FontSize * 1.1f;
        var totalH = lineStep * lines.Count;

        // Каскадное появление букв
        var charIndex = 0;
        var firstBaseline = center.Y - totalH / 2f + ascent;

        foreach (var line in lines)
        {
            var baselineY = firstBaseline + line.index * lineStep;
            var x = center.X - line.width / 2f;

            foreach (var rune in line.text.EnumerateRunes())
            {
                var advance = FontSize * 0.5f;
                if (_font.TryGetCharMetrics(rune, scale, out var m))
                    advance = m.Advance * scale;

                var (phase, amp) = _charWobble.Count > charIndex
                    ? _charWobble[charIndex] : (0f, 4f);

                // Каскад: буква невидима первые X секунд
                var delay = _charDelay.Count > charIndex ? _charDelay[charIndex] : 0f;
                var charFade = Math.Clamp((_elapsed - delay) * 4f, 0f, 1f);
                if (charFade <= 0f)
                {
                    x += advance;
                    charIndex++;
                    continue;
                }

                // Дрожь + редкие резкие сдвиги
                var wobble = MathF.Sin(_elapsed * 2.3f + phase) * amp;
                var jump = _rand.NextDouble() < 0.02 ? (float)(_rand.NextDouble() * 6 - 3) : 0f;
                var charCenter = new Vector2(x + advance / 2f + wobble + jump, baselineY);

                // Лёгкий наклон — рваный край
                var angle = MathF.Sin(_elapsed * 1.5f + phase) * 3f;

                handle.SetTransform(charCenter, Angle.FromDegrees(angle));

                // Тень чёрная, смещена вниз-вправо
                if (_font.TryGetCharMetrics(rune, scale, out _))
                {
                    _font.DrawChar(handle, rune, new Vector2(-advance / 2f + 2f, 2f), scale,
                        new Color(0f, 0f, 0f, charFade * fade));
                    // Кровавая буква
                    _font.DrawChar(handle, rune, new Vector2(-advance / 2f, 0f), scale,
                        new Color(Blood.R, Blood.G, Blood.B, charFade * fade));
                }
                else
                {
                    handle.DrawString(_font, new Vector2(-advance / 2f, 0f), rune.ToString(), scale,
                        new Color(Blood.R, Blood.G, Blood.B, charFade * fade));
                }

                // Редкий потёк — тонкая полоска вниз от буквы
                if (_rand.NextDouble() < 0.04)
                {
                    var dripH = 4f + (float)_rand.NextDouble() * 14f;
                    var dripW = 1.5f;
                    handle.DrawRect(
                        new UIBox2(-dripW / 2f, 0f, dripW / 2f, dripH),
                        new Color(Blood.R, Blood.G, Blood.B, charFade * fade * 0.8f));
                }

                handle.SetTransform(Vector2.Zero, Angle.Zero);
                x += advance;
                charIndex++;
            }
        }
    }

    private sealed record Line(string text, float width, int index);

    private List<Line> SplitLines(string text, float scale, float maxWidth)
    {
        var width = MeasureWidth(text, scale);
        if (width <= maxWidth || !text.Contains(' '))
            return new List<Line> { new(text, width, 0) };

        var half = width / 2f;
        var acc = 0f;
        var bestDiff = float.MaxValue;
        var bestIndex = -1;

        var runes = text.EnumerateRunes().ToList();
        for (var i = 0; i < runes.Count; i++)
        {
            if (runes[i].Value == ' ')
            {
                var diff = MathF.Abs(acc - half);
                if (diff < bestDiff) { bestDiff = diff; bestIndex = i; }
            }
            if (_font.TryGetCharMetrics(runes[i], scale, out var m))
                acc += m.Advance * scale;
            else
                acc += FontSize * 0.5f;
        }

        if (bestIndex < 0)
            return new List<Line> { new(text, width, 0) };

        var line1 = string.Concat(runes.Take(bestIndex).Select(r => r.ToString()));
        var line2 = string.Concat(runes.Skip(bestIndex + 1).Select(r => r.ToString()));
        return new List<Line> {
            new(line1, MeasureWidth(line1, scale), 0),
            new(line2, MeasureWidth(line2, scale), 1)
        };
    }

    private float MeasureWidth(string text, float scale)
    {
        var w = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            if (_font.TryGetCharMetrics(rune, scale, out var m))
                w += m.Advance * scale;
            else
                w += FontSize * 0.5f;
        }
        return w;
    }
}
