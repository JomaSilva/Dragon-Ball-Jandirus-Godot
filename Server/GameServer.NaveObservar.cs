using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-18) OBSERVAR A NAVE DE FORA, E OUVIR A NAVE DE DENTRO.
///
/// ============================ O OBSERVAR VIROU CAMERA ============================
/// O dono: *"outra coisa e a opcao de observe, deveria 'assistir' o exterior da nave sem pode controla-la, bom pros jogadores q querem
/// ver a nave ir pelo espaco sem controla-la"*. O `verb/Observe` do DM e exatamente isso (`client.eye = ship_ref`, `ShipVessel.dm:295-303`),
/// e o port o tinha trocado pela carta estelar porque nao tinha olho remoto: o corte de interesse e POR ZONA, e ver de fora seria receber
/// os pacotes de uma zona em que o corpo nao esta.
///
/// O OLHO VAI PELO MESMO CAMINHO DO LEME, e e o dono quem tinha desenhado: *"e na nave, ao pilotar ou observar, teu corpo vai ficar la
/// parado enquanto vc controla a nave, e caso alguem te bata vc volta pro corpo principal"*. Observar LARGA O CORPO na ponte
/// (`LargarOCorpo`, o mesmo boneco do leme e da mente) e leva a atencao pro lado de fora do casco, na zona dele -- e la ela recebe tudo o
/// que quem esta naquela zona recebe (os corpos, os planetas por perto, o cenario), sem canal novo. O que muda e que este corpo de fora
/// e SO UM OLHO:
///   * ninguem o ve (`EstaOculto` -> o `Oculto` do snapshot) e ninguem esbarra nele (sai da grade de corpos, `EntraNaGrade`);
///   * ele nao anda nem mexe em nada (`PodeMexerOCorpo`) -- e anda com a nave: a cada tique o servidor o poe onde ela esta (o corpo do
///     piloto, se ha alguem ao leme; o casco, se ela esta parada ou seguindo sozinha), e quando ela troca de zona (pousa, decola) ele vai
///     junto (`TickDosObservadores`);
///   * a nave sozinha que passa por cima de um planeta nao pousa o olho nele (`TickDoEspaco`), e a estrela nao o queima;
///   * a fera nao o caca (`PresaDaFera`).
/// O CORPO NA PONTE E VOCE, como o do leme: apanhar nele te traz de volta (`AcordarNoCorpo`). E o olho, se algo o atingir (ninguem o ve,
/// mas um tiro perdido existe), tambem -- e a ficha e a mesma, entao o golpe ja caiu no corpo de verdade.
/// =================================================================================
///
/// ============================ QUEM ESTA AO LEME (OU OBSERVANDO) OUVE A NAVE ============================
/// O dono: *"enquanto vc pilota, deveria dar pra escutar e ver o chat q rola dentro da nave igual como se vc n tivesse pilotando"*. O
/// corpo que ficou na ponte esta na zona do interior, e a fala de quem esta la dentro ja chegava nele -- e morria ali (boneco nao tem
/// rede). Agora o boneco OUVE PELO DONO (`DonoQueOuvePeloBoneco`, lido no funil `Mandar`): o que chegaria nele chega em quem esta ao leme
/// ou observando, medido de onde o corpo esta -- a mesma distancia de quem estivesse de pe ali. E a volta: o que o dono fala tambem sai
/// la dentro, da boca do corpo da ponte (`Falar`). Na mente o boneco continua surdo: la quem medita esta noutro lugar, e o dono nao pediu.
/// ===================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Quem esta observando alguma nave agora (ids). O tique e a barra do tanque andam por aqui, e nao pelo `_players` inteiro.</summary>
	private readonly HashSet<int> _observadores = [];

	/// <summary>DEFEITO INJETADO (`--naveteste`, camada 10): o boneco da ponte volta a ser surdo -- quem esta ao leme nao ouve a nave. Lido pelo <see cref="DonoQueOuvePeloBoneco"/>. Falso em jogo, sempre.</summary>
	internal static bool PonteSurdaDeTeste;

	/// <summary>DEFEITO INJETADO (`--naveteste`, camada 10): a fala de quem esta ao leme volta a sair so do lado de fora. Lido pelo `Falar`. Falso em jogo, sempre.</summary>
	internal static bool LemeMudoPraDentroDeTeste;

	/// <summary>DEFEITO INJETADO (`--naveteste`, camada 11): o olho fica onde a observacao comecou -- a nave anda e ele nao vai junto. Lido pelo <see cref="TickDosObservadores"/>. Falso em jogo, sempre.</summary>
	internal static bool OlhoParadoDeTeste;

	/// <summary>DEFEITO INJETADO (`--naveteste`, camada 11): o olho aparece pra quem esta do lado de fora. Lido pelo `EstaOculto`. Falso em jogo, sempre.</summary>
	internal static bool OlhoAVistaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (`--naveteste`, camada 11): o olho e um corpo comum pro espaco -- a nave que passa por cima de um planeta sem pousar
	/// pousa o olho nele (e a estrela o queima). Lido pelo `TickDoEspaco`. Falso em jogo, sempre.
	/// </summary>
	internal static bool OlhoPousaDeTeste;

	/// <summary>A nave que este jogador observa de fora, ou nula. Laco e nao LINQ: o tique pergunta por observador.</summary>
	private Nave? NaveQueObservo(ServerPlayer pl)
	{
		if (pl.ObservaNave == 0) return null;
		foreach (Nave n in _naves)
			if (n.Id == pl.ObservaNave) return n;
		return null;
	}

	/// <summary>Este corpo e o OLHO de alguem que observa uma nave? (O `EstaOculto`, a grade, o espaco e a fera perguntam.)</summary>
	private static bool EhOlhoDeFora(ServerPlayer pl) => pl.ObservaNave != 0;

	/// <summary>
	/// ONDE O OLHO FICA: no corpo de quem esta ao leme (a nave pilotada e desenhada nele, e e ele que anda), ou no casco parado -- pousado,
	/// a deriva ou seguindo sozinho pelo nav system.
	/// </summary>
	private (ZoneKey Zona, Vec2 Pos) OndeOlhar(Nave n) =>
		n.PilotoId != 0 && _players.TryGetValue(n.PilotoId, out ServerPlayer? piloto)
			? (piloto.Zone, piloto.Pos)
			: (n.Zona, new Vec2(n.X, n.Y));

	/// <summary>
	/// COMECA A OBSERVAR -- do console da ponte. O corpo fica de pe ali (o boneco), e a atencao vai pro lado de fora do casco. A recusa vem
	/// do `LargarOCorpo` (ja fora do corpo, possuido) e ai nada acontece.
	/// </summary>
	private void ComecarAObservar(ServerPlayer pl, Nave n)
	{
		(ZoneKey zona, Vec2 pos) = OndeOlhar(n);
		if (!LargarOCorpo(pl, zona, pos)) return;

		pl.ObservaNave = n.Id;
		pl.PilotoDaNaveObservada = n.PilotoId;
		pl.Moving = false;
		_observadores.Add(pl.Id);
		MandarObservacao(pl, n);
		MandarCombustivel(pl);

		Avisar(pl, $"você observa {NomeDaNave(n)} de fora -- o seu corpo fica de pé na ponte. Aperte E pra voltar a ele.");
		Avisar(pl, Espaco.EhEspaco(n.Zona) ? "  o casco flutua no vazio." : $"  o casco está pousado em {n.Zona.Name}.");
		if (n.TemRumo) Avisar(pl, $"  ela segue sozinha rumo a {NomeDoRumo(n)}.");
		if (n.ArmaduraMax > 0) Avisar(pl, $"  casco: {n.Armadura / n.ArmaduraMax * 100:0}% inteiro.");
		GD.Print($"[server] {pl.Name} passou a observar a nave #{n.Id} de fora ({zona})");
	}

	/// <summary>
	/// PARA DE OBSERVAR: o olho volta pro corpo na ponte, onde ele estiver (`VoltarProCorpo`). Com o corpo de volta ao interior, a carta
	/// estelar volta a mostrar a nave (o `S2C.Nave` -- o cliente o apagou quando o olho saiu do interior).
	/// </summary>
	private void PararDeObservar(ServerPlayer pl, string motivo)
	{
		Nave? n = NaveQueObservo(pl);
		pl.ObservaNave = 0;
		pl.PilotoDaNaveObservada = 0;
		_observadores.Remove(pl.Id);
		MandarObservacao(pl, null);
		VoltarProCorpo(pl, motivo);
		MandarCombustivel(pl);
		if (n != null && NaveGrande.EhInterior(pl.Zone, out int id) && id == n.Id && PosicaoNaGalaxia(n.Zona, new Vec2(n.X, n.Y)) is { } g)
			MandarNaveVista(pl, n, g);
		GD.Print($"[server] {pl.Name} parou de observar{(n != null ? $" a nave #{n.Id}" : "")}");
	}

	/// <summary>
	/// O PACOTE DA OBSERVACAO (`S2C.Observando`): a nave (0 = acabou), o tipo e o nome dela, e quem esta ao leme. O cliente esconde o
	/// proprio corpo, larga o teclado e segue: o corpo do piloto quando ha um (e na tela dele que a nave e desenhada), ou o ponto que o
	/// servidor manda, com a nave desenhada no proprio olho. Sai ao comecar, ao acabar e quando o leme troca de mao.
	/// </summary>
	private static void MandarObservacao(ServerPlayer pl, Nave? n)
	{
		if (pl.Peer == null) return;
		var w = Protocol.Begin(Protocol.S2C.Observando);
		w.Put(n?.Id ?? 0);
		w.Put(n?.Tipo ?? "");
		w.Put(n?.PilotoId ?? 0);
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O TIQUE DOS OLHOS, no fim do `TickDasNaves` (com as naves ja onde ficaram neste tique): cada olho vai pra onde a nave dele esta, troca
	/// de zona com ela, e ouve quando o leme muda de mao. A nave que deixou de existir devolve o olho ao corpo.
	/// </summary>
	private void TickDosObservadores()
	{
		if (_observadores.Count == 0) return;
		foreach (int id in _observadores.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.ObservaNave == 0) { _observadores.Remove(id); continue; }
			if (NaveQueObservo(pl) is not { } n) { PorNaFilaDeVolta(pl.Id); continue; }
			if (OlhoParadoDeTeste) continue;

			(ZoneKey zona, Vec2 pos) = OndeOlhar(n);
			if (!zona.Equals(pl.Zone)) MoveToZone(pl.Id, zona, pos);
			else pl.Pos = pos;

			if (pl.PilotoDaNaveObservada != n.PilotoId)
			{
				pl.PilotoDaNaveObservada = n.PilotoId;
				MandarObservacao(pl, n);
			}
		}
	}

	// =====================================================================
	// O BONECO DA PONTE OUVE PELO DONO
	// =====================================================================
	/// <summary>
	/// QUEM OUVE POR ESTE CORPO: o dono de um boneco que esta no interior de uma Capital Ship, se o dono esta ao leme DELA ou a observando de
	/// fora. Nulo pra todo o resto (o corpo comum, o boneco da mente, o dono que saiu). Lido no `Mandar` -- o funil de toda fala e todo aviso.
	/// </summary>
	private ServerPlayer? DonoQueOuvePeloBoneco(ServerPlayer corpo)
	{
		if (PonteSurdaDeTeste || corpo.DonoDoCorpoLargado == 0 || corpo.Peer != null) return null;
		if (!NaveGrande.EhInterior(corpo.Zone, out int id)) return null;
		if (!_players.TryGetValue(corpo.DonoDoCorpoLargado, out ServerPlayer? dono) || dono.Peer == null) return null;
		if (dono.ObservaNave == id) return dono;
		foreach (Nave n in _naves)
			if (n.Id == id) return n.PilotoId == dono.Id ? dono : null;
		return null;
	}

	/// <summary>
	/// O CORPO DA PONTE DE QUEM ESTA AO LEME OU OBSERVANDO -- e dele que a fala do dono sai la dentro. Nulo pra quem nao esta numa das duas.
	/// </summary>
	private ServerPlayer? CorpoNaPonteDe(ServerPlayer pl) =>
		pl.BonecoLargado is { } b && DonoQueOuvePeloBoneco(b) == pl ? b : null;
}
