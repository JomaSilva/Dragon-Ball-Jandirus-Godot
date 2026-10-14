#!/usr/bin/env bash
# Manda UM comando pro servidor sem abrir o terminal, e mostra a resposta. Serve pro dia a dia e pro cron:
#   ./comando.sh admin fulano
#   ./comando.sh anunciar o servidor reinicia em 5 minutos
set -u
. "$(dirname "$(readlink -f "$0")")/comum.sh"
garantir_dono "$@"
[ $# -ge 1 ] || { echo "uso: ./comando.sh <comando>   (ex.: ./comando.sh admin fulano -- a lista: ./comando.sh ajuda)"; exit 1; }
ligado || { echo "O servidor nao esta ligado. Ligue com ./iniciar.sh"; exit 1; }
digitar "$*"
sleep 1
tmuxj capture-pane -p -t "$SESSAO" | grep -v '^$' | tail -n 10
