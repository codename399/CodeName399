@echo off
setlocal EnableExtensions EnableDelayedExpansion
title CodeName399 Windows Server Setup

REM ============================================================================
REM CodeName399 Windows Server Deployment
REM v17 - stable batch structure
REM
REM IMPORTANT:
REM   - All expensive build/deploy flags are FALSE by default.
REM   - Main execution ends with GOTO :MainEnd.
REM   - Every subroutine is below :MainEnd.
REM   - No GOTO is used to a label from inside a parenthesized FOR/IF block.
REM   - Cloudflare token is NOT printed to the console.
REM ============================================================================

REM ============================================================================
REM CONFIGURATION
REM ============================================================================

set "SOURCE_ROOT=C:\Users\gauda\source\repos\codename399"
set "API_PATH=%SOURCE_ROOT%\CodeName399.API"
set "ANGULAR_PATH=%SOURCE_ROOT%\CodeName399"

set "SLN_PATH=%API_PATH%\CodeName399.sln"

set "SERVER_ROOT=C:\Servers\CodeName399"
set "UI_PATH=%SERVER_ROOT%\UI"

set "GATEWAY_PATH=%SERVER_ROOT%\Gateway"
set "AUTH_PATH=%SERVER_ROOT%\Auth"
set "GAMESTASH_PATH=%SERVER_ROOT%\GameStash"
set "DEBTMANAGER_PATH=%SERVER_ROOT%\DebtManager"
set "KITE_PATH=%SERVER_ROOT%\Kite"

set "GATEWAY_SERVICE=CodeName399.Gateway"
set "AUTH_SERVICE=CodeName399.Auth"
set "GAMESTASH_SERVICE=CodeName399.GameStash"
set "DEBTMANAGER_SERVICE=CodeName399.DebtManager"
set "KITE_SERVICE=CodeName399.Kite"

set "GATEWAY_PORT=5000"
set "AUTH_PORT=5001"
set "GAMESTASH_PORT=5002"
set "DEBTMANAGER_PORT=5003"
set "KITE_PORT=5004"

set "NGINX_ROOT=C:\nginx"
set "NGINX_EXE=%NGINX_ROOT%\nginx.exe"
set "NGINX_CONF=%NGINX_ROOT%\conf\nginx.conf"
set "NGINX_PORT=80"

set "CLOUDFLARED_ROOT=C:\Cloudflared"
set "CLOUDFLARED_EXE=%CLOUDFLARED_ROOT%\cloudflared.exe"
set "CLOUDFLARED_TOKEN_DIR=C:\ProgramData\cloudflared"
set "CLOUDFLARED_TOKEN_FILE=%CLOUDFLARED_TOKEN_DIR%\token"
set "CLOUDFLARE_TUNNEL_NAME=codename399-tunnel"
set "CLOUDFLARE_TUNNEL_ID=494bb29f-fb55-4c2a-ae32-27edef6431dd"
set "CLOUDFLARED_DOWNLOAD=https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe"

set "PUBLIC_UI=https://codename399.com"
set "PUBLIC_API=https://api.codename399.com"

set "BACKUP_PATH=%SERVER_ROOT%\_backups"
set "SETUP_LOG=%TEMP%\CodeName399-ServerSetup.log"

REM ============================================================================
REM PROJECT-SPECIFIC FLAGS
REM Keep ALL FALSE unless the corresponding operation is intentionally required.
REM ============================================================================

set "API_BUILD=false"
set "API_DEPLOY=false"

set "GATEWAY_DEPLOY=false"
set "AUTH_DEPLOY=false"
set "GAMESTASH_DEPLOY=false"
set "DEBTMANAGER_DEPLOY=false"
set "KITE_DEPLOY=false"

set "UI_BUILD=false"
set "UI_DEPLOY=false"

set "START_SERVICES=true"
set "INSTALL_NGINX=true"
set "CONFIGURE_CLOUDFLARE=true"

REM ============================================================================
REM BOOT / ADMIN
REM ============================================================================

fltmc >nul 2>&1
if errorlevel 1 (
    echo [ERROR] This setup must be run as Administrator.
    echo Right-click the launcher and select "Run as administrator".
    pause
    exit /b 1
)

> "%SETUP_LOG%" echo ============================================================
>>"%SETUP_LOG%" echo CodeName399 Windows Server Setup
>>"%SETUP_LOG%" echo Started: %DATE% %TIME%
>>"%SETUP_LOG%" echo ============================================================

