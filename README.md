> [!NOTE]
> vaporvance has released an official port of Aero10 to Windows 11. It currently does not include Metro or Vista variants, but nevertheless the Aero11 theme will most likely be discontinued in favor of Aero10.1.
> 
> Link to Aero10.1: https://github.com/vaporvance/aero10

# Aero11: Seven

[Aero10](https://www.deviantart.com/vaporvance/art/909711949) faithfully ported to Windows 11, with the necessary changes to make it work properly.

This branch houses the Windows 7 version of Aero11, all available versions are listed below.

[Aero11: Seven](https://github.com/SandTechStuff/Aero11/tree/aero11_seven) | [Aero11: Vista](https://github.com/SandTechStuff/Aero11/tree/aero11_vista) | [Aero11: Metro](https://github.com/SandTechStuff/Aero11/tree/aero11_metro)

*Made with permission from vaporvance.*

![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/SandTechStuff/Aero11/total)

![screenshot](screenshot.png)

# Prerequisites

* You need to install a UxTheme patcher to allow custom `.msstyles` themes since Microsoft locks them out by default. Some options include the Windhawk mod [UXTheme hook](https://windhawk.net/mods/uxtheme-hook) and [SecureUxTheme](https://github.com/namazso/SecureUxTheme).
* To properly load ShellStyle mods, OldNewExplorer or the [UIFILE Override](https://windhawk.net/mods/uifile-override) Windhawk mod is needed.

# Installation

1. After patching UxTheme, download the latest release of the theme from [Releases](https://github.com/SandTechStuff/Aero11/releases) (Aero11.zip).
2. Extract the downloaded zip and place the contents in `C:\Windows\Resources\Themes`.
3. Either double click one of the `.theme` files from within the `Themes` folder or select your desired theme from within Windows settings.
4. The theme comes bundled with the Windows 7 wallpaper and accent color, but if you would prefer to use your own wallpapers or colors you can override anything through Windows personalization settings.

_Additional setup is needed to get an accurate Windows 7 look._

## Windows 11 26H1 compatibility build

This repository now includes a tested SecureUxTheme 4 build and a graphical
installer for Windows 11 x64 from build 22000 through **28000.1896**.

The installer:

* copies all Aero11 variants to `%SystemRoot%\Resources\Themes`;
* can install the bundled SecureUxTheme MSI;
* backs up an existing Aero11 installation under `%ProgramData%\Aero11\Backups`;
* can restore the most recent backup without deleting it; and
* blocks untested Windows builds and unsupported architectures.

Build the complete package with:

```powershell
.\build.ps1
```

The final executable is `dist\Aero11-Installer.exe`. It is self-contained but
unsigned; Windows may display an Unknown Publisher prompt until it is signed
with a trusted Authenticode certificate.

`dist\SecureUxTheme-ForceUninstaller.exe` is included for incomplete legacy
installations. Aero11 Installer runs the same cleanup automatically before
every SecureUxTheme v4 installation. The cleanup detects registered MSI
products, v3/v4 IFEO hooks and orphaned DLLs, and backs them up under
`%ProgramData%\Aero11\SecureUxThemeCleanup` before removal. A raw upstream MSI
does not run this cleanup, so use the force uninstaller first when installing
that MSI directly.

SecureUxTheme needs a new Windows session before an unsigned `.msstyles` theme
is applied. The installer offers to sign out automatically; if the hook is not
active after signing in, restart Windows once. Restoring Aero11 files does not
uninstall SecureUxTheme; remove it from Installed apps if needed.

After SecureUxTheme installation, the GUI can close the current session and
resume automatically on the next sign-in through a temporary elevated
scheduled task. The resumed installer removes that task first, then asks for
confirmation before opening the selected theme. A manual equivalent is
available at `scripts\Resume-Aero11AfterLogoff.ps1`.
