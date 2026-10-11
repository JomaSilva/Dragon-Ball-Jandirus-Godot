# Relatório — pedido de 2026-10-10 (e as levas de 11/10)

**Primeira leva:** rádio do scouter, economia do "recurso", Drill, rochas, planetas procedurais, árvores, animais,
tela de conhecidos, verbs, menu de desejos, construção de base, bug da gravidade, mapa do mundo e radar das esferas.
**Segunda leva:** a interface (menu P como janela, chat e painéis redimensionáveis).
**Terceira leva (noite de 10/10 e 11/10):** o despertar do God Ki e os marcos de ganho das formas, as validações do
servidor, a limpeza do catálogo, o versionamento, a abertura com o título, a aba de mestre e alunos, os debuffs de
desmembramento, a raiva por amigo, a parada do servidor, o kit do Deus da Destruição e os NPCs Bills e Whis.

**Como foi feito:** um subagente por tópico, cada um numa pasta de rascunho própria, publicando no repo só pela fila
e só o que compila. **Nada foi commitado** (o HEAD continua no seu commit de 09/10 22:40, `bac218a`). Nenhum arquivo
do repo foi apagado; não houve checkout, reset nem stash.

**Regressão no estado final (11/10, 09:05 às 09:55, depois da última publicação):** 155 bancadas, mais repetições das
que já tinham oscilado (157 corridas). Vermelhas só as que já eram antes desta sessão: `planeta` (2), `tecla` (1),
`diagforma` (as 10 fixas), `dskills` (1), `povo` (1) e a pura `disciplinas` (1). As três que oscilaram durante o
trabalho — `dtranse`, `formas` e `abas` — passaram em todas as repetições; as duas últimas tiveram a causa achada e
consertada (seção 30).

**Tamanho da mudança:** 152 publicações; 233 arquivos alterados e 402 novos no repo (203 deles são código novo e
cerca de 190 são os `.uid` que o Godot gera ao lado de cada script); nenhum apagado.

Fotos de tudo: `Trailer/bruto/fotos_6199e7/pedido-2026-10-10/` (184 arquivos).

---

## 1. Resumo

| # | Tópico | Estado | Bancadas (placar) |
|---|---|---|---|
| 1 | Rádio do scouter | feito | `radio` 62/0 · `dradio` OK · `radiovivo` OK |
| 2 | Economia: recurso, Minerar, tech e melhorias em recursos, Space Pod 999, cavar removido | feito | `recursos` 137/0 · `minerar` 55 OK · `instalar` 248/0 |
| 3 | Drill | feito | `drill` 88/0 · `ddrill` OK |
| 4 | Rochas (colisão das grandes + minério) | feito | `rochas` 63/0 · `drochas` 19/0 |
| 5 | Planetas procedurais (blocos de madeira + macieira) | feito | `procedural` 23/0 · `pomar` 46/0 |
| 6 | Árvore no soco + toco | feito | `lenha` 113/0 · `dlenha` 52/0 |
| 7 | Animais + carne | feito | `animais` 125/0 · `danimais` 12/0 |
| 8 | Tela de conhecidos (People) + detalhes | feito | `conhecidos` 133/0 · `dconhecidos` 72 ok · `convivio` 194/0 |
| 9 | Radar sem radar + verbs por requisito | feito | `verbs` 88/0 · `dverbs` OK |
| 10 | Menu de desejos do dragão | feito | `menudragao` 105/0 · `ddesejos` 49/0 |
| 11 | Construção de base: custo + seletor | feito | `blocos` 86/0 · `construir` 57/0 |
| 12 | Bug da gravidade (4x virava 1x) | feito | `gravidade` 82/0 · `bercos` 327/0 |
| 13 | Mapa do mundo + radar circular | feito | `mapa` 49/0 · `radar` 26/0 · `dmapa` 48 OK |
| 14 | Interface (2ª leva): menu P como janela / destacado, chat e painéis redimensionáveis | feito | `djanelas` 121 OK · `opcoes` 55/0 |
| 15 | God Ki: como desperta (atmosfera divina + luta com quem tem ki divino) | feito | `godki` 104/0 · `dgodki` OK |
| 16 | Marcos de ganho das formas | feito | `marcos` 64/0 |
| 17 | Validações do servidor (3 rodadas) | feito | `validacoes` 226/0 · `atividade` 18 OK · `sala` 170/0 |
| 18 | Catálogo: armas comentadas, skills-fantasma, raça Dog, itens aposentados | feito | `aposentados` 23/0 · `instalar` 248/0 · `fonte` 19/0 |
| 19 | Versionamento (cliente e servidor se conferem) | feito | `versao` 62/0 · `dversao` 63 OK · 6 de dois processos |
| 20 | Abertura com o título animado | feito | `dabertura` 97 OK |
| 21 | Aba de mestre e alunos | feito | `alunos` 88/0 · `dalunos` 109 ok |
| 22 | Debuffs de desmembramento | feito | `membros` 103/0 · `kiponta` 128/0 |
| 23 | Raiva por amigo (dois degraus, salto de Ki, transformação automática) | feito | `raiva` 109/0 · `draiva` 59/0 · `raivapura` 697/0 |
| 24 | Parada do servidor, admin automático, saída no meio do embate | feito | `parada` 68/0 · `adminlocal` 26/0 · `saidaembate` 13/0 |
| 25 | Kit do Deus da Destruição (forma Fury) + passivas de Ultra Instinto / Ultra Ego | feito | `godkit` 203/0 |
| 26 | NPCs Bills e Whis | feito | `bills` 225/0 · `dbills` 48/0 |

Todas as bancadas rodam pelo lançador comum, dentro da fila:
`python E:/Users/Joao/Desktop/_rascunho_6199e7/fila.py teste -- bash Trailer/bruto/vizinhas_dos_blocos.sh <nome>`.

---

## 2. Rádio do scouter

- **Onde:** P → Equip → cartão do Scouter → bloco "Rádio" → "Trocar a frequência" (1 a 999; começa na 1; fica salva no
  personagem).
- **Falar:** com o scouter ligado, segurar **O** fala pelo rádio. O V continua sendo a voz de perto; as duas juntas
  mandam pelos dois.
- **Quem ouve:** quem está com scouter ligado na mesma frequência, no mesmo planeta inteiro, ou a até 500 tiles no
  espaço. Planeta ↔ órbita não chega. Lookout, cavernas, estações e interiores são zonas próprias.
- **Como se ouve:** só no ouvido esquerdo, banda de 300–3400 Hz, saturação, chiado leve e estalo ao abrir e fechar.
- **Na tela:** "RÁDIO 042 · transmitindo" / "ouvindo Fulano" no alto, e um sinal verde com antena sobre quem fala.
- **Bug antigo consertado:** o indicador de voz de outro jogador que nunca tinha voado ficava em cima do rosto.
- **Constantes:** `Core/Social/RadioDoScouter.cs` (faixa, alcance, 4 vozes por ouvinte), `Client/EfeitoDeRadio.cs`
  (banda, chiado, estalos), `Client/Teclas.cs` (tecla).
- **Não provado por robô:** o som de verdade e dois clientes reais (a bancada é muda; a segunda ponta foi forjada).

**Teste (dois clientes, fone estéreo):** os dois marcam "usar o microfone" na pausa; os dois ligam o scouter na
mesma frequência; A segura O — B ouve só no ouvido esquerdo, de qualquer ponto do planeta; B troca de frequência ou
desliga o scouter — silêncio.

## 3. Economia do recurso

- **Recurso** é um segundo saldo, ao lado do zeni (Stats, aba Tech, modo de construir). Tudo o que custa N zeni pode
  ser feito com 2N recursos.
- **Reserva do planeta:** lado² × 2 (Terra 500 mil; procedurais de 74 mil a 2 milhões). Enche do zero em 24 h, mesmo
  com o servidor desligado; persiste (`reservas.json`). Planeta destruído não tem; restaurado volta cheio. Lookout,
  além, interiores e espaço não têm reserva.
- **Minerar** (P → Other → "Treino e estudo"): trava o corpo como a meditação; andar ou agir interrompe. Mãos 3/s,
  pá 6/s, furadeira 12/s — basta a ferramenta estar na mochila. Uma faixa acima do chat mostra ganho, saldo e a
  reserva do planeta.
- **Esmagado não minera** (decisão sua), com a frase certa: "a gravidade daqui está te esmagando (2x contra 1x que
  você domina)".