echo.
echo ============================================================
echo CodeName399 Windows Server Setup
echo ============================================================
echo.
echo Running as Administrator.
echo.
echo [BOOT] Administrator check passed.
echo [BOOT] Setup log: %SETUP_LOG%
echo.

echo ============================================================================
echo CodeName399 Windows Server Setup - Cloudflare Tunnel Rotation
echo ============================================================================
echo.
echo Source root : %SOURCE_ROOT%
echo API source  : %API_PATH%
echo UI source   : %ANGULAR_PATH%
echo Server root : %SERVER_ROOT%
echo UI          : %PUBLIC_UI%
echo API         : %PUBLIC_API%
echo Tunnel      : %CLOUDFLARE_TUNNEL_NAME%
echo Tunnel ID   : %CLOUDFLARE_TUNNEL_ID%
echo.
echo Cloudflare credentials are not printed to the console.
echo.
echo IMPORTANT: This window will remain open after success or failure.
echo If setup stops unexpectedly, check:
echo %SETUP_LOG%
echo.

REM ============================================================================
REM PRECHECK
REM ============================================================================

echo [PRECHECK] Verifying exact source paths...

if not exist "%SOURCE_ROOT%" (
    echo [ERROR] Source root not found: %SOURCE_ROOT%
    goto :SetupFail
)

if not exist "%API_PATH%" (
    echo [ERROR] API source not found: %API_PATH%
    goto :SetupFail
)

if not exist "%ANGULAR_PATH%" (
    echo [ERROR] Angular source not found: %ANGULAR_PATH%
    goto :SetupFail
)

if not exist "%SLN_PATH%" (
    echo [ERROR] API solution not found: %SLN_PATH%
    goto :SetupFail
)

if not exist "%ANGULAR_PATH%\package.json" (
    echo [ERROR] package.json not found: %ANGULAR_PATH%\package.json
    goto :SetupFail
)

echo [OK] Source validation passed.

REM ============================================================================
REM 1 - DIRECTORIES
REM ============================================================================

echo.
echo [1/11] Creating server directories...

call :EnsureDir "%SERVER_ROOT%"
if errorlevel 1 goto :SetupFail

call :EnsureDir "%UI_PATH%"
if errorlevel 1 goto :SetupFail

call :EnsureDir "%GATEWAY_PATH%"
if errorlevel 1 goto :SetupFail

call :EnsureDir "%AUTH_PATH%"
if errorlevel 1 goto :SetupFail

call :EnsureDir "%GAMESTASH_PATH%"
if errorlevel 1 goto :SetupFail

call :EnsureDir "%DEBTMANAGER_PATH%"
if errorlevel 1 goto :SetupFail

call :EnsureDir "%KITE_PATH%"
if errorlevel 1 goto :SetupFail

REM ============================================================================
REM 2 - DOTNET
REM ============================================================================

echo.
echo [2/11] Checking .NET...

dotnet --version
if errorlevel 1 (
    echo [ERROR] .NET SDK is not available.
    goto :SetupFail
)

REM ============================================================================
REM 3 - NODE
REM ============================================================================

echo.
echo [3/11] Checking Node.js...

node --version
if errorlevel 1 (
    echo [ERROR] Node.js is not available.
    goto :SetupFail
)

npm --version
if errorlevel 1 (
    echo [ERROR] npm is not available.
    goto :SetupFail
)

REM ============================================================================
REM 4 - MONGODB
REM ============================================================================

echo.
echo [4/11] MongoDB setup skipped.
echo [OK] Existing MongoDB installation and configuration will not be changed.

REM ============================================================================
REM 5 - BACKUP
REM ============================================================================

echo.
echo [5/11] Backing up existing server configuration...

set "CONFIG_BACKUP=%BACKUP_PATH%\BeforeSetup"
call :EnsureDir "%CONFIG_BACKUP%"
if errorlevel 1 goto :SetupFail

call :BackupIfExists "%GATEWAY_PATH%\appsettings.Production.json" "%CONFIG_BACKUP%\Gateway-appsettings.Production.json"
call :BackupIfExists "%AUTH_PATH%\appsettings.Production.json" "%CONFIG_BACKUP%\Auth-appsettings.Production.json"
call :BackupIfExists "%GAMESTASH_PATH%\appsettings.Production.json" "%CONFIG_BACKUP%\GameStash-appsettings.Production.json"
call :BackupIfExists "%DEBTMANAGER_PATH%\appsettings.Production.json" "%CONFIG_BACKUP%\DebtManager-appsettings.Production.json"
call :BackupIfExists "%KITE_PATH%\appsettings.Production.json" "%CONFIG_BACKUP%\Kite-appsettings.Production.json"

