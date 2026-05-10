param(
    [Parameter(Mandatory=$true)]
    [string]$FileName
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

try {
    $Screen = [System.Windows.Forms.Screen]::PrimaryScreen
    $Width  = $Screen.Bounds.Width
    $Height = $Screen.Bounds.Height
    $Left   = $Screen.Bounds.Left
    $Top    = $Screen.Bounds.Top

    $Bitmap = New-Object System.Drawing.Bitmap($Width, $Height)
    $Graphic = [System.Drawing.Graphics]::FromImage($Bitmap)
    
    # Capture the screen
    $Graphic.CopyFromScreen($Left, $Top, 0, 0, $Bitmap.Size)

    # Save the file
    $extension = [System.IO.Path]::GetExtension($FileName).ToLower()
    $format = [System.Drawing.Imaging.ImageFormat]::Png
    
    if ($extension -eq ".jpg" -or $extension -eq ".jpeg") {
        $format = [System.Drawing.Imaging.ImageFormat]::Jpeg
    } elseif ($extension -eq ".bmp") {
        $format = [System.Drawing.Imaging.ImageFormat]::Bmp
    }

    $Bitmap.Save($FileName, $format)
    
    Write-Host "Screenshot saved to: $FileName"
}
catch {
    Write-Error "Failed to take screenshot: $_"
}
finally {
    if ($Graphic) { $Graphic.Dispose() }
    if ($Bitmap) { $Bitmap.Dispose() }
}
