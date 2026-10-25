using Godot;
using Jandirus.Core.Items;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA POTARA (`--diagpotara`, 2026-10-23). O dono: *"deu problema com as potaras"* -- os jogadores faziam o brinco "esquerdo" e o
/// "direito" com a Materialize (roupa, que nao funde ninguem) e a Potara de verdade sumia em 1 s da mao de quem a recebia. A decisao:
/// *"Kaioshin entrega o par"*. Com janela e `--host`, nascendo Humano no campo da Terra:
///   D1. a grade da Materialize (o guarda-roupa inteiro) NAO tem os quatro brincos de roupa -- foto `potara-1-materialize`, rolada ate onde
///       eles ficavam (entre `PhoenixTorsoMakyo` e `PredatorMask`); CONTRA-EXEMPLO: o catalogo do cliente com os quatro de volta no lugar --
///       foto `potara-1b-defeito-com-os-brincos`;
///   D2. um Kaioshin (forjado pelo palco, com o trono) DA o par pelo botao de producao, e a mochila mostra os Brincos Potara com "Jogar no
///       alvo marcado" e "Dar ao alvo marcado" -- foto `potara-2-mochila-com-o-par`;
///   D3. tres segundos depois (o tique de 1 Hz dos cargos rodando de verdade) o par continua -- foto `potara-3-tres-segundos-depois`;
///       CONTRA-EXEMPLO `GameServer.ParVoltaAoCofreDeTeste` (o jogo de antes): o par some -- foto `potara-3b-defeito-sumiu`;
///   D4. o "Jogar no alvo marcado" no parceiro (marcado pelo palco), o aceite dele e a cena: os dois se fundem -- foto `potara-4-fundidos`;
///   D5. e o par se gastou na fusao: a mochila sem ele -- foto `potara-5-par-gasto`.
///
///     Godot --path . --host --rede 11001 --campoteste 12 --horateste 0.5 --semfauna --diagpotara --raca Human --conta bancada_dpotara --nome Fundido
/// </summary>
public partial class RoboDaPotara : Node
{
	private const string Prefixo = "[dpotara] ";
	private static readonly string[] BrincosDeRoupa = ["potara", "Potara-Equipped", "potaraleft", "potararight"];

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