REM ============================================================================
REM 6 - API BUILD
REM ============================================================================

echo.
echo [6/11] API build...

if /I "%API_BUILD%"=="true" (
    echo [BUILD] Restoring solution...
    dotnet restore "%SLN_PATH%"
    if errorlevel 1 goto :SetupFail

    echo [BUILD] Building Release...
    dotnet build "%SLN_PATH%" -c Release --no-restore
    if errorlevel 1 goto :SetupFail

    echo [OK] API build completed.
) else (
    echo [SKIP] API build disabled. API_BUILD=false
)

REM ============================================================================
REM 7 - API DEPLOYMENT
REM ============================================================================

echo.
echo [7/11] API deployment...

if /I "%API_DEPLOY%"=="true" (
    if /I "%GATEWAY_DEPLOY%"=="true" (
        call :DeployProject "%API_PATH%\CodeName399.Gateway\CodeName399.Gateway.csproj" "%GATEWAY_PATH%" "%GATEWAY_SERVICE%" "%GATEWAY_PORT%"
        if errorlevel 1 goto :SetupFail
    )

    if /I "%AUTH_DEPLOY%"=="true" (
        call :DeployProject "%API_PATH%\CodeName399.Auth.API\CodeName399.Auth.API.csproj" "%AUTH_PATH%" "%AUTH_SERVICE%" "%AUTH_PORT%"
        if errorlevel 1 goto :SetupFail
    )

    if /I "%GAMESTASH_DEPLOY%"=="true" (
        call :DeployProject "%API_PATH%\CodeName399.GameStash.API\CodeName399.GameStash.API.csproj" "%GAMESTASH_PATH%" "%GAMESTASH_SERVICE%" "%GAMESTASH_PORT%"
        if errorlevel 1 goto :SetupFail
    )

    if /I "%DEBTMANAGER_DEPLOY%"=="true" (
        call :DeployProject "%API_PATH%\CodeName399.DebtManager.API\CodeName399.DebtManager.API.csproj" "%DEBTMANAGER_PATH%" "%DEBTMANAGER_SERVICE%" "%DEBTMANAGER_PORT%"
        if errorlevel 1 goto :SetupFail
    )

    if /I "%KITE_DEPLOY%"=="true" (
        call :DeployProject "%API_PATH%\CodeName399.Kite.API\CodeName399.Kite.API.csproj" "%KITE_PATH%" "%KITE_SERVICE%" "%KITE_PORT%"
        if errorlevel 1 goto :SetupFail
    )
) else (
    echo [SKIP] API deployment disabled. API_DEPLOY=false
)

REM ============================================================================
REM 8 - UI BUILD / DEPLOY
REM ============================================================================

echo.
echo [8/11] UI build/deployment...

if /I "%UI_BUILD%"=="true" (
    pushd "%ANGULAR_PATH%"

    if exist package-lock.json (
        call npm ci
        if errorlevel 1 (
            popd
            goto :SetupFail
        )
    ) else (
        call npm install
        if errorlevel 1 (
            popd
            goto :SetupFail
        )
    )

    call npm run build -- --configuration=production
    if errorlevel 1 (
        popd
        goto :SetupFail
    )

    popd
    echo [OK] UI build completed.
) else (
    echo [SKIP] UI build disabled. UI_BUILD=false
)

if /I "%UI_DEPLOY%"=="true" (
    call :DeployUI
    if errorlevel 1 goto :SetupFail
) else (
    echo [SKIP] UI deployment disabled. UI_DEPLOY=false
)

REM ============================================================================
REM 9 - SERVICES
REM ============================================================================

echo.
echo [9/11] Starting CodeName399 services before Nginx validation...

