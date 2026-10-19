using Godot;
using LiteNetLib;
using LiteNetLib.Utils;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// QUEM OUVE O QUE. O alcance da fala e regra de servidor, nao de cliente -- se o cliente
/// decidisse quem ouve, bastaria ignorar a regra pra escutar a mesa ao lado.
///
/// As distancias saem do `sayType()` do BYOND (Code/Modules/Players/Talking.dm), e a conta e
/// direta: la o alcance era em TILES de `view(screenx)` com `screenx = 22` no login padrao, e
/// aqui um tile e <see cref="ZoneCollision.TileSize"/> pixels.
///
///   - falar    view(22)      -> 704 px
///   - gritar   view(22+15)   -> 1184 px   (o original dispara isso ao achar "!" no texto)
///   - sussurro range(2)      -> 64 px pro conteudo; quem esta em volta so ve que houve
///   - OOC      todo o servidor
///   - LOOC     view(22), como a fala
///
/// O "!" VIRA GRITO SOZINHO, sem canal separado. E uma das ideias boas do original: ninguem
/// precisa lembrar de trocar de modo pra gritar, so escrever como se grita.
/// </summary>
public partial class GameServer
{
	private const float RaioDaVista = 22 * ZoneCollision.TileSize;
	private const float RaioDoGrito = (22 + 15) * ZoneCollision.TileSize;
	private const float RaioDoSussurro = 2 * ZoneCollision.TileSize;

	/// <summary>
	/// Quanto tempo entre duas falas do mesmo jogador. Nao e moderacao -- e o teto que impede
	/// um cliente modificado de usar o canal de chat como amplificador de trafego: uma fala
	/// custa uma mensagem confiavel pra TODO MUNDO que ouve.
	/// </summary>
	private const long MsEntreFalas = 400;

	/// <summary>Gancho de bancada: toda fala que passou pelo funil (quem, canal, texto). Nulo fora de teste.</summary>
	internal static List<(int Quem, Protocol.Fala Canal, string Texto)>? EscutaDeFalas;

	/// <summary>
	/// (2026-10-18) Gancho de bancada: toda linha ENTREGUE (pra quem, o autor, o texto), ja depois do desvio do corpo da ponte -- e o que diz
	/// QUEM OUVIU, que a `EscutaDeFalas` (de quem falou) nao diz. Nulo fora de teste. Ver `--naveteste`, camadas 10 e 11.
	/// </summary>
	internal static List<(int Para, string Autor, string Texto)>? EscutaDeEntregas;

	private readonly Dictionary<int, long> _ultimaFala = [];

	private void Falar(ServerPlayer a, Protocol.Fala canal, string texto)
	{
		// SO OS SEIS CANAIS DE FALA. O byte do canal vem do cliente (`C2S.Chat`), e o `default` la embaixo
		// repassava QUALQUER valor a quem estivesse a vista -- com 200 (`Fala.Sistema`, que "nunca sai na rede")
		// o texto chegava sem autor, desenhado como recado do servidor: um aviso de administrador forjado.
		if (!CanalDeFalaLivreDeTeste && canal is < Protocol.Fala.Ooc or > Protocol.Fala.Looc) return;

		// (2026-10-13) BICHO NAO FALA LINGUA DE GENTE, venha a frase de onde vier: o que sai da boca de um animal e um
		// som da especie dele. Quem ja escolhe o som certo pela ocasiao e o cerebro (`Falatorio.Bicho`); esta e a segunda
		// tranca, no funil. O EMOTE PASSA: e a narracao do que o corpo FEZ ("cai atordoado!"), e nao uma frase dele.
		if (EhAnimal(a) && canal != Protocol.Fala.Emote)
			texto = Jandirus.Core.Npc.Animais.NaBocaDoBicho(a.Papel?.Molde.Animal?.Folha ?? "", texto, _rng);

		texto = Limpar(texto);
		if (texto.Length == 0) return;

		long agora = NowMs();
		if (_ultimaFala.TryGetValue(a.Id, out long antes) && agora - antes < MsEntreFalas) return;
		_ultimaFala[a.Id] = agora;
		EscutaDeFalas?.Add((a.Id, canal, texto));

		// (2026-10-18) QUEM ESTA AO LEME DA NAVE GRANDE, OU A OBSERVANDO DE FORA, FALA TAMBEM LA DENTRO -- pela boca do corpo que ficou na
		// ponte, medido de onde ele esta (*"igual como se vc n tivesse pilotando"*). Quem OBSERVA fala SO la dentro: o olho do lado de fora
		// nao tem boca (ninguem o ve). O piloto continua falando tambem do lado de fora, como sempre falou -- e la ele ja se ouve, entao o
		// corpo da ponte dele fica fora da volta de dentro (senao a frase chegaria duas vezes). Ver `GameServer.NaveObservar.cs`.
		ServerPlayer? daPonte = LemeMudoPraDentroDeTeste ? null : CorpoNaPonteDe(a);
		bool soDentro = daPonte != null && EhOlhoDeFora(a);

		switch (canal)
		{
			case Protocol.Fala.Ooc:
				// o unico canal que atravessa planeta: e conversa de jogador, nao de personagem --
				// por isso o `falante: null`, que e o que impede duas pessoas em galaxias
				// diferentes de se conhecerem por escrito (ver `Ouviu`)
				foreach (ServerPlayer o in _players.Values) Mandar(o, canal, a.Name, texto, falante: null);
				break;

			case Protocol.Fala.Sussurro:
				if (!soDentro) Sussurrar(a, a.Zone, a.Pos, texto, excetoEste: null);
				if (daPonte != null) Sussurrar(a, daPonte.Zone, daPonte.Pos, texto, excetoEste: soDentro ? null : daPonte);
				break;

			case Protocol.Fala.Diz:
			{
				float raio = texto.Contains('!') ? RaioDoGrito : RaioDaVista;
				if (!soDentro) PorPerto(a, a.Zone, a.Pos, raio, canal, texto, excetoEste: null);
				if (daPonte != null) PorPerto(a, daPonte.Zone, daPonte.Pos, raio, canal, texto, excetoEste: soDentro ? null : daPonte);
				break;
			}

			default:   // Pensa, Emote, Looc: todos no alcance da vista
				if (!soDentro) PorPerto(a, a.Zone, a.Pos, RaioDaVista, canal, texto, excetoEste: null);
				if (daPonte != null) PorPerto(a, daPonte.Zone, daPonte.Pos, RaioDaVista, canal, texto, excetoEste: soDentro ? null : daPonte);
				break;
		}
	}

