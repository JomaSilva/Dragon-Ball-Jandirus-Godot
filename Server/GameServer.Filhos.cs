using System.Text.Json;
using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.Social;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>O PEDIDO DE FILHO na mesa de quem foi pedido (nulo = ninguem pediu). Ver `GameServer.Filhos.cs`.</summary>
public sealed record PedidoDeFilho(string SigDeQuem, string Nome, long Ate);

/// <summary>
/// OS FILHOS (2026-10-17) -- o dono: *"faça com q seja possivel ter filhos no jogo, entao quando um player chega em nivel relaçao
/// "amor" com outro jogador do sexo oposto, eles possam ter filho ... e quando alguem for ter um filho, o proximo jogador q for entrar
/// criar um personagem mesmo q seja no modo de raça random, vai aparecer uma mensagem perguntando se ele quer ser o filho do jogador x
/// e y e assim nascendo com 0 anos"*.
///
/// E o `Mating.dm` do DM: o `Have_Child` consensual, a gravidez e o parto pela criacao (`CreationUI.dm:350-381`, `race.dm:8-28`,
/// `SpawnPoints.dm:132-147`). As regras de quem pode e do que o filho herda moram no Core (<see cref="Filhos"/>); aqui ficam as tres
/// pecas do servidor:
///
///   1. O PEDIDO: `filho_pedir` pra quem esta na frente, e o `filho_sim` / `filho_nao` de quem foi pedido -- um pendente com prazo,
///      como o convite de mestre (o DM usava `input()`, que trava a tela). O "sim" REVALIDA tudo. E O OVO (a resposta do dono,
///      2026-10-17): `filho_ovo`, o `Lay_Egg` -- as racas de ovo tem filho sozinhas.
///   2. OS BEBES A CAMINHO (`bebes.json`): o "sim" concebe um -- a foto dos pais -- e ele espera la ate alguem nascer dele. A criacao
///      oferece o mais antigo (no fim do `S2C.SlotList`), a quem quer que crie personagem: a conta nao importa, nem o modo de racas.
///   3. O PARTO: o `CreateChar` de quem aceitou (`CharacterDraft.FilhoDe`) -- a raca, zero anos, uma das linhagens do bebe, a classe
///      mais rara dos pais e o poder herdado; o corpo ao lado da mae, quando ela esta no jogo -- e o do ovo, ao lado do OVO, que fica no
///      chao (o dono, 2026-10-17: *"tinha um sprite de ovo"*) e some quando o filho sai dele.
/// </summary>
public sealed partial class GameServer
{
	private sealed class BebesNoDisco
	{
		public int Proximo = 1;
		public List<BebeACaminho> Bebes = [];
	}

	private BebesNoDisco _bebes = new();

	private const string NomeDoArquivoDosBebes = "bebes.json";
	private string CaminhoDosBebes => System.IO.Path.Combine(_store?.Pasta ?? ".", NomeDoArquivoDosBebes);