if /I "%START_SERVICES%"=="true" (
    call :EnsureServicePort "%GATEWAY_SERVICE%" "%GATEWAY_PORT%"
    if errorlevel 1 goto :SetupFail

    call :EnsureServicePort "%AUTH_SERVICE%" "%AUTH_PORT%"
    if errorlevel 1 goto :SetupFail

    call :EnsureServicePort "%GAMESTASH_SERVICE%" "%GAMESTASH_PORT%"
    if errorlevel 1 goto :SetupFail

    call :EnsureServicePort "%DEBTMANAGER_SERVICE%" "%DEBTMANAGER_PORT%"
    if errorlevel 1 goto :SetupFail

    call :EnsureServicePort "%KITE_SERVICE%" "%KITE_PORT%"
    if errorlevel 1 goto :SetupFail

    echo [OK] All CodeName399 services are running and listening on expected ports.
) else (
    echo [SKIP] Service start disabled.
)

echo [CHECKPOINT] All service checks completed. Entering Nginx configuration.

REM ============================================================================
REM 10 - NGINX
REM ============================================================================

echo.
echo [10/11] Installing/configuring Nginx...

if /I "%INSTALL_NGINX%"=="true" (
    call :ConfigureNginx
    if errorlevel 1 goto :SetupFail
) else (
    echo [SKIP] Nginx installation/configuration disabled.
)

REM ============================================================================
REM 11 - CLOUDFLARE
REM ============================================================================

echo.
echo [11/11] Installing/configuring Cloudflare Tunnel...

if /I "%CONFIGURE_CLOUDFLARE%"=="true" (
    call :ConfigureCloudflare
    if errorlevel 1 goto :SetupFail
) else (
    echo [SKIP] Cloudflare configuration disabled.
)

REM ============================================================================
REM FINAL CHECKS
REM ============================================================================

echo.
echo ------------------------------------------------------------
echo FINAL SERVICE CHECK
echo ------------------------------------------------------------

call :ShowServiceState "%GATEWAY_SERVICE%"
call :ShowServiceState "%AUTH_SERVICE%"
call :ShowServiceState "%GAMESTASH_SERVICE%"
call :ShowServiceState "%DEBTMANAGER_SERVICE%"
call :ShowServiceState "%KITE_SERVICE%"

echo.
echo ------------------------------------------------------------
echo FINAL NGINX CHECK
echo ------------------------------------------------------------

call :CheckPort "%NGINX_PORT%"
if errorlevel 1 (
    echo [WARNING] Nginx is not listening on TCP %NGINX_PORT%.
) else (
    echo [OK] Nginx is listening on TCP %NGINX_PORT%.
)

echo.
echo ------------------------------------------------------------
echo FINAL CLOUDFLARE CHECK
echo ------------------------------------------------------------

sc.exe query cloudflared >nul 2>&1
if errorlevel 1 (
    echo [WARNING] cloudflared service is not installed.
) else (
    sc.exe query cloudflared | findstr /I "STATE"
)

echo.
echo ============================================================================
echo FINAL SETUP SUMMARY
echo ============================================================================
echo Server root : %SERVER_ROOT%
echo UI          : %PUBLIC_UI%
echo API         : %PUBLIC_API%
echo Cloudflare  : %CLOUDFLARE_TUNNEL_NAME%
echo Tunnel ID   : %CLOUDFLARE_TUNNEL_ID%
echo.
echo Build/deployment flags:
echo   API_BUILD          : %API_BUILD%
echo   API_DEPLOY         : %API_DEPLOY%
echo   GATEWAY_DEPLOY     : %GATEWAY_DEPLOY%
echo   AUTH_DEPLOY        : %AUTH_DEPLOY%
echo   GAMESTASH_DEPLOY   : %GAMESTASH_DEPLOY%
echo   DEBTMANAGER_DEPLOY : %DEBTMANAGER_DEPLOY%
echo   KITE_DEPLOY        : %KITE_DEPLOY%
echo   UI_BUILD           : %UI_BUILD%
echo   UI_DEPLOY         : %UI_DEPLOY%
echo.
echo Local API ports:
echo   Gateway     : %GATEWAY_PORT%
echo   Auth        : %AUTH_PORT%
echo   GameStash   : %GAMESTASH_PORT%
echo   DebtManager : %DEBTMANAGER_PORT%
echo   Kite        : %KITE_PORT%
echo.
echo Nginx:
echo   %NGINX_EXE%
echo.
echo Cloudflare:
echo   %CLOUDFLARE_TUNNEL_NAME%
echo.
echo ============================================================================
echo SETUP COMPLETE - ALL REQUIRED LOCAL CHECKS PASSED
echo ============================================================================
echo.
echo Log file:
echo %SETUP_LOG%
echo.

