#requires -version 5.1
<#
.SYNOPSIS
    Batch-converts a folder of PNG files to 32-bit uncompressed TGA, with the
    PNG transparency written into the TGA alpha channel.

.DESCRIPTION
    Run with no arguments for a small WinForms GUI.
    Run with -Folder for headless/batch use.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File PngToTga.ps1
    powershell -ExecutionPolicy Bypass -File PngToTga.ps1 -Folder "C:\tex" -Recurse
    powershell -ExecutionPolicy Bypass -File PngToTga.ps1 -Folder "C:\tex" -ColorKey FF00FF
#>
[CmdletBinding()]
param(
    [string]$Folder,
    [string]$OutFolder,
    [switch]$Recurse,
    [switch]$Overwrite,
    [switch]$TopLeftOrigin,
    # Hex RRGGBB. Pixels matching this colour become fully transparent.
    [string]$ColorKey,
    # Force RGB of fully transparent pixels to black.
    [switch]$ZeroTransparentRgb
)

Add-Type -AssemblyName System.Drawing

function ConvertFrom-HexColor {
    param([string]$Hex)
    if ([string]::IsNullOrWhiteSpace($Hex)) { return $null }
    $s = $Hex.Trim().TrimStart('#')
    if ($s.Length -ne 6) { throw "Colour key must be 6 hex digits (RRGGBB), got '$Hex'." }
    return @(
        [Convert]::ToInt32($s.Substring(0, 2), 16),
        [Convert]::ToInt32($s.Substring(2, 2), 16),
        [Convert]::ToInt32($s.Substring(4, 2), 16)
    )
}

function Convert-PngToTga {
    param(
        [Parameter(Mandatory)][string]$InPath,
        [Parameter(Mandatory)][string]$OutPath,
        [bool]$TopLeft = $false,
        [int[]]$Key = $null,
        [bool]$ZeroRgb = $false
    )

    # Load through a memory copy so the source file is never locked.
    $fileBytes = [System.IO.File]::ReadAllBytes($InPath)
    $ms = New-Object System.IO.MemoryStream(, $fileBytes)
    $src = [System.Drawing.Image]::FromStream($ms)
    $w = $src.Width
    $h = $src.Height

    # Normalise to 32bppArgb without blending (SourceCopy preserves alpha as-is).
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $gfx.DrawImage($src, 0, 0, $w, $h)
    $gfx.Dispose()
    $src.Dispose()
    $ms.Dispose()

    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $buf = New-Object byte[] ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
    $bmp.UnlockBits($data)
    $bmp.Dispose()

    # In-memory 32bppArgb is B,G,R,A per pixel - the exact byte order TGA wants.
    $keyed = 0
    if ($Key -or $ZeroRgb) {
        $kb = if ($Key) { [byte]$Key[2] } else { 0 }
        $kg = if ($Key) { [byte]$Key[1] } else { 0 }
        $kr = if ($Key) { [byte]$Key[0] } else { 0 }
        for ($y = 0; $y -lt $h; $y++) {
            $row = $y * $stride
            for ($x = 0; $x -lt $w; $x++) {
                $i = $row + ($x * 4)
                if ($Key -and $buf[$i] -eq $kb -and $buf[$i + 1] -eq $kg -and $buf[$i + 2] -eq $kr) {
                    $buf[$i + 3] = 0
                    $keyed++
                }
                if ($ZeroRgb -and $buf[$i + 3] -eq 0) {
                    $buf[$i] = 0; $buf[$i + 1] = 0; $buf[$i + 2] = 0
                }
            }
        }
    }

    $dir = Split-Path -Parent $OutPath
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    $fs = [System.IO.File]::Create($OutPath)
    try {
        # 18-byte TGA header: uncompressed true-colour, 32bpp.
        $hdr = New-Object byte[] 18
        $hdr[2] = 2                                   # image type: uncompressed RGB
        $hdr[12] = [byte]($w -band 0xFF)
        $hdr[13] = [byte](($w -shr 8) -band 0xFF)
        $hdr[14] = [byte]($h -band 0xFF)
        $hdr[15] = [byte](($h -shr 8) -band 0xFF)
        $hdr[16] = 32                                 # bits per pixel
        # bits 0-3 = alpha channel depth (8), bit 5 = origin at top.
        $hdr[17] = if ($TopLeft) { 0x28 } else { 0x08 }
        $fs.Write($hdr, 0, 18)

        $rowBytes = $w * 4
        if ($TopLeft) {
            for ($y = 0; $y -lt $h; $y++) { $fs.Write($buf, $y * $stride, $rowBytes) }
        }
        else {
            # Default bottom-left origin: write rows bottom-up.
            for ($y = $h - 1; $y -ge 0; $y--) { $fs.Write($buf, $y * $stride, $rowBytes) }
        }
    }
    finally { $fs.Dispose() }

    return [pscustomobject]@{
        Width       = $w
        Height      = $h
        KeyedPixels = $keyed
    }
}

