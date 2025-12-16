Add-Type -AssemblyName System.Drawing

$pngPath = '..\Logos\favicon.png'
$icoPath = 'installer_icon.ico'

Write-Host "Converting PNG to ICO..."
Write-Host "Source: $pngPath"

# Load the original image
$originalImage = [System.Drawing.Image]::FromFile((Resolve-Path $pngPath))
Write-Host "Original size: $($originalImage.Width)x$($originalImage.Height)"

# ICO sizes to include (standard Windows icon sizes)
$sizes = @(16, 32, 48, 256)

# Create memory stream for ICO file
$icoStream = New-Object System.IO.MemoryStream

# ICO header
$writer = New-Object System.IO.BinaryWriter($icoStream)
$writer.Write([Int16]0)           # Reserved
$writer.Write([Int16]1)           # Type (1 = ICO)
$writer.Write([Int16]$sizes.Count) # Number of images

# Calculate offset (header = 6 bytes, each entry = 16 bytes)
$offset = 6 + ($sizes.Count * 16)
$imageDataList = @()

foreach ($size in $sizes) {
    # Create resized bitmap
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($originalImage, 0, 0, $size, $size)
    $graphics.Dispose()

    # Save to PNG in memory
    $pngStream = New-Object System.IO.MemoryStream
    $bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngData = $pngStream.ToArray()
    $pngStream.Dispose()
    $bitmap.Dispose()

    $imageDataList += ,@{Size=$size; Data=$pngData; Offset=$offset}
    $offset += $pngData.Length

    Write-Host "  Created ${size}x${size} icon"
}

# Write directory entries
foreach ($item in $imageDataList) {
    $s = $item.Size
    $widthByte = if ($s -eq 256) { 0 } else { $s }
    $heightByte = if ($s -eq 256) { 0 } else { $s }

    $writer.Write([byte]$widthByte)   # Width (0 = 256)
    $writer.Write([byte]$heightByte)  # Height (0 = 256)
    $writer.Write([byte]0)            # Color palette
    $writer.Write([byte]0)            # Reserved
    $writer.Write([Int16]1)           # Color planes
    $writer.Write([Int16]32)          # Bits per pixel
    $writer.Write([Int32]$item.Data.Length)  # Size of image data
    $writer.Write([Int32]$item.Offset)       # Offset to image data
}

# Write image data
foreach ($item in $imageDataList) {
    $writer.Write($item.Data)
}

$writer.Flush()

# Save to file
[System.IO.File]::WriteAllBytes($icoPath, $icoStream.ToArray())

$writer.Dispose()
$icoStream.Dispose()
$originalImage.Dispose()

Write-Host ""
Write-Host "ICO file created successfully!" -ForegroundColor Green
Write-Host "Output: $icoPath"
Write-Host "Sizes included: 16x16, 32x32, 48x48, 256x256"