goto :MainEnd

REM ============================================================================
REM SUBROUTINES
REM ============================================================================

:EnsureDir
if not exist "%~1" mkdir "%~1" >nul 2>&1
if not exist "%~1" exit /b 1
exit /b 0

:BackupIfExists
if exist "%~1" (
    copy /Y "%~1" "%~2" >nul 2>&1
)
exit /b 0

:CheckPort
powershell -NoProfile -ExecutionPolicy Bypass -Command "$c=Get-NetTCPConnection -LocalPort %~1 -State Listen -ErrorAction SilentlyContinue; if($c){exit 0}else{exit 1}" >nul 2>&1
exit /b %ERRORLEVEL%

:EnsureServicePort
set "CHECK_SERVICE=%~1"
set "CHECK_PORT=%~2"
set "CHECK_FOUND="

echo        Checking %CHECK_SERVICE% on TCP %CHECK_PORT%...

sc.exe query "%CHECK_SERVICE%" >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Service does not exist: %CHECK_SERVICE%
    exit /b 1
)

sc.exe query "%CHECK_SERVICE%" | findstr /I "RUNNING" >nul 2>&1
if errorlevel 1 (
    echo        Starting %CHECK_SERVICE%...
    sc.exe start "%CHECK_SERVICE%" >nul 2>&1
)

REM Do not use GOTO from inside this FOR block.
REM This avoids the batch parser/label-loop problem seen in earlier versions.
for /L %%N in (1,1,20) do (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$c=Get-NetTCPConnection -LocalPort %CHECK_PORT% -State Listen -ErrorAction SilentlyContinue; if($c){exit 0}else{exit 1}" >nul 2>&1
    if not errorlevel 1 set "CHECK_FOUND=1"
    if not defined CHECK_FOUND timeout /t 1 /nobreak >nul
)

if not defined CHECK_FOUND (
    echo [ERROR] %CHECK_SERVICE% did not listen on TCP %CHECK_PORT%.
    sc.exe query "%CHECK_SERVICE%"
    exit /b 1
)

echo [OK] %CHECK_SERVICE% is listening on TCP %CHECK_PORT%.
exit /b 0

:DeployProject
set "DEPLOY_CSPROJ=%~1"
set "DEPLOY_PATH=%~2"
set "DEPLOY_SERVICE=%~3"
set "DEPLOY_PORT=%~4"

echo        Deploying %DEPLOY_SERVICE%...

call :StopService "%DEPLOY_SERVICE%"
if errorlevel 1 exit /b 1

call :EnsureDir "%DEPLOY_PATH%"
if errorlevel 1 exit /b 1

dotnet publish "%DEPLOY_CSPROJ%" -c Release -o "%DEPLOY_PATH%" --no-restore
if errorlevel 1 exit /b 1

call :EnsureWindowsService "%DEPLOY_SERVICE%" "%DEPLOY_PATH%"
if errorlevel 1 exit /b 1

call :EnsureServicePort "%DEPLOY_SERVICE%" "%DEPLOY_PORT%"
if errorlevel 1 exit /b 1

echo [OK] %DEPLOY_SERVICE% deployed.
exit /b 0

:StopService
sc.exe query "%~1" >nul 2>&1
if errorlevel 1 exit /b 0

sc.exe stop "%~1" >nul 2>&1
timeout /t 2 /nobreak >nul
exit /b 0

:EnsureWindowsService
set "SVC_NAME=%~1"
set "SVC_PATH=%~2"
set "SVC_EXE="

for %%F in ("%SVC_PATH%\*.exe") do (
    if not defined SVC_EXE set "SVC_EXE=%%~fF"
)

if not defined SVC_EXE (
    echo [ERROR] No executable found in %SVC_PATH%.
    exit /b 1
)

sc.exe query "%SVC_NAME%" >nul 2>&1
if errorlevel 1 (
    sc.exe create "%SVC_NAME%" binPath= "\"%SVC_EXE%\"" start= auto DisplayName= "%SVC_NAME%" >nul
    if errorlevel 1 exit /b 1
) else (
    sc.exe config "%SVC_NAME%" start= auto binPath= "\"%SVC_EXE%\"" >nul
    if errorlevel 1 exit /b 1
)

sc.exe failure "%SVC_NAME%" reset= 86400 actions= restart/5000/restart/10000/restart/30000 >nul 2>&1
sc.exe start "%SVC_NAME%" >nul 2>&1

