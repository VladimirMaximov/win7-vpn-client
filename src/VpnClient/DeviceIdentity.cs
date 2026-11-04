using System;
using System.IO;
using System.Net;
using System.Text;

namespace MihomoDesk
{
    public static class DeviceIdentity
    {
        public static string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MihomoDesk");
        static readonly object Gate = new object ();
        public static string GetId()
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, "device-id.txt");
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path).Trim();
                    try
                    {
                        if (new Guid(text) != Guid.Empty)
                            return text;
                    }
                    catch (FormatException)
                    {
                    }

                    throw new InvalidDataException("Повреждён сохранённый идентификатор устройства. Он не будет заменён автоматически.");
                }

                string id = Guid.NewGuid().ToString();
                using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = Encoding.ASCII.GetBytes(id);
                    stream.Write(bytes, 0, bytes.Length);
                }

                return id;
            }
        }

        public static bool Required(WebHeaderCollection headers)
        {
            return String.Equals(headers["X-Hwid-Active"], "true", StringComparison.OrdinalIgnoreCase) && String.Equals(headers["X-Hwid-Not-Supported"], "true", StringComparison.OrdinalIgnoreCase);
        }

        public static void Apply(HttpWebRequest request)
        {
            request.Headers["X-HWID"] = GetId();
            request.Headers["X-Device-OS"] = "Windows";
            request.Headers["X-Ver-OS"] = Environment.OSVersion.Version.ToString();
            request.Headers["X-Device-Model"] = "Windows PC";
            request.Headers["X-App-Version"] = "0.10.2";
            request.Headers["X-Device-Locale"] = "ru";
        }
    }
}
