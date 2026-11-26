using System;
using System.Collections.Generic;
using System.IO;

namespace ClamGuard
{
    /// <summary>
    /// Rutas y constantes compartidas por la interfaz y el servicio residente.
    /// </summary>
    public static class AppPaths
    {
        public const string ProductName = "ClamGuard";
        public const string ProductVersion = "1.2.0";
        public const string ServiceName = "ClamGuardResident";
        public const string ServiceDisplayName = "ClamGuard Resident (ClamAV)";
        public const int ClamdPort = 3310;

        private static readonly string DataRoot = ResolveDataRoot();

        private static string ResolveDataRoot()
        {
            string custom = Environment.GetEnvironmentVariable("CLAMGUARD_DATA");
            if (!string.IsNullOrEmpty(custom))
            {
                try
                {
                    string full = Path.GetFullPath(custom.Trim().Trim('"'));
                    if (!string.IsNullOrEmpty(full)) return full.TrimEnd('\\');
                }
                catch
                {
                }
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductName);
        }

        public static string DataDir { get { return DataRoot; } }
        public static string SettingsFile { get { return Path.Combine(DataRoot, "settings.json"); } }
        public static string StateFile { get { return Path.Combine(DataRoot, "state.json"); } }
        public static string DefenderStateFile { get { return Path.Combine(DataRoot, "defender-state.json"); } }
        public static string LogsDir { get { return Path.Combine(DataRoot, "logs"); } }
        public static string DatabaseDir { get { return Path.Combine(DataRoot, "db"); } }
        public static string QuarantineDir { get { return Path.Combine(DataRoot, "quarantine"); } }
        public static string DownloadsDir { get { return Path.Combine(DataRoot, "downloads"); } }
        public static string TempDir { get { return Path.Combine(DataRoot, "temp"); } }
        public static string ClamdConf { get { return Path.Combine(DataRoot, "clamd.conf"); } }
        public static string FreshclamConf { get { return Path.Combine(DataRoot, "freshclam.conf"); } }
        public static string ClamdLog { get { return Path.Combine(LogsDir, "clamd.log"); } }
        public static string FreshclamLog { get { return Path.Combine(LogsDir, "freshclam.log"); } }
        public static string GuiLog { get { return Path.Combine(LogsDir, "clamguard.log"); } }
        public static string ServiceLog { get { return Path.Combine(LogsDir, "resident.log"); } }
        public static string MsiLog { get { return Path.Combine(LogsDir, "msi-install.log"); } }
        public static string ExePath { get { return System.Reflection.Assembly.GetEntryAssembly().Location; } }
        public static string AppDir
        {
            get
            {
                try { return Path.GetDirectoryName(ExePath); }
                catch { return null; }
            }
        }

        public static void EnsureLayout()
        {
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(LogsDir);
            Directory.CreateDirectory(DatabaseDir);
            Directory.CreateDirectory(QuarantineDir);
            Directory.CreateDirectory(DownloadsDir);
            Directory.CreateDirectory(TempDir);
        }

        public static bool IsValidClamAvDir(string dir, bool requireClamd)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            try
            {
                dir = NormalizeDirectory(dir);
                if (!File.Exists(Path.Combine(dir, "clamscan.exe"))) return false;
                if (!File.Exists(Path.Combine(dir, "freshclam.exe"))) return false;
                if (requireClamd && !File.Exists(Path.Combine(dir, "clamd.exe"))) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string NormalizeDirectory(string value)
        {
            if (value == null) return null;
            value = value.Trim().Trim('"');
            if (value.Length == 0) return null;
            try
            {
                if (File.Exists(value)) value = Path.GetDirectoryName(value);
                return Path.GetFullPath(value).TrimEnd('\\', '/');
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Busca una instalación de ClamAV. Devuelve null si no encuentra una válida.
        /// </summary>
        public static string FindClamAvDirectory(string preferred)
        {
            string dir = NormalizeDirectory(preferred);
            if (IsValidClamAvDir(dir, false)) return dir;

            List<string> candidates = ClamAvCandidates();
            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = NormalizeDirectory(candidates[i]);
                if (IsValidClamAvDir(candidate, false)) return candidate;
            }

            // Buscar en PATH
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(path))
                {
                    string[] parts = path.Split(';');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        string candidate = NormalizeDirectory(parts[i]);
                        if (IsValidClamAvDir(candidate, false)) return candidate;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        public static List<string> ClamAvCandidates()
        {
            List<string> list = new List<string>();
            AddCandidate(list, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClamAV"));
            AddCandidate(list, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ClamAV"));
            AddCandidate(list, Path.Combine(DataRoot, "clamav"));
            AddCandidate(list, @"C:\ClamAV");
            return list;
        }

        private static void AddCandidate(List<string> list, string path)
        {
            if (!string.IsNullOrEmpty(path) && !list.Contains(path)) list.Add(path);
        }

        public static List<string> DefaultMonitoredPaths()
        {
            List<string> list = new List<string>();
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            AddIfDirectory(list, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddIfDirectory(list, Path.Combine(profile, "Downloads"));
            AddIfDirectory(list, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddIfDirectory(list, Path.GetTempPath());
            return list;
        }

        public static List<string> DefaultQuickScanPaths()
        {
            List<string> list = DefaultMonitoredPaths();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            AddIfDirectory(list, Path.Combine(appData, @"Microsoft\Windows\Start Menu\Programs\Startup"));
            return list;
        }

        private static void AddIfDirectory(List<string> list, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                string full = Path.GetFullPath(path).TrimEnd('\\');
                if (Directory.Exists(full) && !ContainsPath(list, full)) list.Add(full);
            }
            catch
            {
            }
        }

        public static bool ContainsPath(List<string> list, string path)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], path, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static bool IsUnder(string path, string directory)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(directory)) return false;
            try
            {
                string fullPath = Path.GetFullPath(path);
                string fullDir = Path.GetFullPath(directory).TrimEnd('\\');
                return fullPath.StartsWith(fullDir + "\\", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fullPath, fullDir, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static long GetFreeSpace(string path)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root)) return 0;
                DriveInfo drive = new DriveInfo(root);
                return drive.AvailableFreeSpace;
            }
            catch
            {
                return 0;
            }
        }
    }
}
