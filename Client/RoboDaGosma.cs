using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// AS FOTOS DO PEDIDO 219 (`--diaggosma`, 2026-11-01) -- o dono: *"faça isso e prove com imagens"*. Com janela e com `--host` (o palco e
/// `GameServer.GosmaPalco.cs`). Eu sou o Goku, fraco; um Majin da foto larga a gosma pelo verbo de producao, e o servidor anda o resto:
///   G1. a gosma LARGADA: ninguem a ve, e o Majin fica com 73% do poder -- foto `gosma-1-largada`;
///   G2. ela me VE e aparece, na cor do Majin -- `gosma-2-aparece`;
///   G3. GRUDA em mim -- `gosma-3-grudou`;
///   G4. me ENGOLE: o meu corpo some e a MINHA CAMERA vai com ela, voando pro Majin -- `gosma-4-engolido`;
///   G5. ENCOSTA no Majin: estou dentro dele, diante da imagem -- `gosma-5-dentro`;
///   G6. grudada de novo, eu SUBO O PODER e me solto -- `gosma-6-me-soltei`;
///   G7. engolido de novo, o Majin SAI DA ZONA: a gosma se desfaz e eu caio a vista -- `gosma-7-zona-desfaz`;
///   G8. um GOLPE na gosma a vista a desfaz -- `gosma-8-golpe`;
///   G9. o SUPER BOO da saga larga a dele, e ela me leva pra ele -- `gosma-9-super-boo` e `gosma-10-dentro-do-super-boo`; o Fat nao larga.
///
///     Godot --path . --host --rede 12890 --horateste 0.5 --semfauna --campoteste 9 --diaggosma --raca Human --conta bancada_gosma --nome Goku
/// </summary>
public partial class RoboDaGosma : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;
	private static World? Mundo => World.Instancia;

	/// <summary>O meu poder na foto: bem abaixo do da gosma (70% de 200 mil).</summary>
	private const double MeuPoder = 2_000;

	/// <summary>O voo com a presa dentro nas fotos, em tiles/s (o do jogo e 18): ver `GameServer.VooLevandoDeTeste`.</summary>
	private const double VooDaFoto = 3;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Ready() => _ = Rodar();

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Fechar();
	}

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && Mundo != null && S != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo nasceu e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		int eu = cli.LocalId;
		double meuPoderAntes = srv.CorpoNaFotoDoMajin(eu).Poder;
		await Segundos(1.5);
		GD.Print("[dgosma] ===== AS FOTOS DA GOSMA DO MAJIN (dono, 2026-11-01, o pedido 219) =====");
		srv.PalcoGosma_PoderDoJogador(eu, MeuPoder);
		// O VOO COM A PRESA DENTRO DEVAGAR, SO NAS FOTOS (o caminho e o mesmo): a 18 tiles/s os ~12 tiles ate o Majin duram dois tercos de
		// segundo -- medido na r2, com o mundo ainda nascendo os habitantes (quadros de 130 ms), o aviso de engolido entrou e saiu entre duas
		// olhadas, e o corpo que segue a gosma desenhada um quadro atras ficou 2,3 tiles longe dela. Ver `VooLevandoDeTeste`.
		srv.PalcoGosma_VooDaFoto(VooDaFoto);
		try
		{
			// ---------------------------------------------------------------- G1: largada
			int majin = srv.MajinDaFoto(eu, new Vec2(-3 * ZoneCollision.TileSize, 0), 200_000);
			await Segundos(1.0);
			double poderAntes = srv.CorpoNaFotoDoMajin(majin).Poder;
			srv.HabilidadeNaFotoDoMajin(majin, Jandirus.Core.Races.GosmaDoMajin.Verbo);
			await Segundos(0.8);
			var e1 = srv.PalcoGosma_Estado(majin);
			Fotografar("gosma-1-largada");
			Conferir(majin != 0 && e1.Passo == "Escondida" && e1.Oculta && !NaTela(e1.Corpo) && Math.Abs(e1.PrecoDoDono - 0.73) < 1e-9
					 && e1.PoderDoDono < poderAntes * 0.74 && e1.PoderDoDono > poderAntes * 0.72,
					 $"G1. A GOSMA LARGADA pelo verbo: escondida, oculta e fora da minha tela -- e o Majin com 73% do poder [passo {e1.Passo}, oculta "
					 + $"{e1.Oculta}, na tela {NaTela(e1.Corpo)}, poder {poderAntes:N0} -> {e1.PoderDoDono:N0}]");

			// ---------------------------------------------------------------- G2: ela me ve
			srv.PalcoGosma_LevarODono(majin, -9);
			srv.PalcoGosma_PorPerto(eu, majin, 3);
			await Segundos(1.0);
			srv.PalcoGosma_OlharJa(majin);
			int gosma = e1.Corpo;
			bool apareceu = await Ate(() => NaTela(gosma) && Gosma(gosma) is { PintadaDeTeste: true }, 4);
			await Segundos(0.2);
			Fotografar("gosma-2-aparece");
			Conferir(apareceu && Gosma(gosma)?.AnimacaoDeTeste == GosmaDesenhada.Animacao,
					 $"G2. ELA ME VE E APARECE: a gosma na minha tela, a `{GosmaDesenhada.Animacao}` da `Majin1` na cor do Majin [{Diag(gosma)}; "
					 + $"passo {srv.PalcoGosma_Estado(majin).Passo}]");

			// ---------------------------------------------------------------- G3: grudou
			bool grudou = await Ate(() => srv.PalcoGosma_Estado(majin).Passo == "Grudada", 4);
			await Segundos(0.6);
			Fotografar("gosma-3-grudou");
			Conferir(grudou && Perto(eu, gosma, 1.0), $"G3. GRUDOU EM MIM: a gosma em cima do meu corpo [passo {srv.PalcoGosma_Estado(majin).Passo}, {Distancia(eu, gosma)}]");

			// ---------------------------------------------------------------- G4: engolido, a camera vai junto
			bool engolido = await Ate(() => Local() is { NaGosmaDeTeste: true }, 12);
			Vector2? ondeFuiPego = Mundo?.PosicaoDesenhadaDe(eu);
			// NO MEIO DO VOO: a gosma desenhada ja a 2 tiles de onde me pegou -- e so ai "a camera vai com ela" diz alguma coisa (logo no bote as
			// duas estao no mesmo lugar com ou sem a camera seguindo)
			bool voou = await Ate(() => Mundo?.PosicaoDesenhadaDe(gosma) is { } pg && ondeFuiPego is { } o && pg.DistanceTo(o) > 2 * ZoneCollision.TileSize, 4);
			bool sumido = Local() is { SumidosNaGosmaDeTeste: > 0 };
			bool camera = Perto(eu, gosma, 0.75);
			Fotografar("gosma-4-engolido");
			bool andou = Mundo?.PosicaoDesenhadaDe(eu) is { } pe && ondeFuiPego is { } og && pe.DistanceTo(og) > ZoneCollision.TileSize;
			Conferir(engolido && voou && sumido && camera && andou && srv.PalcoGosma_NaGosma(eu),
					 $"G4. ENGOLIDO: o meu corpo some da minha tela e a MINHA CAMERA vai com a gosma, voando pro Majin [engolido {engolido}, ela voou "
					 + $"{voou}, corpo sumido {sumido}, eu na gosma {camera} ({Distancia(eu, gosma)}), saiu do lugar {andou}]");

			// ---------------------------------------------------------------- G5: dentro do Majin
			bool dentro = await Ate(() => InteriorDoMajin.EhOInterior(cli.Zone), 6);
			await Ate(() => Local() is { NaGosmaDeTeste: false, SumidosNaGosmaDeTeste: 0 }, 3);
			await Segundos(1.0);
			Fotografar("gosma-5-dentro");
			(int preso, _, _) = srv.PrisaoNaFotoDoMajin(eu);
			Conferir(dentro && preso == majin && Local() is { NaGosmaDeTeste: false, SumidosNaGosmaDeTeste: 0 } && !srv.PalcoGosma_NaGosma(eu),
					 $"G5. ENCOSTOU NO MAJIN: estou DENTRO dele (a absorcao de sempre) e o meu corpo voltou [zona {cli.Zone.Name}, preso de #{preso}]");

			// ---------------------------------------------------------------- G6: me soltei
			srv.PalcoGosma_Soltar(majin);
			await Ate(() => !InteriorDoMajin.EhOInterior(cli.Zone), 6);
			await Ate(() => srv.PalcoGosma_Estado(majin).Passo == "Escondida", 6);
			srv.PalcoGosma_EsquecerOUltimo(majin);
			srv.PalcoGosma_LevarODono(majin, -9);
			srv.PalcoGosma_PorPerto(eu, majin, 3);
			await Segundos(1.0);
			srv.PalcoGosma_OlharJa(majin);
			bool grudou2 = await Ate(() => srv.PalcoGosma_Estado(majin).Passo == "Grudada", 5);
			srv.PalcoGosma_PoderDoJogador(eu, 5_000_000);   // eu subo o poder: *"Raise your BP to resist!"*
			bool soltei = await Ate(() => srv.PalcoGosma_Estado(majin).Passo is "Voltando" or "Escondida", 3);
			await Segundos(0.3);
			Fotografar("gosma-6-me-soltei");
			Conferir(grudou2 && soltei && !srv.PalcoGosma_NaGosma(eu),
					 $"G6. GRUDADA, EU SUBO O PODER E ME SOLTO: ela volta pro lugar dela [grudou {grudou2}, passo {srv.PalcoGosma_Estado(majin).Passo}]");
			srv.PalcoGosma_PoderDoJogador(eu, MeuPoder);

			// ---------------------------------------------------------------- G7: o Majin sai da zona
			await Ate(() => srv.PalcoGosma_Estado(majin).Passo == "Escondida", 6);
			srv.PalcoGosma_EsquecerOUltimo(majin);
			srv.PalcoGosma_PorPerto(eu, majin, 3);
			await Segundos(0.8);
			srv.PalcoGosma_OlharJa(majin);
			await Ate(() => srv.PalcoGosma_Estado(majin).Passo == "Grudada", 5);
			srv.PalcoGosma_PularAsConferidas(majin);
			bool engolido2 = await Ate(() => srv.PalcoGosma_NaGosma(eu), 3);
			srv.PalcoGosma_MudarODonoDeZona(majin, "Namek");
			bool solto = await Ate(() => !srv.PalcoGosma_NaGosma(eu) && Local() is { NaGosmaDeTeste: false, SumidosNaGosmaDeTeste: 0 }, 3);
			await Segundos(0.8);
			Fotografar("gosma-7-zona-desfaz");
			var e7 = srv.PalcoGosma_Estado(majin);
			Conferir(engolido2 && solto && e7.Passo == "-" && Math.Abs(e7.PrecoDoDono - 1) < 1e-9 && !NaTela(gosma),
					 $"G7. ENGOLIDO DE NOVO, O MAJIN SAI DA ZONA: a gosma se desfaz, eu caio a vista e ele volta ao poder inteiro [engolido "
					 + $"{engolido2}, solto {solto}, gosma {e7.Passo}, preco {e7.PrecoDoDono}]");

			// ---------------------------------------------------------------- G8: um golpe a desfaz
			srv.PalcoGosma_TrazerODono(majin, eu, -3);
			await Ate(() => srv.CorpoNaFotoDoMajin(majin).Zona.Equals(cli.Zone), 6);
			await Segundos(1.0);
			srv.HabilidadeNaFotoDoMajin(majin, Jandirus.Core.Races.GosmaDoMajin.Verbo);
			await Segundos(0.5);
			int gosma2 = srv.PalcoGosma_Estado(majin).Corpo;
			srv.PalcoGosma_LevarODono(majin, -9);
			srv.PalcoGosma_PorPerto(eu, majin, 4);
			await Segundos(0.8);
			srv.PalcoGosma_OlharJa(majin);
			bool aVista = await Ate(() => NaTela(gosma2), 4);
			bool golpe = srv.PalcoGosma_Golpear(majin);
			bool desfeita = await Ate(() => srv.PalcoGosma_Estado(majin).Passo == "-" && !NaTela(gosma2), 3);
			await Segundos(0.4);
			Fotografar("gosma-8-golpe");
			Conferir(gosma2 != 0 && aVista && golpe && desfeita && Math.Abs(srv.PalcoGosma_Estado(majin).PrecoDoDono - 1) < 1e-9,
					 $"G8. A VISTA, UM GOLPE A DESFAZ: a gosma some, e o Majin volta ao poder inteiro [a vista {aVista}, golpe {golpe}, desfeita {desfeita}]");

			srv.LimparAFotoDoMajin();

			// ---------------------------------------------------------------- G9: o Super Boo
			await SuperBoo(srv, cli, eu);
		}
		finally
		{
			srv.PalcoBoo_Desfazer(eu);
			srv.PalcoGosma_Limpar(eu, meuPoderAntes > 0 ? meuPoderAntes : MeuPoder);
		}
	}

	/// <summary>
	/// G9 -- o adendo 219h: *"faça a parte do super buu da saga buu poder usar essa mecanica (os outros buus como fat e kid nao podem usar)"*.
	/// O Boo nasce gordo (e nao larga gosma), se divide, e o Super larga a dele sozinho (o relogio do degrau); ela me leva pra ele.
	/// </summary>
	private async Tarefa SuperBoo(Jandirus.Server.GameServer srv, GameClient cli, int eu)
	{
		int boo = srv.PalcoBoo_Nascer(eu);
		// O BOO SEM CEREBRO NA FOTO: medido na r2, solto e agressivo ele me derrubava com o Kamehameha -- e caido ele me absorveria pela porta
		// de sempre, sem gosma nenhuma. O relogio da saga (a divisao, o prazo da gosma) nao e do cerebro e continua andando.
		srv.PalcoGosma_SemCerebro(boo, true);
		await Segundos(3.0);
		bool gordoSemGosma = !srv.PalcoGosma_TemGosma(boo);
		srv.PalcoBoo_Dividir();
		bool superBoo = await Ate(() => srv.PalcoBoo_Estado().Degrau == SagaDoBuu.DegrauSuper, 5);
		srv.PalcoBoo_FotografoNaCarencia(eu, 90);
		// A CENA DA DIVISAO (25,5 s, `MajinEvoluindo`) E DEPOIS O PRAZO DELE (20 s): a r2 esperava 40 s e a gosma saiu depois
		bool largou = await Ate(() => srv.PalcoGosma_TemGosma(boo), 75);
		srv.PalcoGosma_LevarODono(boo, -12);
		srv.PalcoGosma_PorPerto(eu, boo, 3);
		await Segundos(0.8);
		srv.PalcoBoo_FotografoNaCarencia(eu, 0);
		srv.PalcoGosma_OlharJa(boo);
		int gosmaDoBoo = srv.PalcoGosma_Estado(boo).Corpo;
		await Ate(() => srv.PalcoGosma_Estado(boo).Passo == "Grudada", 5);
		srv.PalcoGosma_PularAsConferidas(boo);
		bool engolido = await Ate(() => Local() is { NaGosmaDeTeste: true }, 4);
		await Quadros(4);
		// (r4) A COR DO BOO: a folha do Super ja nasce rosa e ele nao tem tinta escolhida -- a gosma dele saia CINZA (a foto 9 da r3)
		bool rosa = Gosma(gosmaDoBoo)?.TintaDeTeste is { } tinta && tinta.Cor.X > 0.5f && tinta.Cor.X > tinta.Cor.Y;
		string corDaGosma = Gosma(gosmaDoBoo)?.TintaDeTeste is { } t2 ? $"{t2.Cor.X:0.00}/{t2.Cor.Y:0.00}/{t2.Cor.Z:0.00}" : "sem tinta";
		Fotografar("gosma-9-super-boo");
		bool dentro = await Ate(() => InteriorDoMajin.EhOInterior(cli.Zone), 6);
		await Segundos(1.5);
		Fotografar("gosma-10-dentro-do-super-boo");
		(int preso, _, _) = srv.PrisaoNaFotoDoMajin(eu);
		Conferir(boo != 0 && gordoSemGosma && superBoo && largou && engolido && dentro && preso == boo && rosa,
				 $"G9. O SUPER BOO LARGA A GOSMA (o Fat nao), ROSA como ele, e ela me leva pra ele: dentro do Super Boo [gordo sem gosma "
				 + $"{gordoSemGosma}, super {superBoo}, largou {largou}, cor {corDaGosma}, engolido {engolido}, zona {cli.Zone.Name}, preso de #{preso} "
				 + $"(Boo #{boo})]");
	}

	// =====================================================================
	// AS LEITURAS
	// =====================================================================
	private static LocalPlayer? Local() => Mundo?.CorpoDeTeste(GameClient.Instance?.LocalId ?? 0) as LocalPlayer;

	private static GosmaDesenhada? Gosma(int id) => Mundo?.CorpoDeTeste(id)?.GetNodeOrNull<GosmaDesenhada>(GosmaDesenhada.NomeDoNode);

	/// <summary>A gosma esta desenhada e visivel na minha tela, dentro do quadro da foto?</summary>
	private static bool NaTela(int id) =>
		id != 0 && Mundo?.CorpoDeTeste(id) is { } c && c.IsVisibleInTree() && Gosma(id) is { } g && g.IsVisibleInTree() && NoQuadro(c);

	private static bool NoQuadro(Node2D n) =>
		n.GetViewport() is { } v && v.GetVisibleRect().Grow(-32).HasPoint(n.GetGlobalTransformWithCanvas().Origin);

	/// <summary>Os dois corpos estao desenhados a menos de <paramref name="tiles"/> tiles um do outro?</summary>
	private static bool Perto(int a, int b, double tiles) =>
		Mundo?.PosicaoDesenhadaDe(a) is { } pa && Mundo.PosicaoDesenhadaDe(b) is { } pb && pa.DistanceTo(pb) <= tiles * ZoneCollision.TileSize;

	private static string Distancia(int a, int b) =>
		Mundo?.PosicaoDesenhadaDe(a) is { } pa && Mundo.PosicaoDesenhadaDe(b) is { } pb
			? $"{pa.DistanceTo(pb) / ZoneCollision.TileSize:0.00} tiles entre #{a} e #{b} desenhados" : "sem um dos dois desenhado";

	private static string Diag(int id) =>
		Mundo?.CorpoDeTeste(id) is not { } c ? $"#{id} fora do meu mundo"
		: $"#{id} visivel {c.IsVisibleInTree()}, no quadro {NoQuadro(c)}, gosma {(Gosma(id) is { } g ? $"`{g.AnimacaoDeTeste}` pintada {g.PintadaDeTeste}" : "SEM o node")}";

	// =====================================================================
	// AS MAOS
	// =====================================================================
	private void Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		_linhas.Add("  --     foto salva em " + caminho);
	}

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + s;
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

	private void Fechar()
	{
		foreach (string l in _linhas) GD.Print("[dgosma] " + l);
		GD.Print(_falhas.Count == 0 ? "[dgosma] ===== TUDO OK =====" : $"[dgosma] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dgosma]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
