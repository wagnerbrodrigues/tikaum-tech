#!/usr/bin/env bash
# Roda na máquina do DESENVOLVEDOR (Linux): publica o TikaumTech e monta o
# pacote de implantação completo. A máquina do estúdio NÃO compila nada —
# só recebe o pacote e executa install.bat (primeira vez) ou update.bat
# (atualização) como Administrador.
#
# Uso:  ./build_release.sh                → pacote Windows (win-x64) em publish/
#       ./build_release.sh /media/x/PEN   → idem, e copia o pacote para o destino
#                                           (ex.: pen drive montado)
#       ./build_release.sh --linux        → pacote Linux (linux-x64) em publish-linux/
#                                           (para instalar com sudo ./install.sh)
#
# O publish é self-contained: a máquina de destino não precisa de .NET.
# Equivalente Linux do build_release.bat (que segue valendo para quem
# desenvolve no Windows).
set -euo pipefail
cd "$(dirname "$0")"

RID="win-x64"
DESTINO=""
for arg in "$@"; do
    case "$arg" in
        --linux) RID="linux-x64" ;;
        *)       DESTINO="$arg" ;;
    esac
done

# ---- Localizar o SDK do .NET 9 (só o dev compila) -------------------------
if command -v dotnet >/dev/null 2>&1; then
    DOTNET=dotnet
elif [ -x "$HOME/.dotnet/dotnet" ]; then
    DOTNET="$HOME/.dotnet/dotnet"
else
    echo ".NET 9 SDK não encontrado. Instalando em ~/.dotnet (sem sudo)..."
    wget -q https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh --channel 9.0 --install-dir "$HOME/.dotnet" --no-path
    rm /tmp/dotnet-install.sh
    DOTNET="$HOME/.dotnet/dotnet"
    echo ".NET 9 SDK instalado. Continuando..."
fi

# Sandbox/ambiente sem libicu: o próprio CLI do .NET aborta no start — o modo
# invariante vale só para o PROCESSO DE BUILD, não afeta o app publicado (que
# roda com ICU completo na máquina de destino). O bash -c interno engole a
# mensagem "Aborted" do crash de teste (o "; exit $?" força fork em vez de
# exec — com exec, o sinal viraria mensagem no stderr DESTE script).
if ! bash -c '"$1" --version; exit $?' _ "$DOTNET" >/dev/null 2>&1; then
    export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
fi

if [ ! -f "src/TikaumTech/TikaumTech.csproj" ]; then
    echo "ERRO: src/TikaumTech/TikaumTech.csproj não encontrado." >&2
    echo "Execute build_release.sh a partir da raiz do repositório clonado" >&2
    echo "completo (não de uma cópia isolada deste arquivo)." >&2
    exit 1
fi

if [ "$RID" = "win-x64" ]; then OUT="publish"; else OUT="publish-linux"; fi

# Limpa a saída anterior: dotnet publish não remove arquivos órfãos de builds
# antigos, e distribuir um pacote com resíduos de várias versões misturadas
# gera comportamento imprevisível na instalação.
rm -rf "$OUT"

echo "[1/2] Publicando aplicação (Release, $RID, self-contained)..."
"$DOTNET" publish src/TikaumTech -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=false \
    -o "$OUT"

echo "[2/2] Copiando instalador e atualizador para a pasta de distribuição..."
if [ "$RID" = "win-x64" ]; then
    # CRLF garantido nos .bat: o working tree Linux costuma tê-los em LF e o
    # cmd.exe quebra com LF puro (continuações ^, blocos com parênteses).
    # Normaliza sempre — remove CR existente e recoloca (idempotente).
    for f in install.bat update.bat; do
        sed 's/\r$//' "$f" | sed 's/$/\r/' > "$OUT/$f"
    done
else
    cp install.sh "$OUT/install.sh"
    chmod +x "$OUT/install.sh"
fi

# ---- Deploy opcional: copiar o pacote para o destino (ex.: pen drive) -----
if [ -n "$DESTINO" ]; then
    if [ ! -d "$DESTINO" ]; then
        echo "ERRO: destino '$DESTINO' não existe ou não está montado." >&2
        exit 1
    fi
    PACOTE="$DESTINO/TikaumTech_$(date +%Y-%m-%d)"
    echo "Copiando pacote para $PACOTE ..."
    rm -rf "$PACOTE"
    cp -r "$OUT" "$PACOTE"
    sync
fi

echo ""
echo "====================================================="
echo " Build concluído. Pasta de distribuição: $OUT/"
[ -n "$DESTINO" ] && echo " Pacote copiado para: $PACOTE/"
echo ""
if [ "$RID" = "win-x64" ]; then
    echo " Na máquina do estúdio (Windows, como Administrador):"
    echo "   - Primeira instalação:  install.bat"
    echo "   - Atualização:          update.bat"
    echo "     (preserva banco de dados e configurações)"
else
    echo " Na máquina de destino (Linux):"
    echo "   sudo ./install.sh"
fi
echo "====================================================="
