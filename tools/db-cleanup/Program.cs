using System;
using System.IO;
using System.Diagnostics;

class Program
{
    static int Main(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        var dbPath = args.Length > 0 ? args[0] : Path.Combine(root, "listenarr.api", "config", "database", "listenarr.db");
        if (!File.Exists(dbPath))
        {
            Console.Error.WriteLine($"Database not found: {dbPath}");
            return 2;
        }

        var backup = dbPath + ".cleanup.bak." + DateTime.Now.ToString("yyyyMMddHHmmss");
        File.Copy(dbPath, backup);
        Console.WriteLine($"Backup created: {backup}");

        // Prefer invoking the sqlite3 CLI to apply the normalization script so we avoid
        // managed native provider mismatches in different environments.
        string? sqlite3Path = null;
        if (args.Length > 1 && File.Exists(args[1])) sqlite3Path = args[1];
        else
        {
            try
            {
                var where = new ProcessStartInfo("where", "sqlite3") { RedirectStandardOutput = true, UseShellExecute = false };
                using var p = Process.Start(where);
                var outp = p?.StandardOutput.ReadToEnd() ?? string.Empty;
                p?.WaitForExit();
                var first = outp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrEmpty(first) && File.Exists(first)) sqlite3Path = first;
            }
            catch { }
        }

        if (sqlite3Path == null)
        {
            Console.Error.WriteLine("sqlite3 executable not found on PATH. Please install sqlite3 or pass its path as second argument.");
            return 3;
        }

        Console.WriteLine($"Using sqlite3 at: {sqlite3Path}");

        var psi = new ProcessStartInfo(sqlite3Path, $"\"{dbPath}\" \".read scripts/normalize_json_columns.sql\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var proc = Process.Start(psi);
        if (proc == null)
        {
            Console.Error.WriteLine("Failed to start sqlite3 process.");
            return 4;
        }
        var stdOut = proc.StandardOutput.ReadToEnd();
        var stdErr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Console.WriteLine(stdOut);
        if (!string.IsNullOrWhiteSpace(stdErr)) Console.Error.WriteLine(stdErr);

        Console.WriteLine("Normalization complete.");
        return 0;
    }
}
