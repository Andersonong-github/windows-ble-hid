namespace BleHid.Core;

/// <summary>Maps Windows virtual-key codes to HID keyboard usage IDs.</summary>
public static class VirtualKeyMap
{
    /// <summary>
    /// Resolves a key from its physical identity rather than the virtual-key code Windows
    /// derived from it. A real keyboard has no Num Lock state of its own: it always reports the
    /// physical key and lets the host apply its own Num Lock. Windows resolves Num Lock for us
    /// first, so without this the local machine would silently decide what the remote receives.
    /// </summary>
    public static bool TryGetUsage(int virtualKey, uint scanCode, bool extended, out byte usage)
    {
        if (TryGetNumpadUsage(scanCode, extended, out usage)) return true;

        // Keypad Enter shares VK_RETURN with the main Enter; only the extended flag tells them
        // apart, and some hosts bind them separately.
        if (virtualKey == 0x0D && extended)
        {
            usage = 0x58; // Keypad Enter
            return true;
        }

        // Windows normally resolves the key for us, but not always: an active IME can hand the
        // hook VK_PROCESSKEY (0xE5) and some layouts/injection paths report 0. Falling straight
        // through leaves those keys dead on the host, which reads as "u and o sometimes do not
        // work". The scan code still identifies the physical key, so it is the fallback.
        if (virtualKey is not (0 or 0xE5) && TryGetUsage(virtualKey, out usage)) return true;

        if (scanCode != 0 && TryGetScanCodeUsage(scanCode, extended, out usage)) return true;

        usage = 0;
        return false;
    }

    /// <summary>
    /// Maps a Scan Code Set 1 make code to the key's HID usage. This describes the physical
    /// key, independent of the layout Windows currently has loaded and of any IME that consumed
    /// the virtual key.
    /// </summary>
    internal static bool TryGetScanCodeUsage(uint scanCode, bool extended, out byte usage)
    {
        // Keys whose navigation-cluster doubles differ only by the E0 prefix.
        if (extended)
        {
            usage = scanCode switch
            {
                0x1C => (byte)0x58, // Keypad Enter
                0x35 => (byte)0x54, // Keypad /
                0x47 => (byte)0x4A, // Home
                0x48 => (byte)0x52, // Up
                0x49 => (byte)0x4B, // Page Up
                0x4B => (byte)0x50, // Left
                0x4D => (byte)0x4F, // Right
                0x4F => (byte)0x4D, // End
                0x50 => (byte)0x51, // Down
                0x51 => (byte)0x4E, // Page Down
                0x52 => (byte)0x49, // Insert
                0x53 => (byte)0x4C, // Delete
                // Left/Right GUI are carried in the modifier byte, not the 6-slot array, whose
                // declared logical maximum is 101. They are resolved from the virtual key.
                0x5D => (byte)0x65, // Menu
                _ => (byte)0
            };
            return usage != 0;
        }

        usage = scanCode switch
        {
            0x01 => (byte)0x29, // Escape
            0x02 => (byte)0x1E, // 1
            0x03 => (byte)0x1F, // 2
            0x04 => (byte)0x20, // 3
            0x05 => (byte)0x21, // 4
            0x06 => (byte)0x22, // 5
            0x07 => (byte)0x23, // 6
            0x08 => (byte)0x24, // 7
            0x09 => (byte)0x25, // 8
            0x0A => (byte)0x26, // 9
            0x0B => (byte)0x27, // 0
            0x0C => (byte)0x2D, // -
            0x0D => (byte)0x2E, // =
            0x0E => (byte)0x2A, // Backspace
            0x0F => (byte)0x2B, // Tab
            0x10 => (byte)0x14, // Q
            0x11 => (byte)0x1A, // W
            0x12 => (byte)0x08, // E
            0x13 => (byte)0x15, // R
            0x14 => (byte)0x17, // T
            0x15 => (byte)0x1C, // Y
            0x16 => (byte)0x18, // U
            0x17 => (byte)0x0C, // I
            0x18 => (byte)0x12, // O
            0x19 => (byte)0x13, // P
            0x1A => (byte)0x2F, // [
            0x1B => (byte)0x30, // ]
            0x1C => (byte)0x28, // Enter
            0x1E => (byte)0x04, // A
            0x1F => (byte)0x16, // S
            0x20 => (byte)0x07, // D
            0x21 => (byte)0x09, // F
            0x22 => (byte)0x0A, // G
            0x23 => (byte)0x0B, // H
            0x24 => (byte)0x0D, // J
            0x25 => (byte)0x0E, // K
            0x26 => (byte)0x0F, // L
            0x27 => (byte)0x33, // ;
            0x28 => (byte)0x34, // '
            0x29 => (byte)0x35, // `
            0x2B => (byte)0x31, // \
            0x2C => (byte)0x1D, // Z
            0x2D => (byte)0x1B, // X
            0x2E => (byte)0x06, // C
            0x2F => (byte)0x19, // V
            0x30 => (byte)0x05, // B
            0x31 => (byte)0x11, // N
            0x32 => (byte)0x10, // M
            0x33 => (byte)0x36, // ,
            0x34 => (byte)0x37, // .
            0x35 => (byte)0x38, // /
            0x37 => (byte)0x55, // Keypad *
            0x39 => (byte)0x2C, // Space
            0x3A => (byte)0x39, // Caps Lock
            0x3B => (byte)0x3A, // F1
            0x3C => (byte)0x3B, // F2
            0x3D => (byte)0x3C, // F3
            0x3E => (byte)0x3D, // F4
            0x3F => (byte)0x3E, // F5
            0x40 => (byte)0x3F, // F6
            0x41 => (byte)0x40, // F7
            0x42 => (byte)0x41, // F8
            0x43 => (byte)0x42, // F9
            0x44 => (byte)0x43, // F10
            0x45 => (byte)0x53, // Num Lock
            0x46 => (byte)0x47, // Scroll Lock
            0x57 => (byte)0x44, // F11
            0x58 => (byte)0x45, // F12
            _ => (byte)0
        };
        return usage != 0;
    }

