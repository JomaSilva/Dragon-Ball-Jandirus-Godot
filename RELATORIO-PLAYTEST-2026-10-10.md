# Relatório — Correções e features do playtest (2026-10-10)

Os 12 itens estão implementados/investigados, publicados na árvore e compilando. **Nada foi commitado** (ver "Preciso de você", no fim).

Rodada final de regressão no código final: **36 bancadas verdes**, mais a `--vozdupla` de dois processos. Exceções, que já existiam antes deste trabalho: `--planetateste` (266 OK e 2 falhas no portão de marcos do Planet Destroy) e `--diagforma` (as 10 falhas fixas de sempre; rodada depois do item 6, sem falha nova).

Como rodar qualquer bancada citada: `bash Trailer/bruto/vizinhas_dos_blocos.sh <nome>` (uma por vez).

| # | Item | Status | Bancada |
|---|------|--------|---------|
| 1a | Sair da meditação profunda | feito | `transe` 30/0, `dtranse` OK, `mente` 65/0 |
| 1b | Dano no corpo real depois do PvP na mente | feito (causa confirmada) | `transe` |
| 1c | Majin de pé em vez de meditando | feito + 1 achado de arte | `transe` |
| 2 | Vigor do Majin não regenera | feito (causa confirmada) | `transe` |
| 3 | Milestones não concedidos | feito (causa confirmada) | `arvores` 60/0 |
| 4 | Meditar e skills de ki | medido; bônus existe; botão + verbo novos | `arvores` |
| 5a | Som de carregar ki e de dash pros outros | feito | `dvizinho` OK |
| 5b | Tremor acima de 120% de ki | feito | `dvizinho` |
| 5c | Outros sons/efeitos só locais | listado; os de mesma causa corrigidos | — |
| 6 | Cor do borrão do dash e do rabo | feito, medido em pixel | `corrastro` 18/18 |
| 7 | Nocaute reseta o ki | feito | `reverter` 24/0 |
| 8 | Tecla de reverter/liberar | feito — **tecla X** | `reverter` |
| 9a | NPC nocauteado | investigado: já funciona como jogador | — |
| 9b | Saque de nocauteado | feito pra quem tem inventário; **loot de NPC = pergunta** | `saque` 14/0 |
| 9c | Majin absorve NPC com multiplicador próprio | feito | `majin` 116/0 |
| 10 | Voz picotando | causa medida e corrigida; ferramentas de teste | `dvoz` OK, `vozteste` 40/0, `--vozdupla` 19/0 |
| 11 | Ultra Divine Water | feito (é o `Sacred Water` do BYOND) | `agua` 23/0 |
| 12 | Quests de vilões | feito | `vilao` 31/0 |

---

## Fase 1

### 1a. Sair da meditação profunda — feito
- **Por que prendia:** (1) o gesto de saída do original nunca tinha sido portado — no DM, apertar Meditar de novo acorda (`Meditate.dm:20-21`); aqui a única porta era um verb no fim de uma aba do menu P. (2) A saída "por vitória" é inalcançável sem modo letal ou contra raça que regenera (o reflexo revive a cada 15 s). (3) Estados órfãos: sobrevivente sozinho depois de um PvP lá dentro, registro do transe perdido após autosave + queda, alguém tirado da mente por fora (`MoveToZone` de admin/torneio/selo/fusão), prisioneiro de Majin mergulhando.
- **O que mudou:** `Server/GameServer.Transe.cs` (novo) — `SairDoTranse` é a saída que funciona com qualquer estado; `ArrancarDoTranse` fecha o transe quando o jogador é levado por fora; o login limpa save em transe. No cliente, a faixa "MEDITAÇÃO PROFUNDA — Acordar (M)" no alto da tela (`Client/SaidaDaMente.cs`) e a própria tecla de meditar acordando dentro da mente (`Client/LocalPlayer.cs`).
- **Tecla:** a de meditar (**M**) + o botão na faixa.
- **Testar:** medite fundo (M → Meditação profunda); a faixa aparece; saia pelo botão e, de novo, pela tecla M. Repita com dois jogadores na mesma mente depois de um nocautear o outro.

