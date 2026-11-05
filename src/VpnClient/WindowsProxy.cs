using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MihomoDesk
{
    // WinINet per-connection options update Windows' active proxy state as well as its cache.
    static class WindowsProxy
    {
        [StructLayout(LayoutKind.Sequential)]
        struct OptionList
        {
            public int Size;
            public IntPtr Connection;
            public int Count, Error;
            public IntPtr Options;
        }
        [DllImport("wininet.dll", EntryPoint = "InternetQueryOptionW", SetLastError = true)]
        static extern bool Query(IntPtr handle, int option, ref OptionList list, ref int size);
        [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)]
        static extern bool Set(IntPtr handle, int option, ref OptionList list, int size);
        [DllImport("wininet.dll", SetLastError = true)]
        static extern bool InternetSetOption(IntPtr handle, int option, IntPtr data, int size);
        [DllImport("kernel32.dll")]
        static extern IntPtr GlobalFree(IntPtr value);

        static int Stride { get { return IntPtr.Size == 8 ? 16 : 12; } }
        static int ValueOffset { get { return IntPtr.Size == 8 ? 8 : 4; } }
        static OptionList Allocate()
        {
            OptionList list = new OptionList();
            list.Size = Marshal.SizeOf(typeof(OptionList));
            list.Count = 4;
            list.Options = Marshal.AllocHGlobal(Stride * list.Count);
            Marshal.Copy(new byte[Stride * list.Count], 0, list.Options, Stride * list.Count);
            for (int i = 0; i < list.Count; i++) Marshal.WriteInt32(list.Options, i * Stride, i + 1);
            return list;
        }
        static IntPtr Value(OptionList list, int index)
        {
            return new IntPtr(list.Options.ToInt64() + index * Stride + ValueOffset);
        }
        public static Dictionary<string, object> Capture()
        {
            OptionList list = Allocate();
            try
            {
                int size = list.Size;
                if (!Query(IntPtr.Zero, 75, ref list, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
                string[] text = new string[3];
                for (int i = 1; i < 4; i++)
                {
                    IntPtr ptr = Marshal.ReadIntPtr(Value(list, i));
                    text[i - 1] = ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUni(ptr);
                }
                return Profiles.Map("flags", Marshal.ReadInt32(Value(list, 0)), "server", text[0], "bypass", text[1], "pac", text[2]);
            }
            finally
            {
                for (int i = 1; i < 4; i++)
                {
                    IntPtr ptr = Marshal.ReadIntPtr(Value(list, i));
                    if (ptr != IntPtr.Zero) GlobalFree(ptr);
                }
                Marshal.FreeHGlobal(list.Options);
            }
        }
        public static void Apply(Dictionary<string, object> state)
        {
            OptionList list = Allocate();
            try
            {
                Marshal.WriteInt32(Value(list, 0), Int32.Parse(Profiles.Get(state, "flags", "1")));
                string[] fields = { "server", "bypass", "pac" };
                for (int i = 1; i < 4; i++)
                    Marshal.WriteIntPtr(Value(list, i), Marshal.StringToHGlobalUni(Profiles.Get(state, fields[i - 1], "")));
                if (!Set(IntPtr.Zero, 75, ref list, list.Size)) throw new Win32Exception(Marshal.GetLastWin32Error());
                // Notify existing WinINet clients and reload their cached settings.
                if (!InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (!InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                for (int i = 1; i < 4; i++)
                {
                    IntPtr ptr = Marshal.ReadIntPtr(Value(list, i));
                    if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
                }
                Marshal.FreeHGlobal(list.Options);
            }
        }
        public static bool UsesPort(string server, string port)
        {
            if (String.IsNullOrEmpty(server)) return false;
            string[] entries = server.Split(';');
            bool found = false;
            foreach (string raw in entries)
            {
                string address = raw.Trim();
                if (address.Length == 0) continue;
                int equals = address.IndexOf('=');
                if (equals >= 0) address = address.Substring(equals + 1).Trim();
                if (!address.Equals("127.0.0.1:" + port, StringComparison.OrdinalIgnoreCase)) return false;
                found = true;
            }
            return found;
        }
        public static bool DisableOwned(int port)
        {
            if (port <= 0) return false;
            Dictionary<string, object> current = Capture();
            if (!UsesPort(Profiles.Get(current, "server", ""), port.ToString())) return false;
            current["flags"] = (Int32.Parse(Profiles.Get(current, "flags", "1")) & ~2) | 1;
            Apply(current);
            if (!Equivalent(current, Capture())) throw new InvalidOperationException("Emergency proxy disable verification failed");
            return true;
        }
        public static bool Equivalent(Dictionary<string, object> expected, Dictionary<string, object> actual)
        {
            int flags = Int32.Parse(Profiles.Get(expected, "flags", "1"));
            if ((flags & 15) != (Int32.Parse(Profiles.Get(actual, "flags", "1")) & 15)) return false;
            if ((flags & 2) != 0 && (Profiles.Get(expected, "server", "") != Profiles.Get(actual, "server", "") || Profiles.Get(expected, "bypass", "") != Profiles.Get(actual, "bypass", ""))) return false;
            return (flags & 4) == 0 || Profiles.Get(expected, "pac", "") == Profiles.Get(actual, "pac", "");
        }
    }
}
