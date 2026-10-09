using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA ABSORCAO DO MAJIN (`--majinteste`).
///
/// Dono, 2026-10-09: *"cheque pra mim se a absorçao do majin ta funcionando e se o jogador absorvido vai pro
/// interior do corpo do majin enfrentar o clone do corpo original pra escapar (assim como era no DM)"*.
///
/// Tudo aqui passa pela porta de producao: o verb entra pelo `UsarHabilidade` (o `case` do
/// `C2S.Habilidade`), a imagem e dirigida pelo `TickDosCorposSemDono`, as saidas sao colhidas pelo
/// `TickDoMajin`, e quem sai do jogo passa pelo `DesfazerAAbsorcaoAoSair` que o `Drop` chama. A bancada so
/// monta a cena (corpos forjados sem `Peer`, como as outras de boot) e le o resultado.
///
/// O QUE ELA NAO VE e a tela: o cenario do interior desenhado, os botoes de expelir e a roupa no boneco
/// sao da `--diagmajin` (`Client/RoboDaAbsorcaoMajin.cs`), de janela.
///
/// COMO RODAR: `Godot --headless --path . --server --port 7989 --majinteste`
/// </summary>
public partial class GameServer
{
	private const int IdBaseDoMajinDeTeste = 94_300;

	private int _mjOk, _mjFalhou, _mjCorpos;
	private ZoneCollision? _mjMapa;
	private int _mjProximaPraca = 60;

	private static readonly ZoneKey ZonaDaBancadaDoMajin = new(ZoneKey.KindPremade, "Earth");