- **Aba Tech:** cada oferta mostra "X zeni ou 2X recursos", com os botões "Construir" e "Com recursos".
- **Space Pod tech 999:** no original, tech 999 marca objeto de mapa que nunca aparece na janela; o extrator do port
  emitia tudo. Saíram `Spacepod_Placed`, `Boat_Placed` e `Central_Computer`. O Spacepod e o barco de verdade continuam.
- **Cavar removido** (decisão sua): pá e furadeira não têm mais ação de usar; a descrição diz que aceleram o Minerar.
- **De quebra:** zeni ganho "por fora" (missão, banco, torneio, desejo) agora atualiza na tela sozinho.
- **Também aceitam recursos, pelo dobro (decisão sua):** as melhorias da máquina de gravidade, do regenerador e da
  nave (degrau de velocidade e recondicionar o foguete), a conversão em androide e a estátua SUPREMA. Nas máquinas:
  E → "Melhorar…" (o chat lista o preço de cada melhoria nas duas moedas e o seu saldo) → a melhoria → "pagar com
  zeni" / "pagar com recursos". Na nave há um botão gêmeo "(com recursos)". No androide e na SUPREMA o preço vem
  escrito no botão nas duas moedas. Sem saldo numa moeda, recusa dizendo os dois preços e nunca completa com a outra.
- **O reviver do Enma continua só em zeni** (1.000.000), com checagem na bancada.
- **Constantes:** `Core/Economia/Recursos.cs`.

| Item | Zeni | Recursos |
|---|---|---|
| Gravidade: força / bateria / alcance | 50 / 50 / 500 a 5.000 | o dobro |
| Gravidade: estabilização | 500.000 | 1.000.000 |
| Regenerador: recarregar / bateria / velocidade | 250 / 500 / 1.000 (× o nível) | o dobro |
| Regenerador: tratar ferimentos | 5.000 | 10.000 |
| Nave: degrau de velocidade | 1.000 × a velocidade atual | 2.000 × |
| Foguete: recondicionar | 100.000 | 200.000 |
| Androide de Absorção / de Energia Infinita | 1.000.000 / 2.000.000 | 2.000.000 / 4.000.000 |
| Estátua + desejo SUPREMO | 2.000.000 | 4.000.000 |
| Enma (reviver) | 1.000.000 | não aceita |

| Fonte | Recursos por hora |
|---|---|
| Minerar com as mãos / pá / furadeira | 10.800 / 21.600 / 43.200 |
| Drill nível 1 / nível 10 (passiva) | 3.600 / 36.000 |
| Rocha de minério | 40 a 160 por rocha |
| Árvore derrubada | 5 a 12 por árvore |

## 4. Drill

- Instalada num planeta, tira da reserva sozinha — com você offline, com a zona vazia e depois de reiniciar o
  servidor (a conta é por tempo decorrido). Guarda num estoque de até 6 horas de produção.
- **Tecla E:** nível, taxa, estoque, reserva do planeta; **Coletar** e **Melhorar** (zeni ou recursos pelo dobro).
- Níveis 1 a 10, 1 recurso/s por nível; subir custa 25.000 zeni × o nível atual.
- Várias Drills no mesmo planeta dividem a mesma reserva; planeta seco, ela para e volta sozinha.
- "Pegar" a máquina não perde nada: o estoque vai pro saldo e o nível volta na próxima que você instalar.
- No Lookout instala e avisa que ali não há o que extrair.
- **Constantes:** `Core/Tech/Drill.cs`.

## 5. Rochas

- **Rochas grandes sem colisão — causa:** o gerador pedia um minério pequeno chamado "White rock"; o catálogo de
  tiles ignora maiúsculas e devolvia a arte grande "white rock" (250 px), desenhada sobre uma célula livre. Agora
  ela barra na base, dá pra passar por trás do topo, não quebra, e voando alto se passa por cima. Ninguém nasce
  preso; personagem salvo em cima de pedra é posto ao lado.
- **Rochas de minério:** nascem em todo planeta com reserva, em campo aberto, fora da vista de quem está perto.
  Quantidade = teto (terra navegável ÷ 400: Terra 316, Namek 135, Vegeta 379) × fração da reserva. Zerou a reserva,
  param de nascer e as que restam viram pó sem render. Tecla E → "Quebrar e coletar": 40 a 160 recursos tirados da
  reserva. O teto inteiro se repõe em 60 minutos.
- **Constantes:** `Core/World/RochaGrande.cs`, `Core/Economia/RochaDeMinerio.cs`.

## 6. Planetas procedurais

- **Blocos de madeira — causa:** eram as montanhas. O gerador pintava toda cordilheira com o tile da janela de madeira
  das casas (`Walls/Wall1`) em vez do penhasco. Planetas do catálogo não tinham o defeito.
- **Macieira garantida:** uma a 2–6 tiles do pouso e mais uma por setor de 128×128 (5 num planeta pequeno, 65 num de
  lado 1000). Dão maçã pela tecla E; derrubadas, voltam em 5 minutos.
- **Constantes:** `Core/World/Pomar.cs`.

## 7. Árvore no soco

- Soco de jogador fora de combate derruba a árvore em 1 a 5 golpes (conforme a força) e rende 5 a 12 recursos. Não
  tira da reserva do planeta.
- **Não paga:** ki, NPC, quem trocou golpe com alguém nos últimos 90 s, e quem está sendo esmagado pela gravidade ou
  pelo peso de treino (alinhado com a sua decisão do Minerar; é a constante `EsmagadoContaComoBriga`).
- **Toco:** fica no lugar até a árvore rebrotar (8 a 12 minutos). É derivado da arte da própria árvore, sem imagem
  nova. Construiu em cima, ela renasce numa clareira perto.
- **Novato agora consegue:** antes árvore caía pela regra de parede (BP 20).
- Árvores pequenas de Icer e Namek também contam. Macieira não rende recurso e não é imune a soco (decisão sua).
- **Consertos no caminho:** cenário caído nunca voltava; máquina assentada em cima de toco ou de parede derrubada
  não barrava o corpo.
- **Constantes:** `Core/Economia/Lenha.cs`.

## 8. Animais e carne

| Mansos | BP | Bravos | BP |
|---|---|---|---|
| Galinha | 10 | Morcego | 10 |
| Sapo | 10 | Lobo | 50 |
| Ovelha | 15 | Dinossauro | 80 |
| Tartaruga | 20 | Leão | 100 |
| Vaca | 25 | | |

- Mansos pastam e fogem de quem bate. Bravos atacam quem chega a 6 tiles e desistem longe da toca. Só corpo a
  corpo; sem teleporte, ki ou voo. Derrubou o jogador, dá 30 s de trégua.
- De 6 a 16 por planeta, nascendo fora da tela e longe de cidade; um abatido volta a cada 90 s.
- **Namek, Icer e Arlia têm fauna cheia; Makyo Star não tem nenhuma** (decisão sua; lista `PlanetasSemFauna`).
- Bicho derrotado morre e deixa carcaça (4 minutos). Tecla E → "Pegar Carne" (1 a 3). Comer dá +30 de nutrição
  (maçã: 6).
- Matar bicho não mexe em karma, reputação nem Zenkai; apanhar de bicho não treina.
- **Constantes:** `Core/Npc/Animais.cs`; espécies em `Assets/Data/npcs.json`. `--semfauna` desliga no servidor.

## 9. Tela de conhecidos (People)

- **Saiu a afinidade** (os pontos que o jogo deduzia), e com ela o ódio automático, o pedido de amizade e o
  "Declare Rival". Ficam **convívio** (o jogo conta, só sobe) e **declaração** (você escolhe).
- **Cartão por pessoa:** retrato, barra de convívio, "Essa pessoa é pra você:" com as nove opções (as que o convívio
  não paga ficam apagadas, com o preço) e uma linha dizendo o que a escolha faz.
- A raiva por ver um amigo cair, a fúria por vê-lo morrer e "duelo entre amigos não enfurece" leem a declaração
  (os dois degraus estão na seção 24; cada cartão diz qual é qual).
- **Detalhes:** clicar no retrato ou no botão abre o cartão. Com 20 de convívio aparecem idade, gênero, cargo, há
  quanto tempo se conhecem, visto por último e a **história** (a que o jogador escreve na criação — já existia no
  save e ninguém conseguia ler). Abaixo de 20 o servidor não manda nada. BP, atributos, localização e itens ficam fora.