exit /b 0

:DeployUI
set "UI_SOURCE="

if exist "%ANGULAR_PATH%\dist\CodeName399\browser\index.html" set "UI_SOURCE=%ANGULAR_PATH%\dist\CodeName399\browser"
if not defined UI_SOURCE if exist "%ANGULAR_PATH%\dist\browser\index.html" set "UI_SOURCE=%ANGULAR_PATH%\dist\browser"

if not defined UI_SOURCE (
    for /d %%D in ("%ANGULAR_PATH%\dist\*") do (
        if not defined UI_SOURCE if exist "%%~D\browser\index.html" set "UI_SOURCE=%%~D\browser"
    )
)

if not defined UI_SOURCE (
    echo [ERROR] Angular browser output was not found.
    exit /b 1
)

call :EnsureDir "%UI_PATH%"
if errorlevel 1 exit /b 1

robocopy "%UI_SOURCE%" "%UI_PATH%" /MIR /R:2 /W:2
if errorlevel 8 exit /b 1

echo [OK] Angular UI deployed.
exit /b 0

:ConfigureNginx
if not exist "%NGINX_EXE%" (
    echo        Nginx executable not found. Installing Nginx...
    call :InstallNginxFiles
    if errorlevel 1 exit /b 1
)

if not exist "%NGINX_ROOT%\conf\mime.types" (
    echo        Nginx mime.types missing. Reinstalling Nginx files...
    call :InstallNginxFiles
    if errorlevel 1 exit /b 1
)

if not exist "%NGINX_ROOT%\logs" mkdir "%NGINX_ROOT%\logs" >nul 2>&1

echo        Writing Nginx configuration...
call :WriteNginxConfig
if errorlevel 1 exit /b 1

echo        Testing Nginx configuration...
pushd "%NGINX_ROOT%"
"%NGINX_EXE%" -t -p "C:/nginx/" -c "conf/nginx.conf"
set "NGINX_TEST_RC=!ERRORLEVEL!"
popd

if not "!NGINX_TEST_RC!"=="0" (
    echo [ERROR] Nginx configuration test failed.
    if exist "%NGINX_ROOT%\logs\error.log" type "%NGINX_ROOT%\logs\error.log"
    exit /b 1
)

echo [OK] Nginx configuration is valid.

echo        Stopping any existing Nginx instance...
taskkill /F /IM nginx.exe >nul 2>&1
timeout /t 2 /nobreak >nul

echo        Starting Nginx...
pushd "%NGINX_ROOT%"
start "" /b "%NGINX_EXE%" -p "C:/nginx/" -c "conf/nginx.conf"
popd
timeout /t 3 /nobreak >nul

netsh advfirewall firewall add rule name="CodeName399 Nginx HTTP" dir=in action=allow protocol=TCP localport=80 >nul 2>&1

echo        Checking Nginx listener...
call :CheckPort 80
if errorlevel 1 (
    echo [ERROR] Nginx did not start/listen on TCP 80.
    if exist "%NGINX_ROOT%\logs\error.log" type "%NGINX_ROOT%\logs\error.log"
    exit /b 1
)

echo [OK] Nginx is listening on TCP 80.

echo        Testing UI host routing...
curl.exe --noproxy "*" --connect-timeout 3 --max-time 5 -sS -o "%TEMP%\CodeName399-ui-test.txt" -w "%%{http_code}" -H "Host: codename399.com" http://127.0.0.1/ >"%TEMP%\CodeName399-ui-status.txt" 2>"%TEMP%\CodeName399-ui-error.txt"

set /p "UI_STATUS="<"%TEMP%\CodeName399-ui-status.txt"

if not "!UI_STATUS!"=="200" (
    echo [ERROR] UI routing returned HTTP !UI_STATUS!.
    type "%TEMP%\CodeName399-ui-error.txt"
    exit /b 1
)

echo [OK] UI host routing is working.

echo        Testing Gateway TCP connection...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$c=Get-NetTCPConnection -LocalPort %GATEWAY_PORT% -State Listen -ErrorAction SilentlyContinue; if($c){exit 0}else{exit 1}" >nul 2>&1

if errorlevel 1 (
    echo [ERROR] Gateway is not listening on TCP %GATEWAY_PORT%.
    exit /b 1
)

