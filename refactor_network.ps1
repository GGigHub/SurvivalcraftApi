$files = Get-ChildItem -Path "Survivalcraft" -Recurse -Include "*.cs"
foreach ($file in $files) {
    $content = [System.IO.File]::ReadAllText($file.FullName)
    $original = $content
    
    $content = $content -replace "Game\.NetWork", "Game.Network"
    $content = $content -replace "IPackage", "Packet"
    $content = $content -replace "PackageManager", "PacketManager"
    $content = $content -replace "PackageType", "PacketType"
    $content = $content -replace "PackageSerializer", "PacketSerializer"
    $content = $content -replace "PackageStreamReader", "PacketStreamReader"
    $content = $content -replace "PackageStreamWriter", "PacketStreamWriter"
    $content = $content -replace "ModPackage", "ModPacket"
    $content = $content -replace "NetWork\.Packages\.InventorySyncPackage", "Network.InventorySyncPacket"
    $content = $content -replace "QueuePackage", "QueuePacket"
    $content = $content -replace "RegisterPackage", "RegisterPacket"
    $content = $content -replace "UnRegisterPackage", "UnRegisterPacket"
    $content = $content -replace "DecodePackage", "DecodePacket"
    $content = $content -replace "DecodePackages", "DecodePackets"
    $content = $content -replace "PendingPackages", "PendingPackets"
    $content = $content -replace "PendingHandlePackages", "PendingHandlePackets"
    $content = $content -replace "OnNetworkReceivePackage", "OnNetworkReceivePacket"
    $content = $content -replace "SendWriterFromPackage", "SendWriterFromPacket"
    $content = $content -replace "SendWriterFromPackages", "SendWriterFromPackets"
    $content = $content -replace "TryDecodePackages", "TryDecodePackets"
    $content = $content -replace "basePackages", "basePackets"
    
    # Class names ending in Package
    $content = [regex]::Replace($content, "\b([A-Z][A-Za-z0-9_]*)Package\b", {
        param($m)
        if ($m.Groups[1].Value -eq "Package") { return $m.Value }
        return $m.Groups[1].Value + "Packet"
    })
    
    if ($file.FullName -like "*Survivalcraft\Network\*") {
        $content = $content -replace "\bpackage\b", "packet"
        $content = $content -replace "\bpackages\b", "packets"
    } elseif ($file.Name -ne "MainActivity.cs" -and $file.Name -ne "ModsManager.cs") {
        $content = $content -replace "\bpackage\b", "packet"
        $content = $content -replace "\bpackages\b", "packets"
    }
    
    if ($content -ne $original) {
        [System.IO.File]::WriteAllText($file.FullName, $content)
    }
}
