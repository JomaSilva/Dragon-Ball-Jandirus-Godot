using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.Tech;

namespace Jandirus.Client;

/// <summary>
/// A DRILL NA TELA (`--diagdrill`).
///
/// ============================ O PEDIDO DO DONO (2026-10-10) ============================
/// *"a maquina "Drill" de tech 30 fica pegando recursos de forma automatica no planeta q foi instalada, vc
/// pode investir mais recurso ou zenni no menu de interaçao pra deixar ela melhor e pegar mais recursos por
/// segundo"*.
///
/// As contas inteiras (a taxa, o teto, a reserva do planeta, o reinicio, de quem ela e) ja sao cobradas sem
/// janela pela `--drillteste`. O que so existe AQUI e o que o jogador ve e aperta: a tecla E abrindo o menu
/// da Drill, o PAINEL em cima dos botoes (nivel, taxa, estoque andando, reserva do planeta, o preco da
/// proxima melhoria nas duas moedas, o saldo), o "Coletar" e as duas formas de pagar a melhoria.
/// =======================================================================================
///
/// UM ATO SO: a Drill e assentada ao lado (com uma hora de trabalho devida, pra ter o que coletar); abro o
/// menu e leio o painel; espero tres segundos e o estoque ANDOU na tela; coleto; melhoro com zeni; reabro e o
/// painel diz nivel 2; melhoro com recursos (o dobro). No fim, o defeito injetado
/// (`EstadoDaDrillNoMenu.MudoDeTeste`: o painel que nao escreve) -- e a mesma regua do painel reprova.
///
/// COMO RODAR -- um processo so, com JANELA (no headless a foto volta vazia; as reguas de texto valem igual):
///
///     Godot --path . --host --rede 8121 --horateste 0.5 --campoteste 9 --diagdrill \
///           --position 1920,0 --resolution 1280x720 --raca Human --conta bancada_drill_ag --nome Perfurador
///
/// As fotos saem em `user://ddrill-*.png`.
/// </summary>
public partial class RoboDaDrill : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;
	private static MenuDeInteracao? E => MenuDeInteracao.Instancia;

	/// <summary>O que o painel da Drill esta MOSTRANDO agora -- vazio quando ele nao esta na tela.</summary>
	private static string Painel => EstadoDaDrillNoMenu.NaTela?.TextoDesenhado ?? "";

	private const double Paciencia = 120;

	/// <summary>O palco: o que a dona tem pra gastar, e quanto trabalho a Drill ja deve ao abrir o menu.</summary>
	private const double ZeniDaCena = 200_000, RecursosDaCena = 150_000, SegundosDevidos = 3600;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private double _t, _vida;
	private int _passo, _sub;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok     " : "  FALHA  ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --     " + oque);

	public override void _ExitTree() => EstadoDaDrillNoMenu.MudoDeTeste = false;

	private const int PAssentar = 0, PPlantar = 1, PMenu = 2, PColetar = 3, PComZeni = 4, PComRecursos = 5, PDefeito = 6;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (S is not { } srv) { Nota("sem servidor no processo (`--diagdrill` precisa de `--host`)"); Fechar(); return; }

		_vida += delta;
		if (_vida > Paciencia) { Conferir(false, $"a bancada coube na paciencia ({Paciencia:0} s; parou no passo {_passo}.{_sub})"); Fechar(); return; }
		_t += delta;

		switch (_passo)
		{
			case PAssentar: Assentar(srv, cli); break;
			case PPlantar: Plantar(mundo, srv, cli); break;
			case PMenu: OMenuEOPainel(srv, cli); break;
			case PColetar: Coletar(srv, cli); break;
			case PComZeni: MelhorarComZeni(srv, cli); break;
			case PComRecursos: MelhorarComRecursos(srv, cli); break;
			case PDefeito: ODefeito(); break;
			default: Fechar(); break;
		}
	}

	private void Ir(int proximo) { _passo = proximo; _sub = 0; _t = 0; }

	private void Sub(int proximo) { _sub = proximo; _t = 0; }

	// =====================================================================
	// 0) O PALCO
	// =====================================================================
	private (int X, int Y) _c;
	private int _drill;

	private void Assentar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_sub == 0)
		{
			if (_t < 3) return;

			// UM CORPO RECEM-CRIADO ENTRA NOCAUTEADO POR UM INSTANTE (a `--diagvariedade` perdeu o primeiro tiro assim).
			(bool ko, bool morto, _, _, _) = srv.EstadoDaVariedade(cli.LocalId);
			if ((ko || morto) && _t < 30) return;
			Conferir(!ko && !morto, "o corpo entrou em pe e acordado");

			bool praca = srv.AssentarNaPracaDasMaquinas(cli.LocalId, 4);
			Conferir(praca, "achei um QUADRADO de chao comum (quatro tiles livres pra cada lado)");
			if (!praca) { Fechar(); return; }
			srv.CravarMeioDiaDaVariedade(cli.LocalId);
			Sub(1);
			return;
		}
		if (_t < 1.0) return;   // a correcao de posicao precisa atravessar o fio

		_c = CatalogoDeObras.Celula(srv.CorpoDaBoca(cli.LocalId).Pos.X, srv.CorpoDaBoca(cli.LocalId).Pos.Y);
		Nota($"a cena: eu na celula ({_c.X},{_c.Y}), a Drill um tile a leste");
		Ir(PPlantar);
	}

	private void Plantar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_sub == 0)
		{
			if (_t < 0.6) return;
			_drill = srv.MaquinaDaFoto(cli.LocalId, Drill.Tipo, 1, 0);
			Conferir(_drill != 0, "uma DRILL foi assentada um tile a leste (mochila + `posicionar`, o funil de producao)");
			if (_drill == 0) { Fechar(); return; }
			Conferir(srv.PalcoDaDrillNaFoto(cli.LocalId, _drill, ZeniDaCena, RecursosDaCena, SegundosDevidos),
				$"(palco) a dona tem {ZeniDaCena:0} zeni e {RecursosDaCena:0} recursos, e a Drill ja deve uma hora de trabalho");
			Sub(1);
			return;
		}

		ObraDesenhada? o = mundo.ObraDeTeste(_c.X + 1, _c.Y);
		string dica = E?.DicaNaTela ?? "";
		if ((o == null || dica.Length == 0) && _t < 4) return;
		Conferir(o is { Tipo: Drill.Tipo }, "...e esta desenhada na minha tela");
		Conferir(dica.Contains("[E]") && dica.Contains("Drill"), $"ao lado dela a dica da tecla acende: \"{dica}\"");
		Ir(PMenu);
	}

	// =====================================================================
	// 1) O MENU E O PAINEL
	// =====================================================================
	private int _estoqueLido;

	/// <summary>O estoque que o painel esta mostrando ("Estoque: 3.612 de 21.600" -> 3612), ou -1.</summary>
	private static int EstoqueNoPainel()
	{
		foreach (string linha in Painel.Split('\n'))
		{
			if (!linha.StartsWith("Estoque:", StringComparison.Ordinal)) continue;
			int ate = linha.IndexOf(" de ", StringComparison.Ordinal);
			if (ate < 0) return -1;
			string digitos = new([.. linha[..ate].Where(char.IsDigit)]);
			return int.TryParse(digitos, out int n) ? n : -1;
		}
		return -1;
	}

	private void OMenuEOPainel(Jandirus.Server.GameServer srv, GameClient cli)
	{
		switch (_sub)
		{
			case 0:
				ApertarE();
				Sub(1);
				return;

			case 1:
			{
				if ((E is not { NaTela: true } || !Painel.Contains("Nível")) && _t < 4) return;
				if (_t < 0.4) return;   // o menu desenhado, pra foto

				List<string> botoes = E?.BotoesDesenhados() ?? [];
				Conferir(E is { NaTela: true } && botoes.SequenceEqual(["Coletar", "Melhorar...", "Ver estado", "Pegar"]),
					$"a tecla E abre o menu da Drill (titulo \"{E?.TituloDesenhado}\"): {string.Join(" | ", botoes)}");

				string p = Painel;
				Conferir(p.Contains($"Nível 1 de {Drill.NivelMaximo}") && p.Contains(Drill.TaxaEmTexto(1)),
					$"em cima dos botoes, o PAINEL diz o nivel e a taxa ({Drill.TaxaEmTexto(1)})");
				Conferir(p.Contains("Estoque:") && p.Contains("cheio em") && p.Contains("Reserva do planeta:"),
					"...o estoque com o teto e o tempo pra encher, e como esta a reserva do planeta");
				Conferir(p.Contains("zeni ou") && p.Contains("Você tem"),
					"...o preco da proxima melhoria nas DUAS moedas, e o meu saldo");
				Nota("o painel: " + p.Replace("\n", " / "));

				_estoqueLido = EstoqueNoPainel();
				double noServidor = srv.DrillNaFoto(cli.LocalId, _drill).Estoque;
				Conferir(_estoqueLido >= SegundosDevidos * Drill.Taxa(1) && Math.Abs(_estoqueLido - noServidor) <= 6 * Drill.Taxa(1),
					$"o estoque que a tela mostra e o do servidor: a hora devida foi paga quando o painel perguntou (tela {_estoqueLido}, servidor {noServidor:0.#})");
				GuardarATela("ddrill-1-menu.png", "1. a tecla E: o menu da Drill com o painel");
				Sub(2);
				return;
			}

			default:
			{
				if (_t < 3.2) return;
				int agora = EstoqueNoPainel();
				Conferir(agora - _estoqueLido >= 2 * Drill.Taxa(1) && agora - _estoqueLido <= 5 * Drill.Taxa(1),
					$"tres segundos de menu aberto e o estoque ANDOU na tela, {Drill.TaxaEmTexto(1)}: {_estoqueLido} -> {agora}");
				Ir(PColetar);
				return;
			}
		}
	}

	// =====================================================================
	// 2) COLETAR
	// =====================================================================
	private double _recursosAntes, _zeniAntes;

	private void Coletar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_sub == 0)
		{
			_recursosAntes = srv.DrillNaFoto(cli.LocalId, _drill).Recursos;
			Conferir(Apertar("Coletar"), "apertei \"Coletar\"");
			Sub(1);
			return;
		}

		var d = srv.DrillNaFoto(cli.LocalId, _drill);
		if (d.Recursos <= _recursosAntes && _t < 3) return;
		Conferir(d.Recursos - _recursosAntes >= SegundosDevidos * Drill.Taxa(1) && d.Estoque < 5 * Drill.Taxa(1),
			$"o estoque virou SALDO no servidor: +{d.Recursos - _recursosAntes:0} recursos, e a maquina ficou vazia ({d.Estoque:0.##})");
		Conferir(E is not { NaTela: true }, "...e o menu fechou, como em toda acao direta");
		Ir(PComZeni);
	}

	// =====================================================================
	// 3) MELHORAR -- as duas moedas
	// =====================================================================
	private void MelhorarComZeni(Jandirus.Server.GameServer srv, GameClient cli)
	{
		switch (_sub)
		{
			case 0:
				ApertarE();
				Sub(1);
				return;

			case 1:
				if (E is not { NaTela: true } && _t < 2) return;
				Conferir(Apertar("Melhorar..."), "reabri o menu e apertei \"Melhorar...\"");
				Sub(2);
				return;

			case 2:
			{
				if (!Painel.Contains("Nível") && _t < 4) return;
				if (_t < 0.4) return;
				List<string> botoes = E?.BotoesDesenhados() ?? [];
				Conferir(botoes.SequenceEqual(["< Voltar", "Pagar com zeni", "Pagar com recursos"]),
					$"a pagina de melhorias tem as duas formas de pagar: {string.Join(" | ", botoes)}");
				Conferir(Painel.Contains("zeni ou") && Painel.Contains("Nível 2"),
					"...e o painel continua em cima delas, com o preco do nivel 2");
				GuardarATela("ddrill-2-melhorias.png", "2. Melhorar...: zeni ou recursos, com o preco no painel");

				_zeniAntes = srv.DrillNaFoto(cli.LocalId, _drill).Zeni;
				Conferir(Apertar("Pagar com zeni"), "apertei \"Pagar com zeni\"");
				Sub(3);
				return;
			}

			default:
			{
				var d = srv.DrillNaFoto(cli.LocalId, _drill);
				if (d.Nivel < 2 && _t < 3) return;
				double custo = Drill.CustoDaMelhoria(1);
				Conferir(d.Nivel == 2 && Math.Abs(_zeniAntes - d.Zeni - custo) < 0.5,
					$"no servidor a Drill foi ao NIVEL 2 por {custo:0} zeni (nivel {d.Nivel}, gastou {_zeniAntes - d.Zeni:0})");
				Ir(PComRecursos);
				return;
			}
		}
	}

	private void MelhorarComRecursos(Jandirus.Server.GameServer srv, GameClient cli)
	{
		switch (_sub)
		{
			case 0:
				ApertarE();
				Sub(1);
				return;

			case 1:
				if ((E is not { NaTela: true } || !Painel.Contains("Nível 2 de")) && _t < 4) return;
				if (_t < 0.4) return;
				Conferir(Painel.Contains($"Nível 2 de {Drill.NivelMaximo}") && Painel.Contains(Drill.TaxaEmTexto(2)),
					$"reaberto, o painel mostra o NIVEL 2 e a taxa nova ({Drill.TaxaEmTexto(2)})");
				GuardarATela("ddrill-3-nivel2.png", "3. depois da melhoria: nivel 2");
				Conferir(Apertar("Melhorar..."), "apertei \"Melhorar...\" de novo");
				Sub(2);
				return;

			case 2:
				if (_t < 0.4) return;
				_recursosAntes = srv.DrillNaFoto(cli.LocalId, _drill).Recursos;
				_zeniAntes = srv.DrillNaFoto(cli.LocalId, _drill).Zeni;
				Conferir(Apertar("Pagar com recursos"), "apertei \"Pagar com recursos\"");
				Sub(3);
				return;

			default:
			{
				var d = srv.DrillNaFoto(cli.LocalId, _drill);
				if (d.Nivel < 3 && _t < 3) return;
				double preco = Recurso.CustoEmRecursos(Drill.CustoDaMelhoria(2));
				// O SALDO SOBE SOZINHO? Nao: so o ESTOQUE anda. O que saiu do saldo e o preco, exato.
				Conferir(d.Nivel == 3 && Math.Abs(_recursosAntes - d.Recursos - preco) < 0.5 && Math.Abs(d.Zeni - _zeniAntes) < 0.5,
					$"no servidor a Drill foi ao NIVEL 3 por {preco:0} RECURSOS -- o dobro do zeni --, sem tocar no zeni "
					+ $"(nivel {d.Nivel}, gastou {_recursosAntes - d.Recursos:0} recursos e {_zeniAntes - d.Zeni:0} zeni)");
				Ir(PDefeito);
				return;
			}
		}
	}

	// =====================================================================
	// 4) O DEFEITO INJETADO
	// =====================================================================
	private void ODefeito()
	{
		switch (_sub)
		{
			case 0:
				EstadoDaDrillNoMenu.MudoDeTeste = true;
				ApertarE();
				Sub(1);
				return;

			case 1:
				if (_t < 1.5) return;
				Conferir(E is { NaTela: true } && !Painel.Contains("Nível"),
					$"(defeito injetado: o painel nao escreve) a mesma regua REPROVA -- o menu abre so com os botoes (\"{Painel}\")");
				EstadoDaDrillNoMenu.MudoDeTeste = false;
				ApertarE();
				Sub(2);
				return;

			default:
				if (_t < 0.3) return;
				Ir(99);
				return;
		}
	}

	// =====================================================================
	// AS TECLAS, O MENU E AS FOTOS
	// =====================================================================
	/// <summary>A TECLA E DE VERDADE: um evento de teclado empurrado no viewport (o caminho do `_UnhandledInput`).</summary>
	private void ApertarE()
	{
		var ev = new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = true };
		GetViewport().PushInput(ev);
	}

	/// <summary>Aperta um botao do menu aberto pelo rotulo -- o mesmo sinal que o dedo emite. Falso se ele nao estava la.</summary>
	private bool Apertar(string rotulo)
	{
		if (E?.ApertarDesenhado(rotulo) == true) return true;
		Nota(E is { NaTela: true } ? $"(\"{rotulo}\" nao esta no menu, que tem: {string.Join(" | ", E.BotoesDesenhados())})"
								   : $"(\"{rotulo}\": o menu nem estava aberto)");
		return false;
	}

	/// <summary>A TELA INTEIRA pela metade -- o menu e interface, nao mora no mundo.</summary>
	private void GuardarATela(string nome, string legenda)
	{
		Image? tela = GetViewport()?.GetTexture()?.GetImage();
		if (tela == null || tela.IsEmpty()) { Nota($"{legenda}: sem foto (headless nao renderiza)"); return; }
		try
		{
			tela.Convert(Image.Format.Rgba8);
			tela.Resize(tela.GetWidth() / 2, tela.GetHeight() / 2, Image.Interpolation.Bilinear);
			string caminho = ProjectSettings.GlobalizePath("user://" + nome);
			tela.SavePng(caminho);
			_linhas.Add($"  foto   {legenda}: {caminho}");
		}
		catch (Exception e) { Nota($"{legenda}: sem foto: {e.Message}"); }
	}

	private void Fechar()
	{
		if (_acabou) return;
		_acabou = true;

		EstadoDaDrillNoMenu.MudoDeTeste = false;
		if (E is { NaTela: true }) ApertarE();
		S?.LimparAFotoDasMaquinas();

		GD.Print("\n[ddrill] ===== A DRILL NA TELA =====");
		foreach (string l in _linhas) GD.Print("[ddrill] " + l);
		GD.Print(_falhas.Count == 0
			? "[ddrill] ===== TUDO OK ====="
			: $"[ddrill] ===== {_falhas.Count} FALHA(S) =====\n[ddrill]   " + string.Join("\n[ddrill]   ", _falhas));
		GetTree().Quit();
	}
}