	private void AfirmarMj(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _mjOk++; GD.Print($"[majin]   OK    {oque}"); return; }
		_mjFalhou++;
		GD.PrintErr($"[majin]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaAbsorcaoMajin()
	{
		_mjOk = _mjFalhou = 0;
		GD.Print("[majin] ================ A ABSORCAO DO MAJIN ================");

		_mjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDoMajin);
		AfirmarMj("a zona da bancada tem colisao carregada", _mjMapa != null);

		try
		{
			OLugar();
			AsContas();
			AsRecusasDoVerb();
			OJogadorEhSelado();
			AImagemLuta();
			VencerAImagemSolta();
			MorrerLaDentroSolta();
			OMajinCaidoSoltaTodos();
			ExpelirSolta();
			SairDoJogoDesfaz();
			ONpcEhDevorado();
			QuemViuSeEnfurece();
			NaoHaAtalhoPraFora();
			SobreAAguaCaiNaMargem();
		}
		catch (Exception e)
		{
			AfirmarMj($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			AbsorcaoMajin.GuardiaoVencidoNaoSoltaDeTeste = false;
			AbsorcaoMajin.NocauteNaoSoltaDeTeste = false;
			ClasseDePredio.QuemVoaAtravessaDeTeste = false;
			LimparABancadaDoMajin();
		}

		GD.Print($"[majin] ================ {_mjOk} passaram, {_mjFalhou} falharam ================");
	}

	// =====================================================================
	// OS CORPOS E A CENA
	// =====================================================================
	/// <summary>
	/// Uma praca 5x5 de CHAO SECO na Terra, cada chamada numa faixa nova (as familias nao se enxergam).
	///
	/// `ServeDeChao`, e nao `!BlockedCell`: agua nao e parede, e a primeira versao desta varredura pos as
	/// doze primeiras pracas no MAR (25 celulas de agua em 25). Quem e solto sobre a agua cai na margem --
	/// e as duas afirmacoes de "ao lado do Majin" reprovaram a 17 tiles. O caso virou a familia 14.
	/// </summary>
	private Vec2 PracaDoMajin()
	{
		const int T = ZoneCollision.TileSize;
		if (_mjMapa is { } mapa)
			for (int y = _mjProximaPraca; y < 240; y++)
				for (int x = 8; x < 240; x++)
				{
					bool livre = true;
					for (int dy = -2; dy <= 2 && livre; dy++)
						for (int dx = -2; dx <= 2 && livre; dx++)
							livre &= mapa.ServeDeChao(x + dx, y + dy);
					if (!livre) continue;
					_mjProximaPraca = y + 6;
					return new Vec2(x * T + T / 2f, y * T + T / 2f);
				}

		AfirmarMj("achei uma praca 5x5 de chao seco no mapa da bancada", false, "varredura falhou");
		return new Vec2(64, 64);
	}

	/// <summary>
	/// Um corpo de bancada. `pessoa` da CONTA e SLOT -- a assinatura que o `EhPessoa` pede: e ela que separa o
	/// jogador (selado) do NPC (devorado). `majin` da a raca e a skill que concede o verb.
	/// </summary>
	private ServerPlayer ForjarMj(string nome, Vec2 onde, double bp, bool majin = false, bool pessoa = true)
	{
		int n = _mjCorpos++;
		string raca = majin ? "Majin" : "Human";
		var novo = new ServerPlayer
		{
			Id = IdBaseDoMajinDeTeste + n,
			Peer = null,
			Name = nome,
			Race = raca,
			Genero = "Male",
			Idade = 25,
			Zone = ZonaDaBancadaDoMajin,
			Pos = onde,
			Conta = pessoa ? $"bancada_majin_{n}" : "",
			Slot = pessoa ? 0 : -1,
			Ficha = new Fighter { Race = raca, BP = bp },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.Tick(agoraMs: NowMs());   // o `expressedBP` sai do `PowerLevel`, e nao do `Statify`
		if (majin) novo.Livro.Dar(AbsorcaoMajin.Skill);
		return novo;
	}

	/// <summary>Derruba um corpo saudavel pelo nocaute de producao, por tempo de sobra.</summary>
	private static void DerrubarMj(ServerPlayer pl) => pl.Combate.Nocautear(600);

	/// <summary>Aperta o botao, depois de vencer a recarga de 2 s (a bancada nao espera o relogio).</summary>
	private void ApertarAbsorver(ServerPlayer majin)
	{
		_prontoAbsorver.Remove(majin.Id);
		UsarHabilidade(majin, "absorver");
	}

	/// <summary>Um Majin de pe e uma vitima nocauteada a um tile dele, numa praca livre.</summary>
	private (ServerPlayer Majin, ServerPlayer Vitima) CenaMj(string rotulo, double bpDoMajin = 1_000_000, double bpDaVitima = 400_000)
	{
		Vec2 praca = PracaDoMajin();
		ServerPlayer majin = ForjarMj($"bancada: o Majin ({rotulo})", praca, bpDoMajin, majin: true);
		ServerPlayer vitima = ForjarMj($"bancada: a vitima ({rotulo})", praca + new Vec2(ZoneCollision.TileSize, 0), bpDaVitima);
		DerrubarMj(vitima);
		return (majin, vitima);
	}

	private ServerPlayer? ImagemDe(ServerPlayer majin, ServerPlayer preso)
	{
		AbsorvidoPeloMajin? rec = AbsorvidosPor(majin.Id).FirstOrDefault(r => r.Quem == preso.Id);
		return rec != null && _players.TryGetValue(rec.Guardiao, out ServerPlayer? g) ? g : null;
	}

	private void LimparABancadaDoMajin()
	{
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			bool daBancada = pl.Id >= IdBaseDoMajinDeTeste && pl.Id < IdBaseDoMajinDeTeste + 1000;
			bool imagem = pl.PresoGuardado >= IdBaseDoMajinDeTeste && pl.PresoGuardado < IdBaseDoMajinDeTeste + 1000;
			if (!daBancada && !imagem) continue;
			_dentroDoMajin.Remove(pl.Id);
			_prontoAbsorver.Remove(pl.Id);
			EsquecerTecnicas(pl.Id);
			_players.Remove(pl.Id);
			ZoneList(pl.Zone.Hash).Remove(pl);
		}
		// os cadaveres e bonecos da bancada moram so na lista da zona
		foreach (ServerPlayer pl in ZoneList(ZonaDaBancadaDoMajin.Hash).ToList())
			if (pl.Name.StartsWith("bancada: ", StringComparison.Ordinal) || pl.NomeDeQuemMorreu.StartsWith("bancada: ", StringComparison.Ordinal))
				ZoneList(ZonaDaBancadaDoMajin.Hash).Remove(pl);
	}

	// =====================================================================
	// 1. O LUGAR
	// =====================================================================
	private void OLugar()
	{
		GD.Print("[majin] --- 1. o lugar: o bolsao de carne (`build_majin_pocket`) ---");
		TerrenoGerado planta = InteriorDoMajin.Planta();
		ZoneCollision col = planta.Colisao;
		int n = InteriorDoMajin.Lado;

		AfirmarMj($"a planta tem {n}x{n} (`MAJIN_POCKET_SIZE 100`)", planta.Largura == 100 && planta.Altura == 100, $"{planta.Largura}x{planta.Altura}");

		int paredesNaBorda = 0, livresNoMiolo = 0;
		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				bool borda = x == 0 || y == 0 || x == n - 1 || y == n - 1;
				if (borda && col.BlockedCell(x, y)) paredesNaBorda++;
				if (!borda && !col.BlockedCell(x, y)) livresNoMiolo++;
			}
		AfirmarMj("a borda inteira e parede e o miolo inteiro e chao", paredesNaBorda == 4 * n - 4 && livresNoMiolo == (n - 2) * (n - 2),
				  $"{paredesNaBorda} paredes na borda (de {4 * n - 4}), {livresNoMiolo} livres no miolo (de {(n - 2) * (n - 2)})");

		AfirmarMj("`destroyable = 0`: nem o chao nem a parede se quebram", col.Indestrutivel(50, 50) && col.Indestrutivel(0, 50));
		AfirmarMj("...e o unico ponto por onde uma celula cai recusa as duas (`DerrubarCelula`)",
				  !DerrubarCelula(InteriorDoMajin.De(1), 50, 50) && !DerrubarCelula(InteriorDoMajin.De(1), 0, 50));

		AfirmarMj("a parede barra QUEM VOA (`Enter()` devolve 0 pra todo mob)", col.Bloqueia(0, 50, ModoDeTravessia.PorCima) && col.Bloqueia(99, 50, ModoDeTravessia.PorCima));
		AfirmarMj("...e o chao nao barra ninguem", !col.Bloqueia(50, 50, ModoDeTravessia.PorCima) && !col.Bloqueia(50, 50, ModoDeTravessia.APe));
		try
		{
			ClasseDePredio.QuemVoaAtravessaDeTeste = true;
			AfirmarMj("(defeito injetado: parede comum, que quem voa atravessa) a mesma pergunta diz que o prisioneiro sairia voando",
					  !col.Bloqueia(0, 50, ModoDeTravessia.PorCima));
		}
		finally { ClasseDePredio.QuemVoaAtravessaDeTeste = false; }

		ZoneKey a = InteriorDoMajin.De(7), b = InteriorDoMajin.De(8);
		AfirmarMj("a zona carrega o Majin: `De(7)` e interior, e o anfitriao e o 7", InteriorDoMajin.EhOInterior(a) && InteriorDoMajin.Anfitriao(a) == 7);
		AfirmarMj("dois Majins, dois bolsoes (hashes diferentes) com a MESMA planta", a.Hash != b.Hash
				  && ReferenceEquals(MapaDaZonaOuCatalogo(a), MapaDaZonaOuCatalogo(b)) && ReferenceEquals(MapaDaZonaOuCatalogo(a), col));
		AfirmarMj("zona que nao e interior nao tem anfitriao", InteriorDoMajin.Anfitriao(ZonaDaBancadaDoMajin) == 0 && !InteriorDoMajin.EhOInterior(DimensaoMental.De(7)));

		var rng = new Random(1234);
		int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
		bool guardaNoLugar = true;
		for (int i = 0; i < 20_000; i++)
		{
			(int X, int Y) c = InteriorDoMajin.CelDoPrisioneiro(rng);
			minX = Math.Min(minX, c.X); maxX = Math.Max(maxX, c.X); minY = Math.Min(minY, c.Y); maxY = Math.Max(maxY, c.Y);
			(int X, int Y) g = InteriorDoMajin.CelDoGuardiao(c);
			guardaNoLugar &= g.Y == c.Y && g.X == Math.Min(c.X + 3, 97) && !col.BlockedCell(g.X, g.Y);
		}
		AfirmarMj("o prisioneiro cai em `rand(8, 92)` nos dois eixos (7..91 em base zero)", minX == 7 && maxX == 91 && minY == 7 && maxY == 91,
				  $"x {minX}..{maxX}, y {minY}..{maxY}");
		AfirmarMj("a imagem nasce tres celulas ao lado, na mesma linha, sem encostar na parede (`min(px+3, psz-2)`)", guardaNoLugar);
	}

