using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace MihomoDesk
{
    static class Bootstrap
    {
        [STAThread]
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate (object sender, ResolveEventArgs e)
            {
                if (new AssemblyName(e.Name).Name != "YamlDotNet")
                    return null;
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("assets.YamlDotNet.dll.gz"))
                {
                    if (s == null)
                        return null;
                    using (GZipStream z = new GZipStream(s, CompressionMode.Decompress))
                    using (MemoryStream b = new MemoryStream())
                    {
                        byte[] buf = new byte[8192];
                        int n;
                        while ((n = z.Read(buf, 0, buf.Length)) > 0)
                            b.Write(buf, 0, n);
                        return Assembly.Load(b.ToArray());
                    }
                }
            };
            try
            {
                Program.Run(args);
            }
            catch (Exception e)
            {
                Diagnostics.Failure("bootstrap", e);
                if (Environment.UserInteractive)
                    MessageBox.Show("Не удалось запустить VPN. Повторите запуск или отправьте журнал диагностики.", "VPN");
            }
        }
    }

    static class Assets
    {
        public static string Folder
        {
            get
            {
                return Path.Combine(Program.Home, "bin-0.7");
            }
        }

        static readonly System.Collections.Generic.Dictionary<string, string> Hashes = new System.Collections.Generic.Dictionary<string, string>
        {
            {
                "mihomo.exe",
                "a4fbf2b8bc2b5416ce2d0d392ac59f2e0a234ea4fcf635b66bd21e62ea9641b9"
            },
            {
                "xray.exe",
                "ee55a613db6d4f2ca12dba02f24c03daa08fdd888bf78a3de04951b059394cee"
            },
            {
                "YamlDotNet.dll",
                "68c0b3c3e123b5d4adc398ac339c313283d404c3269332be12bc93ec17b3a72e"
            },
            {
                "LICENSE-mihomo.txt",
                "3972dc9744f6499f0f9b2dbf76696f2ae7ad8af9b23dde66d6af86c9dfb36986"
            },
            {
                "LICENSE-Xray.txt",
                "1f256ecad192880510e84ad60474eab7589218784b9a50bc7ceee34c2b91f1d5"
            },
            {
                "LICENSE-YamlDotNet.txt",
                "daea8b7f1b2bab48ea12ad50b0ce91634669d99d7dd0cb64aed8dffeeef69ed3"
            },
            {
                "LICENSE.txt",
                "e0740ef32c32390879c2b0e2b2790b18da8bc01ba9056c22c01a7da148a36827"
            }
        };
        static readonly System.Collections.Generic.Dictionary<string, string> Ready = new System.Collections.Generic.Dictionary<string, string>();
        static string Hash(string file)
        {
            using (SHA256 h = SHA256.Create())
            using (FileStream f = File.OpenRead(file))
                return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }

        public static string Get(string name)
        {
            string cached;
            if (Ready.TryGetValue(name, out cached) && File.Exists(cached))
                return cached;
            if (!Hashes.ContainsKey(name))
                throw new IOException("Unknown embedded component");
            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, name);
            if (File.Exists(file) && Hash(file) == Hashes[name])
            {
                Ready[name] = file;
                return file;
            }

            string tmp = file + "." + System.Diagnostics.Process.GetCurrentProcess().Id + ".tmp";
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("assets." + name + ".gz"))
                {
                    if (s == null)
                        throw new IOException("Missing component");
                    using (GZipStream z = new GZipStream(s, CompressionMode.Decompress))
                    using (FileStream dest = File.Create(tmp))
                    {
                        byte[] b = new byte[65536];
                        int n;
                        while ((n = z.Read(b, 0, b.Length)) > 0)
                            dest.Write(b, 0, n);
                    }
                }

                if (Hash(tmp) != Hashes[name])
                    throw new IOException("Component checksum mismatch");
                if (File.Exists(file))
                    File.Replace(tmp, file, null);
                else
                    File.Move(tmp, file);
                Ready[name] = file;
                return file;
            }
            finally
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
        }
    }
}
