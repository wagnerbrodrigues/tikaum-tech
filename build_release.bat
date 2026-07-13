@echo off
:: Roda na maquina do DESENVOLVEDOR: verifica o SDK, publica o TikaumTech
:: para Windows x64 e prepara a pasta de distribuicao publish\.
:: Executar a partir da raiz do repositorio.
:: (install.bat e update.bat rodam na maquina do estudio e nao compilam nada.)

setlocal
cd /d "%~dp0"

:: ---- Verificar .NET 9 SDK --------------------------------------------
:: O SDK e necessario apenas AQUI, para compilar — o publish e
:: self-contained e a maquina do estudio nao precisa de .NET instalado.
:: (Logica movida do install.bat em 2026-07-11: instalacao de SDK nao
:: pertence ao instalador do estudio.)
echo Verificando .NET 9 SDK...
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo .NET 9 nao encontrado. Baixando e instalando...
    echo ^(a instalacao em "C:\Program Files" pode exigir Administrador^)
    powershell -Command "Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile '%TEMP%\dotnet-install.ps1'"
    powershell -ExecutionPolicy Bypass -File "%TEMP%\dotnet-install.ps1" -Channel 9.0 -InstallDir "C:\Program Files\dotnet"
    del "%TEMP%\dotnet-install.ps1"
    setx PATH "%PATH%;C:\Program Files\dotnet" /M
    set "PATH=%PATH%;C:\Program Files\dotnet"
    echo .NET 9 SDK instalado. Continuando...
)

if not exist "src\TikaumTech\TikaumTech.csproj" (
    echo ERRO: src\TikaumTech\TikaumTech.csproj nao encontrado.
    echo Execute build_release.bat a partir da raiz do repositorio clonado
    echo completo ^(nao de uma copia isolada deste arquivo^).
    pause
    exit /b 1
)

:: Limpa a saida anterior: dotnet publish nao remove arquivos orfaos de
:: builds antigos, e distribuir uma publish\ com residuos de varias versoes
:: misturadas gera comportamento imprevisivel na instalacao.
if exist "publish" rd /s /q "publish"

echo [1/2] Publicando aplicacao (Release, win-x64, self-contained)...
dotnet publish src\TikaumTech -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=false ^
    -o publish\
if %errorlevel% neq 0 (
    echo ERRO: falha no dotnet publish.
    pause
    exit /b 1
)

echo [2/2] Copiando instalador e atualizador para a pasta de distribuicao...
copy /Y install.bat publish\install.bat >nul
copy /Y update.bat publish\update.bat >nul

echo.
echo =====================================================
echo  Build concluido. Distribua a pasta publish\ para a
echo  maquina do estudio.
echo.
echo  Na maquina do estudio (como Administrador):
echo    - Primeira instalacao:  publish\install.bat
echo    - Atualizacao:          publish\update.bat
echo      (preserva banco de dados e configuracoes)
echo =====================================================
echo.
pause
