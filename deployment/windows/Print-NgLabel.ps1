#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][uri]$AppUrl,
    [Parameter(Mandatory)][string]$PrinterName,
    [ValidateRange(10, 200)][decimal]$WidthMm = 20,
    [ValidateRange(10, 200)][decimal]$HeightMm = 10,
    [ValidateRange(1, 4)][int]$Columns = 4,
    [ValidateRange(0, 20)][decimal]$GapMm = 3,
    [ValidateRange(0, 20)][decimal]$LeftMarginMm = 1,
    [ValidateRange(10, 300)][decimal]$MediaWidthMm = 95,
    [ValidateRange(0, 20)][decimal]$VerticalGapMm = 3,
    [switch]$ArgoxRaw,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{32}$')][string]$JobId,
    [Parameter(Mandatory)][pscredential]$Credential
)

$ErrorActionPreference = 'Stop'
if ($AppUrl.Scheme -ne 'https' -and !($AppUrl.Scheme -eq 'http' -and $AppUrl.IsLoopback)) {
    throw 'Gunakan HTTPS atau alamat HTTP loopback.'
}

Add-Type -AssemblyName System.Drawing
$document = New-Object System.Drawing.Printing.PrintDocument
$document.PrintController = New-Object System.Drawing.Printing.StandardPrintController
$document.PrinterSettings.PrinterName = $PrinterName
$document.PrinterSettings.Copies = 1
if (!$document.PrinterSettings.IsValid) {
    $document.Dispose()
    throw 'Antrean printer tidak tersedia. Periksa nama printer dan driver Windows.'
}
if ($ArgoxRaw) {
    if ($document.PrinterSettings.PrinterName -notmatch 'X-3200.*PPLB') {
        $document.Dispose()
        throw 'Gunakan antrean Argox X-3200 PPLB untuk mode RAW.'
    }
    Add-Type -Path (Join-Path $PSScriptRoot 'ArgoxPplb.cs')
}

