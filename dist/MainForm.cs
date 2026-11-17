using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace ServerControlUniversal
{
    // ==================== ГЛАВНОЕ ОКНО ====================
    public class MainForm : Form
    {
        readonly string _pass;
        readonly AppConfig _cfg;

        readonly Panel _content = new Panel { Dock = DockStyle.Fill, BackColor = P.Bg, Padding = new Padding(18) };
        readonly Label _statusBar = new Label
        {
            Dock = DockStyle.Bottom, Height = 24, BackColor = P.Panel, ForeColor = P.Muted,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0), Text = "готов"
        };

        readonly Dictionary<string, Panel> _pages = new Dictionary<string, Panel>();
        readonly Dictionary<string, Button> _nav = new Dictionary<string, Button>();

        readonly Label _vTemp = Big("—"), _vMem = Big("—"), _vDisk = Big("—"), _vUp = Big("—"), _vVram = Big("—");
        readonly ListBox _svcList = new ListBox(), _unitList = new ListBox();
        readonly TextBox _unitSearch = new TextBox();
        readonly RichTextBox _svcDetail = Mono(1060, 100), _sysInfo = Mono(1060, 330);
        readonly RichTextBox _backupInfo = Mono(1060, 380), _agentLog = Mono(1060, 380);
        readonly Label _agentStatus = new Label
        {
            Text = "Статус: проверка…", ForeColor = P.Muted,
            Font = new Font("Segoe UI", 11), AutoSize = true, Location = new Point(0, 76)
        };
        Chart _chartTemp, _chartLoad;
        readonly FlowLayoutPanel _linksPanel = new FlowLayoutPanel
        {
            Location = new Point(0, 76), Size = new Size(1060, 420),
            AutoScroll = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };

        List<Tuple<string, string, string>> _units = new List<Tuple<string, string, string>>();
        string _unitFilter = "all";
        System.Windows.Forms.Timer _timer;
        NotifyIcon _tray;
        bool _exit, _attached;
        Process _tunnel;

        public MainForm(string password, AppConfig cfg)
        {
            _pass = password;
            _cfg = cfg;
            Ssh.Cfg = cfg;

            Text = "ServerControl — " + cfg.SshTarget;
            ClientSize = new Size(1120, 740);
            MinimumSize = new Size(900, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = P.Bg;
            ForeColor = P.Text;
            Font = new Font("Segoe UI", 9);
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                var st = asm.GetManifestResourceStream("app.ico");
                if (st != null) Icon = new Icon(st);
            }
            catch { }

            BuildSide();
            BuildOverview();
            BuildServices();
            BuildUnits();
            BuildGraphs();
            BuildSystem();
            BuildBackups();
            BuildAgent();
            BuildLinks();
            BuildSettings();

            Controls.Add(_content);
            Controls.Add(_statusBar);
            Controls.Add(_side);

            ShowPage("overview");

            _timer = new System.Windows.Forms.Timer { Interval = cfg.RefreshSec * 1000 };
            _timer.Tick += (s, e) => { if (Visible && WindowState != FormWindowState.Minimized) RefreshStatus(); };
            _timer.Start();

            Shown += (s, e) =>
            {
                InitTray();
                if (_cfg.StartMinimized) Hide();
                else RefreshStatus();
            };
            FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing && !_exit && _cfg.MinimizeToTray)
                {
                    e.Cancel = true;
                    Hide();
                    if (_tray != null)
                        _tray.ShowBalloonTip(1200, "ServerControl",
                            "Свёрнуто в трей. Двойной клик — открыть.", ToolTipIcon.Info);
                }
            };
            FormClosed += (s, e) =>
            {
                try { if (_tunnel != null && !_tunnel.HasExited) _tunnel.Kill(); } catch { }
                if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            };
        }

        Panel _side;

        // ---------- построение UI ----------
        void BuildSide()
        {
            _side = new Panel { Dock = DockStyle.Left, Width = 212, BackColor = P.Panel };
            _side.Controls.Add(new Label
            {
                Text = "SERVER", ForeColor = P.Accent,
                Font = new Font("Segoe UI", 15, FontStyle.Bold),
                Location = new Point(18, 16), AutoSize = true
            });
            _side.Controls.Add(new Label
            {
                Text = _cfg.SshTarget, ForeColor = P.Muted,
                Font = new Font("Segoe UI", 7.5f), Location = new Point(19, 44), AutoSize = true
            });

            var ssh = Btn(">_ Открыть SSH", P.Accent, Color.White);
            ssh.Location = new Point(12, 68);
            ssh.Width = 188; ssh.Height = 32;
            ssh.Click += (s, e) => OpenSsh();
            _side.Controls.Add(ssh);

            int y = 112;
            foreach (var it in new[]
            {
                T2("overview", "Обзор"), T2("services", "Службы"),
                T2("units", "Все юниты"), T2("graphs", "Графики"),
                T2("sys", "Система"), T2("backup", "Бэкапы"),
                T2("agent", "AI-агент"), T2("links", "Ссылки"),
                T2("cfg", "Настройки")
            })
            {
                var b = Btn(it.Item2, P.Panel, P.Muted);
                b.Location = new Point(8, y);
                b.Width = 196; b.Height = 34;
                b.TextAlign = ContentAlignment.MiddleLeft;
                b.Padding = new Padding(14, 0, 0, 0);
                var key = it.Item1;
                b.Click += (s, e) => ShowPage(key);
                _side.Controls.Add(b);
                _nav[key] = b;
                y += 36;
            }
        }

        void BuildOverview()
        {
            var pg = Page();
            var box = Panel(1060, 100);
            int x = 14;
            foreach (var pr in new[]
            {
                TL("ТЕМПЕРАТУРА", _vTemp), TL("ПАМЯТЬ", _vMem), TL("ДИСК", _vDisk),
                TL("АПТАЙМ", _vUp), TL("VRAM", _vVram)
            })
            {
                box.Controls.Add(new Label
                {
                    Text = pr.Item1, ForeColor = P.Muted,
                    Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                    Location = new Point(x, 32), AutoSize = true
                });
                pr.Item2.Location = new Point(x, 50);
                box.Controls.Add(pr.Item2);
                x += 205;
            }
            pg.Controls.Add(box);

            var svc = Panel(1060, 230);
            svc.Top = 112;
            svc.Controls.Add(new Label
            {
                Text = "СЛУЖБЫ", ForeColor = P.Muted,
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                Location = new Point(14, 10), AutoSize = true
            });
            StyleList(_svcList);
            _svcList.Location = new Point(14, 34);
            _svcList.Size = new Size(1020, 178);
            _svcList.MouseDoubleClick += (s, e) => SvcAction("restart");
            _svcList.ContextMenuStrip = BuildMenu(a => SvcAction(a));
            svc.Controls.Add(_svcList);
            pg.Controls.Add(svc);

            _svcDetail.Location = new Point(0, 354);
            _svcDetail.Size = new Size(1060, 290);
            pg.Controls.Add(_svcDetail);
            _pages["overview"] = pg;
        }

        void BuildServices()
        {
            var pg = Page();
            pg.Controls.Add(Title("СЛУЖБЫ"));
            pg.Controls.Add(Note("Службы из config.sh на сервере. Двойной клик — перезапуск."));

            var row = new Panel { Location = new Point(0, 72), Size = new Size(1060, 36) };
            int bx = 0;
            foreach (var a in new[]
            {
                T3("Запустить", P.Ok), T3("Остановить", P.Err), T3("Перезапустить", P.Warn)
            })
            {
                var b = Btn(a.Item1, a.Item2, Color.White);
                b.Location = new Point(bx, 0);
                b.Width = 148;
                var act = a.Item1.StartsWith("Запустить") ? "start"
                        : a.Item1.StartsWith("Остановить") ? "stop" : "restart";
                b.Click += (s, e) => SvcAction(act);
                row.Controls.Add(b);
                bx += 156;
            }
            var refBtn = Btn("Обновить", P.Card, P.Text);
            refBtn.Location = new Point(bx, 0);
            refBtn.Width = 120;
            refBtn.Click += (s, e) => LoadServices();
            row.Controls.Add(refBtn);
            pg.Controls.Add(row);

            var lb = new ListBox();
            StyleList(lb);
            lb.Location = new Point(0, 118);
            lb.Size = new Size(1060, 400);
            lb.MouseDoubleClick += (s, e) => { _svcList.SelectedIndex = lb.SelectedIndex; SvcAction("restart"); };
            lb.SelectedIndexChanged += (s, e) => { _svcList.SelectedIndex = lb.SelectedIndex; };
            pbServices = lb;
            pg.Controls.Add(lb);
            _pages["services"] = pg;
        }

        ListBox pbServices;

        void BuildUnits()
        {
            var pg = Page();
            pg.Controls.Add(Title("ВСЕ ЮНИТЫ SYSTEMD"));

            int fx = 0;
            foreach (var f in new[]
            {
                T2("all", "Все"), T2("active", "Работают"),
                T2("inactive", "Остановлены"), T2("failed", "Упали")
            })
            {
                var b = Btn(f.Item2, P.Card, P.Text);
                b.Location = new Point(fx, 42);
                b.Width = 120;
                var key = f.Item1;
                b.Click += (s, e) => { _unitFilter = key; FilterUnits(); };
                pg.Controls.Add(b);
                fx += 126;
            }

            _unitSearch.Location = new Point(0, 84);
            _unitSearch.Width = 380;
            _unitSearch.BackColor = P.Card;
            _unitSearch.ForeColor = P.Text;
            _unitSearch.BorderStyle = BorderStyle.FixedSingle;
            _unitSearch.TextChanged += (s, e) => FilterUnits();
            pg.Controls.Add(_unitSearch);

            StyleList(_unitList);
            _unitList.Location = new Point(0, 120);
            _unitList.Size = new Size(1060, 420);
            _unitList.MouseDoubleClick += (s, e) => UnitAction("restart");
            _unitList.ContextMenuStrip = BuildMenu(UnitAction);
            pg.Controls.Add(_unitList);
            _pages["units"] = pg;
        }

        void BuildGraphs()
        {
            var pg = Page();
            pg.Controls.Add(Title("ГРАФИКИ"));

            _chartTemp = ChartOf("Температура CPU", P.Err, 0, 100);
            _chartTemp.Location = new Point(0, 42);
            _chartTemp.Height = 200;
            pg.Controls.Add(_chartTemp);

            _chartLoad = ChartOf("Загрузка CPU", P.Accent, 0, 0);
            _chartLoad.Location = new Point(0, 254);
            _chartLoad.Height = 200;
            pg.Controls.Add(_chartLoad);

            var b = Btn("Обновить", P.Accent, Color.White);
            b.Location = new Point(0, 466);
            b.Width = 160;
            b.Click += (s, e) => LoadCharts();
            pg.Controls.Add(b);
            _pages["graphs"] = pg;
        }

        void BuildSystem()
        {
            var pg = Page();
            pg.Controls.Add(Title("СИСТЕМА"));
            _sysInfo.Location = new Point(0, 42);
            pg.Controls.Add(_sysInfo);

            int bx = 0;
            foreach (var a in new[]
            {
                T2("Обновить информацию", "load"),
                T2("Очистить журнал", "journalctl --vacuum-size=500M"),
                T2("Перезагрузить", "reboot")
            })
            {
                var b = Btn(a.Item1, a.Item2 == "reboot" ? P.Err : P.Card,
                            a.Item2 == "reboot" ? Color.White : P.Text);
                b.Location = new Point(bx, 384);
                b.Width = 190;
                var cmd = a.Item2;
                b.Click += (s, e) =>
                {
                    if (cmd == "load") { LoadSysInfo(); return; }
                    if (cmd == "reboot" &&
                        MessageBox.Show("Перезагрузить сервер?", "Подтверждение",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    RunSudo(cmd);
                };
                pg.Controls.Add(b);
                bx += 198;
            }
            _pages["sys"] = pg;
        }

        void BuildBackups()
        {
            var pg = Page();
            pg.Controls.Add(Title("БЭКАПЫ"));
            _backupInfo.Location = new Point(0, 42);
            pg.Controls.Add(_backupInfo);

            var refBtn = Btn("Обновить", P.Card, P.Text);
            refBtn.Location = new Point(0, 434);
            refBtn.Width = 150;
            refBtn.Click += (s, e) => LoadBackups();
            pg.Controls.Add(refBtn);

            var runBtn = Btn("Запустить бэкап", P.Accent, Color.White);
            runBtn.Location = new Point(158, 434);
            runBtn.Width = 190;
            runBtn.Click += (s, e) => RunBackupCommand();
            pg.Controls.Add(runBtn);
            _pages["backup"] = pg;
        }

        void BuildAgent()
        {
            var pg = Page();
            pg.Controls.Add(Title("AI-АГЕНТ"));
            pg.Controls.Add(Note("Запуск агента и SSH-туннель на локальный порт " + _cfg.AgentPort));
            pg.Controls.Add(_agentStatus);

            int bx = 0;
            foreach (var a in new[]
            {
                T3("Запустить", P.Ok), T3("Открыть", P.Accent), T3("Остановить", P.Err)
            })
            {
                var b = Btn(a.Item1, a.Item2, Color.White);
                b.Location = new Point(bx, 108);
                b.Width = 150;
                var act = a.Item1;
                b.Click += (s, e) =>
                {
                    if (act == "Запустить") LaunchAgent();
                    else if (act == "Открыть") OpenUrl("http://127.0.0.1:" + _cfg.AgentPort);
                    else StopAgent();
                };
                pg.Controls.Add(b);
                bx += 158;
            }

            _agentLog.Location = new Point(0, 152);
            pg.Controls.Add(_agentLog);
            _pages["agent"] = pg;
        }

        void BuildLinks()
        {
            var pg = Page();
            pg.Controls.Add(Title("ССЫЛКИ"));
            pg.Controls.Add(Note("Настраиваются в разделе «Настройки»."));
            pg.Controls.Add(_linksPanel);
            _pages["links"] = pg;
        }

        void BuildSettings()
        {
            var pg = Page();
            pg.Controls.Add(Title("НАСТРОЙКИ"));
            pg.Controls.Add(new Label
            {
                Text = "Сохраняется в " + AppConfig.ConfigPath, ForeColor = P.Muted,
                Font = new Font("Segoe UI", 8), Location = new Point(0, 42), AutoSize = true
            });

            var host = Input(_cfg.SshTarget, 0, 80, 340);
            var path = Input(_cfg.RemotePath, 0, 142, 340);
            var port = Input(_cfg.AgentPort, 0, 204, 120);
            var interval = Input(_cfg.RefreshSec.ToString(), 170, 204, 100);
            pg.Controls.AddRange(new Control[] {
                Lbl("Сервер (user@host)", 0, 62), host,
                Lbl("Путь к server_api.sh на сервере", 0, 124), path,
                Lbl("Порт агента", 0, 186), port,
                Lbl("Обновление, сек", 170, 186), interval
            });

            var tray = Chk("Сворачивать в трей при закрытии", 0, 242, _cfg.MinimizeToTray);
            var start = Chk("Запускать свёрнутым", 0, 266, _cfg.StartMinimized);
            pg.Controls.AddRange(new Control[] { tray, start });

            pg.Controls.Add(Lbl("Ссылки (по одной в строке: Название|http://адрес)", 0, 298));
            var links = new TextBox
            {
                Multiline = true, Location = new Point(0, 316), Size = new Size(600, 90),
                ScrollBars = ScrollBars.Vertical,
                Text = string.Join("\r\n", _cfg.Links.Select(l => l.Item1 + "|" + l.Item2)),
                BackColor = P.Card, ForeColor = P.Text,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9)
            };
            pg.Controls.Add(links);

            var save = Btn("Сохранить", P.Accent, Color.White);
            save.Location = new Point(0, 420);
            save.Width = 170;
            save.Click += (s, e) =>
            {
                _cfg.SshTarget = host.Text.Trim();
                _cfg.RemotePath = path.Text.Trim();
                _cfg.AgentPort = port.Text.Trim();
                int r; _cfg.RefreshSec = int.TryParse(interval.Text, out r) && r >= 5 ? r : 15;
                _cfg.MinimizeToTray = tray.Checked;
                _cfg.StartMinimized = start.Checked;
                _cfg.Links.Clear();
                foreach (var ln in links.Text.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var bits = ln.Trim().Split('|');
                    if (bits.Length == 2 && bits[0].Trim().Length > 0)
                        _cfg.Links.Add(Tuple.Create(bits[0].Trim(), bits[1].Trim()));
                }
                _cfg.Save();
                Ssh.Cfg = _cfg;
                _timer.Interval = _cfg.RefreshSec * 1000;
                BuildLinkButtons();
                Status("настройки сохранены");
            };
            pg.Controls.Add(save);

            var test = Btn("Проверить связь", P.Card, P.Text);
            test.Location = new Point(178, 420);
            test.Width = 170;
            test.Click += (s, e) => TestConnection();
            pg.Controls.Add(test);

            var about = new Label
            {
                Text = "ServerControl Universal v1.0 · .NET 4.5+ · SSH",
                ForeColor = P.Muted, Font = new Font("Segoe UI", 8),
                Location = new Point(0, 466), AutoSize = true
            };
            pg.Controls.Add(about);
            _pages["cfg"] = pg;

            BuildLinkButtons();
        }

        void BuildLinkButtons()
        {
            _linksPanel.Controls.Clear();
            if (_cfg.Links.Count == 0)
            {
                _linksPanel.Controls.Add(new Label
                {
                    Text = "Ссылки не настроены — добавьте их в «Настройках».",
                    ForeColor = P.Muted, AutoSize = true
                });
                return;
            }
            foreach (var l in _cfg.Links)
            {
                var b = Btn(l.Item1, P.Card, P.Text);
                b.Width = 220; b.Height = 42;
                b.Margin = new Padding(0, 0, 10, 10);
                var url = l.Item2;
                b.Click += (s, e) => OpenUrl(url);
                _linksPanel.Controls.Add(b);
            }
        }

        // ---------- хелперы ----------
        static Tuple<string, string> T2(string a, string b) { return Tuple.Create(a, b); }
        static Tuple<string, Label> TL(string a, Label b) { return Tuple.Create(a, b); }
        static Tuple<string, Color> T3(string a, Color c) { return Tuple.Create(a, c); }

        Panel Page() => new Panel { Dock = DockStyle.Fill, BackColor = P.Bg, Visible = false, AutoScroll = true };
        Panel Panel(int w, int h) => new Panel
        {
            Width = w, Height = h, BackColor = P.Panel,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        Label Title(string t) => new Label
        {
            Text = t, ForeColor = P.Text, Font = new Font("Segoe UI", 16, FontStyle.Bold),
            AutoSize = true, Location = new Point(0, 0)
        };
        Label Note(string t) => new Label
        {
            Text = t, ForeColor = P.Muted, Font = new Font("Segoe UI", 9),
            AutoSize = true, Location = new Point(0, 42)
        };
        Label Lbl(string t, int x, int y) => new Label
        {
            Text = t, ForeColor = P.Muted, Font = new Font("Segoe UI", 8),
            Location = new Point(x, y), AutoSize = true
        };
        static Label Big(string t) => new Label
        {
            Text = t, ForeColor = P.Text, Font = new Font("Segoe UI", 17, FontStyle.Bold), AutoSize = true
        };
        static RichTextBox Mono(int w, int h) => new RichTextBox
        {
            Size = new Size(w, h), ReadOnly = true, BorderStyle = BorderStyle.None,
            BackColor = P.Panel, ForeColor = P.Muted, Font = new Font("Consolas", 9),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        TextBox Input(string v, int x, int y, int w) => new TextBox
        {
            Text = v, Location = new Point(x, y), Width = w,
            BackColor = P.Card, ForeColor = P.Text, BorderStyle = BorderStyle.FixedSingle
        };
        CheckBox Chk(string t, int x, int y, bool v) => new CheckBox
        {
            Text = t, ForeColor = P.Text, Location = new Point(x, y), AutoSize = true, Checked = v
        };
        Button Btn(string t, Color bg, Color fg)
        {
            var b = new Button
            {
                Text = t, BackColor = bg, ForeColor = fg, FlatStyle = FlatStyle.Flat,
                Height = 32, Font = new Font("Segoe UI", 9), Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }
        static void StyleList(ListBox lb)
        {
            lb.BackColor = P.Card; lb.ForeColor = P.Text;
            lb.Font = new Font("Consolas", 9); lb.BorderStyle = BorderStyle.None;
            lb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        }
        ContextMenuStrip BuildMenu(Action<string> onAction)
        {
            var m = new ContextMenuStrip();
            foreach (var a in new[]
            {
                T2("start", "Запустить"), T2("stop", "Остановить"),
                T2("restart", "Перезапустить"), T2("", ""),
                T2("status", "Статус"), T2("log", "Лог")
            })
            {
                if (a.Item1 == "") { m.Items.Add(new ToolStripSeparator()); continue; }
                var mi = new ToolStripMenuItem(a.Item2);
                var act = a.Item1;
                mi.Click += (s, e) => onAction(act);
                m.Items.Add(mi);
            }
            return m;
        }
        Chart ChartOf(string title, Color color, double min, double max)
        {
            var ch = new Chart
            {
                Width = 1060, BackColor = P.Panel,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            var area = new ChartArea("main") { BackColor = P.Panel };
            area.AxisX.MajorGrid.LineColor = P.Line;
            area.AxisY.MajorGrid.LineColor = P.Line;
            area.AxisX.LineColor = P.Muted;
            area.AxisY.LineColor = P.Muted;
            area.AxisX.LabelStyle.ForeColor = P.Muted;
            area.AxisY.LabelStyle.ForeColor = P.Muted;
            if (max > 0) { area.AxisY.Minimum = min; area.AxisY.Maximum = max; }
            ch.ChartAreas.Add(area);
            ch.Titles.Add(new Title(title)
            {
                ForeColor = P.Text, Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Alignment = ContentAlignment.TopLeft, Docking = Docking.Top
            });
            ch.Series.Add(new Series("data")
            {
                ChartType = SeriesChartType.FastLine, Color = color, BorderWidth = 2
            });
            return ch;
        }

        void ShowPage(string key)
        {
            if (!_attached)
            {
                _content.SuspendLayout();
                foreach (var kv in _pages) _content.Controls.Add(kv.Value);
                _content.ResumeLayout();
                _attached = true;
            }
            _content.SuspendLayout();
            foreach (var kv in _pages) kv.Value.Visible = kv.Key == key;
            _content.ResumeLayout();
            foreach (var kv in _nav)
            {
                kv.Value.BackColor = kv.Key == key ? P.Card : P.Panel;
                kv.Value.ForeColor = kv.Key == key ? P.Text : P.Muted;
            }
            switch (key)
            {
                case "services": LoadServices(); break;
                case "units": LoadUnits(); break;
                case "graphs": LoadCharts(); break;
                case "sys": LoadSysInfo(); break;
                case "backup": LoadBackups(); break;
                case "agent": RefreshAgentStatus(); break;
            }
        }

        void Status(string t) { _statusBar.Text = t; }

        // ---------- данные ----------
        Status ParseStatus(string raw)
        {
            var st = new Status();
            if (string.IsNullOrWhiteSpace(raw)) return st;
            st.Host = Grab(raw, "\"host\"\\s*:\\s*\"([^\"]*)\"");
            st.TempC = (int)Num(raw, "\"temp_c\"\\s*:\\s*(-?\\d+)", -1);
            st.Uptime = (long)Num(raw, "\"uptime_s\"\\s*:\\s*(\\d+)");
            st.MemTotal = (long)Num(raw, "\"mem\"\\s*:\\s*\\{[^}]*\"total_mb\"\\s*:\\s*(\\d+)");
            st.MemUsed = (long)Num(raw, "\"mem\"\\s*:\\s*\\{[^}]*\"used_mb\"\\s*:\\s*(\\d+)");
            st.SwapTotal = (long)Num(raw, "\"swap\"\\s*:\\s*\\{[^}]*\"total_mb\"\\s*:\\s*(\\d+)");
            st.SwapUsed = (long)Num(raw, "\"swap\"\\s*:\\s*\\{[^}]*\"used_mb\"\\s*:\\s*(\\d+)");
            st.DiskPct = (int)Num(raw, "\"disk\"\\s*:\\s*\\{[^}]*\"pct\"\\s*:\\s*(\\d+)", -1);
            st.HddPct = (int)Num(raw, "\"hdd\"\\s*:\\s*\\{[^}]*\"pct\"\\s*:\\s*(\\d+)", -1);
            st.VramUsed = (long)Num(raw, "\"vram_used_mb\"\\s*:\\s*(\\d+)");
            st.VramTotal = (long)Num(raw, "\"vram_total_mb\"\\s*:\\s*(\\d+)");
            st.LanIp = Grab(raw, "\"lan_ip\"\\s*:\\s*\"([^\"]*)\"");
            st.TailIp = Grab(raw, "\"tailscale_ip\"\\s*:\\s*\"([^\"]*)\"");
            var sv = Regex.Match(raw, "\"services\"\\s*:\\s*\\{(.*?)\\}", RegexOptions.Singleline);
            if (sv.Success)
                foreach (Match m in Regex.Matches(sv.Groups[1].Value, "\"([\\w-]+)\"\\s*:\\s*\"([^\"]*)\""))
                    st.Services[m.Groups[1].Value] = m.Groups[2].Value;
            return st;
        }

        static string Grab(string s, string p) { var m = Regex.Match(s, p); return m.Success ? m.Groups[1].Value : ""; }
        static double Num(string s, string p, double def = 0)
        {
            var m = Regex.Match(s, p);
            double v;
            return m.Success && double.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out v) ? v : def;
        }

        async void RefreshStatus()
        {
            var raw = await Task.Run(() => Ssh.Run("bash " + _cfg.RemotePath + "/server_api.sh status"));
            var st = ParseStatus(raw);
            if (string.IsNullOrEmpty(st.Host)) { Status("нет связи с сервером"); return; }

            _vTemp.Text = st.HasTemp ? st.TempC + "°" : "n/a";
            _vMem.Text = string.Format("{0:N1}/{1:N1} ГБ", st.MemUsed / 1024.0, st.MemTotal / 1024.0);
            _vDisk.Text = st.DiskPct >= 0 ? st.DiskPct + "%" : "n/a";
            _vUp.Text = FmtUp(st.Uptime);
            _vVram.Text = st.VramTotal > 0
                ? string.Format("{0:N0}/{1:N1} ГБ", st.VramUsed, st.VramTotal / 1024.0) : "n/a";

            _svcList.BeginUpdate();
            _svcList.Items.Clear();
            foreach (var kv in st.Services)
                _svcList.Items.Add(string.Format("{0,-28} {1}", kv.Key, Ru(kv.Value)));
            _svcList.EndUpdate();
            if (pbServices != null) SyncServiceLists(st);

            _svcDetail.Text =
                "Хост:        " + st.Host + "\n" +
                "IP (LAN):    " + st.LanIp + "\n" +
                (string.IsNullOrEmpty(st.TailIp) ? "" : "IP (Tailscale): " + st.TailIp + "\n") +
                string.Format("Swap:        {0} / {1} МБ\n", st.SwapUsed, st.SwapTotal) +
                string.Format("Диск /:      {0}%\n", st.DiskPct) +
                (st.HasHdd ? string.Format("Диск данных: {0}%\n", st.HddPct) : "");

            Status("обновлено " + DateTime.Now.ToString("HH:mm:ss"));
        }

        void SyncServiceLists(Status st)
        {
            int sel = pbServices.SelectedIndex;
            pbServices.BeginUpdate();
            pbServices.Items.Clear();
            foreach (var kv in st.Services)
                pbServices.Items.Add(string.Format("{0,-30} {1}", kv.Key, Ru(kv.Value)));
            pbServices.EndUpdate();
            if (sel >= 0 && sel < pbServices.Items.Count) pbServices.SelectedIndex = sel;
        }

        static string Ru(string s)
        {
            switch (s)
            {
                case "active": return "работает";
                case "inactive": return "остановлен";
                case "failed": return "ошибка";
                case "activating": return "запуск…";
                case "on": return "работает";
                case "off": return "выключен";
                default: return s;
            }
        }

        static string FmtUp(long s)
        {
            var d = s / 86400; var h = (s % 86400) / 3600; var m = (s % 3600) / 60;
            if (d > 0) return d + " д " + h + " ч";
            if (h > 0) return h + " ч " + m + " м";
            return m + " м";
        }

        async void LoadServices()
        {
            Status("загрузка служб…");
            var raw = await Task.Run(() => Ssh.Run("bash " + _cfg.RemotePath + "/server_api.sh status", 20));
            var st = ParseStatus(raw);
            if (pbServices != null)
            {
                pbServices.BeginUpdate();
                pbServices.Items.Clear();
                foreach (var kv in st.Services)
                    pbServices.Items.Add(string.Format("{0,-30} {1}", kv.Key, Ru(kv.Value)));
                pbServices.EndUpdate();
            }
            Status("служб: " + st.Services.Count);
        }

        async void LoadUnits()
        {
            Status("загрузка юнитов…");
            var raw = await Task.Run(() => Ssh.Run("bash " + _cfg.RemotePath + "/server_api.sh units", 25));
            _units.Clear();
            foreach (Match m in Regex.Matches(raw,
                "\"name\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"state\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"sub\"\\s*:\\s*\"([^\"]*)\""))
                _units.Add(Tuple.Create(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value));
            FilterUnits();
            Status("юнитов: " + _units.Count);
        }

        void FilterUnits()
        {
            var q = _unitSearch.Text.Trim().ToLower();
            _unitList.BeginUpdate();
            _unitList.Items.Clear();
            foreach (var u in _units)
            {
                if (_unitFilter != "all" && u.Item2 != _unitFilter) continue;
                if (q.Length > 0 && !u.Item1.ToLower().Contains(q)) continue;
                _unitList.Items.Add(string.Format("{0,-46} {1}", u.Item1, u.Item2));
            }
            _unitList.EndUpdate();
        }

        async void LoadSysInfo()
        {
            Status("загрузка…");
            var cmd = "echo '=== СИСТЕМА ==='; hostnamectl 2>/dev/null | head -6; "
                    + "echo; echo '=== CPU ==='; lscpu 2>/dev/null | grep -E 'Model name|^CPU\\\\(s\\\\)' | head -2; "
                    + "echo; echo '=== ПАМЯТЬ ==='; free -h; "
                    + "echo; echo '=== ДИСКИ ==='; df -h | grep -E 'Filesystem|/dev/'; "
                    + "echo; echo '=== ТЕМПЕРАТУРЫ ==='; for z in /sys/class/thermal/thermal_zone*/temp; do "
                    + "[ -f \\\"$z\\\" ] && echo \\\"$(cat $(dirname $z)/type): $(( $(cat $z)/1000 ))°C\\\"; done; "
                    + "echo; echo '=== СЛУЖБЫ В ОШИБКЕ ==='; systemctl --failed --no-legend | head -10";
            var raw = await Task.Run(() => Ssh.Run("bash " + _cfg.RemotePath + "/server_api.sh term " + Wrap(cmd), 30));
            _sysInfo.Text = Unwrap(raw);
            Status("готово");
        }

        async void LoadBackups()
        {
            Status("загрузка…");
            var cmd = "echo '=== АРХИВЫ В РАСПРОСТРАНЁННЫХ МЕСТАХ ==='; "
                    + "for d in /var/backups /home /mnt /backup /srv; do [ -d $d ] && find $d -maxdepth 3 "
                    + "\\\\( -name '*.tar.gz' -o -name '*.zip' -o -name '*.tar.zst' \\\\) -printf '%TY-%Tm-%Td %10s %p\\\\n' 2>/dev/null | head -10; done; "
                    + "echo; echo '=== ДИСКИ ==='; df -h | grep -E 'Filesystem|/dev/'; "
                    + "echo; echo '=== CRON ==='; crontab -l 2>/dev/null | grep -vE '^#|^$' | head -10";
            var raw = await Task.Run(() => Ssh.Run("bash " + _cfg.RemotePath + "/server_api.sh term " + Wrap(cmd), 25));
            _backupInfo.Text = Unwrap(raw);
            Status("готово");
        }

        async void LoadCharts()
        {
            Status("загрузка графиков…");
            var raw = await Task.Run(() => Ssh.Run("bash " + _cfg.RemotePath + "/server_api.sh stats", 25));
            var temps = new List<double>();
            var loads = new List<double>();
            foreach (Match m in Regex.Matches(raw,
                "\"temp\"\\s*:\\s*(\\d+)\\s*,\\s*\"load\"\\s*:\\s*\\[([0-9.]+)"))
            {
                temps.Add(double.Parse(m.Groups[1].Value));
                loads.Add(double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
            if (temps.Count < 2) { Status("мало данных — нужно минимум 2 замера"); return; }
            _chartTemp.Series[0].Points.Clear();
            _chartLoad.Series[0].Points.Clear();
            for (int i = 0; i < temps.Count; i++)
            {
                _chartTemp.Series[0].Points.AddY(temps[i]);
                _chartLoad.Series[0].Points.AddY(loads[i]);
            }
            Status("замеров: " + temps.Count);
        }

        async void RefreshAgentStatus()
        {
            var raw = await Task.Run(() => Ssh.Run("bash " + _cfg.RemotePath + "/server_api.sh status", 15));
            var on = raw.Contains("\"agent\": \"on\"");
            _agentStatus.Text = on ? "Статус: запущен на сервере" : "Статус: не запущен";
            _agentStatus.ForeColor = on ? P.Ok : P.Muted;
        }

        // ---------- действия ----------
        async void RunSudo(string command)
        {
            Status("выполнение…");
            var r = await Task.Run(() => Ssh.RunSudo(command, _pass, 90));
            Status(string.IsNullOrWhiteSpace(r) ? "готово" : Trim(r));
        }

        async void RunBackupCommand()
        {
            var cmd = Prompt.Show("Команда бэкапа", "Например: /usr/local/bin/backup.sh");
            if (string.IsNullOrWhiteSpace(cmd)) return;
            Status("бэкап запущен…");
            var r = await Task.Run(() => Ssh.RunSudo(cmd.Trim(), _pass, 600));
            Status(string.IsNullOrWhiteSpace(r) ? "выполнено" : Trim(r));
            LoadBackups();
        }

        static string Trim(string s)
        {
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 90 ? s.Substring(0, 90) : s;
        }

        async void UnitAction(string action)
        {
            if (_unitList.SelectedIndex < 0) { Status("выберите юнит"); return; }
            var name = FirstWord(_unitList.SelectedItem.ToString());
            if (name.Length == 0) return;

            if (action == "status" || action == "log")
            {
                var cmd = action == "status"
                    ? "systemctl status " + name + " --no-pager -l"
                    : "journalctl -u " + name + " -n 80 --no-pager";
                var raw = await Task.Run(() => Ssh.Run("bash " + _cfg.RemotePath + "/server_api.sh term " + Wrap(cmd), 20));
                OutputWindow(name + " — " + action, Unwrap(raw));
                return;
            }
            Status(action + " " + name + "…");
            await Task.Run(() => Ssh.RunSudo("systemctl " + action + " " + name, _pass, 60));
            Status("готово");
            LoadUnits();
        }

        async void SvcAction(string action)
        {
            if (_svcList.SelectedIndex < 0) { Status("выберите службу"); return; }
            var name = FirstWord(_svcList.SelectedItem.ToString());
            if (name.Length == 0) return;

            if (action == "status" || action == "log")
            {
                var cmd = action == "status"
                    ? "systemctl status " + name + " --no-pager -l"
                    : "journalctl -u " + name + " -n 80 --no-pager";
                var raw = await Task.Run(() => Ssh.Run("bash " + _cfg.RemotePath + "/server_api.sh term " + Wrap(cmd), 20));
                OutputWindow(name + " — " + action, Unwrap(raw));
                return;
            }
            Status(action + " " + name + "…");
            await Task.Run(() => Ssh.RunSudo("systemctl " + action + " " + name, _pass, 60));
            Status("готово");
            RefreshStatus();
        }

        static string FirstWord(string s)
        {
            var parts = s.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : "";
        }

        void OutputWindow(string title, string text)
        {
            var w = new Form
            {
                Text = title, Size = new Size(900, 560),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = P.Bg, ForeColor = P.Text
            };
            var box = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
                BackColor = P.Mono, ForeColor = P.Text,
                Font = new Font("Consolas", 9), Text = text
            };
            var btn = new Button
            {
                Text = "Закрыть", Dock = DockStyle.Bottom, Height = 34,
                FlatStyle = FlatStyle.Flat, BackColor = P.Card, ForeColor = P.Text
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (s, e) => w.Close();
            w.Controls.Add(box);
            w.Controls.Add(btn);
            w.Show(this);
        }

        void OpenSsh()
        {
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe",
                    "/c start \"SSH — " + _cfg.SshTarget + "\" ssh " + _cfg.SshTarget)
                { UseShellExecute = false });
            }
            catch { Status("не удалось открыть терминал"); }
        }

        void OpenUrl(string url)
        {
            try { Process.Start(url); } catch { Status("не удалось открыть браузер"); }
        }

        // ---------- агент ----------
        void LaunchAgent()
        {
            _agentLog.Clear();
            Status("запуск агента…");
            Task.Run(() =>
            {
                Append("Поднимаю SSH-туннель 127.0.0.1:" + _cfg.AgentPort + " → сервер:" + _cfg.AgentPort + "\n");
                try { if (_tunnel != null && !_tunnel.HasExited) _tunnel.Kill(); } catch { }
                try
                {
                    var psi = new ProcessStartInfo("ssh")
                    {
                        Arguments = "-N -o BatchMode=yes -o ConnectTimeout=6 "
                                  + "-o StrictHostKeyChecking=accept-new -o ExitOnForwardFailure=yes "
                                  + "-L " + _cfg.AgentPort + ":127.0.0.1:" + _cfg.AgentPort + " "
                                  + _cfg.SshTarget,
                        UseShellExecute = false, CreateNoWindow = true
                    };
                    _tunnel = Process.Start(psi);
                    Append("туннель запущен, PID " + _tunnel.Id + "\n");
                }
                catch (Exception ex) { Append("ошибка туннеля: " + ex.Message + "\n"); }

                Thread.Sleep(2000);
                Append("Открываю браузер…\n");
                OpenUrl("http://127.0.0.1:" + _cfg.AgentPort);
                Append("Готово. Не закрывайте приложение — туннель работает через него.\n");
                try
                {
                    Invoke((Action)(() =>
                    {
                        _agentStatus.Text = "Статус: туннель активен";
                        _agentStatus.ForeColor = P.Ok;
                        Status("агент запущен");
                    }));
                }
                catch { }
            });
        }

        void StopAgent()
        {
            try
            {
                if (_tunnel != null && !_tunnel.HasExited)
                {
                    _tunnel.Kill();
                    Append("туннель закрыт\n");
                }
            }
            catch { }
            _agentStatus.Text = "Статус: остановлен";
            _agentStatus.ForeColor = P.Muted;
            Status("остановлено");
        }

        void Append(string t)
        {
            if (string.IsNullOrEmpty(t)) return;
            try
            {
                Invoke((Action)(() =>
                {
                    _agentLog.AppendText(t);
                    _agentLog.ScrollToCaret();
                }));
            }
            catch { }
        }

        // ---------- утилиты ----------
        static string Wrap(string s)
        {
            return "\"" + s.Replace("\"", "'") + "\"";
        }

        static string Unwrap(string json)
        {
            var m = Regex.Match(json, "\"output\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : json;
        }

        async void TestConnection()
        {
            Status("проверка связи…");
            var ok = await Task.Run(() => Ssh.Test());
            Status(ok ? "связь есть" : "нет связи");
            MessageBox.Show(
                ok ? "Связь с сервером установлена."
                   : "Не удалось подключиться.\nПроверьте адрес, SSH-ключи и доступность сервера.",
                "Проверка связи", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        // ---------- трей ----------
        void InitTray()
        {
            _tray = new NotifyIcon
            {
                Text = "ServerControl",
                Visible = true,
                Icon = Icon ?? SystemIcons.Application
            };
            var m = new ContextMenuStrip();
            m.Items.Add("Открыть панель", null, (s, e) =>
            {
                Show(); WindowState = FormWindowState.Normal; Activate();
            });
            m.Items.Add("Открыть SSH", null, (s, e) => OpenSsh());
            m.Items.Add("Открыть агента", null, (s, e) => OpenUrl("http://127.0.0.1:" + _cfg.AgentPort));
            m.Items.Add(new ToolStripSeparator());
            foreach (var l in _cfg.Links)
            {
                var url = l.Item2;
                m.Items.Add(l.Item1, null, (s, e) => OpenUrl(url));
            }
            if (_cfg.Links.Count > 0) m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Выход", null, (s, e) => { _exit = true; _tray.Visible = false; Close(); });
            _tray.ContextMenuStrip = m;
            _tray.DoubleClick += (s, e) =>
            {
                Show(); WindowState = FormWindowState.Normal; Activate();
            };
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5) RefreshStatus();
            if (e.Control && e.KeyCode == Keys.T) OpenSsh();
            if (e.Control && e.KeyCode == Keys.O) ShowPage("overview");
            if (e.Control && e.KeyCode == Keys.S) ShowPage("services");
            if (e.Control && e.KeyCode == Keys.U) ShowPage("units");
            if (e.Control && e.KeyCode == Keys.D) ShowPage("graphs");
            if (e.Control && e.KeyCode == Keys.F) { ShowPage("units"); _unitSearch.Focus(); }
            if (e.KeyCode == Keys.Escape) ShowPage("overview");
            base.OnKeyDown(e);
        }
    }

    // ==================== ДИАЛОГ ВВОДА ====================
    public static class Prompt
    {
        public static string Show(string title, string hint)
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.ClientSize = new Size(540, 150);
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = P.Bg; f.ForeColor = P.Text;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false;

                f.Controls.Add(new Label
                {
                    Text = hint, ForeColor = P.Muted,
                    Location = new Point(16, 14), AutoSize = true
                });
                var box = new TextBox
                {
                    Location = new Point(16, 42), Width = 506,
                    BackColor = P.Card, ForeColor = P.Text, BorderStyle = BorderStyle.FixedSingle
                };
                f.Controls.Add(box);

                var ok = new Button
                {
                    Text = "Выполнить", DialogResult = DialogResult.OK,
                    Location = new Point(16, 84), Width = 180, Height = 34,
                    FlatStyle = FlatStyle.Flat, BackColor = P.Accent, ForeColor = Color.White
                };
                ok.FlatAppearance.BorderSize = 0;
                var cancel = new Button
                {
                    Text = "Отмена", DialogResult = DialogResult.Cancel,
                    Location = new Point(206, 84), Width = 120, Height = 34,
                    FlatStyle = FlatStyle.Flat, BackColor = P.Card, ForeColor = P.Text
                };
                cancel.FlatAppearance.BorderSize = 0;
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                box.Focus();
                return f.ShowDialog() == DialogResult.OK ? box.Text : null;
            }
        }
    }

    // ==================== ТОЧКА ВХОДА ====================
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var cfg = AppConfig.Load();
            Ssh.Cfg = cfg;

            using (var login = new LoginForm(cfg))
            {
                if (login.ShowDialog() != DialogResult.OK) return;
                cfg.SshTarget = Ssh.Cfg.SshTarget;
                cfg.Save();
                Application.Run(new MainForm(login.Password, cfg));
            }
        }
    }
}
