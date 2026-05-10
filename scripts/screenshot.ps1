param(
    [Parameter(Mandatory=$true)]
    [string]$FileName
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class DpiAwareHelper {
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    public static void Enable() {
        try {
            SetProcessDPIAware();
        }
        catch {
            // Ignore if the process is already DPI aware or the API is unavailable.
        }
    }
}
"@

try {
    [DpiAwareHelper]::Enable()

    $Left = [System.Windows.Forms.SystemInformation]::VirtualScreen.Left
    $Top = [System.Windows.Forms.SystemInformation]::VirtualScreen.Top
    $Width = [System.Windows.Forms.SystemInformation]::VirtualScreen.Width
    $Height = [System.Windows.Forms.SystemInformation]::VirtualScreen.Height

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
