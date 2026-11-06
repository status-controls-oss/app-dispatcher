using System.Runtime.InteropServices;

namespace AppDispatcher;

/// <summary>The Win32 surface the picker uses. Only user32, gdi32 and dwmapi, to keep load time down.</summary>
internal static unsafe partial class Native
{
    public const uint WS_OVERLAPPED = 0, WS_CAPTION = 0xC00000, WS_SYSMENU = 0x80000, WS_CHILD = 0x40000000,
        WS_VISIBLE = 0x10000000, WS_BORDER = 0x800000, WS_EX_TOPMOST = 8, ES_AUTOHSCROLL = 0x80;

    public const uint WM_DESTROY = 0x2, WM_PAINT = 0xF, WM_CLOSE = 0x10, WM_ERASEBKGND = 0x14, WM_SETFONT = 0x30, WM_SETICON = 0x80,
        WM_KEYDOWN = 0x100, WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_MOUSEWHEEL = 0x20A,
        WM_CTLCOLOREDIT = 0x133, WM_MOUSELEAVE = 0x2A3, WM_DPICHANGED = 0x2E0, WM_APP = 0x8000;

    public const int VK_TAB = 0x09, VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_END = 0x23, VK_HOME = 0x24,
        VK_UP = 0x26, VK_DOWN = 0x28, VK_1 = 0x31, VK_9 = 0x39, VK_NUMPAD1 = 0x61, VK_NUMPAD9 = 0x69;

    public const uint DT_RIGHT = 2, DT_VCENTER = 4, DT_SINGLELINE = 0x20, DT_NOPREFIX = 0x800,
        DT_PATH_ELLIPSIS = 0x4000, DT_END_ELLIPSIS = 0x8000;

    public const uint SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SRCCOPY = 0xCC0020, MB_OK = 0, MB_ICONWARNING = 0x30,
        MB_ICONERROR = 0x10, MB_YESNO = 4, MB_TOPMOST = 0x40000, MONITOR_DEFAULTTONEAREST = 2, DI_NORMAL = 3, TME_LEAVE = 2;

    public const int SW_SHOW = 5, IDYES = 6, IDC_ARROW = 32512, TRANSPARENT = 1, DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
        ICON_SMALL = 0, ICON_BIG = 1, SM_CXICON = 11, SM_CXSMICON = 49;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
        public readonly bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public nint Hwnd; public uint Message; public nint WParam, LParam; public uint Time; public POINT Pt; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint Size, Style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> WndProc;
        public int ClsExtra, WndExtra;
        public nint Instance, Icon, Cursor, Background;
        public char* MenuName, ClassName;
        public nint IconSmall;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public nint Hdc;
        public int Erase;
        public RECT Paint;
        public int Restore, IncUpdate;
        public fixed byte Reserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public uint Size; public RECT Monitor, Work; public uint Flags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT { public uint Size, Flags; public nint Track; public uint HoverTime; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }

    [LibraryImport("user32")] public static partial ushort RegisterClassExW(WNDCLASSEXW* wc);
    [LibraryImport("user32", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [LibraryImport("user32")] public static partial nint DefWindowProcW(nint h, uint m, nint w, nint l);
    [LibraryImport("user32")] public static partial int DestroyWindow(nint h);
    [LibraryImport("user32")] public static partial int GetMessageW(MSG* m, nint h, uint min, uint max);
    [LibraryImport("user32")] public static partial int TranslateMessage(MSG* m);
    [LibraryImport("user32")] public static partial nint DispatchMessageW(MSG* m);
    [LibraryImport("user32")] public static partial void PostQuitMessage(int code);
    [LibraryImport("user32")] public static partial int PostMessageW(nint h, uint m, nint w, nint l);
    [LibraryImport("user32")] public static partial nint SendMessageW(nint h, uint m, nint w, nint l);
    [LibraryImport("user32")] public static partial int ShowWindow(nint h, int cmd);
    [LibraryImport("user32")] public static partial int UpdateWindow(nint h);
    [LibraryImport("user32")] public static partial int SetForegroundWindow(nint h);
    [LibraryImport("user32")] public static partial nint SetFocus(nint h);
    [LibraryImport("user32")] public static partial nint GetFocus();
    [LibraryImport("user32")] public static partial int SetWindowPos(nint h, nint after, int x, int y, int w, int t, uint flags);
    [LibraryImport("user32")] public static partial int InvalidateRect(nint h, nint rect, int erase);
    [LibraryImport("user32")] public static partial int GetClientRect(nint h, RECT* r);
    [LibraryImport("user32")] public static partial uint GetDpiForWindow(nint h);
    [LibraryImport("user32")] public static partial int GetCursorPos(POINT* p);
    [LibraryImport("user32")] public static partial int TrackMouseEvent(TRACKMOUSEEVENT* e);
    [LibraryImport("user32")] public static partial nint LoadCursorW(nint inst, nint name);
    [LibraryImport("user32")] public static partial nint MonitorFromPoint(POINT p, uint flags);
    [LibraryImport("user32")] public static partial int GetMonitorInfoW(nint mon, MONITORINFO* info);
    [LibraryImport("user32")] public static partial int AdjustWindowRectExForDpi(RECT* r, uint style, int menu, uint exStyle, uint dpi);
    [LibraryImport("user32")] public static partial int GetSystemMetricsForDpi(int index, uint dpi);
    [LibraryImport("user32", StringMarshalling = StringMarshalling.Utf16)] public static partial int GetWindowTextW(nint h, char* text, int max);
    [LibraryImport("user32")] public static partial int GetWindowTextLengthW(nint h);
    [LibraryImport("user32", StringMarshalling = StringMarshalling.Utf16)] public static partial int MessageBoxW(nint h, string text, string caption, uint type);
    [LibraryImport("user32", StringMarshalling = StringMarshalling.Utf16)] public static partial int DrawTextW(nint dc, string text, int len, RECT* r, uint fmt);
    [LibraryImport("user32")] public static partial int FillRect(nint dc, RECT* r, nint brush);
    [LibraryImport("user32")] public static partial int DrawIconEx(nint dc, int x, int y, nint icon, int cx, int cy, uint step, nint flicker, uint flags);
    [LibraryImport("user32", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PrivateExtractIconsW(string file, int index, int cx, int cy, nint* icons, uint* ids, uint count, uint flags);

    [LibraryImport("user32")] public static partial nint BeginPaint(nint h, PAINTSTRUCT* ps);
    [LibraryImport("user32")] public static partial int EndPaint(nint h, PAINTSTRUCT* ps);

    [LibraryImport("gdi32")] public static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32")] public static partial nint CreateCompatibleBitmap(nint dc, int w, int h);
    [LibraryImport("gdi32")] public static partial nint SelectObject(nint dc, nint o);
    [LibraryImport("gdi32")] public static partial int DeleteObject(nint o);
    [LibraryImport("gdi32")] public static partial int DeleteDC(nint dc);
    [LibraryImport("gdi32")] public static partial int BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
    [LibraryImport("gdi32")] public static partial int SetDIBitsToDevice(nint dc, int x, int y, uint w, uint h, int sx, int sy, uint start, uint lines, uint* bits, BITMAPINFOHEADER* info, uint usage);
    [LibraryImport("gdi32")] public static partial nint CreateSolidBrush(uint color);
    [LibraryImport("gdi32")] public static partial uint SetTextColor(nint dc, uint color);
    [LibraryImport("gdi32")] public static partial uint SetBkColor(nint dc, uint color);
    [LibraryImport("gdi32")] public static partial int SetBkMode(nint dc, int mode);
    [LibraryImport("gdi32", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFontW(int height, int width, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);

    [LibraryImport("dwmapi")] public static partial int DwmSetWindowAttribute(nint h, int attr, int* value, int size);
}
