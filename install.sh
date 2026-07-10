#!/usr/bin/env bash
# TikaumTech — Instalador Linux
# Execute como root: sudo ./install.sh
#
# Uso:  sudo ./install.sh
#
# Instala o TikaumTech em /opt/tikaumtech, configura o serviço systemd para
# iniciar automaticamente com o sistema e abre o navegador ao final.
# Requer o executável TikaumTech (linux-x64 self-contained) no mesmo diretório
# que este script — gerado por build_release.sh ou equivalente.

set -euo pipefail
cd "$(dirname "$0")"

echo ""
echo " =========================================="
echo "   TikaumTech | Instalador Linux v1.0"
echo " =========================================="
echo ""

# Verificar root
if [ "$(id -u)" -ne 0 ]; then
    echo " ERRO: Execute este script como root (sudo ./install.sh)."
    echo " Abra um terminal nesta pasta e rode: sudo ./install.sh"
    echo ""
    exit 1
fi

if ! command -v dotnet &> /dev/null; then
    echo ".NET 9 não encontrado. Instalando..."
    wget -q https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh --channel 9.0 --install-dir /usr/share/dotnet --no-path
    ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet
    rm /tmp/dotnet-install.sh
    echo ".NET 9 instalado."
fi

DEST="/opt/tikaumtech"

# ---- Localizar pasta de origem ------------------------------------------
SRC="$(pwd)"
if [ ! -f "$SRC/TikaumTech" ]; then
    if [ -f "$SRC/publish/TikaumTech" ]; then
        SRC="$SRC/publish"
    else
        echo " ERRO: Executável TikaumTech não encontrado."
        echo " Rode build_release.sh primeiro e execute install.sh de dentro"
        echo " da pasta publish/ gerada."
        exit 1
    fi
fi

# ---- Parar instância em execução ----------------------------------------
echo " Parando instância anterior do TikaumTech (se houver)..."
systemctl stop tikaumtech 2>/dev/null || true
pkill -f TikaumTech 2>/dev/null || true
sleep 1

# ---- [1/5] Copiar arquivos ----------------------------------------------
echo " [1/5] Instalando arquivos em $DEST..."
mkdir -p "$DEST/data/backups" "$DEST/config"
# Copia todos os arquivos preservando data/ e config/ (credenciais do Drive,
# banco de dados) — um rsync exclui essas pastas; cp -r copia o resto.
if command -v rsync &>/dev/null; then
    rsync -a --exclude=data --exclude=config "$SRC/" "$DEST/"
else
    find "$SRC" -mindepth 1 -maxdepth 1 \
        ! -name data ! -name config \
        -exec cp -r {} "$DEST/" \;
fi
chmod +x "$DEST/TikaumTech"

# ---- [2/5] Senha do administrador ---------------------------------------
echo ""
echo " [2/5] Configurar senha do administrador"
echo " (A senha não é exibida enquanto você digita)"
echo ""
while true; do
    read -rsp " Senha (mínimo 4 caracteres): " SENHA1; echo
    if [ "${#SENHA1}" -lt 4 ]; then echo " Senha muito curta. Tente novamente."; continue; fi
    read -rsp " Confirmar senha: " SENHA2; echo
    if [ "$SENHA1" != "$SENHA2" ]; then echo " Senhas não coincidem. Tente novamente."; continue; fi
    printf '{"AdminPassword":"%s"}' "$SENHA1" > "$DEST/data/setup.json"
    echo " Senha configurada."
    break
done

# ---- [3/5] Hostname -----------------------------------------------------
echo ""
echo " [3/5] Configurando tikaum-tech.local em /etc/hosts..."
if ! grep -q "tikaum-tech\.local" /etc/hosts; then
    printf "\n127.0.0.1   tikaum-tech.local   # TikaumTech\n" >> /etc/hosts
    echo " Hostname adicionado."
else
    echo " Hostname já configurado."
fi

# ---- [4/5] Serviço systemd ----------------------------------------------
echo ""
echo " [4/5] Configurando serviço systemd (início automático com o sistema)..."
cat > /etc/systemd/system/tikaumtech.service << 'UNIT'
[Unit]
Description=TikaumTech
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/tikaumtech
ExecStart=/opt/tikaumtech/TikaumTech
Restart=on-failure
RestartSec=5
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload
systemctl enable tikaumtech
echo " Serviço configurado (inicia automaticamente)."

# ---- [5/5] Iniciar ------------------------------------------------------
echo ""
echo " [5/5] Iniciando TikaumTech..."
systemctl start tikaumtech

echo ""
echo " =========================================="
echo "   Instalação concluída!"
echo "   O app está rodando em"
echo "   http://localhost:5000"
echo "   (http://tikaum-tech.local:5000 também"
echo "   funciona como endereço alternativo)"
echo " =========================================="
echo ""
echo " TikaumTech será iniciado automaticamente"
echo " a cada boot do sistema."
echo ""