echo [OK] Gateway is listening on TCP %GATEWAY_PORT%.

echo        Testing API host routing...
curl.exe --noproxy "*" --connect-timeout 3 --max-time 5 -sS -D "%TEMP%\CodeName399-api-headers.txt" -o "%TEMP%\CodeName399-api-body.txt" -H "Host: api.codename399.com" http://127.0.0.1/__codename399_api_route_test__ >nul 2>"%TEMP%\CodeName399-api-error.txt"

if errorlevel 1 (
    echo [ERROR] API host routing request failed.
    type "%TEMP%\CodeName399-api-error.txt"
    exit /b 1
)

findstr /I /C:"X-CodeName399-Route: gateway" "%TEMP%\CodeName399-api-headers.txt" >nul 2>&1

if errorlevel 1 (
    echo [ERROR] API host did not reach the Gateway proxy.
    echo Response headers:
    type "%TEMP%\CodeName399-api-headers.txt"
    echo Response body:
    type "%TEMP%\CodeName399-api-body.txt"
    exit /b 1
)

echo [OK] API host routing reaches the Gateway.
exit /b 0

:InstallNginxFiles
set "NGINX_ZIP=%TEMP%\nginx-1.30.4.zip"
set "NGINX_EXTRACT=%TEMP%\CodeName399-nginx-extract"

echo        Installing Nginx files...

taskkill /F /IM nginx.exe >nul 2>&1

if exist "%NGINX_ZIP%" del /q "%NGINX_ZIP%" >nul 2>&1
if exist "%NGINX_EXTRACT%" rmdir /s /q "%NGINX_EXTRACT%" >nul 2>&1

powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -UseBasicParsing -Uri 'https://nginx.org/download/nginx-1.30.4.zip' -OutFile '%NGINX_ZIP%'"
if errorlevel 1 exit /b 1

mkdir "%NGINX_EXTRACT%" >nul 2>&1

powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%NGINX_ZIP%' -DestinationPath '%NGINX_EXTRACT%' -Force"
if errorlevel 1 exit /b 1

if exist "%NGINX_ROOT%" rmdir /s /q "%NGINX_ROOT%" >nul 2>&1

mkdir "%NGINX_ROOT%" >nul 2>&1

for /d %%D in ("%NGINX_EXTRACT%\nginx-*") do (
    robocopy "%%~D" "%NGINX_ROOT%" /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP >nul
)

if not exist "%NGINX_ROOT%\nginx.exe" exit /b 1
if not exist "%NGINX_ROOT%\conf\mime.types" exit /b 1

echo [OK] Nginx files installed.
exit /b 0

:WriteNginxConfig
(
echo worker_processes 1;
echo error_log logs/error.log warn;
echo pid logs/nginx.pid;
echo events {
echo     worker_connections 1024;
echo }
echo http {
echo     include mime.types;
echo     default_type application/octet-stream;
echo     access_log logs/access.log;
echo     sendfile on;
echo     keepalive_timeout 65;
echo.
echo     server {
echo         listen 80;
echo         server_name codename399.com www.codename399.com;
echo         root C:/Servers/CodeName399/UI;
echo         index index.html;
echo.
echo         location / {
echo             try_files $uri $uri/ /index.html;
echo         }
echo     }
echo.
echo     server {
echo         listen 80;
echo         server_name api.codename399.com;
echo.
echo         location / {
echo             proxy_pass http://127.0.0.1:5000;
echo             proxy_http_version 1.1;
echo             proxy_set_header Host $host;
echo             proxy_set_header X-Real-IP $remote_addr;
echo             proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
echo             proxy_set_header X-Forwarded-Proto $scheme;
echo             proxy_set_header Upgrade $http_upgrade;
echo             proxy_set_header Connection "upgrade";
echo             add_header X-CodeName399-Route "gateway" always;
echo             proxy_connect_timeout 5;
echo             proxy_read_timeout 300;
echo             proxy_send_timeout 300;
echo         }
echo     }
echo }
) >"%NGINX_CONF%"

if not exist "%NGINX_CONF%" exit /b 1

(
echo @echo off
echo cd /d C:\nginx
echo C:\nginx\nginx.exe -p C:\nginx\ -c conf\nginx.conf
) >"%NGINX_ROOT%\start-nginx.bat"

exit /b 0

