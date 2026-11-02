// v0.11 App · FrameTokens: single source of frame/chrome thickness tokens (batch2 #11)
namespace Decanter.App
{
    public static class FrameTokens
    {
        public const int Outer = 1;      // outer border stroke
        public const int Inner = 1;      // inner hairline (double rule look)
        public const int Corner = 2;     // 2px L-shaped corner mark
        public const int Focus = 2;      // keyboard focus ring thickness
        public const int FocusInset = 2; // focus ring inset from the control edge
        public const int MenuPad = 6;    // popup menu inner padding
    }
}
