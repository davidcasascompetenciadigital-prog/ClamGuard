using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClamGuard
{
    public sealed class MainForm : Form
    {
        private AppSettings _settings;
        private CancellationTokenSource _operation;
        private bool _busy;
        private bool _refreshing;
        private bool _allowClose;

        private TableLayoutPanel _root;
        private CardPanel _banner;
        private IconBadge _bannerIcon;
        private Label _bannerTitle;
        private Label _bannerText;
        private RoundedButton _bannerButton;

        private Label _lblLastScan;
        private StatusPill _pillResident;
        private Label _lblSigDetail;
        private RoundedButton _btnQuick;
        private RoundedButton _btnResident;
        private RoundedButton _btnSignatures;
        private RoundedButton _btnIoc;
        private RoundedButton _btnFirewall;
        private RoundedButton _btnAudit;
        private RoundedButton _btnStop;
        private LinkLabel _lnkChoose;
        private LinkLabel _lnkFolders;
        private LinkLabel _lnkSigInfo;

        private ProgressBarEx _progress;
        private Label _lblActivity;
        private Label _lblPercent;
        private Label _lblSummary;
        private RichTextBox _logBox;
        private Font _logFont;
        private Font _logFontBold;

        private NotifyIcon _tray;
        private ToolStripMenuItem _trayResidentItem;
        private ToolStripMenuItem _trayScanItem;
        private ToolStripMenuItem _trayStateItem;
        private ToolStripMenuItem _trayUninstallItem;
        private System.Windows.Forms.Timer _trayTimer;
        private Icon _trayActiveIcon;
        private Icon _trayIdleIcon;
        private bool _trayHintShown;
        private bool _startUninstall;
        private ResidentState _lastResidentState = ResidentState.Unknown;

        public MainForm() : this(false)
        {
        }

        public MainForm(bool startUninstall)
        {
            _startUninstall = startUninstall;
            _settings = AppSettings.Load();
            AppPaths.EnsureLayout();
            AppLog.Init("clamguard");
            L10n.Load(_settings.Language);
            Uninstaller.Register();
            BuildUi();
            TrySetIcon();
            CreateTray();
            Shown += delegate
            {
                AppendLog(LogLevel.Info, "ClamGuard " + AppPaths.ProductVersion + " iniciado.");
                RefreshStatusAsync();
                LogDefenderStatusAsync();
                Task.Run(delegate
                {
                    Defender.RecoverIfNeeded(new Action<LogLevel, string>(LogFromAnyThread));
                });
                Task.Run(delegate
                {
                    UpdateInfo info = UpdateChecker.Check();
                    if (info != null && !IsDisposed)
                    {
                        LogFromAnyThread(LogLevel.Ok, "Nueva version disponible: " + info.Version);
                        BeginInvoke(new Action(delegate
                        {
                            DialogResult answer = MessageBox.Show(this,
                                "Hay una nueva version disponible: " + info.Version + "\n\n¿Deseas descargarla?",
                                "Actualizacion disponible", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                            if (answer == DialogResult.Yes)
                            {
                                try
                                {
                                    string url = string.IsNullOrEmpty(info.DownloadUrl) ? info.ReleaseUrl : info.DownloadUrl;
                                    if (!string.IsNullOrEmpty(url)) Process.Start(url);
                                }
                                catch { }
                            }
                        }));
                    }
                    else
                    {
                        LogFromAnyThread(LogLevel.Info, "No hay actualizaciones disponibles.");
                    }
                });

                if (_startUninstall)
                {
                    BeginInvoke(new Action(ShowUninstall));
                }
            };
        }

        private void TrySetIcon()
        {
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------
        // Icono en el área de notificación (bandeja del sistema)
        // ------------------------------------------------------------------

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private void CreateTray()
        {
            _trayActiveIcon = BuildTrayIcon(true);
            _trayIdleIcon = BuildTrayIcon(false);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;
            menu.Font = Theme.Ui(9f, FontStyle.Regular);

            ToolStripMenuItem open = new ToolStripMenuItem("Abrir ClamGuard");
            open.Font = new Font(menu.Font.FontFamily, menu.Font.Size, FontStyle.Bold);
            open.Click += delegate { ShowFromTray(); };
            menu.Items.Add(open);

            _trayScanItem = new ToolStripMenuItem("Análisis rápido");
            _trayScanItem.Click += delegate { TrayQuickScan(); };
            menu.Items.Add(_trayScanItem);

            menu.Items.Add(new ToolStripSeparator());

            _trayStateItem = new ToolStripMenuItem("Protección residente: desconocida");
            _trayStateItem.Enabled = false;
            menu.Items.Add(_trayStateItem);

            _trayResidentItem = new ToolStripMenuItem("Activar protección residente");
            _trayResidentItem.Click += delegate { TrayResidentAction(); };
            menu.Items.Add(_trayResidentItem);

            menu.Items.Add(new ToolStripSeparator());

            _trayUninstallItem = new ToolStripMenuItem("Desinstalar ClamGuard…");
            _trayUninstallItem.Click += delegate { TrayUninstall(); };
            menu.Items.Add(_trayUninstallItem);

            ToolStripMenuItem exit = new ToolStripMenuItem("Salir de ClamGuard");
            exit.Click += delegate { TrayExit(); };
            menu.Items.Add(exit);

            _tray = new NotifyIcon();
            _tray.Text = L10n.T("ClamGuard");
            _tray.Icon = _trayIdleIcon != null ? _trayIdleIcon : SystemIcons.Application;
            _tray.ContextMenuStrip = menu;
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ShowFromTray(); };
            _tray.BalloonTipClicked += delegate { ShowFromTray(); };

            _trayTimer = new System.Windows.Forms.Timer();
            _trayTimer.Interval = 7000;
            _trayTimer.Tick += delegate
            {
                if (IsDisposed) return;
                UpdateTray(ResidentService.GetState());
            };
            _trayTimer.Start();

            UpdateTray(ResidentService.GetState());
        }

        /// <summary>
        /// Genera el icono de la bandeja con un punto verde (residente activo)
        /// o gris (residente inactivo) sobre el icono de la aplicación.
        /// </summary>
        private static Icon BuildTrayIcon(bool active)
        {
            try
            {
                using (Bitmap bitmap = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        try
                        {
                            using (Icon baseIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                            {
                                if (baseIcon != null) g.DrawIcon(baseIcon, new Rectangle(0, 0, 32, 32));
                            }
                        }
                        catch
                        {
                        }

                        Color dot = active
                            ? Color.FromArgb(34, 197, 94)
                            : Color.FromArgb(148, 163, 184);
                        using (Brush brush = new SolidBrush(dot))
                            g.FillEllipse(brush, 19, 19, 13, 13);
                        using (Pen pen = new Pen(Color.White, 2f))
                            g.DrawEllipse(pen, 19, 19, 13, 13);
                    }

                    IntPtr handle = bitmap.GetHicon();
                    try
                    {
                        using (Icon temp = Icon.FromHandle(handle))
                        {
                            return (Icon)temp.Clone();
                        }
                    }
                    finally
                    {
                        DestroyIcon(handle);
                    }
                }
            }
            catch
            {
                return (Icon)SystemIcons.Application.Clone();
            }
        }

        private void UpdateTray(ResidentState state)
        {
            if (_tray == null || IsDisposed) return;
            bool active = state == ResidentState.Running;
            try
            {
                _tray.Icon = active ? _trayActiveIcon : _trayIdleIcon;
                _tray.Text = active
                    ? "ClamGuard · Protección residente activa"
                    : "ClamGuard · Protección residente inactiva";

                if (_trayStateItem != null)
                {
                    _trayStateItem.Text = L10n.T("Protección residente: ") + ResidentService.DescribeState(state);
                }
                if (_trayResidentItem != null)
                {
                    _trayResidentItem.Text = state == ResidentState.Running
                        ? "Detener protección residente"
                        : (state == ResidentState.NotInstalled
                            ? "Instalar protección residente"
                            : "Activar protección residente");
                    _trayResidentItem.Enabled = !_busy &&
                        state != ResidentState.StartPending && state != ResidentState.StopPending;
                }
                if (_trayScanItem != null) _trayScanItem.Enabled = !_busy;
                if (_trayUninstallItem != null) _trayUninstallItem.Enabled = !_busy;
            }
            catch
            {
            }

            if (active && _lastResidentState != ResidentState.Running && !Visible && !_trayHintShown)
            {
                _trayHintShown = true;
                ShowTrayBalloon("ClamGuard",
                    "La protección residente está activa. ClamGuard sigue analizando en segundo plano.");
            }
            _lastResidentState = state;
        }

        private void ShowTrayBalloon(string title, string text)
        {
            try
            {
                if (_tray == null) return;
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = text;
                _tray.BalloonTipIcon = ToolTipIcon.Info;
                _tray.ShowBalloonTip(5000);
            }
            catch
            {
            }
        }

        private void ShowFromTray()
        {
            try
            {
                ShowInTaskbar = true;
                if (!Visible) Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                BringToFront();
            }
            catch
            {
            }
        }

        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                ShowTrayBalloon("ClamGuard sigue activo",
                    "La protección residente continúa en segundo plano. Usa el icono de la bandeja para volver a abrir ClamGuard.");
            }
        }

        private void TrayQuickScan()
        {
            ShowFromTray();
            QuickScan_Click(null, EventArgs.Empty);
        }

        private void TrayResidentAction()
        {
            ShowFromTray();
            Resident_Click(null, EventArgs.Empty);
        }

        private void TrayUninstall()
        {
            if (_busy) return;
            ShowFromTray();
            ShowUninstall();
        }

        private async void TrayExit()
        {
            if (_busy)
            {
                DialogResult busyAnswer = MessageBox.Show(
                    "Hay una tarea en curso. Si sales ahora, la tarea se cancelará.\n\n¿Salir de todos modos?",
                    "Salir de ClamGuard", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (busyAnswer != DialogResult.Yes) return;
                if (_operation != null)
                {
                    try { _operation.Cancel(); } catch { }
                }
            }

            if (ResidentService.IsRunning())
            {
                DialogResult answer = MessageBox.Show(
                    "La protección residente está activa.\n\n" +
                    "Si sales de ClamGuard, se detendrá el residente y Windows Defender volverá a activarse.\n\n" +
                    "¿Detener la protección residente y salir?",
                    "Salir de ClamGuard", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;

                SetBusy(true, "Deteniendo el servicio residente…", false);
                string error = string.Empty;
                bool ok = false;
                try
                {
                    ok = await Task.Run(delegate { return ResidentService.Stop(out error); });
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                SetBusy(false, "Listo.", false);
                if (!ok)
                {
                    AppendLog(LogLevel.Error, error);
                    MessageBox.Show(error, "Protección residente", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                AppendLog(LogLevel.Ok, "Protección residente detenida.");
            }

            _allowClose = true;
            Close();
        }

        private async void LogDefenderStatusAsync()
        {
            DefenderStatus status = null;
            try
            {
                status = await Task.Run(delegate { return Defender.Query(); });
            }
            catch
            {
            }
            if (IsDisposed || status == null) return;

            bool conflict = status.Present && status.RealTimeEnabled && ResidentService.IsRunning();
            AppendLog(conflict ? LogLevel.Warn : LogLevel.Info,
                "Windows Defender: " + Defender.Describe(status) + ".");
            if (conflict)
            {
                AppendLog(LogLevel.Warn,
                    "Windows Defender sigue protegiendo en tiempo real mientras el residente está activo. " +
                    "ClamGuard mantiene exclusiones para sus carpetas; revisa la configuración si hay análisis duplicados.");
            }
        }

        private void BuildUi()
        {
            SuspendLayout();
            Text = L10n.T("ClamGuard — Antivirus ClamAV");
            BackColor = Theme.Background;
            ClientSize = new Size(1100, 780);
            MinimumSize = new Size(980, 700);
            StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.Ui(9f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            DoubleBuffered = true;
            KeyPreview = true;

            Panel header = BuildHeader();

            _root = new TableLayoutPanel();
            _root.Dock = DockStyle.Fill;
            _root.BackColor = Theme.Background;
            _root.Padding = new Padding(24, 18, 24, 10);
            _root.ColumnCount = 1;
            _root.RowCount = 4;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 226f));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));

            _banner = BuildBanner();
            _root.Controls.Add(_banner, 0, 0);

            TableLayoutPanel cards = new TableLayoutPanel();
            cards.Dock = DockStyle.Fill;
            cards.BackColor = Theme.Background;
            cards.Margin = new Padding(0);
            cards.ColumnCount = 6;
            cards.RowCount = 1;
            for (int i = 0; i < 6; i++)
                cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.666f));
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            CardPanel scanCard = BuildScanCard();
            scanCard.Margin = new Padding(0, 0, 8, 0);
            CardPanel residentCard = BuildResidentCard();
            residentCard.Margin = new Padding(0, 0, 8, 0);
            CardPanel signatureCard = BuildSignatureCard();
            signatureCard.Margin = new Padding(0, 0, 8, 0);
            CardPanel iocCard = BuildIocCard();
            iocCard.Margin = new Padding(0, 0, 8, 0);
            CardPanel firewallCard = BuildFirewallCard();
            firewallCard.Margin = new Padding(0, 0, 8, 0);
            CardPanel auditCard = BuildAuditCard();
            auditCard.Margin = new Padding(0, 0, 0, 0);
            cards.Controls.Add(scanCard, 0, 0);
            cards.Controls.Add(residentCard, 1, 0);
            cards.Controls.Add(signatureCard, 2, 0);
            cards.Controls.Add(iocCard, 3, 0);
            cards.Controls.Add(firewallCard, 4, 0);
            cards.Controls.Add(auditCard, 5, 0);
            _root.Controls.Add(cards, 0, 1);

            CardPanel activity = BuildActivityCard();
            _root.Controls.Add(activity, 0, 2);

            _root.Controls.Add(BuildFooter(), 0, 3);

            Controls.Add(_root);
            Controls.Add(header);

            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape && _btnStop.Visible) StopOperation();
            };
            FormClosing += OnFormClosing;

            ResumeLayout(true);
            PerformLayout();
        }

        private Panel BuildHeader()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 80;
            header.BackColor = Theme.Surface;
            header.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            };

            IconBadge logo = new IconBadge();
            logo.Glyph = Theme.GlyphShield;
            logo.BadgeColor = Theme.Accent;
            logo.GlyphColor = Color.White;
            logo.CornerRadius = 12;
            logo.BackColor = Theme.Surface;
            logo.Size = new Size(44, 44);
            logo.Location = new Point(24, 18);
            header.Controls.Add(logo);

            Label title = MakeLabel("ClamGuard", 18f, FontStyle.Bold, Theme.TextPrimary);
            title.Location = new Point(82, 12);
            header.Controls.Add(title);

            Label subtitle = MakeLabel("Protección antivirus con el motor ClamAV", 9f, FontStyle.Regular, Theme.TextSecondary);
            subtitle.Location = new Point(84, 44);
            header.Controls.Add(subtitle);

            FlowLayoutPanel links = new FlowLayoutPanel();
            links.Dock = DockStyle.Right;
            links.Width = 700;
            links.FlowDirection = FlowDirection.RightToLeft;
            links.WrapContents = false;
            links.BackColor = Theme.Surface;
            links.Padding = new Padding(0, 27, 24, 0);
            links.Margin = new Padding(0);

            LinkLabel about = Links.Create(L10n.T("menu.about"), delegate { ShowAbout(); });
            LinkLabel quarantine = Links.Create(L10n.T("menu.quarantine"), delegate { ShowQuarantine(); });
            LinkLabel settings = Links.Create(L10n.T("menu.settings"), delegate { ShowSettings(); });
            LinkLabel ioc = Links.Create(L10n.T("menu.ioc"), delegate { ShowIoc(); });
            LinkLabel firewall = Links.Create(L10n.T("menu.firewall"), delegate { ShowFirewall(); });
            LinkLabel updates = Links.Create("Actualizaciones", delegate { CheckUpdates(); });
            LinkLabel audit = Links.Create("Auditoría", delegate { ShowAudit(); });
            LinkLabel uninstall = Links.Create(L10n.T("menu.uninstall"), delegate { ShowUninstall(); });
            foreach (LinkLabel link in new LinkLabel[] { about, quarantine, settings, ioc, firewall, updates, audit, uninstall })
            {
                link.BackColor = Theme.Surface;
                link.Margin = new Padding(16, 0, 0, 0);
                links.Controls.Add(link);
            }
            header.Controls.Add(links);
            return header;
        }

        private CardPanel BuildBanner()
        {
            CardPanel banner = new CardPanel();
            banner.Dock = DockStyle.Fill;
            banner.Margin = new Padding(0, 0, 0, 14);
            banner.FillColor = Theme.AccentSoft;
            banner.BorderColor = Theme.AccentBorder;
            banner.CornerRadius = 12;
            banner.Visible = false;

            _bannerIcon = new IconBadge();
            _bannerIcon.Glyph = Theme.GlyphInfo;
            _bannerIcon.BadgeColor = Color.White;
            _bannerIcon.GlyphColor = Theme.Accent;
            _bannerIcon.BackColor = Theme.AccentSoft;
            _bannerIcon.Size = new Size(38, 38);
            _bannerIcon.Location = new Point(18, 17);
            banner.Controls.Add(_bannerIcon);

            _bannerTitle = MakeLabel("ClamAV no está instalado", 10f, FontStyle.Bold, Theme.TextPrimary);
            _bannerTitle.BackColor = Theme.AccentSoft;
            _bannerTitle.Location = new Point(68, 11);
            banner.Controls.Add(_bannerTitle);

            _bannerText = MakeLabel(string.Empty, 8.75f, FontStyle.Regular, Theme.TextSecondary);
            _bannerText.BackColor = Theme.AccentSoft;
            _bannerText.AutoSize = false;
            _bannerText.Location = new Point(69, 34);
            _bannerText.Height = 22;
            banner.Controls.Add(_bannerText);

            _bannerButton = new RoundedButton();
            _bannerButton.Text = L10n.T("Instalar ClamAV");
            _bannerButton.Style = ButtonStyle.Primary;
            _bannerButton.BackColor = Theme.AccentSoft;
            _bannerButton.Size = new Size(176, 36);
            _bannerButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _bannerButton.Click += delegate { BannerAction(); };
            banner.Controls.Add(_bannerButton);

            banner.Resize += delegate
            {
                if (_bannerButton != null)
                    _bannerButton.Location = new Point(banner.ClientSize.Width - _bannerButton.Width - 18, 17);
                if (_bannerText != null)
                    _bannerText.Width = Math.Max(100, banner.ClientSize.Width - 280);
            };
            return banner;
        }

        private CardPanel CreateBaseCard(string glyph, Color badgeColor, Color glyphColor,
            string title, string description, out Label status)
        {
            CardPanel card = new CardPanel();
            card.Dock = DockStyle.Fill;
            card.FillColor = Theme.Surface;
            card.CornerRadius = 12;

            IconBadge badge = new IconBadge();
            badge.Glyph = glyph;
            badge.BadgeColor = badgeColor;
            badge.GlyphColor = glyphColor;
            badge.BackColor = Theme.Surface;
            badge.Size = new Size(42, 42);
            badge.Location = new Point(20, 18);
            card.Controls.Add(badge);

            Label lblTitle = MakeLabel(title, 12f, FontStyle.Bold, Theme.TextPrimary);
            lblTitle.BackColor = Theme.Surface;
            lblTitle.Location = new Point(20, 68);
            card.Controls.Add(lblTitle);

            Label lblDescription = MakeLabel(description, 8.75f, FontStyle.Regular, Theme.TextSecondary);
            lblDescription.BackColor = Theme.Surface;
            lblDescription.AutoSize = false;
            lblDescription.Location = new Point(20, 96);
            lblDescription.Height = 38;
            card.Controls.Add(lblDescription);

            status = MakeLabel(string.Empty, 8.5f, FontStyle.Regular, Theme.TextMuted);
            status.BackColor = Theme.Surface;
            status.AutoSize = false;
            status.Location = new Point(20, 135);
            status.Height = 20;
            card.Controls.Add(status);

            Label resizableStatus = status;
            card.Resize += delegate
            {
                int width = Math.Max(100, card.ClientSize.Width - 40);
                lblDescription.Width = width;
                resizableStatus.Width = width;
            };
            return card;
        }

        private CardPanel BuildScanCard()
        {
            Label status;
            CardPanel card = CreateBaseCard(Theme.GlyphScan, Theme.AccentSoft, Theme.Accent,
                "Analizar el equipo", "Busca virus y programas dañinos en las carpetas habituales.", out status);
            _lblLastScan = status;
            Label statusLabel = status;

            _btnQuick = new RoundedButton();
            _btnQuick.Text = L10n.T("Análisis rápido");
            _btnQuick.Style = ButtonStyle.Primary;
            _btnQuick.Location = new Point(20, 160);
            _btnQuick.Height = 38;
            _btnQuick.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _btnQuick.Click += QuickScan_Click;
            card.Controls.Add(_btnQuick);

            _lnkChoose = Links.Create("Elegir otra carpeta…", ChooseFolder_Click);
            _lnkChoose.BackColor = Theme.Surface;
            _lnkChoose.Location = new Point(20, 201);
            card.Controls.Add(_lnkChoose);

            card.Resize += delegate { _btnQuick.Width = Math.Max(100, card.ClientSize.Width - 40); };
            return card;
        }

        private CardPanel BuildResidentCard()
        {
            Label status;
            CardPanel card = CreateBaseCard(Theme.GlyphShield, Theme.SuccessSoft, Theme.Success,
                "Protección residente", "Analiza en segundo plano cada archivo nuevo o modificado.", out status);
            status.Visible = false;

            _pillResident = new StatusPill();
            _pillResident.BackColor = Theme.Surface;
            _pillResident.Location = new Point(20, 134);
            _pillResident.SetState("No instalada", Color.FromArgb(248, 250, 252), Theme.TextMuted, Theme.Border);
            card.Controls.Add(_pillResident);

            _btnResident = new RoundedButton();
            _btnResident.Text = L10n.T("Instalar residente");
            _btnResident.Style = ButtonStyle.Primary;
            _btnResident.Location = new Point(20, 160);
            _btnResident.Height = 38;
            _btnResident.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _btnResident.Click += Resident_Click;
            card.Controls.Add(_btnResident);

            _lnkFolders = Links.Create("Configurar carpetas vigiladas", delegate { ShowSettings(1); });
            _lnkFolders.BackColor = Theme.Surface;
            _lnkFolders.Location = new Point(20, 201);
            card.Controls.Add(_lnkFolders);

            card.Resize += delegate { _btnResident.Width = Math.Max(100, card.ClientSize.Width - 40); };
            return card;
        }

        private CardPanel BuildSignatureCard()
        {
            Label status;
            CardPanel card = CreateBaseCard(Theme.GlyphRefresh, Color.FromArgb(250, 245, 255), Color.FromArgb(124, 58, 237),
                "Firmas de virus", "Mantén actualizada la base de firmas de ClamAV.", out status);
            _lblSigDetail = status;

            _btnSignatures = new RoundedButton();
            _btnSignatures.Text = L10n.T("Comprobar y actualizar");
            _btnSignatures.Style = ButtonStyle.Primary;
            _btnSignatures.Location = new Point(20, 160);
            _btnSignatures.Height = 38;
            _btnSignatures.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _btnSignatures.Click += Signatures_Click;
            card.Controls.Add(_btnSignatures);

            _lnkSigInfo = Links.Create("Ver detalles de las firmas", delegate { ShowSignatureDetails(); });
            _lnkSigInfo.BackColor = Theme.Surface;
            _lnkSigInfo.Location = new Point(20, 201);
            card.Controls.Add(_lnkSigInfo);

            card.Resize += delegate { _btnSignatures.Width = Math.Max(100, card.ClientSize.Width - 40); };
            return card;
        }

        private CardPanel BuildIocCard()
        {
            Label status;
            CardPanel card = CreateBaseCard(Theme.GlyphInfo, Theme.AccentSoft, Theme.Accent,
                "Indicadores de compromiso", "Busca IOCs (STIX2) en el equipo.", out status);
            status.Text = L10n.T("IOCs disponibles: ") + IocStore.LocalIndicatorCount();

            _btnIoc = new RoundedButton();
            _btnIoc.Text = L10n.T("Buscar IOCs");
            _btnIoc.Style = ButtonStyle.Primary;
            _btnIoc.Location = new Point(20, 160);
            _btnIoc.Height = 38;
            _btnIoc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _btnIoc.Click += delegate { ShowIoc(); };
            card.Controls.Add(_btnIoc);

            card.Resize += delegate { _btnIoc.Width = Math.Max(100, card.ClientSize.Width - 40); };
            return card;
        }

        private CardPanel BuildFirewallCard()
        {
            Label status;
            CardPanel card = CreateBaseCard(Theme.GlyphShield, Theme.DangerSoft, Theme.Danger,
                "Cortafuegos", "Administra el firewall de Windows.", out status);
            status.Text = L10n.T("Modo activo por defecto");

            _btnFirewall = new RoundedButton();
            _btnFirewall.Text = L10n.T("Abrir cortafuegos");
            _btnFirewall.Style = ButtonStyle.Primary;
            _btnFirewall.Location = new Point(20, 160);
            _btnFirewall.Height = 38;
            _btnFirewall.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _btnFirewall.Click += delegate { ShowFirewall(); };
            card.Controls.Add(_btnFirewall);

            card.Resize += delegate { _btnFirewall.Width = Math.Max(100, card.ClientSize.Width - 40); };
            return card;
        }

        private CardPanel BuildAuditCard()
        {
            Label status;
            CardPanel card = CreateBaseCard(Theme.GlyphInfo, Theme.AccentSoft, Theme.Accent,
                "Auditoría", "Auditoría de archivos de Windows.", out status);
            status.Text = "Eventos de acceso a archivos";

            _btnAudit = new RoundedButton();
            _btnAudit.Text = "Abrir auditoría";
            _btnAudit.Style = ButtonStyle.Primary;
            _btnAudit.Location = new Point(20, 160);
            _btnAudit.Height = 38;
            _btnAudit.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _btnAudit.Click += delegate { ShowAudit(); };
            card.Controls.Add(_btnAudit);

            card.Resize += delegate { _btnAudit.Width = Math.Max(100, card.ClientSize.Width - 40); };
            return card;
        }

        private CardPanel BuildActivityCard()
        {
            CardPanel card = new CardPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 14, 0, 0);
            card.FillColor = Theme.Surface;
            card.CornerRadius = 12;

            Label title = MakeLabel("Actividad", 11f, FontStyle.Bold, Theme.TextPrimary);
            title.BackColor = Theme.Surface;
            title.Location = new Point(20, 15);
            card.Controls.Add(title);

            _lblActivity = MakeLabel("Listo.", 9f, FontStyle.Regular, Theme.TextSecondary);
            _lblActivity.BackColor = Theme.Surface;
            _lblActivity.Location = new Point(104, 18);
            _lblActivity.AutoSize = false;
            _lblActivity.Height = 20;
            card.Controls.Add(_lblActivity);

            _lblPercent = MakeLabel(string.Empty, 9f, FontStyle.Bold, Theme.TextSecondary);
            _lblPercent.BackColor = Theme.Surface;
            _lblPercent.AutoSize = false;
            _lblPercent.Width = 44;
            _lblPercent.Height = 20;
            _lblPercent.TextAlign = ContentAlignment.MiddleRight;
            card.Controls.Add(_lblPercent);

            _btnStop = new RoundedButton();
            _btnStop.Text = L10n.T("Detener");
            _btnStop.Style = ButtonStyle.Secondary;
            _btnStop.Size = new Size(92, 30);
            _btnStop.Visible = false;
            _btnStop.Click += delegate { StopOperation(); };
            card.Controls.Add(_btnStop);

            _progress = new ProgressBarEx();
            _progress.BackColor = Theme.Surface;
            _progress.Location = new Point(20, 48);
            _progress.Height = 6;
            _progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(_progress);

            CardPanel console = new CardPanel();
            console.FillColor = Theme.ConsoleBack;
            console.BorderColor = Theme.ConsoleBack;
            console.CornerRadius = 8;
            console.DrawBorder = false;
            console.BackColor = Theme.Surface;
            console.Location = new Point(14, 66);
            console.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(console);

            _logFont = new Font("Consolas", 8.75f, FontStyle.Regular, GraphicsUnit.Point);
            _logFontBold = new Font("Consolas", 8.75f, FontStyle.Bold, GraphicsUnit.Point);
            _logBox = new RichTextBox();
            _logBox.Dock = DockStyle.Fill;
            _logBox.BorderStyle = BorderStyle.None;
            _logBox.BackColor = Theme.ConsoleBack;
            _logBox.ForeColor = Theme.ConsoleText;
            _logBox.Font = _logFont;
            _logBox.ReadOnly = true;
            _logBox.WordWrap = false;
            _logBox.DetectUrls = false;
            _logBox.TabStop = false;
            _logBox.ShortcutsEnabled = true;
            _logBox.Margin = new Padding(10);
            Panel consoleInner = new Panel();
            consoleInner.Dock = DockStyle.Fill;
            consoleInner.BackColor = Theme.ConsoleBack;
            consoleInner.Padding = new Padding(10, 8, 8, 8);
            consoleInner.Controls.Add(_logBox);
            console.Controls.Add(consoleInner);

            _lblSummary = MakeLabel("Sin actividad reciente.", 8.5f, FontStyle.Regular, Theme.TextMuted);
            _lblSummary.BackColor = Theme.Surface;
            _lblSummary.AutoSize = false;
            _lblSummary.Height = 18;
            _lblSummary.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(_lblSummary);

            card.Resize += delegate
            {
                int width = card.ClientSize.Width;
                int height = card.ClientSize.Height;
                _btnStop.Location = new Point(width - _btnStop.Width - 14, 10);
                _lblPercent.Location = new Point(_btnStop.Left - _lblPercent.Width - 6, 15);
                _lblActivity.Width = Math.Max(80, _lblPercent.Left - _lblActivity.Left - 12);
                _progress.Width = Math.Max(80, width - 40);
                int consoleHeight = height - 66 - 36;
                if (consoleHeight < 40) consoleHeight = 40;
                console.Bounds = new Rectangle(14, 66, Math.Max(80, width - 28), consoleHeight);
                _lblSummary.Location = new Point(20, height - 27);
                _lblSummary.Width = Math.Max(80, width - 40);
            };
            return card;
        }

        private Panel BuildFooter()
        {
            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.BackColor = Theme.Background;
            footer.Margin = new Padding(0);

            Label note = MakeLabel("ClamAV® es un proyecto de Cisco Talos. ClamGuard es software libre, sin garantías.",
                8.25f, FontStyle.Regular, Theme.TextMuted);
            note.BackColor = Theme.Background;
            note.Location = new Point(2, 7);
            footer.Controls.Add(note);

            Label version = MakeLabel("Versión " + AppPaths.ProductVersion, 8.25f, FontStyle.Regular, Theme.TextMuted);
            version.BackColor = Theme.Background;
            version.AutoSize = false;
            version.TextAlign = ContentAlignment.MiddleRight;
            version.Size = new Size(160, 18);
            version.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            footer.Controls.Add(version);

            footer.Resize += delegate
            {
                version.Location = new Point(footer.ClientSize.Width - version.Width, 7);
            };
            return footer;
        }

        private Label MakeLabel(string text, float size, FontStyle style, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Font = Theme.Ui(size, style);
            label.ForeColor = color;
            label.BackColor = Theme.Surface;
            label.Margin = new Padding(0);
            label.UseMnemonic = false;
            return label;
        }

        // ------------------------------------------------------------------
        // Registro visible
        // ------------------------------------------------------------------

        private void LogFromAnyThread(LogLevel level, string message)
        {
            if (IsDisposed || Disposing) return;
            try
            {
                if (InvokeRequired)
                    BeginInvoke(new Action<LogLevel, string>(AppendLog), level, message);
                else
                    AppendLog(level, message);
            }
            catch
            {
            }
        }

        private void AppendLog(LogLevel level, string message)
        {
            if (message == null) return;
            if (IsDisposed || _logBox == null || _logBox.IsDisposed) return;

            Color color;
            switch (level)
            {
                case LogLevel.Ok: color = Color.FromArgb(74, 222, 128); break;
                case LogLevel.Warn: color = Color.FromArgb(251, 191, 36); break;
                case LogLevel.Error: color = Color.FromArgb(248, 113, 113); break;
                case LogLevel.Threat: color = Color.FromArgb(251, 113, 133); break;
                default: color = Theme.ConsoleText; break;
            }

            try
            {
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.SelectionLength = 0;
                _logBox.SelectionColor = Color.FromArgb(100, 116, 139);
                _logBox.SelectionFont = _logFont;
                _logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  ");
                _logBox.SelectionColor = color;
                _logBox.SelectionFont = level == LogLevel.Threat ? _logFontBold : _logFont;
                _logBox.AppendText(message.Replace("\r", " ").Replace("\n", " "));
                _logBox.AppendText(Environment.NewLine);
                _logBox.SelectionColor = Theme.ConsoleText;

                if (_logBox.TextLength > 150000)
                {
                    int cut = _logBox.TextLength - 100000;
                    int index = _logBox.Text.IndexOf('\n', Math.Min(cut, _logBox.TextLength - 1));
                    if (index > 0) _logBox.Select(0, index + 1);
                    else _logBox.Select(0, cut);
                    _logBox.SelectedText = string.Empty;
                }

                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.ScrollToCaret();
            }
            catch
            {
            }

            AppLog.Write(level, message);
        }

        // ------------------------------------------------------------------
        // Estado
        // ------------------------------------------------------------------

        private async void RefreshStatusAsync()
        {
            if (_refreshing || IsDisposed) return;
            _refreshing = true;
            try
            {
                AppSettings settings = _settings;
                string dir = AppPaths.FindClamAvDirectory(settings.ClamAvDirectory);
                bool hasDb = ClamAv.HasDatabase(AppPaths.DatabaseDir);
                SignatureInfo signatures = ClamAv.GetSignatureInfo(AppPaths.DatabaseDir);
                EngineInfo engine = new EngineInfo();
                if (dir != null)
                {
                    EngineInfo queried = await Task.Run(delegate { return ClamAv.QueryEngine(settings); });
                    if (queried != null) engine = queried;
                }
                if (IsDisposed) return;

                if (dir == null)
                    SetBanner(true, false);
                else if (!hasDb)
                    SetBanner(true, true);
                else
                    SetBanner(false, false);

                if (dir == null)
                {
                    _lblSigDetail.Text = L10n.T("ClamAV no instalado");
                    _btnSignatures.Enabled = !_busy;
                }
                else if (!hasDb)
                {
                    _lblSigDetail.Text = L10n.T("Faltan las firmas de virus");
                }
                else
                {
                    string sigText = L10n.T("Firmas: ") + signatures.DateText;
                    if (signatures.TotalBytes > 0) sigText += " · " + ClamAv.FormatBytes(signatures.TotalBytes);
                    _lblSigDetail.Text = sigText;
                }
                _lnkSigInfo.Visible = dir != null;

                ResidentState state = ResidentService.GetState();
                ApplyResidentState(state);

                AppState appState = StateStore.Load();
                DateTime? lastScan = AppState.ParseUtc(appState.LastScanUtc);
                if (lastScan.HasValue)
                {
                    _lblLastScan.Text = L10n.T("Último análisis: ") + RelativeTime(lastScan) +
                        (appState.LastScanFiles > 0 ? " · " + appState.LastScanFiles.ToString("N0") + " archivos" : string.Empty);
                }
                else
                {
                    _lblLastScan.Text = L10n.T("Último análisis: nunca");
                }

                _lblActivity.Text = L10n.T("Listo.");
            }
            catch (Exception ex)
            {
                AppendLog(LogLevel.Warn, "No se pudo actualizar el estado: " + ex.Message);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void SetBanner(bool visible, bool signaturesMissing)
        {
            _root.RowStyles[0].Height = visible ? 72f : 0f;
            _banner.Visible = visible;
            if (!visible) return;

            if (signaturesMissing)
            {
                _banner.FillColor = Theme.WarningSoft;
                _banner.BorderColor = Theme.WarningBorder;
                _bannerIcon.BadgeColor = Color.White;
                _bannerIcon.GlyphColor = Theme.Warning;
                _bannerIcon.Glyph = Theme.GlyphWarning;
                _bannerTitle.BackColor = Theme.WarningSoft;
                _bannerText.BackColor = Theme.WarningSoft;
                _bannerButton.BackColor = Theme.WarningSoft;
                _bannerTitle.Text = L10n.T("Faltan las firmas de virus");
                _bannerText.Text = L10n.T("Descarga la base de firmas de ClamAV para poder detectar amenazas.");
                _bannerButton.Text = L10n.T("Descargar firmas");
            }
            else
            {
                _banner.FillColor = Theme.AccentSoft;
                _banner.BorderColor = Theme.AccentBorder;
                _bannerIcon.BadgeColor = Color.White;
                _bannerIcon.GlyphColor = Theme.Accent;
                _bannerIcon.Glyph = Theme.GlyphInfo;
                _bannerTitle.BackColor = Theme.AccentSoft;
                _bannerText.BackColor = Theme.AccentSoft;
                _bannerButton.BackColor = Theme.AccentSoft;
                _bannerTitle.Text = L10n.T("ClamAV no está instalado");
                _bannerText.Text = L10n.T("ClamGuard utiliza el motor gratuito ClamAV. Instálalo para empezar a proteger el equipo.");
                _bannerButton.Text = L10n.T("Instalar ClamAV");
            }
            _bannerIcon.Invalidate();
        }

        private void ApplyResidentState(ResidentState state)
        {
            switch (state)
            {
                case ResidentState.Running:
                    _pillResident.SetState("Activa", Theme.SuccessSoft, Theme.Success, Theme.SuccessBorder);
                    _btnResident.Text = L10n.T("Detener residente");
                    _btnResident.Style = ButtonStyle.Secondary;
                    break;
                case ResidentState.Stopped:
                    _pillResident.SetState("Detenida", Theme.WarningSoft, Theme.Warning, Theme.WarningBorder);
                    _btnResident.Text = L10n.T("Activar residente");
                    _btnResident.Style = ButtonStyle.Primary;
                    break;
                case ResidentState.StartPending:
                case ResidentState.StopPending:
                case ResidentState.Paused:
                    _pillResident.SetState("Cambiando…", Theme.AccentSoft, Theme.Accent, Theme.AccentBorder);
                    _btnResident.Text = L10n.T("Espera…");
                    _btnResident.Style = ButtonStyle.Secondary;
                    break;
                case ResidentState.NotInstalled:
                    _pillResident.SetState("No instalada", Color.FromArgb(248, 250, 252), Theme.TextMuted, Theme.Border);
                    _btnResident.Text = L10n.T("Instalar residente");
                    _btnResident.Style = ButtonStyle.Primary;
                    break;
                default:
                    _pillResident.SetState("Desconocida", Color.FromArgb(248, 250, 252), Theme.TextMuted, Theme.Border);
                    _btnResident.Text = L10n.T("Protección residente");
                    _btnResident.Style = ButtonStyle.Secondary;
                    break;
            }
            _btnResident.Enabled = !_busy;
            UpdateTray(state);
        }

        private static string RelativeTime(DateTime? utc)
        {
            if (!utc.HasValue) return "nunca";
            TimeSpan span = DateTime.UtcNow - utc.Value;
            if (span.TotalSeconds < 60) return "hace un momento";
            if (span.TotalMinutes < 60) return "hace " + (int)span.TotalMinutes + " min";
            if (span.TotalHours < 24) return "hace " + (int)span.TotalHours + " h";
            if (span.TotalDays < 7) return "hace " + (int)span.TotalDays + " días";
            return utc.Value.ToLocalTime().ToString("dd/MM/yyyy");
        }

        private bool EnsureEngine()
        {
            string dir = AppPaths.FindClamAvDirectory(_settings.ClamAvDirectory);
            if (dir == null)
            {
                MessageBox.Show(this,
                    "ClamAV todavía no está instalado.\n\nPulsa «Instalar ClamAV» en el aviso superior para descargarlo e instalarlo automáticamente.",
                    "ClamAV no instalado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SetBanner(true, false);
                return false;
            }
            if (!ClamAv.HasDatabase(AppPaths.DatabaseDir))
            {
                DialogResult answer = MessageBox.Show(this,
                    "Todavía no hay firmas de virus descargadas.\n\n¿Quieres descargarlas ahora?",
                    "Firmas ausentes", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer == DialogResult.Yes) Signatures_Click(null, EventArgs.Empty);
                return false;
            }
            return true;
        }

        private void SetBusy(bool busy, string status, bool cancellable)
        {
            _busy = busy;
            _btnQuick.Enabled = !busy;
            _btnSignatures.Enabled = !busy;
            _btnResident.Enabled = !busy;
            if (_btnIoc != null) _btnIoc.Enabled = !busy;
            if (_btnFirewall != null) _btnFirewall.Enabled = !busy;
            if (_btnAudit != null) _btnAudit.Enabled = !busy;
            _bannerButton.Enabled = !busy;
            _lnkChoose.Enabled = !busy;
            _lnkFolders.Enabled = !busy;
            _lnkSigInfo.Enabled = !busy;
            _progress.Cursor = Cursors.Default;
            _btnStop.Visible = busy && cancellable;
            _btnStop.Enabled = true;
            _btnStop.Text = L10n.T("Detener");
            if (!string.IsNullOrEmpty(status)) _lblActivity.Text = status;
        }

        private void StopOperation()
        {
            if (_operation == null) return;
            try
            {
                _operation.Cancel();
                _btnStop.Enabled = false;
                _btnStop.Text = L10n.T("Deteniendo…");
                AppendLog(LogLevel.Warn, "Cancelación solicitada.");
            }
            catch
            {
            }
        }

        private void EndOperation(string summary)
        {
            _progress.IsIndeterminate = false;
            _lblPercent.Text = string.Empty;
            _operation = null;
            SetBusy(false, "Listo.", false);
            if (summary != null) _lblSummary.Text = summary;
            RefreshStatusAsync();
        }

        // ------------------------------------------------------------------
        // Acciones
        // ------------------------------------------------------------------

        private async void QuickScan_Click(object sender, EventArgs e)
        {
            if (_busy || !EnsureEngine()) return;
            List<string> targets = Scanner.ExistingQuickTargets(_settings);
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "No hay carpetas válidas configuradas para el análisis rápido.",
                    "Análisis rápido", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            await StartScan(targets);
        }

        private async void ChooseFolder_Click(object sender, EventArgs e)
        {
            if (_busy || !EnsureEngine()) return;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Selecciona la carpeta que quieres analizar";
                dialog.ShowNewFolderButton = false;
                if (Directory.Exists(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
                    dialog.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                List<string> targets = new List<string>();
                targets.Add(dialog.SelectedPath);
                await StartScan(targets);
            }
        }

        private async Task StartScan(List<string> targets)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            _operation = cts;
            SetBusy(true, "Preparando análisis…", true);
            _progress.Value = 0;
            _progress.IsIndeterminate = true;
            _lblPercent.Text = string.Empty;
            AppendLog(LogLevel.Info, "Análisis solicitado sobre " + targets.Count + " ruta(s).");
            Stopwatch watch = Stopwatch.StartNew();

            Progress<ScanProgress> progress = new Progress<ScanProgress>(UpdateScanProgress);
            ScanResult result = null;
            try
            {
                result = await Task.Run(delegate
                {
                    return Scanner.Scan(_settings, targets, cts.Token, progress,
                        new Action<LogLevel, string>(LogFromAnyThread));
                });
            }
            catch (Exception ex)
            {
                AppendLog(LogLevel.Error, "Error al analizar: " + ex.Message);
            }

            watch.Stop();
            if (IsDisposed) return;
            _progress.IsIndeterminate = false;
            _lblPercent.Text = string.Empty;

            if (result == null)
            {
                EndOperation("El análisis no pudo completarse.");
                return;
            }

            if (result.Canceled)
            {
                EndOperation("Análisis cancelado.");
                return;
            }
            if (result.Failed)
            {
                EndOperation("El análisis falló.");
                MessageBox.Show(this, result.Error, "Análisis", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            StateStore.Mutate(delegate(AppState state)
            {
                state.LastScanUtc = AppState.ToUtc(DateTime.UtcNow);
                state.LastScanFiles = result.FilesScanned;
                state.LastScanThreats = result.Threats.Count;
            });

            string summary = string.Format("{0:N0} archivo(s) analizados · {1} amenaza(s) · {2:0.0} s",
                result.FilesScanned, result.Threats.Count, watch.Elapsed.TotalSeconds);
            EndOperation(summary);
            _progress.Value = 100;

            if (result.Threats.Count > 0)
            {
                using (ThreatDialog dialog = new ThreatDialog(result, _settings,
                    new Action<LogLevel, string>(LogFromAnyThread)))
                {
                    dialog.ShowDialog(this);
                }
                RefreshStatusAsync();
            }
            else
            {
                AppendLog(LogLevel.Ok, "El equipo está limpio. No se encontraron amenazas.");
            }
        }

        private void UpdateScanProgress(ScanProgress value)
        {
            if (value == null || IsDisposed) return;
            if (value.TotalFiles > 0)
            {
                _progress.IsIndeterminate = false;
                _progress.Value = value.Percent;
                _lblPercent.Text = value.Percent + "%";
                _lblActivity.Text = string.Format("{0} · {1:N0} de {2:N0} archivos",
                    string.IsNullOrEmpty(value.Phase) ? "Analizando" : value.Phase,
                    value.Files, value.TotalFiles);
            }
            else
            {
                _progress.IsIndeterminate = true;
                _lblPercent.Text = string.Empty;
                _lblActivity.Text = string.IsNullOrEmpty(value.Phase) ? "Preparando…" : value.Phase;
            }

            if (!string.IsNullOrEmpty(value.CurrentFile))
            {
                using (Graphics g = _lblSummary.CreateGraphics())
                {
                    _lblSummary.Text = Theme.Ellipsize(g, value.CurrentFile, _lblSummary.Font, _lblSummary.Width);
                }
            }
        }

        private async void Signatures_Click(object sender, EventArgs e)
        {
            if (_busy) return;
            string dir = AppPaths.FindClamAvDirectory(_settings.ClamAvDirectory);
            if (dir == null)
            {
                if (!EnsureEngine()) return;
            }
            await RunUpdate(true);
        }

        private async Task<bool> RunUpdate(bool showResult)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            _operation = cts;
            SetBusy(true, "Comprobando firmas de virus…", true);
            _progress.IsIndeterminate = true;
            _lblPercent.Text = string.Empty;
            AppendLog(LogLevel.Info, "Comprobando actualizaciones de firmas...");

            UpdateResult result = null;
            try
            {
                result = await Task.Run(delegate
                {
                    return ClamAv.RunFreshclam(_settings, cts.Token, delegate(string line)
                    {
                        LogFromAnyThread(LogLevel.Info, "freshclam: " + line);
                    });
                });
            }
            catch (Exception ex)
            {
                AppendLog(LogLevel.Error, "Error al actualizar: " + ex.Message);
            }

            if (IsDisposed) return false;
            _operation = null;
            SetBusy(false, "Listo.", false);
            _progress.IsIndeterminate = false;

            if (result == null)
            {
                _lblSummary.Text = L10n.T("No se pudo comprobar la actualización.");
                return false;
            }

            _progress.Value = result.Success ? 100 : 0;
            _lblSummary.Text = result.Message;
            if (result.Canceled)
            {
                AppendLog(LogLevel.Warn, "Actualización cancelada.");
            }
            else if (result.Success)
            {
                AppendLog(LogLevel.Ok, result.Message);
            }
            else
            {
                AppendLog(LogLevel.Error, result.Message);
                if (showResult)
                    MessageBox.Show(this, result.Message, "Actualización de firmas",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RefreshStatusAsync();
            return result.Success;
        }

        private async void Resident_Click(object sender, EventArgs e)
        {
            if (_busy) return;
            if (!RequireElevation()) return;
            ResidentState state = ResidentService.GetState();

            if (state == ResidentState.NotInstalled)
            {
                if (!EnsureEngine()) return;
                DialogResult answer = MessageBox.Show(this,
                    "Se instalará el servicio «" + AppPaths.ServiceDisplayName + "» y se iniciará automáticamente con Windows.\n\n" +
                    "La protección residente analiza los archivos nuevos o modificados de las carpetas configuradas.",
                    "Instalar protección residente", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;

                SetBusy(true, "Instalando el servicio residente…", false);
                _progress.IsIndeterminate = true;
                AppendLog(LogLevel.Info, "Instalando el servicio residente...");
                string error = string.Empty;
                bool ok = false;
                try
                {
                    ok = await Task.Run(delegate { return ResidentService.Install(out error); });
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                if (IsDisposed) return;
                _operation = null;
                SetBusy(false, "Listo.", false);
                _progress.IsIndeterminate = false;
                _progress.Value = ok ? 100 : 0;

                if (ok)
                {
                    AppendLog(LogLevel.Ok, "Protección residente activada.");
                    ShowTrayBalloon("ClamGuard",
                        _settings.PauseDefender
                            ? "Windows Defender se ha desactivado temporalmente. Ahora se encarga ClamGuard de la protección del equipo."
                            : "El sistema ahora está protegido por ClamGuard.");
                }
                else
                {
                    AppendLog(LogLevel.Error, "No se pudo activar el residente: " + error);
                    MessageBox.Show(this, error, "Protección residente", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                RefreshStatusAsync();
                return;
            }

            if (state == ResidentState.Running)
            {
                DialogResult answer = MessageBox.Show(this,
                    "Si detienes la protección residente, los archivos nuevos dejarán de analizarse en segundo plano.\n\n¿Detenerla?",
                    "Detener protección", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;

                SetBusy(true, "Deteniendo el servicio residente…", false);
                AppendLog(LogLevel.Warn, "Deteniendo el servicio residente...");
                string error = string.Empty;
                bool ok = false;
                try
                {
                    ok = await Task.Run(delegate { return ResidentService.Stop(out error); });
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                if (IsDisposed) return;
                _operation = null;
                SetBusy(false, "Listo.", false);
                if (ok) AppendLog(LogLevel.Ok, "Protección residente detenida.");
                else
                {
                    AppendLog(LogLevel.Error, error);
                    MessageBox.Show(this, error, "Protección residente", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                RefreshStatusAsync();
                return;
            }

            SetBusy(true, "Iniciando el servicio residente…", false);
            AppendLog(LogLevel.Info, "Iniciando el servicio residente...");
            string startError = string.Empty;
            bool started = false;
            try
            {
                started = await Task.Run(delegate { return ResidentService.Start(out startError); });
            }
            catch (Exception ex)
            {
                startError = ex.Message;
            }
            if (IsDisposed) return;
            _operation = null;
            SetBusy(false, "Listo.", false);
            if (started)
            {
                AppendLog(LogLevel.Ok, "Protección residente activa.");
                ShowTrayBalloon("ClamGuard",
                    _settings.PauseDefender
                        ? "El sistema ahora está protegido por ClamGuard. Windows Defender se ha pausado temporalmente."
                        : "El sistema ahora está protegido por ClamGuard.");
            }
            else
            {
                AppendLog(LogLevel.Error, startError);
                MessageBox.Show(this, startError, "Protección residente", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshStatusAsync();
        }

        private bool RequireElevation()
        {
            if (Elevation.IsElevated) return true;
            MessageBox.Show(this,
                "Esta acción necesita permisos de administrador para instalar ClamAV, " +
                "actualizar las firmas o controlar el servicio residente.\n\n" +
                "Cierra ClamGuard y vuelve a abrirlo aceptando el aviso de " +
                "Control de cuentas de usuario (UAC).",
                "Permisos de administrador", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private async void BannerAction()
        {
            if (_busy) return;
            string dir = AppPaths.FindClamAvDirectory(_settings.ClamAvDirectory);
            if (dir != null && !ClamAv.HasDatabase(AppPaths.DatabaseDir))
            {
                if (!RequireElevation()) return;
                await RunUpdate(true);
                return;
            }
            if (dir != null) return;

            if (!RequireElevation()) return;

            DialogResult answer = MessageBox.Show(this,
                "ClamGuard descargará el instalador oficial de ClamAV desde GitHub (unos 220 MB) y lo instalará en modo silencioso.\n\n" +
                "Después se descargarán las firmas de virus (unos 300 MB adicionales).\n\n¿Continuar?",
                "Instalar ClamAV", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            SetBusy(true, "Instalando ClamAV…", false);
            _progress.IsIndeterminate = true;
            InstallResult result = null;
            using (InstallDialog dialog = new InstallDialog())
            {
                Task<InstallResult> task = Task.Run(delegate
                {
                    return ClamInstaller.Install(_settings, dialog.Progress, dialog.Token,
                        new Action<LogLevel, string>(LogFromAnyThread));
                });
                Task closing = task.ContinueWith(delegate(Task finished)
                {
                    dialog.MarkCompleted();
                    try
                    {
                        if (!IsDisposed) BeginInvoke(new Action(dialog.Close));
                    }
                    catch
                    {
                    }
                });
                dialog.ShowDialog(this);
                try { await closing; }
                catch { }
                try
                {
                    result = await task;
                }
                catch (Exception ex)
                {
                    AppendLog(LogLevel.Error, "Instalación: " + ex.Message);
                }
            }

            if (IsDisposed) return;
            _operation = null;
            SetBusy(false, "Listo.", false);
            _progress.IsIndeterminate = false;
            _settings = AppSettings.Load();

            if (result == null)
            {
                _lblSummary.Text = L10n.T("La instalación no terminó correctamente.");
                return;
            }

            _lblSummary.Text = result.Message;
            if (result.Success)
            {
                AppendLog(LogLevel.Ok, result.Message);
                _progress.Value = 100;
                RefreshStatusAsync();
                if (result.SignaturesReady && ResidentService.GetState() == ResidentState.NotInstalled)
                {
                    DialogResult activate = MessageBox.Show(this,
                        "ClamAV está listo.\n\n¿Quieres activar ahora la protección residente?",
                        "Protección residente", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (activate == DialogResult.Yes) Resident_Click(null, EventArgs.Empty);
                }
            }
            else
            {
                AppendLog(LogLevel.Error, result.Message);
                MessageBox.Show(this, result.Message, "Instalar ClamAV", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowSettings(object sender, EventArgs e)
        {
            ShowSettings();
        }

        private void ShowSettings() { ShowSettings(0); }

        private void ShowSettings(int tabIndex)
        {
            if (_busy) return;
            bool requestUninstall = false;
            using (SettingsDialog dialog = new SettingsDialog(_settings, tabIndex))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _settings = AppSettings.Load();
                    L10n.Load(_settings.Language);
                    AppendLog(LogLevel.Ok, "Configuración guardada. El residente la aplicará automáticamente.");
                    RefreshStatusAsync();
                }
                requestUninstall = dialog.RequestUninstall;
            }
            if (requestUninstall) ShowUninstall();
        }

        /// <summary>
        /// Desinstalación completa: programa, servicio residente, motor ClamAV,
        /// firmas, registros, configuración y cuarentena. Si el usuario confirma
        /// y la operación termina, ClamGuard se cierra para permitir el borrado
        /// de sus propios archivos.
        /// </summary>
        private void ShowUninstall()
        {
            if (_busy) return;
            bool completed = false;
            using (UninstallDialog dialog = new UninstallDialog(_settings))
            {
                dialog.ShowDialog(this);
                completed = dialog.Completed;
            }
            if (!completed) return;

            _settings = AppSettings.Load();
            if (_trayTimer != null)
            {
                try { _trayTimer.Stop(); } catch { }
            }
            if (_tray != null)
            {
                try { _tray.Visible = false; } catch { }
            }
            _allowClose = true;
            Close();
        }

        private void ShowQuarantine()
        {
            using (QuarantineDialog dialog = new QuarantineDialog())
            {
                dialog.ShowDialog(this);
            }
            RefreshStatusAsync();
        }

        private void ShowIoc()
        {
            if (_busy) return;
            using (IocDialog dialog = new IocDialog())
            {
                dialog.ShowDialog(this);
            }
        }

        private void ShowFirewall()
        {
            if (_busy) return;
            using (FirewallDialog dialog = new FirewallDialog())
            {
                dialog.ShowDialog(this);
            }
        }

        private void ShowAudit()
        {
            if (_busy) return;
            using (AuditDialog dialog = new AuditDialog())
            {
                dialog.ShowDialog(this);
            }
        }

        private void CheckUpdates()
        {
            if (_busy) return;
            SetBusy(true, "Buscando actualizaciones...", false);
            AppendLog(LogLevel.Info, "Comprobando actualizaciones en GitHub...");
            UpdateInfo info = null;
            try
            {
                info = Task.Run(delegate { return UpdateChecker.Check(); }).Result;
            }
            catch { }
            SetBusy(false, "Listo.", false);
            if (info == null)
            {
                AppendLog(LogLevel.Info, "No hay actualizaciones disponibles.");
                MessageBox.Show(this, "No hay actualizaciones disponibles o no se pudo consultar GitHub.",
                    "Actualizaciones", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            AppendLog(LogLevel.Ok, "Nueva version disponible: " + info.Version);
            DialogResult answer = MessageBox.Show(this,
                "Hay una nueva version disponible: " + info.Version + "\n\n" +
                (string.IsNullOrEmpty(info.Notes) ? string.Empty : info.Notes + "\n\n") +
                "¿Deseas descargarla ahora?",
                "Actualizacion disponible", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer == DialogResult.Yes)
            {
                try
                {
                    string url = string.IsNullOrEmpty(info.DownloadUrl) ? info.ReleaseUrl : info.DownloadUrl;
                    if (!string.IsNullOrEmpty(url)) Process.Start(url);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Actualizaciones", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ShowSignatureDetails()
        {
            string dir = AppPaths.FindClamAvDirectory(_settings.ClamAvDirectory);
            if (dir == null)
            {
                MessageBox.Show(this, "ClamAV no está instalado.", "Firmas de virus",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            EngineInfo engine = ClamAv.QueryEngine(_settings);
            SignatureInfo sig = ClamAv.GetSignatureInfo(AppPaths.DatabaseDir);
            AppState state = StateStore.Load();
            DateTime? lastUpdate = AppState.ParseUtc(state.LastUpdateUtc);

            string text = "Motor ClamAV: " + (string.IsNullOrEmpty(engine.Version) ? "desconocido" : engine.Version) + Environment.NewLine +
                "Versión de firmas: " + (string.IsNullOrEmpty(engine.SignatureVersion) ? "sin datos" : engine.SignatureVersion) + Environment.NewLine +
                "Fecha de firmas: " + (string.IsNullOrEmpty(engine.SignatureDateText) ? sig.DateText : engine.SignatureDateText) + Environment.NewLine +
                "Tamaño de la base: " + ClamAv.FormatBytes(sig.TotalBytes) + Environment.NewLine +
                "Última comprobación: " + (lastUpdate.HasValue ? lastUpdate.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "nunca") + Environment.NewLine +
                "Carpeta de ClamAV: " + dir + Environment.NewLine +
                "Base de datos: " + AppPaths.DatabaseDir;

            MessageBox.Show(this, text, "Detalles de las firmas", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowAbout()
        {
            using (Form about = new Form())
            {
                about.Text = L10n.T("Acerca de ClamGuard");
                about.ClientSize = new Size(520, 360);
                about.FormBorderStyle = FormBorderStyle.FixedDialog;
                about.MaximizeBox = false;
                about.MinimizeBox = false;
                about.ShowInTaskbar = false;
                about.StartPosition = FormStartPosition.CenterParent;
                about.BackColor = Theme.Surface;
                about.Font = Theme.Ui(9f, FontStyle.Regular);

                Label title = new Label();
                title.Text = L10n.T("ClamGuard ") + AppPaths.ProductVersion;
                title.Font = Theme.Ui(14f, FontStyle.Bold);
                title.ForeColor = Theme.TextPrimary;
                title.BackColor = Theme.Surface;
                title.AutoSize = true;
                title.Location = new Point(24, 20);
                about.Controls.Add(title);

                Label body = new Label();
                body.Text =
                    "Interfaz y protección residente para el motor antivirus ClamAV.\n\n" +
                    "Creado por David Casas M\n" +
                    "Licencia: Creative Commons Compartir-Igual-Mención.\n" +
                    "Software GRATUITO, sin garantía ni soporte adicional.\n\n" +
                    "ClamAV es software libre de Cisco Talos. ClamGuard no incluye el motor ni " +
                    "las firmas: los descarga desde sus fuentes oficiales.\n\n" +
                    "Mientras el residente está activo, ClamGuard muestra un icono en el área de " +
                    "notificación, añade exclusiones para sus carpetas y pausa Windows Defender si " +
                    "es posible. Al detener el residente, Windows Defender se reactiva.";
                body.Font = Theme.Ui(9f, FontStyle.Regular);
                body.ForeColor = Theme.TextSecondary;
                body.BackColor = Theme.Surface;
                body.AutoSize = false;
                body.Location = new Point(24, 60);
                body.Size = new Size(472, 150);
                about.Controls.Add(body);

                int y = 218;
                AddAboutLink(about, "GitHub", "https://github.com/davidcasascompetenciadigital-prog", ref y);
                AddAboutLink(about, "YouTube", "https://www.youtube.com/@Competencia_Digital", ref y);
                AddAboutLink(about, "Twitch", "https://www.twitch.tv/davidcasasm", ref y);

                RoundedButton close = new RoundedButton();
                close.Text = L10n.T("Cerrar");
                close.Style = ButtonStyle.Secondary;
                close.BackColor = Theme.Surface;
                close.Size = new Size(104, 34);
                close.Location = new Point(400, 300);
                close.Click += delegate { about.Close(); };
                about.Controls.Add(close);
                about.AcceptButton = close;
                about.CancelButton = close;

                about.ShowDialog(this);
            }
        }

        private static void AddAboutLink(Form about, string label, string url, ref int y)
        {
            Label caption = new Label();
            caption.Text = label + ":";
            caption.Font = Theme.Ui(9f, FontStyle.Bold);
            caption.ForeColor = Theme.TextPrimary;
            caption.BackColor = Theme.Surface;
            caption.AutoSize = true;
            caption.Location = new Point(24, y);
            about.Controls.Add(caption);

            LinkLabel link = new LinkLabel();
            link.Text = url;
            link.AutoSize = true;
            link.Font = Theme.Ui(9f, FontStyle.Regular);
            link.LinkColor = Theme.Accent;
            link.ActiveLinkColor = Theme.AccentHover;
            link.VisitedLinkColor = Theme.Accent;
            link.BackColor = Theme.Surface;
            link.Location = new Point(110, y);
            link.LinkClicked += delegate
            {
                try { Process.Start(url); }
                catch { }
            };
            about.Controls.Add(link);
            y += 24;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_allowClose) return;

            if (_busy)
            {
                DialogResult answer = MessageBox.Show(this,
                    "Hay una tarea en curso. Si sales ahora, la tarea se cancelará.\n\n¿Salir de todos modos?",
                    "Salir de ClamGuard", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                StopOperation();
                if (ResidentService.IsRunning())
                {
                    e.Cancel = true;
                    HideToTray();
                }
                else
                {
                    _allowClose = true;
                }
                return;
            }

            // Con la protección residente activa, cerrar la ventana oculta
            // ClamGuard en la bandeja para que el icono siga visible.
            if (ResidentService.IsRunning())
            {
                e.Cancel = true;
                HideToTray();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_trayTimer != null)
                {
                    try { _trayTimer.Stop(); } catch { }
                    _trayTimer.Dispose();
                }
                if (_tray != null)
                {
                    try { _tray.Visible = false; } catch { }
                    _tray.Dispose();
                }
                if (_trayActiveIcon != null) _trayActiveIcon.Dispose();
                if (_trayIdleIcon != null) _trayIdleIcon.Dispose();

                if (_operation != null)
                {
                    try { _operation.Cancel(); } catch { }
                }
                if (_logFont != null) _logFont.Dispose();
                if (_logFontBold != null) _logFontBold.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