### 1b. Corpo real tomando dano — feito
- **Causa raiz:** o corpo que fica no mundo e o avatar dentro da mente dividiam a **mesma** ficha e o mesmo estado de combate; uma "foto" de 5 campos era restaurada por só um dos caminhos de saída. Vazavam buffs/Kaio-ken ligados lá dentro, o knockback, o golpe de saída do Zanzo Clash, efeitos atrasados por id e o autosave. Não é específico do Majin — ele só é o caso em que mais aparece (regeneração e vigor).
- **O que mudou:** o corpo largado ganha **corpo próprio** na entrada (`DarCorpoProprio`); a saída devolve o corpo real e faz a faxina (`LimparOCombateDaMente`). O contrário continua valendo e agora chega certo: o que acontece com o corpo **lá fora** (apanhar, nocaute, morte) é real e acorda o dono.
- **Testar:** dois clientes, um Majin × outra raça e outra × outra: lutar dentro da mente até nocaute, sair, conferir vida/ki/buffs iguais aos da entrada. Depois, um terceiro bate no corpo que ficou meditando: o dono acorda ferido de verdade.

### 1c. Majin em pé — feito + achado
- **Causa 1 (todas as raças):** a pose do corpo que fica vinha do `med` compartilhado, apagado no primeiro passo de quem mergulhou. Resolvido pelo corpo próprio do 1b.
- **Causa 2 (só Majin, e é da arte do BYOND):** no `Majin.dmi` o estado "Meditate" **é** uma figura de pé, de braços cruzados e olhos fechados (a folha `Majin1` senta). A conversão está certa; não mexi em arte. Se quiser o Majin sentado, é trocar o quadro.

### 2. Vigor do Majin — feito
- **Causa raiz:** o original dá a Majin e Bio-Android um **piso de 50% de vigor** (`StaminaDrain.dm:19-20`). O port trouxe o custo de vigor da regeneração deles e não trouxe o piso: o vigor ia a zero e, sem vigor, nada regenera.
- **O que mudou:** `Core/Stats/Nutricao.cs` (`TemPisoDeVigor`).

### 3. Milestones — feito
- **Causa raiz:** não é um limiar errado — a conversão BP → marcos (`processpoints()`/`HandleLevel()`, `scaling.dm:1-13`) **nunca tinha sido portada**. Não existia código que desse marco por BP.
- **O que mudou:** `Core/Skills/MarcosDeBp.cs` (novo): limiar `1,466^n ÷ Skillpoint Mod` da raça, como no DM; checagem "BP ≥ limiar e ainda não concedido", concede **todos** os limiares pulados e é **retroativa no login** (cursor `MarcosPorBp` no save). De 10 a 1000 de BP são 12 marcos (do 7º ao 19º natural).
- **Testar:** logar com um personagem antigo de BP alto: os marcos atrasados chegam na hora, com aviso.

### 4. Meditação e skills de ki — medido
- **Resultado:** o bônus **existe e aplica**. Medido no tique de produção, Ki Unlocked nível 1 (barreira de 5.150 de exp):

| Corpo | parado | meditando | até o nível 2 meditando |
|---|---|---|---|
| recém-nascido (Ki máx 100) | 40 exp/min | 139,9 exp/min (3,5×) | 37 min |
| formado (Ki máx 1000) | 1.200 exp/min | 2.428 exp/min (2×) | 2,1 min |

- **Por que "parece que não faz":** (1) não havia onde ver nível/exp — agora há o verbo **"Progresso das skills"** (o `Check_Skill_Stats` do DM); (2) a curva do original pro novato (Ki máx ≤ 300 → ganho × Ki máx/3000) faz o primeiro nível levar ~37 min; (3) Ki Unlocked precisa ser comprado na árvore.
- **Botão:** `GainKnobs.MeditacaoNasSkillsDeKi` (1 = o do BYOND; 3 = triplo — conferido em bancada).
- **Divergências do DM que vi e NÃO mexi:** Ki Unlocked sem o teto de nível `BP/2`; BP de meditação comum 100× o do DM; skills de Ki Effusion sem fonte de exp; ganho por clique nos Ki_Targets não portado.

---

## Fase 2

