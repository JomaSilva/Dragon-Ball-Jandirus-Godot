using Godot;
using Jandirus.Core.Forms;

namespace Jandirus.Client;

// (2026-10-25) AS ESTREIAS DIVINAS NO TOCADOR -- a coluna de luz, o branco com a sombra do corpo, a galaxia e o fogo em volta (os bits
// `Efeito.ColunaDeLuz`, `SombraNoBranco`, `Galaxia` e `FogoEmVolta`; o pedido do dono e os instantes moram no Core, em
// `Cinematicas.EstreiaDivina`). Arquivo proprio pra o `Transformacao.cs` so ganhar as quatro chamadas: a do beat que acende
// (`Disparar`), a do quadro (`_Process`), a da virada (`Assumir`) e a de toda saida (`Soltar`).
//
// OS QUATRO SAO ESTADO, como a silhueta de luz do bio: o beat acende, o quadro anima, a virada os desfaz -- cada um com a sua cauda, dentro
// dos 3 s que a cena tem depois dela -- e o `Soltar` os arranca em todo caminho de saida. Uma cena interrompida no meio do branco nao pode
// deixar a tela branca, o painel escondido ou o corpo preto.
public partial class Transformacao
{
	/// <summary>O plano da galaxia e do fogo: atras do corpo e por cima do chao -- o -1 absoluto da chama da cena (ver o `_Ready`).</summary>
	private const int ZDasEstreiasDivinas = -1;

	/// <summary>
	/// (2026-10-25) O PLANO DA COLUNA E DO CONE: POR CIMA do corpo -- o dono: *"esse cone branco e roxo das transformacoes deveriam ser mais
	/// grossos e ficarem por cima do personagem e nao atras"*. Acima do corpo e da poeira da cena (100), e abaixo do branco do -Sign- (a
	/// cortina, `FundoDaEstreia.CamadaDaCortina`): no branco a coluna some debaixo dele, e a sombra do corpo (que sobe acima da cortina)
	/// continua a vista.
	/// </summary>
	private const int ZDaColuna = 101;

	/// <summary>A coluna: quanto leva pra subir dos pes ao alto da tela, e quanto leva pra se soltar do chao e se apagar depois da virada (s).</summary>
	internal const double ColunaSobe = 1.4, ColunaApaga = 1.6;

	/// <summary>
	/// A meia-largura do miolo da coluna (px do mundo): ao nascer, na virada, e quando ela engole o corpo (o branco). (2026-10-25, de novo, o
	/// dono com o print do cone do Ultra Ego: *"o raio branco do UI na primeira vez q transforma deveria ser dessa grossura do print"*) a coluna
	/// nasce e fica na grossura do cone (<see cref="MeiaLarguraDoCone"/>) ate a virada, e so cresce quando engole o corpo -- era 10, 24 e 60
	/// (e antes, 4, 12 e 44).
	/// </summary>
	internal const float ColunaFina = MeiaLarguraDoCone, ColunaGrossa = MeiaLarguraDoCone, ColunaQueEngole = MeiaLarguraDoCone * 2.2f;

	/// <summary>(2026-10-25) A cor da coluna: branca inteira, o miolo e o halo -- o dono: *"so q totalmente branco"* (era a cor da forma).</summary>
	internal static readonly Color BrancoDaColuna = Colors.White;

	/// <summary>
	/// (2026-10-25) O CONE DENTRO DO FOGO do Ultra Ego -- o dono, apontando o anel roxo: *"nessa deveria ter um segundo cone dentro desse circulo
	/// de fogo roxo q sobe ate o topo da tela cobrindo o personagem"*. A meia-largura do miolo dele (px do mundo, dentro do anel de
	/// <see cref="RaioDoFogo"/>), quanto do miolo e branco (o resto e a cor da forma) e com que forca ele cobre o corpo. Ele nasce e se apaga
	/// com o fogo, e sobe no ritmo da coluna.
	///
	/// (2026-10-25, de novo, com dois prints) O BRILHO E A FORCA DA COLUNA -- o dono: *"deixa o primeiro com a mesma claridade e sem opacidade
	/// igual vc fez no segundo"* (o segundo era a coluna fina dos 12 s, que saiu): o miolo e branco (`BrancoDoCone` 1, era 0,3) e ele cobre o
	/// corpo sem transparencia (`AlfaDoCone` 1, era 0,85); a cor do Ultra Ego fica no halo.
	/// </summary>
	internal const float MeiaLarguraDoCone = RaioDoFogo * 0.75f, BrancoDoCone = 1f, AlfaDoCone = 1f;