	// =====================================================================
	// 2. AS CONTAS
	// =====================================================================
	private void AsContas()
	{
		GD.Print("[majin] --- 2. as contas (10% do BP, metade do poder, a vida do vencido) ---");
		AfirmarMj("o bonus e 10% do BP da vitima, com o PISO do `round()` do DM", AbsorcaoMajin.BonusDeBp(12_345.9) == 1234 && AbsorcaoMajin.BonusDeBp(400_000) == 40_000,
				  $"{AbsorcaoMajin.BonusDeBp(12_345.9)} e {AbsorcaoMajin.BonusDeBp(400_000)}");
		AfirmarMj("a imagem tem METADE do poder expresso do Majin, piso 1", AbsorcaoMajin.BpDoGuardiao(1001) == 500 && AbsorcaoMajin.BpDoGuardiao(1) == 1 && AbsorcaoMajin.BpDoGuardiao(0) == 1);
		AfirmarMj("vencida = nocauteada, morta ou com a vida em 15 (`KO || dead || HP <= 15`)",
				  AbsorcaoMajin.GuardiaoVencido(true, false, 100) && AbsorcaoMajin.GuardiaoVencido(false, true, 100)
				  && AbsorcaoMajin.GuardiaoVencido(false, false, 15) && !AbsorcaoMajin.GuardiaoVencido(false, false, 15.01));
		AfirmarMj("o meio-sangue de Majin tambem sela (`Race == \"Majin\" || Parent_Race == \"Majin\"`)",
				  AbsorcaoMajin.EhMajin("Majin", "") && AbsorcaoMajin.EhMajin("Human", "Majin") && !AbsorcaoMajin.EhMajin("Human", "Saiyan"));
		AfirmarMj("o `guard_watch` olha a cada 0,8 s", Math.Abs(AbsorcaoMajin.SegundosEntreOlhadas - 0.8) < 1e-9, $"{AbsorcaoMajin.SegundosEntreOlhadas}");
	}

	// =====================================================================
	// 3. O VERB RECUSA
	// =====================================================================
	private void AsRecusasDoVerb()
	{
		GD.Print("[majin] --- 3. o verb recusa (sem a skill, alvo de pe, caido, em recarga) ---");
		(ServerPlayer majin, ServerPlayer vitima) = CenaMj("recusas");

		string Ouvir(Action gesto)
		{
			List<string>? antes = EscutaDeAvisos;
			EscutaDeAvisos = [];
			try { gesto(); return string.Join(" | ", EscutaDeAvisos); }
			finally { EscutaDeAvisos = antes; }
		}
		bool Dentro() => DentroDeUmMajin(vitima);

		majin.Livro = new Jandirus.Core.Skills.SkillBook();   // sem a Buu Absorb
		string semSkill = Ouvir(() => ApertarAbsorver(majin));
		AfirmarMj("Majin que nao comprou a Buu Absorb ouve onde ela mora, e nao absorve", !Dentro() && semSkill.Contains("Buu Absorb"), semSkill);
		majin.Livro.Dar(AbsorcaoMajin.Skill);

		vitima.Ficha.KO = false;
		string dePe = Ouvir(() => ApertarAbsorver(majin));
		AfirmarMj("alvo DE PE nao e absorvido (`They must be knocked out and alive`)", !Dentro() && dePe.Contains("NOCAUTEADO"), dePe);
		DerrubarMj(vitima);

		vitima.Ficha.dead = true;
		string morto = Ouvir(() => ApertarAbsorver(majin));
		AfirmarMj("alvo MORTO nao e absorvido", !Dentro() && morto.Contains("NOCAUTEADO"), morto);
		vitima.Ficha.dead = false;

		DerrubarMj(majin);
		string caido = Ouvir(() => ApertarAbsorver(majin));
		AfirmarMj("Majin caido nao absorve (`!usr.KO`)", !Dentro() && caido.Length > 0, caido);
		majin.Combate.Levantar();

		majin.Selo.Preso = true;
		string selado = Ouvir(() => ApertarAbsorver(majin));
		AfirmarMj("Majin SELADO nao absorve (`usr.Planet != \"Sealed\"`)", !Dentro() && selado.Contains("selado"), selado);
		majin.Selo.Preso = false;

		_prontoAbsorver[majin.Id] = NowMs() + 1500;
		string cedo = Ouvir(() => UsarHabilidade(majin, "absorver"));
		AfirmarMj("dentro dos 2 s do `absorbing` o verb recusa", !Dentro() && cedo.Length > 0, cedo);

		ServerPlayer humano = ForjarMj("bancada: humano", majin.Pos + new Vec2(0, ZoneCollision.TileSize), 900_000);
		AfirmarMj("quem bifurca o verb e a RACA do corpo: o Majin sela, o resto segue pro ramo que ja existia", CorpoDeMajin(majin) && !CorpoDeMajin(humano));
		humano.Ficha.ParentRace = "Majin";
		AfirmarMj("...e o filho de Majin conta (`Parent_Race`)", CorpoDeMajin(humano));
	}

