param(
    [string]$WorldName = ""
)

$ErrorActionPreference = "Stop"
$projectPath = "Survivalcraft.Windows\Survivalcraft.Windows.csproj"

Write-Host "Building the project..."
dotnet build $projectPath -c Debug

$buildDir = "Survivalcraft.Windows\bin\Debug"

# Create separate directories to bypass the single-instance Mutex
$serverDir = "Survivalcraft.Windows\bin\Server_Test"
$clientDir = "Survivalcraft.Windows\bin\Client_Test"

if (Test-Path $serverDir) { Remove-Item -Recurse -Force $serverDir }
if (Test-Path $clientDir) { Remove-Item -Recurse -Force $clientDir }

New-Item -ItemType Directory -Force -Path $serverDir | Out-Null
New-Item -ItemType Directory -Force -Path $clientDir | Out-Null

Write-Host "Copying files to bypass mutex..."
Copy-Item -Path "$buildDir\*" -Destination $serverDir -Recurse
Copy-Item -Path "$buildDir\*" -Destination $clientDir -Recurse

$serverArgs = if ($WorldName) { "-play `"$WorldName`"" } else { "" }

Write-Host "Starting Server process..."
Start-Process -FilePath "$serverDir\Survivalcraft.exe" -WorkingDirectory $serverDir -ArgumentList $serverArgs

# Wait for the server to load the world and start listening
Write-Host "Waiting 5 seconds for Server to initialize..."
Start-Sleep -Seconds 5

Write-Host "Starting Client process..."
# We use -join to automatically join the localhost server port
Start-Process -FilePath "$clientDir\Survivalcraft.exe" -WorkingDirectory $clientDir -ArgumentList "-join 127.0.0.1:28887"

Write-Host "Processes started."
if ($WorldName) {
    Write-Host "Server was instructed to auto-start world: $WorldName"
} else {
    Write-Host "Note: No world name provided. Please load or create a world in the Server process to start the server properly."
}
Write-Host "The Client process will attempt to join localhost:28887 automatically."
