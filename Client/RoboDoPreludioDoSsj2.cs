using Godot;
using Jandirus.Core.Forms;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// `--diagpreludio`: O FUNDO DA ESTREIA DO SSJ2 NA TELA (2026-10-24). O dono, com tres prints do Gohan: *"faça um efeito q so aparece pro personagem q virou ssj2 pela primeira vez (um efeito local) como pode ver na imagem, ficaria tudo escuro atras do personagem, tudo do jogo sumiria, e uma linha vermelha passaria atras e se expande cobrindo todo o fundo preto com esse efeito vermelho/amarelo, e ai começa a cinematica normal do ssj2 n deve durar mais do q 4 segundos isso tudo, e a musica do ssj2 ja deve começar desde o começo desse efeito, dps dele faz a cinematica padrao atual"*.
///
/// O robo e o jogador do host: o palco (`GameServer.PreludioPalco.cs`) o poe no SSJ1 dominado, com o poder do SSJ2 pago e a estreia do 2 por
/// ver, e quem transforma e o C do proprio cliente (`SendTransformar`). O que se mede e o que a tela mostra:
///   P0. o roteiro: a estreia do SSJ2 e a cena do DM com o fundo no segundo zero e o resto 4 s depois; a encurtada e as outras formas, sem;
///   P1. o fundo abre com a estreia, o corpo por cima da cortina, o resto do jogo sumido e a musica do SSJ2 ja no ar;
///   P2. a linha fina entra pela esquerda (0,9 s), a faixa se abre em fogo (2,0 s) e cobre a tela (3,1 s) -- os pixels das fotos;
///   P3. aos 4 s o fundo fecha (o painel e o plano do corpo de volta) e a cena de sempre segue ate o SSJ2 ficar;
///   P4. so na tela de quem vira: a mesma cena num corpo visto de fora nao abre nada (contra-exemplo `FundoDaEstreia.PraQuemAssisteDeTeste`);
///   P5. o contra-exemplo da regra: `Cinematicas.EstreiaDoSsj2SemPreludioDeTeste` devolve a cena do DM, e o fundo nao abre.
/// Fotos `user://preludio-*.png`.
///
///     Godot --janela -- --host --rede 11090 --horateste 0.5 --diagpreludio --raca Saiyan --conta bancada_preludio --nome Gohan
/// </summary>
public partial class RoboDoPreludioDoSsj2 : Node
{
	private int _ok, _falha, _fotos;
	private readonly List<string> _reprovadas = [];

	private static void Nota(string s) => GD.Print("[dpreludio] " + s);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK     " : "  FALHA  ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;
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
		Cinematicas.EstreiaDoSsj2SemPreludioDeTeste = false;
		FundoDaEstreia.PraQuemAssisteDeTeste = false;
		FundoDaEstreia.Ativo?.Fechar();
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
		Nota(" O FUNDO DA ESTREIA DO SSJ2 -- so na tela de quem vira, ate 4 s, com a musica do SSJ2 desde o comeco");
		Nota("==================================================================================");

