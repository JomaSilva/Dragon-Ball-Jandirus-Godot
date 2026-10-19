using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Stats;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DOS PESOS E DO FREEZA (`--diagpesosfreeza`, 2026-10-18): as fotos de dois pedidos do dono --
///   * *"quando vc tem 2 itens do mesmo tipo no inventario o jogo acha q os dois sao o mesmo item"* / *"fiz um peso pra um amigo e quando fui
///     dropar veio com o mesmo peso do meu ate o icone"*: os pesos viraram item com estado (`PesoDeTreino`);
///   * *"o freeza n ta usando death beam"*: o tanque do chefe de saga ganhou piso (`Fighter.PisoDoTanqueDeChefe`).
///
/// Com janela e com `--host`, nascendo Humano no campo aberto da Terra:
///   P1. dois pesos na mochila -- o de fabrica e um de 200 kg com outra arte: dois icones, dois nomes -- foto `pesos-1-mochila.png`;
///   P2. a aba Equip, um bloco por peso, cada um com o maximo dele -- foto `pesos-2-equip.png`;
///   P3. o de 200 kg largado pela mochila cai com a arte DELE -- foto `pesos-3-chao.png`;
///   P4. CONTRA-EXEMPLO (`PesoDeTreino.EstadoForaDoIdDeTeste`): a arte escolhida pela grade nao fica no peso (o item sem estado) -- foto
///       `pesos-4-defeito.png`;
///   F1. o Freeza de saga contra um saco do mesmo poder, pelo tique de producao: o Death Beam sai da mao dele -- fotos `freeza-1-deathbeam-*.png`;
///   F2. CONTRA-EXEMPLO (`Fighter.ChefeSemPisoDeTanqueDeTeste`): o tanque de NPC de antes -- 25 s de luta sem um Death Beam -- foto
///       `freeza-2-defeito.png`.
///
///     Godot --path . --host --rede 8468 --campoteste 12 --horateste 0.5 --semfauna --diagpesosfreeza --raca Human --conta bancada_pesosfreeza --nome Fotografo
/// </summary>
public partial class RoboDosPesosEDoFreeza : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

	private static GameClient? C => GameClient.Instance;
	private static MenuJogo? M => MenuJogo.Instancia;
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

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && M != null && S != null
									  && TelaDeInventario.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo, o menu e a mochila nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || M is not { } menu || S is not { } srv || TelaDeInventario.Instancia is not { } mochila) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dpesosfreeza] ===== OS PESOS COM ESTADO E O DEATH BEAM DO FREEZA, NA TELA (dono, 2026-10-18) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);

		await OsPesos(cli, menu, srv, mochila, eu);
		await OFreeza(srv, eu);
	}

	// =====================================================================
	// P) OS PESOS
	// =====================================================================
	private async Tarefa OsPesos(GameClient cli, MenuJogo menu, Jandirus.Server.GameServer srv, TelaDeInventario mochila, int eu)
	{
		srv.PalcoV21_Limpar(eu);
		(string arte, string caminho) = srv.PalcoPF_UmaArte();
		string comArte = PesoDeTreino.IdDe(200, arte);
		srv.PalcoV21_Dar(eu, CatalogoDeItens.Pesos);
		srv.PalcoV21_Dar(eu, comArte);
		bool chegaram = await Ate(() => cli.Mochila.Quantos(CatalogoDeItens.Pesos) == 1 && cli.Mochila.Quantos(comArte) == 1, 6);

		// ---- P1. a mochila ----
		mochila.Abrir();
		await Quadros(4);
		await Segundos(0.3);
		Texture2D? deFabrica = mochila.IconeDoSlotDeTeste(CatalogoDeItens.Pesos), doOutro = mochila.IconeDoSlotDeTeste(comArte);
		string nomeDeFabrica = CatalogoDeItens.Get(CatalogoDeItens.Pesos)?.Nome ?? "", nomeDoOutro = CatalogoDeItens.Get(comArte)?.Nome ?? "";
		Conferir(chegaram && arte.Length > 0 && deFabrica != null && doOutro != null && !ReferenceEquals(deFabrica, doOutro) && nomeDeFabrica != nomeDoOutro,
				 $"P1. DOIS PESOS, DOIS ITENS (*\"o jogo acha q os dois sao o mesmo item\"*): na mochila o de fabrica ('{nomeDeFabrica}', a camisa) e o "
				 + $"de 200 kg com a arte '{arte}' ('{nomeDoOutro}') tem icones e nomes diferentes");
		Fotografar("user://pesos-1-mochila.png");
		mochila.Fechar();
		await Quadros(2);

		// ---- P2. a aba Equip ----
		menu.Abrir();
		menu.IrPara("Equip");
		menu.ForcarRedesenho();
		await Quadros(4);
		Control? pagina = menu.PaginaDeTeste("Equip");
		List<string> textos = pagina == null ? [] : [.. pagina.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text)];
		// o cartao dos pesos a vista (a pagina rola): o rotulo do titulo dele
		Label? titulo = pagina?.FindChildren("*", "Label", true, false).OfType<Label>().FirstOrDefault(l => l.Text == "Pesos");
		if (titulo != null && FindScroll(titulo) is { } rolagem) { rolagem.EnsureControlVisible(titulo); await Quadros(3); }
		Conferir(textos.Contains("50 kg") && textos.Contains("200 kg"),
				 "P2. A ABA EQUIP TEM UM BLOCO POR PESO, cada um com o maximo dele (50 kg e 200 kg) e os botoes que agem nele");
		Fotografar("user://pesos-2-equip.png");
		menu.Fechar();
		await Quadros(2);

		// ---- P3. o chao ----
		mochila.Abrir();
		await Quadros(4);
		bool largou = mochila.AbrirAcoesDeTeste(comArte) && mochila.ApertarAcaoDeTeste("Largar no chão");
		mochila.Fechar();
		bool caiu = await Ate(() => cli.Largados.Exists(l => l.Item == comArte), 5);
		GameClient.LargadoInfo noChao = cli.Largados.Find(l => l.Item == comArte);
		await Segundos(0.6);
		Conferir(largou && caiu && noChao.Arte == caminho,
				 $"P3. O PESO LARGADO CAI COM A CARA DELE (*\"veio com o mesmo peso do meu ate o icone\"*): o de 200 kg cai com a arte '{arte}' -- e o "
				 + $"de fabrica que ficou na mochila continua o de fabrica (no fio '{noChao.Arte}')");
		Fotografar("user://pesos-3-chao.png");

		// ---- P4. o contra-exemplo ----
		srv.PalcoV21_Limpar(eu);
		srv.PalcoV21_Dar(eu, CatalogoDeItens.Pesos);
		await Ate(() => cli.Mochila.Quantos(CatalogoDeItens.Pesos) == 1 && cli.Mochila.Pilhas.Count(p => PesoDeTreino.EhPeso(p.Id)) == 1, 5);
		PesoDeTreino.EstadoForaDoIdDeTeste = true;
		try
		{
			srv.PalcoV30_ArteDosPesos(eu);
			await Segundos(1.0);
			mochila.Abrir();
			await Quadros(4);
			bool soDeFabrica = cli.Mochila.Pilhas.Where(p => PesoDeTreino.EhPeso(p.Id)).All(p => p.Id == CatalogoDeItens.Pesos);
			Conferir(soDeFabrica && ReferenceEquals(mochila.IconeDoSlotDeTeste(CatalogoDeItens.Pesos), deFabrica),
					 "CONTRA-EXEMPLO (defeito injetado: o item sem estado, o jogo de antes): a arte escolhida pela grade nao fica no peso -- ele "
					 + "continua a camisa de fabrica, igual a todo peso da mochila -- e a regua de P1 reprovaria");
			Fotografar("user://pesos-4-defeito.png");
			mochila.Fechar();
		}
		finally { PesoDeTreino.EstadoForaDoIdDeTeste = false; }
		srv.PalcoV21_Limpar(eu);
		srv.PalcoPF_LimparOChao(eu);
		await Segundos(0.5);
	}

	private static ScrollContainer? FindScroll(Node n)
	{
		for (Node? p = n.GetParent(); p != null; p = p.GetParent())
			if (p is ScrollContainer s) return s;
		return null;
	}

	// =====================================================================
	// F) O FREEZA
	// =====================================================================
	private async Tarefa OFreeza(Jandirus.Server.GameServer srv, int eu)
	{
		// O FOTOGRAFO FORA DA LUTA. Na primeira rodada (job95) ele estava a um tile e meio do Freeza: o kiai o jogou pra fora do quadro (3 tiles,
		// `KiaiDaIra`) e as quatro fotos sairam so com ele numa vala, o Death Beam no chat. Agora ele e intocavel pela carencia
		// (`PalcoPF_Intocavel`) e os dois lutam numa linha dois tiles e meio abaixo dele -- o Freeza dois tiles a oeste, o saco dois a leste (4
		// tiles entre eles: a janela do Death Beam e de 3 a 9) --, o beam passa por baixo do fotografo e os dois ficam no quadro.
		srv.PalcoPF_Intocavel(eu, 120);
		(int freeza, int saco) = srv.PalcoPF_FreezaContraOSaco(eu, -2f, 2.5f, 2f, 2.5f);
		Conferir(freeza > 0 && saco > 0, "(montagem) o Freeza de Namek nasce armado como chefe de saga, com um saco do mesmo poder imposto como presa");
		if (freeza <= 0) return;
		int fotos = 0;
		double fim = Time.GetTicksMsec() / 1000.0 + 60;
		while (Time.GetTicksMsec() / 1000.0 < fim && fotos == 0)
		{
			srv.PalcoPF_SacoInteiro(saco);
			if (srv.PalcoPF_DeathBeamNoAr(freeza))
			{
				// o raio e rapido (66 tiles/s): quatro quadros seguidos, a partir do que o servidor viu
				for (int i = 0; i < 4; i++)
				{
					await Quadros(1);
					Fotografar($"user://freeza-1-deathbeam-{i}.png");
				}
				fotos = 4;
				break;
			}
			await Quadros(1);
		}
		Conferir(fotos > 0, "F1. O FREEZA SOLTA O DEATH BEAM (*\"o freeza n ta usando death beam\"*): numa luta pelo tique de producao, o raio sai "
						   + "da mao dele -- quatro quadros fotografados a partir do disparo");
		srv.PalcoPF_Tirar(freeza, saco);
		await Segundos(1.0);

		Fighter.ChefeSemPisoDeTanqueDeTeste = true;
		try
		{
			(int f2, int s2) = srv.PalcoPF_FreezaContraOSaco(eu, -2f, 2.5f, 2f, 2.5f);
			bool viu = false;
			double ate = Time.GetTicksMsec() / 1000.0 + 25;
			while (Time.GetTicksMsec() / 1000.0 < ate)
			{
				srv.PalcoPF_SacoInteiro(s2);
				viu |= srv.PalcoPF_DeathBeamNoAr(f2);
				await Quadros(1);
			}
			Conferir(f2 > 0 && !viu, "CONTRA-EXEMPLO (defeito injetado: o tanque de NPC de antes): 25 s de luta e nenhum Death Beam -- a regua de F1 "
									 + "reprovaria");
			Fotografar("user://freeza-2-defeito.png");
			srv.PalcoPF_Tirar(f2, s2);
		}
		finally
		{
			Fighter.ChefeSemPisoDeTanqueDeTeste = false;
			srv.PalcoPF_Intocavel(eu, 0);
		}
		await Segundos(0.5);
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
		PesoDeTreino.EstadoForaDoIdDeTeste = false;
		Fighter.ChefeSemPisoDeTanqueDeTeste = false;
		foreach (string l in _linhas) GD.Print("[dpesosfreeza] " + l);
		GD.Print(_falhas.Count == 0 ? "[dpesosfreeza] ===== TUDO OK =====" : $"[dpesosfreeza] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dpesosfreeza]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
