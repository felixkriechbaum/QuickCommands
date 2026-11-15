namespace QuickCommands.Core;

public sealed record CommandTemplate(
    string Name,
    ShellLanguage Language,
    string DescriptionEn,
    string DescriptionDe,
    string Script,
    IReadOnlyList<CommandParameter> Parameters)
{
    public string Description(string languageCode) => languageCode == "de" ? DescriptionDe : DescriptionEn;

    public QuickCommand ToCommand(string languageCode) => new()
    {
        Name = Name,
        Language = Language,
        Description = Description(languageCode),
        Script = Script,
        Parameters = Parameters.Select(p =>
        {
            var copy = p.Clone();
            if (languageCode == "de" && GermanParameterTexts.TryGetValue(copy.Description, out var de))
                copy.Description = de;
            return copy;
        }).ToList(),
    };

    private static readonly Dictionary<string, string> GermanParameterTexts = new(StringComparer.Ordinal)
    {
        ["Folder"] = "Ordner",
        ["File"] = "Datei",
        ["Command"] = "Befehl",
        ["Do not ask"] = "Nicht nachfragen",
        ["Number of levels"] = "Anzahl Ebenen",
        ["City (empty = by IP)"] = "Stadt (leer = nach IP)",
        ["Commit message"] = "Commit-Nachricht",
        ["Branch name"] = "Branch-Name",
        ["Subfolder"] = "Unterordner",
        ["Open VS Code"] = "VS Code öffnen",
    };
}

/// <summary>Ready-made commands to start from.</summary>
public static class TemplateLibrary
{
    private static CommandParameter Text(string name, string description, bool required = false, string @default = "") =>
        new() { Name = name, Kind = ParameterKind.Text, Required = required, Default = @default, Description = description };

    private static CommandParameter Switch(string name, string description) =>
        new() { Name = name, Kind = ParameterKind.Switch, Description = description };

