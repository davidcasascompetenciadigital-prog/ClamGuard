using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClamGuard
{
    public sealed class FirewallDialog : Form
    {
        private TabControl _tabs;
        private Label _lblStatus;
        private RichTextBox _rtbStatus;
        private ComboBox _cboMode;
        private ListBox _lstPrograms;
        private ListBox _lstPorts;
        private TextBox _txtPort;
        private string _lastRulesOutput = string.Empty;
        private string _editingRuleName = string.Empty;

        public FirewallDialog()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            Text = L10n.T("Cortafuegos de Windows");
            ClientSize = new Size(760, 620);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(700, 560);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Surface;
            Font = Theme.Ui(9f, FontStyle.Regular);

            Label title = new Label();
            title.Text = L10n.T("Administrador del cortafuegos");
            title.Font = Theme.Ui(14f, FontStyle.Bold);
            title.ForeColor = Theme.TextPrimary;
            title.BackColor = Theme.Surface;
            title.AutoSize = true;
            title.Location = new Point(24, 16);
            Controls.Add(title);

            _lblStatus = new Label();
            _lblStatus.Text = L10n.T("Estado: consultando...");
            _lblStatus.Font = Theme.Ui(8.75f, FontStyle.Regular);
            _lblStatus.ForeColor = Theme.TextSecondary;
            _lblStatus.BackColor = Theme.Surface;
            _lblStatus.AutoSize = false;
            _lblStatus.Location = new Point(24, 44);
            _lblStatus.Size = new Size(712, 20);
            Controls.Add(_lblStatus);

            _rtbStatus = new RichTextBox();
            _rtbStatus.Font = new Font("Consolas", 8.5f, FontStyle.Regular);
            _rtbStatus.Location = new Point(24, 68);
            _rtbStatus.Size = new Size(712, 150);
            _rtbStatus.BackColor = Theme.ConsoleBack;
            _rtbStatus.ForeColor = Theme.ConsoleText;
            _rtbStatus.ReadOnly = true;
            _rtbStatus.BorderStyle = BorderStyle.None;
            _rtbStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_rtbStatus);

            _tabs = new TabControl();
            _tabs.Location = new Point(24, 224);
            _tabs.Size = new Size(712, 340);
            _tabs.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_tabs);

            TabPage modeTab = new TabPage("Modo");
            modeTab.BackColor = Theme.Surface;
            modeTab.Padding = new Padding(18);
            _tabs.TabPages.Add(modeTab);

            Label modeInfo = new Label();
            modeInfo.Text = L10n.T("Selecciona el modo de funcionamiento del cortafuegos.");
            modeInfo.Font = Theme.Ui(9f, FontStyle.Regular);
            modeInfo.ForeColor = Theme.TextSecondary;
            modeInfo.BackColor = Theme.Surface;
            modeInfo.AutoSize = false;
            modeInfo.Location = new Point(0, 0);
            modeInfo.Size = new Size(676, 20);
            modeTab.Controls.Add(modeInfo);

            _cboMode = new ComboBox();
            _cboMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboMode.Items.AddRange(new object[] { "Normal (recomendado)", "Permitir salidas", "Bloquear todo", "Desactivado" });
            _cboMode.SelectedIndex = 0;
            _cboMode.Location = new Point(0, 32);
            _cboMode.Size = new Size(260, 26);
            modeTab.Controls.Add(_cboMode);

            RoundedButton applyMode = new RoundedButton();
            applyMode.Text = L10n.T("Aplicar modo");
            applyMode.Style = ButtonStyle.Primary;
            applyMode.BackColor = Theme.Surface;
            applyMode.Size = new Size(140, 34);
            applyMode.Location = new Point(272, 28);
            applyMode.Click += ApplyMode_Click;
            modeTab.Controls.Add(applyMode);

            RoundedButton resetDefaults = new RoundedButton();
            resetDefaults.Text = "Restaurar por defecto";
            resetDefaults.Style = ButtonStyle.Secondary;
            resetDefaults.BackColor = Theme.Surface;
            resetDefaults.Size = new Size(170, 34);
            resetDefaults.Location = new Point(420, 28);
            resetDefaults.Click += delegate
            {
                if (MessageBox.Show(this, "Se restaurara la configuracion por defecto del cortafuegos. Continuar?",
                    "Cortafuegos", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                string error;
                if (FirewallState.ResetToDefaults(out error))
                    MessageBox.Show(this, "Configuracion restaurada.", "Cortafuegos", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show(this, error, "Cortafuegos", MessageBoxButtons.OK, MessageBoxIcon.Error);
                RefreshStatus();
            };
            modeTab.Controls.Add(resetDefaults);

            Label modeNote = new Label();
            modeNote.Text = L10n.T("Normal: bloquea entrada, permite salida. Permitir salidas: permite todo saliente. Bloquear todo: bloquea entrada y salida (solo excepciones). Desactivado: permite todo.");
            modeNote.Font = Theme.Ui(8.25f, FontStyle.Regular);
            modeNote.ForeColor = Theme.TextMuted;
            modeNote.BackColor = Theme.Surface;
            modeNote.AutoSize = false;
            modeNote.Location = new Point(0, 72);
            modeNote.Size = new Size(676, 30);
            modeTab.Controls.Add(modeNote);

            TabPage progTab = new TabPage("Programas");
            progTab.BackColor = Theme.Surface;
            progTab.Padding = new Padding(18);
            _tabs.TabPages.Add(progTab);

            _lstPrograms = new ListBox();
            _lstPrograms.Location = new Point(0, 0);
            _lstPrograms.Size = new Size(500, 360);
            _lstPrograms.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            progTab.Controls.Add(_lstPrograms);

            RoundedButton addProg = new RoundedButton();
            addProg.Text = L10n.T("Anadir programa");
            addProg.Style = ButtonStyle.Primary;
            addProg.BackColor = Theme.Surface;
            addProg.Size = new Size(160, 34);
            addProg.Location = new Point(512, 0);
            addProg.Click += AddProgram_Click;
            progTab.Controls.Add(addProg);

            RoundedButton remProg = new RoundedButton();
            remProg.Text = L10n.T("Quitar seleccionado");
            remProg.Style = ButtonStyle.Secondary;
            remProg.BackColor = Theme.Surface;
            remProg.Size = new Size(160, 34);
            remProg.Location = new Point(512, 42);
            remProg.Click += RemoveProgram_Click;
            progTab.Controls.Add(remProg);

            TabPage portTab = new TabPage("Puertos");
            portTab.BackColor = Theme.Surface;
            portTab.Padding = new Padding(18);
            _tabs.TabPages.Add(portTab);

            _lstPorts = new ListBox();
            _lstPorts.Location = new Point(0, 0);
            _lstPorts.Size = new Size(500, 360);
            _lstPorts.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            portTab.Controls.Add(_lstPorts);

            Label portLabel = new Label();
            portLabel.Text = L10n.T("Puerto:");
            portLabel.Font = Theme.Ui(9f, FontStyle.Regular);
            portLabel.ForeColor = Theme.TextPrimary;
            portLabel.BackColor = Theme.Surface;
            portLabel.AutoSize = true;
            portLabel.Location = new Point(512, 4);
            portTab.Controls.Add(portLabel);

            _txtPort = new TextBox();
            _txtPort.Location = new Point(512, 24);
            _txtPort.Size = new Size(80, 24);
            portTab.Controls.Add(_txtPort);

            ComboBox proto = new ComboBox();
            proto.Name = "proto";
            proto.DropDownStyle = ComboBoxStyle.DropDownList;
            proto.Items.AddRange(new object[] { "TCP", "UDP" });
            proto.SelectedIndex = 0;
            proto.Location = new Point(600, 24);
            proto.Size = new Size(80, 24);
            portTab.Controls.Add(proto);

            Label commonLabel = new Label();
            commonLabel.Text = L10n.T("Puertos comunes:");
            commonLabel.Font = Theme.Ui(8.5f, FontStyle.Regular);
            commonLabel.ForeColor = Theme.TextPrimary;
            commonLabel.BackColor = Theme.Surface;
            commonLabel.AutoSize = true;
            commonLabel.Location = new Point(512, 56);
            portTab.Controls.Add(commonLabel);

            ComboBox commonPorts = new ComboBox();
            commonPorts.DropDownStyle = ComboBoxStyle.DropDownList;
            commonPorts.Items.AddRange(new object[] {
                "21 - FTP", "22 - SSH", "23 - Telnet", "25 - SMTP", "53 - DNS",
                "80 - HTTP", "110 - POP3", "143 - IMAP", "443 - HTTPS", "445 - SMB",
                "3306 - MySQL", "3389 - RDP", "5432 - PostgreSQL", "8080 - HTTP alt"
            });
            commonPorts.Location = new Point(512, 76);
            commonPorts.Size = new Size(160, 24);
            commonPorts.SelectedIndexChanged += delegate
            {
                string sel = commonPorts.SelectedItem.ToString();
                string[] parts = sel.Split(new string[] { " - " }, StringSplitOptions.None);
                if (parts.Length > 0)
                {
                    int p;
                    if (int.TryParse(parts[0].Trim(), out p)) _txtPort.Text = p.ToString();
                }
            };
            portTab.Controls.Add(commonPorts);

            Label descLabel = new Label();
            descLabel.Name = "desc";
            descLabel.Text = L10n.T("Descripcion: -");
            descLabel.Font = Theme.Ui(8.25f, FontStyle.Regular);
            descLabel.ForeColor = Theme.TextMuted;
            descLabel.BackColor = Theme.Surface;
            descLabel.AutoSize = false;
            descLabel.Location = new Point(512, 104);
            descLabel.Size = new Size(160, 40);
            portTab.Controls.Add(descLabel);

            _txtPort.TextChanged += delegate
            {
                int p;
                if (int.TryParse(_txtPort.Text, out p))
                {
                    string desc = PortDescription(p);
                    descLabel.Text = L10n.T("Descripcion: ") + (string.IsNullOrEmpty(desc) ? "Puerto personalizado" : desc);
                }
                else
                {
                    descLabel.Text = L10n.T("Descripcion: -");
                }
            };

            RoundedButton addPort = new RoundedButton();
            addPort.Text = L10n.T("Abrir puerto");
            addPort.Style = ButtonStyle.Primary;
            addPort.BackColor = Theme.Surface;
            addPort.Size = new Size(160, 34);
            addPort.Location = new Point(512, 148);
            addPort.Click += delegate { AddPort_Click(proto); };
            portTab.Controls.Add(addPort);

            RoundedButton remPort = new RoundedButton();
            remPort.Text = L10n.T("Cerrar seleccionado");
            remPort.Style = ButtonStyle.Secondary;
            remPort.BackColor = Theme.Surface;
            remPort.Size = new Size(160, 34);
            remPort.Location = new Point(512, 190);
            remPort.Click += RemovePort_Click;
            portTab.Controls.Add(remPort);

            RoundedButton listOpen = new RoundedButton();
            listOpen.Text = L10n.T("Listar abiertos");
            listOpen.Style = ButtonStyle.Ghost;
            listOpen.BackColor = Theme.Surface;
            listOpen.Size = new Size(160, 30);
            listOpen.Location = new Point(512, 232);
            listOpen.Click += delegate { PopulatePorts("allow"); };
            portTab.Controls.Add(listOpen);

            RoundedButton listClosed = new RoundedButton();
            listClosed.Text = L10n.T("Listar cerrados");
            listClosed.Style = ButtonStyle.Ghost;
            listClosed.BackColor = Theme.Surface;
            listClosed.Size = new Size(160, 30);
            listClosed.Location = new Point(512, 268);
            listClosed.Click += delegate { PopulatePorts("block"); };
            portTab.Controls.Add(listClosed);

            RoundedButton listAll = new RoundedButton();
            listAll.Text = L10n.T("Listar todos");
            listAll.Style = ButtonStyle.Ghost;
            listAll.BackColor = Theme.Surface;
            listAll.Size = new Size(160, 30);
            listAll.Location = new Point(512, 304);
            listAll.Click += delegate { PopulatePorts("all"); };
            portTab.Controls.Add(listAll);

            RoundedButton editPort = new RoundedButton();
            editPort.Text = L10n.T("Editar seleccionado");
            editPort.Style = ButtonStyle.Secondary;
            editPort.BackColor = Theme.Surface;
            editPort.Size = new Size(160, 34);
            editPort.Location = new Point(512, 340);
            editPort.Click += EditPort_Click;
            portTab.Controls.Add(editPort);

            RoundedButton savePort = new RoundedButton();
            savePort.Text = L10n.T("Guardar cambios");
            savePort.Style = ButtonStyle.Primary;
            savePort.BackColor = Theme.Surface;
            savePort.Size = new Size(160, 34);
            savePort.Location = new Point(512, 382);
            savePort.Click += SavePort_Click;
            portTab.Controls.Add(savePort);

            RoundedButton close = new RoundedButton();
            close.Text = L10n.T("Cerrar");
            close.Style = ButtonStyle.Secondary;
            close.BackColor = Theme.Surface;
            close.Size = new Size(104, 34);
            close.Location = new Point(632, 572);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.Click += delegate { Close(); };
            Controls.Add(close);
            CancelButton = close;

            RefreshStatus();
        }

        private void RefreshStatus()
        {
            string status = Firewall.GetStatus();
            _rtbStatus.Clear();
            _rtbStatus.AppendText(status + Environment.NewLine + Environment.NewLine);

            string rulesOutput, err;
            Firewall.RunNetsh("advfirewall firewall show rule name=all", out rulesOutput, out err);
            _lastRulesOutput = rulesOutput ?? string.Empty;
            _rtbStatus.AppendText("=== REGLAS ===" + Environment.NewLine + rulesOutput);

            _lstPrograms.Items.Clear();
            _lstPorts.Items.Clear();
            ParseRules(rulesOutput);
            PopulatePorts("all");
            _lblStatus.Text = "Estado: consultado. Puertos listados: " + _lstPorts.Items.Count +
                (Firewall.IsElevated ? " (Administrador)" : " (Sin permisos de administrador)");
        }

        private void ParseRules(string output)
        {
            if (string.IsNullOrEmpty(output)) return;
            string[] lines = output.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
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
                string name = GetDictVal(b, "Nombre de regla", "Rule Name");
                string program = GetDictVal(b, "Programa", "Program");
                string localPort = GetDictVal(b, "Puerto local", "LocalPort");
                string action = GetDictVal(b, "Acción", "Action", "Accion");

                if (!string.IsNullOrEmpty(program) && program.IndexOf("N/A", StringComparison.OrdinalIgnoreCase) < 0 &&
                    program.IndexOf("Cualquiera", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    string item = name + " -> " + program;
                    if (!_lstPrograms.Items.Contains(item)) _lstPrograms.Items.Add(item);
                }
                if (!string.IsNullOrEmpty(localPort) && localPort.IndexOf("Cualquiera", StringComparison.OrdinalIgnoreCase) < 0 &&
                    localPort.IndexOf("Any", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    string item = name + " (" + localPort + ")";
                    if (!_lstPorts.Items.Contains(item)) _lstPorts.Items.Add(item);
                }
            }
        }

        private void PopulatePorts(string filter)
        {
            if (string.IsNullOrEmpty(_lastRulesOutput))
            {
                string o, e;
                Firewall.RunNetsh("advfirewall firewall show rule name=all", out o, out e);
                _lastRulesOutput = o ?? string.Empty;
            }
            _lstPorts.Items.Clear();
            if (string.IsNullOrEmpty(_lastRulesOutput)) return;

            string[] lines = _lastRulesOutput.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
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
                string name = GetDictVal(b, "Nombre de regla", "Rule Name");
                string localPort = GetDictVal(b, "Puerto local", "LocalPort");
                string action = GetDictVal(b, "Acción", "Action", "Accion");
                if (string.IsNullOrEmpty(localPort) ||
                    localPort.IndexOf("Cualquiera", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    localPort.IndexOf("Any", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                bool isAllow = action.IndexOf("Allow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               action.IndexOf("Permitir", StringComparison.OrdinalIgnoreCase) >= 0;
                if (filter == "allow" && !isAllow) continue;
                if (filter == "block" && isAllow) continue;

                string item = name + " (" + localPort + ")";
                if (!_lstPorts.Items.Contains(item)) _lstPorts.Items.Add(item);
            }
        }

        private void EditPort_Click(object sender, EventArgs e)
        {
            if (_lstPorts.SelectedIndex < 0) return;
            string item = _lstPorts.SelectedItem.ToString();
            _editingRuleName = item.Split(new string[] { " (" }, StringSplitOptions.None)[0];
            string inside = item.Substring(item.IndexOf('(') + 1).TrimEnd(')');
            _txtPort.Text = inside;
        }

        private void SavePort_Click(object sender, EventArgs e)
        {
            int port;
            if (!int.TryParse(_txtPort.Text, out port))
            {
                MessageBox.Show(this, L10n.T("Puerto invalido."), "Cortafuegos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string proto = "TCP";
            foreach (Control c in _tabs.TabPages[2].Controls)
            {
                ComboBox cb = c as ComboBox;
                if (cb != null) { proto = cb.SelectedItem.ToString(); break; }
            }
            string name = string.IsNullOrEmpty(_editingRuleName) ? "CG-Port-" + port + "-" + proto : _editingRuleName;
            string error;
            if (!string.IsNullOrEmpty(_editingRuleName))
                Firewall.RemovePort(_editingRuleName, out error);
            if (Firewall.AddPort(name, port, proto, true, out error))
            {
                _lstPorts.Items.Remove(_lstPorts.SelectedItem);
                string item = name + " (" + proto + "/" + port + ")";
                if (!_lstPorts.Items.Contains(item)) _lstPorts.Items.Add(item);
                _editingRuleName = string.Empty;
            }
            else
                MessageBox.Show(this, error, "Cortafuegos", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshStatus();
        }

        private void ApplyMode_Click(object sender, EventArgs e)
        {
            if (!Firewall.IsElevated)
            {
                MessageBox.Show(this, "Se necesitan permisos de administrador para cambiar el cortafuegos. Ejecuta ClamGuard como administrador.",
                    "Cortafuegos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string mode = "active";
            if (_cboMode.SelectedIndex == 2) mode = "blockall";
            if (_cboMode.SelectedIndex == 3) mode = "allowall";
            string error;
            if (Firewall.SetMode(mode, out error))
            {
                if (mode == "blockall")
                    MessageBox.Show(this, "Modo bloquear todo aplicado. Se deshabilitaron las reglas existentes. Agrega programas/puertos para permitir trafico.",
                        "Cortafuegos", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show(this, "Modo aplicado correctamente.", "Cortafuegos", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
                MessageBox.Show(this, error, "Cortafuegos", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshStatus();
        }

        private void AddProgram_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Ejecutables (*.exe)|*.exe|Todos (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string name = "CG-" + Path.GetFileNameWithoutExtension(dlg.FileName);
                string error;
                if (Firewall.AddProgram(name, dlg.FileName, true, out error))
                {
                    _lstPrograms.Items.Add(name + " -> " + dlg.FileName);
                }
                else
                    MessageBox.Show(this, error, "Cortafuegos", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RemoveProgram_Click(object sender, EventArgs e)
        {
            if (_lstPrograms.SelectedIndex < 0) return;
            string item = _lstPrograms.SelectedItem.ToString();
            string name = item.Split(new string[] { " -> " }, StringSplitOptions.None)[0];
            string error;
            Firewall.RemoveProgram(name, out error);
            _lstPrograms.Items.RemoveAt(_lstPrograms.SelectedIndex);
        }

        private void RemovePort_Click(object sender, EventArgs e)
        {
            if (_lstPorts.SelectedIndex < 0) return;
            string item = _lstPorts.SelectedItem.ToString();
            string name = item.Split(new string[] { " (" }, StringSplitOptions.None)[0];
            string error;
            Firewall.RemovePort(name, out error);
            _lstPorts.Items.RemoveAt(_lstPorts.SelectedIndex);
            RefreshStatus();
        }

        private void AddPort_Click(ComboBox proto)
        {
            int port;
            if (!int.TryParse(_txtPort.Text, out port))
            {
                MessageBox.Show(this, L10n.T("Puerto invalido."), "Cortafuegos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string name = "CG-Port-" + port + "-" + proto.SelectedItem;
            string error;
            if (Firewall.AddPort(name, port, proto.SelectedItem.ToString(), true, out error))
            {
                _lstPorts.Items.Add(name + " (" + proto.SelectedItem + "/" + port + ")");
            }
            else
                MessageBox.Show(this, error, "Cortafuegos", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshStatus();
        }

        private static string PortDescription(int port)
        {
            switch (port)
            {
                case 20: return "FTP (datos)";
                case 21: return "FTP (control)";
                case 22: return "SSH";
                case 23: return "Telnet";
                case 25: return "SMTP (correo salida)";
                case 53: return "DNS";
                case 67:
                case 68: return "DHCP";
                case 80: return "HTTP (web)";
                case 110: return "POP3 (correo)";
                case 143: return "IMAP (correo)";
                case 443: return "HTTPS (web segura)";
                case 445: return "SMB (recursos compartidos)";
                case 3306: return "MySQL";
                case 3389: return "RDP (escritorio remoto)";
                case 5432: return "PostgreSQL";
                case 5900: return "VNC";
                case 8080: return "HTTP alternativo";
                default: return string.Empty;
            }
        }

        private static string GetDictVal(Dictionary<string, string> b, params string[] keys)
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
