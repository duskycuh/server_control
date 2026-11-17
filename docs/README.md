# ServerControl Universal

Панель управления **любым** Linux-сервером по SSH. Не привязана к конкретному серверу — все службы, пути и ссылки настраиваются.

---

## Что это

Windows-приложение (.exe), которое подключается к вашему Linux-серверу по SSH и показывает:

- температуру, память, диски, VRAM
- состояние выбранных служб (systemd)
- все юниты systemd с управлением
- графики по истории
- бэкапы
- SSH-туннели для веб-приложений

---

## Установка (5 минут)

### 1. На сервере: скопировать API

```bash
sudo mkdir -p /usr/local/bin/servercontrol
sudo cp server_api.sh config.sh.example /usr/local/bin/servercontrol/
sudo chmod +x /usr/local/bin/servercontrol/server_api.sh
```

### 2. Настроить конфиг

```bash
sudo nano /usr/local/bin/servercontrol/config.sh
```

Пример:
```bash
# Диск с данными (если есть)
DATA_DISK="/mnt/storage"

# Службы для отслеживания
WATCH_SERVICES="nginx docker postgresql smbd"

# Каталог логов здоровья
HEALTH_DIR="/var/log/servercontrol"

# Маркеры процессов (необязательно)
GENERATION_MARKER="comfyui"
AGENT_MARKER=""
```

### 3. Логи здоровья (для графиков)

Если хотите графики — добавьте в cron:

```bash
sudo crontab -e
```

```cron
0 * * * * mkdir -p /var/log/servercontrol && \
  { echo "=== $(date '+%Y-%m-%d %H:%M') ==="; \
    echo "CPU: $(( $(cat /sys/class/thermal/thermal_zone*/temp 2>/dev/null|head -1) / 1000 ))°C load: $(cut -d' ' -f1-3 /proc/loadavg)"; } \
  >> /var/log/servercontrol/health_$(date +%F).log
```

### 4. SSH-ключ

Чтобы не вводить пароль ssh каждый раз:

```powershell
ssh-keygen -t ed25519
ssh-copy-id user@ваш-сервер
```

### 5. Запуск приложения

Скопируйте `ServerControl.exe` на рабочий стол и запустите.

В окне входа:
- **Сервер:** `ваш_юзер@192.168.1.50`
- **Пароль sudo:** ваш пароль (нужен для старт/стоп служб)

---

## Настройки приложения

Раздел **«Настройки»**:

| Параметр | Что делает |
|---|---|
| Сервер (user@host) | адрес SSH |
| Путь к server_api.sh | по умолчанию `/usr/local/bin/servercontrol` |
| Порт агента | для SSH-туннеля |
| Обновление, сек | частота опроса |
| Сворачивать в трей | крестик = в трей |
| Запускать свёрнутым | старт в трее |
| Ссылки | `Название\|http://адрес` — кнопки в разделе «Ссылки» и в трее |

Сохраняется в `%APPDATA%\ServerControlUniversal\config.ini`.

---

## Горячие клавиши

| | |
|---|---|
| **F5** | обновить |
| **Ctrl+T** | SSH-терминал |
| **Ctrl+O** | Обзор |
| **Ctrl+S** | Службы |
| **Ctrl+U** | Все юниты |
| **Ctrl+D** | Графики |
| **Ctrl+F** | поиск юнита |
| **Esc** | на обзор |

---

## Что требует настройки на сервере

| Возможность | Что нужно |
|---|---|
| Отслеживание служб | добавить в `WATCH_SERVICES` |
| Графики | cron-запись лога здоровья |
| Бэкапы | кнопка спрашивает команду |
| AI-агент | у вас должен быть процесс на порту (по умолчанию 3080) |
| Температура GPU | только AMD (sysfs) или через драйвер |

---

## Безопасность

- Пароль sudo **не сохраняется на диск** — только в памяти на время сессии
- Передаётся через stdin SSH, не в командной строке
- SSH-соединение мультиплексируется только в пределах сессии

---

## Компиляция из исходников

**Windows:**
```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe ^
  /out:ServerControl.exe ServerControl.cs MainForm.cs ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:System.Windows.Forms.DataVisualization.dll ^
  /resource:app.ico,app.ico /win32icon:app.ico
```

**Linux (Mono):**
```bash
mcs -target:winexe -out:ServerControl.exe ServerControl.cs MainForm.cs \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Core.dll \
  -r:System.Windows.Forms.DataVisualization.dll \
  -resource:app.ico,app.ico -win32icon:app.ico
```

---

## Структура

```
ServerControl-Universal/
├── dist/                    ← готовые файлы
│   ├── ServerControl.exe
│   ├── ServerControl.cs
│   ├── MainForm.cs
│   ├── server_api.sh
│   ├── config.sh.example
│   └── app.ico
├── src/                     ← исходники
└── docs/                    ← этот файл
```
