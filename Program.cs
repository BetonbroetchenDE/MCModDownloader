using System.IO.Compression;
using System.Runtime.InteropServices;

namespace MC_Mod_Downloader
{
    class Program
    {
        private const string ModsZipUrl = "https://apps.betonbroetchen.de/redirect.php?app=MC_Mod_Downloader&key=mods";

        public static readonly string ModsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft", "mods");

        static async Task<int> Main()
        {
            DisableQuickEditMode();

            Console.WriteLine("Minecraft Mod Downloader");
            Console.WriteLine("----------------------------------");
            Console.WriteLine();

            string input;
            do
            {
                Console.Write("Möchtest du die Mods installieren? (j/n): ");
                input = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";
            }
            while (input != "j" && input != "n");

            if (input == "n") return 0;

            try
            {
                // 1) Download
                using var cts = new CancellationTokenSource();
                string[] dotPhases = { ".", "..", "..." };
                int phase = 0;

                var animationTask = Task.Run(async () => {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        Console.Write($"\rLade Mod-Paket herunter{dotPhases[phase]} ");
                        phase = (phase + 1) % dotPhases.Length;
                        await Task.Delay(500, cts.Token);
                    }
                }, cts.Token);

                byte[] data;
                try
                {
                    data = await Download(ModsZipUrl, (read, total) => {}, cts.Token);
                }
                finally
                {
                    // stop the animation
                    cts.Cancel();
                    try { await animationTask; } catch {}
                }

                Console.WriteLine("\rLade Mod-Paket herunter... ✔");

                // 2) Validate ZIP
                Console.Write("Prüfe heruntergeladene Datei... ");
                using (var ms = new MemoryStream(data))
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
                {
                    if (zip.Entries.Count == 0)
                        throw new InvalidDataException("Die ZIP-Datei ist leer oder beschädigt.");
                }
                Console.WriteLine("OK!");

                // 3) Prepare mods folder
                Console.Write("Bereite den Mods-Ordner vor... ");
                PrepareModsDirectory();
                Console.WriteLine("Fertig.");

                // 4) Extract
                Console.WriteLine("\nInstalliere Mods:");
                using (var ms = new MemoryStream(data))
                using (var zip = new ZipArchive(ms))
                {
                    int total = zip.Entries.Count;
                    for (int i = 0; i < total; i++)
                    {
                        var entry = zip.Entries[i];
                        string target = Path.Combine(ModsPath, entry.FullName);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        entry.ExtractToFile(target, overwrite: true);
                        DrawProgress(i + 1, total);
                    }
                }
                Console.WriteLine();

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\nDie Mods wurden erfolgreich installiert!");
                Console.WriteLine("Du kannst jetzt Minecraft starten und die Mods nutzen.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\nFehler während der Installation:");
                Console.WriteLine(ex.Message);
                return 1;
            }
            finally
            {
                Console.ResetColor();
                Console.WriteLine("\nDrücke eine beliebige Taste zum Beenden...");
                Console.ReadKey(true);
            }
        }

        private static async Task<byte[]> Download(
            string url,
            Action<long, long> progressCallback,
            CancellationToken cancellation = default)
        {
            var client = new HttpClient();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            using var input = await response.Content.ReadAsStreamAsync(cancellation);
            using var ms = new MemoryStream();

            var buffer = new byte[81920];
            long read = 0;
            int chunk;
            while ((chunk = await input.ReadAsync(buffer, 0, buffer.Length, cancellation)) > 0)
            {
                ms.Write(buffer, 0, chunk);
                read += chunk;
                progressCallback(read, total);
            }
            return ms.ToArray();
        }

        public static void PrepareModsDirectory()
        {
            if (Directory.Exists(ModsPath) && Directory.EnumerateFileSystemEntries(ModsPath).Any())
            {
                string baseOld = ModsPath + "_ALT";
                string newName = baseOld;
                int count = 1;
                while (Directory.Exists(newName))
                    newName = baseOld + "_" + count++;
                Directory.Move(ModsPath, newName);
            }
            Directory.CreateDirectory(ModsPath);
        }

        private static void DrawProgress(long done, long total)
        {
            const int barWidth = 40;
            double pct = total > 0 ? (double)done / total : 0;
            int filled = (int)(pct * barWidth);

            Console.CursorVisible = false;
            Console.Write("\r[");
            Console.Write(new string('#', filled));
            Console.Write(new string('-', barWidth - filled));
            Console.Write($"] {pct:P0}");
        }

        // 🛑 Prevents console "freezing" when user clicks in the window
        private static void DisableQuickEditMode()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            const int STD_INPUT_HANDLE = -10;
            const uint ENABLE_QUICK_EDIT = 0x0040;
            const uint ENABLE_EXTENDED_FLAGS = 0x0080;

            IntPtr consoleHandle = GetStdHandle(STD_INPUT_HANDLE);
            GetConsoleMode(consoleHandle, out uint consoleMode);

            // Remove Quick Edit and add extended flags
            consoleMode &= ~ENABLE_QUICK_EDIT;
            consoleMode |= ENABLE_EXTENDED_FLAGS;

            SetConsoleMode(consoleHandle, consoleMode);
        }

        // Win32 API calls to handle console mode
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
    }
}