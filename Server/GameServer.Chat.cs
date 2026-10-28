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

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diaglingua`): toda fala sai SEM a marca de automatica (`Protocol.MarcaDeFalaAutomatica`)
	/// -- o jogo de antes da lingua: o grito da raiva de quem joga em portugues chega em portugues a quem joga em ingles.
	/// Lido pelo <see cref="Mandar"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool FalaSemMarcaDeTeste;

	/// <param name="digitada">
	/// (2026-10-22) A FRASE FOI ESCRITA POR UMA PESSOA: a do `C2S.Chat` e as que repetem o que alguem escreveu (o grito que o
	/// jogador deu a tecnica dele, a frase que ele mandou o companheiro dizer). Todo o resto -- o grito da raiva, o da
	/// transformacao, o emote do golpe, a fala do NPC -- e do servidor, e vai com a marca de AUTOMATICA: e o que deixa a tela
	/// de quem joga em ingles traduzi-la, e so ela (ver `Client/Idioma.cs`). Padrao FALSO de proposito: o chamador novo do
	/// futuro quase sempre e mais uma frase do jogo, e o repasse de texto digitado que esquecer o argumento so faz o tradutor
	/// TENTAR a frase -- e frase escrita por gente quase nunca casa com uma do dicionario.
	/// </param>
	private void Falar(ServerPlayer a, Protocol.Fala canal, string texto, bool digitada = false)
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

		// (2026-10-27) A GAGUEIRA (o pedido 119): quem fala CAIDO com o tronco ferido tropeca nas palavras -- so a fala de boca (o say e o
		// sussurro) e so a DIGITADA (a frase automatica do jogo tem que continuar casando com o dicionario de ingles). Ver
		// `Core/Combat/Gagueira.cs`.
		// (2026-10-27, o pedido 127) ...a CABECA ferida tambem gagueja (vale a pior das duas, `Gagueira.FracaoQueFala`); e com o CEREBRO em 40% ou
		// menos, de pe ou caido, as palavras de conteudo viram nonsense ANTES (a ideia do jogador gingo -- `Core/Combat/FalaSemSentido.cs`).
		if (digitada && canal is Protocol.Fala.Diz or Protocol.Fala.Sussurro && a.Combate?.Corpo is { } corpoQueFala)
		{
			if (corpoQueFala.Achar("Cerebro") is { } cerebro && Jandirus.Core.Combat.FalaSemSentido.Confunde(cerebro.Decepado ? 0 : cerebro.Fracao))
				texto = Jandirus.Core.Combat.FalaSemSentido.Embaralhar(texto, NomesQueAFalaGuarda());
			if (a.Ficha.KO)
				texto = Jandirus.Core.Combat.Gagueira.Gaguejar(texto, Jandirus.Core.Combat.Gagueira.FracaoQueFala(corpoQueFala));
			// A FALA TROPECADA CRESCE, e o cliente le no maximo `MaxFala` letras (`GameClient`, `GetString`): acima disso ela chegava VAZIA.
			if (texto.Length > Protocol.MaxFala) texto = texto[..Protocol.MaxFala];
		}
		EscutaDeFalas?.Add((a.Id, canal, texto));

		// (2026-10-18) QUEM ESTA AO LEME DA NAVE GRANDE, OU A OBSERVANDO DE FORA, FALA TAMBEM LA DENTRO -- pela boca do corpo que ficou na
		// ponte, medido de onde ele esta (*"igual como se vc n tivesse pilotando"*). Quem OBSERVA fala SO la dentro: o olho do lado de fora
		// nao tem boca (ninguem o ve). O piloto continua falando tambem do lado de fora, como sempre falou -- e la ele ja se ouve, entao o
		// corpo da ponte dele fica fora da volta de dentro (senao a frase chegaria duas vezes). Ver `GameServer.NaveObservar.cs`.
		ServerPlayer? daPonte = LemeMudoPraDentroDeTeste ? null : CorpoNaPonteDe(a);
		bool soDentro = daPonte != null && EhOlhoDeFora(a);
		// DE ONDE A FRASE VEIO, uma vez so pra todos os envios: ver `Protocol.MarcaDeFalaAutomatica`
		byte marca = (byte)((digitada ? 0 : Protocol.MarcaDeFalaAutomatica) | (EhNpcDoMundo(a) ? Protocol.MarcaDeAutorNpc : 0));

		switch (canal)
		{
			case Protocol.Fala.Ooc:
				// o unico canal que atravessa planeta: e conversa de jogador, nao de personagem --
				// por isso o `falante: null`, que e o que impede duas pessoas em galaxias
				// diferentes de se conhecerem por escrito (ver `Ouviu`)
				foreach (ServerPlayer o in _players.Values) Mandar(o, canal, a.Name, texto, falante: null, marca: marca);
				break;

			case Protocol.Fala.Sussurro:
				if (!soDentro) Sussurrar(a, a, texto, excetoEste: null, marca);
				if (daPonte != null) Sussurrar(a, daPonte, texto, excetoEste: soDentro ? null : daPonte, marca);
				break;

			case Protocol.Fala.Diz:
			{
				float raio = texto.Contains('!') ? RaioDoGrito : RaioDaVista;
				// (2026-10-27) A LINGUA DO SAY (o pedido 118): so a fala DIGITADA de uma pessoa, e so quando ela escolheu outra lingua que nao a
				// Galactica -- cada ouvinte a le pela maestria dele (`Mandar`).
				string? lingua = digitada && EhPessoa(a) && LinguaDaFala(a) is { } l && l != Jandirus.Core.Races.Linguas.Galactica ? l : null;
				if (!soDentro) PorPerto(a, a, raio, canal, texto, excetoEste: null, marca, lingua);
				if (daPonte != null) PorPerto(a, daPonte, raio, canal, texto, excetoEste: soDentro ? null : daPonte, marca, lingua);
				break;
			}

			default:   // Pensa, Emote, Looc: todos no alcance da vista
				if (!soDentro) PorPerto(a, a, RaioDaVista, canal, texto, excetoEste: null, marca);
				if (daPonte != null) PorPerto(a, daPonte, RaioDaVista, canal, texto, excetoEste: soDentro ? null : daPonte, marca);
				break;
		}
	}

	/// <summary>
	/// A FALA DE <paramref name="a"/> SAINDO DA BOCA DE UM CORPO -- o dele, ou o dele na ponte. <paramref name="excetoEste"/> nao ouve (o
	/// corpo da ponte de quem ja se ouviu do lado de fora). O OLHO de quem observa uma nave nao ouve o lado de fora: ele esta la so pra ver,
	/// e o corpo dele, que ouve, esta la dentro.
	///
	/// (2026-10-19) A BOCA E UM CORPO, e nao uma zona e um ponto soltos: e dela que sai o alcance (zona e posicao) E o id que vai no pacote
	/// pro balao (ver o parametro `boca` do <see cref="Mandar"/>). Com os tres vindo do mesmo objeto, a frase de quem pilota nao tem como
	/// sair do corpo da ponte e ir parar no balao do piloto, que nem esta naquela zona.
	/// </summary>
	private void PorPerto(ServerPlayer a, ServerPlayer boca, float raio, Protocol.Fala canal, string texto, ServerPlayer? excetoEste, byte marca,
						  string? lingua = null)
	{
		float raio2 = raio * raio;
		foreach (ServerPlayer o in ZoneList(boca.Zone.Hash))
		{
			if (o == excetoEste || EhOlhoDeFora(o)) continue;
			Vec2 dd = o.Pos - boca.Pos;
			if (dd.X * dd.X + dd.Y * dd.Y <= raio2) Mandar(o, canal, a.Name, texto, a, boca.Id, marca, lingua);
		}
	}

	/// <summary>
	/// O SUSSURRO, DA BOCA DE UM CORPO -- DOIS ENVIOS, como no original: quem esta colado ouve o que foi dito, e quem esta em volta recebe a fala
	/// VAZIA, que o cliente desenha como "Fulano sussurra alguma coisa...". Saber que houve um sussurro faz parte da cena. Os mesmos dois
	/// crivos do <see cref="PorPerto"/>.
	/// </summary>
	private void Sussurrar(ServerPlayer a, ServerPlayer boca, string texto, ServerPlayer? excetoEste, byte marca)
	{
		foreach (ServerPlayer o in ZoneList(boca.Zone.Hash))
		{
			if (o == excetoEste || EhOlhoDeFora(o)) continue;
			float d = (o.Pos - boca.Pos).Length;
			if (d <= RaioDoSussurro) Mandar(o, Protocol.Fala.Sussurro, a.Name, texto, a, boca.Id, marca);
			// QUEM SO VIU QUE HOUVE UM SUSSURRO NAO CONVIVEU: nao chegou palavra nenhuma,
			// entao o `falante` vai nulo e a familiaridade nao anda.
			else if (d <= RaioDaVista) Mandar(o, Protocol.Fala.Sussurro, a.Name, "", falante: null, boca: boca.Id, marca);
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
	/// <param name="boca">
	/// O ID DO CORPO DE ONDE A FALA SAI (0 = nenhum), o campo do `S2C.Chat` depois do texto. E por ele que o cliente
	/// acha o balao (`World.AoFalar`).
	///
	/// ============================ (2026-10-19) O BALAO ACHAVA O CORPO PELO NOME, E NOME REPETE ============================
	/// O dono: *"as vezes o balao de fala dos personagens nao aparece sobre eles mesmo eles falando"*. O pacote
	/// levava so o `autor`, e o cliente o devolvia a um id pela busca reversa no mapa id -> nome do `PeerLook` --
	/// pegando o PRIMEIRO dono do nome na zona. Tres jeitos de errar, todos de jogo comum:
	///   * HOMONIMOS: os 100 habitantes de Vegeta saem de listas de 6 a 25 nomes por raca (`npcs.json`,
	///     `nomesPorRaca`), todo lobo se chama "Lobo", o corpo largado e o boneco da ponte levam o nome do dono. O
	///     primeiro do mapa costuma ser OUTRO corpo da zona, longe da tela: o balao abria la, onde ninguem via, e
	///     sobre quem falou nao aparecia nada -- ou aparecia no homonimo do lado;
	///   * NOME VISIVEL != AUTOR: o `PeerLook` leva o `NomeVisivel` (o da fusao, o do disfarce) e o chat o `Name`;
	///   * NOME COMPRIDO: o cliente lia o nome do `PeerLook` com teto de 24 letras, e acima dele vinha VAZIO (um
	///     "Bio-Androide de " ja tem 16). Hoje o teto e o `Protocol.MaxNomeNoFio`, nas duas pontas.
	/// Quem sabe de que corpo a fala saiu e o servidor, entao ele diz: e o conserto que o proprio `World.AoFalar`
	/// deixou anotado pro dia em que a colisao de nomes deixasse de ser teorica.
	///
	/// PADRAO 0, ao contrario do <paramref name="falante"/>: os chamadores de fora deste arquivo sao todos recados
	/// do servidor (`Avisar`, cargos, ranks), que nao saem de corpo nenhum; toda fala de corpo passa pelo
	/// <see cref="PorPerto"/> e pelo <see cref="Sussurrar"/>, os dois unicos que conhecem a boca -- e quem quiser
	/// um terceiro caminho de fala tem que passar por eles, porque so eles conhecem o alcance. O OOC fica no 0 de
	/// proposito: e conversa do JOGADOR, sem corpo (o portao do balao tambem o recusa).
	/// =====================================================================================================================
	/// </param>
	/// <param name="marca">
	/// (2026-10-22) DE ONDE A FRASE VEIO -- `Protocol.MarcaDeFalaAutomatica` e `MarcaDeAutorNpc`, o ultimo campo. PADRAO
	/// AUTOMATICA pelo mesmo motivo da `boca`: quem chama de fora deste arquivo manda recado do servidor; a fala de corpo
	/// vem do <see cref="Falar"/>, que sabe se alguem a digitou.
	/// </param>
	private void Mandar(ServerPlayer para, Protocol.Fala canal, string autor, string texto,
						ServerPlayer? falante, int boca = 0, byte marca = Protocol.MarcaDeFalaAutomatica, string? lingua = null)
	{
		// (2026-10-18) O CORPO DA PONTE OUVE PELO DONO: a fala (e o aviso) que chega no boneco de quem esta ao leme da nave grande, ou a
		// observando de fora, chega NELE -- *"enquanto vc pilota, deveria dar pra escutar e ver o chat q rola dentro da nave"*. Ver
		// `DonoQueOuvePeloBoneco`, `GameServer.NaveObservar.cs`.
		if (para.Peer == null && DonoQueOuvePeloBoneco(para) is { } dono) para = dono;

		// (2026-10-27) A LINGUA DO SAY (o pedido 118): quem nao a sabe inteira le EMBARALHADO pela maestria dele -- e aprende um pouco ouvindo
		// (so pessoa aprende; quem fala se ouve inteiro). A etiqueta diz a lingua. Ver `Core/Races/Linguas.cs`.
		if (lingua != null && falante != null)
		{
			double m = para == falante ? 1 : Jandirus.Core.Races.Linguas.Maestria(para.Ficha, lingua);
			if (para != falante && m < 1 && EhPessoa(para))
			{
				double antes = m;
				double depois = Jandirus.Core.Races.Linguas.Ouvir(para.Ficha, lingua);
				// o cartao da aba Stats anda a cada ponto inteiro de maestria, e nao a cada frase
				if (Math.Floor(depois * 100) > Math.Floor(antes * 100)) MandarLinguas(para);
			}
			texto = $"[{Jandirus.Core.Races.Linguas.Rotulo(lingua)}] " + Jandirus.Core.Races.Linguas.Embaralhar(texto, lingua, m);
		}
		EscutaDeEntregas?.Add((para.Id, autor, texto));

		if (falante != null) Ouviu(para, falante);

		var w = Protocol.Begin(Protocol.S2C.Chat);
		w.Put((byte)canal);
		// O AUTOR CORTADO NO TETO COM QUE O CLIENTE O LE: acima dele a linha chegava sem autor. Ver `Protocol.MaxNomeNoFio`.
		w.Put(Protocol.NomeNoFio(autor));
		w.Put(texto);
		w.Put(boca);
		w.Put(FalaSemMarcaDeTeste ? (byte)0 : marca);
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

	/// <summary>(2026-10-27, o pedido 127) Os nomes que a fala sem sentido deixa no lugar: os de quem esta no mundo agora, jogador e NPC.</summary>
	private IEnumerable<string> NomesQueAFalaGuarda()
	{
		foreach (ServerPlayer p in _players.Values) yield return p.Name;
	}
}