	// =====================================================================
	// 4. O JOGADOR E SELADO
	// =====================================================================
	private void OJogadorEhSelado()
	{
		GD.Print("[majin] --- 4. o jogador e selado: vai VIVO pro interior, e o Majin fica com 10%, os verbs e a roupa ---");
		(ServerPlayer majin, ServerPlayer vitima) = CenaMj("selar");

		// a vitima sabe uma tecnica que o Majin nao sabe, e veste uma peca
		const string skillDaVitima = "/datum/skill/ki/Afterimage";
		vitima.Livro.Dar(skillDaVitima);
		List<string> verbsDaVitima = TecnicasDe(vitima);
		string? verbNovo = verbsDaVitima.FirstOrDefault(v => !SabeTecnica(majin, v));
		AfirmarMj("(montagem) a vitima sabe pelo menos um verb que o Majin nao sabe", verbNovo != null, string.Join(",", verbsDaVitima));
		vitima.Visual.Roupa.Add(new Jandirus.Core.Appearance.PecaDeRoupa("bancada/colete"));
		int roupaDoMajinNoDisco = majin.Visual.Roupa.Count;

		// ferida de verdade, pra "chega inteiro" medir alguma coisa
		foreach (BodyPart p in vitima.Combate.Corpo.Partes) p.Vida = p.VidaMax * 0.3;
		vitima.Combate.SincronizarVida();
		vitima.Ficha.Ki = 1;

		majin.Ficha.PowerLevel();
		double exprAntes = majin.Ficha.expressedBP;
		double bonus = AbsorcaoMajin.BonusDeBp(vitima.Ficha.BP);

		ApertarAbsorver(majin);

		ZoneKey interior = InteriorDoMajin.De(majin.Id);
		AfirmarMj("a vitima esta DENTRO do Majin (a zona dela e o interior dele)", vitima.Zone.Equals(interior), vitima.Zone.ToString());
		AfirmarMj("...e saiu da lista da zona de fora e entrou na de dentro", !ZoneList(ZonaDaBancadaDoMajin.Hash).Contains(vitima) && ZoneList(interior.Hash).Contains(vitima));
		int cx = (int)MathF.Floor(vitima.Pos.X / 32), cy = (int)MathF.Floor(vitima.Pos.Y / 32);
		AfirmarMj("...num ponto do sorteio (7..91)", cx is >= 7 and <= 91 && cy is >= 7 and <= 91, $"celula ({cx},{cy})");
		AfirmarMj("ela chega ACORDADA (o nocaute foi desfeito)", !vitima.Ficha.KO && vitima.Combate.NocauteRestante == 0);
		AfirmarMj("...INTEIRA (todo membro no maximo)", vitima.Combate.Corpo.Partes.All(p => p.Decepado || p.Vida >= p.VidaMax - 1e-6), $"vida {vitima.Combate.Corpo.Vida():0.#}");
		AfirmarMj("...com o Ki cheio", vitima.Ficha.Ki >= vitima.Ficha.MaxKi - 1e-6, $"{vitima.Ficha.Ki:0.#} de {vitima.Ficha.MaxKi:0.#}");
		AfirmarMj("...e VIVA", !vitima.Ficha.dead);

		AfirmarMj($"o Majin ganhou 10% do BP dela (`majin_absorb_bp` = {bonus:0})", Math.Abs(majin.Ficha.majin_absorb_bp - bonus) < 0.5, $"{majin.Ficha.majin_absorb_bp}");
		double esperado = exprAntes * (majin.Ficha.BP + bonus) / majin.Ficha.BP;
		AfirmarMj("...e ele ENTRA NO PODER EXPRESSO, somado na base", Math.Abs(majin.Ficha.expressedBP - esperado) / esperado < 0.01,
				  $"expresso {exprAntes:0} -> {majin.Ficha.expressedBP:0} (esperado ~{esperado:0})");

		AfirmarMj($"o Majin passou a saber o verb da vitima (`{verbNovo}`)", verbNovo != null && SabeTecnica(majin, verbNovo) && TecnicasDe(majin).Contains(verbNovo));
		AbsorvidoPeloMajin? rec = AbsorvidosPor(majin.Id).FirstOrDefault();
		AfirmarMj("...e o registro guarda SO os verbs que ele nao tinha", rec != null && verbNovo != null && rec.Verbos.Contains(verbNovo) && !rec.Verbos.Contains(AbsorcaoMajin.Verbo),
				  rec == null ? "sem registro" : string.Join(",", rec.Verbos));

		AfirmarMj("o Majin VESTE a roupa da vitima (o que o mundo ve)", VisualVisivel(majin).Roupa.Any(p => p.Caminho == "bancada/colete"));
		AfirmarMj("...e a aparencia dele que vai pro disco nao foi tocada", majin.Visual.Roupa.Count == roupaDoMajinNoDisco);

		ServerPlayer? g = ImagemDe(majin, vitima);
		AfirmarMj("ha uma IMAGEM do Majin la dentro, na zona do interior", g != null && g.Zone.Equals(interior));
		if (g == null) return;
		int gx = (int)MathF.Floor(g.Pos.X / 32), gy = (int)MathF.Floor(g.Pos.Y / 32);
		AfirmarMj("...tres celulas ao lado do prisioneiro", gy == cy && gx == Math.Min(cx + 3, 97), $"imagem ({gx},{gy}), preso ({cx},{cy})");
		AfirmarMj("...com o nome dele", g.Name.Contains(majin.Name), g.Name);
		AfirmarMj("...com o corpo dele e SEM roupa nenhuma (la so o icone, o cabelo e os olhos sao copiados)", g.Race == majin.Race
				  && g.Visual.Corpo == majin.Visual.Corpo && g.Visual.Cabelo == majin.Visual.Cabelo && g.Visual.Roupa.Count == 0,
				  $"{g.Visual.Roupa.Count} peca(s)");
		AfirmarMj($"...com METADE do poder expresso do Majin de antes ({AbsorcaoMajin.BpDoGuardiao(exprAntes):0})", Math.Abs(g.Ficha.BP - AbsorcaoMajin.BpDoGuardiao(exprAntes)) < 0.5, $"{g.Ficha.BP}");
		AfirmarMj("...com os sete stats de combate dele", g.Ficha.physoff == majin.Ficha.physoff && g.Ficha.physdef == majin.Ficha.physdef && g.Ficha.technique == majin.Ficha.technique
				  && g.Ficha.kioff == majin.Ficha.kioff && g.Ficha.kidef == majin.Ficha.kidef && g.Ficha.kiskill == majin.Ficha.kiskill && g.Ficha.speed == majin.Ficha.speed);
		AfirmarMj("...que guarda ESTE prisioneiro, nao mata e nao regenera como um Majin", g.PresoGuardado == vitima.Id && !g.Combate.Letal
				  && !g.Combate.Corpo.Regen.CuraEmCombate && !g.Combate.Corpo.RegeneraDecepado);
		AfirmarMj("...sem papel de NPC do mundo e sem conta (ela nao conta como gente nem como habitante)", !EhJogador(g) && !EhNpcDoMundo(g) && !EhPessoa(g));
		AfirmarMj("...e que sabe voar atras de quem voa (a maestria de Ki do Majin, com piso no voo)", PodeVoar(g));
	}