	/// <summary>DEFEITO INJETADO (bancada `--filhosteste`): basta UM declarar amor pro pedido valer -- o outro nem precisa gostar. Falso em jogo.</summary>
	internal static bool AmorDeUmLadoSoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--filhosteste`): o filho nasce como um personagem comum da raca -- sem a classe mais rara dos pais e sem
	/// o poder herdado. Falso em jogo.
	/// </summary>
	internal static bool FilhoSemHerancaDeTeste;

	// =====================================================================
	// O DISCO
	// =====================================================================
	/// <summary>Le os bebes a caminho. O leitor ESVAZIA antes de ler: a devolucao da caixa da `--wipeteste` le de novo por cima.</summary>
	private void CarregarBebes()
	{
		_bebes = new BebesNoDisco();
		try
		{
			if (System.IO.File.Exists(CaminhoDosBebes))
				_bebes = JsonSerializer.Deserialize<BebesNoDisco>(System.IO.File.ReadAllText(CaminhoDosBebes),
					new JsonSerializerOptions { IncludeFields = true }) ?? new BebesNoDisco();
		}
		catch (Exception e) { GD.PushWarning($"[server] {NomeDoArquivoDosBebes} ilegivel: {e.Message}"); }
		if (_bebes.Bebes.Count > 0) GD.Print($"[server] filhos: {_bebes.Bebes.Count} bebe(s) a caminho");
	}

	private void SalvarBebes()
	{
		if (_store == null) return;
		try
		{
			string tmp = CaminhoDosBebes + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(_bebes, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDosBebes, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao deu pra salvar {NomeDoArquivoDosBebes}: {e.Message}"); }
	}

	/// <summary>A limpeza total: no mundo novo os pais de bebe nenhum existem.</summary>
	private void ZerarBebes() => _bebes = new BebesNoDisco();

	/// <summary>O bebe que a tela de criacao oferece: o MAIS ANTIGO a caminho (quem foi concebido primeiro nasce primeiro), ou nenhum.</summary>
	internal OfertaDeBebe? OfertaDaCriacao => _bebes.Bebes.Count > 0 ? _bebes.Bebes[0].Oferta : null;

	internal IReadOnlyList<BebeACaminho> BebesACaminhoDeTeste => _bebes.Bebes;

	// =====================================================================
	// O PEDIDO
	// =====================================================================
	private bool UsarVerboDeFilho(ServerPlayer pl, string id)
	{
		switch (id)
		{
			case "filho_pedir": PedirFilho(pl); return true;
			case "filho_sim": ResponderAoFilho(pl, aceitou: true); return true;
			case "filho_nao": ResponderAoFilho(pl, aceitou: false); return true;
			case "filho_ovo": PorOvo(pl); return true;
			default: return false;
		}
	}

	/// <summary>O jogador desta assinatura, se estiver no jogo. Sem lambda: o fato do amor pergunta isto cinco vezes por segundo.</summary>
	private ServerPlayer? JogadorDaAssinatura(string sig)
	{
		if (sig.Length == 0) return null;
		foreach (ServerPlayer p in _players.Values)
			if (p.Assinatura == sig && EhJogador(p)) return p;
		return null;
	}

	/// <summary>Os dois declararam AMOR um pelo outro (a aba People)?</summary>
	private static bool AmorCorrespondido(ServerPlayer a, ServerPlayer b) =>
		a.Social.RelacaoCom(b.Assinatura) == Relacao.Amor
		&& (AmorDeUmLadoSoDeTeste || b.Social.RelacaoCom(a.Assinatura) == Relacao.Amor);

	/// <summary>
	/// ESTE PERSONAGEM TEM UM AMOR CORRESPONDIDO NO MUNDO? E o fato que acende o verb de ter um filho (`Fatos.TemUmAmor`). So varre
	/// quem ELE declarou amar -- quase sempre ninguem, ou um. O par e uma PESSOA de pe no mundo (`EhPessoa`: assinatura, e nao povo do
	/// mundo), e nao so um jogador: o fato e o BOTAO; quem pede e quem aceita tem de ser jogadores, e isso quem cobra e o
	/// `PorQueNaoTemFilho`. Sem lambda e sem lista: isto roda cinco vezes por segundo por jogador.
	/// </summary>
	internal bool TemAmorCorrespondido(ServerPlayer pl)
	{
		foreach (KeyValuePair<string, Conhecido> c in pl.Social.Conhecidos)
		{
			if (c.Value.Relacao != Relacao.Amor) continue;
			foreach (ServerPlayer o in _players.Values)
				if (o != pl && o.Assinatura == c.Key && EhPessoa(o) && AmorCorrespondido(pl, o)) return true;
		}
		return false;
	}

	/// <summary>A gravidez a caminho desta mae, se houver -- uma por vez, como o `Pregnant` do DM (`Mating.dm:181`).</summary>
	private BebeACaminho? BebeDaMae(string sigMae)
	{
		foreach (BebeACaminho b in _bebes.Bebes)
			if (b.SigMae == sigMae) return b;
		return null;
	}

	/// <summary>
	/// POR QUE ESTES DOIS NAO PODEM TER UM FILHO AGORA -- vazio = podem. A mesma pergunta no pedido e no "sim", que REVALIDA tudo (o
	/// `try_have_child` depois do `input()`, `Mating.dm:183-187`). A ordem e a de quem ensina: primeiro o que da pra mudar (chegar perto,
	/// acordar, declarar), depois o que e do corpo.
	/// </summary>
	internal string PorQueNaoTemFilho(ServerPlayer a, ServerPlayer b)
	{
		if (!EhJogador(a) || !EhJogador(b) || a == b) return "ter um filho é coisa de dois jogadores.";
		// A MORTE E A REGRA AQUI: o `mate_pair_ok` recusa o morto (`Mating.dm:34`). Nao e trava de luta -- essa o morto nao tem.
		if (a.Ficha.dead || b.Ficha.dead) return "os mortos não têm filhos.";
		if (a.Ficha.KO || b.Ficha.KO) return "vocês dois precisam estar acordados.";
		if (a.Zone.Hash != b.Zone.Hash || Vec2.Distance(a.Pos, b.Pos) > Filhos.TilesDoPedido * ZoneCollision.TileSize)
			return $"{b.Name} precisa estar do seu lado.";
		if (a.Social.RelacaoCom(b.Assinatura) != Relacao.Amor)
			return $"ter um filho é coisa de quem se ama: declare AMOR por {b.Name} na aba People (e {b.Name} por você).";
		if (!AmorCorrespondido(a, b)) return $"{b.Name} ainda não declarou amor por você.";
		if (!Filhos.SexosOpostos(a.Genero, b.Genero)) return "um casal precisa ser de sexos opostos para ter um filho.";
		// A RACA E A DO CORPO (`ServerPlayer.Race`, a que vai pro save): a conversao em androide escreve so ela -- a da ficha so a
		// acompanha no proximo login.
		if (_racas != null)
			foreach (ServerPlayer p in (ReadOnlySpan<ServerPlayer>)[a, b])
				if (!Filhos.ComoGera(_racas, p.Race, p.Ficha.AndroideInfinito).HasFlag(ModoDeGerar.Gravidez))
					return Filhos.PorQueNaoTemFilhoComPar(_racas, p.Race, p.Ficha.AndroideInfinito);
		foreach (ServerPlayer p in (ReadOnlySpan<ServerPlayer>)[a, b])
			if (p.Idade < Filhos.IdadeParaGerar(p.Race))
				return $"{(p == a ? "você ainda é" : $"{p.Name} ainda é")} jovem demais para ter filhos ({Filhos.IdadeParaGerar(p.Race)} anos).";
		if (Filhos.RacaDoFilho(a.Race, b.Race).Length == 0)
			return "raças diferentes não têm filho -- só Humano, Saiyajin e meio-Saiyajin se misturam (o androide de energia infinita conta como Humano).";
		ServerPlayer mae = a.Genero == "Female" ? a : b;
		if (BebeDaMae(mae.Assinatura) is { } jaVem)
			return jaVem.DeOvo ? $"{mae.Name} já tem um ovo a caminho." : $"{mae.Name} já espera um bebê de {jaVem.Pai}.";
		return "";
	}

	/// <summary>A raca poe ovo e o corpo ja tem idade pra gerar? E o fato que acende "Pôr um ovo" (`Fatos.PoeOvo`).</summary>
	internal bool PoeOvoAgora(ServerPlayer pl) =>
		_racas != null && Filhos.ComoGera(_racas, pl.Race, pl.Ficha.AndroideInfinito).HasFlag(ModoDeGerar.Ovo)
		&& pl.Idade >= Filhos.IdadeParaGerar(pl.Race);

	/// <summary>
	/// "PÔR UM OVO" -- o `Lay_Egg` (`Mating.dm:222-236`): as racas de ovo tem filho SOZINHAS (o dono, 2026-10-17). O ovo e um bebe a
	/// caminho de um pai so (<see cref="Filhos.Ovo"/>), e choca como os outros: o proximo personagem que alguem criar. Primeiro o que o
	/// botao exige (a raca, a idade), depois o corpo.
	///
	/// O OVO FICA NO CHAO (o dono, 2026-10-17: *"tinha um sprite de ovo"*), como no DM: o `new /mob/Egg(usr.loc)` (`Mating.dm:230`) vira a
	/// obra <see cref="Filhos.TipoDoOvo"/>, com a arte do `Egg.dmi`, e o filho nasce do lado dela (`SpawnPoints.dm:136-137`). O chao e
	/// conferido ANTES de o ovo existir: sem lugar, o gesto nao acontece.
	///
	/// DIVERGENCIAS DECLARADAS: o ovo cai do LADO de quem o pos, e nao debaixo dos pes (ele e solido -- ver `ChaoDoOvo`); e no DM nao havia
	/// limite de ovos, nem morte ou nocaute que impedisse: aqui e um por vez, vivo e acordado.
	/// </summary>
	private void PorOvo(ServerPlayer pl)
	{
		if (_racas != null && !Filhos.ComoGera(_racas, pl.Race, pl.Ficha.AndroideInfinito).HasFlag(ModoDeGerar.Ovo))
		{ Avisar(pl, Filhos.PorQueNaoPoeOvo(_racas, pl.Race, pl.Ficha.AndroideInfinito)); return; }
		int idade = Filhos.IdadeParaGerar(pl.Race);
		if (pl.Idade < idade) { Avisar(pl, $"você ainda é jovem demais para pôr um ovo ({idade} anos)."); return; }
		if (!EhJogador(pl) || pl.Assinatura.Length == 0) { Avisar(pl, "pôr um ovo é coisa de jogador."); return; }
		if (pl.Ficha.dead) { Avisar(pl, "os mortos não põem ovo."); return; }
		if (pl.Ficha.KO) { Avisar(pl, "você precisa estar acordado."); return; }
		if (BebeDaMae(pl.Assinatura) is { } jaVem)
		{ Avisar(pl, jaVem.DeOvo ? "você já tem um ovo a caminho." : $"você já espera um bebê de {jaVem.Pai}."); return; }

		// O CHAO DO OVO, antes de o ovo existir. Nos lugares em que nada se assenta (mente, bolso da fusao, selo, o interior de um Majin -- `LugarDeObra`)
		// ele nao cai, pelo mesmo motivo de uma construcao nao cair: a chave daquela zona e de sessao, e o ovo gravado ali voltaria noutro lugar.
		Vec2? chao = null;
		if (!Filhos.OvoSemCorpoDeTeste)
		{
			if (!LugarDeObra.Aceita(pl.Zone)) { Avisar(pl, "o ovo precisa de chão de verdade, e aqui não há."); return; }
			chao = ChaoDoOvo(pl);
			if (chao == null) { Avisar(pl, "não há chão livre do seu lado pra pôr o ovo."); return; }
		}

		BebeACaminho ovo = Filhos.Ovo(_bebes.Proximo++, NowMs(), pl.Name, pl.Assinatura, pl.Race, pl.Ficha.Class, pl.Ficha.SaiyanLineage, pl.Ficha.BP);
		_bebes.Bebes.Add(ovo);
		if (chao is { } lugar) ovo.Ovo = PorOOvoNoChao(pl, lugar).Id;
		SalvarBebes();
		GD.Print($"[server] filhos: {pl.Name} pos um ovo -- #{ovo.Id}, {NomeDaRaca.Bonito(ovo.Raca)}"
			+ (ovo.Linhagens.Length > 0 ? $", linhagem {string.Join("/", ovo.Linhagens)}" : ""));
		// `"[usr] lays an egg!"` pra quem esta perto (`view(9)`, `Mating.dm:228`) -- aqui, a zona dele.
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			if (EhJogador(o)) Avisar(o, $"{pl.Name} põe um ovo! Ele vai chocar como o próximo personagem que alguém criar.");
		Avisar(pl, "quem criar um personagem novo vai ser convidado a nascer do seu ovo -- até você mesmo, em outro slot.");
	}

	/// <summary>
	/// ONDE O OVO CAI: a primeira celula livre em volta de quem o poe -- a da frente primeiro, depois as dos lados, a de tras e as diagonais.
	/// No DM ele nascia no tile de quem o pos (`new(usr.loc)`, `Mating.dm:230`); aqui ele e SOLIDO -- o `mob/Egg` e denso, como todo mob do
	/// BYOND -- e nao pode nascer dentro de um corpo. As guardas sao as de assentar qualquer coisa: o alcance e a parede
	/// (`Assentamento.DoLugar`, com as passagens da nave grande) e nao empilhar (`Assentamento.TemCoisaEm`; as naves paradas e a mobilia da
/// nave grande contam). Nulo = nao ha chao.
	/// </summary>
	private Vec2? ChaoDoOvo(ServerPlayer pl)
	{
		const int t = ZoneCollision.TileSize;
		(int cx, int cy) = Jandirus.Core.Tech.CatalogoDeObras.Celula(pl.Pos.X, pl.Pos.Y);
		(int fx, int fy) = pl.Facing switch { Facing.North => (0, -1), Facing.East => (1, 0), Facing.West => (-1, 0), _ => (0, 1) };
		(int Dx, int Dy)[] volta = [(fx, fy), (fy, fx), (-fy, -fx), (-fx, -fy), (1, 1), (1, -1), (-1, 1), (-1, -1)];
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		List<Vec2> ocupados = [.. _noChao.Where(o => o.Zona.Equals(pl.Zone)).Select(o => new Vec2(o.X, o.Y))
			.Concat(NavesParadasEm(pl.Zone).Select(n => new Vec2(n.X, n.Y)))
			.Concat(MobiliaDoInterior(pl.Zone).Select(m => Jandirus.Core.Tech.NaveGrande.PixelDe(m.Peca.Cel)))];
		foreach ((int dx, int dy) in volta)
		{
			int x = cx + dx, y = cy + dy;
			if (Jandirus.Core.Tech.Assentamento.DoLugar(mapa, pl.Pos, x, y, pl.Zone) != Jandirus.Core.Tech.RecusaDeAssento.Pode) continue;
			var ponto = new Vec2(x * t + t / 2f, y * t + t / 2f - MoveRules.FeetOffsetY);
			if (!Jandirus.Core.Tech.Assentamento.TemCoisaEm(ocupados, ponto)) return ponto;
		}
		return null;
	}

	/// <summary>
	/// O OVO NO CHAO -- a obra <see cref="Filhos.TipoDoOvo"/> (o `mob/Egg` do DM), de quem o pos e na zona dele. Sem verbo nenhum
	/// (`Interacoes` nao tem linha pra ela): ninguem a carrega, e o unico jeito de ela sair do lugar e o filho nascer ou alguem a quebrar.
	/// A ARMADURA E A PADRAO, de proposito: o `mob/Egg` tinha `BP = 100` e `HP = 100` (`NPClist.dm:21-23`) e caia no primeiro golpe de
	/// qualquer um -- defender o ovo e parte do preco de te-lo. Vai pro `mundo.json` como toda obra erguida.
	/// </summary>
	private Obra PorOOvoNoChao(ServerPlayer pl, Vec2 lugar)
	{
		var ovo = new Obra
		{
			Id = _proximaObraId++,
			Tipo = Filhos.TipoDoOvo,
			X = lugar.X,
			Y = lugar.Y,
			DonoConta = pl.Conta,
			DonoNome = pl.Name,
			ErguidaEm = NowMs(),
		};
		ovo.PorZona(pl.Zone);
		_noChao.Add(ovo);
		GravarMundo();
		MandarObras(pl.Zone);
		return ovo;
	}

	/// <summary>O ovo de um bebe a caminho, de pe no chao -- ou nulo (gravidez, ovo de antes do corpo, ovo quebrado).</summary>
	private Obra? OvoNoChao(BebeACaminho b) =>
		b.Ovo == 0 ? null : _noChao.Find(o => o.Id == b.Ovo && o.Tipo == Filhos.TipoDoOvo);

	/// <summary>
	/// O OVO QUEBROU (um soco, um corpo arremessado): o filho que ia nascer dele se perde, e quem o pos fica sabendo. No DM o `mob/Egg` era
	/// atacavel (`attackable = 1`, `NPClist.dm:17`) e, morto, sumia -- e com ele o lugar de nascer (`SpawnPoints.dm:136`). Chamada pela queda
	/// de toda obra (`Estragar`), ao lado do pote do Mafuba e do deposito: o que a obra segurava cai com ela.
	/// </summary>
	private void OOvoQuebrou(Obra o)
	{
		if (o.Tipo != Filhos.TipoDoOvo) return;
		BebeACaminho? b = _bebes.Bebes.Find(x => x.DeOvo && x.Ovo == o.Id);
		if (b == null) return;
		_bebes.Bebes.Remove(b);
		SalvarBebes();
		if (JogadorDaAssinatura(b.SigMae) is { } mae) Avisar(mae, "o seu ovo foi quebrado -- o filho que ia nascer dele se perdeu.");
		GD.Print($"[server] filhos: o ovo de {b.Mae} (#{b.Id}, obra #{o.Id}) foi quebrado -- o bebe saiu da fila");
	}

	/// <summary>
	/// `Have_Child` (`Mating.dm:139-145`): o pedido sai pra quem esta NA FRENTE -- o gesto social de sempre (`QuemEstaNaFrente`). Sem
	/// ninguem na frente, o que falta PRIMEIRO e o requisito do botao (`hab:filho_pedir`): quem nao tem amor correspondido no mundo ouve
	/// isso (a recusa da tabela), e nao "fique de frente". Com alguem na frente, o porque e o de `PorQueNaoTemFilho`.
	/// </summary>
	private void PedirFilho(ServerPlayer pl)
	{
		ServerPlayer? par = QuemEstaNaFrente(pl, o => EhJogador(o) && o.Assinatura.Length > 0);
		if (par == null)
		{
			Avisar(pl, TemAmorCorrespondido(pl) ? "fique de frente para quem você ama."
				: "ninguém no jogo te ama de volta (os dois declaram AMOR na aba People).");
			return;
		}
		string nao = PorQueNaoTemFilho(pl, par);
		if (nao.Length > 0) { Avisar(pl, nao); return; }
		par.PedidoDeFilho = new PedidoDeFilho(pl.Assinatura, pl.Name, NowMs() + (long)(Filhos.PrazoDoPedidoSegundos * 1000));
		Avisar(pl, $"você pede a {par.Name} pra terem um filho.");
		Avisar(par, $"{pl.Name} quer ter um filho com você. Aceite ou recuse (você tem {Filhos.PrazoDoPedidoSegundos:0} s).");
	}

	/// <summary>A resposta de quem foi pedido. O "sim" revalida TUDO -- entre o pedido e a resposta alguem pode ter se afastado ou caido.</summary>
	private void ResponderAoFilho(ServerPlayer pl, bool aceitou)
	{
		if (pl.PedidoDeFilho is not { } p) { Avisar(pl, "ninguém te pediu um filho."); return; }
		pl.PedidoDeFilho = null;
		if (NowMs() > p.Ate) { Avisar(pl, $"o pedido de {p.Nome} já tinha expirado."); return; }
		ServerPlayer? quem = JogadorDaAssinatura(p.SigDeQuem);
		if (quem == null) { Avisar(pl, $"{p.Nome} não está mais por aqui."); return; }
		if (!aceitou)
		{
			Avisar(pl, "você prefere esperar.");
			Avisar(quem, $"{pl.Name} prefere esperar.");   // `"[M] prefere esperar."` (`Mating.dm:187`)
			return;
		}
		string nao = PorQueNaoTemFilho(quem, pl);
		if (nao.Length > 0) { Avisar(pl, nao); Avisar(quem, nao); return; }

		BebeACaminho b = Conceber(quem, pl);
		// `"[mom] esta gravida! ..."` pra quem esta perto (`view(9)`, `Mating.dm:198`) -- aqui, a zona dela.
		ServerPlayer mae = pl.Assinatura == b.SigMae ? pl : quem;
		foreach (ServerPlayer o in ZoneList(mae.Zone.Hash))
			if (EhJogador(o)) Avisar(o, $"{b.Mae} está grávida! O bebê de {b.Pai} e {b.Mae} vai nascer como o próximo personagem que alguém criar.");
		foreach (ServerPlayer o in (ReadOnlySpan<ServerPlayer>)[pl, quem])
			Avisar(o, "quem criar um personagem novo vai ser convidado a nascer como o filho de vocês -- até você mesmo, em outro slot.");
	}

	/// <summary>O "sim": a mae fica gravida -- a FOTO dos pais vira um bebe a caminho (`mom.Pregnant`, `Husband_*`, `womb`; `Mating.dm:189-197`).</summary>
	private BebeACaminho Conceber(ServerPlayer a, ServerPlayer b)
	{
		(ServerPlayer pai, ServerPlayer mae) = a.Genero == "Male" ? (a, b) : (b, a);
		string raca = Filhos.RacaDoFilho(pai.Race, mae.Race);
		string linhaPai = Filhos.LinhagemDe(pai.Race, pai.Ficha.Class, pai.Ficha.SaiyanLineage);
		string linhaMae = Filhos.LinhagemDe(mae.Race, mae.Ficha.Class, mae.Ficha.SaiyanLineage);
		var bebe = new BebeACaminho
		{
			Id = _bebes.Proximo++, ConcebidoEm = NowMs(),
			Pai = pai.Name, Mae = mae.Name, SigPai = pai.Assinatura, SigMae = mae.Assinatura,
			RacaDoPai = pai.Race, RacaDaMae = mae.Race,
			ClasseDoPai = pai.Ficha.Class, ClasseDaMae = mae.Ficha.Class,
			LinhagemDoPai = linhaPai, LinhagemDaMae = linhaMae,
			BpDoPai = Filhos.BpDoPai(pai.Ficha.BP), BpDaMae = Math.Max(0, mae.Ficha.BP),
			Raca = raca, Linhagens = Filhos.LinhagensDoFilho(raca, linhaPai, linhaMae),
		};
		_bebes.Bebes.Add(bebe);
		SalvarBebes();
		GD.Print($"[server] filhos: {mae.Name} esta gravida de {pai.Name} -- bebe #{bebe.Id}, {NomeDaRaca.Bonito(raca)}"
			+ (bebe.Linhagens.Length > 0 ? $", linhagens {string.Join("/", bebe.Linhagens)}" : ""));
		return bebe;
	}

	// =====================================================================
	// O PARTO -- o `CreateChar` de quem aceitou nascer de um bebe
	// =====================================================================
	/// <summary>
	/// O BEBE DESTA FICHA, com a ficha ja ajustada a ele -- ou nulo, quando ele ja nasceu de outro (quem chega primeiro leva). A raca e a
	/// dos pais, o mundo de origem e o dela (o meio-Saiyajin, que nao esta em lista de planeta nenhum, e da Terra -- `Bercos`), a idade
	/// e ZERO e a linhagem tem de ser uma das do bebe: a tela so oferece essas, e a de um cliente mexido vira a primeira.
	/// </summary>
	private BebeACaminho? BebeParaNascer(CharacterDraft ficha)
	{
		BebeACaminho? b = _bebes.Bebes.Find(x => x.Id == ficha.FilhoDe);
		if (b == null) return null;
		ficha.Race = b.Raca;
		string planeta = SorteioDeRaca.PlanetaDaRaca(b.Raca);
		ficha.Planet = planeta.Length > 0 ? planeta : "Earth";
		ficha.Age = 0;
		ficha.ChosenClass = b.Linhagens.Length == 0 ? ""
			: Array.IndexOf(b.Linhagens, ficha.ChosenClass) >= 0 ? ficha.ChosenClass : b.Linhagens[0];
		return b;
	}

	/// <summary>
	/// O CORPO DO FILHO: o `Nascer` de sempre, com a classe mais rara dos pais (`Filhos.ClasseDoFilho`) e o poder herdado -- o BP ganha de
	/// 1/24 a 1/6 do de cada um (`Genome.StartingBp`). (2026-10-26) O POTENCIAL NAO SE HERDA MAIS -- o dono: *"os filhos q antes herdavam o
	/// bp dos dois pais como hidden n vao ter mais isso"*: o banco do potencial e o do DU, e enche treinando (`GainKnobs.PotencialDoGanhoMin`).
	/// Era `hiddenpotential += Father_BP` e `+= Parent_BP` (`CharacterCreation.dm:53-58`).
	/// </summary>
	private Fighter NascerDoBebe(CharacterDraft ficha, string nome, BebeACaminho b)
	{
		bool herda = !FilhoSemHerancaDeTeste && _racas != null;
		string classe = herda ? Filhos.ClasseDoFilho(_racas!, b, ficha.ChosenClass, _rng) : "";
		return Nascer(ficha, nome, classe, herda ? b.BpDoPai : 0, herda ? b.BpDaMae : 0);
	}

	/// <summary>
	/// O FILHO NASCE DO LADO DA MAE -- `loc = locate(M.x-1, M.y, M.z)` (`SpawnPoints.dm:137`) --, quando ela esta no jogo, viva e fora do
	/// Outro Mundo; o do ovo, do lado do OVO no chao (o `M` daquela linha e o proprio ovo), esteja quem o pos no jogo ou nao. O ovo de antes
	/// do corpo (sem obra) nasce do lado de quem o pos. Fora disso ele acorda no berco da raca, como todo mundo. DIVERGENCIA DECLARADA: o
	/// berco de RENASCER continua o da raca (no DM o filho herdava o `spawnPlanet` da mae, `:140`). Devolve se pos.
	/// </summary>
	private bool PorAoLadoDaMae(CharacterSave filho, BebeACaminho b)
	{
		if (b.DeOvo && OvoNoChao(b) is { } ovo)
		{
			const float t = ZoneCollision.TileSize;
			var onde = new Vec2(ovo.X, ovo.Y);
			if (PontoLivre(ovo.Zona, onde - new Vec2(t, 0), onde + new Vec2(t, 0), onde + new Vec2(0, t), onde - new Vec2(0, t)) is { } doLado)
			{
				filho.Zona = ovo.ZonaNome;
				filho.ZonaTipo = ovo.ZonaTipo;
				filho.ZonaSeed = ovo.ZonaSeed;
				filho.X = doLado.X;
				filho.Y = doLado.Y;
				return true;
			}
		}
		if (JogadorDaAssinatura(b.SigMae) is not { } mae || mae.Ficha.dead || Alem.EhOAlem(mae.Zone)) return false;
		const float T = ZoneCollision.TileSize;
		Vec2 lugar = PontoLivre(mae.Zone, mae.Pos - new Vec2(T, 0), mae.Pos + new Vec2(T, 0), mae.Pos + new Vec2(0, T), mae.Pos - new Vec2(0, T))
			?? mae.Pos;
		filho.Zona = mae.Zone.Name;
		filho.ZonaTipo = mae.Zone.Kind;
		filho.ZonaSeed = mae.Zone.Seed;
		filho.X = lugar.X;
		filho.Y = lugar.Y;
		return true;
	}

	/// <summary>
	/// O bebe sai da fila (e do disco): ele nasceu. Antes do `Entrar`, junto com a gravacao do filho. E o OVO SE DESFAZ junto -- o
	/// `del(M)` de `SpawnPoints.dm:146`: o filho saiu dele.
	/// </summary>
	private void TirarOBebeDaFila(BebeACaminho b)
	{
		_bebes.Bebes.Remove(b);
		SalvarBebes();
		if (OvoNoChao(b) is { } ovo)
		{
			_noChao.Remove(ovo);
			GravarMundo();
			MandarObras(ovo.Zona);
		}
	}

	/// <summary>
	/// O PARTO NO MUNDO: quem nasceu e os pais sabem -- `"[src] just popped out of [M]!"`, `"You were born from [M]!"`, `"[src] was born
	/// from you!"` (`SpawnPoints.dm:142-144`). Longe da mae, o filho tambem ouve a historia do berco em que acordou.
	/// </summary>
	private void NasceuDoBebe(ServerPlayer filho, BebeACaminho b, bool aoLadoDaMae, Berco berco)
	{
		Avisar(filho, b.DeOvo ? $"você nasceu! Saiu do ovo de {b.Mae}." : $"você nasceu! Filho(a) de {b.Pai} e {b.Mae}.");
		if (!aoLadoDaMae) Avisar(filho, HistoriaDoBerco(berco));
		ServerPlayer? mae = JogadorDaAssinatura(b.SigMae);
		if (mae != null) Avisar(mae, b.DeOvo ? $"{filho.Name} saiu do seu ovo!" : $"{filho.Name} nasceu de você!");
		// QUEM ESTA EM VOLTA VE O PARTO -- o `"[src] just popped out of [M]!"` pra `view()` (`SpawnPoints.dm:142`): a zona de quem nasceu,
		// que e a da mae ou a do ovo (com quem o pos longe dali, ou fora do jogo).
		if (aoLadoDaMae)
			foreach (ServerPlayer o in ZoneList(filho.Zone.Hash))
				if (EhJogador(o) && o != mae && o != filho)
					Avisar(o, b.DeOvo ? $"{filho.Name} acaba de sair do ovo de {b.Mae}!" : $"{filho.Name} acaba de nascer de {b.Mae}!");
		if (JogadorDaAssinatura(b.SigPai) is { } pai) Avisar(pai, $"{filho.Name}, o seu filho com {b.Mae}, nasceu!");
	}
}