### 5a. Sons de carregar e de dash — feito
- **Causa raiz:** o zumbido do corpo alheio lia o bit da **aura** (só quem tem `canPower`, e parado), enquanto o do próprio jogador segue o **estado** da tecla. Quem só tinha Ki Unlocked se ouvia e era mudo pros outros. O swoosh da corrida só era tocado pelo `LocalPlayer`. E o estalo de início de todo corpo alheio era engolido na primeira carga.
- **O que mudou:** dois bits novos no snapshot (`Net/Protocol.cs`): `Reunindo` (estado da tecla C) e `CargaAlta`. `Client/World.cs` liga o zumbido ao estado; `Client/RemotePlayer.cs` toca o swoosh na virada do bit de corrida (com respiro de 0,4 s); `Client/CargaVisual.cs` corrige o estalo. É **estado**: quem chega depois já ouve, e para pra todos quando ele solta o C. Áudio posicional (cai com a distância).
- **Achado no caminho:** o tique derrubava a carga do morto de pé 33 ms depois de aceitá-la — a outra metade do "depois de morrer não dá pra apertar C". Corrigido (`GameServer.Carga.cs`).

### 5b. Tremor acima de 120% — feito
- Constantes em `Core/Combat/CargaDeKi.cs`: limiar **1,2**, raio **16 tiles**, força **3 px**, queda **8 px/s**, cadência 0,08 s. Peso cai em linha reta até zero na borda do raio.
- Medido: 3,00 px na tela de quem carrega; 2,25 px a 4 tiles; 0,75 px a 12; 0 a 20; para em menos de 0,5 s ao soltar.
- **Decisão:** andando a carga fica suspensa e o tremor também. No DM o tremor era sorteado, de 8 px, na tela inteira e sem queda com a distância.

### 5c. Outros sons/efeitos só locais
- **Corrigidos (mesma causa):** zumbido da carga, estalo de início, swoosh da corrida.
- **Só listados:**
  - `explosao_final` (Final Explosion, Aura da Destruição, agonia do planeta): desenhada e ouvida **centrada no corpo de cada destinatário**, e só por quem o servidor endereça; quem está ao lado e não foi atingido não vê nada.
  - Efeitos mandados e nunca desenhados **por ninguém** (dívida já anotada em `PROXIMOS-SISTEMAS.md` §6): escudo, carga_final, rugido, barragem, paralisia, timefreeze, transmissao, dando_poder, zanzoken, invisivel, mordida, jokenpo, rasgo, crono.
  - A guarda (ALT) não tem pose pra ninguém.

### 6. Cor do borrão e do rabo — feito
- **Causa raiz (borrão):** a cor de um corpo não mora na textura — mora na tinta do material de cada camada e no modulate. O material de borrão entrava **no lugar** do que pinta o corpo e desenhava a folha crua: Majin azul com rastro rosa, SSG com cabelo preto, roupa tingida na cor de fábrica.
- **Causa raiz (rabo):** o DM soma **metade da cor do cabelo** no rabo (`SaiyanObjects.dm:99`); o port deixava o sprite cru. Aparece em mestiço de cabelo colorido (Saiyajin puro tem cabelo natural, então nada muda).
- **Outros elementos com a mesma causa:** a miragem do Zanzoken — corrigida também.
- **O que mudou:** `Assets/Shaders/Borrao.gdshader`, `Zanzoken.gdshader`, `Client/CharacterVisual.cs` (`FotografarBorrado`, rabo em `BaseDaFicha`, repintura no `Vestir`), `BorraoDirecional.cs`, `RastroDeCorrida.cs`, `Zanzoken.cs`. A cor é derivada da aparência que todo cliente já recebe, então vale em todos e acompanha a troca de cor.
- **Medido em pixel** (`corrastro`, com janela): cópia do rastro idêntica ao corpo em 100% dos pixels nos 3 corpos; rabo bate a conta do DM em 528/528 px.
- **Observações que não mexi:** (1) rastro e miragem **ignoram o alfa/esmaecer** previstos (o shader descarta o modulate) — as cópias saem opacas; ligar isso muda a cara do efeito. (2) As cópias não levam feridas, rasgos nem membro arrancado.

---

## Fase 3

### 7. Nocaute reseta o ki — feito
- `Core/Combat/CombatState.cs` (`Nocautear`, o funil único): acima de 100% volta a 100%; com 100% ou menos nada muda. Zera também o "troco" da carga (`extracharge`), senão o corpo levantaria acima de 100%. Desvio declarado do DM (lá o excesso só vaza).

