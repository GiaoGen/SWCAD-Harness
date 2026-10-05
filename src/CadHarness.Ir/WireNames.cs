using System;
using System.Text.RegularExpressions;

namespace CadHarness.Ir;

public static class WireNames
{
    public static string Of<T>(T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
        return Regex.Replace(value.ToString(), "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
    }

    public static bool TryParse<T>(string name, out T value) where T : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<T>())
            if (Of(candidate) == name) { value = candidate; return true; }
        value = default;
        return false;
    }
}

public static class Identifiers
{
    internal const string SegmentPattern = "[a-z][a-z0-9]*(?:_[a-z0-9]+)*";
    internal const string IdPattern = "^" + SegmentPattern + "$";
    internal const string ReferencePattern = "^" + SegmentPattern + "(?:\\." + SegmentPattern + ")*$";
    // Defense in depth in addition to closed fields/enums and lowercase semantic IDs.
    // This is a native-name deny rule, not a claim to recognize arbitrary source code.
    private static readonly Regex NativeName = new(
        @"(?:^|[._])(?:i(?:face|edge|body|feature|modeldoc|sldworks|sketch|surface|component|assemblydoc|partdoc|modeldocextension)\d*|sldworks|solidworks|com|marshal|dispatch|select(?:byid)?\d*|feature(?:extrusion|cut|linearpattern|circularpattern|fillet|chamfer)\d*|(?:boss_)?extrude\d+|cut_extrude\d+|d\d+)(?:$|[._])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static bool IsSafe(string? value, bool reference = false) => value is not null &&
        value.Length <= 128 && Regex.IsMatch(value, reference ? ReferencePattern : IdPattern, RegexOptions.CultureInvariant) &&
        !NativeName.IsMatch(value);
    public static string SchemaPattern(bool reference) => "^(?!.*" + NativeName + ")" +
        (reference ? ReferencePattern[1..] : IdPattern[1..]);
}
