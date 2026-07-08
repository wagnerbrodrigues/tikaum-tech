#!/usr/bin/env bash
# Para o Tikaum-Tech iniciado em segundo plano por start_linux.sh (TIKAUM_SPEC.md §10).
set -uo pipefail

if pkill -f "TikaumTech" 2>/dev/null; then
    echo "Tikaum-Tech encerrado."
else
    echo "Nenhuma instância do Tikaum-Tech encontrada rodando."
fi