### 8. Reverter/liberar — feito, **tecla X**
- A tecla X ("reverter") já existia e é remapeável; dei a ela as três regras em vez de criar uma segunda tecla de reverter. Se preferir outra, as livres são H, J, L, O, U, Y, Z.
- Regras (`GameServer.Formas.cs`, `ReverterOuLiberar`): (1) ki > 100% → volta a 100%, sem mexer na forma; (2) senão, transformado → estágio anterior do tronco da linha; sem anterior → base; (3) na base → nada.
- **Decisões:** "estágio anterior" é o primeiro degrau abaixo **em que o personagem já esteve** — recuar não ensina forma (do SSJ4 vindo do Oozaru, quem nunca foi SSJ3 não vira SSJ3). Grade 2/3 voltam ao SSJ1. Escolher "base" na aba de formas continua indo direto ao normal.

### 9a. NPC nocauteado — nada a mudar
- NPC e jogador passam pelo **mesmo** resolvedor: com o atacante em modo não-letal (o padrão) ele é **nocauteado**, fica no chão e levanta sozinho (teto de 225 s); só morre com letal ligado. Vale pra NPC de saga. Não mexi em saga.

### 9b. Saque — feito, com uma pergunta
- **Do BYOND:** `mob/verb/Steal()` (`Grabbing.dm:1-29`) — alvo nocauteado, um item **não equipado** por vez, anunciado a quem vê.
- **O que mudou:** tecla **E** perto de um nocauteado mostra "[E] revistar <nome>" e abre a lista; cada clique leva um item. Fica com o dono o que está em uso (scouter ligado, pesos no corpo; roupa vestida nem entra). O servidor confere tudo a cada clique. Respiro de 1 s entre itens (`Core/Items/Saque.cs`). Bloqueado dentro da mente.
- **Não portei o roubo de zenni:** o DM lista "Zenni" mas o ramo compara a variável errada e nunca dispara.
- **NPC:** cai na mesma regra, mas **nenhum NPC do port tem inventário** — a lista vem vazia. Ver a pergunta no fim.

### 9c. Absorção de NPC pelo Majin — feito
- Já existia (NPC devorado rende os mesmos 10%). Faltava o botão: `AbsorcaoMajin.MultiplicadorDeNpc` (1,0 de fábrica = mesmos ganhos; 0,5 = metade). Não afeta jogador absorvido.

---

## Fase 4

### 10. Voz picotando — causa medida e corrigida
- **O que já estava certo:** Opus (Concentus) 20 ms com FEC, `Unreliable`, fila de jitter adaptativa com PLC, envio por amostras (não por framerate), mesma taxa nas duas pontas.
- **Causa raiz:** o servidor **jogava fora a sequência do falante e renumerava**. Toda perda na **subida** (falante → servidor) deixava de ser um buraco: pra quem ouve virava "engasgo" — a fila esticava a fala, subia o atraso até o teto e descartava quadros reais. Em rede local não há perda na subida, por isso só apareceu no playtest.
- **Medido** (30 s de fala, 100 ms de latência, 2% de perda em cada perna, mesma rede nas duas rodadas):

| | FEC | esticados | quadros reais descartados | alvo final da fila | silêncio |
|---|---|---|---|---|---|
| antes | 38 | 37 | 9 | 10 quadros (200 ms, teto) | 0 |
| depois | 61 de 65 perdas | 5 | 0 | 4 quadros (80 ms) | 0 |

- **O que mudou:** `Core/Social/VozLocal.cs` (`NumeradorDoRelay`: a sequência do falante atravessa o servidor, saneada contra cliente mentiroso), `Server/GameServer.Voz.cs`.
- **Sobre o LiteNetLib:** `SimulatePacketLoss`/`SimulateLatency` só existem no binário de debug da biblioteca; no de release que o jogo carrega não fazem nada. Por isso a simulação é do jogo.
- **Testar (aceite):** suba o servidor com `--vozrede 50 2` (50 ms por perna + jitter, 2% de perda por perna, **só no canal de voz**) e os clientes com `--vozlog`. Conversem 1–2 min. No console de quem ouve sai uma linha a cada 5 s por falante: o esperado é `secou 0x`, `esticados` perto de 0, perdas aparecendo em `FEC`, e `alvo` em 3–5 quadros. (Em `--headless` o `secou`/`alvo` não valem: o driver de áudio falso consome em blocos de 100 ms.)
- **Não mexi:** o relay anda no tique do servidor (até 33 ms de jitter, que a fila absorve); o microfone abre a cada aperto do V.

