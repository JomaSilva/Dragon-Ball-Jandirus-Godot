using Godot;
using Jandirus.Core.Npc;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA AMIZADE E DA ABA ALLIES (`--diagaliados`, 2026-10-21). Com janela e `--host`, no campo aberto:
/// a tecla E no habitante, o presente, o treino, o Recrutar, o aliado seguindo e a aba com as ordens.
/// O contra-exemplo do ouvido (`SurdoDeTeste`) mostra que a aba nao se refaz sozinha.
///
///     Godot --path . --host --rede 10141 --campoteste 8 --horateste 0.5 --semfauna --diagaliados
///     --raca Human --conta bancada_aliados --nome Fulano
///
/// Fotos em `user://`: `aliados-1-tecla-e`, `aliados-2-menu`, `aliados-3-presente`, `aliados-4-treino`,
/// `aliados-5-recrutar`, `aliados-6-seguindo`, `aliados-7-aba`.
/// </summary>
public partial class RoboDosAliados : Node
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
		catch (Exception e)
		{
			Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})");
			GD.Print(e.ToString());
		}
		Fechar();
	}

	private void Ouvir(Protocol.Fala canal, string quem, string texto) => _ditas.Add(texto);

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null
									  && MenuDeInteracao.Instancia != null && MenuDoJogo() != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo, o menu P e a tecla E nasceram");
		if (!pronto || C is not { } cli || S is not { } srv || MenuDeInteracao.Instancia is not { } menu || MenuDoJogo() is not { } jogo)
			return;

		GD.Print("[daliados] ===== AMIZADE E ABA ALLIES (2026-10-21) =====");
		cli.Falou += Ouvir;
		try
		{
			int npc = srv.PalcoAl_Habitante(cli.LocalId);
			string nome = srv.PalcoAl_Nome(npc);
			Conferir(npc != 0 && nome.Length > 0, $"P0. o servidor plantou um cidadao ao lado [{nome}]");
			if (npc == 0) return;

			bool dica = await Ate(() => menu.DicaNaTela == "[E] " + nome, 20);
			await Segundos(0.6);
			Conferir(dica, $"P1. a tecla E aponta o habitante [dica \"{menu.DicaNaTela}\"]");
			await Foto("user://aliados-1-tecla-e.png");

			Jandirus.Server.GameServer.HabitanteSemMenuDeTeste = true;
			string cega = "";
			try
			{
				await Ate(() => menu.DicaNaTela != "[E] " + nome, 4);
				await Segundos(0.3);
				cega = menu.DicaNaTela;
				await Foto("user://aliados-1b-sem-menu.png");
			}
			finally { Jandirus.Server.GameServer.HabitanteSemMenuDeTeste = false; }
			Conferir(cega != "[E] " + nome, $"P1b. (defeito injetado: o apontador nao enxerga o habitante) a dica some [\"{cega}\"]");
			await Ate(() => menu.DicaNaTela == "[E] " + nome, 8);

			Apertar(Key.E);
			bool abriu = await Ate(() => menu.NaTela, 4);
			await Segundos(0.4);
			List<string> botoes = menu.BotoesDesenhados();
			Conferir(abriu && botoes.Contains(Amizade.RotuloPresente) && botoes.Contains(Amizade.RotuloTreinar)
						   && !botoes.Contains(Amizade.RotuloRecrutar),
					 $"P2. o menu e Dar presente e Treinar, sem Recrutar [{string.Join(" | ", botoes)}]");
			await Foto("user://aliados-2-menu.png");

			_ditas.Clear();
			bool pediu = menu.ApertarDesenhado(Amizade.RotuloPresente);
			bool lista = pediu && await Ate(() => menu.BotoesDesenhados().Exists(b => b.Contains("(+", StringComparison.Ordinal)), 4);
			await Segundos(0.3);
			string maca = menu.BotoesDesenhados().Find(b => b.Contains("Maçã", StringComparison.Ordinal)) ?? "";
			Conferir(lista && maca.Length > 0, $"P3. Dar presente lista a maca [{maca}]");
			await Foto("user://aliados-3-presente.png");
			srv.PalcoAl_Juntar(cli.LocalId, npc);
			bool deu = maca.Length > 0 && menu.ApertarDesenhado(maca);
			bool aceitou = deu && await Ate(() => _ditas.Exists(t => t.Contains("Amizade", StringComparison.Ordinal)), 4);
			Conferir(aceitou, $"P3b. a maca sai e o laco sobe [{string.Join(" | ", _ditas.TakeLast(2))}]");

			jogo.Abrir();
			await Quadros(2);
			bool temAba = jogo.AbasDeTeste.Contains(MenuJogo.AbaDosAliados);
			if (temAba) jogo.IrPara(MenuJogo.AbaDosAliados);
			await Quadros(3);
			Conferir(temAba && jogo.AbaDeTeste == MenuJogo.AbaDosAliados && cli.Aliados.Amizades.Count > 0,
					 $"P4. a aba Allies aparece com o medidor [{cli.Aliados.Amizades.Count} amizade(s)]");
			await Foto("user://aliados-3b-amizade.png");
			jogo.Fechar();
			await FecharOMenuE(menu);

			srv.PalcoAl_Juntar(cli.LocalId, npc);
			srv.PalcoAl_Igualar(cli.LocalId, npc);
			await Ate(() => menu.DicaNaTela == "[E] " + nome, 6);
			Apertar(Key.E);
			bool deNovo = await Ate(() => menu.NaTela, 4);
			_ditas.Clear();
			bool treinar = deNovo && menu.ApertarDesenhado(Amizade.RotuloTreinar);
			bool comecou = treinar && await Ate(() => _ditas.Exists(t => t.Contains("começou", StringComparison.OrdinalIgnoreCase)), 4);
			await Quadros(3);
			Conferir(comecou, $"P5. Treinar comeca o spar [{string.Join(" | ", _ditas.TakeLast(2))}]");
			await FecharOMenuE(menu);
			await Foto("user://aliados-4-treino.png");

			_ditas.Clear();
			if (comecou) srv.PalcoAl_Ceder(npc);
			bool acabou = comecou && await Ate(() => _ditas.Exists(t => t.Contains("acabou. Amizade", StringComparison.OrdinalIgnoreCase)), 6);
			Conferir(acabou, $"P5b. na metade da vida o treino acaba [{string.Join(" | ", _ditas.TakeLast(2))}]");

			bool apontou = false;
			bool encheu = false;
			for (int i = 0; i < 8 && !apontou; i++)
			{
				srv.PalcoAl_Juntar(cli.LocalId, npc);
				encheu = srv.PalcoAl_Encher(cli.LocalId, npc);
				apontou = encheu && await Ate(() => cli.TipoDoDivinoPerto == Amizade.TipoDoAmigo, 0.6);
			}
			await FecharOMenuE(menu);
			srv.PalcoAl_Juntar(cli.LocalId, npc);
			Apertar(Key.E);
			bool amigo = apontou && await Ate(() => menu.NaTela && menu.BotoesDesenhados().Contains(Amizade.RotuloRecrutar), 6);
			await Segundos(0.4);
			Conferir(amigo, $"P6. com 100 aparece Recrutar [tipo \"{cli.TipoDoDivinoPerto}\"; {string.Join(" | ", menu.BotoesDesenhados())}]");
			await Foto("user://aliados-5-recrutar.png");

			_ditas.Clear();
			srv.PalcoAl_Juntar(cli.LocalId, npc);
			bool recrutou = amigo && menu.ApertarDesenhado(Amizade.RotuloRecrutar);
			bool veio = recrutou && await Ate(() => cli.Aliados.Aliados.Count > 0, 6);
			await Segundos(0.8);
			Conferir(veio, $"P7. ele vira aliado e segue [{string.Join(" | ", _ditas.TakeLast(2))}]");
			await FecharOMenuE(menu);
			await Foto("user://aliados-6-seguindo.png");

			jogo.Abrir();
			await Quadros(2);
			jogo.IrPara(MenuJogo.AbaDosAliados);
			await Quadros(4);
			bool ordens = Botao(jogo, MenuJogo.RotuloFicarAliado) != null || Botao(jogo, MenuJogo.RotuloGuardarAliado) != null;
			Conferir(jogo.AbaDeTeste == MenuJogo.AbaDosAliados && ordens && cli.Aliados.Aliados.Count > 0,
					 "P8. a aba mostra o aliado e as ordens");
			await Foto("user://aliados-7-aba.png");

			Button? ficar = Botao(jogo, MenuJogo.RotuloFicarAliado);
			if (ficar != null) ficar.EmitSignal(BaseButton.SignalName.Pressed);
			bool ficou = ficar != null && await Ate(() => cli.Aliados.Aliados.Exists(a => (a.Estado & EstadoDeAliado.Ficando) != 0), 4);
			await Quadros(3);
			Conferir(ficou, "P8b. Ficar chega no servidor e a aba acompanha");
			await Foto("user://aliados-7b-ficar.png");

			int versao = cli.VersaoDosAliados;
			int rem = jogo.RemontagensDaAbaDeTeste(MenuJogo.AbaDosAliados);
			OuvidoDosAliados.SurdoDeTeste = true;
			try
			{
				Button? guardar = Botao(jogo, MenuJogo.RotuloGuardarAliado);
				guardar?.EmitSignal(BaseButton.SignalName.Pressed);
				await Ate(() => cli.VersaoDosAliados != versao, 4);
				Conferir(jogo.RemontagensDaAbaDeTeste(MenuJogo.AbaDosAliados) == rem,
						 "R1. (defeito injetado: o ouvido nao refaz a aba) a mesma regua REPROVA");
			}
			finally { OuvidoDosAliados.SurdoDeTeste = false; }
			await Ate(() => jogo.RemontagensDaAbaDeTeste(MenuJogo.AbaDosAliados) > rem, 4);
		}
		finally { cli.Falou -= Ouvir; }
	}

	private MenuJogo? MenuDoJogo() => GetParent()?.GetNodeOrNull<MenuJogo>("Menu");

	private static Button? Botao(Node raiz, string texto)
	{
		var achados = new List<Button>();
		Juntar(raiz, achados);
		foreach (Button b in achados)
			if (b.Text == texto && b.IsVisibleInTree()) return b;
		return null;
	}

	private static void Juntar(Node n, List<Button> sai)
	{
		foreach (Node f in n.GetChildren())
		{
			if (f is Button b) sai.Add(b);
			Juntar(f, sai);
		}
	}

	private void Apertar(Key k)
	{
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = true });
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = false });
	}

	/// <summary>Escape sem o menu E aberto abre a PAUSA. So fecha o menu E quando ele esta na tela, e tira a pausa se ela abriu junto.</summary>
	private async Tarefa FecharOMenuE(MenuDeInteracao menu)
	{
		if (menu.NaTela)
		{
			Apertar(Key.Escape);
			await Ate(() => !menu.NaTela, 2);
		}
		if (PauseMenu.Instancia is { Aberto: true } p) p.Fechar("bancada");
		await Quadros(1);
	}

	/// <summary>A pausa por cima estraga a foto. Fecha, espera o quadro, e so entao grava.</summary>
	private async Tarefa Foto(string destino)
	{
		if (PauseMenu.Instancia is { Aberto: true } p) p.Fechar("bancada");
		await Quadros(2);
		Fotografar(destino);
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
		OuvidoDosAliados.SurdoDeTeste = false;
		Jandirus.Server.GameServer.HabitanteSemMenuDeTeste = false;
		foreach (string l in _linhas) GD.Print("[daliados] " + l);
		GD.Print(_falhas.Count == 0 ? "[daliados] ===== TUDO OK =====" : $"[daliados] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[daliados]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
