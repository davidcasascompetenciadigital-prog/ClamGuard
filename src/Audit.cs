using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ClamGuard
{
    public static class Audit
    {
        private const string SubcategoryGuid = "{0CCE921D-69AE-11D9-BED3-505054503030}";

        public static bool IsElevated { get { return Elevation.IsElevated; } }

        public static bool IsPolicyEnabled()
        {
            string o, e;
            if (!RunCmd("auditpol /get /subcategory:" + SubcategoryGuid, out o, out e)) return false;
            string lower = (o ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("sin auditor") || lower.Contains("no auditing") || lower.Contains("none")) return false;
            return lower.Contains("acierto") || lower.Contains("success") || lower.Contains("error") || lower.Contains("failure");
        }

        public static bool EnablePolicy(out string error)
        {
            error = string.Empty;
            string o, e;
            if (!RunCmd("auditpol /set /subcategory:" + SubcategoryGuid + " /success:enable /failure:enable", out o, out e))
            {
                error = string.IsNullOrEmpty(e) ? o : e;
                return false;
            }
            return true;
        }

        public static bool DisablePolicy(out string error)
        {
            error = string.Empty;
            string o, e;
            if (!RunCmd("auditpol /set /subcategory:" + SubcategoryGuid + " /success:disable /failure:disable", out o, out e))
            {
                error = string.IsNullOrEmpty(e) ? o : e;
                return false;
            }
            return true;
        }

        public static bool SetSacl(string path, string account, string rights, bool enable, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(path)) { error = "Ruta vacía."; return false; }
            if (!Directory.Exists(path) && !File.Exists(path)) { error = "La ruta no existe."; return false; }

            string scriptPath = null;
            try
            {
                scriptPath = Path.Combine(Path.GetTempPath(), "ClamGuard-sacl-" + Guid.NewGuid().ToString("N") + ".ps1");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("$ErrorActionPreference = 'Stop'");
                sb.AppendLine("$path = " + PsQuote(path));
                sb.AppendLine("$account = " + PsQuote(account));
                sb.AppendLine("$rights = " + PsQuote(rights));
                if (enable)
                {
                    sb.AppendLine("$sid = New-Object System.Security.Principal.SecurityIdentifier 'S-1-1-0'");
                    sb.AppendLine("$acl = Get-Acl -Path $path");
                    sb.AppendLine("$rule = New-Object System.Security.AccessControl.FileSystemAuditRule($sid, $rights, 'ContainerInherit,ObjectInherit', 'None', 'Success,Failure')");
                    sb.AppendLine("$acl.AddAuditRule($rule)");
                    sb.AppendLine("Set-Acl -Path $path -AclObject $acl");
                    sb.AppendLine("Write-Host 'SACL_OK'");
                }
                else
                {
                    sb.AppendLine("$acl = Get-Acl -Path $path -Audit");
                    sb.AppendLine("$acl.Audit | ForEach-Object { $acl.RemoveAuditRule($_) | Out-Null }");
                    sb.AppendLine("Set-Acl -Path $path -AclObject $acl");
                    sb.AppendLine("Write-Host 'SACL_REMOVED'");
                }

                File.WriteAllText(scriptPath, sb.ToString(), new UTF8Encoding(false));

                ProcessResult r = Proc.Run("powershell.exe",
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Cmd.Quote(scriptPath),
                    null, 60000, System.Threading.CancellationToken.None);
                if (r.ExitCode != 0)
                {
                    error = string.IsNullOrEmpty(r.Error) ? r.Output : r.Error;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try { if (scriptPath != null && File.Exists(scriptPath)) File.Delete(scriptPath); } catch { }
            }
        }

        public static List<string> ReadRecentEvents(int maxEvents, DateTime sinceUtc)
        {
            List<string> result = new List<string>();
            string scriptPath = null;
            string outPath = null;
            try
            {
                scriptPath = Path.Combine(Path.GetTempPath(), "ClamGuard-auditread-" + Guid.NewGuid().ToString("N") + ".ps1");
                outPath = Path.Combine(Path.GetTempPath(), "ClamGuard-auditread-" + Guid.NewGuid().ToString("N") + ".txt");

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
                sb.AppendLine("$since = [DateTime]::Parse(" + PsQuote(sinceUtc.ToString("o")) + ")");
                sb.AppendLine("$events = Get-WinEvent -FilterHashtable @{LogName='Security'; Id=4663; StartTime=$since} -MaxEvents " + maxEvents + " 2>$null");
                sb.AppendLine("$out = @()");
                sb.AppendLine("foreach ($e in $events) {");
                sb.AppendLine("  $msg = $e.Message -replace \"`r|`n\", ' '");
                sb.AppendLine("  $out += ($e.TimeCreated.ToString('o') + [char]0x00A7 + $msg)");
                sb.AppendLine("}");
                sb.AppendLine("$out | Out-File -FilePath " + PsQuote(outPath) + " -Encoding UTF8");

                File.WriteAllText(scriptPath, sb.ToString(), new UTF8Encoding(false));

                ProcessResult r = Proc.Run("powershell.exe",
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Cmd.Quote(scriptPath),
                    null, 120000, System.Threading.CancellationToken.None);

                if (File.Exists(outPath))
                {
                    string[] lines = File.ReadAllLines(outPath, Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (string.IsNullOrEmpty(lines[i])) continue;
                        result.Add(ToReadable(lines[i]));
                    }
                }
            }
            catch { }
            finally
            {
                try { if (scriptPath != null && File.Exists(scriptPath)) File.Delete(scriptPath); } catch { }
                try { if (outPath != null && File.Exists(outPath)) File.Delete(outPath); } catch { }
            }
            return result;
        }

        private static string ToReadable(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string[] parts = raw.Split(new string[] { "\u00A7" }, StringSplitOptions.None);
            string time = parts.Length > 0 ? parts[0] : "";
            string msg = parts.Length > 1 ? parts[1] : raw;

            string user = Extract(msg, @"(?:Nombre de cuenta|Account Name):\s*(.+)");
            string obj = Extract(msg, @"(?:Nombre del objeto|Object Name):\s*(.+)");
            string accesses = Extract(msg, @"(?:Accesos|Accesses):\s*(.+?)(?:M[aá]scara de acceso|Access Mask|Properties|$)");
            string process = Extract(msg, @"(?:Nombre de proceso|Process Name):\s*(.+)");

            accesses = accesses.Replace("\r", " ").Replace("\n", " ").Trim();
            if (accesses.Length > 80) accesses = accesses.Substring(0, 80);

            string friendly = FriendlyAccess(accesses);

            StringBuilder sb = new StringBuilder();
            sb.Append(time).Append(" - ");
            string action = string.IsNullOrEmpty(friendly) ? accesses : friendly;
            if (string.IsNullOrEmpty(user)) user = "?";
            if (string.IsNullOrEmpty(obj)) obj = "?";
            if (action == "Eliminado")
                sb.Append("El usuario ").Append(user.Trim()).Append(" eliminó ").Append(obj.Trim());
            else if (action == "Creado/Modificado")
                sb.Append("El usuario ").Append(user.Trim()).Append(" creó o modificó ").Append(obj.Trim());
            else if (action == "Leido")
                sb.Append("El usuario ").Append(user.Trim()).Append(" accedió (lectura) a ").Append(obj.Trim());
            else if (action == "Ejecutado")
                sb.Append("El usuario ").Append(user.Trim()).Append(" ejecutó ").Append(obj.Trim());
            else
                sb.Append("El usuario ").Append(user.Trim()).Append(" realizó '").Append(action).Append("' sobre ").Append(obj.Trim());
            if (!string.IsNullOrEmpty(process))
                sb.Append(" (proceso: ").Append(process.Trim()).Append(")");
            return sb.ToString();
        }

        private static string FriendlyAccess(string accesses)
        {
            if (string.IsNullOrEmpty(accesses)) return accesses;
            string a = accesses.ToUpperInvariant();
            if (a.Contains("DELETE")) return "Eliminado";
            if (a.Contains("ADD") || a.Contains("CREATE") || a.Contains("WRITE DATA") || a.Contains("WRITE_DAC") || a.Contains("WRITE_OWNER") || a.Contains("APPEND")) return "Creado/Modificado";
            if (a.Contains("READ") || a.Contains("LIST")) return "Leido";
            if (a.Contains("EXECUTE") || a.Contains("TRAVERSE")) return "Ejecutado";
            return accesses;
        }

        private static string Extract(string text, string pattern)
        {
            try
            {
                Match m = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (m.Success) return m.Groups[1].Value.Trim();
            }
            catch { }
            return string.Empty;
        }

        private static bool RunCmd(string arguments, out string output, out string error)
        {
            output = string.Empty;
            error = string.Empty;
            try
            {
                ProcessResult r = Proc.Run("cmd.exe", "/c " + arguments, null, 60000, System.Threading.CancellationToken.None);
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

        private static string PsQuote(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
        }
    }
}
