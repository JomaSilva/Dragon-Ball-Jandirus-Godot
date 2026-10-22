using Godot;
using Jandirus.Core.Magic;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA ESTRELA NEGRA (`--diagestrelanegra`, 2026-10-21): as Black Star Dragon Balls na tela, com foto de cada passo -- o dono pediu
/// os prints. Com janela e `--host`, nascendo Humano no campo da Terra:
///   N0. a folha e a escala: a esfera acesa usa o sprite das estrelas pretas, a pedra a 8a celula, o Shenlong vermelho a folha dele, na
///       altura do Shenron de hoje; CONTRA-EXEMPLO (`EsferaDesenhada.ShenlongSemEscalaDeTeste`): a arte crua, 878 px de altura;
///   N1. as sete Black Star em volta do corpo (o palco as traz, acesas): as sete no pacote e desenhadas com a folha "negra" -- fotos
///       `negra-1-chao` e `negra-1b-perto` (o recorte ampliado);
///   N1c. o Dragon Radar: as sete em VERMELHO e uma esfera comum em AMARELO, no pixel da tela; CONTRA-EXEMPLO
///       (`RadarNaTela.PintaTudoDeAmareloDeTeste`): tudo amarelo -- foto `negra-1c-radar`;
///   N2. a tecla E de verdade pega a numero 1 ("voce pega a Esfera de 1 estrela negra", e ela sai do chao) e ela esta na mochila -- fotos
///       `negra-2-pegou` e `negra-2b-mochila`;
///   N3. o `db_invocar` de producao: o Shenlong vermelho sobe no mundo, na altura certa -- foto `negra-3-shenlong`;
///   N4. a janela do dragao abre com o nome dele -- foto `negra-4-desejo`.
/// A FOTO SO EXISTE DEPOIS DO `--import` do png novo do Shenlong: no ciclo, o caso `importar` roda antes deste, dentro da fila.
///
///     Godot --path . --host --rede 10131 --campoteste 12 --horateste 0.5 --semfauna --diagestrelanegra --raca Human --conta bancada_negra_foto --nome Negro
/// </summary>
public partial class RoboDaEstrelaNegra : Node
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
		AsFolhas();

		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null
									  && MenuDeInteracao.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o menu da tecla E nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[diagnegra] ===== AS BLACK STAR DRAGON BALLS (dono, 2026-10-21) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);

		// N1. as sete no chao, em volta do corpo
		int postas = srv.PalcoEN_SeteAoLado(eu);
		bool chegaram = postas == Esferas.Total && await Ate(() => NegrasNoChao(cli).Count == Esferas.Total, 10);
		await Segundos(1.0);
		List<EsferaDesenhada> desenhadas = Desenhadas(n => n.Tipo == Protocol.CoisaDeEsfera.Esfera && n.Folha == EsferasNegras.Folha);
		Conferir(chegaram && NegrasNoChao(cli).All(e => !e.Inerte)
				 && Enumerable.Range(1, Esferas.Total).All(n => NegrasNoChao(cli).Any(e => e.Numero == n)),
				 $"N1. AS SETE BLACK STAR NO CHAO, ACESAS, de 1 a 7 [no pacote {NegrasNoChao(cli).Count}; postas {postas}]");
		Conferir(desenhadas.Count == Esferas.Total
				 && desenhadas.All(d => Sprite(d) is { } s && s.Animation == d.Numero.ToString(System.Globalization.CultureInfo.InvariantCulture)),
				 $"N1b. AS SETE DESENHADAS COM A FOLHA DAS ESTRELAS PRETAS, cada uma no quadro do seu numero [nodes {desenhadas.Count}: "
				 + $"{string.Join(", ", desenhadas.Select(d => $"{d.Numero}={Sprite(d)?.Animation}"))}]");
		Fotografar("user://negra-1-chao.png");
		FotografarDePerto("user://negra-1b-perto.png");

		// N1c. (2026-10-21) O RADAR: as sete Black Star em VERMELHO e uma esfera comum em AMARELO (dono: *"faca no radar do dragao vermelho
		// ser as black star dragon ball e amarelo as normais"*). A cor e lida no PIXEL do centro de cada ponto, e nao so na marca dele.
		bool armou = srv.PalcoEN_RadarComUmaComum(eu);
		bool chegou = armou && await Ate(() => RadarNaTela.Instancia is { Visible: true } r
										 && r.PontosDeTeste.Count(p => p.Negra) == Esferas.Total && r.PontosDeTeste.Count(p => !p.Negra) == 1, 8);
		await Quadros(6);
		(int vermelhas, int amarelas) = CoresNoDisco();
		Conferir(chegou && vermelhas == Esferas.Total && amarelas == 1,
				 $"N1c. O RADAR PINTA AS SETE BLACK STAR DE VERMELHO E A COMUM DE AMARELO [vermelhas {vermelhas}, amarelas {amarelas}]");
		Fotografar("user://negra-1c-radar.png");
		try
		{
			RadarNaTela.PintaTudoDeAmareloDeTeste = true;
			await Quadros(6);
			(int vDefeito, int aDefeito) = CoresNoDisco();
			Conferir(vDefeito == 0 && aDefeito == Esferas.Total + 1,
					 $"N1d. (defeito injetado: o disco ignora a marca e pinta tudo de amarelo) a mesma regua REPROVA [vermelhas {vDefeito}, amarelas {aDefeito}]");
		}
		finally { RadarNaTela.PintaTudoDeAmareloDeTeste = false; }
		srv.PalcoEN_TirarAComum(eu);

		// N2. a tecla E de verdade pega a numero 1
		_ditas.Clear();
		cli.Falou += Ouvir;
		try
		{
			GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = true });
			GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = false });
			string fala = $"você pega a {EsferasNegras.NomeDaEsfera(1)}";
			bool pegou = await Ate(() => _ditas.Any(t => t.StartsWith(fala, StringComparison.Ordinal))
										 && !NegrasNoChao(cli).Any(e => e.Numero == 1), 5);
			Conferir(pegou, $"N2. A TECLA E PEGA A BLACK STAR DE 1 ESTRELA: o servidor diz que pegou, e ela sai do chao [ouvi: {string.Join(" | ", _ditas.TakeLast(2))}]");
		}
		finally { cli.Falou -= Ouvir; }
		await Segundos(0.5);
		Fotografar("user://negra-2-pegou.png");

		bool naMochila = await Ate(() => cli.Mochila.Pilhas.Any(p => EsferaNaMochila.Ler(p.Id) is { Negra: true, Numero: 1 }), 5);
		Conferir(naMochila, $"N2b. A BLACK STAR ESTA NA MOCHILA [{string.Join(", ", cli.Mochila.Pilhas.Select(p => p.Id).Where(i => i.StartsWith("Esfera", StringComparison.Ordinal)))}]");
		if (TelaDeInventario.Instancia is { } mochila)
		{
			mochila.Abrir();
			await Segundos(0.6);
			Fotografar("user://negra-2b-mochila.png");
			mochila.Fechar();
			await Quadros(4);
		}

		// N3. o db_invocar de producao: o Shenlong vermelho sobe
		cli.SendVerbo("db_invocar");
		bool subiu = await Ate(() => cli.Esferas.Any(e => e.Tipo == Protocol.CoisaDeEsfera.Dragao && e.Folha == EsferasNegras.Dragao), 10);
		await Segundos(2.0);
		EsferaDesenhada? dragao = Desenhadas(n => n.Tipo == Protocol.CoisaDeEsfera.Dragao && n.Folha == EsferasNegras.Dragao).FirstOrDefault();
		float altura = dragao != null ? AlturaDesenhada(dragao) : -1;
		Conferir(subiu && dragao != null && Mathf.Abs(altura - EsferasNegras.AlturaDoShenronDeHoje) < 1f,
				 $"N3. O DB_INVOCAR LEVANTA O SHENLONG VERMELHO, na altura do Shenron de hoje [no pacote {subiu}; desenhado {dragao != null}; "
				 + $"altura {altura:0.#} px]");
		Fotografar("user://negra-3-shenlong.png");

		// N4. a janela do desejo, com o nome dele
		bool janela = await Ate(() => MenuDeDesejos.Instancia is { NaTela: true } m
									  && m.NomeDesenhado.Contains(EsferasNegras.NomeDoDragao, StringComparison.OrdinalIgnoreCase), 8);
		await Segundos(0.6);
		Conferir(janela, $"N4. A JANELA DO DESEJO ABRE COM O NOME DO DRAGAO [\"{MenuDeDesejos.Instancia?.NomeDesenhado}\"]");
		Fotografar("user://negra-4-desejo.png");

		srv.PalcoEN_Encerrar();
	}

	/// <summary>
	/// (2026-10-21) N1c. AS CORES DO DISCO, NO PIXEL: o centro de cada ponto que o `RadarNaTela` pintou, lido na imagem da tela (o disco e
	/// HUD, sem a luz do mundo). Vermelho = R alto, G e B baixos; amarelo = R e G altos, B baixo.
	/// </summary>
	private (int Vermelhas, int Amarelas) CoresNoDisco()
	{
		if (RadarNaTela.Instancia is not { } r || GetViewport()?.GetTexture()?.GetImage() is not { } img || img.IsEmpty()) return (-1, -1);
		Transform2D t = r.GetGlobalTransformWithCanvas();
		int v = 0, a = 0;
		foreach (RadarNaTela.Ponto p in r.PontosDeTeste)
		{
			Vector2 g = t * p.Onde;
			int x = (int)MathF.Round(g.X), y = (int)MathF.Round(g.Y);
			if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) continue;
			Color c = img.GetPixel(x, y);
			if (c.R > 0.7f && c.G < 0.45f && c.B < 0.4f) v++;
			else if (c.R > 0.7f && c.G > 0.6f && c.B < 0.45f) a++;
		}
		return (v, a);
	}

	/// <summary>N0. A folha e a escala, sem tela: o que a `EsferaDesenhada` usa pras Black Star e pro Shenlong.</summary>
	private void AsFolhas()
	{
		(string arte, string estado) = EsferaDesenhada.FolhaDe(Protocol.CoisaDeEsfera.Esfera, EsferasNegras.Folha, 4, false);
		Conferir(arte == EsferasNegras.ArteDaEsfera && estado == "4", $"N0. a esfera acesa usa o sprite das estrelas pretas, estado 4 [{arte} / {estado}]");
		(_, string pedra) = EsferaDesenhada.FolhaDe(Protocol.CoisaDeEsfera.Esfera, EsferasNegras.Folha, 3, true);
		Conferir(pedra == EsferasNegras.EstadoDaPedra, $"N0. a pedra aponta pra 8a celula (\"default\"), nao pra \"inactive\" [{pedra}]");
		(string arteDoDragao, string estadoDoDragao) = EsferaDesenhada.FolhaDe(Protocol.CoisaDeEsfera.Dragao, EsferasNegras.Dragao, 0, false);
		Conferir(arteDoDragao == EsferasNegras.ArteDoShenlong && estadoDoDragao == EsferasNegras.Dragao,
				 $"N0. o Shenlong vermelho tem folha propria [{arteDoDragao} / {estadoDoDragao}]");

		bool Certa() => Mathf.Abs(EsferaDesenhada.EscalaDoDragao(EsferasNegras.Dragao) * EsferasNegras.AlturaDaArteDoShenlong
								  - EsferasNegras.AlturaDoShenronDeHoje) < 0.5f;
		Conferir(Certa(), "N0. a escala deixa o Shenlong na altura do Shenron de hoje");
		EsferaDesenhada.ShenlongSemEscalaDeTeste = true;
		bool comDefeito;
		try { comDefeito = Certa(); }
		finally { EsferaDesenhada.ShenlongSemEscalaDeTeste = false; }
		Conferir(!comDefeito, "CONTRA-EXEMPLO (defeito injetado: o Shenlong na arte crua, sem escala): a mesma regua de N0 REPROVA");
	}

	private static List<GameClient.EsferaInfo> NegrasNoChao(GameClient cli) =>
		[.. cli.Esferas.Where(e => e.Tipo == Protocol.CoisaDeEsfera.Esfera && e.Folha == EsferasNegras.Folha)];

	/// <summary>Os nodes de esfera DESENHADOS no mundo agora (os vivos, nao os que vao sair no fim do quadro).</summary>
	private static List<EsferaDesenhada> Desenhadas(Func<EsferaDesenhada, bool> filtro)
	{
		var achados = new List<EsferaDesenhada>();
		void Varrer(Node n)
		{
			foreach (Node f in n.GetChildren())
			{
				if (f.IsQueuedForDeletion()) continue;
				if (f is EsferaDesenhada d && filtro(d)) achados.Add(d);
				Varrer(f);
			}
		}
		if (World.Instancia is { } w) Varrer(w);
		return achados;
	}

	private static AnimatedSprite2D? Sprite(EsferaDesenhada d) => d.GetChildren().OfType<AnimatedSprite2D>().FirstOrDefault();

	/// <summary>A altura do dragao como ele SAI na tela: o quadro da animacao vezes a escala do sprite.</summary>
	private static float AlturaDesenhada(EsferaDesenhada d)
	{
		if (Sprite(d) is not { SpriteFrames: { } folha } s) return -1;
		Texture2D? q = folha.GetFrameTexture(s.Animation, 0);
		return q == null ? -1 : q.GetHeight() * Mathf.Abs(s.Scale.Y) * Mathf.Abs(d.Scale.Y);
	}

	private void Fotografar(string destino)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath(destino);
		img.SavePng(caminho);
		_linhas.Add("  --     foto salva em " + caminho);
	}

	/// <summary>O MEIO DA TELA, ampliado 2x sem filtro: o corpo e as sete esferas em volta dele, com o desenho de cada estrela legivel.</summary>
	private void FotografarDePerto(string destino)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto de perto (headless nao renderiza)"); return; }
		int w = 320, h = 240;
		int x0 = Math.Max(0, img.GetWidth() / 2 - w / 2), y0 = Math.Max(0, img.GetHeight() / 2 - h / 2);
		Image perto = img.GetRegion(new Rect2I(x0, y0, Math.Min(w, img.GetWidth() - x0), Math.Min(h, img.GetHeight() - y0)));
		perto.Resize(perto.GetWidth() * 2, perto.GetHeight() * 2, Image.Interpolation.Nearest);
		string caminho = ProjectSettings.GlobalizePath(destino);
		perto.SavePng(caminho);
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

	public override void _ExitTree() => EsferaDesenhada.ShenlongSemEscalaDeTeste = false;

	private void Fechar()
	{
		EsferaDesenhada.ShenlongSemEscalaDeTeste = false;
		foreach (string l in _linhas) GD.Print("[diagnegra] " + l);
		GD.Print(_falhas.Count == 0 ? "[diagnegra] ===== TUDO OK =====" : $"[diagnegra] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[diagnegra]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