    public static IReadOnlyList<CommandTemplate> All { get; } =
    [
        // ---------- PowerShell ----------
        new("mkcd", ShellLanguage.PowerShell, "Create a folder and change into it", "Ordner anlegen und hineinwechseln",
            "New-Item -ItemType Directory -Force -Path $name | Out-Null\nSet-Location -Path $name\n",
            [Text("name", "Folder", required: true)]),
        new("up", ShellLanguage.PowerShell, "Go up one or more folders", "Einen oder mehrere Ordner nach oben",
            "Set-Location -Path ((@('..') * [int]$levels) -join [IO.Path]::DirectorySeparatorChar)\n",
            [Text("levels", "Number of levels", @default: "1")]),
        new("open", ShellLanguage.PowerShell, "Open a folder in Explorer", "Ordner im Explorer öffnen",
            "Invoke-Item -Path $folder\n",
            [Text("folder", "Folder", @default: ".")]),
        new("touch", ShellLanguage.PowerShell, "Create a file or update its timestamp", "Datei anlegen oder Zeitstempel aktualisieren",
            "if (Test-Path -LiteralPath $file) { (Get-Item -LiteralPath $file).LastWriteTime = Get-Date }\nelse { New-Item -ItemType File -Path $file | Out-Null }\n",
            [Text("file", "File", required: true)]),
        new("which", ShellLanguage.PowerShell, "Show where a command comes from", "Zeigen, woher ein Befehl kommt",
            "Get-Command -Name $name -All | Format-Table CommandType, Name, Source -AutoSize\n",
            [Text("name", "Command", required: true)]),
        new("killport", ShellLanguage.PowerShell, "Stop the process listening on a port", "Prozess beenden, der einen Port belegt",
            "$ids = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique\n" +
            "if (-not $ids) { Write-Host \"Nothing listens on port $port.\"; return }\n" +
            "foreach ($id in $ids) {\n    $p = Get-Process -Id $id\n    if ($force -or (Read-Host \"Stop $($p.ProcessName) ($id)? [y/N]\") -eq 'y') { Stop-Process -Id $id -Force }\n}\n",
            [Text("port", "Port", required: true), Switch("force", "Do not ask")]),
        new("ports", ShellLanguage.PowerShell, "List listening TCP ports", "Belegte TCP-Ports anzeigen",
            "Get-NetTCPConnection -State Listen | Sort-Object LocalPort |\n    Select-Object LocalAddress, LocalPort, OwningProcess, @{ n = 'Process'; e = { (Get-Process -Id $_.OwningProcess).ProcessName } } |\n    Format-Table -AutoSize\n",
            []),
        new("myip", ShellLanguage.PowerShell, "Show local IPv4 addresses", "Lokale IPv4-Adressen anzeigen",
            "Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -ne '127.0.0.1' } | Format-Table InterfaceAlias, IPAddress -AutoSize\n",
            []),
        new("serve", ShellLanguage.PowerShell, "Serve the current folder over HTTP", "Aktuellen Ordner per HTTP bereitstellen",
            "Write-Host \"http://localhost:$port\"\npython -m http.server $port\n",
            [Text("port", "Port", @default: "8000")]),
        new("uuid", ShellLanguage.PowerShell, "New GUID, copied to the clipboard", "Neue GUID, in die Zwischenablage kopiert",
            "$id = [guid]::NewGuid().ToString()\n$id | Set-Clipboard\n$id\n",
            []),
        new("hosts", ShellLanguage.PowerShell, "Edit the hosts file as administrator", "hosts-Datei als Administrator bearbeiten",
            "Start-Process notepad.exe -ArgumentList \"$env:SystemRoot\\System32\\drivers\\etc\\hosts\" -Verb RunAs\n",
            []),
        new("weather", ShellLanguage.PowerShell, "Weather forecast in the terminal", "Wettervorhersage im Terminal",
            "(Invoke-WebRequest -Uri \"https://wttr.in/$($city)?0\" -UserAgent 'curl').Content\n",
            [Text("city", "City (empty = by IP)")]),
        new("gac", ShellLanguage.PowerShell, "git: stage everything and commit", "git: alles stagen und committen",
            "git add -A\nif ($LASTEXITCODE -eq 0) { git commit -m $message }\n",
            [Text("message", "Commit message", required: true)]),
        new("gnew", ShellLanguage.PowerShell, "git: create and switch to a branch", "git: Branch anlegen und wechseln",
            "git switch -c $branch\n",
            [Text("branch", "Branch name", required: true)]),

        // ---------- cmd ----------
        new("mkcd", ShellLanguage.Cmd, "Create a folder and change into it", "Ordner anlegen und hineinwechseln",
            "if not exist \"%name%\" mkdir \"%name%\"\ncd /d \"%name%\"\n",
            [Text("name", "Folder", required: true)]),
        new("dev", ShellLanguage.Cmd, "Go to the projects folder and open VS Code", "In den Projektordner wechseln und VS Code öffnen",
            "cd /d \"%USERPROFILE%\\source\\%project%\"\nif \"%code%\"==\"1\" code .\n",
            [Text("project", "Subfolder"), Switch("code", "Open VS Code")]),
        new("flushdns", ShellLanguage.Cmd, "Clear the DNS cache", "DNS-Cache leeren",
            "ipconfig /flushdns\n",
            []),
        new("myip", ShellLanguage.Cmd, "Show local IPv4 addresses", "Lokale IPv4-Adressen anzeigen",
            "ipconfig | findstr /i \"IPv4\"\n",
            []),

        // ---------- sh ----------
        new("mkcd", ShellLanguage.Sh, "Create a folder and change into it", "Ordner anlegen und hineinwechseln",
            "mkdir -p -- \"$name\" && cd -- \"$name\"\n",
            [Text("name", "Folder", required: true)]),
        new("up", ShellLanguage.Sh, "Go up one or more folders", "Einen oder mehrere Ordner nach oben",
            "local target=.\nlocal i=0\nwhile [ \"$i\" -lt \"$levels\" ]; do target=\"$target/..\"; i=$((i + 1)); done\ncd -- \"$target\"\n",
            [Text("levels", "Number of levels", @default: "1")]),
        new("open", ShellLanguage.Sh, "Open a folder in the file manager", "Ordner im Dateimanager öffnen",
            "if command -v xdg-open >/dev/null 2>&1; then xdg-open \"$folder\"\nelif command -v explorer.exe >/dev/null 2>&1; then explorer.exe \"$folder\"\nelse command open \"$folder\"; fi\n",
            [Text("folder", "Folder", @default: ".")]),
        new("killport", ShellLanguage.Sh, "Stop the process listening on a port", "Prozess beenden, der einen Port belegt",
            "local pids\npids=$(lsof -ti tcp:\"$port\" -sTCP:LISTEN)\nif [ -z \"$pids\" ]; then echo \"Nothing listens on port $port.\"; return 0; fi\nkill $pids\n",
            [Text("port", "Port", required: true)]),
        new("serve", ShellLanguage.Sh, "Serve the current folder over HTTP", "Aktuellen Ordner per HTTP bereitstellen",
            "echo \"http://localhost:$port\"\npython3 -m http.server \"$port\"\n",
            [Text("port", "Port", @default: "8000")]),
        new("gs", ShellLanguage.Sh, "git: short status", "git: kurzer Status",
            "git status -sb \"$@\"\n",
            []),
        new("gac", ShellLanguage.Sh, "git: stage everything and commit", "git: alles stagen und committen",
            "git add -A && git commit -m \"$message\"\n",
            [Text("message", "Commit message", required: true)]),
        new("weather", ShellLanguage.Sh, "Weather forecast in the terminal", "Wettervorhersage im Terminal",
            "curl -s \"https://wttr.in/${city}?0\"\n",
            [Text("city", "City (empty = by IP)")]),
    ];
}
