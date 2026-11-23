using PdfSharp.Fonts;

namespace Infrastructure.Reporting;

internal sealed class EmbeddedFontResolver : IFontResolver
{
    internal const string FamilyName = "DejaVu Sans";
    private const string RegularFace = "DejaVuSans";
    private const string BoldFace = "DejaVuSans-Bold";
    private static readonly byte[] RegularFont = ReadFont("DejaVuSans.ttf");
    private static readonly byte[] BoldFont = ReadFont("DejaVuSans-Bold.ttf");

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (!FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new FontResolverInfo(bold ? BoldFace : RegularFace, false, italic);
    }

    public byte[]? GetFont(string faceName) => faceName switch
    {
        RegularFace => RegularFont,
        BoldFace => BoldFont,
        _ => null,
    };

    private static byte[] ReadFont(string fileName)
    {
        var assembly = typeof(EmbeddedFontResolver).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Embedded report font is unavailable.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
