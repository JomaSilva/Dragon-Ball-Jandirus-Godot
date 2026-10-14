# Servidor dedicado numa VPS Linux (Debian)

Esta pasta gera e roda o servidor do Dragon Ball Jandirus numa VPS Debian, com um **terminal de comandos**
para administrar o servidor: dar e tirar admin, banir, expulsar, trocar senha, anunciar, salvar e desligar.

## O que vai para a VPS

O servidor é o **próprio jogo rodando sem tela**, o mesmo do `servidor.bat`. Não dá para ser um
`dotnet Server.dll` solto, porque o código do servidor é um node do Godot e lê os mapas e os dados pelo motor.
O pacote (`jandirus-servidor/`) leva:

| Arquivo | O que é |
| --- | --- |
| `jandirus-servidor.x86_64` | o motor do Godot para Linux, rodando com `--headless` |
| `jandirus-servidor.pck` | os dados do jogo (mapas, raças, skills, sprites) |
| `data_Dragon ball Jandirus_linuxbsd_x86_64/` | as DLLs .NET do jogo (`Dragon ball Jandirus.dll`, `Jandirus.Core.dll`, `LiteNetLib.dll`...) **e o runtime .NET inteiro**: a VPS não precisa ter .NET instalado |
| `*.sh`, `servidor.conf.exemplo` | os scripts de instalar, ligar, desligar e abrir o terminal (VPS) |
| `server.dll` (+ `lancador/`, o fonte) | o lançador para painéis de hospedagem ".NET", que rodam `dotnet server.dll` (ver abaixo) |

Funciona em Debian 11, 12 e 13 (e Ubuntu) **x86_64/amd64**. Para VPS ARM (Oracle Ampere, por exemplo), troque
`binary_format/architecture` do preset "Linux Servidor" para `arm64` no `export_presets.cfg`.

## 1. Gerar o pacote (no Windows)

Dois cliques em **`exportar-servidor-linux.bat`**. Ele compila o C# para Linux, junta os dados e cria
`ServidorLinux\jandirus-servidor-linux.tar.gz`. Precisa dos modelos de exportação do Godot 4.7.1 mono, os mesmos
que geram o `.exe`, e do Godot fechado.

**Versão:** o servidor só aceita jogador com o jogo **da mesma versão e do mesmo build**. Exporte o `.exe` dos
jogadores e o servidor Linux da mesma pasta, sem mexer no código entre um e outro. O comando `status` mostra a
versão e o protocolo do servidor.

## 2. Mandar para a VPS e instalar

No PowerShell do Windows (troque `usuario@ip` pelo acesso da sua VPS):

```
scp ServidorLinux\jandirus-servidor-linux.tar.gz usuario@ip:~
ssh usuario@ip
```

E na VPS:

```
tar xzf jandirus-servidor-linux.tar.gz
cd jandirus-servidor
sudo bash instalar.sh
```

O `instalar.sh` instala o que falta (`tmux`, a ICU e a OpenSSL), cria o `servidor.conf` e liga o servidor como
serviço do systemd. A partir daí ele liga sozinho quando a VPS liga. Use `sudo bash instalar.sh --sem-servico`
se preferir ligar e desligar na mão.

**Libere a porta UDP 7777** no painel do provedor da VPS (Oracle, AWS, Hetzner, Contabo...): é um firewall separado
do da máquina. Se o `ufw` estiver ligado na VPS, o `instalar.sh` já abre a porta nele. Os jogadores entram pelo
IP público da VPS, porta 7777.

## Num painel de hospedagem ".NET" (em vez de uma VPS)

Painéis que oferecem um servidor ".NET" genérico só sabem rodar `dotnet <arquivo>.dll`. Para eles serve o
**`server.dll`**: um lançador pequeno que liga o `jandirus-servidor.x86_64` ao lado dele e deixa o console do painel
falando direto com o servidor (os comandos do terminal funcionam no console do painel). Quando o painel manda
desligar, o lançador repassa o sinal e o servidor grava tudo antes de sair. Os scripts `.sh` não são usados ali.

Suba a pasta `jandirus-servidor` inteira (por SFTP: o upload pelo navegador costuma ter limite de tamanho) e preencha:

| Campo | Valor |
| --- | --- |
| .NET Release Stream | `8.0` ou mais novo (o `server.dll` roda no 8, 9 e 10) |
| .NET SDK Version | vazio |
| App Compilation Mode | a opção que **não compila** (o `server.dll` já vai pronto) |
| App Download Location | vazio |
| App Compilation Arguments | vazio |
| App Compilation Target | vazio |
| App Build Name | `server.dll` |
| App Build Location | a pasta onde ficaram os arquivos (ex.: `jandirus-servidor`), ou vazio se estão na raiz |
| .NET Command Line Arguments | vazio |
| App Command Line Arguments | `--port <porta do painel>` (e, se quiser, `--admin <conta>`) |

Se o painel só tiver modos que compilam: App Compilation Target `lancador/Lancador.csproj` e App Build Location
`lancador/bin/Release/net8.0/publish`. O lançador procura o binário subindo as pastas, então acha o servidor do mesmo
jeito.

- A porta tem de aceitar **UDP**. Se o painel puser a porta na variável `SERVER_PORT` (os painéis no estilo
  Pterodactyl fazem isso), o `server.dll` usa ela sozinho.
