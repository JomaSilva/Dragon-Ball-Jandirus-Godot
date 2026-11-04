using Godot;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DOS VILOES SOLTOS (`--diagsoltos`, 2026-10-31, o pedido 198g) -- as fotos dos exemplos do dono. Com janela e `--host` (o palco e
/// `GameServer.ViloesSoltosPalco.cs`), o host em Arconia, longe da zona segura (a vitoria do Cell acende a destruicao da Terra):
///   V1. o Freeza vence em Namek, fica solto e invade o planeta do host: o corpo nasce e as naves do imperio desembarcam -- sem aviso no
///       chat de onde ele esta (o pedido 200: so a cena de chegada de quem esta no planeta) -- foto `soltos-1-o-freeza-invade`;
///   V2. alguns segundos depois, o exercito espalhado atacando o povo -- foto `soltos-2-o-exercito`;
///   V3. o Cell vence e vem atras do mais forte (o host), tambem sem aviso no chat -- foto `soltos-3-o-cell-caca`;
///   V4. a revanche: o host derrota o Cell, e o mundo ouve -- foto `soltos-4-a-revanche`;
///   V5. o Jornal Galactico do dia conta tudo -- foto `soltos-5-no-jornal`;
///   V6. (o pedido 206) o Freeza invade Icer e ninguem o vence nos dois dias: ele sai, some e dias depois ataca outro planeta -- so o jornal
///       conta -- foto `soltos-6-sumiu-e-voltou`.
///
///     Godot --path . --host --rede 8489 --campoteste 8 --horateste 0.5 --semfauna --diagsoltos --raca Human --conta bancada_soltos_r --nome Revanche
/// </summary>
public partial class RoboDosViloesSoltos : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _falas = [];

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

	private void AoFalar(Protocol.Fala canal, string autor, string texto) => _falas.Add(texto);

	private bool Ouviu(string trecho) => _falas.Exists(f => f.Contains(trecho, StringComparison.Ordinal));

	private string Ultimas() => string.Join(" | ", _falas.TakeLast(3));

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null && Hud.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o HUD nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		GD.Print("[dsoltos] ===== OS VILOES DE SAGA SOLTOS (dono, 2026-10-31, o pedido 198g) =====");
		int eu = cli.LocalId;
		cli.Falou += AoFalar;
		srv.PalcoSV_IrPara(eu, "Arconia");
		await Segundos(2.5);
		srv.EmbateDeFoto_CeuLimpo(eu);
		srv.PalcoSV_Forte(eu);   // o chefe solto luta pra matar: o host tem de sobreviver as cinco fotos
		await Segundos(1.0);

		// ------------------------------------------------------------ V1. o Freeza invade
		_falas.Clear();
		bool solto = srv.PalcoSV_Vencer(eu, "freeza_namek", 2);
		srv.TickDosSoltos();
		bool nasceu = await Ate(() => srv.PalcoSV_Estado("freeza_namek").Corpo != 0, 5);
		srv.PalcoSV_Perto(eu, "freeza_namek", 4);
		await Segundos(2.5);
		(int corpo, int soldados) = srv.PalcoSV_Estado("freeza_namek");
		// (o pedido 200) o chat nao diz pra onde ele foi -- a frase do mundo "o império dele invade" sumiu; quem esta aqui ve a cena de chegada
		Conferir(solto && nasceu && soldados >= Jandirus.Core.Npc.ViloesSoltos.SoldadosNoDesembarque && !Ouviu("o império dele invade"),
				 $"V1. **O FREEZA VENCE EM NAMEK, FICA SOLTO E INVADE O PLANETA DO HOST, COM O EXERCITO -- SEM AVISO NO CHAT DE ONDE ELE ESTA** "
				 + $"(corpo #{corpo}, {soldados} soldado(s)) [{Ultimas()}]");
		Fotografar("soltos-1-o-freeza-invade");

		// ------------------------------------------------------------ V2. o exercito
		await Segundos(6.0);
		srv.PalcoSV_Perto(eu, "freeza_namek", 4);   // o Freeza de volta na frente da camera (o kiai dele joga o host longe)
		await Segundos(0.6);
		(_, soldados) = srv.PalcoSV_Estado("freeza_namek");
		Conferir(soldados > Jandirus.Core.Npc.ViloesSoltos.SoldadosNoDesembarque, $"V2. **O RESTO DO EXERCITO CHEGA** ({soldados} soldado(s))");
		Fotografar("soltos-2-o-exercito");
		srv.PalcoSV_Encerrar();
		await Segundos(1.5);

		// ------------------------------------------------------------ V3. o Cell caca o mais forte
		_falas.Clear();
		bool cell = srv.PalcoSV_Vencer(eu, "cell", 2);
		srv.TickDosSoltos();
		bool veio = await Ate(() => srv.PalcoSV_Estado("cell").Corpo != 0, 5);
		srv.PalcoSV_Perto(eu, "cell", 3);
		await Segundos(2.5);
		Conferir(cell && veio && !Ouviu("caça os mais fortes"), $"V3. **O CELL VENCE E VEM ATRAS DO MAIS FORTE -- SEM AVISO NO CHAT** [{Ultimas()}]");
		Fotografar("soltos-3-o-cell-caca");

		// ------------------------------------------------------------ V4. a revanche
		_falas.Clear();
		srv.PalcoSV_Derrotar(eu, "cell");
		bool derrotou = await Ate(() => Ouviu("derrotou Cell") || Ouviu("derrotou Cell Perfeito") || _falas.Exists(f => f.Contains("derrotou") && f.Contains("Cell")), 5);
		await Segundos(1.5);
		Conferir(derrotou && srv.PalcoSV_Estado("cell").Corpo == 0, $"V4. **A REVANCHE: O HOST DERROTA O CELL, E O MUNDO OUVE** [{Ultimas()}]");
		Fotografar("soltos-4-a-revanche");

		// ------------------------------------------------------------ V5. no jornal
		await Segundos(5.0);   // a cena de chegada do Cell sai da tela antes da janela abrir
		srv.PalcoPG_PublicarJornal();
		bool aviso = await Ate(() => AvisoDoJornal.Instancia is { NaTela: true }, 5);
		AvisoDoJornal.Instancia?.LerDeTeste();
		bool abriu = await Ate(() => JornalGalactico.Instancia is { NaTela: true }, 3);
		await Segundos(1.2);
		List<(string Selo, string Texto)> noticias = JornalGalactico.Instancia?.NoticiasDesenhadas() ?? [];
		Conferir(aviso && abriu && noticias.Exists(n => n.Selo == "AMEAÇA") && noticias.Exists(n => n.Selo == "HERÓI"),
				 $"V5. **O JORNAL GALACTICO CONTA A INVASAO E A REVANCHE** [{string.Join(" / ", noticias.Select(n => n.Selo))}]");
		Fotografar("soltos-5-no-jornal");
		srv.PalcoSV_Encerrar();
		JornalGalactico.Instancia?.Fechar();
		await Segundos(1.5);

		// ------------------------------------------------------------ V6. (o pedido 206) o prazo acaba sem ninguem vence-lo
		// O Freeza invade Icer, longe do host (sem luta), o prazo dele acaba e ele SAI; os dias sumido acabam e ele ataca outro planeta. Os
		// dois dias e os tres sumido sao medidos pela `--soltosteste` (S8-S8d); aqui o desfecho, como o jogador o le: no jornal do dia.
		_falas.Clear();
		bool solto6 = srv.PalcoSV_Vencer(eu, "freeza_namek", 2, "Icer");
		srv.PalcoSV_PrazoAcaba("freeza_namek");
		bool sumiu = srv.PalcoSV_Onde("freeza_namek").Sumido;
		srv.PalcoSV_Volta("freeza_namek");
		(bool aindaSumido, string ondeVoltou) = srv.PalcoSV_Onde("freeza_namek");
		srv.PalcoPG_PublicarJornal();
		bool aviso6 = await Ate(() => AvisoDoJornal.Instancia is { NaTela: true }, 5);
		AvisoDoJornal.Instancia?.LerDeTeste();
		bool abriu6 = await Ate(() => JornalGalactico.Instancia is { NaTela: true }, 3);
		await Segundos(1.2);
		List<(string Selo, string Texto)> noticias6 = JornalGalactico.Instancia?.NoticiasDesenhadas() ?? [];
		Conferir(solto6 && sumiu && !aindaSumido && ondeVoltou != "Icer" && aviso6 && abriu6
				 && noticias6.Exists(n => n.Texto.Contains("deixaram Icer", StringComparison.Ordinal))
				 && noticias6.Exists(n => n.Texto.Contains("voltaram", StringComparison.Ordinal))
				 && !Ouviu("deixaram") && !Ouviu("voltaram"),
				 $"V6. **SEM NINGUEM VENCE-LO NOS DOIS DIAS, O FREEZA SAI DE ICER, SOME, E DIAS DEPOIS ATACA OUTRO PLANETA ({ondeVoltou})** -- "
				 + $"so o jornal conta (o pedido 206) [{string.Join(" / ", noticias6.Select(n => n.Texto))}]");
		Fotografar("soltos-6-sumiu-e-voltou");
		srv.PalcoSV_Encerrar();
	}

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
		if (C is { } cli) cli.Falou -= AoFalar;
		foreach (string l in _linhas) GD.Print("[dsoltos] " + l);
		GD.Print(_falhas.Count == 0 ? "[dsoltos] ===== TUDO OK =====" : $"[dsoltos] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dsoltos]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