- Save antigo carrega: os pontos positivos de afinidade viram convívio uma vez, com aviso.
- **Constantes:** `Core/Social/Convivio.cs`.

## 10. Radar sem radar e verbs por requisito

- **"Sinal dourado" — causa:** três defeitos somados. A frase era mandada a todos, sem conferir o aparelho; a
  distância comparava o pixel do mapa com a posição no espaço (quem pisava em qualquer planeta lia como se estivesse
  na célula central do universo); e o aviso nunca era reenviado ao ganhar ou perder o item. Sem Dragon Radar o
  servidor nem calcula.
- **Verbs:** uma tabela única (`Core/Verbos/RequisitosDosVerbos.cs`) diz o que cada verb exige; abas, busca, teclas
  e o menu da tecla E filtram por ela. Mudar quem vê um verb é uma linha. Somem pra quem não tem: Clã do Dragão,
  Split Form, fusão Namekuseijin, Dança da Fusão, coletores de androide, Absorver, Aceitar/Recusar sem oferta.
- O Mestre Korin ganhou "Time Chamber: Authorize" (o servidor já aceitava).
- **Falha de segurança consertada:** a absorção do bio-androide não tinha portão no servidor.

## 11. Menu de desejos do dragão

- Abre sozinho ao invocar, à direita: quantos pedidos restam, quanto o dragão espera, e a lista (os que não dá pra
  pedir ficam apagados, com o motivo).
- Desejo com alvo abre "pra quem?": "Para mim" ou a lista de quem o servidor aceita; o alvo marcado sobe destacado.
- Vale pro Shenron/Porunga e pro Super Shenron. A caixa de "id do desejo" saiu. "Ver os desejos" reabre.
- O Super Shenron só é chamado ao lado de uma Super Esfera, no espaço (regra das validações, seção 18); longe
  dela a janela apaga os desejos e diz por quê.
- **Bug sério consertado:** a contagem de pedidos zerava a cada segundo com o dragão de pé — desejos ilimitados.
- O disco do radar sai enquanto a janela está aberta (em 720p ela o cobria).

## 12. Construção de base

- Piso 5, parede 10, porta 50 zeni (o dobro em recursos). `Core/World/PrecoDeBloco.cs`.
- Seletor "PAGAR COM [Zeni] [Recursos]" no canto inferior direito do modo de construir; a escolha fica salva.
- O servidor cobra peça por peça; sem saldo naquela moeda não sobe e não completa com a outra. Bases antigas não
  são cobradas. Desmanchar não devolve (constante `DevolveAoDesmanchar`).

## 13. Bug da gravidade

- **Causa:** a gravidade é escrita quando o corpo troca de zona, e esse ponto lia só o `planetas.json`. Planeta
  procedural não tem linha lá e caía em 1x; o único remendo ficava no pouso vindo de órbita. O "Goto Spawn" com o
  mundo já carregado pega um atalho sem pouso. Não era só o mostrador: treino e esmagamento estavam em 1x.
- O conserto ficou no ponto único; login, reviver, Bring/Go To, nave, fusão e transe saem certos juntos.
- **Dois defeitos da mesma família, consertados:** logar num planeta procedural depois de reiniciar o servidor dava
  um mundo sem colisão e sem "Decolar"; e quem perdeu o planeta natal não era aclimatado ao refúgio.
- **Atenção:** Jarginhos e Kentucky Fried Chicken (maestria 1) logam esmagados em Verdejante-113 (4x). Use "Send
  Target To Spawn" neles.

## 14. Mapa do mundo e radar circular

- **Aba World:** cartão "Mapa" com o mundo em miniatura nas cores reais, marcador de onde você está e as linhas
  Mundo, Tamanho e Coordenadas. Clique alterna mundo inteiro ↔ vista de perto. No espaço: "SEM MAPA AQUI" + coordenadas.
- **Radar:** com o Dragon Radar na mochila, disco no canto inferior direito com uma bolinha amarela por esfera
  acordada daquele mundo; além do alcance, presa na borda. Clique troca 50 / 150 / 500 tiles. Sem radar o servidor
  não manda nada.
- O verb "Radar" media a esfera carregada do ponto em que foi pega — consertado.
- Se o tileset mudar, regerar as cores:
  `dotnet run --project Tools/AssetPipeline -- cores Assets/Data/tiles.json . Assets/Data/tiles-cores.json`.

## 15. Interface (segunda leva)

Bancada `djanelas` 121 OK (15 defeitos injetados pegos) · `djanelash` 78 OK · `opcoes` 55/0.

- **Menu P como janela:** arrasta pelo título, redimensiona pelas bordas e cantos (acendem ao passar o mouse), nunca
  sai da tela; as abas se reacomodam do mínimo (520×320) à tela inteira.
- **Menu P destacado:** botão "Destacar" no cabeçalho. Vira uma janela do Windows que vai pro segundo monitor, fica
  aberta e atualizando enquanto você joga, e lembra monitor, posição e tamanho (monitor sumiu → volta pro
  principal). "Encaixar" devolve pra dentro do jogo. P abre e fecha nas duas janelas; o X fecha o menu.
- **Teclado com o menu destacado:** o que se segura (andar, guarda, carga) funciona com o foco na janela do menu; o
  que se aperta (P, TAB, E, I, B, atalhos) é repassado ao jogo; Enter abre o chat e devolve o foco ao jogo. Digitando
  na busca do menu, a tecla é só da busca.
- **Chat:** redimensiona pela borda de cima e pela direita (300×180 até 58% da largura da tela).
- **Painéis do topo:** status e bonecos de 70% a 160%, pelo canto, com a letra redesenhada.
- **Opções (Esc → Interface):** um deslizador por peça, "menu (P) numa janela separada", "travar as bordas" e
  "Restaurar a interface padrão". Fica salvo no `config.json` em frações da tela; valores absurdos são travados.
- **Quem não mexe em nada vê a tela como antes** (só o botão "Destacar" a mais).
- **Consertos no caminho:** a dica "[E] Bank" aparecia por cima do rodapé do menu P; o canto "Assistir torneio"
  ficava 46 px por dentro do chat.
- **Constantes:** `Client/Janelas.cs`.

**O que robô não prova (teste de mão):**
1. P → arrastar pelo nome; puxar bordas e cantos.
2. "Destacar" → arrastar a janela pro segundo monitor, redimensionar, maximizar. Sair e entrar no jogo: P reabre lá.
3. Com o foco na janela do menu: WASD anda, P fecha, TAB/I/E funcionam, Enter abre o chat e o foco pula pro jogo.
4. Alt-tab; minimizar o jogo (o menu some junto); jogo em tela cheia com o menu no mesmo monitor.
5. Desligar o segundo monitor com o jogo fechado e abrir: o menu nasce no principal.
6. Monitores com escala (DPI) diferente.

---

# Terceira leva (noite de 10/10 e madrugada de 11/10)

O que você pediu depois do primeiro relatório: o despertar do God Ki, os marcos de ganho das formas, as validações
do servidor, a limpeza do catálogo, o versionamento, a abertura com o título, a aba de mestre e alunos, os debuffs de
desmembramento, a raiva por amigo, a parada do servidor, o kit do Deus da Destruição e os NPCs Bills e Whis.

## 16. God Ki: como desperta

Antes de hoje nenhum código do jogo ligava o ki divino (só as bancadas): formas divinas e o desafio ao Deus da
Destruição eram inalcançáveis.

- **Requisito pessoal:** BP **base** de 250 milhões × um fator fixo do personagem entre 0,9 e 1,3 (225 a 325
  milhões, em cinco valores). Sai da semente do personagem, a mesma dos limiares de Super Saiyajin; personagem antigo
  já tem o dele, sem conserto de save. Forma, raiva e Kaio-ken não contam.
- **Abaixo do requisito nada acumula.** Acima dele, um medidor de 0 a 100 (salvo no personagem, não zera) enche
  pelos dois caminhos que você descreveu:
  - **Atmosfera divina:** 600 s dentro de um mundo de atmosfera divina.
  - **Luta:** +0,4 por golpe dado e +0,6 por golpe recebido contra quem tem ki divino ou está em forma divina, só
    se o outro revidar em 10 s, com teto de 10 pontos por minuto (apanhar parado de um deus não desperta ninguém).
