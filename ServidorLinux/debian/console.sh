#!/usr/bin/env bash
# Abre o terminal do servidor: o log ao vivo, e onde se digitam os comandos (admin, banir, salvar, parar...).
set -u
. "$(dirname "$(readlink -f "$0")")/comum.sh"
garantir_dono "$@"
ligado || { echo "O servidor nao esta ligado. Ligue com ./iniciar.sh"; exit 1; }
cat <<'FIM'
Abrindo o terminal do servidor. Digite `ajuda` pra ver os comandos.

   PRA SAIR SEM DESLIGAR O SERVIDOR: aperte Ctrl+B, solte, e aperte D.
   (Ctrl+C aqui DESLIGA o servidor -- ele grava antes, mas todo mundo cai.)
FIM
sleep 2
exec tmux -L "$SESSAO" attach -t "$SESSAO"
