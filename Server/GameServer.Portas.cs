using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DO MUNDO -- abrem quando alguem encosta e fecham sozinhas cinco segundos depois.
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// `Modules/Turfs/Doors.dm`. A porta e um turf com `density = 1`, e o `Enter(mob/M)` -- que o
/// BYOND chama quando alguem TENTA pisar nela -- responde:
///
///     if(M.KB) return                      // quem esta sendo arremessado nao abre porta
///     if(icon_state=="Open"||"Opening") return 1
///     Open()  ->  density=0; opacity=0; flick("Opening"); icon_state="Open"; spawn(50) Close()
///     Close() ->  density=1; opacity=1; flick("Closing"); icon_state="Closed"
///
/// `spawn(50)` sao 50 decimos de segundo: CINCO segundos. A porta de Namek (`Turfs.dm:693`) e o
/// mesmo codigo noutro ramo da arvore.
/// ==============================================================================
///
/// ============================ QUEM MANDA E O SERVIDOR ============================
/// A porta e do MUNDO, nao de quem a abriu: duas pessoas na mesma casa tem que ver a mesma porta.
/// Por isso o estado vive aqui e chega ao cliente por pacote -- o cliente nao decide abrir nada.
///
/// E O ESTADO E POR ZONA, nao por jogador. O servidor guarda UM `ZoneCollision` por zona
/// (`ZoneEntry.Mapa`), entao abrir uma porta muda o mapa de todo mundo que estiver la, que e
/// exatamente o certo.
/// ================================================================================
///
/// SENHA FICA PRA DEPOIS, e de proposito: no original so a porta CONSTRUIDA
/// (`/turf/build/Door` + `Set_Password_On_Door`) tem senha, e construcao ainda nao ergue porta.
/// Toda porta que sai do mapa tem `password` nulo, ou seja, abre pra qualquer um -- que e o que
/// esta implementado aqui.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O `spawn(50)` do original, em milissegundos -- o relogio do servidor e em ms, entao a
	/// conversao acontece uma vez aqui em vez de em toda comparacao. O NUMERO mora no Core, junto
	/// da regra de encostar (<see cref="PortasDaZona"/>).
	/// </summary>
	private const long PortaFechaEmMs = (long)(PortasDaZona.SegundosAberta * 1000);

	/// <summary>`--portateste`: todo mundo nasce colado numa porta. Ver <see cref="NascerNaPorta"/>.</summary>
	private bool _portaDeTeste;

	/// <summary>
	/// Poe o jogador DUAS celulas ao sul da primeira porta da zona dele, olhando pro norte.
	///
	/// EXISTE PELO MESMO MOTIVO DO `--techteste`: as 82 portas do jogo estao dentro de casas, longe
	/// do ponto de nascimento, e um teste automatico que precisa de meio minuto de caminhada antes
	/// da primeira medicao simplesmente nao roda. Duas celulas, e nao uma, pra que o primeiro passo
	/// do teste seja com a porta ainda FECHADA -- e o estado que precisa ser medido.
	/// </summary>
	private void NascerNaPorta(ServerPlayer pl)
	{
		if (!_portasDoMapa.TryGetValue(pl.Zone.Name, out List<PortaDoMapa>? portas) || portas.Count == 0) return;

		PortaDoMapa p = portas[0];
		const int t = ZoneCollision.TileSize;
		pl.Pos = new Vec2(p.X * t + t / 2f, (p.Y + 2) * t + t / 2f - MoveRules.FeetOffsetY);
		Godot.GD.Print($"[server] BANCADA: {pl.Name} nasce em ({pl.Pos.X:0},{pl.Pos.Y:0}), porta na celula ({p.X},{p.Y})");
	}

	/// <summary>Onde estao as portas de cada zona (o `.portas` do pipeline). So leitura.</summary>
	private readonly Dictionary<string, List<PortaDoMapa>> _portasDoMapa = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// As portas ABERTAS agora, por zona: celula -> instante (ms) em que ela fecha sozinha.
	///
	/// So as abertas ficam no dicionario. Uma zona sem ninguem dentro nao aparece aqui, e as 99
	/// portas do jogo inteiro custam nada -- nao ha varredura de porta fechada em lugar nenhum.
	/// </summary>
	private readonly Dictionary<string, Dictionary<(int X, int Y), long>> _portasAbertas =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Le o `.portas` de cada zona do manifesto. Chamado uma vez, no boot, junto do `.col`.
	/// </summary>
	private void CarregarPortas()
	{
		if (_catalogo == null) return;

		int total = 0;
		foreach (ZoneEntry e in _catalogo.Todas)
		{
			if (e.Portas.Length == 0 || !Godot.FileAccess.FileExists(e.Portas)) continue;
			List<PortaDoMapa> lista = PortasDaZona.Parse(Godot.FileAccess.GetFileAsString(e.Portas));
			if (lista.Count == 0) continue;
			_portasDoMapa[e.Zona] = lista;
			total += lista.Count;
		}
		if (total > 0) Godot.GD.Print($"[server] portas: {total} em {_portasDoMapa.Count} zona(s)");
	}

	/// <summary>
	/// Abre o que alguem encostou e fecha o que passou da hora. Roda no tick cheio.
	///
	/// O CUSTO E O NUMERO DE JOGADORES, nao o de portas: so as zonas com gente sao olhadas, e
	/// mesmo a mais cheia (Namek) tem 29 portas. Um `for` de 29 comparacoes de retangulo por
	/// jogador por tick nao aparece em perfil nenhum.
	/// </summary>
	private void TickDasPortas()
	{
		long agora = NowMs();

		// ---- fecha o que venceu ----
		foreach ((string zona, Dictionary<(int X, int Y), long> abertas) in _portasAbertas)
		{
			if (abertas.Count == 0) continue;
			List<(int X, int Y)>? vencidas = null;
			foreach (((int X, int Y) celula, long quando) in abertas)
				if (agora >= quando) (vencidas ??= []).Add(celula);

			if (vencidas == null) continue;
			foreach ((int X, int Y) celula in vencidas)
			{
				abertas.Remove(celula);
				MexerNaPorta(zona, celula.X, celula.Y, abrir: false);
				AnunciarPorta(zona, celula.X, celula.Y, aberta: false);
			}
		}

		// ---- abre o que alguem encostou ----
		foreach (ServerPlayer pl in _players.Values)
		{
			// O `if(M.KB) return` do original: quem esta no chao nao abre porta. Morto tambem nao.
			// O CADAVER nao abre porta; o fantasma de pe abre (era `pl.Ficha.dead` cru, e por isso "as portas
			// do outro mundo" nunca abriam pra ninguem morto -- o dono, 2026-09-04). No Outro Mundo a porta
			// ainda pergunta pela ALMA: ver `APortaDoOutroMundoAbrePara`.
			if (pl.Ficha.KO || EhCadaver(pl)) continue;
			if (!_portasDoMapa.TryGetValue(pl.Zone.Name, out List<PortaDoMapa>? portas)) continue;

			// PRECISA ESTAR ANDANDO. E a outra metade do `Enter()`: no BYOND a porta so sabe de
			// alguem quando esse alguem TENTA dar um passo pra dentro dela. Sem isto, parar ao lado
			// de uma porta a manteria aberta pra sempre -- o prazo de 5 s renovaria a cada tique.
			if (!pl.Moving) continue;

			// A REGRA MORA NO CORE, pelo mesmo motivo que a de andar: regra do jogo com duas
			// implementacoes e regra que diverge.
			foreach (PortaDoMapa p in portas)
				if (PortasDaZona.VaiEntrar(pl.Pos, pl.Facing, p.X, p.Y))
				{
					if (!APortaDoOutroMundoAbrePara(pl, agora)) break;
					AbrirPorta(pl.Zone.Name, p.X, p.Y, agora);
				}
		}
	}

	/// <summary>
	/// Abre (ou RENOVA o prazo de) uma porta.
	///
	/// Renovar importa: no original quem esta parado no vao segura a porta, porque cada tentativa
	/// de passar chama o `Enter()` de novo. Sem isto a porta fecharia na cara de quem ainda esta
	/// atravessando.
	/// </summary>
	private void AbrirPorta(string zona, int cx, int cy, long agora)
	{
		if (!_portasAbertas.TryGetValue(zona, out Dictionary<(int X, int Y), long>? abertas))
		{
			abertas = [];
			_portasAbertas[zona] = abertas;
		}

		bool jaEstava = abertas.ContainsKey((cx, cy));
		abertas[(cx, cy)] = agora + PortaFechaEmMs;
		if (jaEstava) return;   // so o prazo mudou: ninguem precisa saber

		// O MAPA DA ZONA E UM SO. Abrir aqui abre pra todo mundo que estiver nela -- e e por isso
		// que a alteracao vai numa camada por cima do bitset, e nao no bitset (ver ZoneCollision).
		MexerNaPorta(zona, cx, cy, abrir: true);
		AnunciarPorta(zona, cx, cy, aberta: true);
	}

	/// <summary>
	/// A PORTA MEXE NOS **DOIS** MAPAS DA ZONA -- o que barra o corpo e o que barra a vista.
	///
	/// ============================ O CLIENTE JA FAZIA ISSO E O SERVIDOR NAO PODIA ============================
	/// O `World.AoMudarPortas` mexe nos dois desde sempre, com o comentario que explica por que: *"o
	/// `.col` porque a porta deixa de barrar o corpo, e o `.vis` porque ela deixa de barrar a vista"*.
	/// Aqui so havia UM mapa a mexer, porque o servidor nunca tinha lido o `.vis`.
	///
	/// Agora ele leu (a voz local pergunta a ele se ha parede entre duas pessoas), e essa e a linha que
	/// a leitura obriga: sem ela, atravessar uma porta ABERTA deixaria a voz abafada dos dois lados --
	/// o servidor continuaria vendo o muro que o jogo ja abriu, e a queixa que chegaria seria "a voz
	/// abafa em porta aberta", com a porta visivelmente aberta na tela. E o defeito exato que o
	/// comentario do cliente existe pra nao deixar acontecer.
	/// ====================================================================================================
	/// </summary>
	private void MexerNaPorta(string zona, int cx, int cy, bool abrir)
	{
		if (_catalogo?.Get(zona) is not { } e) return;
		if (abrir) { e.Mapa?.Abrir(cx, cy); e.Vista?.Abrir(cx, cy); }
		else { e.Mapa?.Fechar(cx, cy); e.Vista?.Fechar(cx, cy); }
	}

	/// <summary>Uma porta mudou: quem estiver na zona precisa ver.</summary>
	private void AnunciarPorta(string zona, int cx, int cy, bool aberta)
	{
		var w = Protocol.Begin(Protocol.S2C.Porta);
		w.PutPortas(completo: false, [(cx, cy, aberta)]);
		foreach (ServerPlayer pl in _players.Values)
			if (string.Equals(pl.Zone.Name, zona, StringComparison.OrdinalIgnoreCase))
				pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// A LISTA INTEIRA pra quem acabou de chegar na zona.
	///
	/// Sem isto quem entra num planeta desenha toda porta fechada, inclusive as que outra pessoa
	/// esta segurando aberta -- e pior: o `.col` do cliente e o do servidor discordariam, e a
	/// discordancia entre os dois e exatamente o que faz o personagem tremer na parede.
	/// </summary>
	private void MandarPortas(ServerPlayer pl)
	{
		var lista = new List<(int, int, bool)>();
		if (_portasAbertas.TryGetValue(pl.Zone.Name, out Dictionary<(int X, int Y), long>? abertas))
			foreach (((int X, int Y) celula, _) in abertas) lista.Add((celula.X, celula.Y, true));

		var w = Protocol.Begin(Protocol.S2C.Porta);
		w.PutPortas(completo: true, lista);
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
