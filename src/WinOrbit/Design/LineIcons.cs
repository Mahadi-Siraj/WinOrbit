namespace WinOrbit.Design;

/// <summary>
/// WinOrbit's own line icons for the Control Centre: drawn on a 24×24 grid with round caps and joins and a
/// single stroke weight, so they read as one calm family (rather than the heavier system glyphs).
/// </summary>
public static class LineIcons
{
    public const string WiFi = "M2.6,10 A13.4,13.4 0 0 1 21.4,10 M5.8,13.3 A8.9,8.9 0 0 1 18.2,13.3 M9,16.5 A4.4,4.4 0 0 1 15,16.5 M12,19.9 A0.35,0.35 0 1 1 11.99,19.9 Z";
    public const string Bluetooth = "M6.6,7.6 L16.8,16.6 L12,20.8 L12,3.2 L16.8,7.4 L6.6,16.4";
    /// <summary>Night light: a low, warm sun on the horizon (distinct from the brightness sun).</summary>
    public const string Sunset = "M3.2,17.4 L20.8,17.4 M7.6,21 L16.4,21 M7.2,17.4 A4.8,4.8 0 0 1 16.8,17.4 M12,5.6 L12,8.2 M5.2,9.4 L7,11.2 M18.8,9.4 L17,11.2 M2.8,13.6 L4.9,13.6 M19.1,13.6 L21.2,13.6";
    public const string Airplane = "M12,2.8 C12.9,2.8 13.4,3.7 13.4,4.8 L13.4,9.4 L20.6,13.6 L20.6,15.6 L13.4,13.3 L13.4,17.6 L15.8,19.5 L15.8,21.1 L12,20 L8.2,21.1 L8.2,19.5 L10.6,17.6 L10.6,13.3 L3.4,15.6 L3.4,13.6 L10.6,9.4 L10.6,4.8 C10.6,3.7 11.1,2.8 12,2.8 Z";
    public const string Moon = "M19.6,14.8 A8.4,8.4 0 1 1 9.2,4.4 A6.6,6.6 0 0 0 19.6,14.8 Z";
    public const string Sun = "M12,8 A4,4 0 1 1 11.99,8 Z M12,2.6 L12,4.4 M12,19.6 L12,21.4 M2.6,12 L4.4,12 M19.6,12 L21.4,12 M5.4,5.4 L6.6,6.6 M17.4,17.4 L18.6,18.6 M5.4,18.6 L6.6,17.4 M17.4,6.6 L18.6,5.4";
    public const string Contrast = "M12,3.8 A8.2,8.2 0 1 1 11.99,3.8 Z M12,3.8 L12,20.2 M12,7 L15.5,7 M12,10.4 L18.4,10.4 M12,13.8 L18.4,13.8 M12,17.2 L15.5,17.2";
    public const string Leaf = "M5,19.2 C5,10.6 10.6,5 19.4,4.6 C19.6,13 14.6,19.2 6.6,19.2 Z M5,19.2 L12.4,11.8";
    public const string Bolt = "M13.2,2.8 L5.6,13.4 L11.6,13.4 L10.6,21.2 L18.4,10 L12.4,10 Z";
    public const string Mic = "M9.2,6.2 A2.8,2.8 0 0 1 14.8,6.2 L14.8,11.4 A2.8,2.8 0 0 1 9.2,11.4 Z M5.6,11 A6.4,6.4 0 0 0 18.4,11 M12,17.4 L12,20.8 M9,20.8 L15,20.8";
    public const string Cup = "M4.8,9 L16,9 L16,13.8 A5,5 0 0 1 11,18.8 L9.8,18.8 A5,5 0 0 1 4.8,13.8 Z M16,10.4 L17.4,10.4 A2.5,2.5 0 0 1 17.4,15.4 L16,15.4 M8.4,3.4 C7.6,4.8 9.2,5.4 8.4,6.8 M12,3.4 C11.2,4.8 12.8,5.4 12,6.8 M4.4,21 L16.4,21";
    public const string Target = "M12,3.8 A8.2,8.2 0 1 1 11.99,3.8 Z M12,8 A4,4 0 1 1 11.99,8 Z M12,11.2 A0.8,0.8 0 1 1 11.99,11.2 Z";
    // ---- system monitor
    public const string Cpu = "M7,7 L17,7 L17,17 L7,17 Z M10.2,10.2 L13.8,10.2 L13.8,13.8 L10.2,13.8 Z M9.5,3.6 L9.5,7 M14.5,3.6 L14.5,7 M9.5,17 L9.5,20.4 M14.5,17 L14.5,20.4 M3.6,9.5 L7,9.5 M3.6,14.5 L7,14.5 M17,9.5 L20.4,9.5 M17,14.5 L20.4,14.5";
    public const string Memory = "M3.4,7 L20.6,7 L20.6,15 L3.4,15 Z M7.2,9.8 L7.2,12.2 M10.4,9.8 L10.4,12.2 M13.6,9.8 L13.6,12.2 M16.8,9.8 L16.8,12.2 M6,15 L6,18 M9.4,15 L9.4,18 M14.6,15 L14.6,18 M18,15 L18,18";
    public const string Gpu = "M3,6.5 L21,6.5 L21,16.5 L3,16.5 Z M6.2,11.5 A2.9,2.9 0 1 1 12,11.5 A2.9,2.9 0 1 1 6.2,11.5 Z M15,9.6 L18.2,9.6 M15,13.4 L18.2,13.4 M5.5,16.5 L5.5,19.5 L11,19.5 L11,16.5";
    public const string Network = "M8,19.2 L8,4.8 M4.6,8.2 L8,4.8 L11.4,8.2 M16,4.8 L16,19.2 M12.6,15.8 L16,19.2 L19.4,15.8";
    public const string Disk = "M5,6.4 A7,2.6 0 1 0 19,6.4 A7,2.6 0 1 0 5,6.4 Z M5,6.4 L5,17.6 A7,2.6 0 0 0 19,17.6 L19,6.4 M5,12 A7,2.6 0 0 0 19,12";
    public const string Clock = "M12,3.8 A8.2,8.2 0 1 1 11.99,3.8 Z M12,7.4 L12,12 L15.2,14";

