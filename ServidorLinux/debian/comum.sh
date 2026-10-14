# Pecas comuns dos scripts desta pasta. Nao e pra rodar sozinho: os outros scripts fazem `. comum.sh`.

PASTA="$(cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")" && pwd)"
SESSAO=jandirus
BINARIO="$PASTA/jandirus-servidor.x86_64"
ARQUIVO_PID="$PASTA/.servidor.pid"

# Um servidor de tmux SO DESTE JOGO (-L): ele nao se mistura com o tmux que voce usa pra outras coisas, e o
# systemd acompanha o processo certo.
tmuxj() { tmux -L "$SESSAO" "$@"; }

# O padrao de cada opcao; o servidor.conf (se existir) troca o que quiser.
PORTA=7777
ADMIN=
FPS_MAXIMO=60
FUSO=America/Sao_Paulo
IDIOMA=pt_BR.UTF-8
[ -f "$PASTA/servidor.conf" ] && . "$PASTA/servidor.conf"

# Os scripts rodam como o DONO DESTA PASTA. Cada usuario tem o seu tmux: se o servidor foi ligado pelo usuario
# "jogo" e voce abre o console como root, o root nao enxerga a sessao. Isto troca de usuario sozinho (sudo) em
# vez de dizer "nao esta ligado" pra um servidor que esta.
garantir_dono() {
  local dono
  dono="$(stat -c %U "$PASTA")"
  [ "$(id -un)" = "$dono" ] && return 0
  # (o root troca pelo `runuser`, que todo Debian tem -- uma instalacao minima nem sempre tem o sudo)
  if [ "$(id -u)" -eq 0 ]; then
    exec runuser -u "$dono" -- bash "$PASTA/$(basename "$0")" "$@"
  fi
  exec sudo -u "$dono" -- bash "$PASTA/$(basename "$0")" "$@"
}

ligado() { tmuxj has-session -t "$SESSAO" 2>/dev/null; }

# Escreve uma linha no console do servidor, como se alguem a tivesse digitado.
digitar() {
  tmuxj send-keys -t "$SESSAO" -l -- "$*"
  tmuxj send-keys -t "$SESSAO" Enter
}
