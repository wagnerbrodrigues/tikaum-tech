@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul 2>&1

echo.
echo  ==========================================
echo   TikaumTech ^| Instalador v2.1
echo  ==========================================
echo.

:: Verificar privilegios de administrador
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo  ERRO: Execute este arquivo como Administrador.
    echo  Clique com botao direito ^> "Executar como administrador".
    echo.
    pause & exit /b 1
)

set "DEST=C:\TikaumTech"

:: ---- Localizar pasta de origem (publish\) ----------------------------
:: Se executado a partir da raiz do repo (antes do build_release.bat copiar
:: este arquivo para publish\), o .exe nao esta em %~dp0 — nesse caso,
:: procurar em publish\ ao lado.
set "SRC=%~dp0"
if not exist "%SRC%TikaumTech.exe" (
    if exist "%~dp0publish\TikaumTech.exe" (
        set "SRC=%~dp0publish\"
    ) else (
        echo  ERRO: TikaumTech.exe nao encontrado.
        echo  Rode build_release.bat primeiro e execute o install.bat
        echo  de dentro da pasta publish\ gerada.
        pause & exit /b 1
    )
)

:: ---- Parar instancia em execucao -------------------------------------
:: Se o TikaumTech ja foi instalado antes, a Tarefa Agendada o inicia a
:: cada logon e ele fica rodando indefinidamente. Sem parar essa instancia
:: agora, o [6/6] abaixo tenta iniciar a versao nova mas a porta 5000 ja
:: esta ocupada pela versao ANTIGA — a nova simplesmente nao sobe, e o
:: usuario continua vendo o app antigo (tema/bugs velhos) mesmo depois de
:: reinstalar, parecendo que a atualizacao "nao pegou".
echo  Parando instancia anterior do TikaumTech (se houver)...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
 "Stop-ScheduledTask -TaskName 'TikaumTech' -ErrorAction SilentlyContinue" >nul 2>&1
taskkill /F /IM TikaumTech.exe /T >nul 2>&1
:: Cobre o caso de outra instancia (aberta manualmente, ou com outro nome de
:: processo) ainda segurando a porta 5000.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
 "Get-NetTCPConnection -LocalPort 5000 -ErrorAction SilentlyContinue |" ^
 " Select-Object -ExpandProperty OwningProcess -Unique |" ^
 " ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }"

:: Espera o Windows soltar o handle do .exe antes do xcopy (o processo pode
:: levar um instante para liberar o arquivo apos ser encerrado). Sem isso, o
:: xcopy podia "ter sucesso" sem realmente substituir o .exe travado, fazendo
:: a versao ANTIGA continuar rodando mesmo depois de reinstalar.
set "TENTATIVAS=0"
:esperar_liberar
if not exist "%DEST%\TikaumTech.exe" goto liberado
powershell -NoProfile -Command "try { $s = [IO.File]::Open('%DEST%\TikaumTech.exe','Open','ReadWrite','None'); $s.Close(); exit 0 } catch { exit 1 }" >nul 2>&1
if not errorlevel 1 goto liberado
set /a TENTATIVAS+=1
if %TENTATIVAS% GEQ 10 (
    echo  AVISO: nao foi possivel confirmar que a versao anterior foi encerrada.
    echo  Se algo parecer desatualizado apos a instalacao, feche "TikaumTech.exe"
    echo  manualmente no Gerenciador de Tarefas e rode este instalador de novo.
    goto liberado
)
timeout /t 1 /nobreak >nul
goto esperar_liberar
:liberado

:: ---- [1/6] Copiar arquivos ------------------------------------------
:: Remove a instalacao anterior (exceto data\ e config\) antes de copiar:
:: o xcopy sozinho so sobrescreve, deixando DLLs e assets de versoes
:: antigas misturados com os novos — receita para comportamento fantasma
:: apos varias reinstalacoes. config\ guarda credenciais/token do Google
:: Drive e chaves de criptografia — apagar obrigaria a reconectar a conta
:: (e invalidaria o token criptografado) a cada atualizacao.
echo  [1/6] Instalando arquivos em %DEST%...
if exist "%DEST%\TikaumTech.exe" (
    for /d %%D in ("%DEST%\*") do if /I not "%%~nxD"=="data" if /I not "%%~nxD"=="config" rd /s /q "%%D"
    del /q "%DEST%\*.*" >nul 2>&1
)
if not exist "%DEST%" mkdir "%DEST%"
xcopy /E /I /Y /Q "%SRC%*" "%DEST%\" 1>nul
if errorlevel 1 (
    echo  ERRO: falha ao copiar arquivos. Verifique permissoes.
    pause & exit /b 1
)
if not exist "%DEST%\data" mkdir "%DEST%\data"
if not exist "%DEST%\data\backups" mkdir "%DEST%\data\backups"
if not exist "%DEST%\config" mkdir "%DEST%\config"

:: ---- [2/6] Senha do administrador ----------------------------------
echo.
echo  [2/6] Configurar senha do administrador
echo  (A senha nao e exibida enquanto voce digita)
echo.

