using Godot;
using Jandirus.Core.Forms;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// `--diagestreiadivina`: AS ESTREIAS DIVINAS NA TELA (2026-10-25). O dono, com quatro imagens: *"faça a cinematica da primeira vez
/// transformando em UI pressagio(sign) ter esse clarao subindo do personagem(imagem 1) , e aparece so a silhueta do corpo como uma
/// sombra(imagem 2) , e no final aparece ele transformado no UI sign. e pro perfect UI aparece essa galaxia (terceira imagem) em volta do
/// personagem e no final ela comprime nele causando um clarao (mesmo efeito de transformar instantaneamente com o personagem ficando todo
/// branco) e lentamente esse efeito vai saindo e mostrando o personagem no perfect ultra instinct. ultra ego vai ter um clarao parecido com
/// do UI so q roxo com uns efeitos q parecem fogo em volta roxo"*.
///
/// O robo e o jogador do host, e as tres estreias tocam no corpo dele pelo tocador de verdade (`Transformacao.Rodar`, com a cena que o
/// funil `Cinematicas.NoDegrau` entrega na estreia), no relogio de verdade, com fotos nos instantes que importam:
///   E0. o roteiro: as tres estreias sao as cenas do DM com os efeitos novos nos instantes certos -- o mesmo relogio, a mesma musica, a
///       mesma virada --, e nenhuma outra cena de forma (nem a encurtada delas) os acende; contra-exemplo `EstreiasDivinasDeAntesDeTeste`;
///   E1. o -Sign- na tela de quem vira: a coluna sobe do corpo e engrossa; aos 12 s o branco cobre a tela (o painel some, o corpo sobe de
///       plano e fica preto); na virada o branco e o preto escoam e o corpo aparece no -Sign-, com tudo devolvido;
///   E2. o -Sign- visto por quem esta PERTO: o mesmo branco e a mesma sombra (o dono: a cinematica e do mundo, e todos perto veem) -- e por
///       quem esta LONGE: so a coluna, sem branco (contra-exemplos `BrancoSoDeQuemViraDeTeste` e `BrancoSemDistanciaDeTeste`);
///   E3. a cena interrompida no meio do branco devolve tudo: o corpo sai do preto, e o painel, a tinta do ceu e o plano do corpo voltam;
///   E4. o Perfected: a galaxia abre em volta do corpo e comprime ate sumir na virada, quando o corpo fica branco -- e o branco escoa devagar;
///   E5. o Ultra Ego: o fogo roxo em volta, a coluna roxa aos 12 s, e os dois se apagam depois da virada.
/// As fotos (`user://estreia-*.png`) sao medidas contra a foto de antes do efeito, no mesmo ponto da tela: o chao muda de lugar pra lugar,
/// e o que se pergunta e o que o efeito MUDOU nele.
///
///     Godot --janela -- --host --rede 11310 --campoteste 12 --horateste 0.5 --semfauna --diagestreiadivina --raca Human --conta bancada_estreia --nome Estreia
/// </summary>
public partial class RoboDaEstreiaDivina : Node
{
	private int _ok, _falha, _fotos;
	private readonly List<string> _reprovadas = [];
	private Transformacao? _cena;

	/// <summary>O corpo de quem vira LONGE de quem joga (E2c): um node no mundo, a trinta tiles -- o branco so pergunta a distancia.</summary>
	private Node2D? _longe;

	private static void Nota(string s) => GD.Print("[destreia] " + s);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK     " : "  FALHA  ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	private static GameClient? C => GameClient.Instance;
	private static bool SemJanela => DisplayServer.GetName() == "headless";

	public override void _Ready() => _ = Rodar();

