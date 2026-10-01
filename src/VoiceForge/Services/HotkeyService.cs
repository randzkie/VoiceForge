using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VoiceForge.Services
{
    /// <summary>
    /// Global (system-wide) hotkeys via RegisterHotKey. The owner form must forward
    /// WndProc messages into ProcessMessage(). Hotkeys survive handle recreation by
    /// re-registering inside the form's OnHandleCreated.
    /// </summary>
    public class HotkeyService : IDisposable
    {
        public const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private IntPtr ownerHandle = IntPtr.Zero;
        private readonly Dictionary<int, bool> registered = new Dictionary<int, bool>();

        /// <summary>id: the hotkey id passed to Register().</summary>
        public event Action<int> HotkeyPressed;

        public void Attach(IntPtr windowHandle)
        {
            ownerHandle = windowHandle;
        }

        public bool Register(int id, Keys key, bool control, bool alt, bool shift)
        {
            if (ownerHandle == IntPtr.Zero) return false;

            Unregister(id);

            uint mods = MOD_NOREPEAT;
            if (control) mods |= MOD_CONTROL;
            if (alt) mods |= MOD_ALT;
            if (shift) mods |= MOD_SHIFT;

            uint vk = (uint)(key & Keys.KeyCode);
            bool ok = RegisterHotKey(ownerHandle, id, mods, vk);
            registered[id] = ok;
            return ok;
        }

        public void Unregister(int id)
        {
            if (ownerHandle == IntPtr.Zero) return;
            try
            {
                UnregisterHotKey(ownerHandle, id);
            }
            catch { }
            if (registered.ContainsKey(id)) registered.Remove(id);
        }

        public void UnregisterAll()
        {
            if (ownerHandle == IntPtr.Zero) return;
            foreach (KeyValuePair<int, bool> kv in registered)
            {
                try
                {
                    UnregisterHotKey(ownerHandle, kv.Key);
                }
                catch { }
            }
            registered.Clear();
        }

        /// <summary>Call from the owner form's WndProc.</summary>
        public void ProcessMessage(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                Action<int> handler = HotkeyPressed;
                if (handler != null) handler(id);
            }
        }

        public void Dispose()
        {
            UnregisterAll();
        }
    }
}