    /// <summary>
    /// The numpad keys whose virtual-key code changes with Num Lock. The navigation cluster
    /// shares their scan codes and is told apart only by the extended flag, so an extended key
    /// here is Home/End/arrows and never the numpad.
    /// </summary>
    private static bool TryGetNumpadUsage(uint scanCode, bool extended, out byte usage)
    {
        usage = 0;
        if (extended) return false;

        usage = scanCode switch
        {
            0x47 => (byte)0x5F, // Keypad 7
            0x48 => (byte)0x60, // Keypad 8
            0x49 => (byte)0x61, // Keypad 9
            0x4B => (byte)0x5C, // Keypad 4
            0x4C => (byte)0x5D, // Keypad 5
            0x4D => (byte)0x5E, // Keypad 6
            0x4F => (byte)0x59, // Keypad 1
            0x50 => (byte)0x5A, // Keypad 2
            0x51 => (byte)0x5B, // Keypad 3
            0x52 => (byte)0x62, // Keypad 0
            0x53 => (byte)0x63, // Keypad .
            _ => (byte)0
        };
        return usage != 0;
    }

    public static bool TryGetUsage(int virtualKey, out byte usage)
    {
        switch (virtualKey)
        {
            case >= 0x41 and <= 0x5A: // A-Z
                usage = (byte)(0x04 + (virtualKey - 0x41));
                return true;
            case >= 0x31 and <= 0x39: // 1-9
                usage = (byte)(0x1E + (virtualKey - 0x31));
                return true;
            case 0x30: usage = 0x27; return true; // 0
            case >= 0x70 and <= 0x7B: // F1-F12
                usage = (byte)(0x3A + (virtualKey - 0x70));
                return true;
            case >= 0x61 and <= 0x69: // Numpad 1-9 (Windows sends these only with Num Lock on)
                usage = (byte)(0x59 + (virtualKey - 0x61));
                return true;
        }

        usage = virtualKey switch
        {
            0x0D => 0x28, // Enter
            0x1B => 0x29, // Escape
            0x08 => 0x2A, // Backspace
            0x09 => 0x2B, // Tab
            0x20 => 0x2C, // Space
            0xBD => 0x2D, // -
            0xBB => 0x2E, // =
            0xDB => 0x2F, // [
            0xDD => 0x30, // ]
            0xDC => 0x31, // \
            0xBA => 0x33, // ;
            0xDE => 0x34, // '
            0xC0 => 0x35, // `
            0xBC => 0x36, // ,
            0xBE => 0x37, // .
            0xBF => 0x38, // /
            0x14 => 0x39, // Caps Lock
            0x2C => 0x46, // Print Screen
            0x91 => 0x47, // Scroll Lock
            0x13 => 0x48, // Pause
            0x2D => 0x49, // Insert
            0x24 => 0x4A, // Home
            0x21 => 0x4B, // Page Up
            0x2E => 0x4C, // Delete
            0x23 => 0x4D, // End
            0x22 => 0x4E, // Page Down
            0x27 => 0x4F, // Right
            0x25 => 0x50, // Left
            0x28 => 0x51, // Down
            0x26 => 0x52, // Up
            0x90 => 0x53, // Num Lock
            0x6F => 0x54, // Numpad /
            0x6A => 0x55, // Numpad *
            0x6D => 0x56, // Numpad -
            0x6B => 0x57, // Numpad +
            0x60 => 0x62, // Numpad 0
            0x6E => 0x63, // Numpad .
            _ => 0
        };

        return usage != 0;
    }
}
