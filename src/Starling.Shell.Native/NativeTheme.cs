namespace Starling.Shell.Native;

/// <summary>
/// The three chrome themes, as CSS colour strings. This is the native shell's
/// port of <c>Starling.Gui.Theme.ThemeTokens</c> — the same "calm modern"
/// palette, kept here as plain strings because the native chrome is built from
/// HTML/CSS the Starling engine lays out (it has no Avalonia
/// <c>Color</c> dependency). Values are copied verbatim from
/// <c>ThemeTokens.Dark/Light/Contrast</c> so the two shells match pixel-for-pixel.
/// Like the Avalonia <c>ThemeManager</c>, the selection lives in memory only and
/// defaults to <see cref="NativeThemeMode.Dark"/>.
/// </summary>
internal enum NativeThemeMode { Dark, Light, Contrast }

/// <summary>Status-bar state, mirroring <c>Starling.Gui.Chrome.StatusState</c>.</summary>
internal enum NativeStatusState { Ready, Loading, Error }

internal sealed record NativeTheme
{
    public required string Bg { get; init; }
    public required string Panel { get; init; }
    public required string Surface { get; init; }
    public required string Hover { get; init; }
    public required string Border { get; init; }
    public required string Hair { get; init; }
    public required string Strong { get; init; }
    public required string Text { get; init; }
    public required string Text2 { get; init; }
    public required string Muted { get; init; }
    public required string Faint { get; init; }
    public required string Accent { get; init; }
    public required string Accent2 { get; init; }
    public required string AccentBg { get; init; }
    public required string AccentLine { get; init; }
    public required string Warn { get; init; }
    public required string Err { get; init; }

    public static readonly NativeTheme Dark = new()
    {
        Bg = "#18171a",
        Panel = "#1f1e21",
        Surface = "#232225",
        Hover = "rgba(255,255,255,0.04)",
        Border = "rgba(255,255,255,0.06)",
        Hair = "rgba(255,255,255,0.10)",
        Strong = "rgba(255,255,255,0.22)",
        Text = "#ececec",
        Text2 = "#b8b6b3",
        Muted = "#82807c",
        Faint = "#58565a",
        Accent = "#7ec59e",
        Accent2 = "#92d6b0",
        AccentBg = "rgba(126,197,158,0.14)",
        AccentLine = "rgba(126,197,158,0.32)",
        Warn = "#f5b942",
        Err = "#ef6f7a",
    };

    public static readonly NativeTheme Light = new()
    {
        Bg = "#f3f1ec",
        Panel = "#eceae3",
        Surface = "#ffffff",
        Hover = "rgba(20,18,14,0.04)",
        Border = "rgba(20,18,14,0.06)",
        Hair = "rgba(20,18,14,0.10)",
        Strong = "rgba(20,18,14,0.20)",
        Text = "#1a1916",
        Text2 = "#4a4842",
        Muted = "#8b8880",
        Faint = "#b6b3aa",
        Accent = "#2e6b54",
        Accent2 = "#245443",
        AccentBg = "#d8e6df",
        AccentLine = "rgba(46,107,84,0.30)",
        Warn = "#b97309",
        Err = "#c54250",
    };

    public static readonly NativeTheme Contrast = new()
    {
        Bg = "#000000",
        Panel = "#0a0a0a",
        Surface = "#121214",
        Hover = "rgba(255,255,255,0.08)",
        Border = "rgba(255,255,255,0.20)",
        Hair = "rgba(255,255,255,0.30)",
        Strong = "rgba(255,255,255,0.55)",
        Text = "#ffffff",
        Text2 = "#e8e8e8",
        Muted = "#b0b0b0",
        Faint = "#808080",
        Accent = "#a4f5c2",
        Accent2 = "#7fe0a5",
        AccentBg = "rgba(164,245,194,0.15)",
        AccentLine = "rgba(164,245,194,0.55)",
        Warn = "#ffd166",
        Err = "#ff8a94",
    };

    public static NativeTheme For(NativeThemeMode mode) => mode switch
    {
        NativeThemeMode.Light => Light,
        NativeThemeMode.Contrast => Contrast,
        _ => Dark,
    };

    /// <summary>Cycle Dark → Light → Contrast → Dark, matching the toolbar toggle.</summary>
    public static NativeThemeMode Next(NativeThemeMode mode) => mode switch
    {
        NativeThemeMode.Dark => NativeThemeMode.Light,
        NativeThemeMode.Light => NativeThemeMode.Contrast,
        _ => NativeThemeMode.Dark,
    };
}