    // ---- weather (drawn, so every condition shows — some weather glyphs are missing from the system icon font)
    public const string CloudSun = "M5.3,10.6 A3.6,3.6 0 0 1 11.6,6.7 M8.6,2.4 L8.6,3.8 M2.4,8.6 L3.8,8.6 M4.2,4.2 L5.2,5.2 M13,4.2 L12,5.2 M9.4,20 A3.1,3.1 0 0 1 9.1,13.9 A4.7,4.7 0 0 1 18,12.6 A3.7,3.7 0 0 1 17.8,20 Z";
    public const string Cloud = "M7.2,18.2 A3.6,3.6 0 0 1 6.9,11 A5.4,5.4 0 0 1 17.2,9.6 A4.3,4.3 0 0 1 17,18.2 Z";
    public const string Rain = "M7.2,14.6 A3.6,3.6 0 0 1 6.9,7.4 A5.4,5.4 0 0 1 17.2,6 A4.3,4.3 0 0 1 17,14.6 Z M8.6,17.4 L7.6,20.4 M12.6,17.4 L11.6,20.4 M16.6,17.4 L15.6,20.4";
    public const string Storm = "M7.2,14.6 A3.6,3.6 0 0 1 6.9,7.4 A5.4,5.4 0 0 1 17.2,6 A4.3,4.3 0 0 1 17,14.6 Z M13.2,16 L10.8,19.2 L13.6,19.2 L11.6,22.2";
    public const string Snow = "M7.2,14.6 A3.6,3.6 0 0 1 6.9,7.4 A5.4,5.4 0 0 1 17.2,6 A4.3,4.3 0 0 1 17,14.6 Z M8,18.4 A0.4,0.4 0 1 1 7.99,18.4 Z M12,20.2 A0.4,0.4 0 1 1 11.99,20.2 Z M16,18.4 A0.4,0.4 0 1 1 15.99,18.4 Z";
    public const string Fog = "M3.6,8 L16.4,8 M6.6,12 L20.4,12 M3.6,16 L16.4,16 M8.6,20 L18.4,20";

    public const string Grid ="M4.2,4.2 L10.4,4.2 L10.4,10.4 L4.2,10.4 Z M13.6,4.2 L19.8,4.2 L19.8,10.4 L13.6,10.4 Z M4.2,13.6 L10.4,13.6 L10.4,19.8 L4.2,19.8 Z M13.6,13.6 L19.8,13.6 L19.8,19.8 L13.6,19.8 Z";
}
