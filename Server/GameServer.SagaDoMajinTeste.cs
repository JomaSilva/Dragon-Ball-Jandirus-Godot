using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA SAGA DO CORRUPTED MAJIN (`--sagamajinteste`, 2026-10-16).
///
/// O dono: *"teste a saga majin e veja se ta funcionando bem (no dm tem tudo certinho la, cada sprite, cada trigger tudo)"* -- e o
/// sistema do amigo dele por cima da Forma Pura. Tudo passa pela porta de producao: o verb de absorver entra pelo `UsarHabilidade`,
/// o luto pelo `AoPerderALuta`, a ativa pelo `UsarTecnica`, os relogios pelos tiques de verdade (`TickDaForma`, `TickDaCarga`,
/// `TickDoMajin`, `TickDaLoucuraDoMajin`). A bancada so monta a cena (corpos forjados sem `Peer`) e le o resultado -- e cada regra
/// que pode sumir calada tem o defeito injetado ao lado, reprovando.
///
/// O QUE ELA NAO VE e a tela (a cena tocando, a passagem, o corpo novo, a vinheta da loucura): isso e da `--diagsagamajin`
/// (`Client/RoboDaSagaMajin.cs`), de janela.
///
/// COMO RODAR: `Godot --headless --path . --server --port 7989 --sagamajinteste`
/// </summary>
public partial class GameServer
{
	private const int IdBaseDaSagaDeTeste = 94_700;

	private int _sgOk, _sgFalhou, _sgCorpos;
	private ZoneCollision? _sgMapa;
	private int _sgProximaPraca = 60;

	private static readonly ZoneKey ZonaDaSagaDeTeste = new(ZoneKey.KindPremade, "Earth");

