using System.Runtime.InteropServices;
using Microsoft.Win32;
using static AppDispatcher.Native;

namespace AppDispatcher;

internal sealed record PickerRow(string Label, string IconExe);

internal readonly record struct PickerResult(int Row, bool Always, string Domain);

/// <summary>
/// The picker. Hand-painted in one window because first paint is the product: icons load on
/// a background thread and fill in afterwards, so they can never delay it.
/// Keyboard: 1-9 open directly, arrows + Enter, Esc cancels. A click opens the row under it.
/// </summary>
internal static unsafe class PickerWindow
{
    const string ClassName = "AppDispatcherPicker";
    const int WidthDip = 460, RowDip = 30, PadDip = 12, HeaderDip = 46, FooterDip = 38, MaxVisibleRows = 14, IconDip = 18;
    const uint WM_ICONS_READY = WM_APP + 1;
    const uint WindowStyle = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU;
    const int GearDip = 16;

    /// <summary>True when the picker was dismissed through its gear button rather than cancelled.</summary>
    public static bool SettingsRequested { get; private set; }

    static string _title = "", _subtitle = "";
    static IReadOnlyList<PickerRow> _rows = Array.Empty<PickerRow>();
    static nint[] _icons = Array.Empty<nint>();
    static byte[] _gearCoverage = Array.Empty<byte>();
    static nint _hwnd, _edit, _font, _boldFont, _editBrush;
    static int _selected, _firstVisible, _pressedRow = -1;
    static uint _dpi = 96;
    static bool _showAlways, _always, _gearHot, _gearPressed;
    static PickerResult? _result;

    // COLORREF is 0x00BBGGRR.
    static uint _background, _text, _dimText, _accent, _accentText, _editBackground, _gearHover;

    /// <summary>Blocks until a row is chosen or the picker is dismissed (null).</summary>
    /// <param name="domain">When non-null, shows the "Always open [domain]" option used for links.</param>
    public static PickerResult? Show(string title, string subtitle, IReadOnlyList<PickerRow> rows, int selected, string? domain)
    {
        _title = title;
        _subtitle = subtitle;
        _rows = rows;
        _selected = Math.Clamp(selected, 0, rows.Count - 1);
        _firstVisible = Math.Max(0, _selected - MaxVisibleRows + 1);
        _icons = new nint[rows.Count];
        _showAlways = domain != null;
        ApplyTheme();

        fixed (char* name = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                Size = (uint)sizeof(WNDCLASSEXW),
                WndProc = &WndProc,
                ClassName = name,
                Cursor = LoadCursorW(0, IDC_ARROW),
            };
            RegisterClassExW(&wc);
        }

