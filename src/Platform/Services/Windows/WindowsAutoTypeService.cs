using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PhantomVault.Core.Services.Platform.Windows
{
    /// <summary>
    /// Windows implementation using SendInput API for realistic keyboard simulation
    /// </summary>
    public class WindowsAutoTypeService : IAutoTypeService
    {
        #region Win32 Interop

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint Type;
            public INPUTUNION Union;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT Mouse;
            [FieldOffset(0)] public KEYBDINPUT Keyboard;
            [FieldOffset(0)] public HARDWAREINPUT Hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint Msg;
            public ushort ParamL;
            public ushort ParamH;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        // Virtual key codes
        private const ushort VK_TAB = 0x09;
        private const ushort VK_RETURN = 0x0D;
        private const ushort VK_ESCAPE = 0x1B;
        private const ushort VK_BACK = 0x08;
        private const ushort VK_DELETE = 0x2E;
        private const ushort VK_UP = 0x26;
        private const ushort VK_DOWN = 0x28;
        private const ushort VK_LEFT = 0x25;
        private const ushort VK_RIGHT = 0x27;

        #endregion

        public async Task TypeCredentialsAsync(string username, string password, bool submit = false)
        {
            if (string.IsNullOrEmpty(username) && string.IsNullOrEmpty(password))
                return;

            // Standard sequence: username → tab → password → (optional enter)
            if (!string.IsNullOrEmpty(username))
            {
                await TypeTextAsync(username);
            }

            await PressKeyAsync(SpecialKey.Tab);
            await Task.Delay(100); // Wait for tab to process

            if (!string.IsNullOrEmpty(password))
            {
                await TypeTextAsync(password);
            }

            if (submit)
            {
                await Task.Delay(200); // Brief pause before submit
                await PressKeyAsync(SpecialKey.Enter);
            }
        }

        public async Task TypeCustomSequenceAsync(string sequence, string username, string password)
        {
            // Parse and execute custom sequence
            // Supports: {username}, {password}, {tab}, {enter}, {delay:ms}
            var pattern = @"\{([^}]+)\}";
            var matches = Regex.Matches(sequence, pattern);

            int lastIndex = 0;
            foreach (Match match in matches)
            {
                // Type literal text before this command
                if (match.Index > lastIndex)
                {
                    var literal = sequence.Substring(lastIndex, match.Index - lastIndex);
                    await TypeTextAsync(literal);
                }

                // Execute command
                var command = match.Groups[1].Value.ToLowerInvariant();

                if (command == "username")
                {
                    await TypeTextAsync(username);
                }
                else if (command == "password")
                {
                    await TypeTextAsync(password);
                }
                else if (command == "tab")
                {
                    await PressKeyAsync(SpecialKey.Tab);
                }
                else if (command == "enter")
                {
                    await PressKeyAsync(SpecialKey.Enter);
                }
                else if (command.StartsWith("delay:"))
                {
                    if (int.TryParse(command.Substring(6), out int delayMs))
                    {
                        await Task.Delay(delayMs);
                    }
                }

                lastIndex = match.Index + match.Length;
            }

            // Type any remaining literal text
            if (lastIndex < sequence.Length)
            {
                var remaining = sequence.Substring(lastIndex);
                await TypeTextAsync(remaining);
            }
        }

        public async Task TypeTextAsync(string text, int delayMs = 10)
        {
            if (string.IsNullOrEmpty(text))
                return;

            foreach (char c in text)
            {
                SendChar(c);
                await Task.Delay(delayMs); // Realistic typing speed
            }
        }

        public Task PressKeyAsync(SpecialKey key)
        {
            ushort vkCode = key switch
            {
                SpecialKey.Tab => VK_TAB,
                SpecialKey.Enter => VK_RETURN,
                SpecialKey.Escape => VK_ESCAPE,
                SpecialKey.Backspace => VK_BACK,
                SpecialKey.Delete => VK_DELETE,
                SpecialKey.Up => VK_UP,
                SpecialKey.Down => VK_DOWN,
                SpecialKey.Left => VK_LEFT,
                SpecialKey.Right => VK_RIGHT,
                _ => throw new ArgumentException($"Unsupported key: {key}")
            };

            SendKey(vkCode);
            return Task.CompletedTask;
        }

        private void SendChar(char character)
        {
            // Use Unicode input for international character support
            INPUT[] inputs = new INPUT[2];

            // Key down
            inputs[0] = new INPUT
            {
                Type = INPUT_KEYBOARD,
                Union = new INPUTUNION
                {
                    Keyboard = new KEYBDINPUT
                    {
                        VirtualKey = 0,
                        ScanCode = character,
                        Flags = KEYEVENTF_UNICODE,
                        Time = 0,
                        ExtraInfo = IntPtr.Zero
                    }
                }
            };

            // Key up
            inputs[1] = new INPUT
            {
                Type = INPUT_KEYBOARD,
                Union = new INPUTUNION
                {
                    Keyboard = new KEYBDINPUT
                    {
                        VirtualKey = 0,
                        ScanCode = character,
                        Flags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                        Time = 0,
                        ExtraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private void SendKey(ushort virtualKey)
        {
            INPUT[] inputs = new INPUT[2];

            // Key down
            inputs[0] = new INPUT
            {
                Type = INPUT_KEYBOARD,
                Union = new INPUTUNION
                {
                    Keyboard = new KEYBDINPUT
                    {
                        VirtualKey = virtualKey,
                        ScanCode = 0,
                        Flags = 0,
                        Time = 0,
                        ExtraInfo = IntPtr.Zero
                    }
                }
            };

            // Key up
            inputs[1] = new INPUT
            {
                Type = INPUT_KEYBOARD,
                Union = new INPUTUNION
                {
                    Keyboard = new KEYBDINPUT
                    {
                        VirtualKey = virtualKey,
                        ScanCode = 0,
                        Flags = KEYEVENTF_KEYUP,
                        Time = 0,
                        ExtraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