	// =====================================================================
	// 5. A IMAGEM LUTA
	// =====================================================================
	private void AImagemLuta()
	{
		GD.Print("[majin] --- 5. a imagem vai pra cima do prisioneiro (o `foundTarget`), pelo tique de producao ---");
		(ServerPlayer majin, ServerPlayer vitima) = CenaMj("luta");
		ApertarAbsorver(majin);
		ServerPlayer? g = ImagemDe(majin, vitima);
		if (g == null) { AfirmarMj("(montagem) a imagem nasceu", false); return; }

		float antes = Vec2.Distance(g.Pos, vitima.Pos);
		double vidaAntes = vitima.Combate.Corpo.Vida();
		bool agrediu = false;
		for (int i = 0; i < 30 * 12 && !agrediu; i++)
		{
			TickDosCorposSemDono(Protocol.TickSeconds);
			agrediu = vitima.Combate.Corpo.Vida() < vidaAntes - 1e-9 || vitima.Combate.EmCombate > 0;
		}
		AfirmarMj("a imagem fechou a distancia e partiu pra cima do prisioneiro", agrediu && Vec2.Distance(g.Pos, vitima.Pos) < antes,
				  $"distancia {antes:0} -> {Vec2.Distance(g.Pos, vitima.Pos):0} px; vida do preso {vidaAntes:0.##} -> {vitima.Combate.Corpo.Vida():0.##}; em combate {vitima.Combate.EmCombate:0.#}");

		// o pino: lutar rende BP a todo corpo, e a imagem nao pode crescer
		g.Ficha.BP *= 3;
		TickDosCorposSemDono(Protocol.TickSeconds);
		AfirmarMj("o BP da imagem e PINADO (o `NPCTicker` dela)", Math.Abs(g.Ficha.BP - g.BpDoGuardiao) < 0.5, $"{g.Ficha.BP} contra {g.BpDoGuardiao}");

		// some sozinha se o prisioneiro sair por uma porta que nao a soltou
		ZoneList(vitima.Zone.Hash).Remove(vitima);
		vitima.Zone = ZonaDaBancadaDoMajin;
		ZoneList(vitima.Zone.Hash).Add(vitima);
		int idDaImagem = g.Id;
		TickDosCorposSemDono(Protocol.TickSeconds);
		AfirmarMj("prisioneiro levado embora por outra porta: a imagem se desfaz", !_players.ContainsKey(idDaImagem));
		TickDoMajin(Protocol.TickSeconds);
		AfirmarMj("...e o Majin perde o que tinha por causa dele", AbsorvidosPor(majin.Id).Count == 0 && majin.Ficha.majin_absorb_bp == 0, $"{majin.Ficha.majin_absorb_bp}");
	}

	// =====================================================================
	// 6. VENCER A IMAGEM
	// =====================================================================
	private void VencerAImagemSolta()
	{
		GD.Print("[majin] --- 6. saida 1: VENCER A IMAGEM (`guard_watch`) ---");

		// (a) nocauteada
		(ServerPlayer majin, ServerPlayer vitima) = CenaMj("vencer");
		const string skillDaVitima = "/datum/skill/ki/Afterimage";
		vitima.Livro.Dar(skillDaVitima);
		string? verbNovo = TecnicasDe(vitima).FirstOrDefault(v => !SabeTecnica(majin, v));
		vitima.Visual.Roupa.Add(new Jandirus.Core.Appearance.PecaDeRoupa("bancada/faixa"));
		ApertarAbsorver(majin);
		ServerPlayer? g = ImagemDe(majin, vitima);
		if (g == null) { AfirmarMj("(montagem) a imagem nasceu", false); return; }
		int idDaImagem = g.Id;

		TickDoMajin(AbsorcaoMajin.SegundosEntreOlhadas);
		AfirmarMj("com a imagem de pe, ninguem sai", DentroDeUmMajin(vitima));

		DerrubarMj(g);
		try
		{
			AbsorcaoMajin.GuardiaoVencidoNaoSoltaDeTeste = true;
			TickDoMajin(AbsorcaoMajin.SegundosEntreOlhadas);
			AfirmarMj("(defeito injetado: vencer a imagem nao solta) a imagem some e o prisioneiro CONTINUA la dentro",
					  DentroDeUmMajin(vitima) && !_players.ContainsKey(idDaImagem));
		}
		finally { AbsorcaoMajin.GuardiaoVencidoNaoSoltaDeTeste = false; }

		List<string>? antes = EscutaDeAvisos;
		EscutaDeAvisos = [];
		string avisos;
		try { TickDoMajin(AbsorcaoMajin.SegundosEntreOlhadas); avisos = string.Join(" | ", EscutaDeAvisos); }
		finally { EscutaDeAvisos = antes; }

		AfirmarMj("imagem vencida: o prisioneiro SAI", !DentroDeUmMajin(vitima) && vitima.Zone.Equals(majin.Zone), vitima.Zone.ToString());
		AfirmarMj("...ao lado do Majin", Vec2.Distance(vitima.Pos, majin.Pos) <= 3 * ZoneCollision.TileSize, $"{Vec2.Distance(vitima.Pos, majin.Pos):0} px");
		AfirmarMj("...e sai de PE (so sai nocauteado quando o proprio Majin caiu)", !vitima.Ficha.KO);
		AfirmarMj("o Majin perde os 10%", majin.Ficha.majin_absorb_bp == 0, $"{majin.Ficha.majin_absorb_bp}");
		AfirmarMj("...e os verbs emprestados", verbNovo != null && !SabeTecnica(majin, verbNovo) && majin.VerbosAbsorvidos == null);
		AfirmarMj("...e fica sabendo (`A prisoner has overpowered your image and torn free!`)", avisos.Contains("venceu a sua imagem"), avisos);
		AfirmarMj("a roupa da ultima refeicao FICA ate a proxima (o guarda-roupa do DM: so a absorcao seguinte a troca)",
				  VisualVisivel(majin).Roupa.Any(p => p.Caminho == "bancada/faixa"));
		AfirmarMj("nao sobrou registro nem imagem", AbsorvidosPor(majin.Id).Count == 0 && !_players.ContainsKey(idDaImagem));

		// (b) de pe, com a vida em 15
		(ServerPlayer majin2, ServerPlayer vitima2) = CenaMj("vencer por vida");
		ApertarAbsorver(majin2);
		ServerPlayer? g2 = ImagemDe(majin2, vitima2);
		if (g2 == null) { AfirmarMj("(montagem) a segunda imagem nasceu", false); return; }
		g2.Ficha.HP = 15.5;
		TickDoMajin(AbsorcaoMajin.SegundosEntreOlhadas);
		AfirmarMj("imagem de pe com 15,5 de vida: ainda nao", DentroDeUmMajin(vitima2));
		g2.Ficha.HP = 15;
		TickDoMajin(AbsorcaoMajin.SegundosEntreOlhadas);
		AfirmarMj("imagem de pe com 15 de vida: vencida (`HP <= 15`)", !DentroDeUmMajin(vitima2));

		// (c) a olhada tem cadencia
		(ServerPlayer majin3, ServerPlayer vitima3) = CenaMj("cadencia");
		ApertarAbsorver(majin3);
		if (ImagemDe(majin3, vitima3) is not { } g3) { AfirmarMj("(montagem) a terceira imagem nasceu", false); return; }
		_relogioDosGuardioes = 0;
		DerrubarMj(g3);
		TickDoMajin(0.3);
		AfirmarMj("a olhada e a cada 0,8 s: 0,3 s depois da queda ela ainda nao viu", DentroDeUmMajin(vitima3));
		TickDoMajin(0.6);
		AfirmarMj("...e ao fechar os 0,8 s, viu", !DentroDeUmMajin(vitima3));
	}

