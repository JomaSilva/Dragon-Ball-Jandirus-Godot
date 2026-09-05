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
/// ============================ O QUE CADA ALCANCE VE (Sense2.0.dm:20-35) ============================
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
	/// <summary>PERTO: ate quantos tiles o primeiro alcance enxerga -- `get_dist(src, D) > 15` (`HtmlUI.dm:371`).</summary>
	public const int TilesDoSensePerto = 15;

	/// <summary>Quem expressa isto de BP ou menos nao tem energia pra sentir -- `D.expressedBP <= 5` (`HtmlUI.dm:370`).</summary>
	public const double BpMinimoSentivel = 5;

	/// <summary>NA GALAXIA so quem passa disto -- `D.expressedBP <= 5000000` (`HtmlUI.dm:389`).</summary>
	public const double BpDaGalaxia = 5_000_000;

	/// <summary>O `kiawarenessskill` que acende o segundo alcance (`Sense2.0.dm:27`).</summary>
	public const double PericiaDoPlaneta = 20;

	/// <summary>...e o terceiro (`Sense2.0.dm:32`).</summary>
	public const double PericiaDaGalaxia = 60;

	/// <summary>Os tres alcances, como o pacote os nomeia (`PresencaState.Alcance`).</summary>
	public const byte AlcancePerto = 1, AlcanceMundo = 2, AlcanceGalaxia = 3;

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
		string sig = AssinaturaDosSentidos(scan, lista);
		if (_sigSentidos.TryGetValue(pl.Id, out string? antiga) && antiga == sig) return;
		_sigSentidos[pl.Id] = sig;

		NetDataWriter w = PacoteDeSentidos(scan, lista);
		EscutaDeSentidos?.Add((pl.Id, w));
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>O pacote, pronto pro fio: `S2C.Sentidos` + `bool scan` + a lista. Separado pra bancada abrir com o leitor do cliente.</summary>
	private static NetDataWriter PacoteDeSentidos(bool scan, List<Protocol.PresencaState> lista)
	{
		NetDataWriter w = Protocol.Begin(Protocol.S2C.Sentidos);
		w.PutSentidos(scan, lista);
		return w;
	}

	/// <summary>
	/// O RESUMO DA LISTA. Os numeros ja saem inteiros do <see cref="SentidosDe"/> (a razao e o BP sao
	/// arredondados como o DM arredonda), entao assinar o valor e assinar o que a tela vai escrever --
	/// nem mais fino (remandaria por ruido), nem mais grosso (seguraria uma mudanca visivel).
	/// </summary>
	private static string AssinaturaDosSentidos(bool scan, List<Protocol.PresencaState> lista)
	{
		var sb = new System.Text.StringBuilder();
		sb.Append(scan ? 'S' : 'K');
		foreach (Protocol.PresencaState p in lista)
			sb.Append('|').Append(p.Nome).Append('/').Append(p.Assinatura).Append('/').Append(p.Alcance)
			  .Append('/').Append(p.PoderRelativo).Append('/').Append(p.Bp)
			  .Append('/').Append(p.Distancia).Append('/').Append(p.Rumo).Append('/').Append(p.X)
			  .Append('/').Append(p.Y).Append('/').Append(p.Zona).Append('/').Append(p.Chefe ? 'c' : '-');
		return sb.ToString();
	}

	/// <summary>Em que alcance este corpo sente: pelo contador, como o `effector` do `Sense2.0.dm:20-35`.</summary>
	private static byte AlcanceDoSense(ServerPlayer pl) =>
		pl.Ficha.kiawarenessskill >= PericiaDaGalaxia ? AlcanceGalaxia
		: pl.Ficha.kiawarenessskill >= PericiaDoPlaneta ? AlcanceMundo
		: AlcancePerto;

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
				if (o == eu || o.ECadaver) continue;
				bool npc = Gente.EhNpcDoMundo(o.Peer != null, o.Papel);
				if (npc && o.Papel?.EhChefe != true) continue;
				if (o.Ficha.expressedBP <= BpMinimoSentivel) continue;
				(int ox, int oy) = TileDe(o.Pos);
				achados.Add((new Protocol.PresencaState
				{
					Nome = o.Name, Assinatura = "",
					Alcance = AlcanceMundo,
					PoderRelativo = float.NaN,
					Bp = Math.Round(o.Ficha.expressedBP, MidpointRounding.AwayFromZero),
					Distancia = DistanciaEmTiles(eu, o),
					Rumo = RumoDoDm(ox - mx, oy - my),
					X = (short)Math.Clamp(ox, -1, short.MaxValue), Y = (short)Math.Clamp(oy, -1, short.MaxValue),
					Zona = "",
					Chefe = o.Papel?.EhChefe == true,
				}, o.Id));
			}
			return (true, Ordenada(achados));
		}

		byte alcance = AlcanceDoSense(eu);
		var vistos = new HashSet<int>();

		// PERTO (HtmlUI.dm:366-375): a mesma zona, ate 15 tiles, todo mob -- NPC inclusive.
		foreach (ServerPlayer o in zona)
		{
			if (o == eu || o.ECadaver || !AchoAEnergiaG4(o)) continue;
			ushort dist = DistanciaEmTiles(eu, o);
			if (dist > TilesDoSensePerto) continue;
			vistos.Add(o.Id);
			achados.Add((Presenca(eu, o, AlcancePerto, dist, mx, my), o.Id));
		}

		// NESTE MUNDO (HtmlUI.dm:377-384): `player_list` do mesmo z -- so gente, com distancia e rumo.
		if (alcance >= AlcanceMundo)
			foreach (ServerPlayer o in zona)
			{
				if (o == eu || o.ECadaver || vistos.Contains(o.Id)) continue;
				if (Gente.EhNpcDoMundo(o.Peer != null, o.Papel) || !AchoAEnergiaG4(o)) continue;
				vistos.Add(o.Id);
				achados.Add((Presenca(eu, o, AlcanceMundo, DistanciaEmTiles(eu, o), mx, my), o.Id));
			}

		// NA GALAXIA (HtmlUI.dm:386-393): `player_list` inteira, so acima de 5 milhoes, so o lugar -- e a
		// razao e a dos BPs BASE (`D.BP/max(BP,1)`), como o DM escreve, e nao a dos expressos.
		if (alcance >= AlcanceGalaxia)
			foreach (ServerPlayer o in _players.Values)
			{
				if (o == eu || o.ECadaver || vistos.Contains(o.Id)) continue;
				if (Gente.EhNpcDoMundo(o.Peer != null, o.Papel)) continue;
				if (o.Race == "Android" || o.Ficha.expressedBP <= BpDaGalaxia) continue;
				vistos.Add(o.Id);
				(string nome, string sig) = IdentidadePara(eu, o);
				achados.Add((new Protocol.PresencaState
				{
					Nome = nome, Assinatura = sig,
					Alcance = AlcanceGalaxia,
					PoderRelativo = Porcento(o.Ficha.BP, eu.Ficha.BP),
					Bp = SemLeitura,
					Distancia = Protocol.DistanciaDesconhecida,
					Rumo = 0, X = -1, Y = -1,
					Zona = o.Zone.Name,
					Chefe = o.Papel?.EhChefe == true,
				}, o.Id));
			}

		return (false, Ordenada(achados));
	}

	/// <summary>Uma presenca dos dois primeiros alcances: razao de poder, nunca numero; vida so perto.</summary>
	private static Protocol.PresencaState Presenca(ServerPlayer eu, ServerPlayer o, byte alcance, ushort dist,
												   int mx, int my)
	{
		(string nome, string sig) = IdentidadePara(eu, o);
		(int ox, int oy) = TileDe(o.Pos);
		return new Protocol.PresencaState
		{
			Nome = nome, Assinatura = sig,
			Alcance = alcance,
			PoderRelativo = Porcento(o.Ficha.expressedBP, eu.Ficha.expressedBP),
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
