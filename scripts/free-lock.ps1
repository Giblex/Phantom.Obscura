$lockFile = "o:\Users\Giblex\Build Projects\Phantom Suite\Phantom.Obscura\src\UI.Desktop\obj\Debug\net10.0-windows10.0.19041.0\Avalonia\resources"
try { $fs = [System.IO.File]::Open($lockFile,'Open','ReadWrite','None'); $fs.Close(); "FILE FREE" } catch { "LOCKED: $($_.Exception.Message)" }
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object { $_.CommandLine -match 'MSBuild\.dll|avalonia\.buildservices' } | ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force; "killed $($_.ProcessId)" } catch { } }
Start-Sleep -Milliseconds 800
try { $fs = [System.IO.File]::Open($lockFile,'Open','ReadWrite','None'); $fs.Close(); "FILE FREE AFTER KILL" } catch { "STILL LOCKED: $($_.Exception.Message)" }