	public override void _ExitTree() => Jandirus.Server.GameServer.ParVoltaAoCofreDeTeste = false;

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		finally
		{
			Jandirus.Server.GameServer.ParVoltaAoCofreDeTeste = false;
			S?.PalcoPtr_Limpar();
		}
		Fechar();
	}

	private void Ouvir(Protocol.Fala canal, string quem, string texto) => _ditas.Add(texto);

	private int Pares(GameClient cli) => cli.Mochila.Quantos(CatalogoDeItens.BrincosPotara);

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null
									  && TelaDeInventario.Instancia != null && TelaDeArmadura.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo, a mochila e a grade nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || TelaDeInventario.Instancia is not { } mochila
			|| TelaDeArmadura.Instancia is not { } grade) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print(Prefixo + "===== O PAR DE BRINCOS POTARA QUE O KAIOSHIN DA (dono, 2026-10-23) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);

		await D1_AGradeDaMaterialize(grade);

		cli.Falou += Ouvir;
		try { await D2aD5_OParDoKaioshin(cli, srv, eu, mochila); }
		finally { cli.Falou -= Ouvir; }
	}

	// ---------------------------------------------------------------- D1
	private async Tarefa D1_AGradeDaMaterialize(TelaDeArmadura grade)
	{
		if (VistosDeGente.Catalogo is not { } cat) { Conferir(false, "D1. (montagem) o cliente tem o catalogo de aparencia"); return; }
		bool abriu = grade.AbrirPraMaterializar();
		await Quadros(4);
		List<string> artes = grade.GradeDeTeste().Artes;
		Conferir(abriu && artes.Count > 100 && !artes.Any(a => a.Contains("potara", StringComparison.OrdinalIgnoreCase)),
				 $"D1. A GRADE DA MATERIALIZE (o guarda-roupa inteiro, {artes.Count} folhas) NAO TEM NENHUM BRINCO POTARA de roupa "
				 + $"[{string.Join(", ", artes.Where(a => a.Contains("otara")))}]");
		await RolarAte(grade, "PredatorMask");
		Fotografar("user://potara-1-materialize.png");

		// CONTRA-EXEMPLO: o catalogo de antes -- os quatro de volta, no lugar em que ficavam (antes da `PredatorMask`).
		string[] caminhos = [.. BrincosDeRoupa.Select(n => $"res://Assets/Sprites/Clothes/{n}.tres")];
		int onde = cat.Roupas.FindIndex(c => c.EndsWith("/PredatorMask.tres", StringComparison.Ordinal));
		List<string> comDefeito;
		try
		{
			cat.Roupas.InsertRange(Math.Max(0, onde), caminhos);
			grade.AbrirPraMaterializar();
			await Quadros(4);
			comDefeito = grade.GradeDeTeste().Artes;
			await RolarAte(grade, "PredatorMask");
			Fotografar("user://potara-1b-defeito-com-os-brincos.png");
		}
		finally { foreach (string c in caminhos) cat.Roupas.Remove(c); }
		grade.Fechar();
		await Quadros(2);
		Conferir(comDefeito.Count(a => a.Contains("potara", StringComparison.OrdinalIgnoreCase)) >= BrincosDeRoupa.Length,
				 "CONTRA-EXEMPLO (defeito injetado: o guarda-roupa de antes, com os quatro brincos): a grade os mostra -- a regua de D1 reprovaria");
	}

	/// <summary>Rola a grade ate o botao desta arte (o no vivo, nao o que o redesenho acabou de soltar).</summary>
	private async Tarefa RolarAte(TelaDeArmadura grade, string arte)
	{
		Button? alvo = grade.FindChildren("*", "Button", true, false).OfType<Button>()
			.FirstOrDefault(b => !b.IsQueuedForDeletion() && b.HasMeta("arte") && b.GetMeta("arte").AsString() == arte);
		Node? n = alvo?.GetParent();
		while (n != null && n is not ScrollContainer) n = n.GetParent();
		if (n is ScrollContainer rolo && alvo != null)
		{
			await Quadros(2);
			rolo.EnsureControlVisible(alvo);
		}
		await Quadros(4);
	}

	// ---------------------------------------------------------------- D2 a D5
	private async Tarefa D2aD5_OParDoKaioshin(GameClient cli, Jandirus.Server.GameServer srv, int eu, TelaDeInventario mochila)
	{
		_ditas.Clear();
		int kaio = srv.PalcoPtr_KaioshinDaOPar(eu);
		bool chegou = kaio > 0 && await Ate(() => Pares(cli) == 1, 5);
		mochila.Abrir();
		await Quadros(6);
		bool abriu = mochila.AbrirAcoesDeTeste(CatalogoDeItens.BrincosPotara);
		await Quadros(3);
		List<string> acoes = [.. mochila.AcoesAbertasDeTeste().Select(a => a.Texto)];
		Conferir(chegou && abriu && acoes.Contains("Jogar no alvo marcado") && acoes.Contains("Dar ao alvo marcado"),
				 "D2. O KAIOSHIN DA O PAR (o botao de producao, com o jogador marcado): a mochila tem os Brincos Potara, com \"Jogar no alvo "
				 + $"marcado\" e \"Dar ao alvo marcado\" [Kaioshin {kaio}, {Pares(cli)} par(es), acoes: {string.Join(", ", acoes)}]");
		Conferir(_ditas.Any(t => t.Contains("te dá um par de brincos Potara", StringComparison.Ordinal)),
				 $"D2b. ...e quem recebe le no chat como usar [ouvi: {string.Join(" | ", _ditas.TakeLast(3))}]");
		Fotografar("user://potara-2-mochila-com-o-par.png");

		// ---- D3: o tique de 1 Hz dos cargos roda de verdade aqui (o servidor deste processo)
		await Segundos(3.2);
		mochila.Abrir();
		await Quadros(6);
		mochila.AbrirAcoesDeTeste(CatalogoDeItens.BrincosPotara);
		await Quadros(3);
		Conferir(Pares(cli) == 1, $"D3. TRES SEGUNDOS DEPOIS (tres tiques dos cargos), O PAR CONTINUA NA MOCHILA -- antes ele voltava pro cofre em 1 s [{Pares(cli)}]");
		Fotografar("user://potara-3-tres-segundos-depois.png");

		bool sumiu;
		Jandirus.Server.GameServer.ParVoltaAoCofreDeTeste = true;
		try
		{
			sumiu = await Ate(() => Pares(cli) == 0, 3);
			mochila.Abrir();
			await Quadros(6);
			Fotografar("user://potara-3b-defeito-sumiu.png");
		}
		finally { Jandirus.Server.GameServer.ParVoltaAoCofreDeTeste = false; }
		Conferir(sumiu, "CONTRA-EXEMPLO (defeito injetado: o par volta pro cofre em 1 s, o jogo de antes): o par some da mochila -- a regua de D3 reprovaria");
		srv.PalcoPtr_DevolverOPar(eu);
		await Ate(() => Pares(cli) == 1, 3);

		// ---- D4: jogar no parceiro marcado, o aceite, a cena
		int par = srv.PalcoPtr_ParceiroMarcado(eu);
		await Segundos(0.8);
		mochila.Abrir();
		await Quadros(6);
		bool jogou = par > 0 && mochila.AbrirAcoesDeTeste(CatalogoDeItens.BrincosPotara) && mochila.ApertarAcaoDeTeste("Jogar no alvo marcado");
		bool convite = jogou && await Ate(() => srv.PalcoPtr_TemConvite(par), 3);
		bool aceitou = convite && srv.PalcoPtr_Aceitar(par);
		mochila.Fechar();
		bool fundiu = aceitou && await Ate(() => srv.PalcoPtr_Fundido(eu), 15);
		await Segundos(3.5);   // a luz da cena escoa antes da foto
		Conferir(fundiu, $"D4. \"JOGAR NO ALVO MARCADO\" NO PARCEIRO, O ACEITE E A CENA: OS DOIS SE FUNDEM [jogou {jogou}, convite {convite}, aceitou {aceitou}]");
		Fotografar("user://potara-4-fundidos.png");

		// ---- D5: o par se gastou
		bool gastou = await Ate(() => Pares(cli) == 0, 3);
		mochila.Abrir();
		await Quadros(6);
		Conferir(gastou && _ditas.Any(t => t.Contains("se gasta na fusão", StringComparison.Ordinal)),
				 $"D5. E O PAR SE GASTOU NA FUSAO: a mochila nao tem mais os brincos, e o chat diz por que [{Pares(cli)} par(es); ouvi: {string.Join(" | ", _ditas.TakeLast(3))}]");
		Fotografar("user://potara-5-par-gasto.png");
		mochila.Fechar();
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
		Jandirus.Server.GameServer.ParVoltaAoCofreDeTeste = false;
		foreach (string l in _linhas) GD.Print(Prefixo + l);
		GD.Print(_falhas.Count == 0 ? Prefixo + "===== TUDO OK =====" : $"{Prefixo}===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print(Prefixo + "  - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
