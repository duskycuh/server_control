// ServerControl Universal — панель управления Linux-сервером по SSH.
// Не привязана к конкретному серверу: всё настраивается в разделе «Настройки».
//
// Компиляция (Windows или Mono):
//   mcs -target:winexe -out:ServerControl.exe ServerControl.cs \
//       -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Core.dll \
//       -r:System.Windows.Forms.DataVisualization.dll \
//       -resource:app.ico,app.ico -win32icon:app.ico
//
// Требует: .NET Framework 4.5+ / Mono 6+, SSH-клиент в PATH, Linux с systemd.

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
    // ==================== КОНФИГ ====================
    public class AppConfig
    {
        public string SshTarget = "";
        public string RemotePath = "/usr/local/bin/servercontrol";
        public int RefreshSec = 15;
        public bool MinimizeToTray = true;
        public bool StartMinimized = false;
        public string AgentPort = "3080";
        public List<Tuple<string, string>> Links = new List<Tuple<string, string>>();

        public static string ConfigPath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ServerControlUniversal");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return Path.Combine(dir, "config.ini");
            }
        }

        public static AppConfig Load()
        {
            var c = new AppConfig();
            try
            {
                if (!File.Exists(ConfigPath)) return c;
                foreach (var line in File.ReadAllLines(ConfigPath))
                {
                    var i = line.IndexOf('=');
                    if (i <= 0) continue;
                    var k = line.Substring(0, i).Trim();
                    var v = line.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "ssh": c.SshTarget = v; break;
                        case "path": c.RemotePath = v; break;
                        case "refresh": int r; if (int.TryParse(v, out r) && r >= 5) c.RefreshSec = r; break;
                        case "tray": c.MinimizeToTray = v == "1"; break;
                        case "startmin": c.StartMinimized = v == "1"; break;
                        case "agentport": c.AgentPort = v; break;
                        case "links":
                            c.Links.Clear();
                            foreach (var part in v.Split(new char[] { ';' }))
                            {
                                var bits = part.Split(new char[] { '|' });
                                if (bits.Length == 2 && bits[0].Trim().Length > 0)
                                    c.Links.Add(Tuple.Create(bits[0].Trim(), bits[1].Trim()));
                            }
                            break;
                    }
                }
            }
            catch { }
            return c;
        }

        public void Save()
        {
            try
            {
                var links = string.Join(";", Links.Select(l => l.Item1 + "|" + l.Item2));
                File.WriteAllLines(ConfigPath, new[] {
                    "ssh=" + SshTarget, "path=" + RemotePath, "refresh=" + RefreshSec,
                    "tray=" + (MinimizeToTray ? "1" : "0"),
                    "startmin=" + (StartMinimized ? "1" : "0"),
                    "agentport=" + AgentPort, "links=" + links
                });
            }
            catch { }
        }
    }

    // ==================== ПАЛИТРА ====================
    static class P
    {
        public static Color Bg = Color.FromArgb(20, 22, 28);
        public static Color Panel = Color.FromArgb(28, 31, 39);
        public static Color Card = Color.FromArgb(35, 39, 47);
        public static Color Line = Color.FromArgb(46, 52, 64);
        public static Color Text = Color.FromArgb(230, 233, 240);
        public static Color Muted = Color.FromArgb(139, 147, 167);
        public static Color Ok = Color.FromArgb(56, 193, 114);
        public static Color Err = Color.FromArgb(224, 79, 95);
        public static Color Warn = Color.FromArgb(230, 162, 60);
        public static Color Accent = Color.FromArgb(108, 140, 255);
        public static Color Mono = Color.FromArgb(13, 15, 20);
    }

    // ==================== SSH ====================
    public static class Ssh
    {
        public static AppConfig Cfg = new AppConfig();

        static string CtlPath
        {
            get
            {
                var d = Path.Combine(Path.GetTempPath(), "scu");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return Path.Combine(d, "mp-%r@%h-%p");
            }
        }

        static string Args
        {
            get
            {
                return "-o BatchMode=yes -o ConnectTimeout=6 -o StrictHostKeyChecking=accept-new "
                     + "-o ServerAliveInterval=15 "
                     + "-o ControlMaster=auto -o ControlPersist=300 "
                     + "-o ControlPath=\"" + CtlPath + "\" ";
            }
        }

        public static string Run(string command, int timeoutSec = 20)
        {
            return Core(command, null, timeoutSec);
        }

        public static string RunSudo(string command, string password, int timeoutSec = 40)
        {
            return Core("sudo -S -p '' " + command, password, timeoutSec);
        }

        static string Core(string command, string stdin, int timeoutSec)
        {
            try
            {
                var psi = new ProcessStartInfo("ssh")
                {
                    Arguments = Args + Cfg.SshTarget + " " + Quote(command),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = stdin != null,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                using (var p = Process.Start(psi))
                {
                    if (stdin != null)
                    {
                        p.StandardInput.Write(stdin + "\n");
                        p.StandardInput.Close();
                    }
                    var o = p.StandardOutput.ReadToEndAsync();
                    var e = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutSec * 1000))
                    {
                        try { p.Kill(); } catch { }
                        return "";
                    }
                    Task.WaitAll(new Task[] { o, e }, 3000);
                    return (o.IsCompleted ? o.Result : "") + (e.IsCompleted ? e.Result : "");
                }
            }
            catch { return ""; }
        }

        static string Quote(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`") + "\"";
        }

        public static bool CheckPassword(string password)
        {
            var r = RunSudo("true", password, 12);
            return !r.Contains("Sorry") && !r.Contains("Permission denied")
                && !r.Contains("incorrect password") && !r.Contains("not allowed");
        }

        public static bool Test()
        {
            return Run("echo ok", 10).Trim().Contains("ok");
        }
    }

    // ==================== МОДЕЛИ ====================
    public class Status
    {
        public string Host = "";
        public int TempC = -1;
        public long Uptime;
        public long MemUsed, MemTotal, SwapUsed, SwapTotal;
        public int DiskPct = -1, HddPct = -1;
        public string DiskFree = "", HddFree = "";
        public long VramUsed, VramTotal;
        public string LanIp = "", TailIp = "";
        public Dictionary<string, string> Services = new Dictionary<string, string>();
        public bool HasTemp { get { return TempC >= 0; } }
        public bool HasHdd { get { return HddPct >= 0; } }
    }

    // ==================== ОКНО ВХОДА ====================
    public class LoginForm : Form
    {
        public string Password { get; private set; }
        readonly TextBox _host, _pass;
        readonly Label _err;
        readonly Button _ok;
        readonly CheckBox _remember;

        public LoginForm(AppConfig cfg)
        {
            Text = "ServerControl — подключение";
            ClientSize = new Size(430, 360);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = P.Bg;

            Controls.Add(new Label
            {
                Text = "ServerControl", ForeColor = P.Text,
                Font = new Font("Segoe UI", 19, FontStyle.Bold),
                Location = new Point(32, 26), AutoSize = true
            });
            Controls.Add(new Label
            {
                Text = "Панель управления Linux-сервером", ForeColor = P.Muted,
                Font = new Font("Segoe UI", 9), Location = new Point(34, 64), AutoSize = true
            });

            Controls.Add(FieldLabel("Сервер (user@host)", 96));
            _host = Input(cfg.SshTarget, 116);
            Controls.Add(_host);

            Controls.Add(FieldLabel("Пароль sudo", 158));
            _pass = Input("", 178, true);
            Controls.Add(_pass);

            _remember = new CheckBox
            {
                Text = "Запомнить адрес сервера", ForeColor = P.Muted,
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(32, 212), AutoSize = true,
                Checked = cfg.SshTarget.Length > 0
            };
            Controls.Add(_remember);

            _err = new Label
            {
                ForeColor = P.Err, Font = new Font("Segoe UI", 8.5f),
                Location = new Point(34, 238), Width = 364, Height = 34
            };
            Controls.Add(_err);

            _ok = new Button
            {
                Text = "Подключиться", Location = new Point(32, 276), Width = 366, Height = 38,
                FlatStyle = FlatStyle.Flat, BackColor = P.Accent, ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            _ok.FlatAppearance.BorderSize = 0;
            _ok.Click += DoLogin;
            Controls.Add(_ok);

            var cancel = new Button
            {
                Text = "Отмена", Location = new Point(32, 320), Width = 366, Height = 26,
                FlatStyle = FlatStyle.Flat, BackColor = P.Bg, ForeColor = P.Muted
            };
            cancel.FlatAppearance.BorderSize = 0;
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            _pass.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoLogin(s, e); } };
            AcceptButton = _ok;
            MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); } };
            (_host.Text.Length == 0 ? _host : (Control)_pass).Focus();
        }

        Label FieldLabel(string t, int y) => new Label
        {
            Text = t, ForeColor = P.Muted, Font = new Font("Segoe UI", 8.5f),
            Location = new Point(34, y), AutoSize = true
        };

        TextBox Input(string v, int y, bool pass = false) => new TextBox
        {
            Text = v, Location = new Point(32, y), Width = 366,
            Font = new Font("Segoe UI", 11), UseSystemPasswordChar = pass,
            BackColor = P.Card, ForeColor = P.Text, BorderStyle = BorderStyle.FixedSingle
        };

        async void DoLogin(object sender, EventArgs e)
        {
            var host = _host.Text.Trim();
            if (!host.Contains("@")) { _err.Text = "Укажите сервер в формате user@host"; return; }
            if (_pass.Text.Length == 0) { _err.Text = "Введите пароль sudo"; return; }

            _err.Text = "Подключение…";
            _ok.Enabled = false;
            Ssh.Cfg.SshTarget = host;

            var problem = await Task.Run(() =>
            {
                if (!Ssh.Test()) return "нет SSH-доступа — проверьте адрес, ключи и запущенный sshd";
                if (!Ssh.CheckPassword(_pass.Text)) return "неверный пароль sudo";
                return null;
            });

            if (problem == null)
            {
                Password = _pass.Text;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            _err.Text = problem;
            _ok.Enabled = true;
            _pass.SelectAll();
            _pass.Focus();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    }
}