	/// <summary>O quadro da coluna (px do mundo): largo pro halo da coluna que engole, alto pro alto da tela em qualquer zoom; e os pes nele.</summary>
	private static readonly Vector2 QuadroDaColuna = new(560f, 1500f);

	/// <summary>Quantos px do quadro da coluna ficam ABAIXO dos pes -- o chao aceso em volta deles.</summary>
	private const float ChaoDaColuna = 24f;

	/// <summary>O branco de quem vira: quanto leva pra engolir a tela, e quanto leva pra escoar depois da virada (s).</summary>
	internal const double BrancoEngole = 0.7, BrancoEscoa = 1.6;

	/// <summary>A galaxia: o raio cheio (px do mundo) e quanto ela leva pra abrir (s).</summary>
	internal const float RaioDaGalaxia = 150f;

	/// <inheritdoc cref="RaioDaGalaxia"/>
	internal const double GalaxiaAbre = 2.5;

	/// <summary>Voltas por segundo da galaxia aberta -- e quantas a mais ela ganha no fim da compressao.</summary>
	private const double GiroDaGalaxia = 0.04, GiroDaCompressao = 1.2;

	/// <summary>O fogo: o raio do anel no chao e a altura da chama cheia (px do mundo).</summary>
	internal const float RaioDoFogo = 54f, AlturaDoFogo = 72f;

	/// <summary>O fogo: quanto leva pra acender, e quanto leva pra se apagar depois da virada (s).</summary>
	internal const double FogoAcende = 2.0, FogoApaga = 1.5;

	private ColorRect? _coluna, _galaxia, _fogo, _branco, _cone;
	private ShaderMaterial? _matColuna, _matGalaxia, _matFogo, _matBranco, _matCone;

	/// <summary>O segundo da cena em que cada um acendeu (-1: nao acendeu), e o da virada (-1: ainda nao virou).</summary>
	private double _colunaDesde = -1, _sombraDesde = -1, _galaxiaDesde = -1, _fogoDesde = -1, _viradaDivinaEm = -1;

	private double _giroDaGalaxia;

	/// <summary>O corpo subiu pro plano do branco (so na tela de quem vira) -- e o plano em que ele estava, pra voltar.</summary>
	private bool _corpoNoBranco, _zRelativoAntesDoBranco;

	/// <inheritdoc cref="_corpoNoBranco"/>
	private int _zAntesDoBranco;

