@echo off
setlocal EnableExtensions

REM Compress the folder containing this BAT file
set "SOURCE=%~dp0"
set "DEST=C:\Users\gauda\Downloads"

REM Create a timestamped ZIP filename
for /f %%I in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd_HH-mm-ss"') do set "TIMESTAMP=%%I"
for %%I in ("%SOURCE%.") do set "FOLDERNAME=%%~nxI"
set "ZIPNAME=%FOLDERNAME%_%TIMESTAMP%.zip"
set "ZIPPATH=%DEST%\%ZIPNAME%"

if not exist "%DEST%" mkdir "%DEST%"

echo Creating ZIP from: %SOURCE%
echo Destination: %ZIPPATH%

REM Prefer 7-Zip if installed
set "SEVENZIP="
if exist "%ProgramFiles%\7-Zip\7z.exe" set "SEVENZIP=%ProgramFiles%\7-Zip\7z.exe"
if exist "%ProgramFiles(x86)%\7-Zip\7z.exe" set "SEVENZIP=%ProgramFiles(x86)%\7-Zip\7z.exe"
if not defined SEVENZIP (
    for /f "delims=" %%I in ('where 7z.exe 2^>nul') do (
        set "SEVENZIP=%%I"
        goto :FOUND7Z
    )
)

:FOUND7Z
if defined SEVENZIP (
    "%SEVENZIP%" a -tzip "%ZIPPATH%" "%SOURCE%*" -xr!node_modules -xr!.git -x!*.gitignore -x!*.gitattributes -x!*.gitmodules -x!*.gitkeep
    if errorlevel 1 goto :FAIL
    goto :SUCCESS
)

REM Fallback: PowerShell, preserving relative paths and excluding node_modules and Git metadata
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
"$source = (Resolve-Path -LiteralPath '%SOURCE%').Path.TrimEnd('\');" ^
"$destination = '%ZIPPATH%';" ^
"$excludedDirs = @('node_modules', '.git');" ^
"$excludedFiles = @('.gitignore', '.gitattributes', '.gitmodules', '.gitkeep');" ^
"$files = Get-ChildItem -LiteralPath $source -Recurse -File -Force | Where-Object {" ^
"  $relative = [IO.Path]::GetRelativePath($source, $_.FullName);" ^
"  $parts = $relative -split '[\\/]';" ^
"  $skipDir = $false;" ^
"  foreach ($part in $parts) { if ($excludedDirs -contains $part) { $skipDir = $true } };" ^
"  $skipFile = ($excludedFiles -contains $_.Name) -or ($_.Name -match '\.git$');" ^
"  -not $skipDir -and -not $skipFile" ^
"};" ^
"if (-not $files) { Write-Error 'No files found to compress.'; exit 1 };" ^
"$temp = Join-Path $env:TEMP ('ZipTemp_' + [guid]::NewGuid().ToString());" ^
"New-Item -ItemType Directory -Path $temp | Out-Null;" ^
"try {" ^
"  foreach ($file in $files) {" ^
"    $relative = [IO.Path]::GetRelativePath($source, $file.FullName);" ^
"    $target = Join-Path $temp $relative;" ^
"    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null;" ^
"    Copy-Item -LiteralPath $file.FullName -Destination $target;" ^
"  };" ^
"  Compress-Archive -Path (Join-Path $temp '*') -DestinationPath $destination -CompressionLevel Optimal -ErrorAction Stop;" ^
"} finally {" ^
"  Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue;" ^
"}"

if errorlevel 1 goto :FAIL
goto :SUCCESS

:FAIL
echo.
echo ERROR: ZIP creation failed.
pause
exit /b 1

:SUCCESS
echo.
echo ZIP created successfully:
echo %ZIPPATH%
echo.
pause
endlocal