	// =====================================================================
	// 7. MORRER LA DENTRO
	// =====================================================================
	private void MorrerLaDentroSolta()
	{
		GD.Print("[majin] --- 7. saida 2: MORRER LA DENTRO (`Death.dm:9`) ---");
		(ServerPlayer majin, ServerPlayer vitima) = CenaMj("morrer");
		ApertarAbsorver(majin);
		int idDaImagem = ImagemDe(majin, vitima)?.Id ?? 0;

		vitima.Combate.Morrer(ignorarSeguro: true);
		TickDoMajin(Protocol.TickSeconds);
		AfirmarMj("quem morre la dentro e solto no mesmo tique, sem esperar a olhada", !DentroDeUmMajin(vitima) && vitima.Zone.Equals(majin.Zone));
		AfirmarMj("...continua morto (a morte segue o caminho dela do lado de fora)", vitima.Ficha.dead);
		AfirmarMj("...o Majin perde os 10% e a imagem some", majin.Ficha.majin_absorb_bp == 0 && !_players.ContainsKey(idDaImagem));
	}

	// =====================================================================
	// 8. O MAJIN CAI
	// =====================================================================
	private void OMajinCaidoSoltaTodos()
	{
		GD.Print("[majin] --- 8. saida 3: O MAJIN CAI (`KO()` -> `majin_escape_all`) ---");
		(ServerPlayer majin, ServerPlayer a) = CenaMj("cair");
		ServerPlayer b = ForjarMj("bancada: a segunda vitima", majin.Pos + new Vec2(0, ZoneCollision.TileSize), 250_000);
		ServerPlayer npc = ForjarMj("bancada: o NPC devorado", majin.Pos + new Vec2(-ZoneCollision.TileSize, 0), 100_000, pessoa: false);
		DerrubarMj(b);
		DerrubarMj(npc);

		ApertarAbsorver(majin);
		ApertarAbsorver(majin);
		ApertarAbsorver(majin);
		double soma = AbsorcaoMajin.BonusDeBp(400_000) + AbsorcaoMajin.BonusDeBp(250_000) + AbsorcaoMajin.BonusDeBp(100_000);
		AfirmarMj("tres absorcoes: dois presos la dentro e um NPC devorado", DentroDeUmMajin(a) && DentroDeUmMajin(b) && npc.Ficha.dead && AbsorvidosPor(majin.Id).Count == 3,
				  $"a dentro={DentroDeUmMajin(a)}, b dentro={DentroDeUmMajin(b)}, npc morto={npc.Ficha.dead}, registros={AbsorvidosPor(majin.Id).Count}");
		AfirmarMj($"...e os tres bonus somados ({soma:0})", Math.Abs(majin.Ficha.majin_absorb_bp - soma) < 0.5, $"{majin.Ficha.majin_absorb_bp}");
		AfirmarMj("...cada preso com a SUA imagem, e o devorado sem nenhuma", ImagemDe(majin, a) != null && ImagemDe(majin, b) != null
				  && ImagemDe(majin, a) != ImagemDe(majin, b) && AbsorvidosPor(majin.Id).Count(r => r.Guardiao != 0) == 2);
		AfirmarMj("...no MESMO bolsao (um interior por Majin)", a.Zone.Equals(b.Zone));

		DerrubarMj(majin);
		try
		{
			AbsorcaoMajin.NocauteNaoSoltaDeTeste = true;
			TickDoMajin(Protocol.TickSeconds);
			AfirmarMj("(defeito injetado: o nocaute do Majin nao solta) os dois continuam presos num Majin caido", DentroDeUmMajin(a) && DentroDeUmMajin(b));
		}
		finally { AbsorcaoMajin.NocauteNaoSoltaDeTeste = false; }

		TickDoMajin(Protocol.TickSeconds);
		AfirmarMj("Majin nocauteado: TODOS saem, no mesmo tique", !DentroDeUmMajin(a) && !DentroDeUmMajin(b) && a.Zone.Equals(majin.Zone) && b.Zone.Equals(majin.Zone));
		AfirmarMj("...e saem NOCAUTEADOS (`if(KO) ... M.KO()`), pelo prazo do nocaute sem relogio do DM", a.Ficha.KO && b.Ficha.KO
				  && Math.Abs(a.Combate.NocauteRestante - MeleeResolver.TetoDoNocaute) < 1, $"KO a={a.Ficha.KO} b={b.Ficha.KO}, resta {a.Combate.NocauteRestante:0}s");
		AfirmarMj("o Majin perde TUDO, o bonus do devorado inclusive", majin.Ficha.majin_absorb_bp == 0 && AbsorvidosPor(majin.Id).Count == 0, $"{majin.Ficha.majin_absorb_bp}");
		AfirmarMj("nenhuma imagem sobrou no mundo", !_players.Values.Any(p => p.PresoGuardado == a.Id || p.PresoGuardado == b.Id));
	}

	// =====================================================================
	// 9. EXPELIR
	// =====================================================================
	private void ExpelirSolta()
	{
		GD.Print("[majin] --- 9. saida 4: EXPELIR (um, e todos) ---");
		(ServerPlayer majin, ServerPlayer a) = CenaMj("expelir");
		ServerPlayer b = ForjarMj("bancada: o outro expelido", majin.Pos + new Vec2(0, ZoneCollision.TileSize), 250_000);
		DerrubarMj(b);
		ApertarAbsorver(majin);
		ApertarAbsorver(majin);

		AbsorvidoPeloMajin? recDoB = AbsorvidosPor(majin.Id).FirstOrDefault(r => r.Quem == b.Id);
		AfirmarMj("(montagem) os dois estao la dentro", DentroDeUmMajin(a) && DentroDeUmMajin(b) && recDoB != null);
		if (recDoB == null) return;

		UsarHabilidade(majin, $"expelir:{recDoB.Numero}");
		AfirmarMj("expelir UM: so ele sai", !DentroDeUmMajin(b) && DentroDeUmMajin(a));
		AfirmarMj("...de pe, ao lado do Majin", !b.Ficha.KO && b.Zone.Equals(majin.Zone) && Vec2.Distance(b.Pos, majin.Pos) <= 3 * ZoneCollision.TileSize);
		AfirmarMj("...e o Majin fica so com o bonus de quem ficou", Math.Abs(majin.Ficha.majin_absorb_bp - AbsorcaoMajin.BonusDeBp(400_000)) < 0.5, $"{majin.Ficha.majin_absorb_bp}");

		UsarHabilidade(majin, $"expelir:{recDoB.Numero}");
		AfirmarMj("expelir de novo quem ja saiu nao faz nada com quem ficou", DentroDeUmMajin(a));

		UsarHabilidade(majin, "expelir");
		AfirmarMj("expelir TODOS: o que restava sai, de pe", !DentroDeUmMajin(a) && !a.Ficha.KO && majin.Ficha.majin_absorb_bp == 0);
	}

