#!/usr/bin/env bash
# Desliga o servidor do jeito certo: o comando `parar` avisa quem esta jogando, grava tudo e fecha.
set -u
. "$(dirname "$(readlink -f "$0")")/comum.sh"
garantir_dono "$@"
ligado || { echo "O servidor ja esta desligado."; exit 0; }
digitar parar
printf 'Gravando e desligando'
for _ in $(seq 1 120); do
  ligado || { echo " -- desligado."; rm -f "$ARQUIVO_PID"; exit 0; }
  printf '.'
  sleep 1
done
echo
echo "O servidor nao fechou em 2 minutos. Veja o que ele esta fazendo com ./console.sh"
exit 1
