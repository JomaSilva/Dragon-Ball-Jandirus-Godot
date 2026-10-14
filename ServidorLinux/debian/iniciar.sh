#!/usr/bin/env bash
# Liga o servidor dentro de uma sessao do tmux -- o "terminal do servidor" -- e devolve o seu prompt.
#
#   ./iniciar.sh          liga em segundo plano (depois: ./console.sh pra abrir o terminal)
#   ./iniciar.sh --aqui   roda neste terminal mesmo, sem tmux (Ctrl+C grava e desliga)
#
# Se o servidor CAIR (erro, crash) ele e religado sozinho em 10 s. Se ele for DESLIGADO (o comando `parar`,
# o ./parar.sh, o systemctl stop) ele fica desligado.
set -u
. "$(dirname "$(readlink -f "$0")")/comum.sh"
garantir_dono "$@"

rodar_em_laco() {
  cd "$PASTA" || exit 1
  local args=(--headless --server --port "$PORTA")
  [ -n "$ADMIN" ] && args+=(--admin "$ADMIN")
  [ "${FPS_MAXIMO:-0}" != "0" ] && args+=(--max-fps "$FPS_MAXIMO")

  # Sem a biblioteca ICU o .NET se recusa a subir. Melhor subir sem cultura (numeros no formato ingles) do que
  # nao subir -- e dizer por que.
  local sem_icu=()
  if ! ls /usr/lib/*/libicuuc.so.* /usr/lib/libicuuc.so.* >/dev/null 2>&1; then
    echo "AVISO: falta a biblioteca ICU (rode: sudo bash instalar.sh). Subindo sem cultura."
    sem_icu=(DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1)
  fi

  while true; do
    echo "== $(date '+%d/%m %H:%M:%S') ligando o servidor na porta $PORTA/UDP"
    env TZ="$FUSO" LANG="$IDIOMA" ${sem_icu[@]+"${sem_icu[@]}"} "$BINARIO" "${args[@]}"
    local codigo=$?
    if [ "$codigo" -eq 0 ]; then
      echo "== $(date '+%d/%m %H:%M:%S') servidor desligado"
      return 0
    fi
    echo "== $(date '+%d/%m %H:%M:%S') o servidor CAIU (codigo $codigo) -- religando em 10 s (Ctrl+C cancela)"
    sleep 10 || return "$codigo"
  done
}

if [ "${1:-}" = "--aqui" ]; then
  rodar_em_laco
  exit $?
fi

if ligado; then
  tmuxj display-message -p -t "$SESSAO" '#{pid}' > "$ARQUIVO_PID"
  echo "O servidor ja esta ligado. Abra o terminal com ./console.sh"
  exit 0
fi
command -v tmux >/dev/null || { echo "Falta o tmux. Rode: sudo bash instalar.sh"; exit 1; }
[ -f "$BINARIO" ] || { echo "Nao achei $BINARIO -- esta pasta e a do pacote do servidor?"; exit 1; }
[ -x "$BINARIO" ] || chmod +x "$BINARIO"

tmuxj new-session -d -s "$SESSAO" -x 200 -y 50 "bash '$PASTA/iniciar.sh' --aqui"
# O PID do servidor do tmux e o que o systemd acompanha (PIDFile do jandirus.service).
tmuxj display-message -p -t "$SESSAO" '#{pid}' > "$ARQUIVO_PID"

# ESPERA O SERVIDOR ABRIR A PORTA, em vez de dizer "ligado" pra um processo que ainda pode cair no boot.
for _ in $(seq 1 60); do
  tela="$(tmuxj capture-pane -p -S -300 -t "$SESSAO" 2>/dev/null || true)"
  case "$tela" in
    *"escutando na porta"*)
      echo "Servidor no ar na porta $PORTA/UDP. Terminal: ./console.sh   Desligar: ./parar.sh"
      exit 0 ;;
    *"FALHOU ao abrir a porta"*|*"o servidor CAIU"*)
      echo "O servidor nao subiu. As ultimas linhas dele:"
      printf '%s\n' "$tela" | grep -v '^$' | tail -n 15
      exit 1 ;;
  esac
  ligado || { echo "O servidor fechou logo ao ligar. Rode ./iniciar.sh --aqui pra ver o erro."; exit 1; }
  sleep 0.5
done
echo "O servidor ainda esta subindo. Acompanhe com ./console.sh"