- **Onde ficam os mundos:** 1 em cada 100 planetas sorteados, com garantia de pelo menos 2 nas 17×17 células em
  volta da Terra (num universo típico são uns 10; o mais perto fica a 2,7 células), mais o `God_Realm`. Têm halo
  dourado no espaço e na carta estelar, e a aba World ganha o cartão "Ki divino" com o medidor.
- **O que o despertar dá** (o `get_godki()` do original): o ki divino, o marco de ganho "godki" (×5) e ×2,5 de poder
  permanente; Kai e Demônio saem com 33% de maestria.
- **Acréscimos:** o motor da maestria divina (16 h de uso da forma, o dobro em combate) e dois verbs de admin,
  "Awaken God Ki" e "Ir Para O Mundo Divino".
- **Constantes:** `Core/Forms/DespertarDivino.cs` (requisito, tempos, pontos por golpe) e
  `Core/World/AtmosferaDivina.cs` (raridade, garantia perto da Terra).
- **Bancadas:** `godki` 104/0 · `dgodki` 28 OK · `dgodkij` (janela, com fotos).

**Diferença em relação ao DM, declarada no código:** lá quem tem o BP desperta na hora com qualquer gota de energia
divina, e as fontes eram a fenda, o ritual dos seis e a forma Golden. Aqui são as duas fontes que você pediu e o
despertar leva tempo.

## 17. Marcos de ganho das formas

Os marcos que multiplicam o ganho de BP para sempre (o "MARCO DE PODER") existiam na tabela, mas nada os concedia.

- Agora cada forma concede o dela **na estreia**, e quem já tinha estreado a forma recebe no login (retroativo).
- **USSJ saiu.** Na escada do Super Saiyajin: SSJ ×2, **Grau 2 ×2,25**, **Grau 3 ×2,5**, SSJ2 ×3 — a distribuição
  que você aprovou.
- 22 formas têm marco. O do Blue vale para Blue, Blue Evolution, Rosé e Rosé 2. O ×2 da forma final do Frost Demon
  ficou na sétima forma.
- **Só jogador.** NPC nasce com a escada estreada e não ganha marco.
- **Não ligados:** `majin2`/`majin3` (a saga não está portada), o Golden do Frost Demon (não portado) e o `lssj4`
  (nem o original concede).
- **Constantes:** `Core/Stats/Milestones.cs` (os valores) e `FormaDef.Marco` em `Core/Forms/Formas.cs`.
- **Bancada:** `marcos` 64/0.

**Correção de uma coisa que eu disse:** os marcos de Ascensão (asc5/10/20) **não** são concedidos — nada no jogo
escreve o `BPBoost` de que eles dependem. Humano, Namekuseijin, Heran, Alien e Kai hoje só sobem o ganho pelo
`potential` e pelo marco do ki divino. Está nas perguntas.

## 18. Validações do servidor

Três rodadas. Tudo com bancada que primeiro falha (o pacote forjado passa) e depois passa.

**Rodada 1 — as sete do relatório anterior e uma varredura das quatro classes de furo**
- Fechadas: `splitform_alvo`, `cargo_nomear`, `cargo_herdeiro`, `regenerar`, `sdb_invocar`, `trn_recusar`,
  `mst_ensinar`. O `decolar` só responde com flag de bancada (o botão já tinha saído).
- A varredura achou e fechou mais onze grupos (uns 17 verbs). Os mais graves:
  - absorver o próprio reflexo ou a própria cópia (fábrica de poder);
  - `db_refazer` com o dragão de pé (pedidos sem fim);
  - `NaN`/`Infinity` aceitos em cinco verbs;
  - o canal 200 do chat (dava para forjar um recado "do servidor");
  - `skill_esquecer` de skill dada por cargo (marcos sem fim);
  - cargo reivindicado nocauteado; Hakai e ensino com o autor caído;
  - `sala_autorizar`, Restore Youth e Keep Body sem conferir alcance.
- Tudo passa por quatro perguntas num arquivo só: `Server/GameServer.Validacoes.cs`.

**Rodada 2 — os oito de risco alto**
- **Treinar e meditar** agora são conferidos no servidor (`Core/Stats/TreinoEMeditacao.cs`,
  `Server/GameServer.Atividade.cs`): não treina andando, lutando, nocauteado nem agarrado; briga nos últimos 10 s
  interrompe; há uma carência de 500 ms entre trocas.
- **Teleportes não furam mais prisão** (Sala do Tempo, interior do Majin): Chrono Trigger, Instant Transmission,
  salto de planeta, G8 e o aceite de duelo passam por uma pergunta só (`PorQueNaoSaiDaqui`).
- Atordoado não atira. Zanzoken e o avanço do Grau 3 respeitam parede em planeta gerado. O Bio-Android Lab confere
  o dono. `forma <id>` não obedece a quem está possuído.
- **`sala_soltar` só funciona com o guardião no Templo**, como você mandou.
- A bancada `sala` tinha 4 falhas antigas; a causa era o palco dela, consertado: 170/0.

**Rodada 3 — o que você liberou depois ("pode fechar os dois, e a investida do soco etc tb pode resolver")**
- **Reembolso só do que foi comprado.** O personagem passa a guardar a lista das skills que comprou. Herdada pela
  absorção Namekuseijin, dada por admin, concedida por degrau de nível, sobra de kit de cargo perdido e emprestada
  por fusão continuam esquecíveis, mas não devolvem marco; o botão diz "Esquecer · não devolve marco". Save antigo
  carrega: sem a lista, tudo o que ele tem (e ninguém ensinou) conta como comprado.
- **Preso da Sala não sai carregado.** Preso não funde pela Dança nem pela Potara (nem convidando) e nenhum Majin o
  absorve; quem já carrega um preso é barrado na porta e em todo teleporte. Fusão Namekuseijin e Majin preso que
  absorve não mudaram.
- **Parede em planeta gerado:** a investida do soco, o puxão da fusão, o embate do Zanzoken e o empurrão passam pelo
  mesmo funil que conhece base, montanha, árvore e rocha. Achado junto: o passo curto do soco nem acontecia em
  planeta gerado; agora acontece. Uma vigia na bancada reprova quem voltar a ler a parede direto do arquivo.
- **Versionamento:** o pacote de skills cresceu, e isso entra na impressão digital do protocolo — builds
  desencontrados se recusam na porta. O número da versão não foi alterado.

**Ficou de fora (achado, não mexido):**
- fundir ou ser absorvido durante a janela de 2 minutos da Sala tira o corpo da contagem dela (a tranca não fecha
  nele; risco baixo);
- skill de kit que o personagem tinha comprado antes de ganhar o cargo não pode ser esquecida enquanto ele tiver o
  cargo.

**Bancadas:** `validacoes` 226/0 (62 defeitos injetados) · `atividade` 18 OK · `sala` 170/0 · e as vizinhas
`torneio`, `mestre`, `mestrevivo`, `portas`, `missoes`, `fusao2`, `cenafusao`.

## 19. Catálogo de tecnologia e de skills

O extrator não entendia `/* */` do DM (lá o comentário de bloco **aninha**, e um `//` dentro do bloco esconde o
`*/`). Ganhou um leitor único (`Tools/AssetPipeline/DmFonte.cs`), conferido contra a árvore que o próprio compilador
do BYOND imprime: idêntica.

- **Saíram do catálogo:** as seis armas comentadas (Axe, Staff, Spear, Club, Hammer, Cross) e o PDA repetido. O
  `Staff` que ficou é o cajado mágico (tech 1). São 101 construções, sem id repetido.
- **Valores que o mesmo furo tinha estragado, agora os do original** (os que você escolheu): Books 10 zeni / tech
  10; Hand Drill tech 10; DNA Container 50.000 zeni / tech 50 / só Humano.
- **Também saíram**, com o seu "pode tirar": as três skills-fantasma do sistema velho de ataques customizados (363 →
  360 skills), o `GuidedBall` do King Of Acronia e a raça `Dog` (24 → 23).
- **Item aposentado:** quem tiver um na mochila perde o item no login com uma frase explicando. **Sem reembolso**,
  como você decidiu.
- Não entrou o estilo `Default` (você não aprovou) e os 17 turfs-fantasma ficaram como estão.
- **Bancadas:** `aposentados` 23/0 · `aposentadosvivo` 7/0 · `instalar` 248/0 · `fonte` 19/0 · `arvoresprova` ·
  `mundoprova`.

