# Contributing

Thanks for wanting to help. A few ground rules keep this project maintainable.

## Issues

Use the provided templates. A bug report without OS, .NET version and error text
cannot be reproduced.

Before opening an issue:

- Check the [Troubleshooting](https://github.com/duskycuh/server_control#troubleshooting) section
- Search existing issues — many problems repeat
- Try the latest release, not the `Code → Download ZIP` archive

## Pull requests

- One change per PR
- Keep the UI consistent — this is a dark-themed WinForms app without external dependencies
- Do not add NuGet packages. The whole point is that it compiles with the bare `csc.exe` / `mcs`
- Targets **.NET Framework 4.5** — no C# 8+, no `Span<T>`, no `System.Text.Json`

### Building

```bash
mcs -target:winexe -out:ServerControl.exe ServerControl.cs MainForm.cs \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Core.dll \
  -r:System.Windows.Forms.DataVisualization.dll \
  -resource:app.ico,app.ico
```

Verify the executable starts before submitting. On Linux you can at least confirm
the PE layout is valid:

```bash
python3 -c "
import struct
d=open('ServerControl.exe','rb').read()
pe=struct.unpack('<I',d[0x3c:0x40])[0]
n=struct.unpack('<H',d[pe+6:pe+8])[0]
optsz=struct.unpack('<H',d[pe+20:pe+22])[0]
sec=pe+24+optsz; prev=0; ok=True
for i in range(n):
    o=sec+i*40; vsz,va,rsz,ra=struct.unpack('<IIII',d[o+8:o+24])
    if ra<prev: ok=False
    prev=ra+rsz
print('sections OK' if ok and prev<=len(d) else 'BROKEN')
"
```

### Things that bit us before

| Symptom | Cause |
|---|---|
| App does not start at all, no window | Broken PE layout from `-win32icon` in Mono |
| `MissingMethodException: String.Split(Char, StringSplitOptions)` | `Split(char)` compiles to an overload missing in .NET Framework — use `new char[] { ... }` |
| Silent exit with code 0 | Unhandled exception during form construction; check `%APPDATA%\ServerControlUniversal\error.log` |

## What will likely be rejected

- Adding an installer, telemetry, or auto-updater that calls home
- Reworking the UI toolkit (WPF, Avalonia, Electron)
- Anything that requires the client to run as Administrator
