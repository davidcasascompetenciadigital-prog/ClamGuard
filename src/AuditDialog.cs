using System.Drawing;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClamGuard
{
    public sealed class AuditDialog : Form
    {
        private TextBox _txtPath;
        private ComboBox _cboUser;
        private CheckBox _chkRead;
        private CheckBox _chkWrite;
        private CheckBox _chkExecute;
        private Label _lblStatus;
        private RichTextBox _rtbEvents;
        private System.Windows.Forms.Timer _timer;
        private DateTime _lastReadUtc = DateTime.UtcNow.AddHours(-1);

        public AuditDialog()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Auditoria de archivos";
            ClientSize = new Size(820, 620);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(760, 560);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Surface;
            Font = Theme.Ui(9f, FontStyle.Regular);

            Label title = new Label();
            title.Text = "Auditoria de archivos de Windows";
            title.Font = Theme.Ui(14f, FontStyle.Bold);
            title.ForeColor = Theme.TextPrimary;
            title.BackColor = Theme.Surface;
            title.AutoSize = true;
            title.Location = new Point(24, 16);
            Controls.Add(title);

            _lblStatus = new Label();
            _lblStatus.Text = "Estado: consultando...";
            _lblStatus.Font = Theme.Ui(8.75f, FontStyle.Regular);
            _lblStatus.ForeColor = Theme.TextSecondary;
            _lblStatus.BackColor = Theme.Surface;
            _lblStatus.AutoSize = false;
            _lblStatus.Location = new Point(24, 44);
            _lblStatus.Size = new Size(772, 20);
            Controls.Add(_lblStatus);

            Label pathLabel = new Label();
            pathLabel.Text = "Carpeta a auditar:";
            pathLabel.Font = Theme.Ui(9f, FontStyle.Regular);
            pathLabel.ForeColor = Theme.TextPrimary;
            pathLabel.BackColor = Theme.Surface;
            pathLabel.AutoSize = true;
            pathLabel.Location = new Point(24, 76);
            Controls.Add(pathLabel);

            _txtPath = new TextBox();
            _txtPath.Location = new Point(24, 98);
            _txtPath.Size = new Size(520, 24);
            Controls.Add(_txtPath);

            RoundedButton browse = new RoundedButton();
            browse.Text = "Examinar";
            browse.Style = ButtonStyle.Secondary;
            browse.BackColor = Theme.Surface;
            browse.Size = new Size(100, 28);
            browse.Location = new Point(552, 96);
            browse.Click += delegate
            {
                using (FolderBrowserDialog dlg = new FolderBrowserDialog())
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK) _txtPath.Text = dlg.SelectedPath;
                }
            };
            Controls.Add(browse);

            Label userLabel = new Label();
            userLabel.Text = "Usuario:";
            userLabel.Font = Theme.Ui(9f, FontStyle.Regular);
            userLabel.ForeColor = Theme.TextPrimary;
            userLabel.BackColor = Theme.Surface;
            userLabel.AutoSize = true;
            userLabel.Location = new Point(24, 132);
            Controls.Add(userLabel);

            _cboUser = new ComboBox();
            _cboUser.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboUser.Items.AddRange(new object[] { "Todos (Everyone)", "Usuario actual" });
            _cboUser.SelectedIndex = 0;
            _cboUser.Location = new Point(24, 154);
            _cboUser.Size = new Size(200, 24);
            Controls.Add(_cboUser);

            _chkRead = new CheckBox();
            _chkRead.Text = "Lectura";
            _chkRead.Checked = true;
            _chkRead.Location = new Point(240, 154);
            _chkRead.AutoSize = true;
            Controls.Add(_chkRead);

            _chkWrite = new CheckBox();
            _chkWrite.Text = "Escritura";
            _chkWrite.Checked = true;
            _chkWrite.Location = new Point(320, 154);
            _chkWrite.AutoSize = true;
            Controls.Add(_chkWrite);

            _chkExecute = new CheckBox();
            _chkExecute.Text = "Ejecucion";
            _chkExecute.Checked = true;
            _chkExecute.Location = new Point(410, 154);
            _chkExecute.AutoSize = true;
            Controls.Add(_chkExecute);

            RoundedButton enablePolicy = new RoundedButton();
            enablePolicy.Text = "Activar auditoria";
            enablePolicy.Style = ButtonStyle.Primary;
            enablePolicy.BackColor = Theme.Surface;
            enablePolicy.Size = new Size(150, 34);
            enablePolicy.Location = new Point(24, 190);
            enablePolicy.Click += EnablePolicy_Click;
            Controls.Add(enablePolicy);

            RoundedButton disablePolicy = new RoundedButton();
            disablePolicy.Text = "Desactivar auditoria";
            disablePolicy.Style = ButtonStyle.Secondary;
            disablePolicy.BackColor = Theme.Surface;
            disablePolicy.Size = new Size(160, 34);
            disablePolicy.Location = new Point(186, 190);
            disablePolicy.Click += DisablePolicy_Click;
            Controls.Add(disablePolicy);

            RoundedButton applySacl = new RoundedButton();
            applySacl.Text = "Aplicar a carpeta";
            applySacl.Style = ButtonStyle.Primary;
            applySacl.BackColor = Theme.Surface;
            applySacl.Size = new Size(150, 34);
            applySacl.Location = new Point(358, 190);
            applySacl.Click += ApplySacl_Click;
            Controls.Add(applySacl);

            RoundedButton readEvents = new RoundedButton();
            readEvents.Text = "Leer eventos ahora";
            readEvents.Style = ButtonStyle.Secondary;
            readEvents.BackColor = Theme.Surface;
            readEvents.Size = new Size(160, 34);
            readEvents.Location = new Point(520, 190);
            readEvents.Click += ReadEvents_Click;
            Controls.Add(readEvents);

            _rtbEvents = new RichTextBox();
            _rtbEvents.Font = new Font("Consolas", 8.5f, FontStyle.Regular);
            _rtbEvents.Location = new Point(24, 236);
            _rtbEvents.Size = new Size(772, 350);
            _rtbEvents.BackColor = Theme.ConsoleBack;
            _rtbEvents.ForeColor = Theme.ConsoleText;
            _rtbEvents.ReadOnly = true;
            _rtbEvents.BorderStyle = BorderStyle.None;
            _rtbEvents.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_rtbEvents);

            RoundedButton close = new RoundedButton();
            close.Text = "Cerrar";
            close.Style = ButtonStyle.Secondary;
            close.BackColor = Theme.Surface;
            close.Size = new Size(104, 34);
            close.Location = new Point(692, 580);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.Click += delegate { Close(); };
            Controls.Add(close);
            CancelButton = close;

            Shown += delegate { RefreshStatus(); StartTimer(); };
        }

        private void StartTimer()
        {
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 60000;
            _timer.Tick += delegate { ReadEvents(false); };
            _timer.Start();
        }

        private void RefreshStatus()
        {
            bool enabled = Audit.IsPolicyEnabled();
            _lblStatus.Text = enabled ? "Auditoria activa" : "Auditoria desactivada";
            if (!Audit.IsElevated)
                _lblStatus.Text += " (sin permisos de administrador)";
        }

        private string GetRights()
        {
            List<string> rights = new List<string>();
            if (_chkRead.Checked) rights.Add("Read");
            if (_chkWrite.Checked) rights.Add("Write");
            if (_chkExecute.Checked) rights.Add("ExecuteFile");
            if (rights.Count == 0) rights.Add("Read");
            return string.Join(",", rights.ToArray());
        }

        private void EnablePolicy_Click(object sender, EventArgs e)
        {
            if (!Audit.IsElevated)
            {
                MessageBox.Show(this, "Se necesitan permisos de administrador.", "Auditoria", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string error;
            if (Audit.EnablePolicy(out error))
                MessageBox.Show(this, "Auditoria activada.", "Auditoria", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(this, error, "Auditoria", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshStatus();
        }

        private void DisablePolicy_Click(object sender, EventArgs e)
        {
            if (!Audit.IsElevated)
            {
                MessageBox.Show(this, "Se necesitan permisos de administrador.", "Auditoria", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string error;
            if (Audit.DisablePolicy(out error))
                MessageBox.Show(this, "Auditoria desactivada.", "Auditoria", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(this, error, "Auditoria", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshStatus();
        }

        private void ApplySacl_Click(object sender, EventArgs e)
        {
            if (!Audit.IsElevated)
            {
                MessageBox.Show(this, "Se necesitan permisos de administrador.", "Auditoria", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string path = _txtPath.Text;
            string account = _cboUser.SelectedIndex == 0 ? "S-1-1-0" : Environment.UserName;
            string rights = GetRights();
            string error;
            if (Audit.SetSacl(path, account, rights, true, out error))
                MessageBox.Show(this, "Auditoria aplicada a la carpeta.", "Auditoria", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(this, error, "Auditoria", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void ReadEvents_Click(object sender, EventArgs e)
        {
            ReadEvents(true);
        }

        private void ReadEvents(bool manual)
        {
            if (!Audit.IsElevated) return;
            if (manual) _rtbEvents.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  Consultando eventos..." + Environment.NewLine);
            List<string> events = Audit.ReadRecentEvents(200, _lastReadUtc);
            _lastReadUtc = DateTime.UtcNow;
            if (events.Count == 0)
            {
                if (manual) _rtbEvents.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  No hay eventos nuevos." + Environment.NewLine);
                return;
            }
            _rtbEvents.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + events.Count + " evento(s) encontrado(s):" + Environment.NewLine);
            foreach (string ev in events)
            {
                _rtbEvents.AppendText(ev + Environment.NewLine);
            }
            _rtbEvents.ScrollToCaret();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_timer != null) { _timer.Stop(); _timer.Dispose(); _timer = null; }
            base.OnFormClosing(e);
        }
    }
}