## 20. Versionamento

- **A versão aparece** no canto inferior direito do menu (`v0.1.0`), na pausa e no título da janela.
- **Ao conectar, os dois lados se conferem** antes do login: o cliente manda a versão no pedido de conexão e o
  servidor responde com a dele. Se não bate, o jogo volta para o menu com um aviso que diz a direção: o seu jogo está
  **atrás** ou **na frente** do servidor, e quais são as duas versões.
- Além do número, vai uma impressão digital do protocolo: dois jogos de mesmo número mas com pacotes diferentes
  também se recusam ("build diferente").
- Servidor antigo (de antes do versionamento) é reconhecido e avisado como tal.
- **Onde muda o número:** `Core/Versao.cs` (`Maior`, `Menor`, `Remendo`), com cópia em `project.godot`.
- **Bancadas:** `versao` 62/0 · `dversao` 63 OK · e seis de dois processos (`dupla_atras`, `dupla_frente`,
  `dupla_igual`, `dupla_build`, `dupla_antigo`, `dupla_cego`).

## 21. Abertura com o título

- **Ao abrir o jogo:** a tela nasce branca, o logo "Dragon Ball Jandirus" cai e bate (clarão, tremor, anel de
  choque, faíscas, brilho laranja), respira, sobe para o alto encolhendo, e o menu aparece por baixo. Dura cerca de
  2,9 s. É a mesma receita do trailer (`Trailer/bruto/t2/titulo_anim.py`), comparada quadro a quadro: a diferença
  média fica entre 0,6 e 2 em 255 por canal.
- **Pular:** qualquer tecla, clique, botão de controle ou toque vai direto para o menu. O gesto morre na abertura (o
  clique não aperta o botão que estava embaixo).
- **O título velho saiu** do menu (os dois textos e a linha). O logo fica no alto do menu principal e da seleção de
  personagem; na criação não coube.
- **Uma vez por execução.** Queda, "Desconectar" e sair da seleção voltam ao menu sem reprisar.
- **Desligar:** caixa "abertura animada ao abrir o jogo" nas opções, ou `--semabertura` na linha de comando.
  `--abertura` força (serve para filmar).
- **Nunca em bancada:** sem janela, com `--semfoco`, `--host`, `--connect` ou robô, ela não roda, e o log diz o
  motivo.
- **Sem engasgo, medido:** com o título em movimento o pior quadro foi de 11 a 17 ms (monitor de 120 Hz). O
  aquecimento dos efeitos, que antes rodava nesse momento, agora espera o título parar.
- **Som:** um baque no impacto (`landharder.ogg`, que já estava no jogo), no volume de Efeitos.
- **Arte:** `Assets/UI/Titulo.png` (cópia da arte do trailer) e `Assets/UI/TituloBrilho.png` (gerado por
  `Tools/brilho_do_titulo.py`). Se o logo mudar: copiar, rodar o script, importar.
- **Constantes:** `Client/AberturaDoJogo.cs` (respiro 1,55 s, subida 0,75 s, volume) e `Client/TituloDoJogo.cs`
  (tamanho do logo no alto).
- **Bancadas:** `dabertura` 97 OK · `dabertura_semflag` 37 OK · `dabertura_headless` 22 OK.

**O que robô não prova:** o som de verdade (a bancada roda muda) e o jogo exportado (não foi exportado; se a arte
não estiver importada o jogo abre sem abertura e sem logo, com o motivo no log).

**Custo declarado:** do duplo clique ao menu são uns 5 s em vez de 2,2 s; a diferença é pulável.

## 22. Aba de mestre e alunos

Aba própria do menu P, logo depois da People. Chama-se **"Alunos"** para quem guia alguém (e mostra também o mestre
dele, se tiver) e **"Mestre"** para quem só é guiado. Só existe enquanto houver o vínculo.

- **De onde vem o progresso:** o jogo não guardava quem ensinou o quê. Ganhou um diário por vínculo: cada tentativa
  de despertar uma forma com a ajuda do mestre, cada skill que o mestre passou e a data do vínculo. Só conta o que
  **este** mestre passou a **este** aluno enquanto era mestre dele.
- **O mestre vê:** um cartão por aluno, do mais adiantado para o menos — retrato, online/offline e "visto por
  último", "seu aluno há 12 dias", o progresso geral e cada ensinamento com barra (forma: ainda não despertou /
  despertou com você / dominada, com a maestria; skill: aprendeu / dominou / esqueceu, com o nível). Com dois alunos
  ou mais, um quadro "Quem está mais adiantado" por ensinamento. Botões "Ajudar a transformar" e "Dispensar aluno".
- **O aluno vê:** o cartão do mestre (o que ele ensinou, o próprio progresso e a posição em cada coisa, "2º de 3")
  e um cartão por colega com as duas barras lado a lado — a dele e a sua —, só nos ensinamentos em comum. Botão
  "Deixar meu mestre".
- **Sem BP, como você mandou.** Nenhum número de poder na aba nem no pacote (a bancada procura o número nos bytes).
  A única leitura de força é o "poder relativo N%" da aba Sense, pela mesma função e com as mesmas condições (ter a
  skill Sense e a pessoa estar a até 15 tiles). Com scouter ligado a aba continua sem número. Do colega só viajam o
  nome e o progresso nos ensinamentos em comum.
- **Vínculo antigo** (feito antes de hoje) aparece com o diário vazio e a frase "o diário deste vínculo começa
  agora"; não inventei autoria.
- **Constantes:** `Core/Skills/ProgressoDeAluno.cs` (receber um ensinamento vale metade do progresso; dominar, o
  resto).
- **Bancadas:** `alunos` 88/0 · `dalunos` 109 ok (28 defeitos injetados) · `abas` 426/0.

**Teste (servidor + 2 ou 3 clientes):** A convida B na aba Learning e B aceita — as abas novas aparecem sem reabrir o
menu. A usa "Ajudar a transformar" e "Ensinar: <skill>"; o cartão de B mostra a forma e a lição. Um terceiro vira
aluno de A: B e C se veem como colegas. B desloga: o cartão dele fica OFFLINE com o progresso congelado.

## 23. Desmembramento

O original ganhou as regras de membro perdido num commit só (29/06); o combate do port era anterior: arrancava o
membro e não cobrava nada.

| Ferimento | Efeito |
|---|---|
| Cada braço perdido | acerto ×(1 − 0,25 por braço), piso de 10% |
| Os dois braços | o soco recarrega no dobro do tempo |
| Uma perna | não corre |
| As duas pernas | não anda no chão (precisa voar) |
| Ferido (HP baixo) | o passo fica mais lento na proporção do HP (menos Androide e Majin) |
| Os quatro membros fora | não ergue a guarda |
| Rabo arrancado | o SSJ4 cai |
| Dois membros arrancados | conta para o Zenkai |

- NPC paga a pontaria e a guarda (sem membro nenhum, ninguém ergue a guarda); corrida, chão, recarga do soco e
  lentidão valem só para jogador.
- A aba Body ganhou o bloco "Efeitos dos ferimentos", que diz em palavras o que cada ferimento está custando.
- **Constantes:** `Core/Combat/DebuffsDeMembro.cs`.
- **Bancadas:** `membros` 103/0 (antes dos consertos: 57 OK / 23 falhas) · `kiponta` 128/0. Teste rápido de mão:
  `--feridateste`.
- **Não portado** (existe no original): equipamento por membro, `CanMate`, o aviso de guarda cedendo, o verb
  `Injure`, a recusa de **entrar** no SSJ4 sem rabo e o texto no boneco do HUD.

## 24. Raiva por amigo

**O que você pediu para verificar:** a morte e a absorção (bio e Majin) já davam o degrau mais alto, e a cinemática
com o tema do Gohan existia. Mas:
- a cinemática **não tocava** no caso mais comum (o amigo cai e depois morre: quem já estava com raiva pelo nocaute
  não ganhava a cena);
- nocaute dava o topo da raiva, igual à morte;
- o tema tocava para o planeta inteiro;
- a raiva não transformava sozinha (só abria a porta);
- meditar não acalmava.

**Os dois degraus**

| Gatilho | Degrau | Cinemática da raiva + tema | Ki |
|---|---|---|---|
| Amigo nocauteado | raiva comum | não | não mexe |
| Amigo morto ou absorvido (bio, Majin) | fúria extrema | sim, se não houver forma nova a estrear | salta para o teto de carga |