	// =====================================================================
	// 10. SAIR DO JOGO, E VOLTAR
	// =====================================================================
	private void SairDoJogoDesfaz()
	{
		GD.Print("[majin] --- 10. saida 5: SAIR DO JOGO (`DoLogoutStuff`) e o login de quem ficou pra tras ---");

		(ServerPlayer majin, ServerPlayer vitima) = CenaMj("o preso sai do jogo");
		ApertarAbsorver(majin);
		DesfazerAAbsorcaoAoSair(vitima);
		AfirmarMj("o PRESO que sai do jogo e solto antes do save: a zona dele ja nao e o bolsao", !DentroDeUmMajin(vitima) && vitima.Zone.Equals(majin.Zone));
		AfirmarMj("...e o Majin perde o que tinha por causa dele", majin.Ficha.majin_absorb_bp == 0 && AbsorvidosPor(majin.Id).Count == 0);

		(ServerPlayer majin2, ServerPlayer vitima2) = CenaMj("o Majin sai do jogo");
		ApertarAbsorver(majin2);
		DesfazerAAbsorcaoAoSair(majin2);
		AfirmarMj("o MAJIN que sai do jogo cospe todo mundo, de pe", !DentroDeUmMajin(vitima2) && !vitima2.Ficha.KO);
		AfirmarMj("...e o poder emprestado nao vai pro disco", majin2.Ficha.majin_absorb_bp == 0);

		// o servidor caiu com gente dentro: o save diz "interior de um Majin" e "bonus 999"
		ServerPlayer orfao = ForjarMj("bancada: ficou pra tras", PracaDoMajin(), 300_000);
		ZoneList(orfao.Zone.Hash).Remove(orfao);
		orfao.Zone = InteriorDoMajin.De(424242);
		orfao.Ficha.majin_absorb_bp = 999;
		AcordarSemAbsorcao(orfao);
		AfirmarMj("login de quem ficou dentro de um Majin que nao existe mais: acorda em terra firme", !DentroDeUmMajin(orfao), orfao.Zone.ToString());
		AfirmarMj("...e sem poder emprestado", orfao.Ficha.majin_absorb_bp == 0);
		ZoneList(orfao.Zone.Hash).Add(orfao);
	}

	// =====================================================================
	// 11. O NPC E DEVORADO
	// =====================================================================
	private void ONpcEhDevorado()
	{
		GD.Print("[majin] --- 11. o NPC e devorado por inteiro: sem bolsao, sem imagem, 10% ate o Majin cair ---");
		Vec2 praca = PracaDoMajin();
		ServerPlayer majin = ForjarMj("bancada: o Majin (devorar)", praca, 1_000_000, majin: true);
		ServerPlayer npc = ForjarMj("bancada: o NPC", praca + new Vec2(ZoneCollision.TileSize, 0), 80_000, pessoa: false);
		npc.Visual.Roupa.Add(new Jandirus.Core.Appearance.PecaDeRoupa("bancada/avental"));
		DerrubarMj(npc);
		foreach (BodyPart p in majin.Combate.Corpo.Partes) p.Vida = p.VidaMax * 0.5;
		majin.Combate.SincronizarVida();
		majin.Ficha.Ki = 1;
		int corposAntes = _players.Count;

		ApertarAbsorver(majin);

		AfirmarMj("o NPC morre (`mobDeath`) e NAO vai pra interior nenhum", npc.Ficha.dead && !DentroDeUmMajin(npc));
		AfirmarMj("nenhuma imagem nasce pra ele", _players.Count == corposAntes && AbsorvidosPor(majin.Id).All(r => r.Guardiao == 0));
		AfirmarMj("o Majin fica com 10% do BP dele", Math.Abs(majin.Ficha.majin_absorb_bp - 8_000) < 0.5, $"{majin.Ficha.majin_absorb_bp}");
		AfirmarMj("...veste a roupa do devorado", VisualVisivel(majin).Roupa.Any(p => p.Caminho == "bancada/avental"));
		AfirmarMj("...se cura (`SpreadHeal(100,1,0)`: os vitais feridos primeiro) e enche o Ki", majin.Ficha.Ki >= majin.Ficha.MaxKi - 1e-6
				  && majin.Combate.Corpo.Partes.Where(p => p.Papel != Vitalidade.Membro).All(p => p.Vida >= p.VidaMax - 1e-6),
				  $"Ki {majin.Ficha.Ki:0.#}/{majin.Ficha.MaxKi:0.#}, vida {majin.Combate.Corpo.Vida():0.#}");

		TickDoMajin(AbsorcaoMajin.SegundosEntreOlhadas);
		AfirmarMj("o bonus do devorado NAO sai sozinho (nao ha imagem pra vencer)", Math.Abs(majin.Ficha.majin_absorb_bp - 8_000) < 0.5);

		// a proxima refeicao troca a roupa
		ServerPlayer outro = ForjarMj("bancada: a refeicao seguinte", praca + new Vec2(0, ZoneCollision.TileSize), 50_000, pessoa: false);
		outro.Visual.Roupa.Add(new Jandirus.Core.Appearance.PecaDeRoupa("bancada/cachecol"));
		DerrubarMj(outro);
		ApertarAbsorver(majin);
		List<Jandirus.Core.Appearance.PecaDeRoupa> veste = VisualVisivel(majin).Roupa;
		AfirmarMj("a refeicao seguinte TROCA a roupa (so a da ultima fica)", veste.Any(p => p.Caminho == "bancada/cachecol") && !veste.Any(p => p.Caminho == "bancada/avental"),
				  string.Join(",", veste.Select(p => p.Caminho)));

		UsarHabilidade(majin, "expelir");
		AfirmarMj("expelir abre mao do poder dos devorados (nao ha quem devolver)", majin.Ficha.majin_absorb_bp == 0 && AbsorvidosPor(majin.Id).Count == 0);
	}

