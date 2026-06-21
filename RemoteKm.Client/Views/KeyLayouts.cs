using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using RemoteKm.Shared;

namespace RemoteKm.Client.Views;

/// <summary>
/// Per-language keyboard character tables, loaded from embedded JSON files
/// (Resources/Layouts/&lt;lang&gt;.json). Each printable key carries the virtual-key to send
/// plus the characters it shows (unshifted / shifted) so the on-screen keyboard mirrors the
/// PC keyboard. Letters follow the layout family (QWERTY/QWERTZ/AZERTY); the JSON only
/// describes the top number row and punctuation keys. To add a language, drop a new
/// &lt;lang&gt;.json next to the others and mark it as an EmbeddedResource.
/// </summary>
public static class KeyLayouts
{
    public readonly record struct Cap(string Vk, string Normal, string Shift);

    public sealed class Caps
    {
        public required Cap[] NumberRow;
        public required string[] Letters2;
        public required string[] Letters3;
        public required string[] Letters4;
        public required Cap[] Row2End;
        public required Cap[] Row3End;
        public required Cap[] Row4End;
    }

    private static readonly ConcurrentDictionary<string, LayoutDto> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Caps For(string language, KeyboardLayout family)
    {
        var dto = Load(language) ?? Load("en")!;
        var (l2, l3, l4) = LettersFor(family);

        return new Caps
        {
            NumberRow = dto.NumberRow.Select(ToCap).ToArray(),
            Letters2 = l2,
            Letters3 = l3,
            Letters4 = l4,
            Row2End = dto.Row2End.Select(ToCap).ToArray(),
            Row3End = dto.Row3End.Select(ToCap).ToArray(),
            Row4End = dto.Row4End.Select(ToCap).ToArray(),
        };
    }

    private static Cap ToCap(CapDto c) => new(c.Vk, c.N, c.S);

    private static LayoutDto? Load(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;
        if (Cache.TryGetValue(language, out var cached))
            return cached;

        try
        {
            var asm = typeof(KeyLayouts).Assembly;
            var suffix = $".Layouts.{language}.json";
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (name is null)
                return null;

            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null)
                return null;

            var dto = JsonSerializer.Deserialize(stream, LayoutJsonContext.Default.LayoutDto);
            if (dto is not null)
                Cache[language] = dto;
            return dto;
        }
        catch
        {
            return null;
        }
    }

    private static (string[] r2, string[] r3, string[] r4) LettersFor(KeyboardLayout family) => family switch
    {
        KeyboardLayout.Qwertz => (Split("qwertzuiop"), Split("asdfghjkl"), Split("yxcvbnm")),
        KeyboardLayout.Azerty => (Split("azertyuiop"), Split("qsdfghjklm"), Split("wxcvbn")),
        _ => (Split("qwertyuiop"), Split("asdfghjkl"), Split("zxcvbnm")),
    };

    private static string[] Split(string s) => s.Select(c => c.ToString()).ToArray();
}

// ---- JSON schema (embedded layout files) ----

public sealed class CapDto
{
    public string Vk { get; set; } = "";
    public string N { get; set; } = "";
    public string S { get; set; } = "";
}

public sealed class LayoutDto
{
    public string Language { get; set; } = "";
    public List<CapDto> NumberRow { get; set; } = new();
    public List<CapDto> Row2End { get; set; } = new();
    public List<CapDto> Row3End { get; set; } = new();
    public List<CapDto> Row4End { get; set; } = new();
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(LayoutDto))]
internal partial class LayoutJsonContext : JsonSerializerContext
{
}