		// ---- P0. o roteiro, no Core ----
		FormaDef ssj2 = Catalogo.Def("ssj2")!, ssj1 = Catalogo.Def("ssj1")!;
		Cinematica estreia = Cinematicas.NoDegrau(ssj2, DegrauDeCena.Estreia)!;
		Cinematica doDm = Cinematicas.Ssj2;
		double s = Cinematicas.SegundosDoPreludio;
		bool deslocado = estreia.Beats.Length == doDm.Beats.Length + 1
						 && estreia.Beats.Skip(1).Zip(doDm.Beats).All(p => Math.Abs(p.First.Em - p.Second.Em - s) < 0.011 && p.First.Faz == p.Second.Faz);
		Checa($"P0. **A ESTREIA DO SSJ2 COMECA PELO FUNDO** (dono, 2026-10-24): o primeiro beat e o `FundoDaEstreia`, no segundo zero, e a cena do "
			  + $"DM inteira vem {s:0.#} s depois -- o corpo preso {s:0.#} s a mais e a MESMA musica, que toca do zero",
			  s <= 4.0 && estreia.Beats[0] is { Em: 0 } b0 && b0.Faz == Efeito.FundoDaEstreia && deslocado
			  && Math.Abs(estreia.SegundosPreso - doDm.SegundosPreso - s) < 0.011 && estreia.Musica == doDm.Musica && estreia.Musica.Length > 0,
			  $"preludio {s} s, preso {estreia.SegundosPreso} (DM {doDm.SegundosPreso}), {estreia.Beats.Length} beats");
		int comFundo = Catalogo.Todas.Count(d => d.Id != ssj2.Id
			&& Cinematicas.NoDegrau(d, DegrauDeCena.Estreia) is { } c && c.Beats.Any(b => b.Faz.HasFlag(Efeito.FundoDaEstreia)));
		bool curtaSem = Cinematicas.NoDegrau(ssj2, DegrauDeCena.Curta) is { } k && !k.Beats.Any(b => b.Faz.HasFlag(Efeito.FundoDaEstreia));
		Checa("P0b. ...e SO a estreia do SSJ2: a encurtada dele nao tem fundo, nem a estreia de forma nenhuma outra", comFundo == 0 && curtaSem,
			  $"{comFundo} outra(s) estreia(s) com fundo; encurtada sem fundo {curtaSem}");

		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } c && c.Atributos.Raca is { Length: > 0 }
									  && S != null && World.Instancia != null && Hud.Instancia != null, 90);
		Checa("o mundo chegou (conexao, ficha lenta, painel de pe) e o servidor e deste processo (--host)", pronto);
		if (!pronto || C is not { } cli || S is not { } srv || World.Instancia is not { } mundo || Hud.Instancia is not { } hud)
		{ Encerrar(2); return; }
		int eu = cli.LocalId;

		// ---- o palco: o SSJ1 dominado, o poder do 2 pago, a estreia do 2 por ver ----
		Checa("(montagem) o palco poe o robo no SSJ1 dominado, com o poder do SSJ2 pago e a estreia do 2 por ver", srv.PalcoPS_NoSsj1(eu) == "ssj1");
		bool noUm = await Ate(() => cli.Atributos.FormaAtual == ssj1.IdRede && mundo.CenaEmCursoDeTeste(eu) == null && mundo.CorpoDeTeste(eu) != null, 10);
		await Segundos(1.0);
		Node2D? corpo = mundo.CorpoDeTeste(eu);
		Checa("(montagem) o corpo do robo esta na tela, no SSJ1 e sem cena", noUm && corpo != null);
		if (corpo == null) { Encerrar(1); return; }
		int zAntes = corpo.ZIndex;
		bool relativoAntes = corpo.ZAsRelative;

		// ---- P1. a estreia: o C sobe pro 2 ----
		cli.SendTransformar(true);
		bool abriu = await Ate(() => FundoDaEstreia.Ativo != null, 3);
		FundoDaEstreia? fundo = FundoDaEstreia.Ativo;
		Cinematica? tocando = mundo.CenaEmCursoDeTeste(eu)?.CenaDeTeste;
		Checa("P1. **O FUNDO ABRE NA ESTREIA DO SSJ2**: o C sobe pro 2, e a cena de estreia que toca na minha tela comeca por ele",
			  abriu && tocando is { Forma: "ssj2" } && tocando.Beats[0].Faz.HasFlag(Efeito.FundoDaEstreia),
			  $"fundo {abriu}; cena {tocando?.Forma ?? "nenhuma"}");
		Checa("P1b. ...com o CORPO POR CIMA da cortina",
			  abriu && corpo.ZIndex == FundoDaEstreia.CamadaDoCorpo && !corpo.ZAsRelative,
			  $"plano do corpo {corpo.ZIndex}, cortina {FundoDaEstreia.CamadaDaCortina}");
		Checa("P1c. ...e o RESTO DO JOGO SUMIDO: o painel de status e as outras camadas de tela escondidos",
			  abriu && !hud.Visible && (fundo?.CamadasEscondidasDeTeste ?? 0) > 0,
			  $"painel {(hud.Visible ? "a vista" : "escondido")}, {fundo?.CamadasEscondidasDeTeste ?? 0} camada(s) escondida(s)");
		AudioDirector? som = AudioDirector.Instance;
		Checa("P1d. **A MUSICA DO SSJ2 COMECA JUNTO DO FUNDO**: a faixa da cena ja no ar, na camada da transformacao",
			  som != null && som.CamadaDeTeste == AudioDirector.Camada.Transformacao && som.FaixaDeTeste.Contains(Cinematicas.Ssj2.Musica),
			  $"camada {som?.CamadaDeTeste}, faixa {som?.FaixaDeTeste}");

		// ---- P2. os tres tempos, nos pixels das fotos ----
		Image? daLinha = await FotoNoSegundo(fundo, 0.9, "a-linha");
		Image? daFaixa = await FotoNoSegundo(fundo, 2.0, "a-faixa");
		Image? doFogo = await FotoNoSegundo(fundo, 3.1, "o-fogo");
		if (daLinha != null && daFaixa != null && doFogo != null)
		{
			Checa("P2. **A LINHA ENTRA PELA ESQUERDA** (0,9 s): o canto ainda preto, a diagonal acesa na esquerda e apagada na direita",
				  Preto(Px(daLinha, 0.06, 0.08)) && Clara(Px(daLinha, 0.2, NaDiagonal(0.2))) && !Clara(Px(daLinha, 0.8, NaDiagonal(0.8))),
				  $"canto {Txt(Px(daLinha, 0.06, 0.08))}, esquerda {Txt(Px(daLinha, 0.2, NaDiagonal(0.2)))}, direita {Txt(Px(daLinha, 0.8, NaDiagonal(0.8)))}");
			Checa("P2b. **...SE ABRE EM FOGO** (2,0 s): a diagonal na direita ja e fogo, e o canto continua preto",
				  Fogo(Px(daFaixa, 0.8, NaDiagonal(0.8))) && Preto(Px(daFaixa, 0.06, 0.08)),
				  $"diagonal {Txt(Px(daFaixa, 0.8, NaDiagonal(0.8)))}, canto {Txt(Px(daFaixa, 0.06, 0.08))}");
			Checa("P2c. **...E COBRE A TELA** (3,1 s): os quatro cantos em fogo -- vermelho e amarelo, sem azul",
				  Fogo(Px(doFogo, 0.06, 0.08)) && Fogo(Px(doFogo, 0.94, 0.08)) && Fogo(Px(doFogo, 0.06, 0.92)) && Fogo(Px(doFogo, 0.94, 0.92)),
				  $"{Txt(Px(doFogo, 0.06, 0.08))} {Txt(Px(doFogo, 0.94, 0.08))} {Txt(Px(doFogo, 0.06, 0.92))} {Txt(Px(doFogo, 0.94, 0.92))}");
		}
		else Nota("  (sem janela: os pixels das fotos nao foram medidos)");

		// ---- P3. o fundo fecha aos 4 s, e a cena de sempre segue ----
		bool fechou = await Ate(() => FundoDaEstreia.Ativo == null, 3);
		await Quadros(2);
		Checa("P3. **NOS 4 s O FUNDO FECHA**: a cortina saiu, o painel voltou e o corpo voltou ao plano dele",
			  fechou && hud.Visible && corpo.ZIndex == zAntes && corpo.ZAsRelative == relativoAntes,
			  $"fechou {fechou}, painel {hud.Visible}, plano {corpo.ZIndex} (era {zAntes})");
		Checa("P3b. ...e a cena de sempre segue, com o corpo preso pelo servidor (a virada vem {s:0.#} s depois da do DM)",
			  mundo.CenaEmCursoDeTeste(eu) != null && srv.EmCenaDeTeste(eu));
		await Segundos(0.6);
		await Foto("a-cena-de-sempre");
		bool virou = await Ate(() => srv.FormaDoCorpoDeTeste(eu) == "ssj2" && !srv.EmCenaDeTeste(eu), estreia.SegundosPreso + 6);
		Checa("P3c. ...e no fim da cena o robo esta no SSJ2", virou, $"forma {srv.FormaDoCorpoDeTeste(eu)}");
		await Quadros(10);

		// ---- P4. so na tela de quem vira ----
		Node atores = corpo.GetParent();
		Transformacao vista = Transformacao.Rodar(atores, corpo, ssj2, estreia, souEu: false);
		await Segundos(0.6);
		bool abriuDeFora = FundoDaEstreia.Ativo != null;
		FundoDaEstreia.Ativo?.Fechar();
		Descartar(vista);
		Checa("P4. **SO NA TELA DE QUEM VIRA**: a mesma estreia tocando num corpo visto de fora nao abre fundo nenhum", !abriuDeFora);
		FundoDaEstreia.PraQuemAssisteDeTeste = true;
		try
		{
			Transformacao vistaComDefeito = Transformacao.Rodar(atores, corpo, ssj2, estreia, souEu: false);
			await Segundos(0.6);
			bool abriuComDefeito = FundoDaEstreia.Ativo != null;
			FundoDaEstreia.Ativo?.Fechar();
			Descartar(vistaComDefeito);
			Checa("   (defeito injetado: `FundoDaEstreia.PraQuemAssisteDeTeste`) o fundo abre pra quem assiste -- a regua do P4 REPROVA", abriuComDefeito);
		}
		finally { FundoDaEstreia.PraQuemAssisteDeTeste = false; }
		await Quadros(5);

		// ---- P5. o contra-exemplo da regra: a estreia do DM, sem o fundo ----
		Checa("(montagem) o palco devolve o robo ao SSJ1, com a estreia do 2 por ver de novo", srv.PalcoPS_NoSsj1(eu) == "ssj1");
		await Ate(() => cli.Atributos.FormaAtual == ssj1.IdRede && mundo.CenaEmCursoDeTeste(eu) == null, 10);
		await Segundos(1.0);
		Cinematicas.EstreiaDoSsj2SemPreludioDeTeste = true;
		try
		{
			cli.SendTransformar(true);
			bool comecou = await Ate(() => mundo.CenaEmCursoDeTeste(eu) != null, 3);
			await Segundos(0.6);
			Cinematica? semFundo = mundo.CenaEmCursoDeTeste(eu)?.CenaDeTeste;
			Checa("   (defeito injetado: `Cinematicas.EstreiaDoSsj2SemPreludioDeTeste`, o jogo de antes) a estreia toca a cena do DM e o fundo NAO "
				  + "abre -- as reguas do P0 e do P1 REPROVAM",
				  comecou && FundoDaEstreia.Ativo == null && semFundo is { Forma: "ssj2" } && !semFundo.Beats[0].Faz.HasFlag(Efeito.FundoDaEstreia),
				  $"cena {semFundo?.Forma ?? "nenhuma"}, fundo {FundoDaEstreia.Ativo != null}");
			await Ate(() => !srv.EmCenaDeTeste(eu), Cinematicas.Ssj2.SegundosPreso + 6);
		}
		finally { Cinematicas.EstreiaDoSsj2SemPreludioDeTeste = false; }

		Encerrar(_falha == 0 ? 0 : 1);
	}

	// =====================================================================
	// OS AUXILIARES
	// =====================================================================
	/// <summary>O y da diagonal do fundo neste x (as duas em fracao da tela) -- a mesma reta do shader.</summary>
	private static double NaDiagonal(double x) => 0.5 + FundoDaEstreia.Inclinacao * (x - 0.5);

	private static Color Px(Image img, double u, double v) =>
		img.GetPixel(Math.Clamp((int)(u * img.GetWidth()), 0, img.GetWidth() - 1), Math.Clamp((int)(v * img.GetHeight()), 0, img.GetHeight() - 1));

	private static bool Preto(Color c) => c.R8 < 45 && c.G8 < 45 && c.B8 < 45;
	private static bool Clara(Color c) => c.R8 > 150;
	private static bool Fogo(Color c) => c.R8 > 140 && c.B8 < 130 && c.R8 > c.B8 + 60;
	private static string Txt(Color c) => $"({c.R8},{c.G8},{c.B8})";

	private static void Descartar(Transformacao? t)
	{
		if (t != null && IsInstanceValid(t)) t.Free();
	}

	/// <summary>Espera o fundo chegar a este segundo (ou fechar) e fotografa a tela.</summary>
	private async System.Threading.Tasks.Task<Image?> FotoNoSegundo(FundoDaEstreia? fundo, double segundo, string nome)
	{
		await Ate(() => fundo == null || !IsInstanceValid(fundo) || fundo.SegundosDeTeste >= segundo, segundo + 2);
		return await Foto(nome);
	}

	/// <summary>A tela inteira em `user://preludio-NN-nome.png`, e a imagem pra medir. SO COM JANELA.</summary>
	private async System.Threading.Tasks.Task<Image?> Foto(string nome)
	{
		if (SemJanela) return null;
		await Quadros(1);
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) return null;
			string caminho = ProjectSettings.GlobalizePath($"user://preludio-{++_fotos:00}-{nome}.png");
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