- Use só `--port`, `--admin` e `--max-fps` nos argumentos. Qualquer outro tira o servidor do modo de produção: ele
  para de gravar ao fechar, e o terminal desliga.
- Os saves ficam **dentro da pasta do servidor**, em `dados/godot/app_userdata/Dragon ball Jandirus/saves/`, e não no
  HOME, que num contêiner pode sumir numa reinstalação. O backup é copiar a pasta `dados/`.
- O `server.dll` também acha um export feito pelo editor com outro nome (por exemplo `Jandirus v0.2.1.x86_64`), desde
  que seja o único `*.x86_64` da pasta.

## 3. O primeiro administrador

A conta nasce no **primeiro login** de cada pessoa, com a senha que ela escolher. Então:

1. Entre no servidor uma vez pelo jogo, com a conta que vai ser admin.
2. Na VPS, abra o terminal (`./console.sh`), ou o console do painel, e digite `admin <sua conta>`.

Vale na hora, sem religar nada. Se você estiver online, a aba de admin aparece no menu na mesma hora. Para dar
admin a outra pessoa, é o mesmo comando, com ela online ou não.

## Comandos do terminal

Onde está escrito `<conta>`, serve o nome da conta **ou** o nome de um personagem dela.

| Comando | O que faz |
| --- | --- |
| `admin <conta>` | dá administrador (na hora, com a pessoa online ou não) |
| `desadmin <conta>` | tira o administrador |
| `admins` | lista quem é administrador |
| `contas` | todas as contas do servidor, com os personagens e as marcas (admin, banida, calada, online) |
| `online` | quem está conectado agora: id, conta, raça, planeta e IP |
| `banir <conta>` ou `banir <conta> \| motivo` | bane e derruba quem estiver usando a conta. Admin precisa de `desadmin` antes |
| `perdoar <conta>` | tira o banimento |
| `expulsar <personagem>` | derruba do servidor. O personagem é gravado antes |
| `calar <conta>` / `descalar <conta>` | a conta não fala em canal nenhum / volta a falar |
| `senha <conta> <nova>` | troca a senha de quem esqueceu. A senha nova não vai para log nenhum |
| `anunciar <texto>` | aviso para todo mundo que está jogando |
| `salvar` | grava tudo agora (o servidor também grava sozinho a cada 2 minutos) |
| `status` | versão, porta, quanto tempo no ar, gente online e onde ficam os saves |
| `parar` | avisa quem está jogando, grava tudo e desliga |
| `ajuda` | a lista acima |

Tudo o que muda uma conta vai para o `admin.log`, assinado por "o terminal do servidor", do lado do que os admins
fazem pelo painel do jogo.

No Windows o `servidor.bat` ganhou o mesmo terminal: é só digitar na janela dele.

## O dia a dia na VPS

Rode de dentro da pasta `jandirus-servidor`:

| Para | Comando |
| --- | --- |
| abrir o terminal do servidor | `./console.sh` (para **sair sem desligar**: `Ctrl+B`, solte, e aperte `D`) |
| mandar um comando sem abrir o terminal | `./comando.sh admin fulano` |
| desligar (grava tudo antes) | `./parar.sh`, ou `sudo systemctl stop jandirus` |
| ligar | `./iniciar.sh`, ou `sudo systemctl start jandirus` |
| rodar na frente, sem tmux (para ver um erro) | `./iniciar.sh --aqui` |

`Ctrl+C` dentro do terminal **desliga** o servidor. Ele grava antes, mas todo mundo cai. Se o servidor cair por
erro, ele volta sozinho em 10 segundos. Se for desligado de propósito, fica desligado.

A porta, o fuso do log e o resto estão no `servidor.conf`. Mude e religue com `./parar.sh` e `./iniciar.sh`.

## Saves, log e backup

Ficam na pasta do usuário que roda o servidor, **fora** da pasta do pacote. Por isso atualizar o servidor não
apaga nada:

- contas, personagens, mundo e `admin.log`: `~/.local/share/godot/app_userdata/Dragon ball Jandirus/saves/`
- log completo do servidor: `~/.local/share/godot/app_userdata/Dragon ball Jandirus/logs/godot.log`

Backup:

```
tar czf ~/backup-jandirus-$(date +%F).tar.gz -C ~/.local/share/godot/app_userdata "Dragon ball Jandirus/saves"
```

## Atualizar o servidor

Gere o pacote novo no Windows (passo 1), mande para a VPS (passo 2) e, na VPS:

```
./jandirus-servidor/parar.sh
rm -rf jandirus-servidor/data_*
tar xzf jandirus-servidor-linux.tar.gz
sudo bash jandirus-servidor/instalar.sh
```

O `servidor.conf` e os saves ficam como estavam. Distribua aos jogadores o `.exe` da mesma versão.

## Se algo der errado

- **Ninguém consegue entrar:** confira a porta UDP no painel do provedor. Na VPS, `./comando.sh status` mostra a
  porta, e `ss -ulpn | grep 7777` mostra se ela está aberta.
- **"versão diferente" ao entrar:** o jogo do jogador e o servidor saíram de builds diferentes. Exporte os dois de
  novo da mesma pasta.
- **O servidor não sobe:** `./iniciar.sh --aqui` roda na frente e mostra o erro. O log completo está no `godot.log`
  (acima).
- **"este processo não tem terminal":** o servidor foi ligado sem tmux (por exemplo, por um serviço feito à mão).
  Ligue pelo `./iniciar.sh` ou pelo `instalar.sh`.