function Invoke-Batch {
    param(
        [Parameter(Mandatory)][string]$InDir,
        [string]$OutDir,
        [bool]$Recurse = $false,
        [bool]$Overwrite = $false,
        [bool]$TopLeft = $false,
        [int[]]$Key = $null,
        [bool]$ZeroRgb = $false,
        [scriptblock]$Log = { param($m) Write-Host $m }
    )

    if (-not (Test-Path -LiteralPath $InDir)) { throw "Input folder not found: $InDir" }
    if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = $InDir }

    $files = Get-ChildItem -LiteralPath $InDir -Filter *.png -File -Recurse:$Recurse
    if (-not $files) { & $Log "No .png files found in $InDir"; return }

    $ok = 0; $skip = 0; $fail = 0
    $inRoot = (Resolve-Path -LiteralPath $InDir).Path.TrimEnd('\')

    foreach ($f in $files) {
        $rel = $f.FullName.Substring($inRoot.Length).TrimStart('\')
        $outPath = Join-Path $OutDir ([System.IO.Path]::ChangeExtension($rel, '.tga'))

        if ((Test-Path -LiteralPath $outPath) -and -not $Overwrite) {
            & $Log "SKIP  $rel (target exists)"
            $skip++
            continue
        }

        try {
            $r = Convert-PngToTga -InPath $f.FullName -OutPath $outPath -TopLeft $TopLeft -Key $Key -ZeroRgb $ZeroRgb
            $note = if ($r.KeyedPixels -gt 0) { " [$($r.KeyedPixels) keyed]" } else { "" }
            & $Log "OK    $rel  ->  $($r.Width)x$($r.Height) 32-bit$note"
            $ok++
        }
        catch {
            & $Log "FAIL  $rel : $($_.Exception.Message)"
            $fail++
        }
    }

    & $Log ""
    & $Log "Done. Converted $ok, skipped $skip, failed $fail."
}

# ---------------------------------------------------------------- headless ---

if ($Folder) {
    Invoke-Batch -InDir $Folder -OutDir $OutFolder -Recurse:$Recurse.IsPresent `
        -Overwrite:$Overwrite.IsPresent -TopLeft:$TopLeftOrigin.IsPresent `
        -Key (ConvertFrom-HexColor $ColorKey) -ZeroRgb:$ZeroTransparentRgb.IsPresent
    return
}

# --------------------------------------------------------------------- GUI ---

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$form = New-Object System.Windows.Forms.Form
$form.Text = "PNG -> TGA (32-bit, alpha preserved)"
$form.Size = New-Object System.Drawing.Size(680, 520)
$form.StartPosition = "CenterScreen"
$form.MinimumSize = New-Object System.Drawing.Size(560, 420)

function New-Label($text, $x, $y) {
    $l = New-Object System.Windows.Forms.Label
    $l.Text = $text; $l.Location = New-Object System.Drawing.Point($x, $y)
    $l.AutoSize = $true
    return $l
}

# Input folder
$form.Controls.Add((New-Label "Input folder (PNGs):" 12 15))
$txtIn = New-Object System.Windows.Forms.TextBox
$txtIn.Location = New-Object System.Drawing.Point(140, 12)
$txtIn.Size = New-Object System.Drawing.Size(420, 22)
$txtIn.Anchor = 'Top,Left,Right'
$form.Controls.Add($txtIn)

$btnIn = New-Object System.Windows.Forms.Button
$btnIn.Text = "Browse..."
$btnIn.Location = New-Object System.Drawing.Point(570, 10)
$btnIn.Size = New-Object System.Drawing.Size(85, 25)
$btnIn.Anchor = 'Top,Right'
$form.Controls.Add($btnIn)

# Output folder
$form.Controls.Add((New-Label "Output folder:" 12 47))
$txtOut = New-Object System.Windows.Forms.TextBox
$txtOut.Location = New-Object System.Drawing.Point(140, 44)
$txtOut.Size = New-Object System.Drawing.Size(420, 22)
$txtOut.Anchor = 'Top,Left,Right'
$form.Controls.Add($txtOut)

$btnOut = New-Object System.Windows.Forms.Button
$btnOut.Text = "Browse..."
$btnOut.Location = New-Object System.Drawing.Point(570, 42)
$btnOut.Size = New-Object System.Drawing.Size(85, 25)
$btnOut.Anchor = 'Top,Right'
$form.Controls.Add($btnOut)

$lblHint = New-Label "(blank = same folder as input)" 140 70
$lblHint.ForeColor = [System.Drawing.Color]::Gray
$form.Controls.Add($lblHint)

# Options
$chkRecurse = New-Object System.Windows.Forms.CheckBox
$chkRecurse.Text = "Include subfolders"
$chkRecurse.Location = New-Object System.Drawing.Point(15, 95)
$chkRecurse.AutoSize = $true
$form.Controls.Add($chkRecurse)

$chkOverwrite = New-Object System.Windows.Forms.CheckBox
$chkOverwrite.Text = "Overwrite existing .tga"
$chkOverwrite.Location = New-Object System.Drawing.Point(180, 95)
$chkOverwrite.AutoSize = $true
$chkOverwrite.Checked = $true
$form.Controls.Add($chkOverwrite)

$chkTop = New-Object System.Windows.Forms.CheckBox
$chkTop.Text = "Top-left origin"
$chkTop.Location = New-Object System.Drawing.Point(380, 95)
$chkTop.AutoSize = $true
$form.Controls.Add($chkTop)

$chkKey = New-Object System.Windows.Forms.CheckBox
$chkKey.Text = "Also make this colour transparent (RRGGBB):"
$chkKey.Location = New-Object System.Drawing.Point(15, 122)
$chkKey.AutoSize = $true
$form.Controls.Add($chkKey)

$txtKey = New-Object System.Windows.Forms.TextBox
$txtKey.Text = "FF00FF"
$txtKey.Location = New-Object System.Drawing.Point(300, 120)
$txtKey.Size = New-Object System.Drawing.Size(80, 22)
$txtKey.Enabled = $false
$form.Controls.Add($txtKey)

$chkZero = New-Object System.Windows.Forms.CheckBox
$chkZero.Text = "Black out RGB of fully transparent pixels"
$chkZero.Location = New-Object System.Drawing.Point(15, 149)
$chkZero.AutoSize = $true
$form.Controls.Add($chkZero)

# Log
$txtLog = New-Object System.Windows.Forms.TextBox
$txtLog.Multiline = $true
$txtLog.ScrollBars = "Vertical"
$txtLog.ReadOnly = $true
$txtLog.Font = New-Object System.Drawing.Font("Consolas", 9)
$txtLog.Location = New-Object System.Drawing.Point(12, 180)
$txtLog.Size = New-Object System.Drawing.Size(643, 250)
$txtLog.Anchor = 'Top,Left,Right,Bottom'
$form.Controls.Add($txtLog)

$btnGo = New-Object System.Windows.Forms.Button
$btnGo.Text = "Convert"
$btnGo.Location = New-Object System.Drawing.Point(545, 440)
$btnGo.Size = New-Object System.Drawing.Size(110, 32)
$btnGo.Anchor = 'Bottom,Right'
$form.Controls.Add($btnGo)
$form.AcceptButton = $btnGo

function Select-FolderInto($textBox) {
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    if ($textBox.Text -and (Test-Path -LiteralPath $textBox.Text)) { $dlg.SelectedPath = $textBox.Text }
    if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $textBox.Text = $dlg.SelectedPath }
}

