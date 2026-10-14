#!/usr/bin/env bash
# Prepara a VPS pra rodar o servidor. Rode como root (ou com sudo), de dentro desta pasta:
#
#   sudo bash instalar.sh                 instala o que falta e liga o servidor junto com a VPS (systemd)
#   sudo bash instalar.sh --sem-servico   so instala o que falta; voce liga e desliga na mao (./iniciar.sh)
#
# Pode rodar de novo quando quiser (depois de atualizar o servidor, por exemplo): ele nao apaga nada.
set -eu
PASTA="$(cd "$(dirname "$(readlink -f "$0")")" && pwd)"
[ "$(id -u)" -eq 0 ] || { echo "Rode com sudo:  sudo bash instalar.sh"; exit 1; }
[ -f "$PASTA/jandirus-servidor.x86_64" ] || { echo "Nao achei jandirus-servidor.x86_64 aqui -- rode de dentro da pasta do pacote."; exit 1; }

DONO="$(stat -c %U "$PASTA")"
if [ "$DONO" = "root" ]; then
  echo "AVISO: a pasta e do root, entao o servidor vai rodar como root. O mais seguro e um usuario so pra ele:"
  echo "    adduser --disabled-password --gecos '' jogo"
  echo "    mv \"$PASTA\" /home/jogo/ && chown -R jogo: /home/jogo/$(basename "$PASTA")"
  echo "  ...e rodar este script de novo de la."
  echo
fi

echo "== pacotes"
apt-get update -qq
# O nome do pacote da ICU (cultura/idioma do .NET) e o da OpenSSL (a senha das contas) mudam a cada versao do
# Debian (libicu72, libicu76, libssl3, libssl3t64...): pega o que esta disponivel nesta.
ICU="$(apt-cache pkgnames libicu | grep -E '^libicu[0-9]+$' | sort -V | tail -n 1 || true)"
SSL="$(apt-cache pkgnames libssl | grep -E '^libssl(3t64|3|1\.1)$' | sort -V | tail -n 1 || true)"
DEBIAN_FRONTEND=noninteractive apt-get install -y -qq tmux ${ICU:+"$ICU"} ${SSL:+"$SSL"} >/dev/null
echo "   tmux, ${ICU:-(ICU NAO ACHADA)}, ${SSL:-(OpenSSL NAO ACHADA)}"

echo "== arquivos"
chmod +x "$PASTA"/*.sh "$PASTA/jandirus-servidor.x86_64"
if [ ! -f "$PASTA/servidor.conf" ]; then
  cp "$PASTA/servidor.conf.exemplo" "$PASTA/servidor.conf"
  echo "   servidor.conf criado (porta 7777) -- mude la a porta e o resto"
fi
chown -R "$DONO": "$PASTA"
. "$PASTA/servidor.conf"

# O firewall da propria VPS, se houver um ligado. (O do PROVEDOR -- o painel da Oracle, da AWS, da Hetzner... --
# e outro, e so se abre por la.)
if command -v ufw >/dev/null && ufw status 2>/dev/null | grep -q "Status: active"; then
  ufw allow "${PORTA:-7777}/udp" >/dev/null && echo "   ufw: porta ${PORTA:-7777}/udp liberada"
fi

if [ "${1:-}" = "--sem-servico" ]; then
  echo
  echo "Pronto. Ligue com:  $PASTA/iniciar.sh"
  exit 0
fi

echo "== servico (o servidor liga junto com a VPS e volta sozinho se o tmux morrer)"
cat > /etc/systemd/system/jandirus.service <<FIM
[Unit]
Description=Dragon Ball Jandirus - servidor dedicado
After=network-online.target
Wants=network-online.target

[Service]
Type=forking
User=$DONO
WorkingDirectory=$PASTA
ExecStart=/bin/bash $PASTA/iniciar.sh
ExecStop=/bin/bash $PASTA/parar.sh
PIDFile=$PASTA/.servidor.pid
TimeoutStartSec=120
TimeoutStopSec=180
Restart=on-failure
RestartSec=15

[Install]
WantedBy=multi-user.target
FIM
systemctl daemon-reload
systemctl enable jandirus.service >/dev/null 2>&1
# UM SERVIDOR LIGADO NA MAO (./iniciar.sh) fica fora do servico, e o systemd recusaria acompanha-lo: ele e
# desligado com calma (grava tudo) e religado pelo servico.
if ! systemctl is-active --quiet jandirus.service && runuser -u "$DONO" -- tmux -L jandirus has-session -t jandirus 2>/dev/null; then
  runuser -u "$DONO" -- bash "$PASTA/parar.sh"
fi
systemctl restart jandirus.service
sleep 1
systemctl --no-pager --lines=0 status jandirus.service || true

cat <<FIM

Pronto. O servidor esta no ar na porta ${PORTA:-7777}/UDP e liga sozinho quando a VPS liga.
   Terminal do servidor:  $PASTA/console.sh   (sai sem desligar: Ctrl+B, depois D)
   Desligar / ligar:      systemctl stop jandirus   /   systemctl start jandirus
FIM
