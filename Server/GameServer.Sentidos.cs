using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// OS SENTIDOS -- a aba SENSE (a leitura de Ki) e a aba SCAN (o scouter) do menu P. Sao o `ui_tab_sense`
/// (`HtmlUI.dm:360-398`) e o `ui_tab_scan` (`HtmlUI.dm:400-419`) do original, calculados AQUI e mandados
/// prontos pro cliente desenhar.
///
/// ============================ POR QUE O SERVIDOR CALCULA E O CLIENTE SO DESENHA ============================
/// O que estas abas mostram e informacao de OUTRAS pessoas: onde estao, quao fortes sao, quanto de vida
/// tem. Nada disso viaja no snapshot, de proposito (ver `Protocol.SheetState`: "BP alheio se descobre com
/// scouter ou sentido de ki, nunca de graca") -- entao o cliente nao tem de onde tirar a lista, e nao pode
/// ter: se tivesse, um cliente modificado leria todo mundo sem skill nenhuma. A lista nasce aqui, passa
/// pelo SIGILO (`GameServer.Sigilo`) e vai pronta, do mesmo jeito que o `ui_tab_*` do DM montava o HTML
/// no servidor e mandava a pagina.
///
/// ============================ (2026-10-18) OS CINCO NIVEIS DO SENSE -- e o que veio do DM, logo abaixo ============================
/// *"adicione niveis na skill de sense, assim aumentando a area de efeito, começando no campo de visao, dps regiao, dps planeta, dps
/// sistema, e dps o universo (no caso do universo o player so vai sentir chefes e jogadores...)"*. O alcance passou a ser o NIVEL DA SKILL
/// (`SenseEmNiveis`): campo de visao (15 tiles), regiao (60 tiles) -- os dois com NPC --, planeta (a zona inteira), sistema (os outros
/// planetas da mesma estrela e o espaco dela) e universo -- os tres so jogador e chefe. O contador `kiawarenessskill` da arvore da Mente
/// ficou como PISO (`NivelDoSense`). O bloco de baixo e o desenho do DM, que este substitui: os tres alcances pelo contador, o NPC so no
/// perto, e o piso dos 5 milhoes da galaxia -- que saiu.
/// =================================================================================================================================
///
/// ============================ O QUE CADA ALCANCE VIA NO DM (Sense2.0.dm:20-35) ============================
/// A skill Sense tem UM nivel so (`maxlevel = 1`, `Sense2.0.dm:8`); os tres alcances acendem pelo
/// CONTADOR `kiawarenessskill` da arvore da Mente, e nao pelo nivel dela:
///   * `gotsense`  -- `kiawarenessskill >= 1`  (`:22`): PERTO -- a mesma zona, ate 15 tiles (`HtmlUI.dm:371`);
///                    o nome (so de quem se conhece), o poder RELATIVO ao meu e a vida em %;
///   * `gotsense2` -- `kiawarenessskill >= 20` (`:27`): NESTE MUNDO -- a zona inteira, com distancia e rumo;
///   * `gotsense3` -- `kiawarenessskill >= 60` (`:32`): NA GALAXIA -- qualquer zona, so quem tem BP acima de
///                    5.000.000 (`HtmlUI.dm:389`), com o lugar e sem coordenada ("(?,?,z)").
/// O primeiro alcance vale sempre que o bit `Poder.Sense` esta aceso (a skill esta no livro -- e a porta
/// que o port ja usava pra aba existir, `GameServer.Skills.cs`, `AplicarPoderes`); os outros dois pelo
/// contador, que a arvore da Mente escreve (`--menteskills`, `--arvoreteste`).
///
/// QUEM NAO SE SENTE, literal do DM (`HtmlUI.dm:369-370`, os mesmos tres da telepatia -- ver
/// <see cref="AchoAEnergiaG4"/>): quem esta escondido (`isconcealed`), quem e Android, e quem expressa 5
/// de BP ou menos. Na galaxia o DM testa so Android e o piso de 5 milhoes (`:387-388`) -- reproduzido
/// igual, e anotado: um invisivel com 5 milhoes de BP e sentido de outro planeta, como no original.
///
/// OS DOIS PISOS SAO MEDIDOS SEM A BARRA DE PODER -- DIVERGENCIA DECLARADA das linhas acima, regra do dono de
/// 2026-10-12: *"vc mudar teu poder na barra de % n esconde o ki do scouter, ou sense, so serve pra se segurar ou
/// usar todo o poder, esconder o ki totalmente so com relaxar"*. Quem se segura continua em toda lista em que
/// estaria com o poder inteiro (com a leitura baixa que a barra lhe da); quem RELAXA expressa a constante 5 e sai
/// de todas. Ver <see cref="TemEnergiaQueSeSente"/> e `PoderEmUso.SemABarra`.
/// Cadaver nao entra em alcance nenhum: no DM ele e um `/obj` (`Corpse.dm:1`), nao um mob, e nunca
/// esteve em lista nenhuma.
///
/// NESTE MUNDO e NA GALAXIA varrem `player_list` no DM -- so JOGADOR. Aqui a lista e uma so (`_players`),
/// entao a peneira e <see cref="Gente.EhNpcDoMundo"/>: cidadao, Rei e chefe de saga ficam de fora desses
/// dois alcances; PERTO (que no DM e `current_area.contents`, todo mob) ve tambem os NPCs.
///
/// ============================ O SCAN (o scouter ligado, `HtmlUI.dm:400-419`) ============================
/// Substitui o Sense (e a mesma aba com outro nome, `:402-404`): a zona inteira, com o BP EXATO,
/// distancia, rumo e coordenadas. NPC comum nao aparece -- so chefe (`isBoss`, `:410`) e o corpo em
/// transe (`mind_dummy`, que aqui e o boneco largado, sem papel). O scouter e aparelho: nao exige a
/// skill nem o contador, e vence quando os dois existem.
///
/// ============================ OS NPCs TEM NOME, E O SCOUTER OS LE (dono, 2026-10-13) ============================
/// *"scouter deveria mostrar o bp dos npcs tb, e o sense (e o scouter) deveria mostrar o nome dos npcs q vc ta
/// analisando"*. DUAS DIVERGENCIAS DECLARADAS das linhas de cima:
///   * o NPC COMUM entra no scan (o DM so listava chefe, `HtmlUI.dm:410`) -- ate <see cref="TilesDoScanDeNpc"/>
///     tiles: uma cidade tem cento e cinquenta habitantes, e a lista da zona inteira seria um pacote de seis
///     kilobytes por segundo pra cada scouter ligado. O chefe continua aparecendo de qualquer distancia;
///   * o NPC vai com o NOME dele no Sense (`IdentidadePara`): ele nao tem assinatura nem identidade pra
///     esconder, e o "???" que saia nao dizia se aquilo era um guarda ou o Rei. Jogador desconhecido continua
///     indo sem nome, com a assinatura.
/// ==========================================================================================================
///
/// ============================ O TETO DO SCOUTER E O KI DIVINO (dono, 2026-10-18) ============================
/// *"coloque um bp maximo no scouter de 100m de bp, acima disso o scouter marca com ???, e pessoas q estao em formas com godki, ou estao em
/// constante uso de ki divino (...) n podem ter o poder lido com scouter ou ter o ki sentido. somente pessoas q tb tem ki divino podem dar
/// sense em outras pessoas com ki divino"*. DUAS DIVERGENCIAS DECLARADAS das linhas de cima:
///   * no SCAN, o BP acima de 100 milhoes vai NaN (a aba escreve "???"), e quem esta com o ki divino em uso NAO entra na lista -- o aparelho
///     nao o detecta, seja quem for o dono dele (`KiDivinoEscondido`, `Core/Stats/Scouter.cs`);
///   * no SENSE, nos tres alcances, o ki divino em uso so e sentido por quem tambem tem ki divino (`AchoAEnergia`, `SenteKiDivino`).
/// ===========================================================================================================
///
/// ============================ O ANDROIDE NAO TEM KI (dono, 2026-10-19) ============================
/// *"androides nem deveriam aparecer no sense ou scouter por serem androides (faça isso pra player androide tb, ambos os tipos de
/// androide)"*. O jogador androide dos dois tipos (absorcao e energia infinita) e os androides NPC da saga do Cell (`AndroideSemKi`):
///   * no SCAN ninguem deles entra -- DIVERGENCIA DECLARADA do `ui_tab_scan`, que nao testava raca (`HtmlUI.dm:407-414`);
///   * no SENSE (e na telepatia e no Observe) o jogador androide ja ficava de fora pela raca, literal do DM (`HtmlUI.dm:369`); o NPC, que
///     nasce "Human" pelo molde, era sentido -- e chefe, entao do planeta ao universo. Agora nao (`AchoAEnergiaG4`).
/// ===================================================================================================
///
/// ============================ O SIGILO ============================
/// No modo Sense o BP absoluto NUNCA vai no pacote: o campo sai como <see cref="SemLeitura"/> (NaN) e so
/// a RAZAO viaja (`round((D.expressedBP/max(expressedBP,1))*100, 1)`, `HtmlUI.dm:374`) -- que e
/// exatamente a licenca da regra 1 do `GameServer.Sigilo` ("multiplicador e razao podem; numero nao"). No
/// modo Scan o numero vai, porque o scouter E a porta de leitura (<see cref="TemScouter"/>). A bancada
/// `--sentidosteste` abre o pacote com o leitor do cliente e afirma os dois.
///
/// ============================ QUANDO SAI ============================
/// A 1 Hz (o laco de um segundo do `Tick`), so pra quem tem `Poder.Sense` ou scouter, e so quando a LISTA
/// MUDA -- a mesma disciplina de assinatura do `MandarAtributos`/`MandarCorpo`. Um mundo parado e zero
/// trafego; alguem andando um tile e um pacote por segundo pra quem o sente.
/// ==================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Quem expressa isto de BP ou menos nao tem energia pra sentir -- `D.expressedBP <= 5` (`HtmlUI.dm:370`).</summary>
	public const double BpMinimoSentivel = 5;

	/// <summary>
	/// (2026-10-18) O PISO PELA ARVORE DA MENTE: o `kiawarenessskill` que no DM acendia o segundo alcance (`Sense2.0.dm:27`) da no minimo o
	/// nivel do PLANETA -- ver `SenseEmNiveis`.
	/// </summary>
	public const double PericiaDoPlaneta = 20;

	/// <summary>...e o que acendia o terceiro (`Sense2.0.dm:32`) da no minimo o do SISTEMA.</summary>
	public const double PericiaDaGalaxia = 60;

	/// <summary>Os cinco alcances, como o pacote os nomeia (`PresencaState.Alcance`) -- os niveis do `SenseEmNiveis`.</summary>
	public const byte AlcanceVisao = Jandirus.Core.Skills.SenseEmNiveis.CampoDeVisao, AlcanceRegiao = Jandirus.Core.Skills.SenseEmNiveis.Regiao,
						  AlcancePlaneta = Jandirus.Core.Skills.SenseEmNiveis.Planeta, AlcanceSistema = Jandirus.Core.Skills.SenseEmNiveis.Sistema,
						  AlcanceUniverso = Jandirus.Core.Skills.SenseEmNiveis.Universo;

	/// <summary>DEFEITO INJETADO (`--sentidosteste`): o nivel da skill nao abre alcance nenhum -- so o contador, o jogo de antes. Falso em jogo, sempre.</summary>
	internal static bool SenseSemNiveisDeTeste;

	/// <summary>Ate quantos tiles o scouter le um NPC comum (o chefe, de qualquer distancia). Ver o cabecalho.</summary>
	public const int TilesDoScanDeNpc = 40;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): o NPC comum volta a ficar fora do scan e a ir sem nome no Sense --
	/// a v0.2.0. Lido pelas duas linhas da regra (`SentidosDe`, `IdentidadePara`). Falso em jogo, sempre.
	/// </summary>
	internal static bool NpcSemNomeNemLeituraDeTeste;

	/// <summary>
	/// A BANCADA ESCUTA O QUE SAIU, pelo mesmo motivo do `EscutaDeAtributos`: pacote que saiu no fio nao
	/// volta, e corpo forjado nao tem `Peer`. Nula em jogo; com ela ligada o tique tambem considera corpos
	/// sem tela -- e o unico jeito de a bancada exercitar o proprio <see cref="TickDosSentidos"/>.
	/// </summary>
	internal static List<(int Quem, NetDataWriter Pacote)>? EscutaDeSentidos;

	/// <summary>
	/// A ULTIMA LISTA QUE CADA UM RECEBEU, resumida numa assinatura. Por id, e nao num campo do
	/// `ServerPlayer` (a classe dele mora em `GameServer.cs`); a poda no comeco de cada tique tira quem
	/// saiu do mundo, e quem perde o sentido perde a assinatura junto -- pra lista sair de novo quando ele
	/// voltar, mesmo que seja igual a de antes.
	/// </summary>
	private readonly Dictionary<int, string> _sigSentidos = [];

	/// <summary>Tem a skill Sense (o bit que `AplicarPoderes` acende quando ela esta no livro).</summary>
	internal static bool TemSense(ServerPlayer pl) => (pl.Poderes & Protocol.Poder.Sense) != 0;

	/// <summary>
	/// O TIQUE DE 1 Hz: quem sente, recebe a lista -- se ela mudou. Pendurado no laco de um segundo do
	/// `Tick` (`GameServer.cs`), ao lado do convivio, e pela mesma razao: nada aqui e evento de quadro.
	/// </summary>
	private void TickDosSentidos()
	{
		if (_sigSentidos.Count > 0)
			foreach (int id in _sigSentidos.Keys.ToList())
				if (!_players.ContainsKey(id)) _sigSentidos.Remove(id);

		foreach (ServerPlayer pl in _players.Values)
		{
			// CORPO SEM TELA NAO LE ABA NENHUMA -- e montar a lista custa uma varredura da zona. A escuta
			// da bancada e a unica excecao, porque corpo forjado nunca tem `Peer`.
			if (pl.Peer == null && EscutaDeSentidos == null) continue;
			bool scan = TemScouter(pl);
			if (!scan && !TemSense(pl)) { _sigSentidos.Remove(pl.Id); continue; }
			MandarSentidos(pl, scan);
		}
	}

	/// <summary>Monta, assina, e so manda se a assinatura mudou -- o idioma do `MandarAtributos`.</summary>
	private void MandarSentidos(ServerPlayer pl, bool scan)
	{
		(_, List<Protocol.PresencaState> lista) = SentidosDe(pl);
		byte nivel = scan ? (byte)0 : (byte)NivelDoSense(pl);
		byte nivelIt = NivelItNoFio(pl);
		string sig = AssinaturaDosSentidos(scan, nivel, nivelIt, lista);
		if (_sigSentidos.TryGetValue(pl.Id, out string? antiga) && antiga == sig) return;
		_sigSentidos[pl.Id] = sig;

		NetDataWriter w = PacoteDeSentidos(scan, nivel, lista);
		EscutaDeSentidos?.Add((pl.Id, w));
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		EnviarNivelDaTransmissao(pl, nivelIt);
	}

	/// <summary>O pacote, pronto pro fio: `S2C.Sentidos` + `bool scan` + o nivel do Sense + a lista. Separado pra bancada abrir com o leitor do cliente.</summary>
	private static NetDataWriter PacoteDeSentidos(bool scan, byte nivel, List<Protocol.PresencaState> lista)
	{
		NetDataWriter w = Protocol.Begin(Protocol.S2C.Sentidos);
		w.PutSentidos(scan, nivel, lista);
		return w;
	}

	/// <summary>O nivel do Instant Transmission, no pacote proprio, junto da lista: a aba decide o botao por ele.</summary>
	private static void EnviarNivelDaTransmissao(ServerPlayer pl, byte nivelIt)
	{
		NetDataWriter w = Protocol.Begin(Protocol.S2C.NivelDaTransmissao);
		w.Put(nivelIt);
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O RESUMO DA LISTA. Os numeros ja saem inteiros do <see cref="SentidosDe"/> (a razao e o BP sao
	/// arredondados como o DM arredonda), entao assinar o valor e assinar o que a tela vai escrever --
	/// nem mais fino (remandaria por ruido), nem mais grosso (seguraria uma mudanca visivel).
	/// </summary>
	private static string AssinaturaDosSentidos(bool scan, byte nivel, byte nivelIt, List<Protocol.PresencaState> lista)
	{
		var sb = new System.Text.StringBuilder();
		sb.Append(scan ? 'S' : 'K').Append(nivel).Append('T').Append(nivelIt);
		foreach (Protocol.PresencaState p in lista)
			sb.Append('|').Append(p.Nome).Append('/').Append(p.Assinatura).Append('/').Append(p.Alcance)
			  .Append('/').Append(p.PoderRelativo).Append('/').Append(p.Bp)
			  .Append('/').Append(p.Distancia).Append('/').Append(p.Rumo).Append('/').Append(p.X)
			  .Append('/').Append(p.Y).Append('/').Append(p.Zona).Append('/').Append(p.Chefe ? 'c' : '-')
			  .Append('#').Append(p.Corpo);
		return sb.ToString();
	}

	/// <summary>
	/// (2026-10-18) O NIVEL DO SENSE DESTE CORPO -- o alcance dele (`SenseEmNiveis`): o nivel da skill, com o PISO da arvore da Mente (o
	/// contador que abria os alcances do DM: 20 = o planeta, 60 = o sistema), e nunca abaixo do campo de visao (a skill comprada nasce no 0).
	/// </summary>
	internal static int NivelDoSense(ServerPlayer pl) =>
		Math.Max(Math.Max(SenseSemNiveisDeTeste ? 0 : pl.Niveis.Nivel(Jandirus.Core.Skills.SenseEmNiveis.Path), AlcanceVisao),
					pl.Ficha.kiawarenessskill >= PericiaDaGalaxia ? AlcanceSistema
					: pl.Ficha.kiawarenessskill >= PericiaDoPlaneta ? AlcancePlaneta
					: AlcanceVisao);

	/// <summary>
	/// (2026-10-18) EM QUE SISTEMA ESTELAR ESTE CORPO ESTA: o do planeta (pre-feito ou gerado) ou o do ponto do espaco. Um sistema cabe inteiro
	/// na sua celula (`Sistemas`), entao a celula e o sistema. Nulo onde nao ha sistema (o Outro Mundo, os interiores, a mente).
	/// </summary>
	private SistemaId? SistemaDe(ServerPlayer o)
	{
		if (Espaco.EhEspaco(o.Zone)) return SistemaId.De(o.Pos);
		if (EhZonaProcedural(o.Zone))
			return _zonasGeradas.TryGetValue(o.Zone.Hash, out ZonaGerada? viva) ? SistemaId.De(viva.NoEspaco.Pos) : null;
		if (o.Zone.Kind != ZoneKey.KindPremade) return null;
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (string.Equals(p.Nome, o.Zone.Name, StringComparison.OrdinalIgnoreCase)) return SistemaId.De(p.Pos);
		return null;
	}

	/// <summary>(2026-10-18) DE LONGE SO SE SENTE JOGADOR E CHEFE -- o NPC comum, so no campo de visao e na regiao.</summary>
	private static bool GenteOuChefe(ServerPlayer o) => !Gente.EhNpcDoMundo(o.Peer != null, o.Papel) || o.Papel?.EhChefe == true;

	/// <summary>
	/// A LISTA DE QUEM `eu` SENTE (ou le), e em que modo. E o corpo do `ui_tab_sense`/`ui_tab_scan`, e a
	/// unica funcao que decide o que entra -- a bancada a chama direto, e o tique a chama por
	/// <see cref="MandarSentidos"/>.
	///
	/// A ORDEM E DETERMINISTICA (alcance, distancia, nome, id): a assinatura compara conteudo, e uma
	/// lista que mudasse de ordem sem mudar de conteudo remandaria o pacote a toa.
	/// </summary>
	internal (bool Scan, List<Protocol.PresencaState> Lista) SentidosDe(ServerPlayer eu)
	{
		var achados = new List<(Protocol.PresencaState P, int Id)>();
		bool scan = TemScouter(eu);
		if (!scan && !TemSense(eu)) return (false, []);

		(int mx, int my) = TileDe(eu.Pos);
		List<ServerPlayer> zona = _zones.TryGetValue(eu.Zone.Hash, out List<ServerPlayer>? z) ? z : [];

		if (scan)
		{
			// `for(var/mob/E in current_area.contents)` (HtmlUI.dm:407-414): a area inteira, sem os 15
			// tiles; NPC so se for chefe; o piso dos 5 de BP; e o BP exato, `FullNum(round(expressedBP,1))`.
			foreach (ServerPlayer o in zona)
			{
				// (2026-10-18) nem o corpo escondido de quem o Neo Tuffle possui -- `GameServer.NeoTuffle.cs`
				if (o == eu || o.ECadaver || HospedeEscondido(o)) continue;
				// (2026-10-13) O NPC COMUM TAMBEM, ate `TilesDoScanDeNpc`; o chefe, de qualquer distancia.
				bool npc = Gente.EhNpcDoMundo(o.Peer != null, o.Papel);
				if (npc && o.Papel?.EhChefe != true
					&& (NpcSemNomeNemLeituraDeTeste || DistanciaEmTiles(eu, o) > TilesDoScanDeNpc)) continue;
				if (!TemEnergiaQueSeSente(o)) continue;   // o piso dos 5, SEM a barra: quem se segura fica; quem relaxa sai
				if (KiDivinoEscondido(o)) continue;   // (2026-10-18) o scouter nunca detecta o ki divino -- ver o cabecalho
				if (AndroideSemKi(o)) continue;   // (2026-10-19) nem o androide: nao ha ki nele pro vidro detectar -- ver o cabecalho
				double lido = Math.Round(o.Ficha.expressedBP, MidpointRounding.AwayFromZero);
				(int ox, int oy) = TileDe(o.Pos);
				achados.Add((new Protocol.PresencaState
				{
					Nome = o.Name, Assinatura = "",
					Alcance = AlcancePlaneta,   // (o scouter le a zona inteira)
					PoderRelativo = float.NaN,
					// (2026-10-18) ACIMA DO TETO O VIDRO MARCA "???": no modo Scan, NaN e o numero que o scouter nao alcanca.
					Bp = Jandirus.Core.Stats.Scouter.Le(lido) ? lido : SemLeitura,
					Distancia = DistanciaEmTiles(eu, o),
					Rumo = RumoDoDm(ox - mx, oy - my),
					X = (short)Math.Clamp(ox, -1, short.MaxValue), Y = (short)Math.Clamp(oy, -1, short.MaxValue),
					Zona = "",
					Chefe = o.Papel?.EhChefe == true,
					Corpo = IdNoCartaoDoSense(o),
				}, o.Id));
			}
			return (true, Ordenada(achados));
		}

		// (2026-10-18) OS CINCO NIVEIS -- ver o cabecalho e `SenseEmNiveis`.
		int nivel = NivelDoSense(eu);
		SistemaId? meuSistema = nivel >= AlcanceSistema ? SistemaDe(eu) : null;
		// De longe (alem da regiao): o mesmo sistema ou o universo. Na zona do ESPACO (uma so pro universo inteiro) quem passa da regiao tambem
		// cai aqui -- o "planeta" de quem esta no espaco nao existe.
		byte Longe(ServerPlayer o) => meuSistema is { } ms && SistemaDe(o) is { } os && os == ms ? AlcanceSistema : AlcanceUniverso;
		var vistos = new HashSet<int>();

		// A MESMA ZONA: campo de visao (15 tiles) e regiao (60) com todo mundo, NPC inclusive -- o `current_area.contents` do DM; o planeta
		// (a zona inteira), so jogador e chefe -- o `player_list` do mesmo z, mais o chefe.
		bool noEspaco = Espaco.EhEspaco(eu.Zone);
		foreach (ServerPlayer o in zona)
		{
			if (o == eu || o.ECadaver || !AchoAEnergia(eu, o)) continue;
			ushort dist = DistanciaEmTiles(eu, o);
			byte alcance = dist <= Jandirus.Core.Skills.SenseEmNiveis.TilesDoCampoDeVisao ? AlcanceVisao
				: dist <= Jandirus.Core.Skills.SenseEmNiveis.TilesDaRegiao ? AlcanceRegiao
				: noEspaco ? Longe(o) : AlcancePlaneta;
			if (alcance > nivel || (alcance > AlcanceRegiao && !GenteOuChefe(o))) continue;
			vistos.Add(o.Id);
			achados.Add((Presenca(eu, o, alcance, dist, mx, my), o.Id));
		}

		// AS OUTRAS ZONAS: o sistema (os planetas da mesma estrela e o espaco dela) e o universo -- so jogador e chefe, so o lugar (sem
		// coordenada nem distancia), e a razao dos BPs BASE (`D.BP/max(BP,1)`), como a galaxia do DM (`HtmlUI.dm:386-393`).
		if (nivel >= AlcanceSistema)
			foreach (ServerPlayer o in _players.Values)
			{
				if (o == eu || o.ECadaver || vistos.Contains(o.Id) || o.Zone.Hash == eu.Zone.Hash) continue;
				if (!GenteOuChefe(o) || !AchoAEnergia(eu, o)) continue;
				byte alcance = Longe(o);
				if (alcance > nivel) continue;
				vistos.Add(o.Id);
				(string nome, string sig) = IdentidadePara(eu, o);
				achados.Add((new Protocol.PresencaState
				{
					Nome = nome, Assinatura = sig,
					Alcance = alcance,
					PoderRelativo = Porcento(o.Ficha.BP, eu.Ficha.BP),
					Bp = SemLeitura,
					Distancia = Protocol.DistanciaDesconhecida,
					Rumo = 0, X = -1, Y = -1,
					Zona = o.Zone.Name,
					Chefe = o.Papel?.EhChefe == true,
					Corpo = IdNoCartaoDoSense(o),
				}, o.Id));
			}

		return (false, Ordenada(achados));
	}

	/// <summary>Uma presenca da MESMA zona (os tres primeiros alcances, e o espaco): razao de poder, nunca numero.</summary>
	private static Protocol.PresencaState Presenca(ServerPlayer eu, ServerPlayer o, byte alcance, ushort dist,
												   int mx, int my)
	{
		(string nome, string sig) = IdentidadePara(eu, o);
		(int ox, int oy) = TileDe(o.Pos);
		return new Protocol.PresencaState
		{
			Nome = nome, Assinatura = sig,
			Alcance = alcance,
			// CONTRA O PODER QUE QUEM LE COMPARA -- o proprio expresso, menos quando ele esta escondido (travado em 5,
			// uma constante: a razao contra ela seria o BP do outro com outra unidade). Ver `PoderQueSeCompara`.
			PoderRelativo = Porcento(o.Ficha.expressedBP, PoderQueSeCompara(eu)),
			// O SIGILO MORA NESTA LINHA: no modo Sense o BP absoluto NAO existe no pacote.
			Bp = SemLeitura,
			// `round(D.HP)` de um argumento e PISO no DM (ver a nota do `MandarCorpo`)
			// A VIDA NAO VAI. O DM da o `round(D.HP)% hp` a quem esta perto (`HtmlUI.dm:375`); o dono
			// pediu o contrario (2026-09-04: *"na tela de sense e do scouter nao deve aparecer a vida de
			// quem voce sente, somente o poder relativo/bp"*). Divergencia declarada: o registro nem
			// tem o campo, pra nenhuma tela poder mostrar por engano.
			Distancia = dist,
			Rumo = RumoDoDm(ox - mx, oy - my),
			X = -1, Y = -1,
			Zona = "",
			Chefe = o.Papel?.EhChefe == true,
			Corpo = IdNoCartaoDoSense(o),
		};
	}

	/// <summary>
	/// O NOME SO DE QUEM EU CONHECO -- `known_contact_list["[D.signature]"] || check_familiarity(D)`
	/// (`HtmlUI.dm:374`): as duas metades do DM leem a mesma lista de contatos, que aqui e o
	/// `Convivio.Conhece` -- a mesma lista que a aba People desenha. Desconhecido vai sem nome e com a
	/// assinatura (a tela escreve "??? (assinatura)"); quem nao tem identidade nenhuma (NPC) vai so "???".
	/// </summary>
	private static (string Nome, string Assinatura) IdentidadePara(ServerPlayer eu, ServerPlayer o)
	{
		// (2026-10-13) O NPC DIZ QUEM E: ele nao tem identidade pra esconder -- ver o cabecalho.
		if (!NpcSemNomeNemLeituraDeTeste && Gente.EhNpcDoMundo(o.Peer != null, o.Papel)) return (o.Name, "");

		string sig = o.Assinatura;
		return eu.Social.Conhece(sig) ? (o.Name, "") : ("", sig);
	}

	/// <summary>`round((a/max(b,1))*100, 1)` do DM: o `round(A, B)` do BYOND e o multiplo de B mais proximo -- inteiro.</summary>
	private static float Porcento(double a, double b) =>
		(float)Math.Round(a / Math.Max(b, 1) * 100, MidpointRounding.AwayFromZero);

	/// <summary>O `get_dist` do DM (distancia de tabuleiro entre turfs), ja portado pra fusao -- reusado, nao copiado.</summary>
	private static ushort DistanciaEmTiles(ServerPlayer a, ServerPlayer b) =>
		(ushort)Math.Clamp(Jandirus.Core.Social.Fusao.DistanciaEmTilesDoDm(a.Pos, b.Pos, ZoneCollision.TileSize), 0, ushort.MaxValue - 1);

	/// <summary>A celula do centro do corpo (a mesma conta do `DistanciaEmTilesDoDm`).</summary>
	private static (int X, int Y) TileDe(Vec2 p) =>
		((int)Math.Floor(p.X / (double)ZoneCollision.TileSize), (int)Math.Floor(p.Y / (double)ZoneCollision.TileSize));

	/// <summary>
	/// O `get_dir(src, D)` do BYOND, em oito pontos: ele JUNTA os dois eixos sempre que os dois diferem
	/// (20 tiles a leste e 1 ao norte e NORTHEAST), sem olhar a proporcao. O Y do port cresce pra BAIXO, e
	/// o NORTH do BYOND e y+1 -- por isso o norte aqui e `dy &lt; 0`. Indices de `Protocol.NomesDosRumos`.
	/// </summary>
	internal static byte RumoDoDm(int dx, int dy) => (Math.Sign(dx), Math.Sign(dy)) switch
	{
		(0, -1) => 1,    // N
		(1, -1) => 2,    // NE
		(1, 0) => 3,     // E
		(1, 1) => 4,     // SE
		(0, 1) => 5,     // S
		(-1, 1) => 6,    // SW
		(-1, 0) => 7,    // W
		(-1, -1) => 8,   // NW
		_ => 0,          // em cima de mim: "?"
	};

	private static List<Protocol.PresencaState> Ordenada(List<(Protocol.PresencaState P, int Id)> achados)
	{
		achados.Sort((a, b) =>
			a.P.Alcance != b.P.Alcance ? a.P.Alcance.CompareTo(b.P.Alcance)
			: a.P.Distancia != b.P.Distancia ? a.P.Distancia.CompareTo(b.P.Distancia)
			: string.CompareOrdinal(a.P.Nome, b.P.Nome) != 0 ? string.CompareOrdinal(a.P.Nome, b.P.Nome)
			: a.Id.CompareTo(b.Id));
		var lista = new List<Protocol.PresencaState>(achados.Count);
		foreach ((Protocol.PresencaState p, _) in achados) lista.Add(p);
		return lista;
	}
}
