using Godot;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA ESFERA NA TECLA E (`--diagesferae`, 2026-10-18): o dono -- *"n to conseguindo pegar a esfera do dragao"* e *"seria bom aparecer a
/// mensagem de pegar a esfera assim como pega um item dropado"*. Com janela e `--host`, nascendo Humano no campo da Terra:
///   E1. uma Esfera do Dragao a um tile do corpo (o palco a traz pra perto): a dica no pe da tela diz "[E] pegar a Esfera de N estrelas", como a
///       de um item largado -- foto `esfera-1-dica`; CONTRA-EXEMPLO (`MenuDeInteracao.EsferaSoPeloMenuDeTeste`, o jogo de antes): sem dica --
///       foto `esfera-1b-defeito`;
///   E2. a tecla E de verdade: o servidor diz "voce pega a ..." e a esfera sai do chao -- foto `esfera-2-pegou`.
///   E3-E5. (2026-10-19) a esfera pega na MOCHILA: pedra com o set dormindo, laranja quando ele acorda (as duas AMPLIADAS no slot, E3b e
///       E4b), e o "Largar no chão" do slot -- ver `NaMochila`.
///
///     Godot --path . --host --rede 8471 --campoteste 12 --horateste 0.5 --semfauna --diagesferae --raca Human --conta bancada_esferae --nome Catador
/// </summary>
public partial class RoboDaEsferaNaTeclaE : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ditas = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

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

	private void Ouvir(Protocol.Fala canal, string quem, string texto) => _ditas.Add(texto);

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null
									  && MenuDeInteracao.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o menu da tecla E nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || MenuDeInteracao.Instancia is not { } menu) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[desferae] ===== A ESFERA DO DRAGAO NA TECLA E (dono, 2026-10-18) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);

		int numero = srv.PalcoEE_EsferaAoLado(eu);
		bool chegou = numero > 0 && await Ate(() => cli.Esferas.Any(e => e.Tipo == Protocol.CoisaDeEsfera.Esfera && e.Numero == numero), 10);
		await Segundos(0.8);
		string esperada = $"[E] pegar a {Jandirus.Core.Magic.Esferas.NomeDaEsfera(numero)}";
		Conferir(chegou && menu.DicaNaTela == esperada,
				 "E1. A ESFERA A UM TILE, E A DICA DE PEGAR APARECE (*\"seria bom aparecer a mensagem de pegar a esfera assim como pega um item "
				 + $"dropado\"*) [esfera {numero} na tela {chegou}; dica \"{menu.DicaNaTela}\"]");
		Fotografar("user://esfera-1-dica.png");

		MenuDeInteracao.EsferaSoPeloMenuDeTeste = true;
		string dicaDeAntes;
		try
		{
			await Quadros(4);
			dicaDeAntes = menu.DicaNaTela;
			Fotografar("user://esfera-1b-defeito.png");
		}
		finally { MenuDeInteracao.EsferaSoPeloMenuDeTeste = false; }
		await Quadros(4);
		Conferir(!dicaDeAntes.Contains("Esfera", StringComparison.Ordinal),
				 $"CONTRA-EXEMPLO (defeito injetado: a tecla E nao enxerga a esfera, o jogo de antes): sem dica de pegar -- a regua de E1 reprovaria [\"{dicaDeAntes}\"]");

		_ditas.Clear();
		cli.Falou += Ouvir;
		try
		{
			GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = true });
			GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = false });
			bool pegou = await Ate(() => _ditas.Any(t => t.StartsWith("você pega a", StringComparison.Ordinal))
										 && !cli.Esferas.Any(e => e.Tipo == Protocol.CoisaDeEsfera.Esfera && e.Numero == numero), 5);
			Conferir(pegou, "E2. A TECLA E PEGA A ESFERA: o servidor diz que pegou, e ela sai do chao "
							+ $"[ouvi: {string.Join(" | ", _ditas.TakeLast(2))}]");
		}
		finally { cli.Falou -= Ouvir; }
		await Segundos(0.5);
		Fotografar("user://esfera-2-pegou.png");

		await NaMochila(cli, srv, eu, numero);
		srv.PalcoEE_Largar(eu);
	}

	/// <summary>
	/// (2026-10-19) A ESFERA PEGA ESTA NA MOCHILA (dono: *"caso pegue as esferas elas vao pro seu inventario (mantendo o sprite atual, se elas
	/// estriverem como pedra, elas vao como pedra, se nao vao como a esfera laranja mesmo normal) podendo dropar elas"*):
	///   E3. a mochila aberta tem o slot da esfera, e com o set dormindo o icone e o MESMO desenho do quadro "inactive" da esfera do chao (a
	///       pedra) -- foto `esfera-3-mochila-pedra`;
	///   E4. o set acorda: o slot vira a esfera LARANJA, o desenho do quadro "N" do chao -- foto `esfera-4-mochila-laranja`; CONTRA-EXEMPLO
	///       (`GameServer.EsferaForaDaMochilaDeTeste`, o jogo de antes): o slot some;
	///   E3b/E4b. (2026-10-19, dono: *"Pode ampliar o ícone da esfera no slot da mochila, com o mesmo sprite (pedra ou laranja), só maior."*)
	///       a bola MEDIDA NA FOTO ocupa no slot um multiplo inteiro, de pelo menos 3x, do lado dela no quadro; CONTRA-EXEMPLO
	///       (`TelaDeInventario.EsferaPequenaNoSlotDeTeste`, o jogo de antes): o quadro inteiro pelo `Icon` -- foto `esfera-4b-defeito-pequena`;
	///   E5. o painel do slot oferece so "Largar no chão", e o clique a poe de volta no chao e a tira da mochila -- foto `esfera-5-largou`.
	/// </summary>
	private async Tarefa NaMochila(GameClient cli, Jandirus.Server.GameServer srv, int eu, int numero)
	{
		if (TelaDeInventario.Instancia is not { } mochila) { Conferir(false, "E3. a tela da mochila existe neste cliente"); return; }
		Jandirus.Core.Magic.EsferaNaMochila? NoSlot() => cli.Mochila.Pilhas.Select(p => Jandirus.Core.Magic.EsferaNaMochila.Ler(p.Id))
			.OfType<Jandirus.Core.Magic.EsferaNaMochila>().FirstOrDefault(x => x.Numero == numero);

		// O QUADRO DO CHAO: a folha e o estado que a `EsferaDesenhada` usa pra esta esfera, e o quadro 0 dessa animacao.
		Texture2D? QuadroDoChao(bool inerte)
		{
			(string arte, string estado) = EsferaDesenhada.FolhaDe(Protocol.CoisaDeEsfera.Esfera, "comum", numero, inerte);
			return FolhasPresas.Carregar(arte)?.GetFrameTexture(Miniaturas.NomeDaAnimacao(estado), 0);
		}

		// (2026-10-19) A REGUA DO "SO MAIOR": a bola desenhada no slot e um multiplo INTEIRO, de pelo menos 3x, do lado dela no quadro do chao
		// (9 px a da Terra) -- maior, e com cada pixel do sprite do mesmo tamanho. O quadro inteiro pelo `Icon` dava 12 px.
		int bola = QuadroDoChao(false)?.GetImage() is { } q && !q.IsEmpty() ? Math.Max(q.GetUsedRect().Size.X, q.GetUsedRect().Size.Y) : 0;
		bool Ampliada(int lado) => bola > 0 && lado >= 3 * bola && lado % bola == 0;

		async System.Threading.Tasks.Task<(bool Desenho, int Lado)> SlotCom(bool pedra)
		{
			bool chegou = await Ate(() => NoSlot() is { } s && s.Apagada == pedra, 5);
			await Quadros(4);
			string id = NoSlot()?.Id ?? "";
			Texture2D? icone = id.Length > 0 ? mochila.IconeDoSlotDeTeste(id) : null;
			return (chegou && MesmoDesenho(icone, QuadroDoChao(pedra)), LadoNaTela(mochila, id));
		}

		// O SET DO PALCO NASCE DORMINDO (a espera de nascimento), e a esfera do chao era pedra: a pedra vem primeiro, e o set acorda depois.
		mochila.Abrir();
		(bool pedra, int ladoDaPedra) = srv.PalcoEE_ApagarOSet(eu, apagar: true) ? await SlotCom(pedra: true) : (false, -1);
		Conferir(pedra && mochila.MinhaAbertaDeTeste,
				 "E3. A ESFERA ESTA NA MOCHILA, e com o set dormindo ela e PEDRA: o slot dela, com o MESMO desenho do quadro \"inactive\" da esfera do chao "
				 + $"[slot {NoSlot()?.Id ?? "nenhum"}]");
		Conferir(Ampliada(ladoDaPedra), $"E3b. A PEDRA AMPLIADA (dono: *\"só maior\"*): a bola ocupa {ladoDaPedra} px de lado no slot -- escala inteira, "
										+ $"de pelo menos 3x, dos {bola} px dela no quadro");
		Fotografar("user://esfera-3-mochila-pedra.png");

		(bool laranja, int ladoDaLaranja) = srv.PalcoEE_ApagarOSet(eu, apagar: false) ? await SlotCom(pedra: false) : (false, -1);
		Conferir(laranja, $"E4. O SET ACORDA, E O SLOT VIRA A ESFERA LARANJA: o desenho do quadro \"{numero}\" da esfera acesa do chao [slot {NoSlot()?.Id ?? "nenhum"}]");
		Conferir(Ampliada(ladoDaLaranja), $"E4b. A LARANJA AMPLIADA: a bola ocupa {ladoDaLaranja} px de lado no slot -- escala inteira, de pelo menos 3x, "
										  + $"dos {bola} px dela no quadro");
		Fotografar("user://esfera-4-mochila-laranja.png");

		// CONTRA-EXEMPLO DO TAMANHO: o quadro inteiro pelo `Icon` do botao, o jogo de antes -- a mesma regua tem que reprovar.
		TelaDeInventario.EsferaPequenaNoSlotDeTeste = true;
		int ladoComDefeito;
		try
		{
			mochila.Abrir();   // redesenha a grade, agora com o defeito
			await Quadros(4);
			ladoComDefeito = LadoNaTela(mochila, NoSlot()?.Id ?? "");
			Fotografar("user://esfera-4b-defeito-pequena.png");
		}
		finally { TelaDeInventario.EsferaPequenaNoSlotDeTeste = false; }
		mochila.Abrir();
		await Quadros(4);
		Conferir(ladoComDefeito > 0 && !Ampliada(ladoComDefeito),
				 $"CONTRA-EXEMPLO (defeito injetado: o quadro inteiro pelo `Icon` do botao, o jogo de antes): a bola sai com {ladoComDefeito} px -- a regua "
				 + "de E3b e E4b reprovaria");

		Jandirus.Server.GameServer.EsferaForaDaMochilaDeTeste = true;
		bool someComDefeito;
		try
		{
			srv.PalcoEE_MandarMochila(eu);
			someComDefeito = await Ate(() => NoSlot() == null, 5);
		}
		finally { Jandirus.Server.GameServer.EsferaForaDaMochilaDeTeste = false; }
		Conferir(someComDefeito, "CONTRA-EXEMPLO (defeito injetado: a esfera nao vai no pacote da mochila, o jogo de antes): o slot some -- a regua "
								 + "de E3 e E4 reprovaria");
		srv.PalcoEE_MandarMochila(eu);
		await Ate(() => NoSlot() is { Apagada: false }, 5);

		string id = NoSlot()?.Id ?? "";
		bool abriu = id.Length > 0 && mochila.AbrirAcoesDeTeste(id);
		List<string> acoes = [.. mochila.AcoesAbertasDeTeste().Select(a => a.Texto)];
		bool largou = abriu && acoes.SequenceEqual(["Largar no chão"]) && mochila.ApertarAcaoDeTeste("Largar no chão")
					  && await Ate(() => NoSlot() == null && cli.Esferas.Any(e => e.Tipo == Protocol.CoisaDeEsfera.Esfera && e.Numero == numero), 5);
		Conferir(largou, $"E5. O SLOT LARGA: o painel oferece so \"Largar no chão\", e o clique a poe no chao e a tira da mochila [acoes: {string.Join(", ", acoes)}]");
		await Segundos(0.5);
		Fotografar("user://esfera-5-largou.png");
		mochila.Fechar();
	}

	/// <summary>
	/// O MESMO DESENHO: a parte visivel do icone do slot e a do quadro do chao, pixel a pixel. O slot mostra so a bola (o recorte do quadro,
	/// ampliado na tela), entao o vazio em volta nao entra na conta -- e o quadro inteiro (o jogo de antes) passa tambem.
	/// </summary>
	private static bool MesmoDesenho(Texture2D? icone, Texture2D? chao)
	{
		if (icone?.GetImage() is not { } a || chao?.GetImage() is not { } c || a.IsEmpty() || c.IsEmpty()) return false;
		Image va = a.GetRegion(a.GetUsedRect()), vc = c.GetRegion(c.GetUsedRect());
		va.Convert(Image.Format.Rgba8);
		vc.Convert(Image.Format.Rgba8);
		return va.GetSize() == vc.GetSize() && va.GetData().AsSpan().SequenceEqual(vc.GetData());
	}

	/// <summary>
	/// O LADO DA BOLA COMO O JOGADOR A VE, em pixels de interface: a caixa dos pixels do slot que nao sao o fundo dele, na imagem da tela.
	/// Mede o que foi DESENHADO -- o `Icon` do botao (o jogo de antes) e o retangulo ampliado (o de agora) passam pela mesma regua. -1 sem
	/// slot ou sem imagem; 0 com o slot vazio.
	/// </summary>
	private int LadoNaTela(TelaDeInventario mochila, string itemId)
	{
		Rect2 r = mochila.RetanguloDoSlotDeTeste(itemId);
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (r.Size.X <= 0 || img == null || img.IsEmpty()) return -1;
		float escala = img.GetWidth() / GetViewport().GetVisibleRect().Size.X;
		// POR DENTRO DA BORDA (6 px de folga); o fundo e o meio da beirada esquerda, longe da bola e dos cantos redondos.
		int x0 = (int)((r.Position.X + 6) * escala), y0 = (int)((r.Position.Y + 6) * escala);
		int x1 = Math.Min(img.GetWidth(), (int)((r.End.X - 6) * escala)), y1 = Math.Min(img.GetHeight(), (int)((r.End.Y - 6) * escala));
		Color fundo = img.GetPixel(x0, (y0 + y1) / 2);
		int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
		for (int y = y0; y < y1; y++)
			for (int x = x0; x < x1; x++)
			{
				Color p = img.GetPixel(x, y);
				if (Mathf.Abs(p.R - fundo.R) + Mathf.Abs(p.G - fundo.G) + Mathf.Abs(p.B - fundo.B) <= 40f / 255f) continue;
				minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
				minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
			}
		return maxX < 0 ? 0 : Mathf.RoundToInt(Math.Max(maxX - minX + 1, maxY - minY + 1) / escala);
	}

	private void Fotografar(string destino)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath(destino);
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
		MenuDeInteracao.EsferaSoPeloMenuDeTeste = false;
		TelaDeInventario.EsferaPequenaNoSlotDeTeste = false;
		foreach (string l in _linhas) GD.Print("[desferae] " + l);
		GD.Print(_falhas.Count == 0 ? "[desferae] ===== TUDO OK =====" : $"[desferae] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[desferae]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
