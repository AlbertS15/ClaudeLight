# Smoke test on the CI runner's desktop: starts Lumi, drives the hotkey and the bar,
# and saves a screenshot after each step to dist/screenshots. Fails if the app exits early.
param([string]$Exe = "dist/app/Lumi.exe")

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Keys {
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    const uint Up = 0x0002;
    public static void Chord(byte modifier, byte key) {
        keybd_event(modifier, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, Up, UIntPtr.Zero);
        keybd_event(modifier, 0, Up, UIntPtr.Zero);
    }
}
"@

$shots = "dist/screenshots"
New-Item -ItemType Directory -Force -Path $shots | Out-Null

function Shot([string]$name) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bitmap.Save("$shots/$name.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    Write-Host "screenshot: $name"
}

function AssertAlive([string]$step) {
    if ($script:app.HasExited) {
        Shot "crash-$step"
        throw "Lumi exited during '$step' with code $($script:app.ExitCode)"
    }
}

# Interface in Russian, as most users have it; the welcome window shows on first launch.
$settingsDir = Join-Path $env:APPDATA "Lumi"
New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
'{ "Language": "ru", "ShowWelcomeOnLaunch": true }' | Set-Content -Encoding UTF8 (Join-Path $settingsDir "settings.json")

$script:app = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 10
AssertAlive "launch"
Shot "1-welcome"

# Alt + the key left of 1 (VK_MENU 0x12, VK_OEM_3 0xC0) opens the bar.
[Keys]::Chord(0x12, 0xC0)
Start-Sleep -Seconds 2
AssertAlive "hotkey"
Shot "2-bar"

[System.Windows.Forms.SendKeys]::SendWait("notepad")
Start-Sleep -Seconds 4
AssertAlive "search"
Shot "3-search"

# Ctrl+Enter asks the model; with no Claude Code on the runner this shows the "not found" message.
[System.Windows.Forms.SendKeys]::SendWait("^{ENTER}")
Start-Sleep -Seconds 4
AssertAlive "ask"
Shot "4-answer"

[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Seconds 1
AssertAlive "escape"

Stop-Process -Id $script:app.Id -Force
Write-Host "smoke test passed"