	/// <summary>
	/// A FALA DE <paramref name="a"/> SAINDO DE UM PONTO -- o dele, ou o do corpo dele na ponte. <paramref name="excetoEste"/> nao ouve (o
	/// corpo da ponte de quem ja se ouviu do lado de fora). O OLHO de quem observa uma nave nao ouve o lado de fora: ele esta la so pra ver,
	/// e o corpo dele, que ouve, esta la dentro.
	/// </summary>
	private void PorPerto(ServerPlayer a, ZoneKey zona, Vec2 de, float raio, Protocol.Fala canal, string texto, ServerPlayer? excetoEste)
	{
		float raio2 = raio * raio;
		foreach (ServerPlayer o in ZoneList(zona.Hash))
		{
			if (o == excetoEste || EhOlhoDeFora(o)) continue;
			Vec2 dd = o.Pos - de;
			if (dd.X * dd.X + dd.Y * dd.Y <= raio2) Mandar(o, canal, a.Name, texto, a);
		}
	}

	/// <summary>
	/// O SUSSURRO, DE UM PONTO -- DOIS ENVIOS, como no original: quem esta colado ouve o que foi dito, e quem esta em volta recebe a fala
	/// VAZIA, que o cliente desenha como "Fulano sussurra alguma coisa...". Saber que houve um sussurro faz parte da cena. Os mesmos dois
	/// crivos do <see cref="PorPerto"/>.
	/// </summary>
	private void Sussurrar(ServerPlayer a, ZoneKey zona, Vec2 de, string texto, ServerPlayer? excetoEste)
	{
		foreach (ServerPlayer o in ZoneList(zona.Hash))
		{
			if (o == excetoEste || EhOlhoDeFora(o)) continue;
			float d = (o.Pos - de).Length;
			if (d <= RaioDoSussurro) Mandar(o, Protocol.Fala.Sussurro, a.Name, texto, a);
			// QUEM SO VIU QUE HOUVE UM SUSSURRO NAO CONVIVEU: nao chegou palavra nenhuma,
			// entao o `falante` vai nulo e a familiaridade nao anda.
			else if (d <= RaioDaVista) Mandar(o, Protocol.Fala.Sussurro, a.Name, "", falante: null);
		}
	}

