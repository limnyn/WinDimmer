namespace WinDimmer;

public static class AlphaMath
{
    public const byte Default = 110;
    public const int Step = 10;

    public static byte Adjust(byte current, int delta) =>
        (byte)Math.Clamp(current + delta, 0, 255);
}
