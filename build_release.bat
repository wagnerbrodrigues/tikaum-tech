@echo off
:: Publica o TikaumTech para Windows x64 e prepara a pasta de distribuicao.
:: Executar a partir da raiz do repositorio.

setlocal
cd /d "%~dp0"

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
dotnet publish src\TikaumTech -c Release -r win-x64 --self-contained ^
    -p:PublishSingleFile=false ^
    -o publish\
if %errorlevel% neq 0 (
    echo ERRO: falha no dotnet publish.
    pause
    exit /b 1
)

echo [2/2] Copiando instalador para pasta de distribuicao...
copy /Y install.bat publish\install.bat >nul

echo.
echo =====================================================
echo  Concluido!
echo  Pasta de distribuicao: publish\
echo.
echo  Para instalar no computador do estudio:
echo    1. Copie a pasta publish\ para o destino
echo       (pen drive, compartilhamento de rede, etc.)
echo    2. Execute publish\install.bat como Administrador
echo =====================================================
echo.
pause