	/// <param name="falante">
	/// QUEM ESTA FALANDO, ou nulo quando nao ha ninguem (recado do servidor) ou quando a fala nao
	/// chega como conversa (OOC, sussurro que voce nao ouviu).
	///
	/// ============================ POR QUE ELE NAO TEM VALOR PADRAO ============================
	/// Ele so serve pra uma coisa: a familiaridade que a CONVERSA faz crescer (ver
	/// <see cref="Ouviu"/>, o `TestListeners` do DM). E este e o funil unico -- toda fala que chega
	/// em alguem passa por aqui. Um padrao `= null` deixaria o canal novo do futuro entrar mudo e
	/// sem ninguem notar; obrigatorio, ele obriga cada chamador a dizer se aquilo e conversa ou nao,
	/// e esquecer vira erro de compilacao em vez de um sistema que para de crescer em silencio.
	/// ==========================================================================================
	/// </param>
	private void Mandar(ServerPlayer para, Protocol.Fala canal, string autor, string texto,
						ServerPlayer? falante)
	{
		// (2026-10-18) O CORPO DA PONTE OUVE PELO DONO: a fala (e o aviso) que chega no boneco de quem esta ao leme da nave grande, ou a
		// observando de fora, chega NELE -- *"enquanto vc pilota, deveria dar pra escutar e ver o chat q rola dentro da nave"*. Ver
		// `DonoQueOuvePeloBoneco`, `GameServer.NaveObservar.cs`.
		if (para.Peer == null && DonoQueOuvePeloBoneco(para) is { } dono) para = dono;
		EscutaDeEntregas?.Add((para.Id, autor, texto));

		if (falante != null) Ouviu(para, falante);

		var w = Protocol.Begin(Protocol.S2C.Chat);
		w.Put((byte)canal);
		w.Put(autor);
		w.Put(texto);
		// CONFIAVEL, sempre. Fala perdida e a unica perda que o jogador percebe como sumico:
		// um golpe faltando vira lag, uma frase faltando vira "ele me ignorou".
		para.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Gancho de bancada: o TOM de cada aviso (`Avisar`), na ordem. Nulo fora de teste.</summary>
	internal static List<(Protocol.Fala Tom, string Texto)>? EscutaDeTons;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v030teste`): todo aviso volta a sair no `Fala.Sistema`, sem cor propria -- o jogo de
	/// antes. Lido pelo <see cref="Avisar(ServerPlayer, string, Protocol.Fala)"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool AvisoSemTomDeTeste;

	/// <summary>Uma linha do servidor pra UM jogador so: aviso, recusa, resultado de comando.</summary>
	public void Avisar(ServerPlayer para, string texto) => Avisar(para, texto, Protocol.Fala.Sistema);

	/// <summary>
	/// O MESMO AVISO, NUM TOM: `Fala.AvisoDeRaiva` e `Fala.AvisoDeZenkai` saem pintados no chat (2026-10-15). Fora deles,
	/// o `Fala.Sistema` de sempre.
	/// </summary>
	public void Avisar(ServerPlayer para, string texto, Protocol.Fala tom)
	{
		if (AvisoSemTomDeTeste) tom = Protocol.Fala.Sistema;
		EscutaDeTons?.Add((tom, texto));
		// A BANCADA ESCUTA AQUI. `Avisar` termina num `Peer.Send`, e pacote que saiu no fio nao volta
		// pra ser conferido -- uma bancada de servidor nao tem como perguntar "o jogador foi avisado?".
		// E justamente este pedaco ja falhou nesta fase: a punicao do Oozaru dizia o que ACONTECEU e
		// nao dizia a SAIDA, e nada no projeto percebeu, porque a ausencia de uma frase nao quebra nada.
		// Nula em jogo (uma comparacao por linha de chat). Ver `GameServer.FormasTeste.cs`.
		EscutaDeAvisos?.Add(texto);
		// O CORPO QUE A IA DIRIGE NAO OUVE O PROPRIO VERB (v0.2.1) -- ver `_iaApertando`, `GameServer.RepertorioDaIa.cs`.
		// (A bancada continua ouvindo: a escuta fica acima desta linha.)
		if (ReferenceEquals(para, _iaApertando) && !IaFalaComODonoDeTeste) { _frasesCaladasDaIa++; return; }
		// `falante: null` -- nao ha ninguem falando: e o servidor. Ver o parametro em `Mandar`.
		Mandar(para, tom, "", texto, falante: null);
	}

	/// <summary>
	/// O que da pra dizer.
	///
	/// Corta no tamanho do original e tira as quebras de linha -- uma fala com "\n" viraria
	/// varias linhas no painel e daria pra desenhar um cartaz no chat alheio. O resto do texto
	/// vai como veio: quem escapa o que aparece na tela e o cliente, na hora de desenhar.
	/// </summary>
	private static string Limpar(string s)
	{
		if (s.Length > Protocol.MaxFala) s = s[..Protocol.MaxFala];
		s = s.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
		return s.Trim();
	}
}