:senha_loop
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
 "$ok=$false; while(-not $ok){" ^
 "  $s1=Read-Host -AsSecureString 'Senha (minimo 4 caracteres)';" ^
 "  $p1=[Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s1));" ^
 "  if($p1.Length -lt 4){Write-Host 'Senha muito curta. Tente novamente.'; continue}" ^
 "  $s2=Read-Host -AsSecureString 'Confirmar senha';" ^
 "  $p2=[Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s2));" ^
 "  if($p1 -ne $p2){Write-Host 'Senhas nao coincidem. Tente novamente.'; continue}" ^
 "  $json=[pscustomobject]@{AdminPassword=$p1}|ConvertTo-Json -Compress;" ^
 "  [IO.File]::WriteAllText('%DEST%\data\setup.json',$json);" ^
 "  Write-Host 'Senha configurada.'; $ok=$true}"
if errorlevel 1 ( echo. & echo  Tente novamente. & goto senha_loop )

:: ---- [3/6] Hostname -------------------------------------------------
echo.
echo  [3/6] Configurando tikaum-tech.local no arquivo hosts...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
 "$f='C:\Windows\System32\drivers\etc\hosts';" ^
 "$c=[IO.File]::ReadAllText($f);" ^
 "if($c -notmatch 'tikaum-tech\.local'){" ^
 "  [IO.File]::AppendAllText($f,\"`r`n127.0.0.1   tikaum-tech.local   # TikaumTech\");" ^
 "  Write-Host 'Hostname adicionado.'" ^
 "} else { Write-Host 'Hostname ja configurado.' }"

:: Limpa o cache de DNS: se o navegador ja tentou acessar tikaum-tech.local
:: antes desta instalacao, o Windows guarda a falha e continua reportando
:: "pagina nao encontrada" mesmo com o hosts corrigido, ate isso ser limpo.
ipconfig /flushdns >nul 2>&1

:: ---- [4/6] Inicio automatico ----------------------------------------
echo.
echo  [4/6] Configurando inicio automatico com o Windows...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
 "$a=New-ScheduledTaskAction -Execute '%DEST%\TikaumTech.exe' -WorkingDirectory '%DEST%';" ^
 "$t=New-ScheduledTaskTrigger -AtLogOn;" ^
 "$s=New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0 -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1);" ^
 "Register-ScheduledTask -TaskName 'TikaumTech' -Action $a -Trigger $t -Settings $s -RunLevel Highest -Force | Out-Null;" ^
 "Write-Host 'Tarefa agendada criada (inicia com o logon do Windows).'"
if errorlevel 1 ( echo  AVISO: nao foi possivel configurar inicio automatico. )

:: ---- [5/6] Atalho na area de trabalho --------------------------------
:: O atalho precisa GARANTIR que o site abre, mesmo se o TikaumTech.exe nao
:: estiver rodando (autostart falhou, foi fechado manualmente, etc.) — um
:: .url simples so abre o navegador e deixa a pagina "inacessivel" nesse
:: caso. Por isso o atalho agora aponta para um pequeno launch.vbs que
:: tenta iniciar o .exe (sem nenhuma janela — nem console, nem prompt) e so
:: entao abre o navegador. Se o .exe ja estiver rodando, iniciar de novo so
:: falha silenciosamente (porta 5000 ja em uso) sem efeito nenhum.
echo.
echo  [5/6] Criando atalho na area de trabalho...
(
echo Set sh = CreateObject^("WScript.Shell"^)
echo sh.Run """%DEST%\TikaumTech.exe""", 0, False
echo WScript.Sleep 1500
echo sh.Run "http://localhost:5000"
) > "%DEST%\launch.vbs"

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
 "$desktop=[Environment]::GetFolderPath('CommonDesktopDirectory');" ^
 "Remove-Item \"$desktop\TikaumTech.url\" -ErrorAction SilentlyContinue;" ^
 "$ws=New-Object -ComObject WScript.Shell;" ^
 "$lnk=$ws.CreateShortcut(\"$desktop\TikaumTech.lnk\");" ^
 "$lnk.TargetPath='C:\Windows\System32\wscript.exe';" ^
 "$lnk.Arguments='//B \"%DEST%\launch.vbs\"';" ^
 "$lnk.IconLocation='%DEST%\TikaumTech.exe,0';" ^
 "$lnk.WorkingDirectory='%DEST%';" ^
 "$lnk.Description='TikaumTech';" ^
 "$lnk.Save();" ^
 "Write-Host 'Atalho criado na area de trabalho (todos os usuarios).'"
if errorlevel 1 ( echo  AVISO: nao foi possivel criar o atalho na area de trabalho. )

:: ---- [6/6] Iniciar --------------------------------------------------
echo.
echo  [6/6] Iniciando TikaumTech...
:: /D fixa o diretorio de trabalho na pasta da instalacao — a elevacao do UAC
:: troca o CWD para C:\Windows\System32 (defesa extra; o Program.cs tambem se
:: ancora sozinho na pasta do executavel).
start "" /D "%DEST%" "%DEST%\TikaumTech.exe"

echo.
echo  ==========================================
echo   Instalacao concluida!
echo   O navegador abrira automaticamente em
echo   http://localhost:5000 assim que o
echo   TikaumTech terminar de iniciar.
echo   (http://tikaum-tech.local:5000 tambem
echo   funciona como endereco alternativo)
echo  ==========================================
echo.
echo  TikaumTech sera iniciado automaticamente
echo  a cada logon do Windows, e ha um atalho
echo  na area de trabalho para abrir manualmente.
echo.
pause