$btnIn.Add_Click({ Select-FolderInto $txtIn })
$btnOut.Add_Click({ Select-FolderInto $txtOut })
$chkKey.Add_CheckedChanged({ $txtKey.Enabled = $chkKey.Checked })

$writeLog = {
    param($m)
    $txtLog.AppendText("$m`r`n")
    [System.Windows.Forms.Application]::DoEvents()
}

$btnGo.Add_Click({
        $txtLog.Clear()
        if (-not $txtIn.Text -or -not (Test-Path -LiteralPath $txtIn.Text)) {
            [System.Windows.Forms.MessageBox]::Show("Pick a valid input folder first.", "PNG -> TGA") | Out-Null
            return
        }
        $key = $null
        if ($chkKey.Checked) {
            try { $key = ConvertFrom-HexColor $txtKey.Text }
            catch {
                [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "PNG -> TGA") | Out-Null
                return
            }
        }
        $btnGo.Enabled = $false
        try {
            Invoke-Batch -InDir $txtIn.Text -OutDir $txtOut.Text -Recurse $chkRecurse.Checked `
                -Overwrite $chkOverwrite.Checked -TopLeft $chkTop.Checked `
                -Key $key -ZeroRgb $chkZero.Checked -Log $writeLog
        }
        catch { & $writeLog "ERROR: $($_.Exception.Message)" }
        finally { $btnGo.Enabled = $true }
    })

[void]$form.ShowDialog()