	private readonly List<CanvasLayer> _camadasDoBranco = [];
	private readonly List<CanvasModulate> _tintasDoBranco = [];

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestreiadivina`): o branco volta a abrir so na tela de quem vira -- a r1 deste agente, antes de o dono
	/// lembrar que a cinematica e do mundo. Lido por <see cref="VeOBranco"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool BrancoSoDeQuemViraDeTeste;

	/// <summary>DEFEITO INJETADO (bancada): o branco abre na tela de quem assiste a qualquer distancia. Lido por <see cref="VeOBranco"/>. Falso em jogo, sempre.</summary>
	internal static bool BrancoSemDistanciaDeTeste;

	/// <summary>
	/// QUEM VE O BRANCO: quem vira, e quem esta PERTO -- o dono (2026-10-25): *"lembrando q a cinematica dos ui e ue n sao efeitos locais mas
	/// server side tb, todos perto podem ver"*. Perto e o `view(src)` do DM, o mesmo com que a cena treme cheio (`Cinematicas.RaioDoTremorCheio`)
	/// e com que o `ui_grand_cinematic` narra a coluna (`UltraInstinct.dm:361`). Quem esta longe ve a coluna e a sombra, no mundo. Perguntado a
	/// cada quadro (`TocarAEstreiaDivina`): quem chega perto no meio do branco o ve, e quem se afasta sai dele.
	/// </summary>
	private bool VeOBranco()
	{
		if (_souEu) return true;
		if (BrancoSoDeQuemViraDeTeste) return false;
		if (BrancoSemDistanciaDeTeste) return true;
		return Mundo()?.PosicaoLocal is { } eu && IsInstanceValid(_alvo) && eu.DistanceTo(_alvo.GlobalPosition) <= Cinematicas.RaioDoTremorCheio;
	}

	/// <summary>O BEAT ACENDE -- ver o cabecalho do arquivo. Chamado do `Disparar`. Um segundo beat com a mesma bandeira nao faz nada.</summary>
	private void AcenderAEstreiaDivina(Beat b)
	{
		if (b.Faz.HasFlag(Efeito.ColunaDeLuz) && _coluna == null)
		{
			_colunaDesde = _t;
			_coluna = QuadroDaEstreia("ColunaDeLuz", "ColunaDeLuz", QuadroDaColuna,
									  new Vector2(-QuadroDaColuna.X / 2, SpriteDeAura.LinhaDosPes + ChaoDaColuna - QuadroDaColuna.Y), out ShaderMaterial mc,
									  ZDaColuna);
			mc.SetShaderParameter("lado", QuadroDaColuna);
			mc.SetShaderParameter("pe", QuadroDaColuna.Y - ChaoDaColuna);
			// (2026-10-25) TODA BRANCA, o halo tambem -- o dono: *"so q totalmente branco"* (era a cor da forma: o halo saia prata-azulado)
			mc.SetShaderParameter("cor", BrancoDaColuna);
			_matColuna = mc;
		}

		// O CORPO PRETO E PRA TODO MUNDO (quem esta longe o ve dentro da coluna); O BRANCO, PRA QUEM VIRA E PRA QUEM ESTA PERTO -- ver
		// `Efeito.SombraNoBranco` e `VeOBranco`
		if (b.Faz.HasFlag(Efeito.SombraNoBranco) && _sombraDesde < 0)
		{
			_sombraDesde = _t;
			if (VeOBranco()) AbrirOBranco();
		}

		if (b.Faz.HasFlag(Efeito.Galaxia) && _galaxia == null)
		{
			_galaxiaDesde = _t;
			float lado = RaioDaGalaxia * 2 + 8;
			_galaxia = QuadroDaEstreia("Galaxia", "GalaxiaDoInstinto", new Vector2(lado, lado), new Vector2(-lado / 2, -lado / 2), out ShaderMaterial mg);
			mg.SetShaderParameter("lado", lado);
			_matGalaxia = mg;
		}

		if (b.Faz.HasFlag(Efeito.FogoEmVolta) && _fogo == null)
		{
			_fogoDesde = _t;
			// o quadro cobre o anel com folga dos lados, a chama do estouro (ela passa da cheia na virada) e o pedaco da frente do anel
			var quadro = new Vector2(RaioDoFogo * 2 + 40, AlturaDoFogo * 1.8f + 30);
			var pe = new Vector2(quadro.X / 2, quadro.Y - 30);
			_fogo = QuadroDaEstreia("FogoEmVolta", "FogoEmVolta", quadro, new Vector2(-pe.X, SpriteDeAura.LinhaDosPes - pe.Y), out ShaderMaterial mf);
			mf.SetShaderParameter("lado", quadro);
			mf.SetShaderParameter("pe", pe);
			mf.SetShaderParameter("anel", new Vector2(RaioDoFogo, RaioDoFogo * 0.36f));
			mf.SetShaderParameter("altura", AlturaDoFogo);
			mf.SetShaderParameter("cor", _corAura);
			_matFogo = mf;

			// O CONE DENTRO DO FOGO: o quadro da coluna, na cor da forma, da largura do anel e por cima do corpo -- ver `MeiaLarguraDoCone`
			_cone = QuadroDaEstreia("ConeDoFogo", "ColunaDeLuz", QuadroDaColuna,
									new Vector2(-QuadroDaColuna.X / 2, SpriteDeAura.LinhaDosPes + ChaoDaColuna - QuadroDaColuna.Y), out ShaderMaterial mk,
									ZDaColuna);
			mk.SetShaderParameter("lado", QuadroDaColuna);
			mk.SetShaderParameter("pe", QuadroDaColuna.Y - ChaoDaColuna);
			mk.SetShaderParameter("cor", _corAura);
			mk.SetShaderParameter("branco", BrancoDoCone);
			mk.SetShaderParameter("largura", MeiaLarguraDoCone);
			mk.SetShaderParameter("chao_aceso", 0.2f);
			_matCone = mk;
		}
	}

	/// <summary>Um quadro com o shader da estreia, filho desta cena -- anda com o corpo, como a chama dela -- e atras do corpo.</summary>
	private ColorRect QuadroDaEstreia(string nome, string shader, Vector2 tamanho, Vector2 canto, out ShaderMaterial mat,
									  int z = ZDasEstreiasDivinas)
	{
		mat = new ShaderMaterial { Shader = GD.Load<Shader>($"res://Assets/Shaders/{shader}.gdshader") };
		var q = new ColorRect
		{
			Name = nome,
			Size = tamanho,
			Position = canto,
			Color = Colors.White,
			Material = mat,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			LightMask = 0,   // a luz das tochas e dos tiros nao clareia luz nenhuma
			ZIndex = z,
			ZAsRelative = false,
		};
		AddChild(q);
		return q;
	}

	/// <summary>
	/// O BRANCO NA TELA (de quem vira, e de quem esta perto -- `VeOBranco`): o quadro branco do tamanho de qualquer tela no plano da cortina do
	/// fundo do SSJ2 (acima do mundo inteiro), o corpo de quem vira por cima dele -- e e ele que fica preto, pelo quadro (`TocarAEstreiaDivina`)
	/// --, e as camadas de tela e a tinta do ceu escondidas: *"aparece so a silhueta do corpo"*. As mesmas tres pecas do `FundoDaEstreia`, e a
	/// volta pelo mesmo caminho.
	/// </summary>
	private void AbrirOBranco()
	{
		if (_branco != null || !IsInstanceValid(_alvo)) return;
		const float lado = 8000f;   // a tela inteira em qualquer zoom: o quadro anda com o corpo
		_branco = QuadroDaEstreia("BrancoDaEstreia", "BrancoDaSombra", new Vector2(lado, lado), new Vector2(-lado / 2, -lado / 2), out ShaderMaterial mb,
								  FundoDaEstreia.CamadaDaCortina);
		mb.SetShaderParameter("lado", lado);
		mb.SetShaderParameter("alfa", 0f);
		_matBranco = mb;
		_zAntesDoBranco = _alvo.ZIndex;
		_zRelativoAntesDoBranco = _alvo.ZAsRelative;
		_alvo.ZIndex = FundoDaEstreia.CamadaDoCorpo;
		_alvo.ZAsRelative = false;
		_corpoNoBranco = true;
		foreach (Node n in GetTree().Root.FindChildren("*", nameof(CanvasLayer), true, false))
			if (n is CanvasLayer c && c.Visible && !IsAncestorOf(c)) { c.Visible = false; _camadasDoBranco.Add(c); }
		foreach (Node n in GetTree().Root.FindChildren("*", nameof(CanvasModulate), true, false))
			if (n is CanvasModulate m && m.Visible) { m.Visible = false; _tintasDoBranco.Add(m); }
	}

	/// <summary>O branco sai: o corpo volta ao plano dele, e o que sumiu volta como estava. Idempotente.</summary>
	private void FecharOBranco()
	{
		if (_branco != null && IsInstanceValid(_branco)) _branco.QueueFree();
		_branco = null;
		_matBranco = null;
		if (_corpoNoBranco && IsInstanceValid(_alvo))
		{
			_alvo.ZIndex = _zAntesDoBranco;
			_alvo.ZAsRelative = _zRelativoAntesDoBranco;
		}
		_corpoNoBranco = false;
		foreach (CanvasLayer c in _camadasDoBranco) if (IsInstanceValid(c)) c.Visible = true;
		foreach (CanvasModulate m in _tintasDoBranco) if (IsInstanceValid(m)) m.Visible = true;
		_camadasDoBranco.Clear();
		_tintasDoBranco.Clear();
	}

	/// <summary>
	/// O QUADRO: cada um anda pelo relogio da cena -- a coluna sobe e engrossa ate a virada, o corpo fica preto (e o branco cobre a tela de
	/// quem vira), a galaxia abre, gira e comprime nos ultimos segundos antes da virada, o fogo acende e cresce -- e, depois da virada, cada
	/// um faz a sua cauda. Chamado do `_Process`, depois dos beats (o beat que acende pinta no proprio quadro).
	/// </summary>
	private void TocarAEstreiaDivina(double delta)
	{
		if (_colunaDesde < 0 && _sombraDesde < 0 && _galaxiaDesde < 0 && _fogoDesde < 0) return;
		double virada = _cena.SegundosAteAVirada;
		double depois = _viradaDivinaEm < 0 ? -1 : _t - _viradaDivinaEm;   // segundos desde a virada; -1 antes dela

		// ---- a coluna: sobe dos pes, engrossa ate a virada (e engole o corpo com o branco), e na cauda se solta do chao e se apaga ----
		if (_matColuna != null)
		{
			double desde = _t - _colunaDesde;
			float subiu = (float)Math.Clamp(desde / ColunaSobe, 0, 1);
			subiu = subiu * subiu * (3 - 2 * subiu);
			float largura = Mathf.Lerp(ColunaFina, ColunaGrossa, (float)Math.Clamp(desde / Math.Max(virada - _colunaDesde, 0.01), 0, 1));
			if (_sombraDesde >= 0)
				largura = Mathf.Lerp(largura, ColunaQueEngole, (float)Math.Clamp((_t - _sombraDesde) / BrancoEngole, 0, 1));
			float alfa = Math.Min(1f, (float)desde * 4f), solta = 0f;
			if (depois >= 0)
			{
				float k = (float)Math.Clamp(depois / ColunaApaga, 0, 1);
				alfa *= 1f - k;
				solta = k * k * 600f;
			}
			_matColuna.SetShaderParameter("altura", subiu * (QuadroDaColuna.Y - ChaoDaColuna));
			_matColuna.SetShaderParameter("largura", largura);
			_matColuna.SetShaderParameter("base", solta);
			_matColuna.SetShaderParameter("alfa", alfa);
			if (depois >= ColunaApaga) Arrancar(ref _coluna, ref _matColuna);
		}

		// ---- a sombra: o corpo preto ate a virada (pra todo mundo), e o branco na tela de quem vira e de quem esta perto ----
		if (_sombraDesde >= 0)
		{
			// (o branco segue quem esta perto, quadro a quadro: chegou perto no meio dele, ve; se afastou, sai -- ver `VeOBranco`)
			if (depois < 0)
			{
				bool ve = VeOBranco();
				if (ve && _branco == null) AbrirOBranco();
				else if (!ve && _branco != null) FecharOBranco();
			}
			// O PRETO SEGURADO NO PICO: o `Banhar` escoa linear a partir do pico, e um prazo longo reescrito todo quadro e o preto chapado
			// parado. Quem o solta e a virada (`VirarAEstreiaDivina`), com o relogio do branco.
			if (depois < 0 && IsInstanceValid(_alvo))
				_alvo.GetNodeOrNull<CharacterVisual>("Visual")?.Banhar(Colors.Black, 30, lavagem: 1f);
			if (_matBranco != null)
			{
				float a = depois < 0 ? (float)Math.Clamp((_t - _sombraDesde) / BrancoEngole, 0, 1)
									 : 1f - (float)Math.Clamp(depois / BrancoEscoa, 0, 1);
				_matBranco.SetShaderParameter("alfa", a);
				if (depois >= BrancoEscoa) FecharOBranco();
			}
		}

		// ---- a galaxia: abre, gira, e comprime no corpo nos ultimos segundos antes da virada ----
		if (_matGalaxia != null)
		{
			double desde = _t - _galaxiaDesde;
			float abre = (float)Math.Clamp(desde / GalaxiaAbre, 0, 1);
			abre = 1f - (1f - abre) * (1f - abre);   // sai rapido e assenta
			double falta = virada - _t;
			float comprime = (float)Math.Clamp(falta / Cinematicas.SegundosDaCompressaoDaGalaxia, 0, 1);
			_giroDaGalaxia += delta * Math.Tau * (GiroDaGalaxia + (1 - comprime) * GiroDaCompressao);
			_matGalaxia.SetShaderParameter("raio", RaioDaGalaxia * abre * comprime * comprime);
			_matGalaxia.SetShaderParameter("giro", (float)_giroDaGalaxia);
			_matGalaxia.SetShaderParameter("miolo", 1f - comprime);
			if (depois >= 0 || falta <= 0) Arrancar(ref _galaxia, ref _matGalaxia);
		}

		// ---- o fogo: acende, cresce ate a virada, e na virada estoura e se apaga ----
		if (_matFogo != null)
		{
			double desde = _t - _fogoDesde;
			float forca = (float)(Math.Clamp(desde / FogoAcende, 0, 1) * (0.7 + 0.3 * Math.Clamp(desde / Math.Max(virada - _fogoDesde, 0.01), 0, 1)));
			float alfa = 1f;
			if (depois >= 0)
			{
				float k = (float)Math.Clamp(depois / FogoApaga, 0, 1);
				forca = 1f + 0.35f * (1f - k);
				alfa = 1f - k;
			}
			_matFogo.SetShaderParameter("forca", forca);
			_matFogo.SetShaderParameter("alfa", alfa);

			// o cone sobe no ritmo da coluna e se apaga com o fogo
			if (_matCone != null)
			{
				float sobe = (float)Math.Clamp(desde / ColunaSobe, 0, 1);
				sobe = sobe * sobe * (3 - 2 * sobe);
				_matCone.SetShaderParameter("altura", sobe * (QuadroDaColuna.Y - ChaoDaColuna));
				_matCone.SetShaderParameter("alfa", AlfaDoCone * Math.Min(1f, (float)desde * 2f) * alfa);
			}
			if (depois >= FogoApaga)
			{
				Arrancar(ref _fogo, ref _matFogo);
				Arrancar(ref _cone, ref _matCone);
			}
		}
	}

	private static void Arrancar(ref ColorRect? quadro, ref ShaderMaterial? mat)
	{
		if (quadro != null && IsInstanceValid(quadro)) quadro.QueueFree();
		quadro = null;
		mat = null;
	}

	/// <summary>
	/// A VIRADA: cada um comeca a sua cauda (ver o `TocarAEstreiaDivina`) -- e o corpo preto escoa no relogio do branco, entao o corpo
	/// aparece na forma enquanto a tela volta. Chamado do `Assumir`, DEPOIS de a forma vestir o corpo: o preto escoa por cima dela.
	/// </summary>
	private void VirarAEstreiaDivina()
	{
		if (_colunaDesde < 0 && _sombraDesde < 0 && _galaxiaDesde < 0 && _fogoDesde < 0) return;
		_viradaDivinaEm = _t;
		if (_sombraDesde >= 0 && IsInstanceValid(_alvo))
			_alvo.GetNodeOrNull<CharacterVisual>("Visual")?.Banhar(Colors.Black, BrancoEscoa, lavagem: 1f);
	}

	/// <summary>
	/// TODA SAIDA ARRANCA OS QUATRO -- o fim bom (as caudas ja acabaram e isto e formalidade) e os outros tres (o teto, o alvo sumindo, o
	/// `_ExitTree`). O preto que ainda estava segurado sai num instante, e nao nos 30 s do prazo dele.
	/// </summary>
	private void ApagarAEstreiaDivina()
	{
		bool corpoPreto = _sombraDesde >= 0 && _viradaDivinaEm < 0;
		FecharOBranco();
		Arrancar(ref _coluna, ref _matColuna);
		Arrancar(ref _galaxia, ref _matGalaxia);
		Arrancar(ref _fogo, ref _matFogo);
		Arrancar(ref _cone, ref _matCone);
		if (corpoPreto && IsInstanceValid(_alvo))
			_alvo.GetNodeOrNull<CharacterVisual>("Visual")?.Banhar(Colors.Black, 0.15, lavagem: 1f);
		_colunaDesde = _sombraDesde = _galaxiaDesde = _fogoDesde = -1;
	}

	/// <summary>Pra bancada: o que da estreia divina esta aceso agora nesta cena -- a coluna, o branco (nesta tela), a galaxia, o fogo e o cone.</summary>
	public (bool Coluna, bool Branco, bool Galaxia, bool Fogo, bool Cone) EstreiaDivinaDeTeste =>
		(_coluna != null && IsInstanceValid(_coluna), _branco != null && IsInstanceValid(_branco),
		 _galaxia != null && IsInstanceValid(_galaxia), _fogo != null && IsInstanceValid(_fogo), _cone != null && IsInstanceValid(_cone));

	/// <summary>Pra bancada: o raio da galaxia agora (px do mundo), e a meia-largura do miolo da coluna e do cone.</summary>
	public (float Galaxia, float Coluna, float Cone) MedidasDaEstreiaDeTeste =>
		(_matGalaxia?.GetShaderParameter("raio").AsSingle() ?? 0f, _matColuna?.GetShaderParameter("largura").AsSingle() ?? 0f,
		 _matCone?.GetShaderParameter("largura").AsSingle() ?? 0f);

	/// <summary>(2026-10-25) Pra bancada: a cor da coluna, e o branco e a forca (alfa) que o cone tem agora.</summary>
	public (Color Coluna, float BrancoDoCone, float AlfaDoCone) TintasDaEstreiaDeTeste =>
		(_matColuna?.GetShaderParameter("cor").AsColor() ?? Colors.Transparent,
		 _matCone?.GetShaderParameter("branco").AsSingle() ?? 0f, _matCone?.GetShaderParameter("alfa").AsSingle() ?? 0f);

	/// <summary>
	/// Pra bancada: o plano em que o Godot desenha a coluna, o cone e o corpo (o `ZIndex` somado pelos pais relativos) -- `int.MinValue` pro
	/// que nao esta aceso.
	/// </summary>
	public (int Coluna, int Cone, int Corpo) PlanosDaEstreiaDeTeste =>
		(_coluna != null && IsInstanceValid(_coluna) ? ZEfetivo(_coluna) : int.MinValue,
		 _cone != null && IsInstanceValid(_cone) ? ZEfetivo(_cone) : int.MinValue,
		 IsInstanceValid(_alvo) ? ZEfetivo(_alvo) : int.MinValue);

	private static int ZEfetivo(CanvasItem c)
	{
		int z = 0;
		for (Node? n = c; n is CanvasItem ci; n = n.GetParent())
		{
			z += ci.ZIndex;
			if (!ci.ZAsRelative) break;
		}
		return z;
	}

	/// <summary>Pra bancada: quantas camadas de tela o branco escondeu.</summary>
	public int CamadasNoBrancoDeTeste => _camadasDoBranco.Count;
}