	/// <summary>Uma excecao no roteiro e um vermelho com placar, e nao uma bancada muda ate o prazo.</summary>
	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e)
		{
			Checa("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			Nota(e.ToString());
			Encerrar(3);
		}
	}

	private void Encerrar(int codigo)
	{
		Cinematicas.EstreiasDivinasDeAntesDeTeste = false;
		Transformacao.BrancoSoDeQuemViraDeTeste = Transformacao.BrancoSemDistanciaDeTeste = false;
		Descartar(_cena);
		if (_longe != null && IsInstanceValid(_longe)) _longe.Free();
		World.Instancia?.PararDeTeste();
		Nota("==================================================================================");
		Nota($" PLACAR: {_ok} OK, {_falha} FALHA(S)");
		foreach (string r in _reprovadas) Nota("   FALHA  " + r);
		Nota(_falha == 0 ? "===== TUDO OK =====" : $"===== {_falha} FALHA(S) =====");
		GetTree().Quit(codigo);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async Tarefa Percurso()
	{
		Nota("==================================================================================");
		Nota(" AS ESTREIAS DIVINAS -- a coluna e a sombra do -Sign-, a galaxia do Perfected, o fogo roxo do Ultra Ego");
		Nota("==================================================================================");

		// ---- E0. o roteiro, no Core ----
		FormaDef sign = Catalogo.Def("ui_sign")!, perfeito = Catalogo.Def("ui_perfected")!, ego = Catalogo.Def("ultra_ego")!;
		Cinematica eSign = Cinematicas.NoDegrau(sign, DegrauDeCena.Estreia)!;
		Cinematica ePerf = Cinematicas.NoDegrau(perfeito, DegrauDeCena.Estreia)!;
		Cinematica eEgo = Cinematicas.NoDegrau(ego, DegrauDeCena.Estreia)!;
		Checa("E0. **AS TRES ESTREIAS SAO A CENA DO DM COM OS EFEITOS DO DONO POR CIMA**: o mesmo relogio, o mesmo corpo preso, a mesma musica "
			  + "e a mesma virada, e cada beat do DM continua la, com tudo o que fazia",
			  MesmaCena(eSign, Cinematicas.Para(sign)!) && MesmaCena(ePerf, Cinematicas.Para(perfeito)!) && MesmaCena(eEgo, Cinematicas.Para(ego)!),
			  $"-Sign- {eSign.Beats.Length} beats ({Cinematicas.Para(sign)!.Beats.Length} no DM), preso {eSign.SegundosPreso}; Perfected "
			+ $"{ePerf.Beats.Length}; Ultra Ego {eEgo.Beats.Length}");
		Checa("E0a. o -Sign-: a COLUNA DE LUZ nasce aos 2,0 s e o BRANCO COM A SOMBRA aos 12,0 s ('uma coluna de luz azul-prateada engole tudo'), "
			  + "um de cada, antes da virada",
			  Em(eSign, Efeito.ColunaDeLuz) == 2.0 && Em(eSign, Efeito.SombraNoBranco) == 12.0
			  && Quantos(eSign, Efeito.ColunaDeLuz) == 1 && Quantos(eSign, Efeito.SombraNoBranco) == 1 && eSign.SegundosAteAVirada == 22.0,
			  $"coluna {Em(eSign, Efeito.ColunaDeLuz)}, sombra {Em(eSign, Efeito.SombraNoBranco)}, virada {eSign.SegundosAteAVirada}");
		Checa("E0b. o Perfected: a GALAXIA abre aos 2,0 s, e o beat da virada lava o corpo de BRANCO (a `SilhuetaBranca` no beat que assume)",
			  Em(ePerf, Efeito.Galaxia) == 2.0 && Quantos(ePerf, Efeito.Galaxia) == 1
			  && ePerf.Beats.Any(b => b.Faz.HasFlag(Efeito.Assumir) && b.Faz.HasFlag(Efeito.SilhuetaBranca)),
			  $"galaxia {Em(ePerf, Efeito.Galaxia)}, branco {Em(ePerf, Efeito.SilhuetaBranca)}, virada {ePerf.SegundosAteAVirada}");
		Checa("E0c. o Ultra Ego: o FOGO EM VOLTA aos 4,0 s, e **SEM A SEGUNDA COLUNA** (o dono, 2026-10-25: 'n precisa de um segundo raio no ultra "
			  + "ego, so do primeiro') -- o raio dele e o cone de dentro do fogo",
			  Em(eEgo, Efeito.FogoEmVolta) == 4.0 && Quantos(eEgo, Efeito.ColunaDeLuz) == 0,
			  $"fogo {Em(eEgo, Efeito.FogoEmVolta)}, colunas {Quantos(eEgo, Efeito.ColunaDeLuz)}");
		const Efeito Novos = Efeito.ColunaDeLuz | Efeito.SombraNoBranco | Efeito.Galaxia | Efeito.FogoEmVolta;
		int outras = 0;
		foreach (FormaDef d in Catalogo.Todas)
			foreach (DegrauDeCena g in new[] { DegrauDeCena.Curta, DegrauDeCena.Estreia })
			{
				if (g == DegrauDeCena.Estreia && d.Id is "ui_sign" or "ui_perfected" or "ultra_ego") continue;
				if (Cinematicas.NoDegrau(d, g) is { } c && c.Beats.Any(b => (b.Faz & Novos) != 0)) outras++;
			}
		bool fusaoLimpa = !Cinematicas.Fusao.Beats.Any(b => (b.Faz & Novos) != 0);
		Checa("E0d. ...e SO as tres estreias: nenhuma outra cena de forma, nem a encurtada delas (nem a fusao), acende os quatro efeitos",
			  outras == 0 && fusaoLimpa, $"{outras} outra(s) cena(s)");
		// (2026-10-25) o dono: *"tira essa aura de carga q fica em volta deles no final da cinematica, e so ultra ego q cria cratera, ui e ui
		// perfeito nao criam"*
		bool chamaNaCurta = Cinematicas.Para(sign)!.Beats.Any(b => b.Faz.HasFlag(Efeito.AuraGrande));
		Checa("E0e. **SEM A AURA DE CARGA** (o dono: 'tira essa aura de carga q fica em volta deles no final da cinematica'): nenhuma das tres "
			  + "estreias acende a chama da cena -- e a cena do DM (a curta) continua com ela",
			  new[] { eSign, ePerf, eEgo }.All(e => Quantos(e, Efeito.AuraGrande) == 0) && chamaNaCurta,
			  $"-Sign- {Quantos(eSign, Efeito.AuraGrande)}, Perfected {Quantos(ePerf, Efeito.AuraGrande)}, Ultra Ego {Quantos(eEgo, Efeito.AuraGrande)}; "
			+ $"a curta {(chamaNaCurta ? "com" : "SEM")} ela");
		Cinematica[] curtas = [.. new[] { Cinematicas.NoDegrau(sign, DegrauDeCena.Curta), Cinematicas.NoDegrau(perfeito, DegrauDeCena.Curta) }
			.OfType<Cinematica>()];
		Checa("E0f. **SO O ULTRA EGO ABRE O CHAO** (o dono: 'so ultra ego q cria cratera, ui e ui perfeito nao criam'): nem cratera nem poeira no "
			  + "-Sign- e no Perfected (a estreia e a curta), e a cratera do Ultra Ego no beat da virada",
			  curtas.Length == 2 && new[] { eSign, ePerf }.Concat(curtas).All(c => Quantos(c, Efeito.Cratera) == 0 && Quantos(c, Efeito.Poeira) == 0)
			  && Quantos(eEgo, Efeito.Cratera) == 1 && eEgo.Beats.First(b => b.Faz.HasFlag(Efeito.Cratera)).Faz.HasFlag(Efeito.Assumir),
			  $"crateras: -Sign- {Quantos(eSign, Efeito.Cratera)}, Perfected {Quantos(ePerf, Efeito.Cratera)}, curtas "
			+ $"{string.Join("/", curtas.Select(c => Quantos(c, Efeito.Cratera)))}, Ultra Ego {Quantos(eEgo, Efeito.Cratera)}");
		Cinematicas.EstreiasDivinasDeAntesDeTeste = true;
		try
		{
			Checa("   (defeito injetado: `Cinematicas.EstreiasDivinasDeAntesDeTeste`, o jogo de antes) as tres estreias voltam a ser as cenas do DM -- "
				  + "as reguas do E0 REPROVAM",
				  ReferenceEquals(Cinematicas.NoDegrau(sign, DegrauDeCena.Estreia), Cinematicas.Para(sign))
				  && ReferenceEquals(Cinematicas.NoDegrau(perfeito, DegrauDeCena.Estreia), Cinematicas.Para(perfeito))
				  && ReferenceEquals(Cinematicas.NoDegrau(ego, DegrauDeCena.Estreia), Cinematicas.Para(ego)));
		}
		finally { Cinematicas.EstreiasDivinasDeAntesDeTeste = false; }

		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } c && c.Atributos.Raca is { Length: > 0 }
									  && World.Instancia != null && Hud.Instancia != null, 90);
		Checa("o mundo chegou (conexao, ficha lenta, painel de pe)", pronto);
		if (!pronto || C is not { } cli || World.Instancia is not { } mundo || Hud.Instancia is not { } hud) { Encerrar(2); return; }
		// O CEU LIMPO: as fotos medem o escuro da galaxia contra o chao, e o clima sorteado mudaria a regua de rodada pra rodada (a r3 caiu
		// numa TEMPESTADE ao meio-dia: o chao ja escuro, e nenhum ponto do anel mais escuro). O mesmo remedio do robo das subzonas.
		Jandirus.Server.GameServer.Instance?.ForcarClima(cli.Zone, Jandirus.Core.World.TipoDeClima.Limpo, 1200, 1, "robo da estreia divina");
		await Segundos(4.0);
		Node2D? corpo = mundo.CorpoDeTeste(cli.LocalId);
		CharacterVisual? vis = corpo?.GetNodeOrNull<CharacterVisual>("Visual");
		Checa("(montagem) o corpo do robo esta na tela, com o boneco", corpo != null && vis != null);
		if (corpo == null || vis == null) { Encerrar(1); return; }
		Node atores = corpo.GetParent();
		int zAntes = corpo.ZIndex;
		bool relativoAntes = corpo.ZAsRelative;
		var tintas = GetTree().Root.FindChildren("*", nameof(CanvasModulate), true, false).OfType<CanvasModulate>().Where(m => m.Visible).ToList();

		// ======================================================== E1. o -Sign-, na tela de quem vira
		Nota("---- E1. o -Sign- na tela de quem vira ----");
		int crateras0 = Crateras();
		_cena = Transformacao.Rodar(atores, corpo, sign, eSign, souEu: true);
		await AteOSegundo(1.5);
		Image? s0 = await Foto("sign-0-antes");
		var antes = _cena.EstreiaDivinaDeTeste;
		Checa("E1. antes do primeiro beat (1,5 s) nada da estreia esta aceso", !antes.Coluna && !antes.Branco && !antes.Galaxia && !antes.Fogo);
		await AteOSegundo(3.6);
		Image? s1 = await Foto("sign-1-a-coluna-sobe");
		Checa("E1a. **A COLUNA DE LUZ SOBE DO CORPO** (2,0 s, a primeira imagem do dono): acesa, ja no alto, e JA NA GROSSURA DO CONE do Ultra Ego "
			  + "(o dono, 2026-10-25: 'o raio branco do UI [...] deveria ser dessa grossura do print') -- e TODA BRANCA (a cor do quadro e o branco)",
			  _cena.EstreiaDivinaDeTeste.Coluna && Math.Abs(_cena.MedidasDaEstreiaDeTeste.Coluna - Transformacao.MeiaLarguraDoCone) < 0.5f
			  && _cena.TintasDaEstreiaDeTeste.Coluna.IsEqualApprox(Colors.White),
			  $"miolo {_cena.MedidasDaEstreiaDeTeste.Coluna:0.0} px (o cone tem {Transformacao.MeiaLarguraDoCone:0.0}); "
			+ $"cor #{_cena.TintasDaEstreiaDeTeste.Coluna.ToHtml(false)}");
		var planos = _cena.PlanosDaEstreiaDeTeste;
		Checa("E1j. **A COLUNA FICA POR CIMA DO CORPO** (o dono: 'esse cone branco e roxo [...] deveriam [...] ficarem por cima do personagem e nao "
			  + "atras'): o plano em que ela e desenhada passa o do corpo",
			  planos.Coluna > planos.Corpo, $"coluna {planos.Coluna}, corpo {planos.Corpo}");
		if (s0 != null && s1 != null)
		{
			Color m0 = Media(s0, Tela(corpo, 0, -6)), m1 = Media(s1, Tela(corpo, 0, -6));
			Checa("E1k. ...e na foto o meio do corpo virou LUZ: a coluna o cobre (antes, o corpo; agora, quase branco)",
				  Lum(m1) > 0.8f && Lum(m1) - Lum(m0) > 0.15f, $"{Txt(m0)} -> {Txt(m1)}");
		}
		if (s0 != null && s1 != null)
		{
			// (o lado a 190 px: com a coluna na grossura do cone, o halo dela ja chega aos 120 de antes)
			Vector2 acima = Tela(corpo, 0, -90), dolado = Tela(corpo, 190, -60);
			Color a0 = Media(s0, acima), a1 = Media(s1, acima), l0 = Media(s0, dolado), l1 = Media(s1, dolado);
			Checa("E1b. ...e na foto: o chao acima do corpo virou LUZ (clareou e ficou quase branco), e o chao do lado nao mudou",
				  Lum(a1) - Lum(a0) > 0.12f && Lum(a1) > 0.75f && Math.Abs(Lum(l1) - Lum(l0)) < 0.08f,
				  $"acima {Txt(a0)} -> {Txt(a1)}; do lado {Txt(l0)} -> {Txt(l1)}");
		}
		await AteOSegundo(11.0);
		await Foto("sign-2-a-coluna-engrossa");
		float larg11 = _cena.MedidasDaEstreiaDeTeste.Coluna;
		Checa("E1c. ...e FICA NA GROSSURA DO CONE a subida inteira (o dono, 2026-10-25): aos 11 s o miolo continua com a meia-largura do cone do "
			  + "Ultra Ego -- bem mais grossa que as de antes (7,6 e 24 px)",
			  Math.Abs(larg11 - Transformacao.MeiaLarguraDoCone) < 0.5f && larg11 > 30f, $"{larg11:0.0} px");
		await AteOSegundo(13.2);
		Image? s3 = await Foto("sign-3-a-sombra-no-branco");
		var lav = vis.LavagemDeTeste;
		Checa("E1d. **AOS 12 s O BRANCO ENGOLE TUDO E DO CORPO SO FICA A SOMBRA** (a segunda imagem): o branco aberto, o painel e as outras "
			  + "camadas de tela escondidos, o corpo por cima dele, chapado de PRETO",
			  _cena.EstreiaDivinaDeTeste.Branco && !hud.Visible && _cena.CamadasNoBrancoDeTeste > 0
			  && corpo.ZIndex == FundoDaEstreia.CamadaDoCorpo && !corpo.ZAsRelative && lav is { Mistura: > 0.97f } l && l.Cor.V < 0.05f,
			  $"branco {_cena.EstreiaDivinaDeTeste.Branco}, painel {(hud.Visible ? "a vista" : "escondido")}, {_cena.CamadasNoBrancoDeTeste} camada(s), "
			+ $"plano {corpo.ZIndex}, lavagem {lav?.Mistura:0.00} #{lav?.Cor.ToHtml(false)}");
		if (s3 != null)
			Checa("E1e. ...e na foto: os quatro cantos BRANCOS, e o meio do corpo ESCURO",
				  Branco(Media(s3, new Vector2(0.05f, 0.08f))) && Branco(Media(s3, new Vector2(0.95f, 0.08f)))
				  && Branco(Media(s3, new Vector2(0.05f, 0.92f))) && Branco(Media(s3, new Vector2(0.95f, 0.92f)))
				  && Lum(MaisEscuro(s3, Tela(corpo, 0, 2))) < 0.2f,
				  $"cantos {Txt(Media(s3, new Vector2(0.05f, 0.08f)))} {Txt(Media(s3, new Vector2(0.95f, 0.92f)))}; corpo {Txt(MaisEscuro(s3, Tela(corpo, 0, 2)))}");
		await AteOSegundo(20.0);
		await Foto("sign-4-ainda-a-sombra");
		Checa("E1f. ...e o preto SEGURA ate a virada: aos 20 s o corpo continua chapado", vis.LavagemDeTeste is { Mistura: > 0.97f },
			  $"{vis.LavagemDeTeste?.Mistura:0.00}");
		await AteOSegundo(22.7);
		await Foto("sign-5-a-forma-aparece");
		await AteOSegundo(24.5);
		Image? s6 = await Foto("sign-6-o-sign");
		var fimSign = _cena.EstreiaDivinaDeTeste;
		Checa("E1g. **NO FIM ELE APARECE TRANSFORMADO** (a virada aos 22 s): o branco e a coluna sairam, o painel, a tinta do ceu e o plano do "
			  + "corpo voltaram, e o corpo nao esta mais preto",
			  !fimSign.Branco && !fimSign.Coluna && hud.Visible && tintas.All(m => m.Visible) && corpo.ZIndex == zAntes && corpo.ZAsRelative == relativoAntes
			  && vis.LavagemDeTeste is { Mistura: < 0.05f },
			  $"branco {fimSign.Branco}, coluna {fimSign.Coluna}, painel {hud.Visible}, plano {corpo.ZIndex} (era {zAntes}), lavagem {vis.LavagemDeTeste?.Mistura:0.00}");
		if (s0 != null && s6 != null)
			Checa("E1h. ...e na foto o mundo voltou: o canto de baixo (o da direita, sem painel) e o de antes, e nao branco",
				  !Branco(Media(s6, CantoLivre)) && Math.Abs(Lum(Media(s6, CantoLivre)) - Lum(Media(s0, CantoLivre))) < 0.1f,
				  $"{Txt(Media(s0, CantoLivre))} -> {Txt(Media(s6, CantoLivre))}");
		bool acabou = await Ate(() => !IsInstanceValid(_cena) || !_cena.Rodando, 4);
		Checa("E1i. ...e a cena acaba sozinha, sem deixar nada pra tras", acabou && hud.Visible && corpo.ZIndex == zAntes);
		Descartar(_cena);
		_cena = null;
		await Segundos(0.5);

		// ======================================================== E2/E3. o -Sign- visto por quem esta perto, por quem esta longe, e a cena interrompida
		// (2026-10-25) O dono: *"lembrando q a cinematica dos ui e ue n sao efeitos locais mas server side tb, todos perto podem ver"*. A cena
		// e a do corpo do proprio robo tocada como se ele ASSISTISSE (`souEu: false`): ele esta a zero tiles dela -- perto.
		Nota("---- E2. o -Sign- visto por quem esta perto, e por quem esta longe ----");
		_cena = Transformacao.Rodar(atores, corpo, sign, eSign, souEu: false);
		await AteOSegundo(13.4);
		Image? v1 = await Foto("sign-7-visto-de-perto");
		var dePerto = _cena.EstreiaDivinaDeTeste;
		Checa("E2. **QUEM ESTA PERTO VE O MESMO BRANCO E A MESMA SOMBRA** (o dono: 'todos perto podem ver'): na tela de quem assiste a zero tiles o "
			  + "branco abre, o painel some, o corpo de quem vira sobe por cima, preto -- e a coluna engole o corpo",
			  dePerto.Branco && dePerto.Coluna && !hud.Visible && corpo.ZIndex == FundoDaEstreia.CamadaDoCorpo
			  && vis.LavagemDeTeste is { Mistura: > 0.97f } l2 && l2.Cor.V < 0.05f && _cena.MedidasDaEstreiaDeTeste.Coluna > Transformacao.ColunaGrossa + 10,
			  $"branco {dePerto.Branco}, coluna {dePerto.Coluna} ({_cena.MedidasDaEstreiaDeTeste.Coluna:0} px), painel {hud.Visible}, plano {corpo.ZIndex}, "
			+ $"lavagem {vis.LavagemDeTeste?.Mistura:0.00}");
		if (v1 != null)
			Checa("E2b. ...e na foto de quem esta perto: os cantos brancos e o meio do corpo escuro",
				  Branco(Media(v1, new Vector2(0.05f, 0.08f))) && Branco(Media(v1, CantoLivre)) && Lum(MaisEscuro(v1, Tela(corpo, 0, 2))) < 0.2f,
				  $"cantos {Txt(Media(v1, new Vector2(0.05f, 0.08f)))} {Txt(Media(v1, CantoLivre))}; corpo {Txt(MaisEscuro(v1, Tela(corpo, 0, 2)))}");

		// ---- E3. interrompida no meio do branco: tudo volta ----
		Descartar(_cena);
		_cena = null;
		await Segundos(0.5);
		Checa("E3. **A CENA INTERROMPIDA NO MEIO DO BRANCO DEVOLVE TUDO**: o painel, a tinta do ceu e o plano do corpo voltaram, e o corpo saiu "
			  + "do preto em meio segundo (e nao nos 30 s do prazo dele)",
			  hud.Visible && tintas.All(m => m.Visible) && corpo.ZIndex == zAntes && corpo.ZAsRelative == relativoAntes && vis.LavagemDeTeste is { Mistura: < 0.05f },
			  $"painel {hud.Visible}, plano {corpo.ZIndex} (era {zAntes}), lavagem {vis.LavagemDeTeste?.Mistura:0.00}");

		// ---- E2c. quem esta LONGE: a trinta tiles, so a coluna -- o branco nao abre ----
		_longe = new Node2D { Name = "QuemViraLonge", Position = corpo.Position + new Vector2(30 * Jandirus.Core.World.ZoneCollision.TileSize, 0) };
		atores.AddChild(_longe);
		Transformacao AbreLonge() => Transformacao.Rodar(atores, _longe!, sign, eSign, souEu: false);
		_cena = AbreLonge();
		await AteOSegundo(13.0);
		var deLonge = _cena.EstreiaDivinaDeTeste;
		Checa("E2c. **QUEM ESTA LONGE NAO VE O BRANCO**: a trinta tiles a cena acende a coluna no mundo, e a tela de quem joga continua o jogo -- o "
			  + "painel a vista e o corpo dele no plano de sempre",
			  !deLonge.Branco && deLonge.Coluna && hud.Visible && corpo.ZIndex == zAntes,
			  $"branco {deLonge.Branco}, coluna {deLonge.Coluna}, painel {hud.Visible}, plano {corpo.ZIndex}");
		Descartar(_cena);
		_cena = null;

		// ---- os dois contra-exemplos ----
		Transformacao.BrancoSoDeQuemViraDeTeste = true;
		bool abriuPerto;
		try
		{
			_cena = Transformacao.Rodar(atores, corpo, sign, eSign, souEu: false);
			await AteOSegundo(12.9);
			abriuPerto = _cena.EstreiaDivinaDeTeste.Branco;
			Descartar(_cena);
			_cena = null;
		}
		finally { Transformacao.BrancoSoDeQuemViraDeTeste = false; }
		Checa("   (defeito injetado: `BrancoSoDeQuemViraDeTeste`, a r1) quem assiste de perto fica sem o branco -- a regua do E2 REPROVA", !abriuPerto);
		await Segundos(0.5);
		Transformacao.BrancoSemDistanciaDeTeste = true;
		bool abriuLonge;
		try
		{
			_cena = AbreLonge();
			await AteOSegundo(12.9);
			abriuLonge = _cena.EstreiaDivinaDeTeste.Branco;
			Descartar(_cena);
			_cena = null;
		}
		finally { Transformacao.BrancoSemDistanciaDeTeste = false; }
		Checa("   (defeito injetado: `BrancoSemDistanciaDeTeste`) quem esta a trinta tiles ganha o branco tambem -- a regua do E2c REPROVA", abriuLonge);
		_longe.Free();
		_longe = null;
		await Segundos(0.5);
		Checa("E3b. ...e depois de cada uma (e de cada contra-exemplo) a tela de quem joga e a de sempre", hud.Visible && corpo.ZIndex == zAntes
			  && tintas.All(m => m.Visible) && vis.LavagemDeTeste is { Mistura: < 0.05f });
		Checa("E3c. **O ULTRA INSTINTO NAO ABRE O CHAO** (o dono: 'ui e ui perfeito nao criam'): todas as cenas do -Sign- desta bancada viraram e "
			  + "nenhuma cratera nasceu no mundo", Crateras() == crateras0, $"{Crateras() - crateras0} cratera(s)");

		// ======================================================== E4. o Perfected
		Nota("---- E4. o Perfected ----");
		_cena = Transformacao.Rodar(atores, corpo, perfeito, ePerf, souEu: true);
		await AteOSegundo(1.5);
		Image? p0 = await Foto("perfeito-0-antes");
		await AteOSegundo(3.4);
		await Foto("perfeito-1-a-galaxia-abre");
		await AteOSegundo(10.0);
		Image? p2 = await Foto("perfeito-2-a-galaxia");
		float raio10 = _cena.MedidasDaEstreiaDeTeste.Galaxia;
		Checa("E4. **A GALAXIA EM VOLTA DO CORPO** (a terceira imagem): acesa e aberta no raio cheio", _cena.EstreiaDivinaDeTeste.Galaxia
			  && Math.Abs(raio10 - Transformacao.RaioDaGalaxia) < 1, $"raio {raio10:0} px");
		if (p0 != null && p2 != null)
		{
			// O ANEL a meia distancia, e nao um ponto so: os bracos da espiral giram, e um ponto so cai ora no gas escuro, ora no braco claro
			// (a r2 caiu no braco duas vezes: (84,122,26) -> (72,81,74), mais azul e quase nada mais escuro). No anel inteiro o chao fica
			// AZULADO em todo ponto, e ESCURO onde o braco nao passa. O quarto de baixo a esquerda fica de fora: e o chat.
			int pontos = 0, azulados = 0, escuros = 0;
			for (int k = 0; k < 24; k++)
			{
				float a = k * Mathf.Tau / 24;
				if (Mathf.Cos(a) < -0.01f && Mathf.Sin(a) > 0.01f) continue;
				Vector2 uv = Tela(corpo, 70 * Mathf.Cos(a), 70 * Mathf.Sin(a));
				Color c0 = Media(p0, uv), c2 = Media(p2, uv);
				bool azul = (c2.B - c2.R) - (c0.B - c0.R) > 0.03f;
				pontos++;
				if (azul) azulados++;
				if (azul && Lum(c0) - Lum(c2) > 0.12f) escuros++;
			}
			Vector2 canto = new(0.03f, 0.5f);
			Checa("E4b. ...e na foto o chao a meia distancia do corpo virou o escuro azulado dela (o anel todo mais azul, e escuro onde o braco "
				  + "nao passa), e o canto da tela nao mudou",
				  azulados * 4 >= pontos * 3 && escuros * 4 >= pontos && Math.Abs(Lum(Media(p2, canto)) - Lum(Media(p0, canto))) < 0.08f,
				  $"{azulados} de {pontos} pontos do anel mais azuis, {escuros} escuros; canto {Txt(Media(p0, canto))} -> {Txt(Media(p2, canto))}");
		}
		await AteOSegundo(21.5);
		await Foto("perfeito-3-comprimindo");
		float raio215 = _cena.MedidasDaEstreiaDeTeste.Galaxia;
		await AteOSegundo(21.95);
		float raio2195 = _cena.MedidasDaEstreiaDeTeste.Galaxia;
		Checa("E4c. **NO FIM ELA COMPRIME NO CORPO**: a meio segundo da virada o raio ja caiu pela metade, e a um vigesimo de segundo quase sumiu",
			  raio215 < Transformacao.RaioDaGalaxia * 0.5f && raio2195 < Transformacao.RaioDaGalaxia * 0.06f, $"{raio215:0} px e {raio2195:0} px");
		await AteOSegundo(22.12);
		await Foto("perfeito-4-o-clarao");
		var lp = vis.LavagemDeTeste;
		Checa("E4d. **...CAUSANDO UM CLARAO, COM O CORPO TODO BRANCO** (o mesmo canal da instantanea): na virada a galaxia sumiu e o corpo esta "
			  + "chapado de branco",
			  !_cena.EstreiaDivinaDeTeste.Galaxia && lp is { Mistura: > 0.85f } l3 && l3.Cor.R > 0.95f && l3.Cor.G > 0.95f && l3.Cor.B > 0.95f,
			  $"galaxia {_cena.EstreiaDivinaDeTeste.Galaxia}, lavagem {lp?.Mistura:0.00} #{lp?.Cor.ToHtml(false)}");
		await AteOSegundo(23.6);
		float noMeio = vis.LavagemDeTeste?.Mistura ?? -1;
		await Foto("perfeito-5-o-branco-escoa");
		await AteOSegundo(24.8);
		await Foto("perfeito-6-o-perfected");
		float noFim = vis.LavagemDeTeste?.Mistura ?? -1;
		Checa("E4e. **...E LENTAMENTE O BRANCO SAI**, mostrando o Perfected: ainda branco pela metade 1,6 s depois da virada, e quase limpo no fim da cena",
			  noMeio is > 0.2f and < 0.8f && noFim is >= 0 and < 0.15f, $"{noMeio:0.00} e {noFim:0.00}");
		await Ate(() => !IsInstanceValid(_cena) || !_cena.Rodando, 4);
		Descartar(_cena);
		_cena = null;
		Checa("E4f. ...e o Perfected tambem nao abre o chao: a virada dele nao plantou cratera", Crateras() == crateras0,
			  $"{Crateras() - crateras0} cratera(s)");
		await Segundos(0.5);

		// ======================================================== E5. o Ultra Ego
		Nota("---- E5. o Ultra Ego ----");
		_cena = Transformacao.Rodar(atores, corpo, ego, eEgo, souEu: true);
		await AteOSegundo(1.5);
		Image? u0 = await Foto("ego-0-antes");
		await AteOSegundo(7.0);
		Image? u1 = await Foto("ego-1-o-fogo");
		Checa("E5. **O FOGO ROXO EM VOLTA** (a quarta imagem): aceso desde os 4 s, e a coluna ainda nao", _cena.EstreiaDivinaDeTeste.Fogo
			  && !_cena.EstreiaDivinaDeTeste.Coluna);
		var planosDoFogo = _cena.PlanosDaEstreiaDeTeste;
		float cone = _cena.MedidasDaEstreiaDeTeste.Cone;
		Checa("E5x. **O CONE DENTRO DO FOGO** (o dono: 'deveria ter um segundo cone dentro desse circulo de fogo roxo q sobe ate o topo da tela "
			  + "cobrindo o personagem'): aceso com o fogo, mais largo que o corpo e dentro do anel, e POR CIMA do corpo",
			  _cena.EstreiaDivinaDeTeste.Cone && cone > 16f && cone < Transformacao.RaioDoFogo && planosDoFogo.Cone > planosDoFogo.Corpo,
			  $"meia-largura {cone:0} px (o anel tem {Transformacao.RaioDoFogo:0}), plano {planosDoFogo.Cone} contra o do corpo {planosDoFogo.Corpo}");
		var tintaDoCone = _cena.TintasDaEstreiaDeTeste;
		Checa("E5z. **...COM A CLARIDADE DA COLUNA E SEM TRANSPARENCIA** (o dono, 2026-10-25: 'deixa o primeiro com a mesma claridade e sem "
			  + "opacidade igual vc fez no segundo'): o miolo do cone e branco e ele cobre com a forca inteira",
			  tintaDoCone.BrancoDoCone > 0.99f && tintaDoCone.AlfaDoCone > 0.99f,
			  $"branco {tintaDoCone.BrancoDoCone:0.00}, alfa {tintaDoCone.AlfaDoCone:0.00}");
		if (u0 != null && u1 != null)
		{
			Color k0 = Media(u0, Tela(corpo, 0, -6)), k1 = Media(u1, Tela(corpo, 0, -6)), alto = Media(u1, new Vector2(Tela(corpo, 0, 0).X, 0.05f));
			// (o alto e o da FOTO, a 5% do topo e na coluna do corpo: 150 px de mundo acima dele caiam fora da tela com o zoom do robo, e a media de fora sai preta)
			Checa("E5y. ...e na foto o corpo esta COBERTO pela LUZ do cone (o meio dele virou quase branco: o brilho da coluna de antes), e o cone "
				  + "sobe ate o alto da tela, claro la em cima tambem",
				  Lum(k1) > 0.8f && Lum(k1) - Lum(k0) > 0.15f && Lum(alto) > 0.6f, $"meio {Txt(k0)} -> {Txt(k1)}; no alto {Txt(alto)}");
		}
		if (u0 != null && u1 != null)
		{
			Vector2 lado = Tela(corpo, Transformacao.RaioDoFogo * 0.9f, SpriteDeAura.LinhaDosPes - 20f);
			Color f0 = Media(u0, lado), f1 = Media(u1, lado);
			Checa("E5b. ...e na foto o chao do lado do corpo, no anel, ficou ROXO (o azul subiu acima do verde)",
				  (f1.B - f1.G) - (f0.B - f0.G) > 0.15f && f1.R > f1.G, $"{Txt(f0)} -> {Txt(f1)}");
		}
		await AteOSegundo(13.6);
		Image? u2 = await Foto("ego-2-o-cone");
		Checa("E5c. **NENHUMA SEGUNDA COLUNA IRROMPE** aos 12 s (o dono, 2026-10-25: 'tirar o segundo raio'): so o cone e o fogo seguem acesos",
			  !_cena.EstreiaDivinaDeTeste.Coluna && _cena.EstreiaDivinaDeTeste.Cone && _cena.EstreiaDivinaDeTeste.Fogo);
		if (u0 != null && u2 != null)
		{
			Vector2 acima = Tela(corpo, 0, -90);
			Color c0 = Media(u0, acima), c2 = Media(u2, acima);
			Checa("E5d. ...e na foto o chao acima do corpo segue aceso pela luz do cone (o azul nao fica abaixo do verde)",
				  Lum(c2) - Lum(c0) > 0.12f && c2.B >= c2.G, $"{Txt(c0)} -> {Txt(c2)}");
		}
		await AteOSegundo(20.0);
		await Foto("ego-3-o-ego");
		await AteOSegundo(22.6);
		await Foto("ego-4-o-estouro");
		await AteOSegundo(24.6);
		await Foto("ego-5-apagado");
		Checa("E5e. ...e depois da virada os tres se apagam (a coluna, o fogo e o cone)", !_cena.EstreiaDivinaDeTeste.Coluna && !_cena.EstreiaDivinaDeTeste.Fogo
			  && !_cena.EstreiaDivinaDeTeste.Cone);
		await Ate(() => !IsInstanceValid(_cena) || !_cena.Rodando, 4);
		Descartar(_cena);
		_cena = null;
		Checa("E5f. **SO O ULTRA EGO ABRE O CHAO**: a virada dele plantou a cratera", Crateras() > crateras0, $"{Crateras() - crateras0} cratera(s)");

		Encerrar(_falha == 0 ? 0 : 1);
	}

	// =====================================================================
	// OS AUXILIARES
	// =====================================================================
	/// <summary>A estreia e a cena do DM com coisas a mais: outra instancia, o mesmo relogio, e cada beat do DM la dentro, inteiro.</summary>
	private static bool MesmaCena(Cinematica e, Cinematica dm) =>
		!ReferenceEquals(e, dm) && e.Forma == dm.Forma && e.Musica == dm.Musica && e.Musica.Length > 0
		&& Math.Abs(e.SegundosPreso - dm.SegundosPreso) < 1e-9 && Math.Abs(e.Segundos - dm.Segundos) < 1e-9
		&& Math.Abs(e.SegundosAteAVirada - dm.SegundosAteAVirada) < 1e-9
		&& dm.Beats.All(b => e.Beats.Any(x => Math.Abs(x.Em - b.Em) < 1e-9 && (x.Faz & (b.Faz & ~Efeito.AuraGrande)) == (b.Faz & ~Efeito.AuraGrande)
											   && x.Fala == b.Fala && x.Narra == b.Narra && x.Som == b.Som));

	/// <summary>Quantas crateras o mundo desta tela ja plantou (as pequenas e as grandes).</summary>
	private static int Crateras() => Decalques.CraterasDeTeste + Decalques.CraterasGrandesDeTeste;

	/// <summary>O segundo do primeiro beat com esta bandeira, ou -1.</summary>
	private static double Em(Cinematica c, Efeito e) => c.Beats.FirstOrDefault(b => b.Faz.HasFlag(e)) is { Faz: var f } b0 && f.HasFlag(e) ? b0.Em : -1;

	private static int Quantos(Cinematica c, Efeito e) => c.Beats.Count(b => b.Faz.HasFlag(e));

	/// <summary>Espera a cena em curso chegar a este segundo (ou acabar).</summary>
	private async Tarefa AteOSegundo(double s) =>
		await Ate(() => _cena == null || !IsInstanceValid(_cena) || !_cena.Rodando || _cena.TempoDeTeste >= s, s + 6);

	/// <summary>Onde na foto (em fracao da tela) fica o ponto a (dx, dy) px do mundo do meio deste corpo -- pelo transform de tela dele.</summary>
	private Vector2 Tela(Node2D corpo, float dx, float dy)
	{
		Vector2 naTela = corpo.GetGlobalTransformWithCanvas() * new Vector2(dx, dy);
		Vector2 visivel = GetViewport().GetVisibleRect().Size;
		return new Vector2(naTela.X / visivel.X, naTela.Y / visivel.Y);
	}

	/// <summary>A media de um quadradinho de 9x9 pixels da foto: o chao tem textura, e um pixel so muda de foto pra foto com o tremor.</summary>
	private static Color Media(Image img, Vector2 uv, int r = 4)
	{
		int cx = (int)(uv.X * img.GetWidth()), cy = (int)(uv.Y * img.GetHeight());
		float sr = 0, sg = 0, sb = 0;
		int n = 0;
		for (int y = cy - r; y <= cy + r; y++)
			for (int x = cx - r; x <= cx + r; x++)
			{
				if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) continue;
				Color c = img.GetPixel(x, y);
				sr += c.R; sg += c.G; sb += c.B; n++;
			}
		return n == 0 ? Colors.Black : new Color(sr / n, sg / n, sb / n);
	}

	/// <summary>O pixel mais escuro de um quadradinho de 7x7 -- o corpo preto, onde quer que o tremor o tenha posto.</summary>
	private static Color MaisEscuro(Image img, Vector2 uv, int r = 3)
	{
		int cx = (int)(uv.X * img.GetWidth()), cy = (int)(uv.Y * img.GetHeight());
		Color melhor = Colors.White;
		for (int y = cy - r; y <= cy + r; y++)
			for (int x = cx - r; x <= cx + r; x++)
			{
				if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) continue;
				Color c = img.GetPixel(x, y);
				if (Lum(c) < Lum(melhor)) melhor = c;
			}
		return melhor;
	}

	/// <summary>O canto de baixo da DIREITA: o unico canto da tela sem painel (o de status e o mapa do corpo em cima, o chat embaixo a esquerda).</summary>
	private static readonly Vector2 CantoLivre = new(0.95f, 0.92f);

	private static float Lum(Color c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
	private static bool Branco(Color c) => c.R > 0.9f && c.G > 0.9f && c.B > 0.9f;
	private static string Txt(Color c) => $"({c.R8},{c.G8},{c.B8})";

	private static void Descartar(Transformacao? t)
	{
		if (t != null && IsInstanceValid(t)) t.Free();
	}

	/// <summary>A tela inteira em `user://estreia-NN-nome.png`, e a imagem pra medir. SO COM JANELA.</summary>
	private async System.Threading.Tasks.Task<Image?> Foto(string nome)
	{
		if (SemJanela) return null;
		await Quadros(1);
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) return null;
			string caminho = ProjectSettings.GlobalizePath($"user://estreia-{++_fotos:00}-{nome}.png");
			img.SavePng(caminho);
			Nota($"  foto: {caminho}");
			return img;
		}
		catch (Exception e) { Nota($"  (sem foto de '{nome}': {e.Message})"); return null; }
	}

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double segundos)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + segundos;
		while (Time.GetTicksMsec() / 1000.0 < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + segundos;
		while (Time.GetTicksMsec() / 1000.0 < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}
}
