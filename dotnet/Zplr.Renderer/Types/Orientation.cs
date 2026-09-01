// Port of src/types/Orientation.ts — keep string values "N"|"R"|"I"|"B" for file-for-file diffability.
// TS type Orientation = "N" | "R" | "I" | "B"
namespace Zplr.Renderer.Types;

/// <summary>Mirrored from <c>Orientation</c> in TypeScript. Values match ZPL orientation codes.</summary>
public enum Orientation
{
    N = 0, // Normal
    R = 1, // Rotated 90°
    I = 2, // Inverted 180°
    B = 3, // Bottom-up 270°
}

public static class OrientationExtensions
{
    public static string ToZplCode(this Orientation o) => o switch
    {
        Orientation.N => "N",
        Orientation.R => "R",
        Orientation.I => "I",
        Orientation.B => "B",
        _ => "N",
    };

    public static Orientation FromZplCode(string? value, Orientation fallback)
    {
        var s = value?.Trim();
        return s switch
        {
            "N" => Orientation.N,
            "R" => Orientation.R,
            "I" => Orientation.I,
            "B" => Orientation.B,
            _ => fallback,
        };
    }

    public static bool IsRotated(this Orientation o) => o == Orientation.R || o == Orientation.B;
}
