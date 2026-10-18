using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA ESFERA VERDE DO DEMONIC SAIYAN (`--diagesfera`, v0.4.0). A lista do amigo do dono: *"Activating this skill creates a
/// green orb visual effect around the player, signaling the start of a charge-up"*.
///
/// Com janela e com `--host`: o servidor deste processo poe DOIS corpos no ponto de carregar (o MEU e um ao lado) e aperta a ativa pelo
/// canal de producao (`GameServer.CarregarODemonicoDeTeste`). O que se mede e a TELA, pelo caminho de producao -- o snapshot leva a
/// `Ocupacao.CargaDemoniaca` e o `World.MarcarEsferaDemoniaca` pendura a `EsferaDemoniaca` no corpo:
///
///   0. o ceu da zona fica LIMPO (`EmbateDeFoto_CeuLimpo`, o mesmo do palco do embate de ki) antes de tudo -- ver abaixo;
///   1. as duas cargas comecam (o servidor diz "fique PARADO");
///   2. a esfera acende no MEU corpo (o dono ve a propria carga) e no do outro (quem esta em volta ve) -- e ela e a barreira de energia
///      do jogo esverdeada, com a camera de quem esta perto tremendo (o dono, 2026-10-17); de NOITE ela brilha (a luz verde acesa, o
///      pedido seguinte do dono) e de dia a luz fica apagada -- foto `esfera-1-carregando-dia.png` / `-noite.png` (`--horateste`);
///   3. o outro ANDA: a carga dele se desfaz e a esfera dele apaga; a minha continua -- foto `esfera-2-o-outro-andou.png`;
///   4. CONTRA-EXEMPLO (`World.EsferaDemoniacaApagadaDeTeste`, a tela ignorando a carga): a minha esfera apaga com o servidor ainda
///      carregando -- a prova 2 reprovaria --, e sem esfera nenhuma a camera para de tremer;
///   5. E ELA SE VE, MEDIDO NO PIXEL: a mesma coroa em volta do meu corpo, na foto com ela e na foto sem ela (a do contra-exemplo) --
///      com ela, mais verde, descontado o que um pedaco de chao de CONTROLE mudou sozinho entre as duas fotos (<see cref="Controle"/>).
///      Comparar a mesma regiao nas duas fotos cancela tudo o que e parado (a grama, o boneco, o rabo); um ponto "dentro" contra um
///      ponto "fora" media a textura da grama, e foi o que a primeira regua fez.
///
///     Godot --path . --host --rede 8452 --campoteste 8 --horateste 0.5 --semfauna --diagesfera --raca Human --conta bancada_desfera --nome Demoniaco
///
/// ============================ O DIA E A NOITE DA FOTO SAO DE VERDADE ============================
/// As duas rodadas (`desfera` ao meio-dia, `desferanoite` a meia-noite) cairam em duas armadilhas:
///   * o `--horateste` acerta o relogio da TERRA, e um Saiyajin nasce em Vegeta, cuja hora muda de rodada pra rodada: a primeira
///     "meia-noite" foi uma tarde de chuva de sangue (o HUD da foto dizia), e o "meio-dia" foi de manha numa e quase meio-dia na
///     outra. As duas nascem HUMANAS (na Terra), e o gancho do servidor as veste de Lendario Primal do mesmo jeito;
///   * o CLIMA escurece a cena: uma nevasca ao meio-dia da Terra deixou a tela com forca da noite 0,70, e a esfera acendeu a luz --
///     em jogo isso esta CERTO (ela brilha em qualquer escuro, como as auras), mas a rodada de dia nao provou o dia. Dai o passo 0.
/// A conferencia "o ceu diz noite/dia, limpo, e a tela concorda" e a que pegou a nevasca, e fica: sem ela, uma rodada no lugar ou na
/// hora errada passa calada.
/// ===============================================================================================
/// </summary>
public partial class RoboDaEsferaDemoniaca : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private int _passo, _outro;
	private double _t, _espera, _limpoHa;

	/// <summary>As coroas da foto com a barreira acesa -- a do corpo e a de controle --, e onde foram medidas (a foto sem ela mede no MESMO lugar).</summary>
	private (double R, double G)? _coroaAcesa;
	private (double R, double G) _controleAceso;
	private Vector2 _centroDaFoto, _centroDoControle;
	private float _raioDaFoto;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Passar() { _passo++; _t = 0; }

	private static EsferaDemoniaca? Esfera(Node2D? corpo) => corpo?.GetNodeOrNull<EsferaDemoniaca>(EsferaDemoniaca.NomeDoNode);

	/// <summary>Acesa de verdade: pendurada, sem estar apagando, e ja inteira na tela.</summary>
	private static bool Acesa(Node2D? corpo) => Esfera(corpo) is { Apagando: false } e && e.AlfaDeTeste > 0.95;

	private static bool DeNoite => Iluminacao.ForcaDaNoite() > 0.5f;

	private static string Hora => DeNoite ? "noite" : "dia";

	private Image? Fotografar(string destino)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return null; }
		string caminho = ProjectSettings.GlobalizePath(destino);
		img.SavePng(caminho);
		_linhas.Add("  --     foto salva em " + caminho);
		return img;
	}

	/// <summary>
	/// O VERMELHO E O VERDE MEDIOS DE UMA COROA em volta do corpo: de 0,55 a 0,9 do raio da barreira -- fora do boneco (o quadro de 32 px)
	/// e dentro da casca. Um pixel sim, um nao: milhares de amostras, e o tremor de 3 px da camera some na media.
	/// </summary>
	private static (double R, double G) Coroa(Image img, Vector2 centro, float raio)
	{
		double r = 0, g = 0;
		int n = 0;
		for (int y = (int)(centro.Y - raio); y <= (int)(centro.Y + raio); y += 2)
			for (int x = (int)(centro.X - raio); x <= (int)(centro.X + raio); x += 2)
			{
				float d = new Vector2(x - centro.X, y - centro.Y).Length();
				if (d < raio * 0.55f || d > raio * 0.9f) continue;
				if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) continue;
				Color px = img.GetPixel(x, y);
				r += px.R;
				g += px.G;
				n++;
			}
		return n == 0 ? (0, 0) : (r / n, g / n);
	}

	/// <summary>
	/// ONDE MORA A COROA DE CONTROLE: do mesmo tamanho, num pedaco de chao sem ninguem -- uns tres tiles acima e a esquerda do corpo,
	/// alem do alcance da luz das duas esferas (a do outro fica a direita) e fora dos paineis do HUD. O que ela muda entre as duas fotos e
	/// o que a CENA muda sozinha: medido nas fotos de 2026-10-17, a manha clareando deixava a tela inteira 0,004 menos verde na foto de
	/// antes -- da ordem do sinal da barreira de dia. A prova desconta isso.
	/// </summary>
	private static Vector2 Controle(Vector2 centro, float zoom) => centro + new Vector2(-96, -74) * zoom;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (GameClient.Instance is not { Connected: true, LocalId: > 0 } cli || World.Instancia is not { PosicaoLocalDeTeste: not null } mundo
			|| Jandirus.Server.GameServer.Instance is not Jandirus.Server.GameServer srv)
		{
			_espera += delta;
			if (_espera > 30) { Conferir(false, "em 30 s o cliente conectou, o mundo nasceu e o servidor esta neste processo"); Fechar(); }
			return;
		}
		_t += delta;
		int eu = cli.LocalId;
		Node2D? meu = mundo.CorpoDeTeste(eu);
		Node2D? dele = _outro != 0 ? mundo.CorpoDeTeste(_outro) : null;
		Iluminacao? ceu = mundo.GetNodeOrNull<Iluminacao>("Iluminacao");
		switch (_passo)
		{
			case 0:
				if (_t < 1) return;   // o corpo local assenta no mundo
				GD.Print("[desfera] ===== A ESFERA VERDE DO DEMONIC SAIYAN (v0.4.0) =====");
				srv.EmbateDeFoto_CeuLimpo(eu);
				Passar();
				break;
			case 1:
				// O CEU LIMPO CHEGA NA TELA, e o veu do clima termina de sair (dois segundos seguidos sem clima nenhum).
				_limpoHa = ceu is { TempoQueFaz.Ativo: false } ? _limpoHa + delta : 0;
				if (_limpoHa < 2 && _t < 20) return;
				Conferir(_limpoHa >= 2, $"o ceu da foto esta LIMPO na tela ({_t:0.0}s) -- clima escurece a cena, e a rodada de dia viraria a da noite");
				string minha = srv.CarregarODemonicoDeTeste(eu);
				_outro = srv.ForjarParaAFotoDeCorpos(eu, new Vec2(3 * ZoneCollision.TileSize, 0), "Outro Lendario", 5e8);
				string daOutra = _outro != 0 ? srv.CarregarODemonicoDeTeste(_outro) : "sem corpo";
				Conferir(minha.Contains("PARADO") && srv.CarregandoODemonicoDeTeste(eu),
						 $"a MINHA carga comeca pelo canal de producao (\"{minha}\")");
				Conferir(daOutra.Contains("PARADO") && srv.CarregandoODemonicoDeTeste(_outro),
						 $"...e a do corpo AO LADO tambem (\"{daOutra}\")");
				Passar();
				break;
			case 2:
				if ((!Acesa(meu) || !Acesa(dele)) && _t < 5) return;
				Conferir(Acesa(meu), $"em {_t:0.0}s a esfera verde ACENDE no meu corpo (quem carrega ve a propria carga)");
				Conferir(Acesa(dele), $"...e no corpo do outro (quem esta em volta ve) -- e o mesmo estado do snapshot");
				// (dono, 2026-10-17) *"usar essas barreiras de energia no demonic saiyan so q esverdeadas e fazer a camera de todos perto tremer"*
				Conferir(Esfera(meu)?.BarreiraDeTeste is { } barreira && barreira.SpriteFrames?.ResourcePath == EsferaDemoniaca.FolhaDaBarreira
						 && barreira.Modulate.G > 0.9f && barreira.Modulate.R < barreira.Modulate.G * 0.3f && barreira.IsPlaying(),
						 $"ela e a BARREIRA DE ENERGIA do jogo (`{EsferaDemoniaca.FolhaDaBarreira}`), animada e tingida de verde");
				Conferir(mundo.TremorDeTeste > 0, $"...e a camera de quem esta perto TREME ({mundo.TremorDeTeste:0.00} px na minha)");
				// A HORA DA FOTO E A DO CEU: o relogio do planeta diz noite ou dia, e a tela (de ceu limpo) tem de concordar.
				Conferir(ceu != null && ceu.Estado.Noite == DeNoite && !ceu.TempoQueFaz.Ativo,
						 $"a hora da foto: o ceu diz {(ceu?.Estado.Noite == true ? "NOITE" : "dia")} (hora {ceu?.Estado.Hora:0.00}), limpo, e a tela "
						 + $"concorda (forca da noite {Iluminacao.ForcaDaNoite():0.00})");
				// (dono, 2026-10-17) *"faz essa barreira brilhar pra ser visivel a noite"*: a luz verde acende SO com a noite. O piso
				// sai da propria regua de noite daqui (forca > 0,5, a esfera inteira): a luz acesa nao fica abaixo de 0,5.
				PointLight2D? luz = Esfera(meu)?.LuzDeTeste;
				Conferir(luz != null && (DeNoite ? luz.Enabled && luz.Energy > 0.5f : !luz.Enabled),
						 DeNoite ? $"de NOITE (forca da noite {Iluminacao.ForcaDaNoite():0.00}) ela BRILHA: a luz verde esta acesa (energia {luz?.Energy:0.00})"
								 : "de dia a luz dela fica APAGADA -- efeito de dia nao estoura (a regra das auras)");
				if (Fotografar($"user://esfera-1-carregando-{Hora}.png") is { } foto && meu != null)
				{
					Transform2D tr = meu.GetGlobalTransformWithCanvas();
					float zoom = tr.X.Length();
					_centroDaFoto = tr.Origin;
					_centroDoControle = Controle(_centroDaFoto, zoom);
					_raioDaFoto = 64f * EsferaDemoniaca.Escala * zoom;   // o disco ocupa a folha de 128 px quase inteira
					_coroaAcesa = Coroa(foto, _centroDaFoto, _raioDaFoto);
					_controleAceso = Coroa(foto, _centroDoControle, _raioDaFoto);
				}
				srv.DarUmPassoDeTeste(_outro, new Vec2(2 * ZoneCollision.TileSize, 0));
				Passar();
				break;
			case 3:
				if (_t < 1.2) return;
				Conferir(!srv.CarregandoODemonicoDeTeste(_outro), "o outro ANDOU: a carga dele se desfaz no servidor");
				Conferir(Esfera(dele) is null or { Apagando: true }, "...e a esfera dele APAGA na minha tela");
				Conferir(srv.CarregandoODemonicoDeTeste(eu) && Acesa(meu), "...e a minha, parada, continua acesa");
				Fotografar("user://esfera-2-o-outro-andou.png");
				World.EsferaDemoniacaApagadaDeTeste = true;
				Passar();
				break;
			case 4:
				if (_t < 0.6) return;
				bool apagou = Esfera(meu) is null or { Apagando: true };
				Conferir(apagou && srv.CarregandoODemonicoDeTeste(eu),
						 "CONTRA-EXEMPLO (defeito injetado: a tela ignora a carga): a minha esfera apaga com o servidor ainda carregando -- "
						 + "a prova da esfera acesa reprovaria");
				Conferir(mundo.TremorDeTeste <= 0,
						 $"...e sem esfera nenhuma na tela a camera PARA de tremer ({mundo.TremorDeTeste:0.00} px) -- o tremor e dela, e nao de outra coisa");
				// AS MESMAS COROAS, SEM ELA: a foto do contra-exemplo e a tela de antes menos a barreira (e a luz dela). O PISO de 0,01 fica
				// acima do que o chao de controle varia depois de descontado (milesimos, nas fotos de 2026-10-17) e abaixo do que a barreira
				// da (+0,02 de dia, +0,3 de noite); "o dobro do vermelho" e o que diz que a mudanca e VERDE, e nao so mais clara.
				if (Fotografar($"user://esfera-3-sem-ela-{Hora}.png") is { } semEla && _coroaAcesa is { } acesa)
				{
					(double rSem, double gSem) = Coroa(semEla, _centroDaFoto, _raioDaFoto);
					(double rCtl, double gCtl) = Coroa(semEla, _centroDoControle, _raioDaFoto);
					double deriva = _controleAceso.G - gCtl;
					double verde = acesa.G - gSem - deriva, vermelho = acesa.R - rSem - (_controleAceso.R - rCtl);
					Conferir(verde > 0.01 && verde > 2 * vermelho,
							 $"...e ela SE VE de {Hora.ToUpperInvariant()}: no pixel, a coroa em volta do corpo fica {verde:+0.000;-0.000;0.000} mais verde "
							 + $"com ela do que sem ela (vermelho {vermelho:+0.000;-0.000;0.000}), descontado o que o chao de controle mudou sozinho "
							 + $"({deriva:+0.000;-0.000;0.000})");
				}
				else
					Conferir(false, "a prova do pixel precisa das duas fotos -- a bancada e de JANELA");
				World.EsferaDemoniacaApagadaDeTeste = false;
				Fechar();
				break;
		}
	}

	private void Fechar()
	{
		_acabou = true;
		World.EsferaDemoniacaApagadaDeTeste = false;
		foreach (string l in _linhas) GD.Print("[desfera] " + l);
		GD.Print(_falhas.Count == 0 ? "[desfera] ===== TUDO OK =====" : $"[desfera] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[desfera]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