- A fusão Namekuseijin **não** dispara raiva.
- **Raiva comum** vale `100 + folga ÷ 1,30` (a extrema vale a folga inteira).
- **Salto de Ki:** só na fúria extrema e só para quem tem Ki Unlocked + Basic Ki Control 5. Vai para o máximo que o
  personagem consegue carregar (140% de fábrica) e nunca desce. O original não tem regra de raiva mexendo em Ki; o
  critério de "certo nível de ki control" é meu e mora numa função só.
- **Sem aura vermelha:** a cena da fúria perdeu a chama vermelha do original; ficam as ondas de choque (na cor do ki
  do personagem), a poeira, o tremor, a cratera, os sons, o tema e a linha no chat. Quem saltou acima de 100% mostra a
  aura normal de Ki; quem não tem controle de ki não ganha aura.
- **Meditar acalma** os dois degraus na hora. Como no original, não devolve o Ki, não tira a forma e não cala o tema.
- Cena e tema vão para o enfurecido e para quem está a até 22 tiles.

**A raiva transforma sozinha (na estreia), e cada raiva tem o seu preço**

| Forma | Fúria extrema | Raiva comum |
|---|---|---|
| SSJ1, SSJ2, Future | o requisito normal | ×1,2 |
| Heran (1ª / 2ª) | o requisito normal | ×1,3 / ×1,2 |
| Legendary (Wrathful, C-Type, Full Power) | o requisito normal | ×1,5 |
| Legendary Primal (três formas) | o requisito normal | ×1,2 / ×1,2 / ×1,3 |
| Beast | abre | não abre |

- Calmo, ninguém desperta. Os números são os do original (`supersaiyan.dm`, `heran.dm`).
- **Estreia:** o personagem se transforma sem apertar nada, com a cinemática **da transformação** (não a da raiva),
  e ganha o marco da forma. Na raiva comum o Ki vai a 100%, não a 140%.
- **Forma nova acima de uma já despertada** (requisitos do SSJ2 estando na base): sobe sozinho pelos degraus que já
  são dele, sem cena, e estreia a nova.
- **Nada novo a estrear:** toca a cinemática da raiva (só na fúria extrema) e o personagem fica na forma em que está.
- A transformação ajudada pelo mestre não paga o preço maior.
- A tecla C explica o que falta, sem número: "…quase vem — mas a raiva que você sente pede mais poder do que você
  tem. Só uma dor maior o traria agora."
- A aba People separa os dois degraus em cada cartão.
- **Constantes:** `Core/Combat/RaivaEmDegraus.cs` (os degraus, o salto de Ki) e `Core/Forms/Formas.cs` (os preços:
  `AgioComumDoSaiyajin`, `AgioComumDoLegendary`…).
- **Bancadas:** `raiva` 109/0 (antes de tudo: 30 OK / 22 falhas) · `draiva` 59/0 · `raivapura` 697/0.

**Onde o original difere do que ficou** (está nas perguntas): no original a raiva comum não abre o Beast; o
Legendary paga mais que o Saiyajin comum na raiva comum (o desconto antigo do port deixou de existir); e ver um amigo
nocauteado já dá o degrau mais alto.

**Não portado:** a "inspiração" (quem conviveu com um Super Saiyajin pode despertar na raiva comum sem o preço
maior), os humores intermediários do original e a regra de 80% do limiar na fúria extrema.

## 25. Parada do servidor, admin automático e saída no meio do embate

Começou num achado do agente da abertura e cresceu com as suas respostas.

- **O defeito original:** o servidor que parava não esquecia quem estava na tela de seleção. Anfitrião clicava em
  "Desconectar" com um amigo na seleção, hospedava de novo sem fechar o jogo, e o amigo ficava em "conectando…" para
  sempre.
- **A parada agora derruba cada jogador** pelo caminho normal de saída antes de fechar a rede; quem grava é esse
  caminho, uma vez por personagem.
- **Depois de parar e subir no mesmo processo:** os NPCs estão de volta na hora (antes: até 5 min de mundo vazio), o
  admin do anfitrião vale, a Sala do Tempo zera a lotação.
- **Fundidos** são separados e gravados, **sem** a recarga de 1 hora quando é o servidor que para (no logout normal o
  preço continua).
- **Torneio em andamento é cancelado** na parada: ninguém perde por W.O.
- **Fechar o servidor dedicado grava tudo:** o X da janela, e também Ctrl+C e fechar o console no servidor sem
  janela. Uma corrida de bancada continua sem gravar (critério: `--server` só com argumentos de produção).
- **Castigo de quem abandona não cai em quem foi derrubado pela parada.** O único que existia era o golpe final do
  embate de Zanzoken.

**Admin automático (furo de segurança fechado).** Quando uma segunda conta local entrava, o servidor desligava o admin
automático — mas quem disparava o desligamento entrava como admin, e o dono perdia. Agora:
- é admin quem entra da máquina do servidor **enquanto for a única conta local em jogo**;
- com duas ao mesmo tempo, ninguém é admin pelo endereço até o servidor reiniciar;
- trocar de conta não desliga;
- o dono continua admin pela marca na própria conta.

**Sair no meio de um embate de Zanzoken.** O personagem era gravado invisível, com o poder preso em 5, e o golpe
final acontecia embora o menu dissesse que não. Agora, como no original, sair desfaz o embate sem vencedor, e o login
conserta quem já estava gravado assim.

- **Bancadas:** `parada` 68/0 · `adminlocal` 26/0 · `adminlocal_chave` 6/0 · `saidaembate` 13/0.

**O que robô não prova:** o Ctrl+C de verdade no `servidor.bat`. Teste: abrir o `servidor.bat`, entrar com um
personagem, dar Ctrl+C e ver `[server] fechando: N personagem(ns)...` antes de sair.

**O que continua aberto:** um estranho que entra **sozinho** por túnel, com o dono fora do jogo, ainda vira admin;
hoje só `--sem-admin-local` cobre. Matar o servidor pelo Gerenciador de Tarefas, queda de energia e crash continuam
cobertos só pelo save de 2 minutos.

## 26. Kit do Deus da Destruição e passivas de Ultra Instinto / Ultra Ego

Você pediu para conferir se as passivas do Deus da Destruição e a forma Fury estavam portadas, e portar o que
faltasse.

**Deus da Destruição — antes só o Hakai (como disciplina) e respirar no vácuo existiam.** O resto era texto na
descrição do cargo. Agora existe:

| Habilidade | O que faz |
|---|---|
| **Fury** (forma) | poder ×72, aura roxa colada, sem dreno e sem maestria; atravessa nocaute e morte; cai em até 1 s se perder o título; recusa com outra forma de pé |
| **Hakai** | agarrando alguém com o torso abaixo de 15% e ao menos 25% mais fraco: executa. O Enma cobra o dobro para reviver |
| **Power Flick** | solta o agarrado, fere o torso com dois golpes pesados e arremessa |
| **Destruction God's Fury** (tiro) | mira o torso e derruba a guarda de quem bloqueia |
| **Destroyer Sphere** | Hakai à distância, no contato |
| **Energy Nullification** | apaga os ataques de ki mais fracos em volta; recarga de 5 min, só gasta se anulou algo |
| **God's Temper** | dez danos sem bloquear → +10% de ataque e defesa física por 10 s; recarga de 30 s |

- **Caminhos:** o Deus do caminho da Destruição tem o kit todo ×1,25; o do caminho do Instinto pega a aura e a
  infusão emprestadas a 25%.
- **Ganhar e perder o título** aplicam e tiram tudo na hora, com as frases do original; o login lembra a tarefa
  pendente.
- Os oito botões aparecem na aba Skills só para quem é o Deus, e o servidor revalida tudo. As habilidades também
  servem a um NPC (é o que o Bills usa).

**Ultra Instinto e Ultra Ego — estavam portados, mas com erros.** Corrigidos pelo original:
- as formas abriam com 1 e 100 de maestria; o certo é 20 e 60;
- o dreno de Ki das formas era de 1,6% e 2,08% por segundo; o certo é 0,35% e 0,5%;
- a esquiva de **ki** (30%) não existia; só a de soco;
- a passiva só valia com o botão ligado; no original vale também transformado;
- faltavam os portões da esquiva (meditando, treinando, de guarda, carregando ki abaixo de 40% de maestria);
- o Godly Display era outra coisa; virou o avanço de 10 tiles e os 10 golpes em rodízio do original;
- o Unbound Ego entrava duas vezes na conta do poder;
- a redução de dano, o recuo, o ki devorado e a explosão da aura não levavam o fator do caminho.

