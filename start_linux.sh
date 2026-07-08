#!/usr/bin/env bash
# Roda o Tikaum-Tech no Linux (TIKAUM_SPEC.md §10, "Execução no Linux").
#
# Uso:  ./start_linux.sh            → modo produção (banco data/tikaum.db, abre o navegador)
#       ./start_linux.sh --dev      → modo desenvolvimento (banco data/tikaum-dev.db)
#
# Roda em segundo plano (nohup, desacoplado do terminal) — equivalente ao Windows, onde o
# .exe (subsistema WinExe) nunca abre uma janela de console (decisão de 2026-07-06: mesmo
# comportamento nos dois sistemas). Log em logs/tikaum.log; para parar, use ./stop_linux.sh.
#
# O diretório de trabalho do app é ancorado na pasta do binário (Program.cs), então o
# banco fica em src/TikaumTech/bin/Release/net9.0/data/ ao rodar via SDK. Para uma
# instalação fixa, prefira: dotnet publish -c Release -r linux-x64 --self-contained
# e execute ./TikaumTech de dentro da pasta publicada.
set -euo pipefail
cd "$(dirname "$0")"

if command -v dotnet >/dev/null 2>&1; then
    DOTNET=dotnet
elif [ -x "$HOME/.dotnet/dotnet" ]; then
    DOTNET="$HOME/.dotnet/dotnet"
else
    echo "SDK do .NET 9 não encontrado. Instale em https://dotnet.microsoft.com/download" >&2
    exit 1
fi

if [ "${1:-}" = "--dev" ]; then
    export ASPNETCORE_ENVIRONMENT=Development
else
    export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
fi

# Uma instância já no ar faria esta nova morrer EM SILÊNCIO (design da Rodada 19) — e o
# navegador continuaria mostrando a versão antiga, parecendo que a atualização "não pegou".
# Detecta e orienta em vez de fingir que iniciou (2026-07-08).
if (exec 3<>/dev/tcp/127.0.0.1/5000) 2>/dev/null; then
    echo "Já existe uma instância do Tikaum-Tech no ar em http://localhost:5000." >&2
    echo "Para aplicar uma versão atualizada: rode ./stop_linux.sh e depois este script de novo" >&2
    echo "(no navegador, recarregue com Ctrl+F5)." >&2
    exit 1
fi

# Ambientes mínimos sem libicu derrubam o .NET na inicialização — detecta e ativa o
# modo invariante automaticamente (efeito colateral: moeda renderiza como ¤ em vez de R$
# e datas saem em MM/dd; para o correto, instale libicu: sudo apt install libicu-dev  /
# dnf install libicu).
#
# ATENÇÃO à detecção: ldconfig mora em /sbin, que NÃO está no PATH de usuário comum no
# Debian — a versão antiga deste teste ("ldconfig -p | grep libicu") falhava em silêncio
# e forçava o modo invariante mesmo com libicu instalada, quebrando datas (07/08/2026 em
# vez de 08/07/2026) e moeda (¤) em todas as telas. Por isso: tenta ldconfig também por
# caminho absoluto e, se indisponível, procura o arquivo da lib nos diretórios usuais.
tem_libicu() {
    for LDC in ldconfig /sbin/ldconfig /usr/sbin/ldconfig; do
        if command -v "$LDC" >/dev/null 2>&1; then
            "$LDC" -p 2>/dev/null | grep -q 'libicu' && return 0
            return 1   # ldconfig funcionou e não achou libicu: resposta confiável
        fi
    done
    # Sem ldconfig utilizável: procura direto no filesystem (cobre Debian/Ubuntu/Fedora/Arch)
    for dir in /usr/lib/x86_64-linux-gnu /usr/lib64 /usr/lib /lib/x86_64-linux-gnu /usr/lib/aarch64-linux-gnu; do
        for f in "$dir"/libicuuc.so*; do
            [ -e "$f" ] && return 0
        done
    done
    return 1
}
if ! tem_libicu; then
    echo "Aviso: libicu não encontrada — rodando em modo invariante (moeda ¤, datas MM/dd)."
    echo "Para corrigir: sudo apt install libicu-dev (Debian/Ubuntu) ou dnf install libicu (Fedora)."
    export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
fi

mkdir -p logs
LOG="logs/tikaum.log"

nohup "$DOTNET" run --project src/TikaumTech -c Release --no-launch-profile \
    > "$LOG" 2>&1 &
disown

echo "Tikaum-Tech iniciando em segundo plano (PID $!, ambiente: $ASPNETCORE_ENVIRONMENT)."
echo "Acesse http://localhost:5000 — log em $LOG"
echo "Para parar: ./stop_linux.sh"
