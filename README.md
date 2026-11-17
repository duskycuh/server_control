# ServerControl Universal

A Windows desktop panel for managing **any** Linux server over SSH.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-4.5%2B-purple)
![License](https://img.shields.io/badge/license-MIT-green)

No agents to install. No ports to open. Just SSH and systemd.

---

## Why this exists

Managing a home or small server over SSH is tedious: just to check status you need
a dozen commands. Existing tools (Portainer, Cockpit, Netdata) require installing an
agent, opening ports, or a web backend.

ServerControl uses what every Linux box already has: **SSH and systemd**.

---

## Features

| | |
|---|---|
| **Live status** | CPU temperature, RAM, swap, disks, VRAM |
| **Service control** | start / stop / restart any systemd unit |
| **All units** | browse every service, filter by state, search |
| **Graphs** | CPU temperature and load history |
| **System info** | hardware, temperatures, updates, failed units |
| **Backups** | find archives, run your backup command |
| **SSH tunnel** | one button to tunnel a remote web app to localhost |
| **Custom links** | buttons for your own web interfaces |
| **System tray** | minimize instead of close, quick actions |
| **Dark theme** | easy on the eyes |

---

## Requirements

**Client (Windows):**
- Windows 7 SP1 or later
- .NET Framework 4.5+
- SSH client in `PATH` (built into Windows 10/11)

**Server (Linux):**
- systemd
- SSH access
- `bash`, `python3`

---

## Quick start

### 1. Install the server API

```bash
sudo mkdir -p /usr/local/bin/servercontrol
sudo cp server_api.sh /usr/local/bin/servercontrol/
sudo chmod 755 /usr/local/bin/servercontrol/server_api.sh

# optional: per-server settings
sudo cp config.sh.example /usr/local/bin/servercontrol/config.sh
sudo chmod 644 /usr/local/bin/servercontrol/config.sh
sudo nano /usr/local/bin/servercontrol/config.sh
```

### 2. Configure services to watch

Edit `config.sh`:

```bash
DATA_DISK="/mnt/storage"
WATCH_SERVICES="nginx docker postgresql smbd"
HEALTH_DIR="/var/log/servercontrol"
AGENT_MARKER="myapp"
```

### 3. Enable history graphs (optional)

Add to root's crontab:

```cron
0 * * * * mkdir -p /var/log/servercontrol && \
  { echo "=== $(date '+%Y-%m-%d %H:%M') ==="; \
    echo "CPU: $(( $(cat /sys/class/thermal/thermal_zone*/temp 2>/dev/null|head -1) / 1000 ))°C load: $(cut -d' ' -f1-3 /proc/loadavg)"; } \
  >> /var/log/servercontrol/health_$(date +%F).log
```

### 4. Set up SSH keys (recommended)

```powershell
ssh-keygen -t ed25519
ssh-copy-id user@your-server
```

### 5. Run

Download `ServerControl.exe` from [Releases](../../releases), run it, and enter:
- **Server:** `user@192.168.1.50`
- **sudo password:** your password (needed for service control)

Settings are saved to `%APPDATA%\ServerControlUniversal\config.ini`.

---

## Screenshots

*Coming soon.*

---

## Hotkeys

| Key | Action |
|---|---|
| `F5` | refresh |
| `Ctrl+T` | open SSH terminal |
| `Ctrl+O` | overview |
| `Ctrl+S` | services |
| `Ctrl+U` | all units |
| `Ctrl+D` | graphs |
| `Ctrl+F` | search unit |
| `Esc` | back to overview |

---

## Configuration

All settings live in the **Settings** tab:

| Setting | Description |
|---|---|
| Server (user@host) | SSH target |
| API path | where `server_api.sh` lives on the server |
| Agent port | local port for the SSH tunnel |
| Refresh interval | seconds between status polls |
| Minimize to tray | close button hides window |
| Start minimized | launch into tray |
| Links | `Name\|http://address` — custom buttons |

---

## Security

- The sudo password is **never written to disk**
- It is passed via SSH stdin, not the command line
- SSH connections are multiplexed only within a session
- No inbound ports are opened on the server

⚠️ The app runs `systemctl` and arbitrary commands via sudo. Only connect to servers you own.

---

## Building from source

**Windows:**
```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe ^
  /out:ServerControl.exe ServerControl.cs MainForm.cs ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:System.Windows.Forms.DataVisualization.dll ^
  /resource:app.ico,app.ico
```

**Linux (Mono):**
```bash
mcs -target:winexe -out:ServerControl.exe ServerControl.cs MainForm.cs \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Core.dll \
  -r:System.Windows.Forms.DataVisualization.dll \
  -resource:app.ico,app.ico
```

---

## Project structure

```
.
├── dist/                  ready-to-use files
│   ├── ServerControl.exe
│   ├── server_api.sh
│   └── config.sh.example
├── src/
│   ├── ServerControl.cs   config, SSH, models, login window
│   └── MainForm.cs        main window and pages
├── docs/
│   └── README.md          detailed setup guide (Russian)
└── LICENSE
```

---

## Troubleshooting

### Nothing happens when I double-click the .exe

Look for the error log first:

```
%APPDATA%\ServerControlUniversal\error.log
```

Then check that .NET Framework 4.5 or newer is installed:

```powershell
(Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full").Version
```

If the value is missing or below `4.5`, install [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48).

### Windows shows "Windows protected your PC"

The executable is not code-signed, so SmartScreen warns about it. Click
**More info** → **Run anyway**.

To avoid the prompt entirely, unblock the file first:

```powershell
Unblock-File .\ServerControl.exe
```

### Antivirus deletes the .exe

Unsigned .NET executables often trigger heuristics in AV software. Add the folder
to your antivirus exclusions, or build the app yourself from `src/` — the result is
functionally identical.

### "The system cannot find the file specified" when connecting

The app cannot reach `server_api.sh` on the server. Verify the path:

```bash
ls -l /usr/local/bin/servercontrol/server_api.sh
```

The file must be readable and executable by the SSH user:

```bash
sudo chmod 755 /usr/local/bin/servercontrol/server_api.sh
sudo chmod 644 /usr/local/bin/servercontrol/config.sh
```

Then set the correct folder in **Settings → API path**.

### Connects but shows no services

`WATCH_SERVICES` in `config.sh` is empty. Edit it:

```bash
sudo nano /usr/local/bin/servercontrol/config.sh
```

```bash
WATCH_SERVICES="nginx docker postgresql"
```

Restart is not needed — the app re-reads it on every refresh.

### Graphs say "not enough data"

The history file has fewer than two samples. The graphs read
`$HEALTH_DIR/health_YYYY-MM-DD.log`, filled by the cron job from
[step 3](#3-enable-history-graphs-optional). Wait a couple of hours, or run
the cron command manually once.

### Service start/stop silently fails

The sudo password was rejected, or the user has no sudo rights at all. Check
manually:

```bash
sudo -k && sudo systemctl status ssh
```

If that works, re-enter the password in the app. If the server uses
`NOPASSWD` for some commands only, the app still needs the full sudo password
for `systemctl` on arbitrary units.

### SSR / Remote desktop: window does not appear

The app opens on the current session. If you use RDP or VNC, make sure the
session is active before launching.

---

## Limitations

- **Windows-only** client (WinForms + .NET Framework)
- **NVIDIA GPU temperature** not read (AMD/Intel via sysfs only)
- Graphs need the cron job above — otherwise the page shows "not enough data"
- The AI-agent tab only opens a tunnel; the agent itself must run on your server

---

## Contributing

Issues and pull requests are welcome. For a quick check before committing:

```bash
mcs -target:winexe -out:/tmp/test.exe ServerControl.cs MainForm.cs \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Core.dll \
  -r:System.Windows.Forms.DataVisualization.dll
```

Keep PRs small and focused — one change per PR.

---

## License

MIT — see [LICENSE](LICENSE).