- **Divergências declaradas:** o God's Temper conta todo dano de outro corpo (no original o gancho só existe num dos
  caminhos de dano); a esquiva do Instinto custa Ki no port (no original é de graça); a cena de estreia da Fury é
  invenção (no original é instantânea); os dois tiros usam a arte de ki comum, sem a tinta fixa.
- **Não portado:** o pulso da Hakai Infusion na disputa de raios (o embate de ki do port é outro sistema), a aura
  e o cabelo roxos do Deus pelo rank no ki divino, e "não envelhece" (o port não tem calendário de idade).
- **Constantes:** `Core/Ranks/KitDoDeus.cs` e `Core/Forms/Disciplinas.cs`.
- **Bancadas:** `godkit` 203/0 (12 defeitos injetados) · `pformas` 656/0 · `pdisc`.

## 27. Bills e Whis

Regra sua, sem equivalente no original. Tudo em `Core/Npc/BillsEWhis.cs` (regras, números e falas).

**Como o jogador vive**
1. **Majin Boo é vencido por jogadores.** O mundo lê um aviso, e a chegada fica marcada para 28 dias in-game depois
   (cerca de 11 horas reais). O prazo sobrevive a reinício.
2. **Bills e Whis pousam na Terra.** Bills veste o traje e cochila; quem bate nele o acorda. Ele luta só com quem
   bateu, com 15% do poder (150 bilhões de 1 trilhão), e só derruba. Sem ninguém de pé por 6 s, volta a cochilar
   inteiro. Fica na Terra sem prazo, até cair. O Whis é intocável.
3. **Derrotado**, fala sete linhas; uma delas dá as coordenadas galácticas, e o Whis repete com o nome do mundo. A
   carta estelar de todos passa a marcar a casa em roxo, com um botão na aba Nav. Quem não estava online recebe a
   notícia no primeiro login.
4. **A casa** é o mundo gerado mais pesado a até 8 células de sistema da Terra (77x a 80x nas rodadas). Os dois
   ficam parados e intocáveis.
5. **Tecla E no Whis → treinar.** Um contra um, no lugar, sem morte. O Whis tem o triplo do poder de quem o chamou
   (acompanha se o jogador se transformar), Ultra Instinto a 100% sem efeito visual, e a precisão dele não cai. Dura
   até 5 minutos; a tecla E oferece "Parar o treino" de qualquer distância. Quem já tem o Ultra Instinto ganha
   maestria 3× mais rápido ali.
6. **Tecla E no Bills → treinar** (igual, com a aura da Destruição a 100%; acelera o Ultra Ego) **ou desafiar pelo
   título.** O desafio exige ki divino desperto e segue as regras do duelo que já existia: nocaute decide, 10
   minutos, terceiro que se mete anula. Bills entra com 1 trilhão de base, na Fury e com o God's Temper; ele não usa
   as habilidades ativas do kit (Hakai, tiros). Quem perde espera 1 dia in-game.
7. **Bills cai no desafio:** o vencedor é entronizado na hora, recebe o kit e o traje, e o Bills sai do mundo. O
   Whis fica.

- **Existem enquanto o cargo está vago** (Bills ↔ Deus da Destruição, Whis ↔ Anjo), cada um pelo seu. Se o cargo
  vagar de novo, ele volta.
- **O traje** (`Beerus Inspired Suit (DU)`) entra na mochila de quem ganha o título por qualquer caminho, uma vez por
  personagem; quem já é o Deus recebe no login.
- **Sem BP na tela:** nenhuma fala, menu ou pacote novo leva número de poder (a bancada procura nos bytes).
- **Falas:** mais de 80, escritas para o jogo no tom dos dois.
- **Bancadas:** `bills` 225/0 (20 defeitos injetados) · `dbills` 48/0.

**Teste de mão** (menu P → Admin → cartão "Bills e Whis"): "Avançar a história" três vezes leva do despertar ao
convite; "Aguentar a casa" sobe a sua maestria de gravidade (sem isso os ~80x esmagam); "Ir para a casa" leva à
órbita. No servidor, `--billsdias N` troca o prazo de 28 dias.

**O que robô não mede:** a graça da luta (o mestre usa a IA de combate de todo NPC) e dois clientes reais.

---

## 28. Decisões que você já deu

**Primeira e segunda levas**
- Namek, Icer e Arlia: sem macieira, só animais. Makyo Star: nada.
- Lenha 5–12 e rebrota 8–12 min: bom. Macieira com dono não é imune.
- Gravidade acima do limite conta como combate (não minera; alinhei a árvore).
- Quer toco. Tirar o cavar.
- Melhorias da máquina de gravidade, do regenerador e da nave, conversão em androide e estátua SUPREMA aceitam
  recursos (o dobro). O reviver do Enma continua só em zeni.

**Terceira leva**
- God Ki desperta por atmosfera divina em planetas raros ou lutando com quem tem ki divino, só acima do requisito
  pessoal.
- Marcos de forma ligados. USSJ saiu: SSJ ×2, Grau 2 ×2,25, Grau 3 ×2,5, SSJ2 ×3.
- Validações: resolver todas; o guardião tem de estar no Templo para soltar da Sala; reembolso só do que foi
  comprado; preso não sai carregado; investida do soco respeita parede.
- Catálogo: valem os valores do original (Books 10 / tech 10, Hand Drill tech 10, DNA Container 50.000 / tech 50 /
  só Humano); item aposentado não reembolsa; saem as armas comentadas, as skills-fantasma, o `GuidedBall` e a raça
  `Dog`.
- Mestre e alunos: o mestre não vê BP (a regra principal do BP vale para toda tela).
- Raiva: nocaute = raiva comum; morte ou absorção = fúria extrema; salto de Ki só na extrema; a raiva transforma
  sozinha na estreia, com a cinemática da transformação; forma já despertada toca a cinemática da raiva; com os
  requisitos do SSJ2 na base, sobe pelo SSJ1 e estreia o SSJ2; fusão Namekuseijin não dispara; meditar acalma; sem
  aura vermelha; a raiva comum também transforma, por um preço maior.
- Parada do servidor: consertar o `Stop`; a parada derruba cada jogador; fechar o dedicado pelo X grava; torneio
  cancelado na parada; fusão perdoada na parada.
- Bills e Whis, e o que não estava portado do kit do Deus da Destruição: portar.

## 29. Perguntas abertas

**Economia**
1. Sem o cavar, zeni sem luta só vem de missão de cargo, tributo, desejo e torneio. Quer outra fonte?
2. Minerar dentro de prédio vale, ou só a céu aberto?
3. Os números (taxas de mineração, Drill, preços de peça) servem?
4. Nas máquinas de gravidade e no regenerador, os preços das melhorias saem no chat ao abrir "Melhorar…". Quer um
   painel dentro do menu, como o da Drill?

**Construção**
5. Reembolso ao desmanchar? Admin constrói de graça?

**Animais**
6. Novato de BP 1–3 não mata nem a galinha (BP 10). Quer uma presa mais fraca?
7. Espécies por planeta/bioma, ou todas em todo lugar como está? Arconia fica com fauna?
8. Entra mais algum bicho com arte pronta (urso, dinossauro e sapo de Namek, gato, cachorro, cobra…)?

**Rádio**
9. Atravessa do Lookout e das cavernas pro planeta-mãe? A tecla O serve? Frequência também na mochila?

**Conhecidos**
10. A classe fora do cartão está certa? 20 de convívio é o número? Idade e gênero atrás do convívio?
11. Quer uma tela pra reler ou editar a própria história?

**Esferas e dragão**
12. O servidor manda a posição de todas as esferas do chão da zona pra todo mundo (é como são desenhadas); um
    cliente adulterado saberia sem radar. Quer que eu feche?
13. Sobrou pedido, a próxima invocação começa com o resto. Fica assim ou começa cheia?
14. "Erguer Estátua" aparece pro Clã do Dragão em mundo que ele não domina (com a frase do que falta). Esconder?
15. Fusão Namekuseijin cobra a skill da árvore, como no original?
16. As Super Esferas não são desenhadas no espaço (o servidor manda a posição, o cliente não usa). Com a regra
    nova o Super Shenron só é chamado a até 2 tiles de uma delas: o jogador precisa ficar ao lado de algo
    invisível, guiado só pela frase do Dragon Radar. Quer que elas apareçam?

