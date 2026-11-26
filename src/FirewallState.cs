using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ClamGuard
{
    public static class FirewallState
    {
        private static string StateFile
        {
            get { return Path.Combine(AppPaths.DataDir, "firewall-state.txt"); }
        }

        public static bool ResetToDefaults(out string error)
        {
            error = string.Empty;
            if (!Elevation.IsElevated)
            {
                error = "Se necesitan permisos de administrador.";
                return false;
            }
            string o, e;
            if (!Firewall.RunNetsh("advfirewall reset", out o, out e))
            {
                error = string.IsNullOrEmpty(e) ? o : e;
                return false;
            }
            try
            {
                if (File.Exists(StateFile)) File.Delete(StateFile);
            }
            catch { }
            return true;
        }

        public static void SaveEnabledRules()
        {
            try
            {
                AppPaths.EnsureLayout();
                string o, e;
                if (!Firewall.RunNetsh("advfirewall firewall show rule name=all", out o, out e)) return;
                List<string> names = new List<string>();
                string[] lines = (o ?? string.Empty).Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
                Dictionary<string, string> current = null;
                List<Dictionary<string, string>> blocks = new List<Dictionary<string, string>>();
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.IndexOf("Nombre de regla", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("Rule Name", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (current != null && current.Count > 0) blocks.Add(current);
                        current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    if (current == null) continue;
                    if (string.IsNullOrEmpty(line)) continue;
                    int colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        string key = line.Substring(0, colon).Trim();
                        string val = line.Substring(colon + 1).Trim();
                        if (!current.ContainsKey(key)) current[key] = val;
                    }
                }
                if (current != null && current.Count > 0) blocks.Add(current);

                foreach (Dictionary<string, string> b in blocks)
                {
                    string name = GetVal(b, "Nombre de regla", "Rule Name");
                    string enabled = GetVal(b, "Habilitada", "Enabled");
                    if (string.IsNullOrEmpty(name)) continue;
                    if (enabled.IndexOf("S", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        enabled.IndexOf("Y", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        enabled.IndexOf("Yes", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        enabled.IndexOf("Si", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (!names.Contains(name)) names.Add(name);
                    }
                }

                File.WriteAllLines(StateFile, names.ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }

        public static void RestoreEnabledRules()
        {
            try
            {
                if (!File.Exists(StateFile)) return;
                string[] names = File.ReadAllLines(StateFile);
                for (int i = 0; i < names.Length; i++)
                {
                    string name = names[i].Trim();
                    if (string.IsNullOrEmpty(name)) continue;
                    string o, e;
                    Firewall.RunNetsh("advfirewall firewall set rule name=\"" + name + "\" new enable=yes", out o, out e);
                }
                File.Delete(StateFile);
            }
            catch { }
        }

        private static string GetVal(Dictionary<string, string> b, params string[] keys)
        {
            foreach (string k in keys)
            {
                foreach (KeyValuePair<string, string> kv in b)
                {
                    if (kv.Key.Equals(k, StringComparison.OrdinalIgnoreCase)) return kv.Value;
                }
            }
            foreach (string k in keys)
            {
                foreach (KeyValuePair<string, string> kv in b)
                {
                    if (kv.Key.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return kv.Value;
                }
            }
            return string.Empty;
        }
    }
}