$width = [double]$WidthMm * 100 / 25.4
$height = [double]$HeightMm * 100 / 25.4
$gap = [double]$GapMm * 100 / 25.4
$leftMargin = [double]$LeftMarginMm * 100 / 25.4
$rowWidth = [double]$MediaWidthMm * 100 / 25.4
if ($leftMargin + $Columns * $width + ($Columns - 1) * $gap -gt $rowWidth) {
    $document.Dispose()
    throw 'Susunan label melebihi lebar media.'
}
$paper = New-Object System.Drawing.Printing.PaperSize('Trace NG QR Row', ([int][Math]::Round($rowWidth)), ([int][Math]::Round($height)))
$document.DefaultPageSettings.PaperSize = $paper
$document.DefaultPageSettings.Landscape = $false
$document.DefaultPageSettings.Margins = New-Object System.Drawing.Printing.Margins(0, 0, 0, 0)
$document.DocumentName = "Trace NG $JobId"
$base = $AppUrl.AbsoluteUri.TrimEnd('/')
$route = "$base/api/v1/labels/print-jobs/$JobId/usb"
$progress = @{ Index = 0 }
try {
    $login = @{ email = $Credential.UserName; password = $Credential.GetNetworkCredential().Password }
    Invoke-RestMethod "$base/api/v1/auth/login" -Method Post -ContentType 'application/json' `
        -Body (ConvertTo-Json -InputObject $login) -SessionVariable authSession | Out-Null
    $login.Clear()
    $job = Invoke-RestMethod "$route/claim" -Method Post -WebSession $authSession
    $state = 'UNKNOWN'
    try {
        if ($ArgoxRaw) {
            $ids = [System.Collections.Generic.List[string]]::new()
            $matrices = [System.Collections.Generic.List[string[]]]::new()
            foreach ($item in $job.pages) {
                $ids.Add([string]$item.id)
                $matrices.Add([string[]]$item.rows)
            }
            $data = [ArgoxPplb]::Build($ids.ToArray(), $matrices.ToArray(), $MediaWidthMm,
                $WidthMm, $HeightMm, $GapMm, $VerticalGapMm, $LeftMarginMm, $Columns)
            [ArgoxPplb]::Send($PrinterName, $document.DocumentName, $data)
            $state = 'SPOOLED'
            return
        }
        $draw = {
            param($sender, $eventArgs)
            $pageBounds = $eventArgs.PageBounds
            if ($eventArgs.PageSettings.Landscape -or
                [Math]::Abs($pageBounds.Width - $rowWidth) -gt 2 -or
                [Math]::Abs($pageBounds.Height - $height) -gt 2) {
                $eventArgs.Cancel = $true
                throw "Driver mengubah ukuran atau orientasi media. Atur stock baris $MediaWidthMm x $HeightMm mm tanpa rotasi pada Printing Preferences ARGOX."
            }
            $graphics = $eventArgs.Graphics
            $graphics.TranslateTransform(-$eventArgs.PageSettings.HardMarginX, -$eventArgs.PageSettings.HardMarginY)
            $qrArea = [Math]::Min($height * 0.88, $width * 0.45)
            $dotsX = $graphics.DpiX / 100
            $dotsY = $graphics.DpiY / 100
            $font = New-Object Drawing.Font('Arial', 5.5, [Drawing.FontStyle]::Bold)
            $format = New-Object Drawing.StringFormat
            $format.Alignment = [Drawing.StringAlignment]::Near
            $format.LineAlignment = [Drawing.StringAlignment]::Center
            $format.FormatFlags = [Drawing.StringFormatFlags]::NoWrap
            try {
                for ($column = 0; $column -lt $Columns -and $progress.Index -lt $job.pages.Count; $column++) {
                    $page = $job.pages[$progress.Index]
                    $rows = $page.rows
                    $labelLeft = $leftMargin + $column * ($width + $gap)
                    $cellX = [Math]::Floor($qrArea / $rows.Count * $dotsX) / $dotsX
                    $cellY = [Math]::Floor($qrArea / $rows.Count * $dotsY) / $dotsY
                    if ([Math]::Min($cellX * $dotsX, $cellY * $dotsY) -lt 2) {
                        throw 'Resolusi printer terlalu rendah untuk QR pada ukuran label ini.'
                    }
                    $left = [Math]::Round(($labelLeft + ($qrArea - $rows.Count * $cellX) / 2) * $dotsX) / $dotsX
                    $top = [Math]::Round((($height - $rows.Count * $cellY) / 2) * $dotsY) / $dotsY
                    for ($y = 0; $y -lt $rows.Count; $y++) {
                        for ($x = 0; $x -lt $rows[$y].Length; $x++) {
                            if ($rows[$y][$x] -eq '1') {
                                $graphics.FillRectangle([Drawing.Brushes]::Black,
                                    [single]($left + $x * $cellX), [single]($top + $y * $cellY),
                                    [single]$cellX, [single]$cellY)
                            }
                        }
                    }
                    $textLeft = $labelLeft + $qrArea + 1
                    $textWidth = $width - $qrArea - 3
                    $textRect = New-Object Drawing.RectangleF($textLeft, 0, $textWidth, $height)
                    $measured = $graphics.MeasureString([string]$page.id, $font)
                    if ($measured.Width -gt $textWidth) {
                        $smallFont = New-Object Drawing.Font('Arial', ([single]($font.Size * $textWidth / $measured.Width)), [Drawing.FontStyle]::Bold)
                        try { $graphics.DrawString([string]$page.id, $smallFont, [Drawing.Brushes]::Black, $textRect, $format) }
                        finally { $smallFont.Dispose() }
                    } else {
                        $graphics.DrawString([string]$page.id, $font, [Drawing.Brushes]::Black, $textRect, $format)
                    }
                    $progress.Index++
                }
            } finally {
                $format.Dispose()
                $font.Dispose()
            }
            $eventArgs.HasMorePages = $progress.Index -lt $job.pages.Count
        }.GetNewClosure()
        $document.add_PrintPage($draw)
        $document.Print()
        $state = 'SPOOLED'
    } finally {
        try {
            Invoke-RestMethod "$route/report" -Method Post -WebSession $authSession -ContentType 'application/json' `
                -Body (ConvertTo-Json -InputObject @{ state = $state }) | Out-Null
            Write-Host "Job ${JobId}: $state. Periksa hasil fisik sebelum konfirmasi atau cetak ulang."
        } catch {
            Write-Warning "Status job $JobId belum tercatat. Periksa antrean Windows dan hasil fisik sebelum membuat job baru."
        }
    }
} finally {
    $document.Dispose()
    if ($authSession) {
        try { Invoke-RestMethod "$base/api/v1/auth/logout" -Method Post -WebSession $authSession | Out-Null }
        catch { Write-Warning 'Sesi komponen cetak tidak dapat ditutup dari PC ini.' }
    }
}
