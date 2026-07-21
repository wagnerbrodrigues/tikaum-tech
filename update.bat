@echo off
:: ATUALIZACAO de uma instalacao existente na maquina do ESTUDIO,
:: preservando todos os dados (data\ e config\ nunca sao tocadas).
:: Nao compila nada: usa o publish\ ja gerado pelo build_release.bat.
:: Para a PRIMEIRA instalacao, use o install.bat.
setlocal enabledelayedexpansion
chcp 65001 >nul 2>&1

echo.
echo  ==========================================
echo   TikaumTech ^| Atualizador v1.0
echo  ==========================================
echo.

:: Verificar privilegios de administrador (parar tarefa/processo e
:: escrever em C:\TikaumTech exigem elevacao)
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo  ERRO: Execute este arquivo como Administrador.
    echo  Clique com botao direito ^> "Executar como administrador".
    echo.
    pause & exit /b 1
)

set "DEST=C:\TikaumTech"

:: ---- [1/5] Verificar instalacao e origem -----------------------------
echo  [1/5] Verificando instalacao existente...
if not exist "%DEST%\TikaumTech.exe" (
    echo  ERRO: Instalacao nao encontrada em %DEST%.
    echo  Execute install.bat para uma nova instalacao.
    pause & exit /b 1
)

:: Origem: a pasta deste script deve ser o publish\ novo (mesma logica do
:: install.bat — aceita tambem rodar da raiz do repo com publish\ ao lado)
set "SRC=%~dp0"
if not exist "%SRC%TikaumTech.exe" (
    if exist "%~dp0publish\TikaumTech.exe" (
        set "SRC=%~dp0publish\"
    ) else (
        echo  ERRO: TikaumTech.exe nao encontrado ao lado deste update.bat.
        echo  Rode build_release.bat primeiro, na raiz do repositorio
        echo  ^(gera a pasta publish\ com o .exe e uma copia deste script^),
        echo  e execute o update.bat de dentro da pasta publish\ gerada.
        pause & exit /b 1
    )
)

:: ---- [2/5] Parar o TikaumTech ----------------------------------------
echo  [2/5] Parando o TikaumTech...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
 "Stop-ScheduledTask -TaskName 'TikaumTech' -ErrorAction SilentlyContinue" >nul 2>&1
taskkill /F /IM TikaumTech.exe /T >nul 2>&1
:: Espera o Windows soltar o handle do .exe apos encerrar o processo
timeout /t 3 /nobreak >nul

:: ---- [3/5] Backup do banco ANTES de qualquer copia -------------------
echo  [3/5] Fazendo backup do banco de dados...
for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd"') do set "DATA=%%i"
set "BACKUP_PRE_UPDATE="
if exist "%DEST%\data\tikaum.db" (
    set "BACKUP_PRE_UPDATE=%DEST%\data\tikaum_pre_update_%DATA%.db"
    copy /Y "%DEST%\data\tikaum.db" "!BACKUP_PRE_UPDATE!" >nul
    if errorlevel 1 (
        echo  ERRO: nao foi possivel criar o backup do banco.
        echo  Atualizacao abortada — nada foi alterado.
        pause & exit /b 1
    )
    echo  Backup do banco criado: tikaum_pre_update_%DATA%.db
) else (
    echo  AVISO: banco de dados nao encontrado em %DEST%\data\ — seguindo
    echo  sem backup ^(instalacao nunca usada?^).
)

:: ---- [4/5] Copiar arquivos novos --------------------------------------
:: /XD data config: banco, backups locais, credenciais/token do Google
:: Drive e chaves de criptografia ficam intactos. /XF: os instaladores nao
:: fazem parte da instalacao. /R:3 /W:2 evita o retry infinito padrao do
:: robocopy se algum arquivo ainda estiver travado. Codigos 0-7 = sucesso.
echo  [4/5] Copiando arquivos novos...
robocopy "%SRC%." "%DEST%" /E /XD data config /XF install.bat update.bat /R:3 /W:2 >nul
if errorlevel 8 (
    echo  ERRO: falha ao copiar arquivos ^(robocopy^). Verifique se o
    echo  TikaumTech.exe foi realmente encerrado e tente novamente.
    pause & exit /b 1
)

:: Verificacao critica: o banco precisa continuar existindo apos a copia
if defined BACKUP_PRE_UPDATE (
    if not exist "%DEST%\data\tikaum.db" (
        echo  ERRO CRITICO: o banco de dados sumiu durante a copia!
        echo  Restaurando o backup criado no passo [3/5]...
        copy /Y "!BACKUP_PRE_UPDATE!" "%DEST%\data\tikaum.db" >nul
        echo  Banco restaurado a partir de tikaum_pre_update_%DATA%.db.
        echo  Atualizacao abortada. Verifique a pasta de origem e tente de novo.
        pause & exit /b 1
    )
)

:: ---- [5/5] Reiniciar o TikaumTech -------------------------------------
echo  [5/5] Reiniciando o TikaumTech...
:: -ErrorAction Stop faz o PowerShell sair com erro se a tarefa nao existir
:: (instalacoes antigas sem tarefa agendada) — nesse caso inicia direto.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
 "Start-ScheduledTask -TaskName 'TikaumTech' -ErrorAction Stop" >nul 2>&1
if errorlevel 1 (
    start "" /D "%DEST%" "%DEST%\TikaumTech.exe"
)

echo.
echo  ==========================================
echo   Atualizacao concluida.
echo   Banco de dados preservado em
echo   C:\TikaumTech\data\
echo   Acesse http://localhost:5000
echo  ==========================================
echo.
pause
