using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// POR QUE ESTE AGARRAO ACABOU. **So escolhe a frase** -- a limpeza dos campos e identica nos cinco
/// casos, e essa uniformidade e proposital: um motivo que limpasse "um pouco menos" seria o buraco
/// por onde alguem ficaria preso. Ver <see cref="GameServer.Soltar"/>.
/// </summary>
public enum MotivoDaSoltura
{
	/// <summary>O terceiro toque na tecla (`Grabbing.dm:60-77`).</summary>
	Tecla,

	/// <summary>Quem segurava caiu ou morreu -- o `if(KO...)` de `Grabbing.dm:192`.</summary>
	Desfaleceu,

	/// <summary>Os dois deixaram de estar na mesma zona -- o `grabbee.z != usr.z` de `:192`.</summary>
	OutraZona,

	/// <summary>Segurando (modo 1), o corpo saiu do alcance -- a INTENCAO do `:192` (ver la).</summary>
	Distancia,

	/// <summary>O preso se debateu ate se soltar (`movement handler.dm:238-250`).</summary>
	Escapou,
}

/// <summary>
/// ============================ O AGARRAO -- PEGAR, SEGURAR, CARREGAR E ARREMESSAR ============================
/// *"vc n colocou o sistema de GRAB, pode AGARRAR o inimigo e JOGAR ELE LONGE, ou apertar o botao de
/// agarrar 2 VEZES e poder CARREGAR alguem com vc (entao se vc apertar o botao de grab 2 vezes e
/// VOAR, a pessoa agarrada vai ser considerada como VOANDO tb NA MESMA ALTITUDE q vc)"* -- o pedido.
///
/// As formulas moram no <see cref="Agarrao"/>, no Core, cada uma citando a linha do DM. Aqui fica o
/// encanamento: quem pega quem, quem escreve a posicao de quem, e -- a metade que mais importa --
/// **por quantos caminhos alguem se solta**.
/// ========================================================================================================
///
/// ============================ A REGRA E SOBRE CORPO, E POR ISSO O ITEM 3 SAI DE GRACA ============================
/// Nao ha uma unica pergunta `Ficha.dead` neste arquivo, e a ausencia e a decisao central. O original
/// tambem nao pergunta (`Grabbing.dm:170` so quer saber se o alvo ja esta preso por outro); quem
/// inventou a excecao la foi o CADAVER, por ser um `/obj` com `IsntAItem=1` (`Corpse.dm:2`).
///
/// Neste port o cadaver e o proprio corpo (ver `Core/World/Alem.cs`), entao *"o corpo mesmo morto TEM
/// TODAS AS INTERACOES DE UM CORPO VIVO: pode sofrer dano, ser agarrado, jogado etc, e pessoas podem
/// agarrar o corpo e SAIR VOANDO com ele"* ja e verdade sem uma linha de excecao. Se este arquivo
/// perguntasse "esta vivo?", cada uma dessas quatro frases viraria um remendo separado depois.
///
/// **A UNICA DIVERGENCIA DELIBERADA COM O DM ESTA AQUI**: la, morrer solta o agarrao a forca
/// (`Death.dm:24-28` varre a `view()` e desprende). Aqui nao -- morrer nos bracos de alguem deixa o
/// corpo nos bracos dele. Portar aquele bloco apagaria o pedido do dono no instante em que ele mais
/// importa.
/// ============================================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// 1. AS PERGUNTAS
	// =====================================================================
	/// <summary>
	/// ESTE CORPO ESTA PRESO? -- lida no <see cref="PodeMexerOCorpo"/>, ou seja no funil de vetor que
	/// o jogador e a IA compartilham. E o `grabParalysis` do original (`Grabbing.dm:183`), e entrar
	/// por ali significa que o NPC agarrado para de andar pela MESMA regra que trava o jogador.
	///
	/// Um `int` e nao um `bool`: guardar QUEM segura e o que permite a varredura orfa (ver
	/// <see cref="TickDoAgarrao"/>) descobrir sozinha que o dono do aperto sumiu.
	/// </summary>
	private static bool Agarrado(ServerPlayer pl) => pl.AgarradoPorId != 0;

	/// <summary>
	/// O CORPO QUE <paramref name="a"/> SEGURA -- e so se o aperto e confirmado DOS DOIS LADOS. Nulo
	/// quando nao segura ninguem, quando o outro corpo sumiu do mundo, ou quando o outro lado ja foi
	/// solto por outra ponta: *se o aperto nao e confirmado dos dois lados, ele nao existe*.
	///
	/// ============================ O PRESO E RESOLVIDO PELA ZONA, E NAO PELO `_players` ============================
	/// ISTO ERA UM DEFEITO ANTES DO CADAVER EXISTIR, e contradizia o cabecalho deste arquivo: la esta
	/// escrito que o BONECO DO CORPO LARGADO **pode** ser agarrado. Nao podia -- o `CorpoNaFrente` o
	/// achava (varre a `ZoneList`), o `Prender` escrevia os dois lados, e no tique seguinte um
	/// `_players.TryGetValue` falhava (o boneco fica fora do `_players` de proposito) e o aperto era
	/// desfeito na hora, sem uma linha dizendo por que. <see cref="CorpoNaMinhaZona"/> e a resolucao
	/// que o `Mirar` e o arremesso ja usam: **nem todo corpo que existe num lugar e um corpo
	/// SIMULADO**. Com ela o boneco e agarravel de verdade e o CADAVER sai de graca.
	///
	/// O CUSTO E UMA VARREDURA DE ZONA POR AGARRAO, nao por corpo: so quem tem `AgarrandoId != 0` chega
	/// aqui, e em qualquer instante do jogo sao zero ou um punhado.
	/// ==========================================================================================================
	/// </summary>
	private ServerPlayer? QuemEuSeguro(ServerPlayer a)
		=> a.AgarrandoId != 0 && CorpoNaMinhaZona(a, a.AgarrandoId) is { } d && d.AgarradoPorId == a.Id ? d : null;

	/// <summary>
	/// QUEM SEGURA <paramref name="d"/> -- confirmado dos dois lados, como acima. Aqui o dicionario
	/// BASTA: quem agarra e sempre um corpo simulado (agarrar e um gesto -- vem de uma tecla ou de um
	/// cerebro -- e as duas coisas so existem em corpo do `_players`). E isto importa: esta pergunta
	/// e feita pelo `EntraNaGrade` uma vez por corpo por tique, e uma varredura de zona aqui faria a
	/// montagem da grade -- que existe justamente pra nao ser O(n²) -- virar O(n²).
	/// </summary>
	private ServerPlayer? QuemMeSegura(ServerPlayer d)
		=> d.AgarradoPorId != 0 && _players.TryGetValue(d.AgarradoPorId, out ServerPlayer? a) && a.AgarrandoId == d.Id ? a : null;

	/// <summary>
	/// ESTE CORPO ESTA NO COLO DE ALGUEM? -- a pergunta que o <see cref="TickDoVoo"/> faz pra nao
	/// derrubar quem esta sendo levado pelo ar.
	///
	/// ============================ POR QUE ELA CONSULTA O OUTRO CORPO EM VEZ DE UM CAMPO ============================
	/// O `grabMode` e do AGARRADOR (`Grabbing.dm`, o campo e do mob que segura), porque e ele quem
	/// cicla o modo com a tecla. Espelhar o modo tambem no corpo preso daria a resposta em uma
	/// comparacao -- e daria DUAS VERDADES sobre o mesmo aperto, com a copia divergindo no primeiro
	/// caminho de soltura que alguem esquecesse de atualizar (e sao oito).
	///
	/// O custo do jeito honesto e zero na pratica: a guarda `AgarradoPorId != 0` corta antes do
	/// dicionario, e em qualquer instante do jogo quase ninguem esta preso.
	/// ==========================================================================================================
	/// </summary>
	private bool SendoCarregado(ServerPlayer pl)
		=> QuemMeSegura(pl) is { ModoDoAgarrao: ModoDeAgarrao.Carregando };

	/// <summary>
	/// O CORPO NA FRENTE -- o `for(var/mob/A in get_step(src,dir))` do `Grab()` (`Grabbing.dm:105`).
	///
	/// ============================ ELE NAO E O `AlvoNaFrente` DO SOCO, E A DIFERENCA E UMA LINHA ============================
	/// O alcance, o cone e a regra de andar sao **os mesmos** de proposito: "consigo encostar nele?"
	/// tem que ter uma resposta so, senao um dia daria pra agarrar quem nao da pra socar. O que muda
	/// e que o do soco PULAVA `o.Ficha.dead` ate 2026-09-05 (hoje aceita o cadaver, preferindo o vivo) -- e era esse `if`
	/// que transformaria o item 3 do dono em excecao.
	///
	/// Reusar o do soco e apagar o cadaver do jogo; copiar o do soco e criar a segunda resposta pra
	/// "quem esta na minha frente". A saida e esta: as regras de ALCANCE sao chamadas das mesmas
	/// funcoes (`AlcancaPelaAltura`, `MeleeArea.NoAlcance`), e o que sobra aqui e so a lista de quem
	/// conta -- que pro agarrao e **todo corpo**.
	/// ====================================================================================================================
	///
	/// ============================ QUEM NAO PODE SER AGARRADO: **NINGUEM, POR IDENTIDADE** ============================
	/// A pergunta foi feita corpo a corpo contra <see cref="Jandirus.Core.Npc.Gente"/>, e a resposta foi
	/// a mesma nos tres casos dificeis -- por isso **nao ha nenhuma consulta ao `Gente` neste arquivo**,
	/// e a ausencia esta escrita aqui pra ser lida como escolha e nao como esquecimento:
	///
	///   * **O BONECO DO CORPO LARGADO** (`DonoDoCorpoLargado != 0`) -- **PODE**. Ele e literalmente um
	///     corpo deitado no mundo com o dono noutro lugar; e a mesma coisa que o cadaver, um instante
	///     antes. Recusar aqui seria a primeira excecao, e a segunda seria o cadaver. De quebra sai uma
	///     regra que o jogo ja pedia: **entrar em transe passa a ter risco**, e o `MarcarAgressao` do
	///     <see cref="Prender"/> ja acorda o dono (e o gancho que o `MindMeditate.dm:147-149` usa).
	///
	///   * **O REFLEXO DA MENTE** (`DonoDoClone != 0`) -- **PODE**, e a permissao nao custa nada: ele
	///     vive numa zona de uma pessoa so (a Dimensao Mental), entao quem o alcanca e o dono dele. Uma
	///     recusa aqui seria uma regra que nunca dispara -- codigo morto vestido de seguranca.
	///
	///   * **O CHEFE DE SAGA** (`Papel.EhChefe`) -- **PODE**, e esta e a que mais tenta. O original nao
	///     tem corte por chefe (`Grabbing.dm:170` so pergunta se o alvo ja esta preso), e o
	///     contrapeso ja existe e e o certo: o `grabberSTR` e `Ephysoff * expressedBP` e a chance de
	///     escapar e `Etechnique * expressedBP / grabberSTR` -- **agarrar um chefe funciona, segurar um
	///     chefe nao**. Ele se solta em uma ou duas frações de segundo, pelo poder, e nao por uma
	///     etiqueta. Uma imunidade escrita seria o poder deixando de importar exatamente onde ele mais
	///     importa.
	///
	/// AS UNICAS RECUSAS SAO DE **ESTADO**, e as tres estao logo abaixo: ja preso por outro (a do DM),
	/// intocavel (a que o soco ja aplica) e fora de alcance/andar (a mesma do soco).
	/// ============================================================================================================
	/// </summary>
	private ServerPlayer? CorpoNaFrente(ServerPlayer a)
	{
		ServerPlayer? melhor = null;
		float melhorDist = float.MaxValue;

		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
		{
			if (o == a) continue;

			// JA ESTA PRESO POR OUTRO -- `!M.grabber` (`Grabbing.dm:170`). E a UNICA recusa que o
			// original faz sobre o alvo, e ela e sobre o ESTADO dele e nao sobre quem ele e.
			if (o.AgarradoPorId != 0) continue;

			// INTOCAVEL e o que ja tira alguem de toda mira do jogo (cinematica de transformacao,
			// imunidade de embate). Pular aqui nao e uma regra nova do agarrao: e a mesma que o
			// `AlvoNaFrente` do soco aplica, e agarrar alguem no meio da cena do SSJ3 seria
			// atravessar o escudo que a cena inteira existe pra dar.
			if (o.Combate.Intocavel) continue;

			// QUEM ALCANCA QUEM, POR ANDAR -- assimetrico de proposito (ver `Voo.PodeAcertar`): quem
			// paira rasante agarra quem esta no chao, e nao o contrario. E o que faz o carregar pelo
			// ar comecar com uma descida ate o alvo.
			if (!AlcancaPelaAltura(a, o)) continue;
			if (!MeleeArea.NoAlcance(a.Pos, a.Facing, o.Pos)) continue;

			float dist = (o.Pos - a.Pos).LengthSquared;
			if (dist >= melhorDist) continue;
			melhorDist = dist;
			melhor = o;
		}
		return melhor;
	}

	// =====================================================================
	// 2. A TECLA -- um gesto, tres estados
	// =====================================================================
	/// <summary>
	/// ============================ A MESMA TECLA CICLA OS TRES ESTADOS -- `Grabbing.dm:48-95` ============================
	/// O original faz exatamente isto, e o duplo toque do pedido do dono JA E de la:
	///
	///     nada        --toque-->  SEGURANDO  (`grabMode=1`, ":170-182")
	///     SEGURANDO   --toque-->  CARREGANDO (`grabMode=2`, ":78-84" -- "You pick up [grabbee].")
	///     CARREGANDO  --toque-->  solta      (`grabMode=0`, ":60-77" -- "You release [grabbee].")
	///
	/// **O ARREMESSO NAO TEM TECLA**, e isso tambem e do DM: segurando (modo 1), o primeiro PASSO
	/// arremessa (`mob/OnStep()`, `Throw.dm:1-3`). Uma tecla de arremesso seria invencao, e uma
	/// invencao que roubaria o gesto -- "agarrei e andei" e o que o jogo ensina sozinho.
	/// ================================================================================================================
	/// </summary>
	private void AlternarAgarrao(ServerPlayer pl)
	{
		// SOLTAR SEMPRE PODE, e vem primeiro. Nenhuma condicao de estado (nocaute, cena, Ki) entra
		// no caminho de LARGAR alguem -- e a mesma disciplina do `AlternarVoo`, onde descer nunca
		// falha: o que nao pode existir e um gesto de libertar que possa ser recusado.
		if (pl.AgarrandoId != 0)
		{
			if (pl.ModoDoAgarrao == ModoDeAgarrao.Segurando)
			{
				// SEGUNDO TOQUE: sobe pro colo. `grabbee.grabberSTR *= 1.5` (`Grabbing.dm:83`).
				if (QuemEuSeguro(pl) is { } preso)
				{
					pl.ModoDoAgarrao = ModoDeAgarrao.Carregando;
					preso.ForcaDeQuemMeSegura *= Agarrao.ApertoDeQuemCarrega;
					Avisar(pl, $"você levanta {preso.Name} do chão.");
					AvisarSePessoa(preso, $"{pl.Name} levanta você do chão.");
					// A FICHA SAI AGORA: o corpo preso passa a ser desenhado no colo (a altura dele
					// vira a de quem carrega no proximo tique) e o cliente dele precisa saber que
					// nao esta mais dirigindo. Mesma razao do `MandarFicha` do arremesso.
					MandarFicha(preso);
				}
				return;
			}

			Soltar(pl, MotivoDaSoltura.Tecla);
			return;
		}

		// ============================ AS RECUSAS SAO TODAS DO LADO DE QUEM AGARRA ============================
		// `if(KO||attacking||!canfight) return` (`Grabbing.dm:96`). O port ja tem essa frase inteira
		// num lugar so: `PodeMexerOCorpo` -- caido, morto-no-chao, carregando Ki, em embate, em cena,
		// enraizado, paralisado, prensado, arrastado por feixe. Reusa-la aqui e o que garante que uma
		// recusa nova (uma skill, um debuff) valha pro agarrao no mesmo dia em que passa a valer pro
		// passo, sem ninguem lembrar de vir aqui.
		// ================================================================================================
		if (!PodeMexerOCorpo(pl)) { Avisar(pl, "não dá pra agarrar ninguém assim."); return; }

		// O BRACO ESTICADO DO NAMEKUSEIJIN (lote G11) vem ANTES de quem esta na frente, como no DM
		// (`Grabbing.dm:97`): com a flag e um alvo marcado a tres tiles ou mais, o agarrao vai ate ele
		// com um terco da forca (`namekian.dm:241`). Ver `AlvoDoBracoEsticadoG11` / `FatorDoBracoG11`.
		if (AlvoDoBracoEsticadoG11(pl) is { } longe)
		{
			_agarroesEsticadosG11.Add(pl.Id);
			Prender(pl, longe);
			longe.ForcaDeQuemMeSegura /= FatorDoBracoG11(pl);
			Avisar(pl, $"seus bracos se esticam e agarram {longe.Name} a distancia!");
			return;
		}
		_agarroesEsticadosG11.Remove(pl.Id);

		if (CorpoNaFrente(pl) is not { } alvo)
		{
			Avisar(pl, "não há ninguém ao seu alcance pra agarrar.");
			return;
		}

		Prender(pl, alvo);
	}

	/// <summary>
	/// PEGOU -- `Grabbing.dm:170-182`. **Nao ha rolagem, nem BP, nem tecnica: pegou.** O poder so
	/// entra depois, na dificuldade de escapar (<see cref="Agarrao.ChanceDeEscapar"/>).
	/// </summary>
	private void Prender(ServerPlayer a, ServerPlayer d)
	{
		a.AgarrandoId = d.Id;
		a.ModoDoAgarrao = ModoDeAgarrao.Segurando;

		d.AgarradoPorId = a.Id;
		d.ForcaDeQuemMeSegura = Agarrao.Forca(a.Ficha);
		d.ContadorDaLuta = 0;
		d.DebatendoSe = false;

		// `M.totalTime = 0` (`Grabbing.dm:172`) -- o buffer de passo do alvo e zerado, pra ele nao
		// gastar credito guardado no primeiro instante da prisao. Aqui o equivalente exato e o
		// orcamento de passo, que e o que o port usa no lugar do `totalTime` do BYOND.
		d.OrcamentoPx = 0;

		// `M.refresh_combat_tag()` (`:177`) -- agarrar CONTA COMO ATAQUE. E o mesmo funil que o soco
		// usa, entao de graca vem o resto: o corpo largado de quem esta em transe acorda, o NPC
		// pacifico revida (`:178-180`, o `foundTarget` de la) e o rancor e registrado.
		MarcarAgressao(d, a);

		Avisar(a, $"você agarra {d.Name}! Ande pra arremessá-lo, ou aperte de novo pra carregá-lo.");
		AvisarSePessoa(d, $"{a.Name} agarra você! Ande pra tentar se soltar.");
		MandarFicha(d);
	}

	/// <summary>
	/// SOLTOU -- e este e o **unico** lugar do jogo que desfaz um agarrao.
	///
	/// ============================ UMA PORTA SO, PORQUE SAO OITO CAMINHOS ============================
	/// Solta-se por: a tecla, o arremesso, o nocaute de quem segura, a morte de quem segura, a troca
	/// de zona, o logout, o alvo sair da frente (no modo 1), e a luta do proprio preso. Oito lugares
	/// escrevendo quatro campos em dois corpos e a receita para o defeito mais grave que este sistema
	/// pode ter -- alguem preso pra sempre por um campo que sobrou.
	///
	/// Aqui os dois lados sao limpos numa chamada, e todo caminho passa por ela.
	/// ============================================================================================
	/// </summary>
	/// <param name="motivo">So escolhe a FRASE -- a limpeza e a mesma nos cinco casos.</param>
	private void Soltar(ServerPlayer a, MotivoDaSoltura motivo)
	{
		int id = a.AgarrandoId;
		a.AgarrandoId = 0;
		a.ModoDoAgarrao = ModoDeAgarrao.Nenhum;
		a.Estrangulando = false;
		if (id == 0) return;

		// PELA ZONA, e nao pelo dicionario -- ver o `TickDoAgarrao`. Um cadaver solto por aqui e o caso
		// mais comum do jogo (largar o corpo que se estava carregando).
		if (CorpoNaMinhaZona(a, id) is not { } d) return;
		LimparPreso(d);

		// AS FRASES MORAM AQUI, e nao nos chamadores, pela mesma razao dos campos: cada caminho de
		// soltura escrevendo o proprio par de frases seria o mesmo convite a divergencia -- e a que
		// mais custa e a do PRESO, porque ele e quem precisa entender por que ficou livre de repente.
		(string paraQuemSegurava, string? paraQuemFoiSolto) = motivo switch
		{
			MotivoDaSoltura.Tecla => ($"você solta {d.Name}.", $"{a.Name} solta você."),
			MotivoDaSoltura.Desfaleceu => ($"você desfalece e solta {d.Name}.",
										   $"{a.Name} desfalece e solta você."),
			MotivoDaSoltura.OutraZona => ($"o mundo muda sob os seus pés e você perde {d.Name}.", null),
			MotivoDaSoltura.Distancia => ($"{d.Name} escapa do seu alcance.",
										  $"você escapa do alcance de {a.Name}."),
			_ => ($"{d.Name} se solta do seu aperto!", $"você se solta de {a.Name}!"),
		};

		Avisar(a, paraQuemSegurava);
		if (paraQuemFoiSolto != null) AvisarSePessoa(d, paraQuemFoiSolto);

		// A FICHA DO SOLTO SAI AGORA, e nao no tique de 5 Hz: sem ela o cliente dele continuaria por
		// ate 200 ms recebendo correcao e recusa de passo depois de ja estar livre. Mesma armadilha
		// que o fim do arremesso ja documenta.
		MandarFicha(d);
	}

	/// <summary>
	/// LIMPA O LADO DE QUEM ESTAVA PRESO. Separado do <see cref="Soltar"/> porque a varredura orfa
	/// chega aqui SEM ter um agarrador vivo pra limpar do outro lado -- e e justamente esse caso
	/// (o corpo de quem segurava sumiu do mundo) que nao pode deixar ninguem congelado.
	/// </summary>
	private static void LimparPreso(ServerPlayer d)
	{
		d.AgarradoPorId = 0;
		d.ForcaDeQuemMeSegura = 0;
		d.ContadorDaLuta = 0;
		d.DebatendoSe = false;
		d.OrcamentoPx = 0;   // sai do aperto sem credito acumulado, como sai do arremesso
	}

	/// <summary>
	/// Mensagem que so faz sentido pra quem tem alguem lendo a tela. Um NPC agarrado nao le nada, e
	/// o corpo morto muito menos -- mas os dois passam por todo o resto do sistema.
	/// </summary>
	private void AvisarSePessoa(ServerPlayer pl, string texto)
	{
		if (EhJogador(pl)) Avisar(pl, texto);
	}

	// =====================================================================
	// 3. O TIQUE
	// =====================================================================
	/// <summary>
	/// QUANTO FALTA PRO PROXIMO TIQUE DO DM. O `grab()` do original dorme `sleep(1)` = 0,1 s
	/// (`Grabbing.dm:207`), e e nessa cadencia que a forca se recalcula e que a luta pra escapar
	/// avanca. A POSICAO de quem e carregado, nao: essa e escrita no tique cheio, senao o corpo no
	/// colo ficaria 100 ms parado e daria um salto -- exatamente o defeito que o `TickDoEmpurrao`
	/// conta em detalhe.
	/// </summary>
	private double _relogioDoAgarrao;

	/// <summary>
	/// O TIQUE DO AGARRAO. Roda no tique CHEIO, logo depois do arremesso -- e o mesmo vizinho de
	/// sempre, porque e no funil dele que o arremesso deste sistema desemboca.
	/// </summary>
	private void TickDoAgarrao(double dt)
	{
		_relogioDoAgarrao += dt;

		// A FOLGA DE 1e-6 NAO E ENFEITE, e o `TickDoEmpurrao` ja a tem pelo mesmo motivo: tres tiques
		// de `1f/30` somam 0,09999999 e nao 0,1. Sem ela o tique do DM cairia ora em 3 ora em 4
		// tiques do servidor, e a luta pra escapar avancaria a ~9 Hz em vez de 10 -- 10% mais lenta
		// que o original, calada.
		bool tiqueDoDm = _relogioDoAgarrao >= Empurrao.SegundosPorTique - 1e-6;
		if (tiqueDoDm) _relogioDoAgarrao -= Empurrao.SegundosPorTique;

		long agora = NowMs();

		// ---------------------------------------------------------------
		// PASSADA 1 -- do lado de quem SEGURA
		// ---------------------------------------------------------------
		foreach (ServerPlayer a in _players.Values)
		{
			if (a.AgarrandoId == 0) continue;

			// ============================ AS CINCO SOLTURAS AUTOMATICAS -- `Grabbing.dm:192` ============================
			// La sao quatro (`KO || z diferente || totalTime==0 || alvo saiu da frente`); aqui sao
			// cinco porque o port tem um caso que o BYOND nao tem -- o corpo que **sumiu do mundo**
			// (logout, remocao de NPC morto). No DM um mob deletado leva a referencia junto; aqui o
			// id fica apontando pra um fantasma.
			//
			// **A ORDEM E: PERGUNTE PRIMEIRO, SOLTE SEMPRE.** Nao ha ramo em que a duvida mantenha o
			// aperto de pe -- *"prefira soltar"*.
			// =========================================================================================================
			// O PRESO E RESOLVIDO PELA ZONA, e o aperto so existe confirmado dos dois lados -- ver `QuemEuSeguro`.
			if (QuemEuSeguro(a) is not { } d)
			{
				// o outro corpo nao existe mais (ou ja foi solto por outra ponta): limpa o meu lado
				a.AgarrandoId = 0;
				a.ModoDoAgarrao = ModoDeAgarrao.Nenhum;
				a.Estrangulando = false;
				continue;
			}

			// NOCAUTE E MORTE DE QUEM SEGURA -- `if(KO...)` de `:192`. Braco desmaiado nao segura.
			// (A morte do PRESO nao esta aqui, e a ausencia e a decisao central deste arquivo -- ver
			// o cabecalho: no DM `Death.dm:24-28` solta a forca, e o dono quer o oposto.)
			if (a.Ficha.KO || (a.Ficha.dead && !a.MortoDePe))
			{ Soltar(a, MotivoDaSoltura.Desfaleceu); continue; }

			// ZONA DIFERENTE -- o `grabbee.z != usr.z` de `:192`. Cobre de uma vez a passagem, o
			// pouso, a nave, a Sala do Tempo, a mente e a viagem pro Outro Mundo: nenhum desses
			// caminhos precisou de uma linha propria, porque todos terminam numa zona diferente.
			if (a.Zone.Hash != d.Zone.Hash)
			{ Soltar(a, MotivoDaSoltura.OutraZona); continue; }

			// ============================ SAIU DA FRENTE (SO NO MODO 1) ============================
			// `Grabbing.dm:192` escreve `!grabbee in get_step(src,dir)`. Em DM o `!` casa ANTES do
			// `in`, entao a expressao testa `(!grabbee) in ...` -- ou seja **essa condicao nunca
			// funcionou no original**. O que se porta e a INTENCAO, nao o texto: segurar exige
			// continuar ao alcance, e quem e carregado esta colado (nao ha "sair da frente").
			//
			// E ela e a que fecha o buraco do `escapechance = 9999` (o "far away man" de `:228`):
			// sem esta linha, quem fosse afastado sem se debater ficaria preso a distancia pra
			// sempre, porque a soltura por distancia so acontece dentro da LUTA.
			// ======================================================================================
			// O BRACO ESTICADO (lote G11) segura a DISTANCIA por definicao: o `stretch_arms()` do DM nao
			// tem regra de alcance no laco (`namekian.dm:245-276`) -- so nocaute, andar e `totalTime`. O
			// que ele tem e o alcance do INSTANTE de agarrar (`target in view(screenx)`, `Grabbing.dm:97`),
			// e e esse que vale aqui: o braco vai ate onde a vista vai.
			float alcanceDoAperto = _agarroesEsticadosG11.Contains(a.Id) ? RaioDaVista : CombatKnobs.Alcance;
			if (a.ModoDoAgarrao == ModoDeAgarrao.Segurando
				&& (!AlcancaPelaAltura(a, d)
					|| (d.Pos - a.Pos).LengthSquared > alcanceDoAperto * alcanceDoAperto))
			{ Soltar(a, MotivoDaSoltura.Distancia); continue; }

			// ---- o aperto, recalculado ---- `grabbee.grabberSTR = (Ephysoff*expressedBP)` (`:188`)
			if (tiqueDoDm)
			{
				double forca = Agarrao.Forca(a.Ficha) / FatorDoBracoG11(a);   // braco esticado: um terco (lote G11)
				if (a.ModoDoAgarrao == ModoDeAgarrao.Carregando) forca *= Agarrao.ApertoDeQuemCarrega;
				d.ForcaDeQuemMeSegura = forca;
			}

			// ---- ANDOU SEGURANDO? ARREMESSA ---- `mob/OnStep()` (`Throw.dm:1-3`)
			if (a.ModoDoAgarrao == ModoDeAgarrao.Segurando && a.Moving)
			{ ArremessarComOAgarrao(a, d); continue; }

			// ---- CARREGANDO: o corpo vem junto, e vem com a ALTURA ----
			if (a.ModoDoAgarrao == ModoDeAgarrao.Carregando) LevarNoColo(a, d, agora);

			// ---- a luta pra escapar, e o estrangulamento ----
			if (tiqueDoDm) LutaPraEscapar(a, d);
		}

		// ---------------------------------------------------------------
		// PASSADA 2 -- a VARREDURA ORFA, do lado de quem esta preso
		// ---------------------------------------------------------------
		// ============================ ELA E A REDE DE SEGURANCA, E TEM QUE EXISTIR ============================
		// A passada 1 so alcanca quem AINDA esta na lista. Se o corpo de quem segurava sair do mundo
		// -- deslogou, era NPC e foi removido, era um boneco que voltou pro dono --, ninguem mais
		// itera aquele agarrao e o preso fica com `AgarradoPorId` apontando pra um id que nao existe:
		// `PodeMexerOCorpo` recusa o passo dele **pra sempre**, e nao ha nada na tela dizendo por que.
		//
		// E a mesma disciplina do `ArrastoRestante` (que virou PRAZO justamente pra nao depender de
		// alguem lembrar de apaga-lo), resolvida aqui pela outra ponta: se o aperto nao e confirmado
		// dos dois lados, ele nao existe.
		// ==================================================================================================
		//
		// E ELA VARRE **TODO CORPO DO MUNDO**, e nao o `_players`: quem fica preso e justamente quem nao
		// e simulado -- o boneco de quem esta em transe e o CADAVER. Varrendo so o dicionario, um
		// cadaver com `AgarradoPorId` orfao nunca seria limpo, e o `CorpoNaFrente` (que recusa quem ja
		// esta preso) o tornaria **impossivel de agarrar pra sempre** -- ou seja, um corpo que ninguem
		// consegue mais tirar do caminho nem enterrar levando embora. Ver `TodosOsCorpos`.
		foreach (ServerPlayer d in TodosOsCorpos())
		{
			if (d.AgarradoPorId == 0) continue;
			if (QuemMeSegura(d) != null) continue;

			LimparPreso(d);
			MandarFicha(d);
			GD.Print($"[server] {d.Name} foi solto: quem o segurava nao esta mais no mundo");
		}
	}

	// =====================================================================
	// 4. CARREGAR -- a metade que o DM nao tem
	// =====================================================================
	/// <summary>
	/// ============================ O CORPO NO COLO -- `grabbee.loc = locate(x,y,z)` (`Grabbing.dm:203`) ============================
	/// No BYOND sao tres coordenadas e acabou, porque la **nao existe altura**: voar e um booleano, e
	/// o `grabMode==2` nunca escreve a flag `flying` do carregado. Por isso o pedido do dono --
	/// *"se vc apertar o botao de grab 2 vezes e VOAR, a pessoa agarrada vai ser considerada como
	/// VOANDO tb NA MESMA ALTITUDE q vc"* -- e a metade NOVA deste sistema.
	///
	/// ============================ E ELA NAO ESCREVE UMA SEGUNDA ALTURA ============================
	/// O carregado recebe o `Altitude` de quem carrega, e **so isso**. Todo o resto se deriva sozinho
	/// pelas funcoes que ja mandam nesses assuntos:
	///
	///   * <c>Voo.Andar(Altitude)</c> -- em que andar ele esta pro alcance de soco e pra vista;
	///   * <c>ClasseDeAgua.ModoDe(...)</c> (via `ModoDeTravessiaDe`) -- a agua deixa de barra-lo,
	///     porque ele esta no ar. **Este e o "modo de travessia" do pedido**, e ele nao foi escrito:
	///     ele e consequencia da altura, exatamente como e pra quem voa por conta propria;
	///   * <c>EntityState.Voando = Altitude > 0</c> (`GameServer.cs`) -- o cliente ja desenha a pose
	///     de voo e a sombra por ALTURA e nao pelo campo `Voando`. Ou seja o corpo no colo aparece
	///     voando pra todo mundo sem um bit novo no protocolo.
	///
	/// O `Nadando` VIAJA JUNTO pela mesma razao, e e o unico estado que precisou ser copiado: nadar
	/// e a terceira classe de travessia e ela NAO se deriva de altura (ver `Core/World/Agua.cs`) --
	/// sem esta linha, quem entrasse no lago carregando alguem veria o carregado ser barrado pela
	/// agua que o proprio corpo dele ja atravessou.
	///
	/// **O QUE NAO SE COPIA E O `Voando`.** Quem esta no colo nao paga Ki de voo: o carregador e que
	/// esta voando, e cobrar dos dois seria cobrar duas vezes pelo mesmo par de pes fora do chao. E
	/// por isso o <see cref="TickDoVoo"/> tem que pular quem esta sendo carregado -- senao ele veria
	/// "altura sem voo" e trataria como QUEDA, puxando o corpo pro chao 16 tiles por segundo enquanto
	/// esta funcao o traz de volta pra cima. Os dois brigariam pelo mesmo campo, trinta vezes por
	/// segundo.
	/// ==========================================================================================================================
	/// </summary>
	private void LevarNoColo(ServerPlayer a, ServerPlayer d, long agora)
	{
		d.Altitude = a.Altitude;
		d.Nadando = a.Nadando;

		// `grabbee.dir = turn(dir,180)` (`Grabbing.dm:190`) -- carregado de costas pra quem carrega.
		d.Facing = DeCostasPara(a.Facing);

		// O `Moving` VEM DE QUEM CARREGA, e nao e desleixo: o corpo no colo se DESLOCA (a posicao dele
		// e reescrita todo quadro), e um corpo que se desloca com `Moving` falso e exatamente o "sai
		// deslizando" que o `EntityState.SemRedeas` ja documenta -- posicao andando e animacao parada.
		// Enquanto nao houver uma pose de "carregado", a animacao de quem anda e a leitura menos errada.
		d.Moving = a.Moving;

		// O CARIMBO DE SEQUENCIA e o que impede os pacotes de input que o cliente do carregado JA
		// MANDOU (com a posicao antiga) de serem lidos como cliente errado e puxarem o corpo de volta
		// pro chao. Sem ele, quem e levado pelo ar vem sacudindo. E o funil de todo teleporte.
		CravarPosicao(d, a.Pos);
	}

	/// <summary>O `turn(dir,180)` do BYOND, nas quatro direcoes que o sprite tem.</summary>
	private static Facing DeCostasPara(Facing f) => f switch
	{
		Facing.North => Facing.South,
		Facing.South => Facing.North,
		Facing.East => Facing.West,
		_ => Facing.East,
	};

	// =====================================================================
	// 5. ARREMESSAR -- pelo funil que ja existe
	// =====================================================================
	/// <summary>
	/// ============================ O ARREMESSO NAO GANHOU FUNIL PROPRIO ============================
	/// Ele termina em <c>Arremessar(...)</c> -- o mesmo metodo do soco e do sopro, o unico do jogo
	/// que poe um corpo no ar (ver o cabecalho dele em `GameServer.Empurrao.cs`, que explica por que
	/// isso virou metodo). Dai pra frente o corpo jogado herda TUDO de graca: o passo fatiado a 30 Hz,
	/// o sulco no chao a cada tile, a parede derrubada pela forca, a cratera e a fumaca no fim -- e,
	/// o que mais importa pro pedido 2 do dono, **o dano nos DOIS quando ele acerta outro corpo**
	/// (`GameServer.Empurrao.cs:216-225`, o `Movement Effects.dm:77-81` do original).
	///
	/// O que este metodo faz e so a parte que e do agarrao: as tres grandezas do `Throw.dm` e o dano
	/// de partida.
	/// ==========================================================================================
	/// </summary>
	private void ArremessarComOAgarrao(ServerPlayer a, ServerPlayer d)
	{
		int tiques = Agarrao.TiquesDoArremesso(a.Ficha, d.Ficha, (lo, hi) => _rng.Next(lo, hi + 1));
		double forca = Agarrao.ForcaDoArremesso(a.Ficha);
		double dano = Agarrao.DanoDoArremesso(a.Ficha, d.Ficha);

		// SOLTA ANTES DE JOGAR, e a ordem importa: o `Arremessar` manda a ficha do corpo jogado, e ela
		// tem que sair ja com o aperto desfeito. Alem disso `LimparPreso` zera o orcamento de passo --
		// e o voo comeca do zero, como todo arremesso do jogo.
		a.AgarrandoId = 0;
		a.ModoDoAgarrao = ModoDeAgarrao.Nenhum;
		a.Estrangulando = false;
		LimparPreso(d);

		// `damage_mob(grabbee, dmg*BPModulus(...)/4)` (`Throw.dm:27`) -- espalhado pelo corpo, que e
		// o que o `damage_mob` do original faz. O baque do FIM do voo e outro, e sai do `Espalhar`
		// do proprio funil de arremesso.
		Espalhar(d, dano);

		Arremessar(d, MeleeArea.Frente(a.Facing), forca, tiques);

		Avisar(a, $"você arremessa {d.Name}!");
		AvisarSePessoa(d, $"{a.Name} arremessa você!");
		GD.Print($"[server] {a.Name} ARREMESSOU {d.Name} ({tiques} tiques, forca {forca:0})");
	}

	// =====================================================================
	// 6. A LUTA PRA ESCAPAR -- `movement handler.dm:222-255`
	// =====================================================================
	/// <summary>
	/// ============================ ESCAPAR E ANDAR, E ISSO E DO ORIGINAL ============================
	/// La o bloco inteiro mora DENTRO do laco de movimento, no ramo `if(grabParalysis)` -- ou seja,
	/// so avanca quando a vitima aperta uma direcao. Nao ha tecla de escapar e nao ha sorteio de
	/// libertacao: ha um contador que sobe enquanto se luta.
	///
	/// Aqui o gesto e o mesmo. O pacote de input do preso e recusado inteiro pelo `PodeMexerOCorpo`
	/// (ele nao anda), mas a INTENCAO e lida antes da recusa e guardada em
	/// <see cref="ServerPlayer.DebatendoSe"/> -- ver `GameServer.Input`. E esse bit que este metodo
	/// consome, uma vez por tique do DM.
	/// ==========================================================================================
	/// </summary>
	private void LutaPraEscapar(ServerPlayer a, ServerPlayer d)
	{
		// ---- O ESTRANGULAMENTO acontece no tique de movimento da VITIMA (`movement handler.dm:193`),
		//      e nao no de quem aperta: `prob(5)` por tique, com o `grabCounter` somando no dano.
		if (a.Estrangulando && _rng.NextDouble() * 100 < Agarrao.ChanceDeAfogar)
		{
			Espalhar(d, Agarrao.DanoDeAfogamento(a.Ficha, d.Ficha, d.ContadorDaLuta));
			AvisarSePessoa(d, "você não consegue respirar!");
		}

		if (!d.DebatendoSe) return;
		d.DebatendoSe = false;

		bool longe = (d.Pos - a.Pos).LengthSquared
					 >= 2 * ZoneCollision.TileSize * (2 * ZoneCollision.TileSize);
		double escape = Agarrao.ChanceDeEscapar(d.Ficha, a.Ficha, d.ForcaDeQuemMeSegura,
												longe, a.ModoDoAgarrao);

		d.ContadorDaLuta += Agarrao.PassoDaLuta(d.Ficha);

		// `grabber.stamina -= min(0.10*escapechance, 2)` (`:235`) -- segurar quem se debate CANSA.
		a.Ficha.stamina = Math.Max(0, a.Ficha.stamina - Agarrao.CustoDeSegurar(escape));

		// `if(prob(escapechance*grabCounter)) grabCounter += 2` (`:237`) -- o empurrao de sorte, que
		// se realimenta: quanto mais perto de sair, mais chance de dar o salto.
		if (_rng.NextDouble() * 100 < escape * d.ContadorDaLuta)
			d.ContadorDaLuta += Agarrao.SaltoDeSorte;

		// O ALVO DO CONTADOR: `20/escapechance` (`:238`). O piso da chance (que e do port, ver
		// `Agarrao.EscapeMinimo`) e o que torna este alvo FINITO em qualquer abismo de poder.
		if (d.ContadorDaLuta >= Agarrao.ContadorParaEscapar / escape)
		{
			Soltar(a, MotivoDaSoltura.Escapou);
			return;
		}

		// FALHOU: o contador sobe mais (`:253-255`). E o que garante progresso mesmo sem sorte.
		d.ContadorDaLuta += Agarrao.PassoDeQuemFalhou(d.Ficha);
		AvisarSePessoa(d, $"você se debate contra {a.Name}!");
	}

	// =====================================================================
	// 7. O ESTRANGULAMENTO -- o que o AGARRADOR pode fazer
	// =====================================================================
	/// <summary>O `spawn(5)` do `choke_cooldown` -- meio segundo. Ver <see cref="ServerPlayer.AfogamentoLivreEm"/>.</summary>
	private const long MsEntreTrocasDeAperto = 500;

	/// <summary>
	/// SOCAR COM ALGUEM NA MAO NAO SOCA: ALTERNA O AFOGAMENTO. E o `attack_bck.dm:43-57` do original,
	/// e ele mora no comeco do `Attack()` de la -- por isso a chamada fica no comeco do
	/// <c>Atacar</c> daqui.
	///
	/// Devolve `true` quando o golpe foi consumido pelo estrangulamento.
	/// </summary>
	private bool EstrangularEmVezDeSocar(ServerPlayer a)
	{
		if (a.AgarrandoId == 0) return false;

		// O GOLPE CONTINUA CONSUMIDO durante a recarga (o `return` do original acontece nos dois
		// ramos): com alguem na mao nao se soca, e apertar rapido demais so nao alterna.
		long agora = NowMs();
		if (agora < a.AfogamentoLivreEm) return true;
		a.AfogamentoLivreEm = agora + MsEntreTrocasDeAperto;

		a.Estrangulando = !a.Estrangulando;
		if (QuemEuSeguro(a) is { } d)
		{
			Avisar(a, a.Estrangulando
				? $"você aperta o pescoço de {d.Name}."
				: $"você afrouxa o aperto em {d.Name}.");
			if (a.Estrangulando) AvisarSePessoa(d, $"{a.Name} aperta o seu pescoço!");
		}
		return true;
	}
}
