using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ClamGuard
{
    public static class Firewall
    {
        private static string StateFile
        {
            get { return Path.Combine(AppPaths.DataDir, "firewall-state.txt"); }
        }
        public static bool IsElevated { get { return Elevation.IsElevated; } }
        public static bool RunNetsh(string arguments, out string output, out string error)
        {
            output = string.Empty;
            error = string.Empty;
            try
            {
                string cmdArgs = "/c chcp 65001 >nul & netsh " + arguments;
                ProcessResult r = Proc.Run("cmd.exe", cmdArgs, null, 60000, System.Threading.CancellationToken.None);
                output = r.Output;
                error = r.Error;
                return r.ExitCode == 0;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static string GetStatus()
        {
            string output, error;
            if (RunNetsh("advfirewall show allprofiles", out output, out error))
                return output;
            return "Error: " + error;
        }

        public static bool SetMode(string mode, out string error)
        {
            error = string.Empty;
            if (!Elevation.IsElevated)
            {
                error = "Se necesitan permisos de administrador para cambiar el cortafuegos.";
                return false;
            }
            if (mode == "allowall")
            {
                FirewallState.RestoreEnabledRules();
                string o, e;
                if (!RunNetsh("advfirewall set allprofiles state off", out o, out e)) { error = e; return false; }
                RunNetsh("advfirewall firewall set rule all new enable=yes", out o, out e);
                return true;
            }
            if (mode == "blockall")
            {
                string o, e;
                if (!RunNetsh("advfirewall set allprofiles state on", out o, out e)) { error = e; return false; }
                if (!RunNetsh("advfirewall set allprofiles firewallpolicy blockinbound,blockoutbound", out o, out e)) { error = e; return false; }
                FirewallState.SaveEnabledRules();
                // Deshabilitar reglas existentes para que el bloqueo sea efectivo (estilo TinyWall)
                RunNetsh("advfirewall firewall set rule all new enable=no", out o, out e);
                return true;
            }
            // active (default): block inbound, allow outbound
            {
                FirewallState.RestoreEnabledRules();
                string o, e;
                if (!RunNetsh("advfirewall set allprofiles state on", out o, out e)) { error = e; return false; }
                if (!RunNetsh("advfirewall set allprofiles firewallpolicy blockinbound,allowoutbound", out o, out e)) { error = e; return false; }
                // Rehabilitar reglas existentes
                RunNetsh("advfirewall firewall set rule all new enable=yes", out o, out e);
                return true;
            }
        }

        public static bool AddProgram(string name, string path, bool allow, out string error)
        {
            error = string.Empty;
            string action = allow ? "allow" : "block";
            string args = "advfirewall firewall add rule name=\"" + name + "\" dir=in action=" + action + " program=\"" + path + "\" enable=yes";
            string o, e;
            if (!RunNetsh(args, out o, out e)) { error = e; return false; }
            string argsOut = "advfirewall firewall add rule name=\"" + name + " (salida)\" dir=out action=" + action + " program=\"" + path + "\" enable=yes";
            RunNetsh(argsOut, out o, out e);
            return true;
        }

        public static bool RemoveProgram(string name, out string error)
        {
            error = string.Empty;
            string o, e;
            RunNetsh("advfirewall firewall delete rule name=\"" + name + "\"", out o, out e);
            RunNetsh("advfirewall firewall delete rule name=\"" + name + " (salida)\"", out o, out e);
            return true;
        }

        public static bool AddPort(string name, int port, string protocol, bool allow, out string error)
        {
            error = string.Empty;
            string action = allow ? "allow" : "block";
            string proto = protocol.ToUpperInvariant();
            string args = "advfirewall firewall add rule name=\"" + name + "\" dir=in action=" + action + " protocol=" + proto + " localport=" + port + " enable=yes";
            string o, e;
            if (!RunNetsh(args, out o, out e)) { error = e; return false; }
            string argsOut = "advfirewall firewall add rule name=\"" + name + " (salida)\" dir=out action=" + action + " protocol=" + proto + " localport=" + port + " enable=yes";
            RunNetsh(argsOut, out o, out e);
            return true;
        }

        public static bool RemovePort(string name, out string error)
        {
            error = string.Empty;
            string o, e;
            RunNetsh("advfirewall firewall delete rule name=\"" + name + "\"", out o, out e);
            RunNetsh("advfirewall firewall delete rule name=\"" + name + " (salida)\"", out o, out e);
            return true;
        }

        public static List<string> ListRules()
        {
            List<string> result = new List<string>();
            string o, e;
            if (RunNetsh("advfirewall firewall show rule name=all", out o, out e))
            {
                string[] lines = o.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.IndexOf("Nombre de regla", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("Rule Name", StringComparison.OrdinalIgnoreCase) >= 0)
                        result.Add(line);
                }
            }
            return result;
        }
    }
}