	// =====================================================================
	// 12. QUEM VIU
	// =====================================================================
	private void QuemViuSeEnfurece()
	{
		GD.Print("[majin] --- 12. quem viu o amigo ser absorvido reage como se o tivesse visto morrer ---");
		(ServerPlayer majin, ServerPlayer vitima) = CenaMj("luto");
		ServerPlayer amigo = ForjarMj("bancada: o amigo", vitima.Pos + new Vec2(3 * ZoneCollision.TileSize, 0), 200_000);
		ServerPlayer estranho = ForjarMj("bancada: o estranho", vitima.Pos + new Vec2(0, 3 * ZoneCollision.TileSize), 200_000);
		amigo.Social.Amizade[vitima.Assinatura] = Jandirus.Core.Social.Convivio.ExigenciaDeAmigo;
		// O MAJIN TAMBEM E AMIGO DA VITIMA, e mesmo assim nao se enfurece com o proprio gesto (`A == src`)
		majin.Social.Amizade[vitima.Assinatura] = Jandirus.Core.Social.Convivio.ExigenciaDeAmigo;

		long agora = NowMs();
		ApertarAbsorver(majin);
		AfirmarMj("o AMIGO que viu entra em furia extrema (`Do_Anger_Stuff(1)`)", amigo.FuriaExtremaAte > agora);
		AfirmarMj("...o estranho, nao", estranho.FuriaExtremaAte <= agora);
		AfirmarMj("...e o Majin tambem nao (quem absorve nao se enfurece com o proprio gesto)", majin.FuriaExtremaAte <= agora);
		AfirmarMj("a lista de afeto e a da absorcao: amigo ou `Good`/`Very Good` (sem `Love`, sem `Rival/Good`)",
				  amigo.Social.LutoPorAbsorcao(vitima.Assinatura) && !estranho.Social.LutoPorAbsorcao(vitima.Assinatura));
	}

	// =====================================================================
	// 13. NAO HA ATALHO PRA FORA
	// =====================================================================
	private void NaoHaAtalhoPraFora()
	{
		GD.Print("[majin] --- 13. de dentro nao se sai por atalho, e o Majin dentro de outro cospe na Terra ---");
		(ServerPlayer majin, ServerPlayer vitima) = CenaMj("atalho");
		ApertarAbsorver(majin);

		AfirmarMj("o prisioneiro nao salta de planeta la de dentro", !PodeSaltarDePlaneta(vitima, out string porque) && porque.Contains("imagem"), porque);
		AfirmarMj("...e do lado de fora o mesmo corpo poderia", PodeSaltarDePlaneta(majin, out _));

		// voando alto, o passo contra a parede e recusado pela mesma pergunta que o servidor faz
		ZoneCollision col = MapaDaZonaOuCatalogo(vitima.Zone)!;
		AfirmarMj("a zona do prisioneiro resolve pra planta do bolsao no funil da colisao", ReferenceEquals(col, InteriorDoMajin.Planta().Colisao));
		AfirmarMj("voando acima do cenario, a parede do bolsao continua parede", col.Bloqueia(0, 10, Voo.ModoNaAltura(Voo.AlturaMaxima, ModoDeTravessia.Voando)));

		// o Majin que esta dentro de OUTRO Majin cospe no meio da Terra (`majin_safe_release_turf`)
		ZoneList(majin.Zone.Hash).Remove(majin);
		majin.Zone = InteriorDoMajin.De(777);
		ZoneList(majin.Zone.Hash).Add(majin);
		(ZoneKey zona, Vec2 pos) = OndeOMajinCospe(majin);
		int tx = (int)MathF.Floor(pos.X / 32), ty = (int)MathF.Floor(pos.Y / 32);
		AfirmarMj("Majin dentro de um bolsao: quem ele solta cai no meio da Terra, nunca num bolsao", zona.Equals(SpawnZone) && !InteriorDoMajin.EhOInterior(zona)
				  && tx is >= 230 and <= 270 && ty is >= 230 and <= 270, $"{zona} celula ({tx},{ty})");
		UsarHabilidade(majin, "expelir");
		AfirmarMj("...e e pra la que o prisioneiro dele vai", vitima.Zone.Equals(SpawnZone), vitima.Zone.ToString());
	}

	// =====================================================================
	// 14. SOBRE A AGUA
	// =====================================================================
	private void SobreAAguaCaiNaMargem()
	{
		GD.Print("[majin] --- 14. Majin sobre a AGUA: quem sai dele cai no chao seco mais proximo, e nao no mar ---");
		if (_mjMapa is not { } mapa) return;

		// um trecho de mar aberto: 5x5 de agua sem parede
		(int X, int Y)? mar = null;
		for (int y = 60; y < 240 && mar == null; y++)
			for (int x = 8; x < 240 && mar == null; x++)
			{
				bool soAgua = true;
				for (int dy = -2; dy <= 2 && soAgua; dy++)
					for (int dx = -2; dx <= 2 && soAgua; dx++)
						soAgua &= mapa.EhAgua(x + dx, y + dy) && !mapa.BlockedCell(x + dx, y + dy);
				if (soAgua) mar = (x, y);
			}
		AfirmarMj("(montagem) ha um trecho de mar aberto no mapa da bancada", mar != null);
		if (mar == null) return;

		Vec2 noMar = mapa.CentroDaCelula(mar.Value.X, mar.Value.Y);
		ServerPlayer majin = ForjarMj("bancada: o Majin (no mar)", noMar, 1_000_000, majin: true);
		ServerPlayer vitima = ForjarMj("bancada: a vitima (no mar)", noMar + new Vec2(ZoneCollision.TileSize, 0), 400_000);
		DerrubarMj(vitima);
		ApertarAbsorver(majin);
		AfirmarMj("(montagem) a vitima foi absorvida com os dois sobre a agua", DentroDeUmMajin(vitima));

		Vec2 aFrente = majin.Pos + MeleeArea.Frente(majin.Facing) * ZoneCollision.TileSize;
		Vec2 margem = mapa.PontoLivrePerto(aFrente);
		UsarHabilidade(majin, "expelir");

		int cx = (int)MathF.Floor(vitima.Pos.X / ZoneCollision.TileSize), cy = (int)MathF.Floor(vitima.Pos.Y / ZoneCollision.TileSize);
		AfirmarMj("a frente do Majin e agua, e quem ele solta NAO cai no mar: cai em chao que serve a um corpo a pe",
				  !DentroDeUmMajin(vitima) && mapa.EhAguaEm(aFrente) && mapa.ServeDeChao(cx, cy) && !mapa.EhAgua(cx, cy), $"celula ({cx},{cy})");
		AfirmarMj("...o mais proximo da frente dele (a rede de todo pouso do port, `PontoLivrePerto`)", Vec2.Distance(vitima.Pos, margem) < 0.5f,
				  $"caiu a {Vec2.Distance(vitima.Pos, margem):0} px do ponto esperado, a {Vec2.Distance(vitima.Pos, majin.Pos) / ZoneCollision.TileSize:0.#} tiles do Majin");
	}
}
