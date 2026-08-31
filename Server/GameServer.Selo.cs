using Godot;
using Jandirus.Core;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// O SELO -- o corpo preso num bolso, o pote que quebra e a fuga por poder.
///
/// ============================ DE ONDE ISTO SAIU, INTEIRO ============================
/// `Code/Modules/Magic/Sealing.dm`, 336 linhas, e um subsistema fechado: o estado no mob
/// (`:22-74`), o pote (`:78-144`) e tres habilidades penduradas nele (Mafuba `:146-226`, Open Dead
/// Zone `:229-298`, Superior Seal `:302-336`).
///
/// **DUAS DAS TRES ESTAO AQUI.** O Superior Seal NAO: ele nao usa o motor de selo pelo caminho de
/// cima -- ele monta um `/obj/Ritual`, converte Ki em mana (`convert_ki_to_magic_e`) e delega a
/// `do_tietary("e_seal_s")`, cuja forca e `((Magic - alvo.Magic) * magnitude) + log(...)`
/// (`Rituals_Manipulation.dm:327-343`). E uma disputa de MANA, e este port tem do sistema de magia
/// so o TETO (`Fighter.MagicCap`), sem pool corrente, sem ritual e sem palavra de poder. Entregar
/// metade dele seria inventar a metade que falta. Ele fica catalogado como divida em
/// `CensoDeSkills.Esperando` (o `SistMagia`), que e onde ja moram os outros sete verbos de magia.
/// ====================================================================================
///
/// ============================ AS TRES COISAS QUE ESTE ARQUIVO GUARDA ============================
///   1. O ESTADO, que e do Core (<see cref="Selo"/>) e vai pro disco -- selo nao morre no logout;
///   2. OS QUATRO RELOGIOS, que sao do servidor e tem cadencias DIFERENTES, todas do DM:
///        * teste de fuga  -- 0,3 s  (`Stats.dm:64` dentro do laco que dorme 3)
///        * tique do pote  -- 5,0 s  (`Sealing.dm:133`, `sleep(50)`)
///        * fita do Mafuba -- 0,2 s  (`:209`, `sleep(2)`), e ela morre aos 7 s (`:182`)
///        * portal da Dead Zone -- 0,1 s (`:291`), e ele morre aos 10 s (`:298`)
///   3. O VINCULO POTE-PRESO, que no DM e uma string sorteada (`signature`) e aqui e o `Obra.Id`.
/// ==============================================================================================
///
/// ============================ TRES DESVIOS DECLARADOS, E POR QUE ============================
///   * **O POTE COM ALGUEM DENTRO NAO SE CARREGA.** No DM da pra por o pote na mochila e o
///     `checkdur` reescreve o ponto de volta do preso pro lugar do pote (`:122`) -- levar o pote e
///     levar o prisioneiro. Aqui um pote no chao e uma `Obra` (com `Id` proprio, gravada no
///     `mundo.json`) e um pote na mochila e um ITEM DE PILHA, sem identidade nenhuma: recolher um
///     pote selado apagaria o vinculo e deixaria um preso sem carcere. Enquanto houver alguem
///     dentro, ele fica pregado no chao -- e quebra-lo continua sendo o jeito de soltar, que e a
///     interacao principal do original (o verb `Destroy_Container`, `:97-104`).
///   * **A ZONA DO SELO E UMA SO, COMPARTILHADA.** E a decisao do proprio autor do DM, escrita na
///     primeira linha do arquivo: *"The reason why the Dead Zone and Mafuba Jars and etc would be
///     combined is just to allow OOC/slightly IC character interactions."* Presos se veem e se
///     falam. Ela usa a mesma `ZoneKey.Interior("Selado", ...)` da fusao, com **seed 0** -- e o id
///     de jogador comeca em 1 (`GameServer._nextId`), entao o bolso do selo nunca colide com o
///     bolso de uma fusao.
///   * **A DIVISAO POR ZERO DO `TestEscape`** virou guarda. Ver <see cref="Selo.Corrosao"/>.
/// ==========================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// 0. O LUGAR, E OS PRAZOS
	// =====================================================================
	/// <summary>
	/// O CHAO DOS SELADOS -- um so pra todo mundo. Ver o desvio declarado no cabecalho.
	///
	/// Reaproveita o nome `"Selado"` de proposito: e ele que o <see cref="EhOSelo"/> reconhece, e e
	/// pelo `EhOSelo` que a colisao da zona cai na planta da Dimensao Mental
	/// (`GameServer.Volta.cs:195`). Um nome novo entraria num bolso sem chao.
	/// </summary>
	internal static ZoneKey ZonaDosSelados => ZoneKey.Interior(NomeDoSelo, 0);

	/// <summary>`Stats.dm:64` -- `if(isSealed) spawn TestEscape()`, no laco que dorme 3 (0,3 s).</summary>
	private const double PassoDoTesteDeFuga = TempoDoDm.SegundosDoLacoGlobalStats;

	/// <summary>`Sealing.dm:133` -- `sleep(50)` entre uma volta do `checkdur()` e a proxima.</summary>
	private const double PassoDoPote = 50 / TempoDoDm.TiquesPorSegundo;

	/// <summary>`Sealing.dm:209` -- `sleep(2)` entre duas checagens de raio da fita.</summary>
	private const double PassoDaFita = 2 / TempoDoDm.TiquesPorSegundo;

	/// <summary>`Sealing.dm:182` -- `spawn(70) del(A)`: a fita do Mafuba vive 7 segundos.</summary>
	private const double VidaDaFita = 70 / TempoDoDm.TiquesPorSegundo;

	/// <summary>`Sealing.dm:291` -- `sleep(1)` dentro do `spawn while(src)` do portal.</summary>
	private const double PassoDoPortal = 1 / TempoDoDm.TiquesPorSegundo;

	/// <summary>`Sealing.dm:298` -- `spawn(100) del(src)`: o portal vive 10 segundos.</summary>
	private const double VidaDoPortal = 100 / TempoDoDm.TiquesPorSegundo;

	/// <summary>
	/// A VELOCIDADE DA FITA DO MAFUBA, em tiles por segundo.
	///
	/// `walk_to(A, thesealed)` (`Sealing.dm:183`) sem `Lag`: no BYOND isso e um passo por TIQUE DO
	/// MUNDO, e o mundo deste jogo roda a `fps = 12` (`Modules/Globals/World.dm:5`). Doze tiles por
	/// segundo -- rapida, e por isso a fita quase nunca erra: em 7 s de vida ela cobre 84 tiles.
	///
	/// **Nao e o <see cref="TempoDoDm.TiquesPorSegundo"/> (10)**, e a diferenca e proposital: aquele
	/// e o divisor do `sleep`/`spawn` (deciseundos), este e a cadencia do `walk`. Sao dois relogios
	/// distintos no BYOND, e confundi-los daria uma fita 20% lenta.
	/// </summary>
	private const double TilesPorSegundoDaFita = 12.0;

	/// <summary>
	/// `if(get_dist(src.loc,M.loc)<= 1)` (`Sealing.dm:198`) -- a fita sela a UM tile de distancia,
	/// nao encostada.
	/// </summary>
	private const double AlcanceDaFita = 1.0;

	/// <summary>`oview(12,src)` (`Sealing.dm:292`) -- quem o portal puxa.</summary>
	private const double RaioDeSuccao = 12.0;

	/// <summary>`prob(20)` (`:292`) -- a chance, por passo, de o portal puxar cada corpo.</summary>
	private const int ChanceDeSuccao = 20;

	/// <summary>
	/// `A.loc = locate(usr.x, usr.y+5, usr.z)` (`Sealing.dm:250`) -- CINCO tiles ao norte, sempre.
	/// Nao e "a frente": o portal ignora a direcao de quem o abriu, e isso e do original.
	/// </summary>
	private const int TilesAoNorteDoPortal = 5;

	// =====================================================================
	// 1. SELAR E SOLTAR
	// =====================================================================
	/// <summary>
	/// `SealMob(SealingPersonBP, ContainerDur)` -- `Sealing.dm:31-42`.
	///
	/// A parte de aritmetica mora no Core (<see cref="Selo.Selar"/>); o que sobra aqui e o mundo:
	/// guardar de onde a pessoa saiu e leva-la pro chao dos selados. O `spawn(1) TestEscape()` de
	/// `:42` nao tem irmao -- neste port o teste e um relogio do tique e nao um agendamento, e o
	/// primeiro passo dele chega em 0,3 s de qualquer jeito.
	/// </summary>
	private void Selar(ServerPlayer preso, double bpDeQuemSelou, double duracaoDoPote, int poteId)
	{
		preso.Selo.Selar(bpDeQuemSelou, duracaoDoPote, poteId, preso.Zone, preso.Pos.X, preso.Pos.Y);
		MoveToZone(preso.Id, ZonaDosSelados, PosDoSelo);

		Avisar(preso, duracaoDoPote > 0
			? "o mundo se fecha em volta de você. Você está SELADO -- só sai daqui ficando 25% mais "
			+ "forte que o selo, ou se alguém quebrar o pote."
			: "o mundo se fecha em volta de você. Você está SELADO, e não há pote nenhum pra "
			+ "quebrar: a única saída é ficar 25% mais forte que quem te prendeu.");

		GD.Print($"[server] SELO: {preso.Name} preso (selo {preso.Selo.BpDoSelador:N0} BP, "
				 + $"pote #{poteId}, dur {duracaoDoPote:0.##})");
	}

	/// <summary>
	/// `UnSealMob()` -- `Sealing.dm:67-74`.
	///
	/// `if(isnull(SealedLocation)) src.GotoPlanet(spawnPlanet)` (`:68`): sem ponto de volta gravado,
	/// a pessoa vai pro berco -- e o berco deste port e o <see cref="MandarProBerco"/>, o MESMO
	/// funil de quem renasce (que ainda olha se a pessoa escolheu um dominio conquistado). A guarda
	/// importa de verdade: um save gravado por uma versao anterior pode trazer `Preso` sem volta.
	/// </summary>
	private void SoltarDoSelo(ServerPlayer preso, string motivo)
	{
		ZoneKey volta = preso.Selo.ZonaDeVolta;
		var pos = new Vec2(preso.Selo.VoltaX, preso.Selo.VoltaY);
		bool temVolta = volta.Name.Length > 0;

		preso.Selo.Soltar();

		// O MUNDO DE VOLTA PODE TER ACABADO ENQUANTO ELE ESTAVA SELADO. Mesma porta do Templo e das
		// cavernas -- ver `SaidaParaUmMundoMorto`. O selo e o caso mais provavel dos tres, porque ele
		// dura o tempo que o selador quiser: e a unica prisao do jogo em que dias podem passar.
		if (!temVolta) MandarProBerco(preso);
		else if (!SaidaParaUmMundoMorto(preso, volta)) MoveToZone(preso.Id, volta, pos);

		Avisar(preso, $"o selo se rompe: {motivo}");
		GD.Print($"[server] SELO: {preso.Name} saiu ({motivo})");
	}

	/// <summary>
	/// `if(isSealed) spawn TestEscape()` no login (`Login.dm:258`).
	///
	/// Sem isto, entrar no mundo selado poria o corpo na ULTIMA zona gravada -- que e o chao dos
	/// selados, sim, mas sem ninguem testando a fuga: o relogio deste arquivo so anda pra quem esta
	/// marcado, e o marcado veio do disco. Esta chamada e o que amarra o disco ao relogio.
	/// </summary>
	private void SeloNoLogin(ServerPlayer pl)
	{
		if (!pl.Selo.Preso) return;

		// `if(Planet!="Sealed") GotoPlanet("Sealed")` (`:63-64`): entrou selado, volta pro selo.
		if (!EhOSelo(pl.Zone)) MoveToZone(pl.Id, ZonaDosSelados, PosDoSelo);

		Avisar(pl, "você continua selado. O selo não se desfaz por você ter saído do mundo.");
	}

	// =====================================================================
	// 2. OS QUATRO RELOGIOS
	// =====================================================================
	private double _relogioDaFuga, _relogioDoPote, _relogioDaFita, _relogioDoPortal;

	/// <summary>
	/// O TIQUE DO SELO INTEIRO. Chamado do <see cref="Tick"/>, e ele so trabalha quando ha trabalho:
	/// sem preso, sem fita e sem portal, sao tres comparacoes de double por quadro.
	/// </summary>
	private void TickDoSelo(double dt)
	{
		// A FITA E O PORTAL PRIMEIRO, e a ordem tem razao: os dois SELAM, e quem for selado neste
		// quadro deve ser testado ja neste quadro pelo teste de fuga -- e nao no proximo, com o
		// corpo meio dentro e meio fora do bolso.
		_relogioDaFita += dt;
		if (_relogioDaFita >= PassoDaFita) { _relogioDaFita = 0; PassoDasFitas(); }

		_relogioDoPortal += dt;
		if (_relogioDoPortal >= PassoDoPortal) { _relogioDoPortal = 0; PassoDosPortais(); }

		_relogioDaFuga += dt;
		if (_relogioDaFuga >= PassoDoTesteDeFuga) { _relogioDaFuga = 0; PassoDaFuga(); }

		_relogioDoPote += dt;
		if (_relogioDoPote >= PassoDoPote) { _relogioDoPote = 0; PassoDosPotes(); }
	}

	/// <summary>
	/// `TestEscape()` (`Sealing.dm:44-66`) por preso. A decisao e do Core; aqui mora o mundo.
	///
	/// A varredura `for(var/obj/items/SealingItem/O in world)` de `:54-57` vira uma busca por `Id`
	/// na lista de obras -- e ela so acontece quando o Core pergunta (pote danificado), e nao toda
	/// volta: o laco do DM dorme 1 tique POR OBJETO de selo do mundo inteiro, o que naquele jogo era
	/// um jeito discreto de o servidor engasgar com dez potes.
	/// </summary>
	private void PassoDaFuga()
	{
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			if (!pl.Selo.Preso) continue;

			// `if(Planet!="Sealed") GotoPlanet("Sealed")` (`:63-64`) -- saiu do bolso por qualquer
			// via (admin, morte, passagem), volta. E o que impede o selo de virar decoracao.
			if (!EhOSelo(pl.Zone)) MoveToZone(pl.Id, ZonaDosSelados, PosDoSelo);

			bool poteVivo = pl.Selo.PoteId != 0 && _noChao.Any(o => o.Id == pl.Selo.PoteId);
			switch (pl.Selo.Passo(pl.Ficha.expressedBP, poteVivo))
			{
				case FimDoSelo.Solta:
					SoltarDoSelo(pl, "seu poder passou do selo");
					break;

				// `to_chat(src, "Your seal has weakened! ...")` (`:60`), uma vez so.
				case FimDoSelo.Enfraqueceu:
					Avisar(pl, "seu selo ENFRAQUECEU! O pote não está mais no mundo -- se ninguém "
							 + "o repuser, você volta ao mundo normal.");
					break;
			}
		}
	}

	/// <summary>
	/// `checkdur()` (`Sealing.dm:117-134`) por pote com alguem dentro.
	///
	/// TRES COISAS EM UMA: o pote empurra a propria durabilidade pro preso (`:121`), reescreve o
	/// ponto de volta dele pro lugar do pote (`:122`) e -- se a durabilidade zerou -- solta (`:127-130`).
	///
	/// A DURABILIDADE DO POTE NESTE PORT E A ARMADURA DA `Obra`, normalizada: `Armadura/ArmaduraMax`
	/// da exatamente a mesma escala 0..1 do `SealedContainerDur` do DM (que nasce em 1, `:96`, e
	/// desce 0,01 por ponto de dano, `:115`). Nao ha um segundo campo de vida do pote -- **um pote
	/// com duas barras de vida e uma barra que vai divergir**, e a armadura ja e a que o soco
	/// consulta (`GameServer.Estrago.Estragar`).
	/// </summary>
	private void PassoDosPotes()
	{
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			if (!pl.Selo.Preso || pl.Selo.PoteId == 0) continue;

			Obra? pote = _noChao.FirstOrDefault(o => o.Id == pl.Selo.PoteId);
			if (pote == null) continue;   // o `TestEscape` cuida do pote sumido -- ver `:51-62`

			pl.Selo.DuracaoDoPote = pote.ArmaduraMax > 0 ? pote.Armadura / pote.ArmaduraMax : 0;
			pl.Selo.GuardarVolta(pote.Zona, pote.X, pote.Y);
		}
	}

	/// <summary>
	/// O POTE FOI AO CHAO: `SealingItem/Del()` (`Sealing.dm:140-144`) -- quem estava dentro sai.
	///
	/// Chamado do <see cref="Estragar"/>, junto do irmao que cancela a fornada do Bio Lab, e pelo
	/// mesmo motivo: destruir a construcao TEM que desfazer o que ela segurava. Sem esta linha o
	/// preso ficaria no bolso pra sempre apontando pra um `PoteId` que nao existe -- e o
	/// `TestEscape` levaria HORAS pra corroer o selo (`0.001/0.25` por 0,3 s).
	/// </summary>
	private void PoteFoiAoChao(Obra pote)
	{
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			if (!pl.Selo.Preso || pl.Selo.PoteId != pote.Id) continue;
			SoltarDoSelo(pl, "o pote que te prendia foi destruído");
		}
	}

	/// <summary>
	/// ESTE POTE TEM ALGUEM DENTRO? Consultado por quem quer RECOLHER a obra -- ver o desvio
	/// declarado no cabecalho deste arquivo.
	/// </summary>
	private bool PoteEstaSelado(Obra o) =>
		_players.Values.Any(p => p.Selo.Preso && p.Selo.PoteId == o.Id);

	// =====================================================================
	// 3. A FITA DO MAFUBA
	// =====================================================================
	/// <summary>
	/// O `obj/attack/blast/MafubaBlast` (`Sealing.dm:192-210`) enquanto voa.
	///
	/// Guardado numa lista do servidor e nao na lista de projeteis por um motivo de fidelidade: o
	/// MafubaBlast do DM **nao passa pelo dano de tiro**. Ele nasce com `basedamage=0.1` (que e
	/// ruido) e tem `checkradius()` PROPRIO -- o que ele faz ao chegar nao e ferir, e selar. Enfiar
	/// isso no funil dos projeteis exigiria um segundo desfecho la dentro, num sistema que hoje tem
	/// um so e que outra gente mexe.
	/// </summary>
	private sealed class FitaDoMafuba
	{
		public int Dono, Alvo, PoteId;
		public ZoneKey Zona;
		public Vec2 Pos;
		public double Vida;
	}

	private readonly List<FitaDoMafuba> _fitas = [];

	/// <summary>
	/// `walk_to` + `checkradius` (`Sealing.dm:183-184`, `:197-210`), um passo.
	///
	/// A fita anda em direcao ao alvo na velocidade do `walk` do DM e sela quando chega a um tile.
	/// Ela NAO desiste se o alvo correr: `walk_to` persegue. O que a mata e o prazo (`:182`).
	/// </summary>
	private void PassoDasFitas()
	{
		if (_fitas.Count == 0) return;

		for (int i = _fitas.Count - 1; i >= 0; i--)
		{
			FitaDoMafuba f = _fitas[i];
			f.Vida -= PassoDaFita;

			if (!_players.TryGetValue(f.Alvo, out ServerPlayer? alvo)
				|| !alvo.Zone.Equals(f.Zona) || alvo.Selo.Preso || f.Vida <= 0)
			{
				if (f.Vida <= 0 && _players.TryGetValue(f.Dono, out ServerPlayer? quemErrou))
					Avisar(quemErrou, "a fita do Mafuba se desfaz sem alcançar ninguém.");
				_fitas.RemoveAt(i);
				continue;
			}

			// o passo do `walk_to`: um tile por tique do mundo, fatiado no passo desta lista
			float avanco = (float)(TilesPorSegundoDaFita * PassoDaFita * ZoneCollision.TileSize);
			Vec2 delta = new(alvo.Pos.X - f.Pos.X, alvo.Pos.Y - f.Pos.Y);
			float dist = Vec2.Distance(alvo.Pos, f.Pos);
			if (dist > 0.001f)
			{
				float passo = Math.Min(avanco, dist);
				f.Pos = new Vec2(f.Pos.X + delta.X / dist * passo, f.Pos.Y + delta.Y / dist * passo);
				dist = Vec2.Distance(alvo.Pos, f.Pos);
			}

			if (dist > AlcanceDaFita * ZoneCollision.TileSize) continue;

			// ---- CHEGOU: `M.SealMob(SealStrength,1)` (`:203`) ----
			//
			// `SealStrength` e a variavel que o DM DECLARA E NUNCA PREENCHE (`:194`) -- entra zero, e
			// zero vira o teto duro de 40 bilhoes. Ver `Selo.TetoDuro`, onde a escolha de manter o
			// defeito esta justificada.
			Selar(alvo, 0, 1, f.PoteId);
			foreach (ServerPlayer o in ZoneList(f.Zona.Hash))
				Avisar(o, $"{alvo.Name} foi selado pelo Mafuba!");

			// `icon_state = "Closed"` (`:120`): o pote fecha. O cliente redesenha pelo pacote de obras.
			if (_noChao.FirstOrDefault(x => x.Id == f.PoteId) is { } pote) MandarObras(pote.Zona);

			_fitas.RemoveAt(i);
		}
	}

	// =====================================================================
	// 4. O PORTAL DA DEAD ZONE
	// =====================================================================
	/// <summary>O `obj/DeadZone` (`Sealing.dm:255-298`) enquanto esta aberto.</summary>
	private sealed class PortalDaDeadZone
	{
		public int Dono;
		public double BpDeQuemAbriu;
		public ZoneKey Zona;
		public Vec2 Pos;
		public double Vida;
	}

	private readonly List<PortalDaDeadZone> _portais = [];

	/// <summary>
	/// O laco `spawn while(src)` do portal (`Sealing.dm:290-298`), um passo.
	///
	/// DOIS LACOS EM UM, e sao os dois do DM:
	///   * `oview(12)` com `prob(20)` e `!expandlevel` -> **arrasta** o corpo um passo pro portal;
	///   * `view(0)` -> quem chegou na celula do portal e SELADO com `SealMob(makerBP, 0)`.
	///
	/// O `!expandlevel` do original e "nao arrasta quem esta gigante". Este port nao tem o corpo
	/// expandido (a skill `Body_Expansion` esta catalogada como divida), entao a condicao nao tem
	/// como ser consultada -- fica anotada aqui pra o dia em que o tamanho existir.
	/// </summary>
	private void PassoDosPortais()
	{
		if (_portais.Count == 0) return;

		for (int i = _portais.Count - 1; i >= 0; i--)
		{
			PortalDaDeadZone p = _portais[i];
			p.Vida -= PassoDoPortal;
			if (p.Vida <= 0)
			{
				foreach (ServerPlayer o in ZoneList(p.Zona.Hash))
					Avisar(o, "a fenda da Dead Zone se fecha.");
				_portais.RemoveAt(i);
				continue;
			}

			foreach (ServerPlayer alvo in ZoneList(p.Zona.Hash).ToList())
			{
				if (alvo.Selo.Preso || alvo.Ficha.dead) continue;

				float dist = Vec2.Distance(alvo.Pos, p.Pos);

				// `for(var/mob/M in view(0,src))` (`:295`): a MESMA celula. Sela e sai.
				if (dist <= ZoneCollision.TileSize / 2f)
				{
					foreach (ServerPlayer o in ZoneList(p.Zona.Hash))
						Avisar(o, $"{alvo.Name} é sugado pra dentro da Dead Zone!");
					Selar(alvo, p.BpDeQuemAbriu, 0, 0);
					continue;
				}

				if (dist > RaioDeSuccao * ZoneCollision.TileSize) continue;
				if (_rng.Next(100) >= ChanceDeSuccao) continue;

				// `step_towards(M,src)` (`:294`): UM tile na direcao do portal, na marra.
				float passo = Math.Min(ZoneCollision.TileSize, dist);
				alvo.Pos = new Vec2(alvo.Pos.X + (p.Pos.X - alvo.Pos.X) / dist * passo,
									alvo.Pos.Y + (p.Pos.Y - alvo.Pos.Y) / dist * passo);
				ArrastadoPeloPortal(alvo);
			}
		}
	}

	/// <summary>
	/// O CORPO FOI MOVIDO SEM PEDIR LICENCA -- e o cliente precisa saber, senao ele desenha a pessoa
	/// no lugar antigo e o servidor a "corrige" de volta no quadro seguinte.
	///
	/// As quatro linhas antes do pacote sao as MESMAS do empurrao (`GameServer.Empurrao.cs:481-485`)
	/// e pelo mesmo motivo escrito la: os inputs que o cliente ja mandou falam da posicao ANTIGA, e
	/// sem carimbar o teleporte eles seriam lidos como cliente trapaceando. O pacote em si sai pelo
	/// <see cref="MandarCorrecaoG3"/> -- ele ja existe, e uma segunda copia dele e a chance de
	/// alguem escrever so a posicao e mandar o pacote malformado que aquele comentario descreve.
	/// </summary>
	private void ArrastadoPeloPortal(ServerPlayer pl)
	{
		long agora = NowMs();
		pl.LastInputMs = agora;
		pl.CorrecaoEsperadaAte = agora + 500;
		pl.SeqDoTeleporte = pl.SeqInput;
		pl.OrcamentoPx = 0;
		MandarCorrecaoG3(pl);
	}
}