        POINT cursor;
        GetCursorPos(&cursor);
        // Created hidden at the cursor so GetDpiForWindow reports that monitor's DPI.
        _hwnd = CreateWindowExW(WS_EX_TOPMOST, ClassName, "App Dispatcher", WindowStyle, cursor.X, cursor.Y, 100, 100, 0, 0, 0, 0);
        _dpi = GetDpiForWindow(_hwnd);
        var dark = IsDark() ? 1 : 0;
        DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int));
        CreateFonts();

        if (domain != null)
        {
            _editBrush = CreateSolidBrush(_editBackground);
            _edit = CreateWindowExW(0, "EDIT", domain, WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL, 0, 0, 10, 10, _hwnd, 0, 0, 0);
            SendMessageW(_edit, WM_SETFONT, _font, 0);
        }

        PlaceAtCursor(cursor);
        LayoutEdit();
        // A process's first ShowWindow obeys its launcher's STARTUPINFO instead of the argument, and
        // command-line tools opening a link may ask for hidden. The second call is always honoured.
        ShowWindow(_hwnd, SW_SHOW);
        ShowWindow(_hwnd, SW_SHOW);
        UpdateWindow(_hwnd);
        SetForegroundWindow(_hwnd);
        SetFocus(_hwnd);
        LoadIconsInBackground();

        MSG msg;
        while (GetMessageW(&msg, 0, 0, 0) > 0)
        {
            if (msg.Message == WM_KEYDOWN && HandleKey((int)msg.WParam)) continue;
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        return _result;
    }

    static int Scale(int dip) => dip * (int)_dpi / 96;

    static int VisibleRows => Math.Min(_rows.Count, MaxVisibleRows);
    static int RowsTop => Scale(HeaderDip + PadDip);
    static int RowsBottom => RowsTop + VisibleRows * Scale(RowDip);
    static int ClientHeight => RowsBottom + (_showAlways ? Scale(FooterDip) : 0) + Scale(PadDip);
    static int ClientWidth => Scale(WidthDip);

    static (int Width, int Height) WindowSize()
    {
        var r = new RECT(0, 0, ClientWidth, ClientHeight);
        AdjustWindowRectExForDpi(&r, WindowStyle, 0, WS_EX_TOPMOST, _dpi);
        return (r.Right - r.Left, r.Bottom - r.Top);
    }

    /// <summary>
    /// Opens with the caption under the cursor: short mouse travel, and the tail of the
    /// double-click that launched us can't land on a row.
    /// </summary>
    static void PlaceAtCursor(POINT cursor)
    {
        const int SM_CYCAPTION = 4;
        var (w, h) = WindowSize();
        var mon = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { Size = (uint)sizeof(MONITORINFO) };
        GetMonitorInfoW(mon, &info);

        var x = Math.Clamp(cursor.X - w / 2, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - w));
        var y = cursor.Y - GetSystemMetricsForDpi(SM_CYCAPTION, _dpi) / 2 - Scale(4);
        y = Math.Clamp(y, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - h));
        SetWindowPos(_hwnd, 0, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    static bool IsDark()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("AppsUseLightTheme") is 0;
    }

    static void ApplyTheme()
    {
        var dark = IsDark();
        _background = dark ? 0x202020u : 0xFBFBFBu;
        _text = dark ? 0xF0F0F0u : 0x1A1A1Au;
        _dimText = dark ? 0xA0A0A0u : 0x6E6E6Eu;
        _accent = dark ? 0xA8622Bu : 0xD47800u; // BGR: blue
        _accentText = 0xFFFFFFu;
        _editBackground = dark ? 0x2D2D2Du : 0xFFFFFFu;
        _gearHover = dark ? 0x3A3A3Au : 0xE6E6E6u;
    }

    static void CreateFonts()
    {
        const uint DefaultCharset = 1, ClearType = 5;
        const int Normal = 400, SemiBold = 600;
        var height = -9 * (int)_dpi / 72;
        if (_font != 0) DeleteObject(_font);
        if (_boldFont != 0) DeleteObject(_boldFont);
        _font = CreateFontW(height, 0, 0, 0, Normal, 0, 0, 0, DefaultCharset, 0, 0, ClearType, 0, "Segoe UI");
        _boldFont = CreateFontW(height, 0, 0, 0, SemiBold, 0, 0, 0, DefaultCharset, 0, 0, ClearType, 0, "Segoe UI");
    }

    static void LoadIconsInBackground()
    {
        var size = Scale(IconDip);
        var thread = new Thread(() =>
        {
            SetWindowIcon(ICON_SMALL, GetSystemMetricsForDpi(SM_CXSMICON, _dpi));
            SetWindowIcon(ICON_BIG, GetSystemMetricsForDpi(SM_CXICON, _dpi));
            var byExe = new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < _rows.Count; i++)
            {
                var exe = _rows[i].IconExe;
                if (!byExe.TryGetValue(exe, out var icon))
                {
                    uint id;
                    PrivateExtractIconsW(exe, 0, size, size, &icon, &id, 1, 0);
                    byExe[exe] = icon;
                }
                _icons[i] = icon;
            }
            PostMessageW(_hwnd, WM_ICONS_READY, 0, 0);
        }) { IsBackground = true };
        thread.Start();
    }

    static void SetWindowIcon(int kind, int size)
    {
        nint icon;
        uint id;
        if (Environment.ProcessPath is { } self && PrivateExtractIconsW(self, 0, size, size, &icon, &id, 1, 0) == 1 && icon != 0)
            PostMessageW(_hwnd, WM_SETICON, kind, icon);
    }

    static RECT GearRect => new(ClientWidth - Scale(36), Scale(10), ClientWidth - Scale(8), Scale(38));

    /// <summary>
    /// How much of each pixel a gear covers, 0-255. Computed rather than taken from a symbol font,
    /// because first paint shouldn't have to wait for a font to load, and plain GDI shapes have jagged edges.
    /// </summary>
    static byte[] ComputeGearCoverage(int size)
    {
        const int Samples = 4, Teeth = 8;
        const float Hole = 0.36f, Body = 0.72f, ToothWidth = 0.2f;
        var coverage = new byte[size * size];
        var centre = size / 2f;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var hits = 0;
            for (var sy = 0; sy < Samples; sy++)
            for (var sx = 0; sx < Samples; sx++)
            {
                var dx = (x + (sx + 0.5f) / Samples - centre) / centre;
                var dy = (y + (sy + 0.5f) / Samples - centre) / centre;
                var r = MathF.Sqrt(dx * dx + dy * dy);
                if (r < Hole || r > 1) continue;
                if (r <= Body || MathF.Cos(Teeth * MathF.Atan2(dy, dx)) > ToothWidth) hits++;
            }
            coverage[y * size + x] = (byte)(hits * 255 / (Samples * Samples));
        }
        return coverage;
    }

    static void PaintGear(nint dc, RECT gear, uint under, uint color)
    {
        var size = Scale(GearDip);
        if (_gearCoverage.Length != size * size) _gearCoverage = ComputeGearCoverage(size);

        var pixels = new uint[size * size];
        for (var i = 0; i < pixels.Length; i++)
        {
            var a = (uint)_gearCoverage[i];
            // COLORREF channels are blended in place, then swapped because a DIB pixel is 0x00RRGGBB.
            uint Mix(int shift) => (((under >> shift) & 0xFF) * (255 - a) + ((color >> shift) & 0xFF) * a) / 255;
            pixels[i] = (Mix(0) << 16) | (Mix(8) << 8) | Mix(16);
        }

        // A negative height makes the bitmap top-down.
        var info = new BITMAPINFOHEADER { Size = (uint)sizeof(BITMAPINFOHEADER), Width = size, Height = -size, Planes = 1, BitCount = 32 };
        var x = gear.Left + (gear.Right - gear.Left - size) / 2;
        var y = gear.Top + (gear.Bottom - gear.Top - size) / 2;
        fixed (uint* bits = pixels)
            SetDIBitsToDevice(dc, x, y, (uint)size, (uint)size, 0, 0, 0, (uint)size, bits, &info, 0);
    }

    static void SetGearHot(bool hot)
    {
        if (hot == _gearHot) return;
        _gearHot = hot;
        if (hot)
        {
            // Without this, leaving the window directly from the gear would leave it highlighted.
            var track = new TRACKMOUSEEVENT { Size = (uint)sizeof(TRACKMOUSEEVENT), Flags = TME_LEAVE, Track = _hwnd };
            TrackMouseEvent(&track);
        }
        InvalidateRect(_hwnd, 0, 0);
    }

    // Footer geometry: [x] Always open [ domain edit ] this way
    static RECT CheckRect => new(Scale(PadDip), RowsBottom + Scale(10), Scale(PadDip + 100), RowsBottom + Scale(34));

    static void LayoutEdit()
    {
        if (_edit == 0) return;
        SetWindowPos(_edit, 0, Scale(PadDip + 104), RowsBottom + Scale(10), Scale(WidthDip - PadDip * 2 - 104 - 64), Scale(24), SWP_NOZORDER | SWP_NOACTIVATE);
        SendMessageW(_edit, WM_SETFONT, _font, 1);
    }

    static int RowAt(int x, int y)
    {
        if (y < RowsTop || y >= RowsBottom || x < 0 || x >= ClientWidth) return -1;
        var row = _firstVisible + (y - RowsTop) / Scale(RowDip);
        return row < _rows.Count ? row : -1;
    }

    static void Select(int row)
    {
        row = Math.Clamp(row, 0, _rows.Count - 1);
        if (row < _firstVisible) _firstVisible = row;
        else if (row >= _firstVisible + VisibleRows) _firstVisible = row - VisibleRows + 1;
        if (row == _selected) return;
        _selected = row;
        InvalidateRect(_hwnd, 0, 0);
    }

    static void Choose(int row)
    {
        var domain = "";
        if (_edit != 0)
        {
            var length = GetWindowTextLengthW(_edit);
            var buffer = stackalloc char[length + 1];
            GetWindowTextW(_edit, buffer, length + 1);
            domain = new string(buffer, 0, length);
        }
        _result = new PickerResult(row, _always, domain);
        DestroyWindow(_hwnd);
    }

    /// <summary>Handled in the message loop so that it also works while the domain box has focus.</summary>
    static bool HandleKey(int key)
    {
        var inEdit = _edit != 0 && GetFocus() == _edit;
        switch (key)
        {
            case VK_ESCAPE: DestroyWindow(_hwnd); return true;
            case VK_RETURN: Choose(_selected); return true;
            case VK_UP: Select(_selected - 1); return true;
            case VK_DOWN: Select(_selected + 1); return true;
            case VK_TAB when _edit != 0: SetFocus(inEdit ? _hwnd : _edit); return true;
        }
        if (inEdit) return false;
        switch (key)
        {
            case VK_HOME: Select(0); return true;
            case VK_END: Select(_rows.Count - 1); return true;
            case VK_SPACE when _showAlways: ToggleAlways(); return true;
            case >= VK_1 and <= VK_9: return ChooseByNumber(key - VK_1);
            case >= VK_NUMPAD1 and <= VK_NUMPAD9: return ChooseByNumber(key - VK_NUMPAD1);
        }
        return false;
    }

    static bool ChooseByNumber(int offset)
    {
        var row = _firstVisible + offset;
        if (row < _rows.Count) Choose(row);
        return true;
    }

    static void ToggleAlways()
    {
        _always = !_always;
        InvalidateRect(_hwnd, 0, 0);
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nint w, nint l)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                return 1; // Painted in full, off-screen, in WM_PAINT.
            case WM_PAINT:
                Paint(hwnd);
                return 0;
            case WM_MOUSEMOVE:
            {
                var (x, y) = (LowWord(l), HighWord(l));
                SetGearHot(GearRect.Contains(x, y));
                var row = RowAt(x, y);
                if (row >= 0) Select(row);
                return 0;
            }
            case WM_MOUSELEAVE:
                SetGearHot(false);
                return 0;
            case WM_LBUTTONDOWN:
            {
                var (x, y) = (LowWord(l), HighWord(l));
                _pressedRow = RowAt(x, y);
                _gearPressed = GearRect.Contains(x, y);
                SetFocus(hwnd);
                if (_showAlways && CheckRect.Contains(x, y)) ToggleAlways();
                return 0;
            }
            case WM_LBUTTONUP:
            {
                // Needs a matching press here: a release alone can be the end of the launching double-click.
                var (x, y) = (LowWord(l), HighWord(l));
                var row = RowAt(x, y);
                if (row >= 0 && row == _pressedRow) Choose(row);
                else if (_gearPressed && GearRect.Contains(x, y))
                {
                    SettingsRequested = true;
                    DestroyWindow(hwnd);
                }
                _pressedRow = -1;
                _gearPressed = false;
                return 0;
            }
            case WM_MOUSEWHEEL:
                if (_rows.Count > MaxVisibleRows)
                {
                    var delta = HighWord(w) > 0 ? -1 : 1;
                    _firstVisible = Math.Clamp(_firstVisible + delta, 0, _rows.Count - MaxVisibleRows);
                    InvalidateRect(hwnd, 0, 0);
                }
                return 0;
            case WM_ICONS_READY:
                InvalidateRect(hwnd, 0, 0);
                return 0;
            case WM_CTLCOLOREDIT:
                SetTextColor(w, _text);
                SetBkColor(w, _editBackground);
                return _editBrush;
            case WM_DPICHANGED:
            {
                _dpi = (uint)LowWord(w);
                CreateFonts();
                var suggested = (RECT*)l;
                SetWindowPos(hwnd, 0, suggested->Left, suggested->Top, suggested->Right - suggested->Left, suggested->Bottom - suggested->Top, SWP_NOZORDER | SWP_NOACTIVATE);
                var (width, height) = WindowSize();
                SetWindowPos(hwnd, 0, suggested->Left, suggested->Top, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
                LayoutEdit();
                InvalidateRect(hwnd, 0, 0);
                return 0;
            }
            case WM_DESTROY:
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, w, l);
    }

    static int LowWord(nint v) => (short)(v & 0xFFFF);
    static int HighWord(nint v) => (short)((v >> 16) & 0xFFFF);

    static void Paint(nint hwnd)
    {
        PAINTSTRUCT ps;
        var screen = BeginPaint(hwnd, &ps);
        RECT client;
        GetClientRect(hwnd, &client);

        var dc = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, client.Right, client.Bottom);
        var oldBitmap = SelectObject(dc, bitmap);
        var oldFont = SelectObject(dc, _font);
        SetBkMode(dc, TRANSPARENT);

        var background = CreateSolidBrush(_background);
        FillRect(dc, &client, background);
        DeleteObject(background);

        var pad = Scale(PadDip);
        var gear = GearRect;
        var headerRight = gear.Left - Scale(6);
        SelectObject(dc, _boldFont);
        DrawLine(dc, _title, new RECT(pad, Scale(8), headerRight, Scale(30)), _text, DT_END_ELLIPSIS);
        SelectObject(dc, _font);
        DrawLine(dc, _subtitle, new RECT(pad, Scale(28), headerRight, Scale(50)), _dimText, DT_PATH_ELLIPSIS);

        if (_gearHot)
        {
            var hover = CreateSolidBrush(_gearHover);
            FillRect(dc, &gear, hover);
            DeleteObject(hover);
        }
        PaintGear(dc, gear, _gearHot ? _gearHover : _background, _gearHot ? _text : _dimText);

        var accent = CreateSolidBrush(_accent);
        var rowHeight = Scale(RowDip);
        var iconSize = Scale(IconDip);
        for (var slot = 0; slot < VisibleRows; slot++)
        {
            var i = _firstVisible + slot;
            var top = RowsTop + slot * rowHeight;
            var row = new RECT(Scale(6), top, client.Right - Scale(6), top + rowHeight);
            var isSelected = i == _selected;
            if (isSelected) FillRect(dc, &row, accent);
            if (_icons[i] != 0)
                DrawIconEx(dc, pad, top + (rowHeight - iconSize) / 2, _icons[i], iconSize, iconSize, 0, 0, DI_NORMAL);

            var labelLeft = pad + iconSize + Scale(10);
            DrawLine(dc, _rows[i].Label, new RECT(labelLeft, top, client.Right - Scale(36), top + rowHeight), isSelected ? _accentText : _text, DT_END_ELLIPSIS);
            if (slot < 9)
                DrawLine(dc, (slot + 1).ToString(), new RECT(client.Right - Scale(36), top, client.Right - pad, top + rowHeight), isSelected ? _accentText : _dimText, DT_RIGHT);
        }
        DeleteObject(accent);

        if (_showAlways) PaintFooter(dc, client);

        BitBlt(screen, 0, 0, client.Right, client.Bottom, dc, 0, 0, SRCCOPY);
        SelectObject(dc, oldFont);
        SelectObject(dc, oldBitmap);
        DeleteObject(bitmap);
        DeleteDC(dc);
        EndPaint(hwnd, &ps);
    }

    static void PaintFooter(nint dc, RECT client)
    {
        var check = CheckRect;
        var box = Scale(14);
        var boxTop = check.Top + (check.Bottom - check.Top - box) / 2;
        var outer = new RECT(check.Left, boxTop, check.Left + box, boxTop + box);
        var brush = CreateSolidBrush(_always ? _accent : _dimText);
        FillRect(dc, &outer, brush);
        DeleteObject(brush);
        if (!_always)
        {
            var inner = new RECT(outer.Left + 1, outer.Top + 1, outer.Right - 1, outer.Bottom - 1);
            var fill = CreateSolidBrush(_background);
            FillRect(dc, &inner, fill);
            DeleteObject(fill);
        }
        DrawLine(dc, "Always open", new RECT(check.Left + box + Scale(6), check.Top, check.Right, check.Bottom), _text, 0);
        DrawLine(dc, "this way", new RECT(client.Right - Scale(PadDip + 60), check.Top, client.Right - Scale(PadDip), check.Bottom), _dimText, DT_RIGHT);
    }

    static void DrawLine(nint dc, string text, RECT r, uint color, uint extra)
    {
        SetTextColor(dc, color);
        DrawTextW(dc, text, -1, &r, DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX | extra);
    }
}