---

## Fase 5

### 11. Ultra Divine Water — feito
- Não existe "Ultra Divine Water" no código do BYOND; o jarro do Mirante é o `/obj/Sacred_Water` (`EarthRanks.dm:181-231`), que é a mesma coisa. Portei esse.
- **Regras portadas:** uma vez por personagem; aguenta quem tem resistência física (`Ephysdef`) ≥ 1, senão 50% — e quem perde **morre** (e pode tentar de novo); quem aguenta ouve três falas, cai nocauteado 30 s, acorda, e ganha vigor máximo ×1,2 e, se estiver abaixo do próprio teto, BP pelo `capcheck(BPMod × relBPmax) ÷ 5`. Há a opção "O que é esta água?".
- **Onde:** Mirante, célula (141, 438) — a mesma do `.dmm`. `Core/Tech/AguaSagrada.cs`, `Server/GameServer.AguaSagrada.cs`, linha nova em `construcoes.json` e no `z12_Lookout.objetos`.
- **Decisão:** quem desloga no meio do efeito leva o prêmio antes do save (no DM perdia a única água da vida).
- **Testar:** ir ao Mirante, tecla E no jarro.

### 12. Quests de vilões — feito
- `Core/Npc/VilaoAvulso.cs` (números), `Server/GameServer.Viloes.cs` (sistema), molde `vilao_avulso` no `npcs.json`. Separado das sagas (lista, relógio e tipo de NPC próprios).
- **Padrões:** intervalo 12–30 min por planeta com jogador; some em 10 min; no máximo 1 por planeta e 3 no servidor; karma **+10** pra quem nocautear ou matar (uma vez); nasce em vaga de terra firme do mesmo habitat do povoamento, a 10–30 tiles de um jogador; aviso no planeta com nome, direção, karma e prazo.
- **Força:** no molde — `bpRelativo` 1.0 (média de BP dos jogadores online, ±15%) com `bpPiso` 500.
- **Decisão:** criei o tipo de NPC `vilao` em vez de religar o "inimigo comum", que você tinha mandado manter desligado — o recorte continua de pé.
- **Testar rápido:** em `VilaoAvulso.cs` baixe `IntervaloMinS`/`IntervaloMaxS` pra alguns segundos, entre na Terra e espere o aviso.

---

## Pedidos avulsos da mesma sessão
- Skill **Saiyan Power** removida por completo.
- **C depois de morrer** (toque duplo pra transformar e a carga do morto de pé).
- **Admin:** lista de planetas destruídos com botão "restaurar" (funciona de qualquer planeta).
- **Semente de macieira:** comer maçã tem 25% de chance de dar a semente; "Plantar" pela mochila.
- **Desconexão:** volta ao menu principal com o aviso "DESCONECTADO DO SERVIDOR" e o motivo.
- **Batizar planeta:** descobridor (uma vez, em mundo gerado sem dono), dono (a cada 5 min) e admin; nome de 3 a 24 letras; só muda o que se mostra.

## Não provado por robô (vale um olho humano)
- A lista de saque abrindo na tela (o servidor está coberto; a tela, não).
- O fantasma de "Plantar" a semente e o campo de texto do batismo pra não-admin.
- Voz com placa de som de verdade — o teste de aceite acima.

## Preciso de você
1. **Loot de NPC (9b):** quer que NPC carregue itens? No BYOND: `mobDrops` + um equipamento por raridade, com 15% de chance de cada item cair ao morrer. O saque já funcionaria neles sem mais nada.
2. **Roubo de zenni:** portar (quanto? tudo, como o DM pretendia?) ou deixar de fora?
3. **Commits:** o documento pede um por item; não commitei nada sem a sua palavra. Quer que eu separe e commite?
4. Opcionais: alfa do rastro/miragem; arte do Majin meditando; manter o reverter no X.