	private void AfirmarSg(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _sgOk++; GD.Print($"[sagamajin]   OK    {oque}"); return; }
		_sgFalhou++;
		GD.PrintErr($"[sagamajin]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaSagaDoMajin()
	{
		_sgOk = _sgFalhou = 0;
		GD.Print("[sagamajin] ================ A SAGA DO CORRUPTED MAJIN ================");
		_sgMapa = MapaDaZonaOuCatalogo(ZonaDaSagaDeTeste);
		AfirmarSg("a zona da bancada tem colisao carregada", _sgMapa != null);

		EscutaDeCenasDoMajin = [];
		EscutaDaLoucura = [];
		EscutaDeAnuncios = [];
		EscutaDeFalas = [];
		try
		{
			OsNumerosDaSaga();
			AFolhaTemAPassagem();
			AFormaUmPeloKai();
			AFormaDoisPelaMorte();
			AFormaTresPelaOutraMetade();
			AFormaQuatroPelosTresJogadores();
			AFormaPuraEORepouso();
			ASanidade();
			ALoucura();
			OSono();
			ASycophanticRage();
			OMindlessGambit();
			OEchoedInsanity();
			AsClassesRarasSemDisciplina();
			SairEVoltar();
		}
		catch (Exception e)
		{
			AfirmarSg($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			SagaSemOutraMetadeDeTeste = OutraMetadeSemVencerDeTeste = PuraSemPisoDeTeste = LoucuraComRegenDeTeste = false;
			ExpelirCustaSanidadeDeTeste = LoucuraSemIaDeTeste = MenteInteiraNaLoucuraDeTeste = FuriaSemTiroDeTeste = false;
			EcoSemFatorDeTeste = SagaNoDicionarioDeTeste = false;
			Body.GambitoDesligadoDeTeste = false;
			EscutaDeCenasDoMajin = null;
			EscutaDaLoucura = null;
			EscutaDeAnuncios = null;
			EscutaDeFalas = null;
			LimparABancadaDaSaga();
		}

		GD.Print($"[sagamajin] ================ {_sgOk} passaram, {_sgFalhou} falharam ================");
	}

	// =====================================================================
	// OS CORPOS E A CENA
	// =====================================================================
	/// <summary>Uma praca 5x5 de chao seco na Terra, cada chamada numa faixa nova -- a mesma varredura da `--majinteste`.</summary>
	private Vec2 PracaDaSaga()
	{
		const int T = ZoneCollision.TileSize;
		if (_sgMapa is { } mapa)
			for (int y = _sgProximaPraca; y < 240; y++)
				for (int x = 8; x < 240; x++)
				{
					bool livre = true;
					for (int dy = -2; dy <= 2 && livre; dy++)
						for (int dx = -2; dx <= 2 && livre; dx++)
							livre &= mapa.ServeDeChao(x + dx, y + dy);
					if (!livre) continue;
					_sgProximaPraca = y + 6;
					return new Vec2(x * T + T / 2f, y * T + T / 2f);
				}
		AfirmarSg("achei uma praca 5x5 de chao seco no mapa da bancada", false, "varredura falhou");
		return new Vec2(64, 64);
	}

	/// <summary>Um corpo de bancada. `pessoa` da conta e slot (a assinatura que separa o jogador selado do NPC devorado).</summary>
	private ServerPlayer ForjarSg(string nome, Vec2 onde, double bp, string raca = "Human", string classe = "Normal",
								  bool pessoa = true)
	{
		int n = _sgCorpos++;
		var novo = new ServerPlayer
		{
			Id = IdBaseDaSagaDeTeste + n,
			Peer = null,
			Name = nome,
			Race = raca,
			Class = classe,
			Genero = "Male",
			Idade = 25,
			Zone = ZonaDaSagaDeTeste,
			Pos = onde,
			Conta = pessoa ? $"bancada_saga_{n}" : "",
			Slot = pessoa ? 0 : -1,
			Ficha = new Fighter { Race = raca, BP = bp },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Class = classe;
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.Tick(agoraMs: NowMs());
		if (AbsorcaoMajin.EhMajin(raca, null)) novo.Livro.Dar(AbsorcaoMajin.Skill);
		return novo;
	}

	/// <summary>Um Corrupted Majin de pe numa praca nova, e a praca.</summary>
	private (ServerPlayer Majin, Vec2 Praca) MajinDaSaga(string rotulo, double bp = 1_000_000)
	{
		Vec2 praca = PracaDaSaga();
		return (ForjarSg($"bancada: o Corrupted Majin ({rotulo})", praca, bp, "Majin", SagaDoMajin.Classe), praca);
	}

	/// <summary>Uma vitima NOCAUTEADA a um tile do Majin.</summary>
	private ServerPlayer VitimaSg(string rotulo, Vec2 praca, string raca = "Human", bool pessoa = true, double bp = 400_000)
	{
		ServerPlayer v = ForjarSg($"bancada: {rotulo}", praca + new Vec2(ZoneCollision.TileSize, 0), bp, raca, "Normal", pessoa);
		v.Combate.Nocautear(600);
		return v;
	}

	/// <summary>A CENA DO DEGRAU ACABA: o prazo zera e o tique fecha o degrau (as frases, a outra metade, a porta da Pura).</summary>
	private void FimDaCenaSg(ServerPlayer majin)
	{
		majin.CenaSegundos = 0;
		TickDaLoucuraDoMajin(majin, 0);
	}

	private void ApertarAbsorverSg(ServerPlayer majin)
	{
		_prontoAbsorver.Remove(majin.Id);
		UsarHabilidade(majin, "absorver");
	}

	private static string OuvirSg(Action gesto)
	{
		List<string>? antes = EscutaDeAvisos;
		EscutaDeAvisos = [];
		try { gesto(); return string.Join(" | ", EscutaDeAvisos); }
		finally { EscutaDeAvisos = antes; }
	}

	/// <summary>Poe o Majin num degrau da saga como se tivesse andado ate ele (o estado, sem as cenas).</summary>
	private static void NaFormaSg(ServerPlayer majin, int forma)
	{
		majin.Ficha.majin_saga_form = forma;
		majin.Ficha.majin_kai_absorbed = forma >= 1;
		if (forma >= 1) majin.Visual.Corpo = SagaDoMajin.CorpoDaForma(forma);
	}

	/// <summary>Um Majin JA com a Forma Pura, de pe nela, fora de cena -- o ponto de partida do sistema do amigo.</summary>
	private ServerPlayer MajinPuroSg(string rotulo, out Vec2 praca)
	{
		(ServerPlayer majin, Vec2 p) = MajinDaSaga(rotulo, 3e9);
		praca = p;
		NaFormaSg(majin, SagaDoMajin.UltimaForma);
		majin.Ficha.majin_pure_unlocked = true;
		majin.Ficha.majin_sanidade = SagaDoMajin.SanidadeCheia;
		AjustarFormaDoMajin(majin);
		majin.CenaSegundos = 0;
		return majin;
	}

	private void LimparABancadaDaSaga()
	{
		bool DaBancada(int id) => id >= IdBaseDaSagaDeTeste && id < IdBaseDaSagaDeTeste + 1000;
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			bool imagem = DaBancada(pl.PresoGuardado);
			bool metade = _outrasMetades.TryGetValue(pl.Id, out OutraMetade? m) && DaBancada(m.Mestre);
			if (!DaBancada(pl.Id) && !imagem && !metade) continue;
			_dentroDoMajin.Remove(pl.Id);
			_prontoAbsorver.Remove(pl.Id);
			_outrasMetades.Remove(pl.Id);
			EsquecerTecnicas(pl.Id);
			SoltarDoRaio(pl.Id);
			_players.Remove(pl.Id);
			ZoneList(pl.Zone.Hash).Remove(pl);
		}
		foreach (ServerPlayer pl in ZoneList(ZonaDaSagaDeTeste.Hash).ToList())
			if (pl.Name.StartsWith("bancada: ", StringComparison.Ordinal) || pl.NomeDeQuemMorreu.StartsWith("bancada: ", StringComparison.Ordinal))
				ZoneList(ZonaDaSagaDeTeste.Hash).Remove(pl);
	}

	// =====================================================================
	// 1. OS NUMEROS
	// =====================================================================
	private void OsNumerosDaSaga()
	{
		GD.Print("[sagamajin] --- 1. os numeros (Core) ---");
		AfirmarSg("as formas 1..4 vestem os corpos 101..104 (a faixa reservada do `Appearance.Corpo`)",
				  Enumerable.Range(1, 4).All(f => SagaDoMajin.CorpoDaForma(f) == 100 + f)
				  && SagaDoMajin.FolhaDoCorpo(103) == SagaDoMajin.Corpos[2] && SagaDoMajin.FolhaDoCorpo(0) == null);
		AfirmarSg("o catalogo de aparencia desenha o corpo da saga pro Majin (e o saneamento nao o zera)",
				  _visual != null && _visual.CorpoSprite(new Jandirus.Core.Appearance.Appearance { Corpo = 104 }, "Majin", "Male") == SagaDoMajin.Corpos[3]
				  && SagaDoMajin.EhCorpoDaSaga(Sanear(104, "Majin")) && Sanear(104, "Human") == 0,
				  _visual == null ? "sem catalogo" : $"{_visual.CorpoSprite(new Jandirus.Core.Appearance.Appearance { Corpo = 104 }, "Majin", "Male")}");

		var m = new Maestrias();
		var perfil = new PerfilDeFormas(Raca: "Majin", Classe: SagaDoMajin.Classe);
		double Mult(string id, double loucura) => Catalogo.Multiplicador(id, m, perfil, 0, loucura);
		AfirmarSg("a Pura sa vale 56x, com ou sem loucura", Mult(Catalogo.IdDaFormaPura, 0) == 56 && Mult(Catalogo.IdDaFormaPura, 500) == 56);
		AfirmarSg("a insana nasce em 56x e ACELERA: 61x na metade do caminho (60 s sem maestria), 76x no fim (120 s)",
				  Math.Abs(Mult(Catalogo.IdDaFormaPuraInsana, 0) - 56) < 1e-9
				  && Math.Abs(Mult(Catalogo.IdDaFormaPuraInsana, 60) - 61) < 1e-9
				  && Math.Abs(Mult(Catalogo.IdDaFormaPuraInsana, 120) - 76) < 1e-9
				  && Math.Abs(Mult(Catalogo.IdDaFormaPuraInsana, 999) - 76) < 1e-9,
				  $"{Mult(Catalogo.IdDaFormaPuraInsana, 0):0.##} / {Mult(Catalogo.IdDaFormaPuraInsana, 60):0.##} / {Mult(Catalogo.IdDaFormaPuraInsana, 120):0.##}");
		m.Por(Catalogo.IdDaFormaPura, 100);
		AfirmarSg("as duas Puras dividem UMA barra de maestria, e dominada a insana chega a 76x na METADE do tempo (60 s)",
				  m.De(Catalogo.IdDaFormaPuraInsana) == 100 && Math.Abs(Mult(Catalogo.IdDaFormaPuraInsana, 60) - 76) < 1e-9,
				  $"maestria da insana {m.De(Catalogo.IdDaFormaPuraInsana)}, {Mult(Catalogo.IdDaFormaPuraInsana, 60):0.##}x");

		AfirmarSg("o dreno da insana: 0,25%/s a 56x, ~0,62%/s a 76x (o cubo)",
				  Math.Abs(SagaDoMajin.DrenoInsano(56) - 0.0025) < 1e-12
				  && Math.Abs(SagaDoMajin.DrenoInsano(76) - 0.0025 * Math.Pow(76.0 / 56, 3)) < 1e-12);
		AfirmarSg("o Mindless Gambit: nada com a cabeca cheia, 10% com metade, 20% vazia e sa, 40% insano",
				  SagaDoMajin.ReducaoNoTronco(100, false) == 0 && Math.Abs(SagaDoMajin.ReducaoNoTronco(50, false) - 0.10) < 1e-12
				  && Math.Abs(SagaDoMajin.ReducaoNoTronco(0, false) - 0.20) < 1e-12 && SagaDoMajin.ReducaoNoTronco(0, true) == 0.40);
		AfirmarSg("a sanidade: 15 min na Pura sa a esvaziam, 5 min com alguem dentro a enchem, e no resto ela fica",
				  SagaDoMajin.PassoDaSanidade(100, 900, true, false) == 0 && SagaDoMajin.PassoDaSanidade(0, 300, false, true) == 100
				  && SagaDoMajin.PassoDaSanidade(40, 100, false, false) == 40);

		var semPiso = new EstadoDeForma();
		AfirmarSg("a tecla C nunca oferece a Forma Pura (a linha nao abre pra ninguem)",
				  semPiso.Avaliar(Catalogo.IdDaFormaPura, 1e12, 1, false, perfil) == RecusaForma.LinhaFechada
				  && semPiso.Proxima(1e12, perfil)?.Id is not (Catalogo.IdDaFormaPura or Catalogo.IdDaFormaPuraInsana));
		AfirmarSg("o repouso imposto vence a derivacao: com o `PisoProprio` o piso e a Pura; sem ele, a base",
				  Catalogo.IdDoPiso(perfil with { PisoProprio = Catalogo.IdDaFormaPura }) == Catalogo.IdDaFormaPura
				  && Catalogo.IdDoPiso(perfil) == Catalogo.IdBase);
		var naPura = new EstadoDeForma { Atual = Catalogo.IdDaFormaPura };
		AfirmarSg("estando na Pura, nenhuma outra forma entra pela escada (o Mistico concedido inclusive)",
				  naPura.Avaliar(Catalogo.IdMistico, 1e12, 1, false, perfil) == RecusaForma.FormaErrada);
		AfirmarSg("a porta da Forma Pura: forma 4, ninguem dentro, 2 bilhoes de BP base",
				  SagaDoMajin.DestravaAFormaPura(4, false, false, 2e9) && !SagaDoMajin.DestravaAFormaPura(4, false, true, 3e9)
				  && !SagaDoMajin.DestravaAFormaPura(3, false, false, 3e9) && !SagaDoMajin.DestravaAFormaPura(4, false, false, 1.9e9));
	}

	/// <summary>O `Sanear` de producao sobre um indice de corpo, numa aparencia descartavel.</summary>
	private int Sanear(int corpo, string raca)
	{
		var ap = new Jandirus.Core.Appearance.Appearance { Corpo = corpo };
		_visual?.Sanear(ap, raca, "Male");
		return ap.Corpo;
	}

	// =====================================================================
	// 2. A PASSAGEM -- o defeito do DM era o nome da animacao
	// =====================================================================
	private void AFolhaTemAPassagem()
	{
		GD.Print("[sagamajin] --- 2. a passagem mora na folha de ORIGEM, e com o nome que ela tem ---");
		var passagens = new List<(string Folha, string Estado)>();
		for (int f = 2; f <= 4; f++) passagens.Add(SagaDoMajin.PassagemPara(f)!.Value);
		passagens.Add(SagaDoMajin.PassagemDaPura);
		foreach ((string folha, string estado) in passagens)
		{
			var frames = ResourceLoader.Load<SpriteFrames>(folha);
			AfirmarSg($"`{estado}` existe em {folha.GetFile()} (no DM o nome nao batia e nada tocava)",
					  frames != null && frames.HasAnimation(estado) && frames.GetFrameCount(estado) > 20,
					  frames == null ? "folha nao carregou" : $"{frames.GetFrameCount(estado)} quadros");
		}
		AfirmarSg("a forma 1 nao tem passagem (`majin_advance_form(1, null)`), e a cena dela nao acende o bit",
				  SagaDoMajin.PassagemPara(1) == null && Cinematicas.MajinForma1.FolhaDaPassagem.Length == 0
				  && Cinematicas.MajinForma1.Beats.All(b => !b.Faz.HasFlag(Efeito.Passagem)));
		AfirmarSg("as cenas 2..4 levam a folha e o estado da passagem, e UM beat que a acende",
				  Enumerable.Range(2, 3).All(f => Cinematicas.DoMajin((Cinematicas.CenaMajin)f) is { } c
					  && c.FolhaDaPassagem == SagaDoMajin.PassagemPara(f)!.Value.Folha
					  && c.Beats.Count(b => b.Faz.HasFlag(Efeito.Passagem)) == 1));
		AfirmarSg("a cena da Forma Pura toca o tema dela e a passagem do `kid trans` -- e a encurtada leva a passagem junto",
				  Cinematicas.MajinPura.Musica == SagaDoMajin.TemaDaPura && Cinematicas.MajinPura.EstadoDaPassagem == "kid_trans"
				  && Cinematicas.Encurtada(Cinematicas.MajinPura).EstadoDaPassagem == "kid_trans"
				  && Cinematicas.Para(Catalogo.Def(Catalogo.IdDaFormaPura)!) == Cinematicas.MajinPura);
	}

	// =====================================================================
	// 3. A FORMA 1 -- o Kai
	// =====================================================================
	private void AFormaUmPeloKai()
	{
		GD.Print("[sagamajin] --- 3. a forma 1: absorver um Kai (`:146-148`, `:228-230`) ---");
		(ServerPlayer majin, Vec2 praca) = MajinDaSaga("kai");
		double modAntes = majin.Ficha.BPMod;
		ServerPlayer kai = VitimaSg("o Kai", praca, "Kai", pessoa: false);
		EscutaDeCenasDoMajin!.Clear();
		ApertarAbsorverSg(majin);

		AfirmarSg("o Kai NPC devorado sobe o Corrupted Majin pra forma 1",
				  majin.Ficha.majin_saga_form == 1 && majin.Ficha.majin_kai_absorbed, $"forma {majin.Ficha.majin_saga_form}");
		AfirmarSg("...com o corpo da forma 1, +1 de poder de luta e a cena dela", majin.Visual.Corpo == SagaDoMajin.CorpoDaForma(1)
				  && Math.Abs(majin.Ficha.BPMod - modAntes - 1) < 1e-9
				  && EscutaDeCenasDoMajin.Contains((majin.Id, Cinematicas.CenaMajin.Forma1)),
				  $"corpo {majin.Visual.Corpo}, BPMod {modAntes} -> {majin.Ficha.BPMod}, cenas {EscutaDeCenasDoMajin.Count}");

		VitimaSg("o segundo Kai", praca, "Kai", pessoa: false);
		ApertarAbsorverSg(majin);
		AfirmarSg("um segundo Kai nao sobe nada (`!majin_kai_absorbed`)", majin.Ficha.majin_saga_form == 1);

		ServerPlayer comum = ForjarSg("bancada: um Majin comum", PracaDaSaga(), 1_000_000, "Majin", "Majin");
		VitimaSg("o Kai do Majin comum", comum.Pos, "Kai", pessoa: false);
		ApertarAbsorverSg(comum);
		AfirmarSg("o Majin de classe comum absorve o Kai e a saga nao anda -- ela e so do Corrupted", comum.Ficha.majin_saga_form == 0);
	}

	// =====================================================================
	// 4. A FORMA 2 -- o amigo morto, e a outra metade
	// =====================================================================
	private void AFormaDoisPelaMorte()
	{
		GD.Print("[sagamajin] --- 4. a forma 2: o amigo MORTO por um inimigo (`Death.dm:82`) e a outra metade (`:489-508`) ---");
		(ServerPlayer majin, Vec2 praca) = MajinDaSaga("luto");
		NaFormaSg(majin, 1);
		ServerPlayer amigo = ForjarSg("bancada: o amigo do Majin", praca + new Vec2(2 * ZoneCollision.TileSize, 0), 500_000);
		ServerPlayer algoz = ForjarSg("bancada: o algoz", praca + new Vec2(3 * ZoneCollision.TileSize, 0), 900_000);
		DeclararDeTeste(majin, amigo, Jandirus.Core.Social.Relacao.Bom);

		AoPerderALuta(amigo, algoz, morreu: false);
		AfirmarSg("o amigo so NOCAUTEADO nao parte o Majin (o gatilho do DM e a morte)", majin.Ficha.majin_saga_form == 1);

		AoPerderALuta(amigo, algoz, morreu: true);
		AfirmarSg("o amigo MORTO por um inimigo parte o Majin: forma 2", majin.Ficha.majin_saga_form == 2, $"forma {majin.Ficha.majin_saga_form}");
		AfirmarSg("...e DURANTE a cena a outra metade ainda nao saltou (no DM ela sai no FIM do `MajinSagaCinematic`, `:425`)",
				  OutraMetadeDe(majin.Id) == null && EmCena(majin), $"cena {majin.CenaSegundos:0.#}s");
		FimDaCenaSg(majin);
		ServerPlayer? metade = OutraMetadeDe(majin.Id);
		AfirmarSg("...e com a cena acabada a OUTRA METADE salta do corpo: o nome dele, o corpo da forma 1, metade do poder expresso, PINADA",
				  metade != null && metade.Name == NomeVisivel(majin) && metade.Visual.Corpo == SagaDoMajin.CorpoDaForma(1)
				  && Math.Abs(metade.Ficha.BP - SagaDoMajin.BpDaOutraMetade(majin.Ficha.expressedBP)) < majin.Ficha.expressedBP * 0.01,
				  metade == null ? "nao nasceu" : $"'{metade.Name}', corpo {metade.Visual.Corpo}, BP {metade.Ficha.BP:N0} (mestre expressa {majin.Ficha.expressedBP:N0})");
		if (metade != null)
		{
			bool guia = GuiarAOutraMetade(metade, _outrasMetades[metade.Id], out ServerPlayer? presa, out _);
			AfirmarSg("...e ela caca o mestre, e so ele (`foundTarget(src)`)", guia && presa == majin);
			metade.Ficha.BP *= 10;
			GuiarAOutraMetade(metade, _outrasMetades[metade.Id], out _, out _);
			AfirmarSg("...e o BP dela volta ao pino (o `NPCTicker` do clone)", Math.Abs(metade.Ficha.BP - _outrasMetades[metade.Id].Bp) < 1);
		}

		// O DEFEITO: a furia que nao solta a outra metade.
		(ServerPlayer majin2, Vec2 praca2) = MajinDaSaga("luto sem metade");
		NaFormaSg(majin2, 1);
		ServerPlayer amigo2 = ForjarSg("bancada: o amigo 2", praca2 + new Vec2(2 * ZoneCollision.TileSize, 0), 500_000);
		ServerPlayer algoz2 = ForjarSg("bancada: o algoz 2", praca2 + new Vec2(3 * ZoneCollision.TileSize, 0), 900_000);
		DeclararDeTeste(majin2, amigo2, Jandirus.Core.Social.Relacao.Bom);
		SagaSemOutraMetadeDeTeste = true;
		try { AoPerderALuta(amigo2, algoz2, morreu: true); FimDaCenaSg(majin2); }
		finally { SagaSemOutraMetadeDeTeste = false; }
		AfirmarSg("(defeito injetado: a furia sem a outra metade) a forma 2 chega SEM ninguem pra vencer -- a primeira regua REPROVA",
				  majin2.Ficha.majin_saga_form == 2 && OutraMetadeDe(majin2.Id) == null);

		// ============================ O FIM DA CENA PELO RELOGIO DE PRODUCAO ============================
		// As reguas de cima fecham a cena pela chamada direta (`FimDaCenaSg`). Em jogo quem fecha e o `TickDosRelogiosDoCorpo`, e a
		// outra metade entra no `_players` DE DENTRO da volta dele -- a primeira rodada da `--diagsagamajin` pegou o laco estourando
		// ("Collection was modified"), o tique inteiro abortando e a metade nascendo de novo a cada tique.
		// ===========================================================================================
		int MetadesDe(int mestre) => _outrasMetades.Values.Count(m => m.Mestre == mestre);
		ServerPlayer EnlutadoPeloRelogio(string rotulo)
		{
			(ServerPlayer m, Vec2 p) = MajinDaSaga(rotulo);
			NaFormaSg(m, 1);
			ServerPlayer a = ForjarSg($"bancada: o amigo ({rotulo})", p + new Vec2(2 * ZoneCollision.TileSize, 0), 500_000);
			ServerPlayer z = ForjarSg($"bancada: o algoz ({rotulo})", p + new Vec2(3 * ZoneCollision.TileSize, 0), 900_000);
			DeclararDeTeste(m, a, Jandirus.Core.Social.Relacao.Bom);
			AoPerderALuta(a, z, morreu: true);
			m.CenaSegundos = 0;   // a cena acabou: quem fecha o degrau agora e o relogio
			return m;
		}
		string? Relogios()
		{
			try { TickDosRelogiosDoCorpo(0.05); TickDosRelogiosDoCorpo(0.05); return null; }
			catch (InvalidOperationException e) { return e.Message; }
		}

		ServerPlayer peloRelogio = EnlutadoPeloRelogio("metade pelo relogio");
		string? estouro = Relogios();
		AfirmarSg("pelo RELOGIO de producao, o fim da cena solta UMA outra metade -- e o laco dos relogios nao estoura",
				  estouro == null && MetadesDe(peloRelogio.Id) == 1, estouro ?? $"{MetadesDe(peloRelogio.Id)} metade(s)");

		ServerPlayer noDicionario = EnlutadoPeloRelogio("metade no dicionario");
		SagaNoDicionarioDeTeste = true;
		string? estouroDoDefeito;
		try { estouroDoDefeito = Relogios(); }
		finally { SagaNoDicionarioDeTeste = false; }
		AfirmarSg("(defeito injetado: a saga direto no dicionario) o laco ESTOURA quando a metade nasce -- a regua de cima REPROVA",
				  estouroDoDefeito != null, $"{MetadesDe(noDicionario.Id)} metade(s), sem estouro");
	}

	// =====================================================================
	// 5. A FORMA 3 -- a outra metade vencida, absorvida
	// =====================================================================
	private void AFormaTresPelaOutraMetade()
	{
		GD.Print("[sagamajin] --- 5. a forma 3: absorver a outra metade VENCIDA (`:124-130`) ---");
		(ServerPlayer majin, Vec2 praca) = MajinDaSaga("metade");
		NaFormaSg(majin, 2);
		SoltarAOutraMetade(majin);
		ServerPlayer? metade = OutraMetadeDe(majin.Id);
		AfirmarSg("a outra metade esta de pe ao lado", metade != null);
		if (metade == null) return;
		metade.Pos = majin.Pos + new Vec2(ZoneCollision.TileSize, 0);

		string dePe = OuvirSg(() => ApertarAbsorverSg(majin));
		AfirmarSg("de pe, ela recusa: *\"You must defeat your other half before you can take it back\"*",
				  majin.Ficha.majin_saga_form == 2 && _players.ContainsKey(metade.Id) && dePe.Contains("vencer a sua outra metade"), dePe);

		metade.Combate.Nocautear(600);
		ApertarAbsorverSg(majin);
		AfirmarSg("vencida, ela volta pra dentro (sai do mundo) e o Majin vira SUPER MAJIN: forma 3, com o marco",
				  majin.Ficha.majin_saga_form == 3 && !_players.ContainsKey(metade.Id) && majin.Ficha.bp_milestones_done.Contains("majin3")
				  && majin.Visual.Corpo == SagaDoMajin.CorpoDaForma(3),
				  $"forma {majin.Ficha.majin_saga_form}, no mundo {_players.ContainsKey(metade.Id)}");

		// O DEFEITO: absorver de pe.
		(ServerPlayer m2, Vec2 _) = MajinDaSaga("metade de pe");
		NaFormaSg(m2, 2);
		SoltarAOutraMetade(m2);
		if (OutraMetadeDe(m2.Id) is { } metade2)
		{
			metade2.Pos = m2.Pos + new Vec2(ZoneCollision.TileSize, 0);
			OutraMetadeSemVencerDeTeste = true;
			try { ApertarAbsorverSg(m2); }
			finally { OutraMetadeSemVencerDeTeste = false; }
			AfirmarSg("(defeito injetado: a metade de pe se absorve) o Majin pula pra forma 3 sem lutar -- a regua da recusa REPROVA",
					  m2.Ficha.majin_saga_form == 3);
		}
	}

	// =====================================================================
	// 6. A FORMA 4 -- tres jogadores
	// =====================================================================
	private void AFormaQuatroPelosTresJogadores()
	{
		GD.Print("[sagamajin] --- 6. a forma 4: tres JOGADORES absorvidos na forma 3 (`:231-234`) ---");
		(ServerPlayer majin, Vec2 praca) = MajinDaSaga("tres", 1_000_000);
		NaFormaSg(majin, 3);

		VitimaSg("um NPC", praca, pessoa: false);
		ApertarAbsorverSg(majin);
		AfirmarSg("o NPC devorado na forma 3 nao conta (so jogador)", majin.Ficha.majin_form3_players == 0);

		string ouvido = "";
		for (int i = 1; i <= 3; i++)
		{
			VitimaSg($"o jogador {i}", praca);
			ouvido = OuvirSg(() => ApertarAbsorverSg(majin));
		}
		AfirmarSg("o terceiro jogador absorvido sobe pra forma 4, com o contador falado e o corpo da forma 4",
				  majin.Ficha.majin_saga_form == 4 && majin.Visual.Corpo == SagaDoMajin.CorpoDaForma(4) && ouvido.Contains("3/3"),
				  $"forma {majin.Ficha.majin_saga_form}, {majin.Ficha.majin_form3_players} jogadores; ouvido: {ouvido}");
		AfirmarSg("com gente dentro a Forma Pura NAO destrava (*\"precisa ter PERDIDO todos os absorvidos\"*)", !majin.Ficha.majin_pure_unlocked);

		string semPoder = OuvirSg(() => ExpelirDoMajin(majin, ""));
		AfirmarSg("vazio, mas sem os 2 bilhoes de BP base, ele OUVE o que falta -- em proporcao, sem numero de BP",
				  !majin.Ficha.majin_pure_unlocked && semPoder.Contains("Forma Pura") && semPoder.Contains("%"), semPoder);

		FimDaCenaSg(majin);
		AfirmarSg("o fim da cena do degrau 4 confere a porta -- e sem o poder ela continua fechada", !majin.Ficha.majin_pure_unlocked);

		majin.Ficha.BP = 3e9;
		majin.Ficha.Statify();
		EscutaDeAnuncios!.Clear();
		TickDaLoucuraDoMajin(majin, 1.0);
		AfirmarSg("o BP que cruza a porta depois destrava sozinho, no relogio de um segundo (`Stats.dm:61`)", majin.Ficha.majin_pure_unlocked);
		AfirmarSg("...e o corpo vai NA HORA pra Forma Pura, com a estreia (a cena cheia, com o tema)",
				  majin.Forma.Atual == Catalogo.IdDaFormaPura
				  && EscutaDeAnuncios.Exists(a => a.Quem == majin.Id && a.Para == Catalogo.IdDaFormaPura && a.Degrau == DegrauDeCena.Estreia),
				  $"forma {majin.Forma.Atual}");
		AfirmarSg("...e ganha a Sycophantic Rage na lista de tecnicas", TecnicasDe(majin).Contains(SagaDoMajin.VerboDaFuria));
	}

	// =====================================================================
	// 7. A FORMA PURA E O REPOUSO
	// =====================================================================
	private void AFormaPuraEORepouso()
	{
		GD.Print("[sagamajin] --- 7. a Forma Pura e o repouso: o X, a queda, absorver e soltar ---");
		ServerPlayer majin = MajinPuroSg("repouso", out Vec2 praca);
		AfirmarSg("sozinho, o Corrupted Majin com a Forma Pura ESTA nela (*\"base form replaced with Pure Form\"*)",
				  majin.Forma.Atual == Catalogo.IdDaFormaPura, majin.Forma.Atual);
		AfirmarSg("...e ela vale 56x no corpo (o `ssjBuff`)", Math.Abs(majin.Ficha.ssjBuff - 56) < 1e-6, $"{majin.Ficha.ssjBuff}");

		string x = OuvirSg(() => ReverterOuLiberar(majin));
		AfirmarSg("o X nao a desfaz -- e diz por que", majin.Forma.Atual == Catalogo.IdDaFormaPura && x.Contains("só absorver"), x);

		majin.Combate.Nocautear(5);
		TickDaForma(majin, 0.1);
		AfirmarSg("cair nocauteado nao a desfaz: ela e o corpo dele", majin.Forma.Atual == Catalogo.IdDaFormaPura);
		majin.Combate.Levantar();

		// O DEFEITO: o repouso imposto desligado. A queda leva o corpo pro piso DERIVADO (a base), como faria com qualquer forma -- e
		// e pela queda que se mede, porque o X do Majin nao desceria nada de todo jeito (a raca nao tem escada).
		PuraSemPisoDeTeste = true;
		try { majin.Combate.Nocautear(5); TickDaForma(majin, 0.1); }
		finally { PuraSemPisoDeTeste = false; majin.Combate.Levantar(); }
		AfirmarSg("(defeito injetado: a Pura sem o repouso) a queda a desfaz como qualquer forma -- a regua da queda REPROVA",
				  majin.Forma.Atual != Catalogo.IdDaFormaPura, majin.Forma.Atual);
		AjustarFormaDoMajin(majin);
		majin.CenaSegundos = 0;

		VitimaSg("o NPC do repouso", praca, pessoa: false);
		ApertarAbsorverSg(majin);
		AfirmarSg("absorver alguem TIRA da Forma Pura: o corpo de absorcao (a base), sem o multiplicador",
				  majin.Forma.Atual == Catalogo.IdBase && majin.Ficha.ssjBuff < 2, $"{majin.Forma.Atual}, x{majin.Ficha.ssjBuff}");

		ExpelirDoMajin(majin, "");
		AfirmarSg("expulsar quem estava dentro devolve o corpo a Forma Pura", majin.Forma.Atual == Catalogo.IdDaFormaPura, majin.Forma.Atual);
	}

	// =====================================================================
	// 8. A SANIDADE
	// =====================================================================
	private void ASanidade()
	{
		GD.Print("[sagamajin] --- 8. a sanidade: a Pura gasta, alguem dentro devolve, a fuga do primeiro custa, expulsar nao ---");
		ServerPlayer majin = MajinPuroSg("sanidade", out Vec2 praca);
		majin.CenaSegundos = 0;
		TickDaLoucuraDoMajin(majin, 450);
		AfirmarSg("meia hora... meio caminho: 7,5 min na Pura sa levam METADE da sanidade",
				  Math.Abs(majin.Ficha.majin_sanidade - 50) < 0.01, $"{majin.Ficha.majin_sanidade:0.##}");
		AfirmarSg("...e o Mindless Gambit ja protege o tronco em 10%", Math.Abs(majin.Combate.Corpo.ReducaoNoTronco - 0.10) < 1e-9,
				  $"{majin.Combate.Corpo.ReducaoNoTronco}");

		VitimaSg("o NPC da sanidade", praca, pessoa: false);
		ApertarAbsorverSg(majin);
		TickDaLoucuraDoMajin(majin, 150);
		AfirmarSg("com alguem dentro, 2,5 min devolvem a metade que faltava", Math.Abs(majin.Ficha.majin_sanidade - 100) < 0.01,
				  $"{majin.Ficha.majin_sanidade:0.##}");
		ExpelirDoMajin(majin, "");

		// A FUGA DO PRIMEIRO: o jogador que vence a imagem.
		ServerPlayer primeiro = VitimaSg("o primeiro absorvido", praca);
		ApertarAbsorverSg(majin);
		ServerPlayer segundo = VitimaSg("o segundo absorvido", praca);
		ApertarAbsorverSg(majin);
		double antes = majin.Ficha.majin_sanidade;
		AbsorvidoPeloMajin? recDoSegundo = AbsorvidosPor(majin.Id).FirstOrDefault(r => r.Quem == segundo.Id);
		if (recDoSegundo != null && _players.TryGetValue(recDoSegundo.Guardiao, out ServerPlayer? g2)) g2.Combate.Nocautear(600);
		TickDoMajin(AbsorcaoMajin.SegundosEntreOlhadas);
		AfirmarSg("o SEGUNDO fugir nao custa nada (*\"having their first absorb freed\"*)",
				  majin.Ficha.majin_sanidade == antes && !DentroDeUmMajin(segundo), $"{antes:0.##} -> {majin.Ficha.majin_sanidade:0.##}");
		AbsorvidoPeloMajin? recDoPrimeiro = AbsorvidosPor(majin.Id).FirstOrDefault(r => r.Quem == primeiro.Id);
		if (recDoPrimeiro != null && _players.TryGetValue(recDoPrimeiro.Guardiao, out ServerPlayer? g1)) g1.Combate.Nocautear(600);
		TickDoMajin(AbsorcaoMajin.SegundosEntreOlhadas);
		AfirmarSg($"o PRIMEIRO fugir (venceu a imagem) custa {SagaDoMajin.CustoDaFuga:0} de sanidade",
				  Math.Abs(majin.Ficha.majin_sanidade - (antes - SagaDoMajin.CustoDaFuga)) < 1e-9, $"{antes:0.##} -> {majin.Ficha.majin_sanidade:0.##}");

		// EXPULSAR NAO E FUGA -- e o defeito ao lado.
		majin.Ficha.majin_sanidade = 100;
		VitimaSg("o expulso", praca);
		ApertarAbsorverSg(majin);
		ExpelirDoMajin(majin, "");
		AfirmarSg("expulsar o primeiro NAO custa sanidade (*\"expulsar n conta como jogador fugir\"*)", majin.Ficha.majin_sanidade == 100);
		VitimaSg("o expulso do defeito", praca);
		ApertarAbsorverSg(majin);
		ExpelirCustaSanidadeDeTeste = true;
		try { ExpelirDoMajin(majin, ""); }
		finally { ExpelirCustaSanidadeDeTeste = false; }
		AfirmarSg("(defeito injetado: expulsar conta como fuga) a sanidade cai -- a regua de cima REPROVA", majin.Ficha.majin_sanidade < 100);
	}

	// =====================================================================
	// 9. A LOUCURA
	// =====================================================================
	private void ALoucura()
	{
		GD.Print("[sagamajin] --- 9. a loucura: a insana, a IA, os gritos, sem Sense, sem teletransporte, sem regeneracao ---");
		ServerPlayer majin = MajinPuroSg("loucura", out Vec2 _);
		majin.Livro.Dar("/datum/skill/sense");
		AplicarPoderes(majin);
		AfirmarSg("(controle) com a cabeca inteira ele tem o Sense", (majin.Poderes & Protocol.Poder.Sense) != 0);

		majin.Ficha.majin_sanidade = 0.01;
		EscutaDeFalas!.Clear();
		TickDaLoucuraDoMajin(majin, 1.0);
		AfirmarSg("a sanidade acaba: a Forma Pura INSANA, SEM cena (a loucura nao espera)", majin.Forma.Atual == Catalogo.IdDaFormaPuraInsana
				  && majin.CenaSegundos == 0, $"{majin.Forma.Atual}, cena {majin.CenaSegundos}");
		AfirmarSg("...a IA assume o corpo (*\"ao ficar insano a IA controla o jogador\"*)", majin.CerebroDaPosse != null);
		AfirmarSg("...o Sense apaga (Fragmented Mind)", (majin.Poderes & Protocol.Poder.Sense) == 0);
		string it = OuvirSg(() => TeletransporteG11(majin, ""));
		AfirmarSg("...e a Transmissao Instantanea recusa", it.Contains("mente partida"), it);

		majin.Ficha.Ki = majin.Ficha.MaxKi * 0.5;
		double kiAntes = majin.Ficha.Ki;
		TickDaCarga(majin, 1.0);
		AfirmarSg("...a regeneracao passiva para (*\"desativa regen de ki passiva\"*)", majin.Ficha.Ki <= kiAntes, $"{kiAntes:0.#} -> {majin.Ficha.Ki:0.#}");
		LoucuraComRegenDeTeste = true;
		try { TickDaCarga(majin, 1.0); }
		finally { LoucuraComRegenDeTeste = false; }
		AfirmarSg("(defeito injetado: a insana regenera) o Ki SOBE -- a regua da regeneracao REPROVA", majin.Ficha.Ki > kiAntes);
		majin.Ficha.Ki = kiAntes;

		VivoDoMajin(majin).ProximoGritoEm = 0;   // o relogio do grito e de parede: a bancada nao espera o segundo dele
		double multAntes = MultiplicadorDaForma(majin);
		TickDaLoucuraDoMajin(majin, 30);
		double kiDepois = majin.Ficha.Ki;
		AfirmarSg("...o Ki CAI devagar com o tempo de loucura (*\"faz o ki cair bem devagar\"*)",
				  kiDepois < kiAntes && kiAntes - kiDepois < majin.Ficha.MaxKi * 0.2, $"{kiAntes:0.#} -> {kiDepois:0.#} de {majin.Ficha.MaxKi:0.#}");
		TickDaForma(majin, 0.1);   // o tique da forma reaplica o multiplicador com a loucura de agora
		AfirmarSg("...e o multiplicador SOBE com a loucura (56x -> mais)", MultiplicadorDaForma(majin) > multAntes + 0.5,
				  $"{multAntes:0.##} -> {MultiplicadorDaForma(majin):0.##}");
		AfirmarSg("...e o corpo GRITA no chat como bicho", EscutaDeFalas.Exists(f => f.Quem == majin.Id && SagaDoMajin.Gritos.Contains(f.Texto)),
				  string.Join(" / ", EscutaDeFalas.Where(f => f.Quem == majin.Id).Select(f => f.Texto)));
		AfirmarSg("...e a tela do dono recebe a loucura (o tunel)", EscutaDaLoucura!.Exists(l => l.Quem == majin.Id && l.Insano));

		// OS DEFEITOS: sem IA, mente inteira.
		ServerPlayer outro = MajinPuroSg("loucura sem ia", out Vec2 _);
		outro.Ficha.majin_sanidade = 0.01;
		LoucuraSemIaDeTeste = true;
		try { TickDaLoucuraDoMajin(outro, 1.0); }
		finally { LoucuraSemIaDeTeste = false; }
		AfirmarSg("(defeito injetado: a loucura sem IA) o insano continua com o jogador no comando -- a regua da posse REPROVA",
				  outro.Forma.Atual == Catalogo.IdDaFormaPuraInsana && outro.CerebroDaPosse == null);
		MenteInteiraNaLoucuraDeTeste = true;
		string itDefeito;
		try { itDefeito = OuvirSg(() => TeletransporteG11(outro, "")); }
		finally { MenteInteiraNaLoucuraDeTeste = false; }
		AfirmarSg("(defeito injetado: a mente inteira) o insano tenta se teletransportar -- a regua do Fragmented Mind REPROVA",
				  !itDefeito.Contains("mente partida"), itDefeito);
	}

	// =====================================================================
	// 10. O SONO
	// =====================================================================
	private void OSono()
	{
		GD.Print("[sagamajin] --- 10. o sono: o Ki acaba (ou ele cai) -> dois minutos -> 10% de Ki e a cabeca de volta ---");
		ServerPlayer majin = MajinPuroSg("sono", out Vec2 _);
		majin.Ficha.majin_sanidade = 0.01;
		TickDaLoucuraDoMajin(majin, 1.0);
		majin.Ficha.Ki = 0;
		TickDaLoucuraDoMajin(majin, 0.1);
		MajinPuroVivo v = VivoDoMajin(majin);
		AfirmarSg("o Ki zerado APAGA o corpo: nocauteado, fora da insana, sem a IA, dormindo",
				  majin.Ficha.KO && majin.Forma.Atual == Catalogo.IdBase && majin.CerebroDaPosse == null && v.DormeAte > NowMs(),
				  $"KO {majin.Ficha.KO}, {majin.Forma.Atual}, posse {majin.CerebroDaPosse != null}");
		AfirmarSg("...por dois minutos", Math.Abs((v.DormeAte - NowMs()) / 1000.0 - SagaDoMajin.SegundosDormindo) < 2);

		v.DormeAte = NowMs() - 1;
		majin.CenaSegundos = 0;
		TickDaLoucuraDoMajin(majin, 0.1);
		AfirmarSg("acordado: a sanidade CHEIA, o Ki em 10%, de pe -- e de volta a Forma Pura",
				  majin.Ficha.majin_sanidade == 100 && Math.Abs(majin.Ficha.Ki / majin.Ficha.MaxKi - 0.10) < 0.01 && !majin.Ficha.KO
				  && majin.Forma.Atual == Catalogo.IdDaFormaPura,
				  $"sanidade {majin.Ficha.majin_sanidade}, Ki {majin.Ficha.Ki / majin.Ficha.MaxKi:P0}, KO {majin.Ficha.KO}, {majin.Forma.Atual}");

		// DERRUBADO NA LOUCURA: tambem dorme (*"se ninguem o vencer"*).
		ServerPlayer outro = MajinPuroSg("sono pelo golpe", out Vec2 _);
		outro.Ficha.majin_sanidade = 0.01;
		TickDaLoucuraDoMajin(outro, 1.0);
		outro.Combate.Nocautear(10);
		TickDaLoucuraDoMajin(outro, 0.1);
		AfirmarSg("vencido na loucura (nocauteado), ele tambem apaga e dorme os dois minutos", VivoDoMajin(outro).DormeAte > NowMs()
				  && outro.Combate.NocauteRestante >= SagaDoMajin.SegundosDormindo - 1);
	}

	// =====================================================================
	// 11. A SYCOPHANTIC RAGE
	// =====================================================================
	private void ASycophanticRage()
	{
		GD.Print("[sagamajin] --- 11. a Sycophantic Rage: o ataque carregando sai mais forte, e a sanidade paga ---");
		ServerPlayer majin = MajinPuroSg("furia", out Vec2 _);
		static ReceitaDeProjetil Onda() => new()
		{
			Tipo = TipoDeProjetil.Beam, BaseDano = 1, Velocidade = 1, AlcanceTiles = 30, CargaMinima = 1, Nome = "Onda de Ki",
		};

		string semCarga = OuvirSg(() => UsarHabilidade(majin, SagaDoMajin.VerboDaFuria));
		AfirmarSg("sem nada carregando ela recusa, sem cobrar sanidade nem recarga",
				  semCarga.Contains("carregando") && majin.Ficha.majin_sanidade == 100 && VivoDoMajin(majin).FuriaProntaEm == 0, semCarga);

		majin.Ficha.Ki = majin.Ficha.MaxKi;
		Canalizar(majin, "Ki_Wave", 10 * majin.Ficha.BaseDrain(), Onda());
		AfirmarSg("(controle) o raio esta carregando", _canais.ContainsKey(majin.Id));
		UsarHabilidade(majin, SagaDoMajin.VerboDaFuria);
		AfirmarSg($"carregando, ela arma o ataque e cobra {SagaDoMajin.CustoDaFuria:0} de sanidade",
				  majin.Ficha.majin_sanidade == 100 - SagaDoMajin.CustoDaFuria && VivoDoMajin(majin).FuriaNoTiro == 2,
				  $"sanidade {majin.Ficha.majin_sanidade}, fator {VivoDoMajin(majin).FuriaNoTiro}");
		double expresso = majin.Ficha.expressedBP;
		if (_canais.TryGetValue(majin.Id, out CanalDeKi? canal)) NascerORaio(majin, canal);
		Projetil? raio = _canais.TryGetValue(majin.Id, out CanalDeKi? c2) ? c2.Raio : null;
		AfirmarSg("o raio que sai leva o DOBRO do poder (`A.BP = expressedBP * wavemult`, vezes 2)",
				  raio != null && Math.Abs(raio.Bp / expresso - 2) < 0.05, raio == null ? "o raio nao saiu" : $"{raio.Bp / expresso:0.###}x");
		string recarga = OuvirSg(() => UsarHabilidade(majin, SagaDoMajin.VerboDaFuria));
		AfirmarSg("a recarga de um minuto segura o segundo uso", recarga.Contains("ainda não voltou"), recarga);
		SoltarDoRaio(majin.Id);

		// O DEFEITO: a furia que nao mexe no tiro.
		ServerPlayer outro = MajinPuroSg("furia sem tiro", out Vec2 _);
		outro.Ficha.Ki = outro.Ficha.MaxKi;
		Canalizar(outro, "Ki_Wave", 10 * outro.Ficha.BaseDrain(), Onda());
		FuriaSemTiroDeTeste = true;
		try { UsarHabilidade(outro, SagaDoMajin.VerboDaFuria); }
		finally { FuriaSemTiroDeTeste = false; }
		double exp2 = outro.Ficha.expressedBP;
		if (_canais.TryGetValue(outro.Id, out CanalDeKi? c3)) NascerORaio(outro, c3);
		Projetil? raio2 = _canais.TryGetValue(outro.Id, out CanalDeKi? c4) ? c4.Raio : null;
		AfirmarSg("(defeito injetado: a furia sem tiro) a sanidade paga e o raio sai do tamanho de sempre -- a regua do poder REPROVA",
				  raio2 != null && Math.Abs(raio2.Bp / exp2 - 1) < 0.05, raio2 == null ? "sem raio" : $"{raio2.Bp / exp2:0.###}x");
		SoltarDoRaio(outro.Id);

		// INSANO: o triplo e um quarto a menos de Ki.
		ServerPlayer louco = MajinPuroSg("furia insana", out Vec2 _);
		louco.Ficha.majin_sanidade = 0;
		louco.Ficha.Ki = louco.Ficha.MaxKi;
		Canalizar(louco, "Ki_Wave", 10 * louco.Ficha.BaseDrain(), Onda());
		double custoAntes = _canais.TryGetValue(louco.Id, out CanalDeKi? c5) ? c5.CustoPorCiclo : 0;
		UsarAFuriaBajuladora(louco);
		double custoDepois = _canais.TryGetValue(louco.Id, out CanalDeKi? c6) ? c6.CustoPorCiclo : 0;
		AfirmarSg("insano, ela arma o TRIPLO e o raio custa um quarto a menos por ciclo",
				  VivoDoMajin(louco).FuriaNoTiro == 3 && custoAntes > 0 && Math.Abs(custoDepois / custoAntes - 0.75) < 1e-9,
				  $"fator {VivoDoMajin(louco).FuriaNoTiro}, custo {custoAntes:0.####} -> {custoDepois:0.####}");
		SoltarDoRaio(louco.Id);
	}

	// =====================================================================
	// 12. O MINDLESS GAMBIT
	// =====================================================================
	private void OMindlessGambit()
	{
		GD.Print("[sagamajin] --- 12. o Mindless Gambit: o tronco aguenta mais quanto menos sanidade sobra ---");
		ServerPlayer majin = MajinPuroSg("gambito", out Vec2 _);
		Body corpo = majin.Combate.Corpo;
		BodyPart tronco = corpo.Partes.First(p => p.Nome == Body.NomeDoTronco);
		BodyPart braco = corpo.Partes.First(p => p.Nome != Body.NomeDoTronco && !p.Aninhado && string.IsNullOrEmpty(p.Dono) && p.VidaMax > 0);

		double Golpe(BodyPart p)
		{
			p.Vida = p.VidaMax;
			corpo.Ferir(p, 10, letal: true);
			return p.VidaMax - p.Vida;
		}

		majin.Ficha.majin_sanidade = 0;
		corpo.ReducaoNoTronco = SagaDoMajin.ReducaoNoTronco(0, insano: true);
		AfirmarSg("insano, o tronco leva 40% a menos; o braco, o golpe inteiro",
				  Math.Abs(Golpe(tronco) - 6) < 1e-9 && Math.Abs(Golpe(braco) - 10) < 1e-9, $"tronco {Golpe(tronco)}, braco {Golpe(braco)}");
		Body.GambitoDesligadoDeTeste = true;
		double semGambito;
		try { semGambito = Golpe(tronco); }
		finally { Body.GambitoDesligadoDeTeste = false; }
		AfirmarSg("(defeito injetado: o gambito desligado) o tronco leva o golpe inteiro -- a regua de cima REPROVA", semGambito == 10);
		majin.Ficha.majin_sanidade = SagaDoMajin.SanidadeCheia;
		// TEMPO ZERO: o tique da Pura sa gasta sanidade, e 0,01 s ja tirava um fio dela (o tronco saia com 2e-6 de reducao).
		TickDaLoucuraDoMajin(majin, 0);
		AfirmarSg("com a cabeca cheia o tique devolve o tronco a zero de reducao", corpo.ReducaoNoTronco < 1e-6, $"{corpo.ReducaoNoTronco}");
	}

	// =====================================================================
	// 13. O ECHOED INSANITY
	// =====================================================================
	private void OEchoedInsanity()
	{
		GD.Print("[sagamajin] --- 13. o Echoed Insanity: toda maestria 20% mais devagar ---");
		ServerPlayer majin = MajinPuroSg("eco", out Vec2 praca);
		ServerPlayer comum = ForjarSg("bancada: o Majin comum do eco", praca + new Vec2(0, 2 * ZoneCollision.TileSize), 1_000_000, "Majin", "Majin");

		double Ganho(ServerPlayer pl)
		{
			double antes = pl.Forma.Maestria.De(Catalogo.IdDaFormaPura);
			SubirMaestriaDaZona(pl, Catalogo.IdDaFormaPura, 1.0, 1.0, out _);
			return pl.Forma.Maestria.De(Catalogo.IdDaFormaPura) - antes;
		}
		double doMajin = Ganho(majin), doComum = Ganho(comum);
		AfirmarSg("a maestria de forma do Corrupted Majin com a Pura sobe 80% do que a de um corpo comum",
				  doComum > 0 && Math.Abs(doMajin / doComum - 0.8) < 1e-9, $"{doMajin:0.####} contra {doComum:0.####}");
		EcoSemFatorDeTeste = true;
		double semEco;
		try { semEco = Ganho(majin); }
		finally { EcoSemFatorDeTeste = false; }
		AfirmarSg("(defeito injetado: sem o eco) a maestria sobe igual a de todo mundo -- a regua de cima REPROVA",
				  Math.Abs(semEco - doComum) < 1e-9);
		TickDaLoucuraDoMajin(majin, 0.01);
		AfirmarSg("...e a exp dos niveis de skill entra a 80% tambem", Math.Abs(majin.Niveis.FatorDeGanho - 0.8) < 1e-9 && comum.Niveis.FatorDeGanho == 1);
	}

	// =====================================================================
	// 14. AS CLASSES RARAS
	// =====================================================================
	private void AsClassesRarasSemDisciplina()
	{
		GD.Print("[sagamajin] --- 14. as classes raras nao aprendem o Ultra Instinto nem o Ultra Ego ---");
		Vec2 praca = PracaDaSaga();
		foreach ((string raca, string classe, bool rara) in new[]
		{
			("Majin", "Corrupted Majin", true), ("Kai", "Golden Apple", true), ("Saiyan", "Legendary", true),
			("Saiyan", "Legendary Primal Saiyan", true), ("Icer", "Mutant Frost Demon", true),
			("Saiyan", "Elite", false), ("Human", "Normal", false),
		})
		{
			ServerPlayer pl = ForjarSg($"bancada: um {classe}", praca, 1_000_000, raca, classe);
			pl.Ficha.godki = new GodKiState { awakened = true, mastery = 90 };
			RecusaDeDisciplina ui = PodeAprender(pl, TipoDeDisciplina.UltraInstinct);
			RecusaDeDisciplina ue = PodeAprender(pl, TipoDeDisciplina.PoderDaDestruicao);
			bool linha = Catalogo.LinhaAberta(Perfil(pl), LinhaDeForma.UltraInstinct);
			AfirmarSg(rara
					  ? $"{classe}: recusa as duas disciplinas pela CLASSE, e a escada nao abre a linha do Ultra Instinto"
					  : $"{classe}: (controle) aprende -- o Elite e classe de linhagem, nao de lenda",
					  rara ? ui == RecusaDeDisciplina.ClasseRara && ue == RecusaDeDisciplina.ClasseRara && !linha
						   : ui == RecusaDeDisciplina.Pode && ue == RecusaDeDisciplina.Pode && linha,
					  $"{ui}/{ue}, linha {linha}");
		}

		// (2026-10-16) O TRONO NAO PASSA POR CIMA. O dono: *"se uma classe rara virar deus da destruiçao ele n ganha os paths nem nada, no
		// maximo a forma fury"* -- e, perguntado, so os caminhos: a Fury e o kit do titulo ficam. Um Corrupted Majin no trono do Deus e no do
		// Anjo nao ganha a disciplina de nenhum dos dois e nao a ENSINA; a Fury continua dele. O controle e um Humano comum no mesmo trono.
		string? deusAntes = _tronos.GetValueOrDefault(Jandirus.Core.Ranks.KitDoDeus.Cargo), anjoAntes = _tronos.GetValueOrDefault(Disciplinas.UltraInstinct.RankQueEnsina);
		try
		{
			ServerPlayer raro = ForjarSg("bancada: o Deus raro", praca, 1_000_000, "Majin", SagaDoMajin.Classe);
			ServerPlayer comum = ForjarSg("bancada: o Deus comum", praca + new Vec2(0, 2 * ZoneCollision.TileSize), 1_000_000, "Human", "Normal");
			(bool Ganha, bool Ensina) NoTrono(ServerPlayer pl, DisciplinaDef def)
			{
				_tronos[def.RankQueEnsina] = pl.Conta;
				ConcederADisciplinaDoCargo(pl);
				bool ganha = EstadoDe(pl, def.Tipo).Aprendida, ensina = SabeDisciplina(pl, def.Tipo);
				_tronos.Remove(def.RankQueEnsina);
				return (ganha, ensina);
			}
			var deus = NoTrono(raro, Disciplinas.PoderDaDestruicao);
			var anjo = NoTrono(raro, Disciplinas.UltraInstinct);
			_tronos[Jandirus.Core.Ranks.KitDoDeus.Cargo] = raro.Conta;
			bool fury = EntrarNaFury(raro, semCena: true);
			_tronos.Remove(Jandirus.Core.Ranks.KitDoDeus.Cargo);
			AfirmarSg("**A CLASSE RARA NO TRONO NAO GANHA O CAMINHO NEM O ENSINA** (*\"ele n ganha os paths nem nada\"*): Deus da Destruicao e Anjo "
					  + "-- e a FURY continua dela (*\"no maximo a forma fury\"*)",
					  !deus.Ganha && !deus.Ensina && !anjo.Ganha && !anjo.Ensina && fury,
					  $"Deus: ganha {deus.Ganha}, ensina {deus.Ensina}; Anjo: ganha {anjo.Ganha}, ensina {anjo.Ensina}; Fury {fury}");

			var deusComum = NoTrono(comum, Disciplinas.PoderDaDestruicao);
			AfirmarSg("(controle) o Humano comum no trono do Deus ganha o Poder da Destruicao e o ensina", deusComum.Ganha && deusComum.Ensina,
					  $"ganha {deusComum.Ganha}, ensina {deusComum.Ensina}");

			CargoEnsinaClasseRaraDeTeste = true;
			ServerPlayer raro2 = ForjarSg("bancada: o Deus raro de antes", praca, 1_000_000, "Majin", SagaDoMajin.Classe);
			var deAntes = NoTrono(raro2, Disciplinas.PoderDaDestruicao);
			AfirmarSg("(defeito injetado: o trono faz professor) o Deus raro ENSINA o caminho que nao pode ter -- a regua de cima REPROVA",
					  !deAntes.Ganha && deAntes.Ensina, $"ganha {deAntes.Ganha}, ensina {deAntes.Ensina}");
		}
		finally
		{
			CargoEnsinaClasseRaraDeTeste = false;
			if (deusAntes != null) _tronos[Jandirus.Core.Ranks.KitDoDeus.Cargo] = deusAntes; else _tronos.Remove(Jandirus.Core.Ranks.KitDoDeus.Cargo);
			string tronoDoAnjo = Disciplinas.UltraInstinct.RankQueEnsina;
			if (anjoAntes != null) _tronos[tronoDoAnjo] = anjoAntes; else _tronos.Remove(tronoDoAnjo);
		}
	}

	// =====================================================================
	// 15. SAIR E VOLTAR
	// =====================================================================
	private void SairEVoltar()
	{
		GD.Print("[sagamajin] --- 15. sair do jogo e voltar: cospe todo mundo sem custo, volta NA Forma Pura ---");
		ServerPlayer majin = MajinPuroSg("relog", out Vec2 praca);
		ServerPlayer preso = VitimaSg("o preso do relog", praca);
		ApertarAbsorverSg(majin);
		AfirmarSg("(controle) o preso esta dentro e o corpo saiu da Pura", DentroDeUmMajin(preso) && majin.Forma.Atual == Catalogo.IdBase);

		SairDoJogoSagaDoMajin(majin);
		DesfazerAAbsorcaoAoSair(majin);
		AfirmarSg("sair do jogo cospe o absorvido, SEM custo de sanidade (nao e fuga)",
				  !DentroDeUmMajin(preso) && majin.Ficha.majin_sanidade == 100, $"sanidade {majin.Ficha.majin_sanidade}");

		PrepararOMajinPuroNoLogin(majin);
		RestaurarFormaEDisciplina(majin, null);
		AfirmarSg("e o login o poe DIRETO na Forma Pura (*\"quando ele entrar no jogo novamente ele vai estar na pure form\"*)",
				  majin.Forma.Atual == Catalogo.IdDaFormaPura, majin.Forma.Atual);

		majin.Ficha.majin_sanidade = 0;
		PrepararOMajinPuroNoLogin(majin);
		RestaurarFormaEDisciplina(majin, null);
		AfirmarSg("quem saiu insano volta insano (a loucura foi pro disco)", majin.Forma.Atual == Catalogo.IdDaFormaPuraInsana, majin.Forma.Atual);

		// QUEM SAI DORMINDO ACORDA NA SAIDA.
		VivoDoMajin(majin).DormeAte = NowMs() + 60_000;
		SairDoJogoSagaDoMajin(majin);
		AfirmarSg("quem sai DORMINDO acorda na saida: a sanidade cheia e o Ki em 10% vao pro disco",
				  majin.Ficha.majin_sanidade == 100 && VivoDoMajin(majin).DormeAte == 0
				  && Math.Abs(majin.Ficha.Ki / majin.Ficha.MaxKi - 0.10) < 0.01);

		// A OUTRA METADE SAI COM O MESTRE.
		(ServerPlayer m2, Vec2 _) = MajinDaSaga("relog da metade");
		NaFormaSg(m2, 2);
		SoltarAOutraMetade(m2);
		int idDaMetade = OutraMetadeDe(m2.Id)?.Id ?? 0;
		SairDoJogoSagaDoMajin(m2);
		AfirmarSg("a outra metade sai do mundo com o mestre (e volta no login)", idDaMetade != 0 && !_players.ContainsKey(idDaMetade));
	}
}