**Gravidade**
17. Refúgio aclimata como berço (como ficou), ou sorteia refúgio só entre mundos que o corpo aguenta?

**Estética**
18. A "terra batida" marrom embaixo do toco destoa na neve de Icer. Quer que siga o bioma?

**Interface**
19. O X da janela destacada fecha o menu e o "Encaixar" devolve. Prefere que o X encaixe?
20. A faixa de 70% a 160% dos painéis serve? A 70% a letra fica com 7 a 9 px.

**God Ki e marcos**
21. Requisito pessoal (225 a 325 milhões, como ficou) ou um número único de 250 milhões?
22. Quem não é Saiyajin desperta o ki divino mas não tem forma divina para treinar a maestria; com isso Ultra
    Instinto e o desafio ao Deus da Destruição continuam fora do alcance dele. Como ele deve treinar a maestria?
23. O Golden do Frost Demon (no original dava ki divino): portar?
24. Os tempos servem (600 s de atmosfera; luta +0,4 / +0,6 por golpe, teto de 10 por minuto)?
25. Ligar os marcos de Ascensão (asc5/10/20) para as raças sem escada de forma? Hoje nada os concede.
26. Quer uma linha na aba Stats com o multiplicador de ganho atual? Hoje só aparece no chat e no Assess.

**Catálogo**
27. `*_Placed` e `Central_Computer` (que saíram do catálogo na primeira leva) entram na lista de itens aposentados?

**Versionamento**
28. Igualdade estrita (como está) ou versões que só diferem no último número se aceitam?
29. 0.1.0 serve como número inicial? O pacote de skills cresceu depois: sobe para 0.1.1?

**Abertura**
30. Título também na criação de personagem (teria de ser menor)? O som e o volume servem? O respiro de 1,55 s e a
    subida de 0,75 s estão no gosto?

**Mestre e alunos**
31. Preencher o vínculo antigo a partir das formas já despertadas, assumindo o mestre atual?
32. Contador de treinos com o mestre? Do colega só nome e ensinamentos em comum? Receber um ensinamento vale metade
    do progresso?

**Desmembramento**
33. No original, sem as duas pernas e no chão o personagem também fica sem socar e atirar. Aqui ele só não anda.
    Quer igual?
34. No original o NPC refaz os membros quando sai da luta. Quer?
35. A lentidão de quem está ferido (com metade da vida, metade do passo) é do original e pesa. Mantém?

**Raiva**
36. Beast: no original a raiva comum não o abre (ficou assim). Confirma?
37. Legendary: com os números do original ele paga ×1,5 na raiva comum e perdeu o desconto antigo. Vale o original?
38. Nocaute: no original já dá o degrau mais alto (preço barato); aqui dá a raiva comum (preço maior). Confirma?
39. "Inspiração" (quem conviveu com um Super Saiyajin pode despertar na raiva comum sem o preço maior): portar?
40. Dupla estreia (SSJ1 e, quando a cena acaba, SSJ2 na mesma raiva): ok?
41. Ondas de choque da fúria na cor do ki do personagem: ok? A fusão e as cenas do bio continuam com o clarão
    vermelho: mantém?

**Parada do servidor e admin**
42. Admin automático só para quem clicou em "Hospedar partida" (o cliente do mesmo processo), em vez de qualquer
    conexão local? Fecharia de vez o caso do estranho que entra sozinho por túnel.
43. Quem sai do jogo **perdendo** um embate de Zanzoken deveria levar o golpe antes de ser gravado?

**Kit do Deus da Destruição**
44. O Deus deve ficar imune à idade também na Sala do Tempo?
45. A Fury atravessa nocaute e morte, como no original. Fica? A cena de estreia (invenção) fica?
46. God's Temper conta todo dano (como está) ou só os caminhos em que o original o conta?
47. Cores dos dois tiros: amarelo e roxo fixos, como no original, ou a arte de ki do personagem?
48. A esquiva do Instinto custa Ki no port e é de graça no original. Tira o custo?
49. Aura e cabelo roxos do Deus (e brancos do Anjo) pelo rank no ki divino: portar?

**Bills e Whis**
50. "100% + Fury": 1 trilhão de base × 72 (como está) ou 1 trilhão já na Fury?
51. Eles ensinam Ultra Instinto / Ultra Ego, ou só aceleram quem já tem (como está)?
52. Cargo vago de novo: o Bills ou o Whis volta (como está)?
53. Antes da visita eles não existem em lugar nenhum. Ou já devem estar no planeta deles desde o começo?
54. No desafio o Bills usa a Fury e o God's Temper, e nenhuma habilidade ativa do kit (a IA de combate não sabe
    chamá-las). Quer que ele use o Hakai, os dois tiros e a Energy Nullification?
55. O triplo é do poder expresso e acompanha o jogador. Ou do base, medido só no início?
56. Desafio exige ki divino e espera de 1 dia in-game entre tentativas: ok? Treino de 5 minutos, sem bônus de BP: ok?
57. Com o trono vago, o botão "desafiar" da aba Cargos ainda diz que não há Deus para desafiar. Aponta para o Bills?

**Do relatório do playtest, ainda sem resposta**
58. Loot de NPC humanoide; roubo de zenni; alfa do rastro; commits (um por tópico?).

## 30. Achados fora do pedido

**Consertados no caminho**
- A aba World se refazia inteira uma vez por segundo (a assinatura dela contava os segundos até a lua cheia). Quem
  digitasse no batismo de planeta perderia o texto a cada segundo.
- Com o menu da tecla E aberto, Esc abria a pausa em vez de fechar o menu.
- A bancada das formas media o poder em dois instantes com o personagem em fúria, e a raiva goteja por milissegundo:
  oscilava. Passou a medir o corpo em paz.
- O robô das abas reprovava "a lua no HUD": ele nasce dentro do Banco, e sob teto o HUD esconde a lua de propósito.

**Não consertados**
- Base ou máquina feita antes de 10/10 num planeta procedural, em célula que virou base de pedra grande, fica
  sobreposta.
- A descrição da Drill ainda é a do original, em inglês.
- Quem hospeda jogando e fecha o **console** por trás da janela continua sem gravar (só o servidor dedicado vigia o
  console).
- O save periódico no meio de um embate de Zanzoken ainda grava o personagem oculto; quem conserta é o login.
- Fundir ou ser absorvido durante a janela de 2 minutos da Sala do Tempo tira o corpo da contagem dela.
- A tela de inventário e a de técnicas ouvem o Esc do mesmo jeito que o menu da tecla E ouvia; não medi se a
  pausa também abre por cima delas.
- Se a casa do Bills for revelada com a aba Nav aberta, o botão "casa de Bills" só aparece na próxima vez que a
  aba se refizer.

**Bancadas antigas que continuam vermelhas** (já eram antes desta sessão): `planeta` (2, portão de marcos do Planet
Destroy), `tecla` (1, um "[ACHADO]" que o próprio robô registra), `diagforma` (10 fixas), `dskills` (1, pede o
servidor em outro processo), `povo` (1, depende da semente do universo), `menteviva` (8), a pura `disciplinas` (1,
uma linha de bancada de 05/09) e a `--iateste` (2, de 07/10 e 09/10).

**Bancadas frágeis:** `--diagabas` reprova 5 linhas quando o sorteio da criação dá Saiyajin Lendário (o berço vira
um planeta sem lua); `npc`, `povo` e `rotina` oscilam com a semente do universo; `fotofusao` falha quando a classe
sorteada é Low-Class; `--diagcarga` reprova 1 ou 2 linhas de noite.

## 31. Disco

- Pastas de rascunho dos subagentes: cerca de 1,4 GB em `E:\Users\Joao\Desktop\_rascunho_6199e7\ag\` (quase tudo é
  cache de compilação de cada agente).
- Saves de bancada desta sessão: 290 pastas, cerca de 250 MB em `Trailer\bruto\appdata\`.
- Fotos de evidência: 184 arquivos, 35 MB em `Trailer\bruto\fotos_6199e7\pedido-2026-10-10\`.
- Tudo no E:. Não apaguei nada.
- Para listar e limpar (eu não apago): `Trailer\bruto\limpar-sessao-6199e7.ps1` (sem chave só lista).
