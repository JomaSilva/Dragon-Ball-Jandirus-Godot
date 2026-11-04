using Godot;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA PATRULHA GALACTICA (`--diagpatrulha`, 2026-10-31, o pedido 198) -- as fotos dos exemplos do dono. Com janela e `--host` (o
/// servidor no mesmo processo; o palco e `GameServer.PatrulhaGalacticaPalco.cs`), o host na Terra, longe da zona segura:
///   P1. ele mata cinco habitantes (o gancho de verdade) e vira PROCURADO -- foto `patrulha-1-procurado`;
///   P2. a nave da Patrulha pousa ao lado e o esquadrao desce, de armadura do exercito do Freeza -- foto `patrulha-2-a-nave-pousa`;
///   P3. os patrulheiros (a IA deles) o nocauteiam e ele acorda PRESO no planeta prisao -- foto `patrulha-3-preso` (se a IA nao o derrubar
///       a tempo, o atalho do palco o faz pelo funil da derrota, e o log diz);
///   P4. ele tenta decolar e o campo de forca recusa -- foto `patrulha-4-o-campo-de-forca`;
///   P5. solto, ele denuncia um procurado forjado ao lado -- foto `patrulha-5-a-denuncia`;
///   P6. derruba o procurado e o ENTREGA a Patrulha: a recompensa inteira -- foto `patrulha-6-a-entrega`;
///   P7. no nivel 5, derruba o esquadrao inteiro e o Bills desce atras dele -- foto `patrulha-7-o-bills-desce`;
///   P8. a edicao do Jornal Galactico sai: o aviso no canto inferior direito -- foto `patrulha-8-o-aviso-do-jornal`;
///   P9. "Ler" abre a janela holografica com as noticias do dia -- foto `patrulha-9-o-jornal-galactico`.
///
///     Godot --path . --host --rede 8488 --campoteste 8 --horateste 0.5 --semfauna --diagpatrulha --raca Human --conta bancada_patrulha_r --nome Foragido
/// </summary>
public partial class RoboDaPatrulha : Node
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
		GD.Print("[dpatrulha] ===== A PATRULHA GALACTICA (dono, 2026-10-31, o pedido 198) =====");
		int eu = cli.LocalId;
		cli.Falou += AoFalar;
		srv.EmbateDeFoto_CeuLimpo(eu);
		srv.PalcoPG_ForaDaZonaSegura(eu);
		await Segundos(2.0);

		// ------------------------------------------------------------ P1. procurado
		_falas.Clear();
		int nivel = srv.PalcoPG_Procurado(eu, 3);
		bool procurado = await Ate(() => Ouviu("você agora é PROCURADO"), 5);
		await Segundos(0.8);
		Conferir(procurado && nivel == 3, $"P1. **CINCO HABITANTES MORTOS E ELE E PROCURADO** (nivel {nivel}) [{Ultimas()}]");
		Fotografar("patrulha-1-procurado");

		// ------------------------------------------------------------ P2. as naves pousam (o pedido 202: uma por patrulheiro, uma a uma)
		_falas.Clear();
		int vem = srv.PalcoPG_PousarJa(eu);
		bool viu = await Ate(() => Ouviu("naves da Patrulha descem do céu perto de você"), 5);
		await Segundos(0.4);
		(int nasPrimeiras, _, int aindaNoCeu) = srv.PalcoPG_Esquadrao(eu);
		Fotografar("patrulha-2a-as-naves-descendo");
		bool todas = await Ate(() => srv.PalcoPG_Esquadrao(eu) is { NoCeu: 0 } e && e.Naves == vem && e.Patrulheiros.Count == vem, 15);
		(int naves, List<int> esquadrao, _) = srv.PalcoPG_Esquadrao(eu);
		await Segundos(1.0);
		Conferir(viu && aindaNoCeu > 0 && todas && vem == Jandirus.Core.Npc.PatrulhaGalactica.QuantosPatrulheiros(nivel) && srv.PalcoPG_NavesEspalhadas(eu),
				 $"P2. **AS NAVES POUSAM UMA A UMA, CADA UMA COM A SUA CRATERA E UM PATRULHEIRO** (o pedido 202): {naves} nave(s), "
				 + $"{esquadrao.Count} patrulheiro(s); no primeiro instante, {nasPrimeiras} no chao e {aindaNoCeu} no ceu [{Ultimas()}]");
		Fotografar("patrulha-2-as-naves-pousaram");
		// (2026-10-31, o pedido 202) E DE PERTO: a Spacepod no meio da cratera dela -- daqui do host os centros das crateras ficavam fora da
		// tela ou atras do painel do canto.
		bool pertoDaNave = srv.PalcoPG_PertoDeUmaNave(eu);
		await Segundos(1.5);
		Conferir(pertoDaNave, "P2b. **DE PERTO, UMA DAS NAVES NA CRATERA DELA** (o host a tres tiles ao sul da primeira)");
		Fotografar("patrulha-2b-uma-nave-e-a-cratera");

		// ------------------------------------------------------------ P3. nocauteado e preso
		_falas.Clear();
		bool pelaIa = await Ate(() => srv.PalcoPG_Preso(eu), 45);
		if (!pelaIa)
		{
			GD.Print("[dpatrulha] a IA nao o derrubou em 45 s: o atalho do palco o nocauteia pelo funil da derrota");
			srv.PalcoPG_Nocautear(eu);
		}
		bool preso = await Ate(() => srv.PalcoPG_Preso(eu), 8);
		await Segundos(2.5);
		Conferir(preso && Ouviu("você foi preso"),
				 $"P3. **NOCAUTEADO PELA PATRULHA, ELE ACORDA PRESO NO PLANETA PRISAO** ({(pelaIa ? "a IA o derrubou" : "pelo atalho")}) [{Ultimas()}]");
		Fotografar("patrulha-3-preso");

		// ------------------------------------------------------------ P4. o campo de forca
		_falas.Clear();
		srv.DecolarNaFoto(eu);
		bool recusou = await Ate(() => Ouviu("um campo de força envolve"), 5);
		await Segundos(0.8);
		Conferir(recusou && srv.PalcoPG_Preso(eu), $"P4. **O CAMPO DE FORCA RECUSA A DECOLAGEM** [{Ultimas()}]");
		Fotografar("patrulha-4-o-campo-de-forca");

		// ------------------------------------------------------------ P5. a denuncia
		srv.PalcoPG_Soltar(eu);
		await Segundos(2.5);
		_falas.Clear();
		int outro = srv.PalcoPG_OutroProcurado(eu, "Raditz Foragido", 2);
		await Segundos(1.0);
		cli.SendVerbo(Jandirus.Server.GameServer.VerboDenunciar);
		bool denunciou = await Ate(() => Ouviu("você denuncia Raditz Foragido"), 5);
		await Segundos(0.8);
		Conferir(outro != 0 && denunciou, $"P5. **A DENUNCIA**: o procurado do lado e anotado, e a prisao paga 25% [{Ultimas()}]");
		Fotografar("patrulha-5-a-denuncia");

		// ------------------------------------------------------------ P6. a entrega
		_falas.Clear();
		srv.PalcoPG_Derrubar(eu, outro);
		await Segundos(0.6);
		cli.SendVerbo(Jandirus.Server.GameServer.VerboChamar);
		bool entregou = await Ate(() => Ouviu("recolhem o corpo desmaiado de Raditz Foragido") && Ouviu("a recompensa pela entrega"), 5);
		await Segundos(0.8);
		Conferir(entregou, $"P6. **A ENTREGA**: a Patrulha leva o corpo desmaiado, e a recompensa inteira e de quem o derrubou [{Ultimas()}]");
		Fotografar("patrulha-6-a-entrega");

		// ------------------------------------------------------------ P7. o Bills
		_falas.Clear();
		int bills = srv.PalcoPG_ChamarOBills(eu);
		bool desceu = await Ate(() => Ouviu("BILLS, desce em pessoa"), 5);
		await Segundos(1.5);
		Conferir(bills != 0 && desceu, $"P7. **NO NIVEL 5, O ESQUADRAO DERRUBADO CHAMA O DEUS DA DESTRUICAO: O BILLS DESCE** [{Ultimas()}]");
		Fotografar("patrulha-7-o-bills-desce");
		srv.PalcoPG_EncerrarTudo(eu);
		await Segundos(1.5);

		// ------------------------------------------------------------ P8. o aviso do jornal
		srv.PalcoPG_PublicarJornal();
		bool aviso = await Ate(() => AvisoDoJornal.Instancia is { NaTela: true }, 5);
		await Segundos(0.8);
		Conferir(aviso && C.Jornal is { Noticias.Count: > 0 },
				 $"P8. **A EDICAO DO JORNAL GALACTICO SAI: O AVISO NO CANTO INFERIOR DIREITO** ['{AvisoDoJornal.Instancia?.TextoDesenhado}']");
		Fotografar("patrulha-8-o-aviso-do-jornal");

		// ------------------------------------------------------------ P9. a janela holografica
		AvisoDoJornal.Instancia?.LerDeTeste();
		bool abriu = await Ate(() => JornalGalactico.Instancia is { NaTela: true }, 3);
		await Segundos(1.2);
		List<(string Selo, string Texto)> noticias = JornalGalactico.Instancia?.NoticiasDesenhadas() ?? [];
		Rect2 tela = GetViewport().GetVisibleRect();
		bool cabe = JornalGalactico.Instancia is { } j && tela.Encloses(j.RetanguloDaJanela);
		Conferir(abriu && cabe && noticias.Exists(n => n.Selo == "PROCURADO") && noticias.Exists(n => n.Selo == "PRISÃO"),
				 $"P9. **\"Ler\" ABRE A JANELA HOLOGRAFICA**: inteira na tela, com as noticias do dia [{string.Join(" / ", noticias.Select(n => n.Selo))}]");
		Fotografar("patrulha-9-o-jornal-galactico");
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
		foreach (string l in _linhas) GD.Print("[dpatrulha] " + l);
		GD.Print(_falhas.Count == 0 ? "[dpatrulha] ===== TUDO OK =====" : $"[dpatrulha] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dpatrulha]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
