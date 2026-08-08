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

	private readonly Dictionary<int, long> _ultimaFala = [];

	private void Falar(ServerPlayer a, Protocol.Fala canal, string texto)
	{
		texto = Limpar(texto);
		if (texto.Length == 0) return;

		long agora = NowMs();
		if (_ultimaFala.TryGetValue(a.Id, out long antes) && agora - antes < MsEntreFalas) return;
		_ultimaFala[a.Id] = agora;

		switch (canal)
		{
			case Protocol.Fala.Ooc:
				// o unico canal que atravessa planeta: e conversa de jogador, nao de personagem --
				// por isso o `falante: null`, que e o que impede duas pessoas em galaxias
				// diferentes de se conhecerem por escrito (ver `Ouviu`)
				foreach (ServerPlayer o in _players.Values) Mandar(o, canal, a.Name, texto, falante: null);
				break;

			case Protocol.Fala.Sussurro:
				// DOIS ENVIOS, como no original: quem esta colado ouve o que foi dito, e quem
				// esta em volta recebe a fala VAZIA -- que o cliente desenha como "Fulano
				// sussurra alguma coisa...". Saber que houve um sussurro faz parte da cena.
				foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
				{
					float d = (o.Pos - a.Pos).Length;
					if (d <= RaioDoSussurro) Mandar(o, canal, a.Name, texto, a);
					// QUEM SO VIU QUE HOUVE UM SUSSURRO NAO CONVIVEU: nao chegou palavra nenhuma,
					// entao o `falante` vai nulo e a familiaridade nao anda.
					else if (d <= RaioDaVista) Mandar(o, canal, a.Name, "", falante: null);
				}
				break;

			case Protocol.Fala.Diz:
			{
				float raio = texto.Contains('!') ? RaioDoGrito : RaioDaVista;
				PorPerto(a, raio, canal, texto);
				break;
			}

			default:   // Pensa, Emote, Looc: todos no alcance da vista
				PorPerto(a, RaioDaVista, canal, texto);
				break;
		}
	}

	private void PorPerto(ServerPlayer a, float raio, Protocol.Fala canal, string texto)
	{
		float raio2 = raio * raio;
		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
		{
			Vec2 dd = o.Pos - a.Pos;
			if (dd.X * dd.X + dd.Y * dd.Y <= raio2) Mandar(o, canal, a.Name, texto, a);
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
		if (falante != null) Ouviu(para, falante);

		var w = Protocol.Begin(Protocol.S2C.Chat);
		w.Put((byte)canal);
		w.Put(autor);
		w.Put(texto);
		// CONFIAVEL, sempre. Fala perdida e a unica perda que o jogador percebe como sumico:
		// um golpe faltando vira lag, uma frase faltando vira "ele me ignorou".
		para.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Uma linha do servidor pra UM jogador so: aviso, recusa, resultado de comando.</summary>
	public void Avisar(ServerPlayer para, string texto)
	{
		// A BANCADA ESCUTA AQUI. `Avisar` termina num `Peer.Send`, e pacote que saiu no fio nao volta
		// pra ser conferido -- uma bancada de servidor nao tem como perguntar "o jogador foi avisado?".
		// E justamente este pedaco ja falhou nesta fase: a punicao do Oozaru dizia o que ACONTECEU e
		// nao dizia a SAIDA, e nada no projeto percebeu, porque a ausencia de uma frase nao quebra nada.
		// Nula em jogo (uma comparacao por linha de chat). Ver `GameServer.FormasTeste.cs`.
		EscutaDeAvisos?.Add(texto);
		// `falante: null` -- nao ha ninguem falando: e o servidor. Ver o parametro em `Mandar`.
		Mandar(para, Protocol.Fala.Sistema, "", texto, falante: null);
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
