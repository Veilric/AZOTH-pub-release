// Independent AZOTH installation identities shared by Setup, the app and Windows.

namespace AZOTH.Setup.Installation;

public static class SetupNames
{
    /// <summary>Independent AZOTH uninstall identity. Never reuse the upstream installer GUID.</summary>
    public const string AppGuid = "52A99766-B7BE-43D3-A17E-8ED3D5CEB19F";

    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{" + AppGuid + "}_is1";

    public const string MachineEnvironmentKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    public const string UserEnvironmentKey = "Environment";

    public const string AppName = "AZOTH";
    public const string Publisher = "Veilric";
    public const string AppExe = "AZOTH.exe";

    /// <summary>The uninstaller next to AZOTH.exe (UpdateService.IsInstalled looks for it).</summary>
    public const string UninstallerExe = "uninstall.exe";

    /// <summary>The list of installed files next to AZOTH.exe: what the uninstaller removes and an update replaces.</summary>
    public const string InstallRecordFile = "azoth-install.json";

    /// <summary>The "azoth" command (cli\azoth.cmd), the folder Setup puts on PATH.</summary>
    public const string CliFolder = "cli";

    /// <summary>Inno Setup's uninstaller files, removed when Setup takes over a 1.x installation.</summary>
    public static readonly IReadOnlyList<string> InnoUninstallerFiles = ["unins000.exe", "unins000.dat", "unins000.msg"];

    /// <summary>Start menu and desktop shortcut name.</summary>
    public const string ShortcutName = "AZOTH.lnk";

    /// <summary>Registry values Setup adds to the uninstall key (Inno Setup kept the same in "Inno Setup: …").</summary>
    public const string TasksValue = "AZOTH Setup: Tasks";
    public const string LanguageValue = "AZOTH Setup: Language";
    public const string ScopeValue = "AZOTH Setup: Scope";
    public const string InnoTasksValue = "Inno Setup: Selected Tasks";
    public const string InnoAppPathValue = "Inno Setup: App Path";
    public const string InnoVersionValue = "Inno Setup: Setup Version";
    public const string InnoLanguageValue = "Inno Setup: Language";

    /// <summary>Windows 10 1809: the oldest Windows AZOTH runs on (Inno Setup's MinVersion).</summary>
    public const int MinimumBuild = 17763;

    /// <summary>Where Microsoft offers the Visual C++ Redistributable (x64).</summary>
    public const string VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
}
