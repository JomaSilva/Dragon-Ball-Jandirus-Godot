using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Npc;
using Jandirus.Core.Social;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ A LISTA DA MENTE (2026-10-27, o pedido 124c) ============================
/// A meditacao profunda nova -- ver o cabecalho de `Core.Npc.MenteOponentes`. O que mora aqui:
///   * QUEM SE CONHECE: os chefes vistos (`ChefesVistos`, de sempre), os HABITANTES vistos de perto (`AnotarHabitantesVistos`, pelo molde
///     E PELO PLANETA onde foi visto -- o cidadao nao tem raca sem ele, ver `MenteOponentes.ChaveDeHabitante`) e os JOGADORES conhecidos (a aba People, com o poder e a cara de quando foram vistos -- ver
///     `Conhecido.PoderVisto`);
///   * A LISTA na tela (`MandarOponentesDaMente`): ela abre ao entrar na propria mente e depois de cada vitoria; a foto dos habitantes e
///     dos chefes e um corpo do molde sorteado com semente fixa (`FotoDoMolde`);
///   * A ESCOLHA (`ChamarOponenteDaMente`): uma lembranca por vez, so na propria mente, sem visita, e nunca no meio de uma luta;
///   * O FIM DA LUTA: derrubar a lembranca e VITORIA (`VencerNaMente`: o BP pela forca dela, e a lista de novo); cair e DERROTA
///     (`MarcarADerrotaNaMente`: acorda, e o foco abalado nao mergulha por `MenteOponentes.MinutosSemMergulhoAoPerder`). Acordar no meio,
///     nao (`LutandoNaMente`, no `SairDoTranse`);
///   * A DERROTA TEM DUAS PORTAS E UMA MARCA: em jogo quem cai e tirado da mente pela BORDA do corpo largado (`BordasDeQuemEstaFora`, no
///     `TickCombate`, antes do tique da lembranca), e a marca vem no `SairDaMente`; sem corpo largado (a bancada, o corpo que sumiu) quem
///     pega e o ramo da lembranca no `TicarUmCorpo` (`PerderNaMente`);
///   * A LISTA FECHA em toda saida (`SairDaMente`) e quando a luta comeca (a escolha, ou o chefe pelo menu P), e volta pro dono que ficou
///     sozinho quando a visita vai embora. A FORMA do chefe com mais de uma etapa vista e perguntada na propria lista (`TelaDaMente`).
/// =========================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Uma linha da lista: a categoria, o id do pedido, o nome, e a foto quando o servidor a manda.</summary>
	internal readonly record struct OponenteDaMente(MenteOponentes.Tipo Tipo, string Id, string Nome, string Raca, string Genero, Appearance? Foto);

	/// <summary>A ESCUTA DA BANCADA: cada lista que sairia (quem, abrir, as linhas). Nula em jogo.</summary>
	internal static List<(int Quem, bool Abrir, List<OponenteDaMente> Linhas)>? EscutaDaListaDaMente;

	/// <summary>Este corpo e um habitante que entra na lista de quem o ve: NPC do mundo, de um molde de habitante, fora de qualquer mente.</summary>
	private bool HabitanteDaMente(ServerPlayer o) =>
		EhNpcDoMundo(o) && o.Papel is { Molde: { Id.Length: > 0 } m } && !m.EhChefe && MenteOponentes.TipoDeHabitante(m.Tipo)
		&& !BillsEWhis.EhMolde(m.Id) && !NaMente(o) && !o.Ficha.dead;

	/// <summary>
	/// OS HABITANTES QUE EU CONHECO -- a regra dos chefes vistos (`AnotarChefesVistos`): quem passou a `RaioDaVista` de mim fica gravado,
	/// pelo MOLDE E PELO PLANETA (o cidadao da Terra, o de Namek, o Saibaman...). A 1 Hz, do tique do servidor. Sem frase: habitante passa
	/// toda hora.
	/// </summary>
	private void AnotarHabitantesVistos()
	{
		if (_racas == null) return;
		float raio2 = RaioDaVista * RaioDaVista;
		foreach (ServerPlayer npc in _players.Values)
		{
			if (!HabitanteDaMente(npc)) continue;
			MoldeDeNpc molde = npc.Papel!.Molde;
			string chave = MenteOponentes.ChaveDeHabitante(molde.Id, npc.Zone.Name);
			bool perguntei = false, nasceAqui = false;
			foreach (ServerPlayer o in ZoneList(npc.Zone.Hash))
			{
				if (!EhPessoa(o) || NaMente(o)) continue;
				if ((o.Pos - npc.Pos).LengthSquared > raio2) continue;
				if (o.Ficha.HabitantesDaMente?.Contains(chave) == true) continue;
				// SO ONDE ELE PODE NASCER (a pergunta do `Sortear`, uma vez por habitante novo): o cidadao que seguiu alguem pro espaco nao tem
				// raca la, e a lembranca dele seria uma linha que nao se monta nem nasce.
				if (!perguntei) { perguntei = true; nasceAqui = SorteioDeNpc.RacasPossiveis(molde, npc.Zone.Name, _racas).Length > 0; }
				if (!nasceAqui) break;
				(o.Ficha.HabitantesDaMente ??= []).Add(chave);
			}
		}
	}

	/// <summary>
	/// A foto de um molde: um corpo dele sorteado com semente FIXA (o mesmo molde no mesmo planeta da sempre a mesma cara na lista). O
	/// `planeta` da a raca de quem nao a declara (o cidadao); o chefe passa vazio.
	/// </summary>
	private (string Raca, string Genero, Appearance Foto) FotoDoMolde(MoldeDeNpc molde, string planeta)
	{
		if (_racas == null) return ("", "", new Appearance());   // sem catalogo de racas (nunca em jogo): sem foto de verdade
		ulong semente = SorteioDeNpc.SementeDe(SeedDoUniverso, planeta.Length > 0 ? MenteOponentes.ChaveDeHabitante(molde.Id, planeta) : molde.Id, 0);
		FichaSorteada s = SorteioDeNpc.Sortear(molde, semente, _racas, _skills, planeta, MediaDoServidor());
		return (s.Raca, s.Genero, AparenciaDeNpc(molde, s.Raca, s.Genero, semente));
	}

	/// <summary>Este habitante lembrado ainda se monta: o molde existe, e habitante, e tem raca naquele planeta. A chave velha de um molde
	/// que mudou some da lista calada, e nao derruba a lista inteira (o `Sortear` grita com planeta sem berco).</summary>
	private MoldeDeNpc? HabitanteLembrado(string chave, out string planeta)
	{
		if (!MenteOponentes.LerHabitante(chave, out string id, out planeta) || _moldes?.Get(id) is not { EhChefe: false } m
			|| !MenteOponentes.TipoDeHabitante(m.Tipo)) return null;
		return _racas == null || SorteioDeNpc.RacasPossiveis(m, planeta, _racas).Length > 0 ? m : null;
	}

	/// <summary>A LISTA desta pessoa, nas quatro categorias: voce, os habitantes vistos, os jogadores conhecidos e os chefes vistos.</summary>
	internal List<OponenteDaMente> MontarAListaDaMente(ServerPlayer pl)
	{
		var l = new List<OponenteDaMente> { new(MenteOponentes.Tipo.Reflexo, "", "O seu reflexo", pl.Race, pl.Genero, null) };
		if (_moldes != null)
		{
			var habitantes = new List<(string Nome, string Planeta, string Chave, MoldeDeNpc M)>();
			foreach (string chave in pl.Ficha.HabitantesDaMente ?? [])
				if (HabitanteLembrado(chave, out string planeta) is { } m) habitantes.Add((m.Nome.Length > 0 ? m.Nome : m.Id, planeta, chave, m));
			foreach ((string nome, string planeta, string chave, MoldeDeNpc m) in habitantes.OrderBy(h => h.Nome, StringComparer.Ordinal)
																							 .ThenBy(h => h.Planeta, StringComparer.Ordinal))
			{
				(string raca, string genero, Appearance foto) = FotoDoMolde(m, planeta);
				// o planeta com o nome que o jogador le (`NomeDoPlaneta`: "Terra", ou o batizado) -- a foto da rodada r4 mostrava "(Earth)"
				l.Add(new(MenteOponentes.Tipo.Habitante, chave, $"{nome} ({NomeDoPlaneta(planeta)})", raca, genero, foto));
			}
		}
		foreach (Conhecido c in pl.Social.EmOrdem())
			// com a cara de quando eu o vi (`Conhecido.AparenciaVista`, a mesma que veste a lembranca dele); ficha de antes da lista vai sem
			// ela, e o cliente usa a memoria dele (`VistosDeGente`)
			l.Add(new(MenteOponentes.Tipo.Jogador, c.Assinatura, c.Nome, c.Raca, c.Genero, c.AparenciaVista));
		if (_moldes != null)
		{
			var chefes = new List<MoldeDeNpc>();
			foreach (string id in pl.ChefesVistos)
				if (_moldes.Get(id) is { EhChefe: true } m) chefes.Add(m);
			chefes.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
			foreach (MoldeDeNpc m in chefes)
			{
				(string raca, string genero, Appearance foto) = FotoDoMolde(m, "");
				l.Add(new(MenteOponentes.Tipo.Chefe, m.Id, RotuloDoChefeNaMente(m, chefes), raca, genero, foto));
			}
		}
		return l;
	}

	private static string Curto(string s, int max) => s.Length <= max ? s : s[..max];

	/// <summary>Manda a lista (`abrir` = a tela abre agora) -- ou so a ordem de fechar, quando a luta comeca.</summary>
	private void MandarOponentesDaMente(ServerPlayer pl, bool abrir)
	{
		List<OponenteDaMente> linhas = abrir ? MontarAListaDaMente(pl) : [];
		EscutaDaListaDaMente?.Add((pl.Id, abrir, linhas));
		if (pl.Peer is not { } peer) return;
		var w = Protocol.Begin(Protocol.S2C.MenteOponentes);
		w.Put(abrir);
		int n = Math.Min(linhas.Count, 400);
		w.Put((ushort)n);
		foreach (OponenteDaMente o in linhas.Take(n))
		{
			w.Put((byte)o.Tipo);
			w.Put(Curto(o.Id, 90));
			w.Put(Curto(o.Nome, 90));
			w.Put(Curto(o.Raca, 30));
			w.Put(Curto(o.Genero, 14));
			w.Put(o.Foto != null);
			if (o.Foto != null) w.PutAppearance(o.Foto);
		}
		peer.Send(w, Protocol.ChannelReliable, LiteNetLib.DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Este corpo esta LUTANDO na propria mente: a lembranca que ele ergueu esta de pe.</summary>
	internal bool LutandoNaMente(ServerPlayer pl) =>
		NaMente(pl) && pl.CloneId != 0 && _players.TryGetValue(pl.CloneId, out ServerPlayer? c) && !c.Ficha.KO && !c.Ficha.dead;

	/// <summary>
	/// A ESCOLHA DA LISTA (`mente_oponente:&lt;tipo&gt;:&lt;id&gt;`, o canal de habilidade): uma lembranca por vez, so na propria mente, sem
	/// visita -- as mesmas portas do `ChamarChefe` --, e nunca no meio de uma luta (a luta nao se encerra no meio).
	/// </summary>
	private void ChamarOponenteDaMente(ServerPlayer pl, string resto)
	{
		if (!MenteOponentes.LerPedido(resto, out MenteOponentes.Tipo tipo, out string id)) { Avisar(pl, "essa lembranca ja nao existe."); return; }
		if (tipo == MenteOponentes.Tipo.Chefe) { ChamarChefe(pl, id); return; }   // as portas e a etapa dele moram la
		if (!NaMente(pl)) { Avisar(pl, "isso so acontece dentro da sua mente."); return; }
		if (DimensaoMental.Anfitriao(pl.Zone) != pl.Id) { Avisar(pl, "esta mente nao e a sua -- aqui quem chama as lembrancas e o dono dela."); return; }
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			if (o != pl && EhPessoa(o)) { Avisar(pl, $"{o.Name} esta na sua mente -- as lembrancas nao se erguem com visita."); return; }
		if (LutandoNaMente(pl)) { Avisar(pl, "a luta de agora ainda nao acabou -- uma lembranca por vez."); return; }
		DesfazerOOponente(pl, "");   // o corpo caido de antes, se ficou

		switch (tipo)
		{
			case MenteOponentes.Tipo.Reflexo:
			{
				ServerPlayer reflexo = CriarClone(pl, pl.Zone);
				pl.CloneId = reflexo.Id;
				Avisar(pl, "Diante de voce, VOCE -- e ele conhece cada golpe seu.");
				break;
			}
			case MenteOponentes.Tipo.Habitante:
			{
				if (pl.Ficha.HabitantesDaMente?.Contains(id) != true) { Avisar(pl, "voce nunca viu esse ser de perto."); return; }
				if (HabitanteLembrado(id, out string planeta) is not { } molde) { Avisar(pl, "essa lembranca ja nao existe."); return; }
				// a RACA vem do planeta onde ele foi visto: a mente nao e berco de ninguem (o `Sortear` recusaria a zona dela)
				ServerPlayer? npc = NascerNpc(molde.Id, pl.Zone, pl.Pos + new Vec2(DistanciaDoOponente, 0), ++_lugarDaMente, planeta: planeta);
				if (npc == null) { Avisar(pl, "a lembranca nao se formou."); return; }
				// as duas linhas que fazem dele uma lembranca -- as mesmas do chefe (`ChamarChefe`)
				npc.DonoDoClone = pl.Id;
				npc.Combate.Letal = false;
				pl.CloneId = npc.Id;
				Avisar(pl, $"a lembranca toma forma: {npc.Name} esta diante de voce.");
				break;
			}
			case MenteOponentes.Tipo.Jogador:
			{
				if (!pl.Social.Conhecidos.TryGetValue(id, out Conhecido? c)) { Avisar(pl, "voce nao conhece essa pessoa."); return; }
				ServerPlayer lembranca = CriarClone(pl, pl.Zone, c);
				pl.CloneId = lembranca.Id;
				Avisar(pl, $"a lembranca toma forma: {c.Nome} esta diante de voce.");
				break;
			}
		}
		GD.Print($"[server] {pl.Name} escolheu na mente: {tipo} '{id}' (id {pl.CloneId})");
		MandarOponentesDaMente(pl, abrir: false);
	}

	/// <summary>
	/// A VITORIA NA MENTE: a lembranca caiu. O BP de `MenteOponentes.SegundosDeTreinoDaVitoria` de treino (os passes do T, no ritmo da
	/// mente) vezes a forca dela sobre a sua -- e ela se desfaz, e a lista volta pra escolher a proxima.
	/// </summary>
	private void VencerNaMente(ServerPlayer dono, ServerPlayer npc)
	{
		double fator = MenteOponentes.Fator(npc.Ficha.expressedBP, dono.Ficha.expressedBP);
		double antes = dono.Ficha.BP;
		int passes = (int)Math.Round(MenteOponentes.SegundosDeTreinoDaVitoria / SegundosPorFicha * fator);
		for (int i = 0; i < passes; i++) dono.Ficha.TrainGain(_rng, 6.0 / (1 + Math.Log(2)));   // o fator do passe do T, o mesmo do `Treinar`
		string nome = npc.Name;
		DesfazerOOponente(dono, "");
		MandarFicha(dono);
		AvisarNaMente(dono.Zone, $"{nome} cai -- a lembranca se desfaz.");
		Avisar(dono, $"VITORIA na mente: {dono.Ficha.BP - antes:N0} de BP (a forca dela era {fator:0.##}x a sua). Escolha o proximo -- ou acorde.");
		GD.Print($"[server] {dono.Name} venceu '{nome}' na mente (fator {fator:0.##}, +{dono.Ficha.BP - antes:0.##} BP)");
		MandarOponentesDaMente(dono, abrir: true);
	}

	/// <summary>
	/// A DERROTA NA MENTE: o dono caiu com a lembranca de pe. A marca e a frase -- o foco abalado (o relogio sem mergulho) e quem o derrubou.
	/// Quem tira o corpo da mente e quem chamou: o `SairDaMente` (a borda do corpo largado, o caminho de jogo) ou o <see cref="PerderNaMente"/>.
	/// </summary>
	private string MarcarADerrotaNaMente(ServerPlayer dono)
	{
		string quem = _players.TryGetValue(dono.CloneId, out ServerPlayer? npc) ? npc.Name : "a lembranca";
		dono.Ficha.mergulho_bloqueado_ate = NowMs() + (long)(MenteOponentes.MinutosSemMergulhoAoPerder * 60_000);
		GD.Print($"[server] {dono.Name} perdeu na mente para '{quem}'");
		return $"{quem} te derruba. A derrota abala o seu foco: voce acorda, e so mergulha de novo em "
			 + $"{MenteOponentes.MinutosSemMergulhoAoPerder:0} min.";
	}

	/// <summary>
	/// A DERROTA PELO RAMO DA LEMBRANCA (`TicarUmCorpo`): o dono SEM corpo largado -- em jogo a borda o tira antes, ver o cabecalho. A frase
	/// sai agora, e a volta e a da onda (a do fim da luta, como era a da vitoria antes da lista).
	/// </summary>
	private void PerderNaMente(ServerPlayer dono)
	{
		Avisar(dono, MarcarADerrotaNaMente(dono));
		ComecarAVoltaDaMente(dono, "");
	}

	/// <summary>O robo da lista (`--diagmentelista`) poe o host na propria mente pela porta de producao, sem a onda.</summary>
	internal void EntrarNaMenteDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.med = true;
		EntrarNaMente(pl);
	}

	/// <summary>O robo da lista: o id da lembranca de pe (zero = nenhuma), se o corpo esta na mente e se o foco abalado o barra de mergulhar.</summary>
	internal (int Lembranca, bool NaMente, bool Barrado) MenteDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? (pl.CloneId, NaMente(pl), pl.Ficha.mergulho_bloqueado_ate > NowMs()) : (0, false, false);

	/// <summary>
	/// O robo da lista: a memoria de um habitante e do Freeza de Namek com DUAS formas vistas (a pergunta da forma aparece na lista), e o
	/// foco devolvido -- a conta da bancada PERDE no ultimo passo do robo, e a rodada seguinte tem que poder mergulhar.
	/// </summary>
	internal void SemearMemoriaDaMenteDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || _moldes == null) return;
		pl.Ficha.mergulho_bloqueado_ate = 0;
		if (_moldes.Todos.FirstOrDefault(m => !m.EhChefe && m.Tipo == TipoDeNpc.Cidadao) is { } h)
			(pl.Ficha.HabitantesDaMente ??= []).Add(MenteOponentes.ChaveDeHabitante(h.Id, "Earth"));
		if (_moldes.Get("freeza_namek") is { EhChefe: true } c)
		{
			pl.ChefesVistos.Add(c.Id);
			pl.EtapasDeChefe[c.Id] = [0, 3];
		}
	}

	/// <summary>O robo da lista derruba a lembranca (o funil comum do nocaute).</summary>
	internal void DerrubarALembrancaDeTeste(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && pl.CloneId != 0 && _players.TryGetValue(pl.CloneId, out ServerPlayer? c))
			c.Combate.Nocautear(10);
	}

	/// <summary>O robo da lista derruba o DONO com a lembranca de pe (o funil comum do nocaute): a derrota pelo caminho de jogo, a borda.</summary>
	internal void DerrubarODonoNaMenteDeTeste(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Combate.Nocautear(10);
	}
}