:ConfigureCloudflare
if not exist "%CLOUDFLARED_ROOT%" mkdir "%CLOUDFLARED_ROOT%" >nul 2>&1
if not exist "%CLOUDFLARED_TOKEN_DIR%" mkdir "%CLOUDFLARED_TOKEN_DIR%" >nul 2>&1

if not exist "%CLOUDFLARED_EXE%" (
    echo        Downloading cloudflared...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -UseBasicParsing -Uri '%CLOUDFLARED_DOWNLOAD%' -OutFile '%CLOUDFLARED_EXE%'"
    if errorlevel 1 (
        echo [ERROR] Failed to download cloudflared.
        exit /b 1
    )
)

REM --------------------------------------------------------------------------
REM Token source:
REM   1. Existing protected token file, if present.
REM   2. Environment variable CLOUDFLARE_TUNNEL_TOKEN.
REM
REM Do NOT place the token directly in this batch file.
REM --------------------------------------------------------------------------

if exist "%CLOUDFLARED_TOKEN_FILE%" (
    for %%A in ("%CLOUDFLARED_TOKEN_FILE%") do if %%~zA GTR 0 (
        echo [OK] Existing Cloudflare token file found.
        goto :CloudflareTokenReady
    )
)

if not defined CLOUDFLARE_TUNNEL_TOKEN (
    echo [ERROR] Cloudflare tunnel token is not available.
    echo.
    echo Set the token before running this script:
    echo.
    echo   setx CLOUDFLARE_TUNNEL_TOKEN "YOUR_NEW_TUNNEL_TOKEN"
    echo.
    echo Then open a NEW Administrator CMD window and run the setup again.
    exit /b 1
)

> "%CLOUDFLARED_TOKEN_FILE%" echo %CLOUDFLARE_TUNNEL_TOKEN%

:CloudflareTokenReady

icacls "%CLOUDFLARED_TOKEN_FILE%" /inheritance:r /grant:r "SYSTEM:(F)" "Administrators:(F)" >nul
if errorlevel 1 (
    echo [ERROR] Failed to secure Cloudflare token file.
    exit /b 1
)

echo        Recreating cloudflared Windows service...

sc.exe stop cloudflared >nul 2>&1
timeout /t 2 /nobreak >nul

sc.exe delete cloudflared >nul 2>&1
timeout /t 2 /nobreak >nul

sc.exe create cloudflared binPath= "\"%CLOUDFLARED_EXE%\" tunnel run --token-file \"%CLOUDFLARED_TOKEN_FILE%\"" start= auto DisplayName= "Cloudflared agent" >nul

if errorlevel 1 (
    echo [ERROR] Failed to create cloudflared Windows service.
    exit /b 1
)

sc.exe description cloudflared "CodeName399 Cloudflare Tunnel - %CLOUDFLARE_TUNNEL_NAME%" >nul 2>&1
sc.exe failure cloudflared reset= 86400 actions= restart/5000/restart/10000/restart/30000 >nul 2>&1

echo        Starting cloudflared...
sc.exe start cloudflared >nul 2>&1

if errorlevel 1 (
    echo [ERROR] cloudflared service failed to start.
    sc.exe query cloudflared
    exit /b 1
)

set "CF_RUNNING="

for /L %%N in (1,1,15) do (
    sc.exe query cloudflared | findstr /I "RUNNING" >nul 2>&1
    if not errorlevel 1 set "CF_RUNNING=1"
    if not defined CF_RUNNING timeout /t 1 /nobreak >nul
)

if not defined CF_RUNNING (
    echo [ERROR] cloudflared did not reach RUNNING state.
    sc.exe query cloudflared
    exit /b 1
)

echo [OK] cloudflared Windows service is RUNNING.
echo        Tunnel: %CLOUDFLARE_TUNNEL_NAME%
echo        Tunnel ID: %CLOUDFLARE_TUNNEL_ID%
exit /b 0

:ShowServiceState
sc.exe query "%~1" | findstr /I "SERVICE_NAME STATE"
exit /b 0

:SetupFail
echo.
echo ============================================================================
echo SETUP FAILED
echo ============================================================================
echo Review the error above.
echo.
echo Log file:
echo %SETUP_LOG%
echo.
>>"%SETUP_LOG%" echo SETUP FAILED %DATE% %TIME%
pause
exit /b 1

:MainEnd
echo.
echo Setup finished. This window will remain open.
echo Press any key to close.
pause >nul
endlocal
exit /b 0
