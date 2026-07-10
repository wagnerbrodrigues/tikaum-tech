@echo off
cd /d %~dp0

echo Compilando...
dotnet build ImportClientes.csproj -c Release -o bin\Release --nologo -v quiet
if errorlevel 1 (
    echo ERRO: falha na compilacao
    pause
    exit /b 1
)

echo.
echo Importando...
dotnet run --project ImportClientes.csproj -c Release --no-build
