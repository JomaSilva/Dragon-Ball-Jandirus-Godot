using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ A MIRAGEM POR ONDE PASSO (`--diagmiragem`) ============================
/// O dono (2026-10-14): *"zanzoken miragem ta bem bugado, ele n fica criando miragem por onde passo, ele fica criando
/// miragem no ultimo local q usei o zanzoken/after image"*.
///
/// A CAUSA mora no cliente, e so no corpo LOCAL: pra ele a origem de todo salto anunciado (`S2C.Zanzo`) era a marca que
/// o soco e o duplo clique gravam (`LocalPlayer.OrigemDoSalto`), e as miragens que nenhum dos dois pede -- a Zanzoken
/// Afterimage, uma a cada meio segundo onde o corpo esta -- nasciam todas no ultimo soco ou na ultima piscada. Quem
/// assistia de fora via certo. Por isso esta bancada mede o que SO o jogador ve: a miragem do proprio corpo, no palco dele.
/// ======================================================================================================
///
/// ============================ AS DUAS CENAS, E A RODADA DO DEFEITO ============================
///   1. A PISCADA E DEPOIS AS MIRAGENS, ANDANDO. O corpo anda pela reta, pisca pelo duplo clique (a marca) e, no quadro
///      em que a miragem da piscada nasce, liga a Zanzoken Afterimage: o flick dela chega DENTRO da janela de uma marca
///      que a piscada ja gastou. Andando mais dois segundos e meio, as miragens tem que nascer onde o servidor disse que
///      o corpo estava -- espalhadas pelo caminho.
///   2. O SOCO NO AR E O PRAZO. Um soco sem ninguem na frente grava a marca e nao salta; o corpo anda mais que a janela
///      e liga a Afterimage: o flick nasce no corpo, e nao no soco.
///   A RODADA DO DEFEITO repete as duas com o `LocalPlayer.MarcaDeSaidaEternaDeTeste` ligado -- o estado de antes -- e as
///   MESMAS reguas tem que reprovar. Sem ela, "a miragem nasceu longe da marca" poderia ser so o palco.
/// ===============================================================================================
///
/// ============================ COMO A MIRAGEM E ACHADA -- E POR QUE ASSIM ============================
/// Pelo `GameClient.Piscou`, o mesmo evento que o `World.AoPiscar` ouve -- e a assinatura daqui e feita DEPOIS da dele
/// (no primeiro quadro com o corpo no mundo), entao, no mesmo disparo, a miragem que o `World` acabou de criar ja e o
/// ultimo `Zanzoken` do palco. A conta `Zanzoken.MiragensDeTeste` diz se ela nasceu mesmo neste disparo. A origem do
/// pacote vem no proprio evento: a regua compara a miragem com o que o SERVIDOR mandou, e nao com uma copia da conta.
/// ======================================================================================================
///
/// COMO RODAR -- um processo so, e ele NAO precisa de janela (o que se mede e a posicao de um no, e nao pixel):
///
///     &lt;godot&gt; --headless --path . --host --rede 7921 --diagmiragem --skillteste /datum/skill/ki/Afterimage \
///              --raca Human --conta bancada_miragem --nome Vulto
/// </summary>
public partial class RoboDaMiragemPorOndePasso : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S
		=> Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	/// <summary>Depois disto ela desiste e conta o que faltou -- bancada travada le como bancada morta.</summary>
	private const double Paciencia = 150;

	/// <summary>A reta caminhavel: a cena 1 anda uns tres segundos e pisca um tile, a cena 2 anda menos de dois.</summary>
	private const int TilesDoPalco = 24;

	private const float T = ZoneCollision.TileSize;

	/// <summary>A miragem nasceu NO PONTO: meio pixel, o arredondamento de um `Vec2` de float que viajou no pacote.</summary>
	private const float NoPonto = 0.5f;

	/// <summary>LONGE da marca: meio tile. A piscada de um corpo novo e de UM tile (`zanzorange`) -- o menor vao da cena 1.</summary>
	private const float LongeDaMarca = T / 2;

	/// <summary>A Afterimage dura cinco segundos (`DuracaoDasMiragensMsG14`); com a folga, a ultima miragem ja chegou.</summary>
	private const double DuracaoDaAfterimage = 5.8;

	private const int PAssentar = 0, P1Marca = 1, P1Andar = 2, P1Piscar = 3, P1EsperarPiscada = 4, P1Gravar = 5,
					  P2Marca = 6, P2Socar = 7, P2Soltar = 8, P2Andar = 9, P2Parar = 10, P2EsperarFlick = 11,
					  P2Esvaziar = 12;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou, _assinei, _defeito, _soltei, _ligou;
	private double _t, _vida, _ligouEm;
	private int _passo, _conta, _antesDaAfterimage;

	private Vec2 _inicio;
	private Facing _rumo;
	private Vector2 _dir;
	private string _tecla = "move_right";
	private Vector2 _marca;

	/// <summary>Uma miragem do corpo LOCAL: onde nasceu, o que o pacote mandou, onde o corpo estava, e quando.</summary>
	private readonly record struct Vulto(Vector2 Onde, Vector2 DoServidor, Vector2 Corpo, double Quando);

	private readonly List<Vulto> _vultos = [];

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --    " + oque);

	private string Rodada => _defeito ? "(defeito injetado) a mesma regua REPROVA: " : "";

	/// <summary>A regua da rodada: na honesta tem que passar, na do defeito tem que reprovar.</summary>
	private void Regua(bool passou, string oque) => Conferir(_defeito ? !passou : passou, Rodada + oque);

	private static string P(Vector2 v) => $"({v.X:0.0},{v.Y:0.0})";

	public override void _Ready()
	{
		// ANTES DO CORPO: a cena 2 aperta a TECLA do soco, e o `LocalPlayer` a le com `IsActionJustPressed` -- que so e
		// verdadeiro no quadro em que ela desceu. A mesma nota da `--diagborrao`.
		ProcessPriority = -100;
	}

	public override void _ExitTree()
	{
		if (C is { } cli && _assinei) cli.Piscou -= AoPiscar;
		Soltar();
	}

	/// <summary>
	/// UM SALTO FOI ANUNCIADO, e o `World.AoPiscar` ja o desenhou (esta assinatura veio depois da dele). Se a conta de
	/// miragens andou neste disparo e o salto e do corpo local, a miragem dele e o ultimo `Zanzoken` do palco.
	/// </summary>
	private void AoPiscar(int quem, Vec2 de, bool vulto)
	{
		int agora = Zanzoken.MiragensDeTeste;
		bool nasceu = agora > _conta;
		_conta = agora;
		if (!nasceu || C is not { } cli || quem != cli.LocalId) return;
		if (World.Instancia?.CorpoDeTeste(quem) is not LocalPlayer local || local.GetParent() is not { } palco) return;

		for (int i = palco.GetChildCount() - 1; i >= 0; i--)
		{
			if (palco.GetChild(i) is not Zanzoken z) continue;
			// O `+ vis.Position` do `Zanzoken.Deixar` (a altura de quem voa) sai daqui: a origem e o ponto do chao.
			Vector2 altura = local.GetNodeOrNull<Node2D>("Visual")?.Position ?? Vector2.Zero;
			_vultos.Add(new Vulto(z.GlobalPosition - altura, new Vector2(de.X, de.Y), local.GlobalPosition, _vida));
			return;
		}
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (S is not { } srv) { Nota("sem servidor no processo (`--diagmiragem` precisa de `--host`)"); Fechar(); return; }
		if (mundo.CorpoDeTeste(cli.LocalId) is not LocalPlayer local) return;

		if (!_assinei)
		{
			_assinei = true;
			_conta = Zanzoken.MiragensDeTeste;
			cli.Piscou += AoPiscar;
		}

		_vida += delta;
		if (_vida > Paciencia) { Conferir(false, $"a bancada terminou antes da paciencia ({Paciencia:0} s; parou no passo {_passo})"); Fechar(); return; }
		_t += delta;

		switch (_passo)
		{
			case PAssentar: Assentar(srv, cli); break;
			case P1Marca: srv.PorNoPontoNaFotoDoBorrao(cli.LocalId, _inicio, _rumo); _vultos.Clear(); Virar(P1Andar); break;
			case P1Andar: if (_t >= 1.0) { Godot.Input.ActionPress(_tecla); Virar(P1Piscar); } break;
			case P1Piscar: Piscar(mundo, local); break;
			case P1EsperarPiscada: EsperarPiscada(srv, cli); break;
			case P1Gravar: Gravar(); break;
			case P2Marca: srv.PorNoPontoNaFotoDoBorrao(cli.LocalId, _inicio, _rumo); Virar(P2Socar); break;
			case P2Socar: if (_t >= 1.0) { Godot.Input.ActionPress("attack"); Virar(P2Soltar); } break;
			case P2Soltar: Socou(local); break;
			case P2Andar: if (_t >= 1.6) { Godot.Input.ActionRelease(_tecla); Virar(P2Parar); } break;
			case P2Parar: if (_t >= 0.25) LigarAAfterimage(srv, cli, P2EsperarFlick); break;
			case P2EsperarFlick: EsperarFlick(local); break;
			case P2Esvaziar: Esvaziar(); break;
			default: Fechar(); break;
		}
	}

	private void Virar(int proximo) { _passo = proximo; _t = 0; }

	// =====================================================================
	// 0) O CORPO ASSENTA E A RETA E ACHADA
	// =====================================================================
	private void Assentar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		// TRES SEGUNDOS: um corpo recem-criado entra nocauteado por um instante, e a Afterimage o recusaria com razao.
		if (_t < 3) return;

		if (!cli.SkillsAprendidas.Contains(World.PathDoZanzoken))
		{
			Conferir(false, "o corpo sabe a Afterimage Technique (a linha precisa de `--skillteste /datum/skill/ki/Afterimage`)");
			Fechar();
			return;
		}

		// A MESMA RETA da `--diagborrao`, com as tres faixas dela numa so (afastamento zero).
		(bool achou, Vec2 origem, Facing rumo) = srv.PalcoDoBorrao(cli.LocalId, TilesDoPalco, 0f);
		if (!achou)
		{
			Conferir(false, $"achou uma reta caminhavel de {TilesDoPalco} tiles em volta do corpo");
			Fechar();
			return;
		}
		_inicio = origem;
		_rumo = rumo;
		Vec2 f = MeleeArea.Frente(rumo);
		_dir = new Vector2(f.X, f.Y);
		_tecla = rumo switch { Facing.East => "move_right", Facing.West => "move_left", Facing.South => "move_down", _ => "move_up" };
		Nota($"a reta: {TilesDoPalco} tiles a partir de ({origem.X:0},{origem.Y:0}), rumo {rumo}");
		Virar(P1Marca);
	}

	// =====================================================================
	// 1) A PISCADA E DEPOIS AS MIRAGENS, ANDANDO
	// =====================================================================
	private void Piscar(World mundo, LocalPlayer local)
	{
		if (_t < 0.5) return;
		// O GESTO DE PRODUCAO: o `World.DuploClique` grava a marca (`MarcarSaida`) e manda o pedido. Tres tiles a frente;
		// o servidor encurta pro alcance da tecnica.
		mundo.DuploCliqueDeTeste(local.GlobalPosition + _dir * (3 * T));
		_marca = local.SaidaMarcadaDeTeste;
		Virar(P1EsperarPiscada);
	}

	private void EsperarPiscada(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_vultos.Count == 0)
		{
			if (_t < 2) return;
			Conferir(false, Rodada + "a piscada pelo duplo clique foi atendida em dois segundos (a miragem dela nasceu)");
			Fechar();
			return;
		}
		// NO QUADRO EM QUE A MIRAGEM DA PISCADA NASCEU: o flick da Afterimage chega dentro da janela da marca.
		LigarAAfterimage(srv, cli, P1Gravar);
	}

	private void LigarAAfterimage(Jandirus.Server.GameServer srv, GameClient cli, int proximo)
	{
		_antesDaAfterimage = _vultos.Count;
		_ligou = srv.LigarAsMiragensNaBancada(cli.LocalId);
		_ligouEm = _vida;
		_soltei = false;
		if (!_ligou)
		{
			Conferir(false, Rodada + "a Zanzoken Afterimage ligou pela funcao de producao (`MiragensG14`)");
			Fechar();
			return;
		}
		Virar(proximo);
	}

	private void Gravar()
	{
		if (!_soltei && _t >= 2.5) { _soltei = true; Godot.Input.ActionRelease(_tecla); }
		if (_t < DuracaoDaAfterimage) return;
		MedirACena1();
		Virar(P2Marca);
	}

	private void MedirACena1()
	{
		Nota(_defeito ? "CENA 1 DA RODADA DO DEFEITO: a piscada e depois as miragens, andando"
					  : "CENA 1: a piscada e depois as miragens, andando");
		Vulto piscada = _vultos[0];
		Conferir(piscada.Onde.DistanceTo(_marca) <= NoPonto,
				 $"a PISCADA deixa a miragem na marca do gesto, onde o corpo estava no duplo clique ({P(piscada.Onde)}; marca {P(_marca)};"
				 + $" o pacote mandou {P(piscada.DoServidor)}, a {piscada.DoServidor.DistanceTo(_marca):0.0} px dela)");

		List<Vulto> miragens = _vultos.GetRange(_antesDaAfterimage, _vultos.Count - _antesDaAfterimage);
		Conferir(miragens.Count >= 10,
				 $"a Zanzoken Afterimage deixou {miragens.Count} miragem(ns) do corpo local (11 esperadas: o flick e uma a cada meio segundo por cinco)");
		if (miragens.Count == 0) return;

		Vulto flick = miragens[0];
		Regua(flick.Onde.DistanceTo(flick.DoServidor) <= NoPonto && flick.Onde.DistanceTo(_marca) >= LongeDaMarca,
			  $"o FLICK da Afterimage, {(flick.Quando - piscada.Quando) * 1000:0} ms depois da piscada (dentro da janela da marca), nasce onde o"
			  + $" servidor disse e nao na marca que a piscada ja gastou ({P(flick.Onde)}; pacote {P(flick.DoServidor)}; marca {P(_marca)})");

		int noPonto = miragens.Count(v => v.Onde.DistanceTo(v.DoServidor) <= NoPonto);
		Regua(noPonto == miragens.Count,
			  $"TODAS as miragens da Afterimage nascem no ponto que o servidor mandou ({noPonto} de {miragens.Count})");

		float andou = miragens[^1].Corpo.DistanceTo(miragens[0].Corpo);
		float espalhou = miragens.Max(v => v.Onde.DistanceTo(miragens[0].Onde));
		int naMarca = miragens.Count(v => v.Onde.DistanceTo(_marca) < LongeDaMarca);
		Conferir(andou >= 2 * T, $"o corpo andou durante a Afterimage ({andou:0} px) -- parado, a regua de baixo nao separa o caminho da marca");
		Regua(espalhou >= 0.5f * andou && naMarca == 0,
			  $"POR ONDE PASSO: as miragens se espalham pelo caminho ({espalhou:0} px da primeira a mais longe, com o corpo andando {andou:0} px)"
			  + $" e nenhuma nasce no ponto da piscada ({naMarca} la)");
	}

	// =====================================================================
	// 2) O SOCO NO AR E O PRAZO
	// =====================================================================
	private Vector2 _marcaDoSoco;

	private void Socou(LocalPlayer local)
	{
		// O corpo pensa DEPOIS deste robo: no quadro do aperto ele leu a tecla e gravou a marca. Aqui, um quadro depois,
		// ela ja esta la -- e a tecla sobe, senao o corpo socaria de novo.
		Godot.Input.ActionRelease("attack");
		_marcaDoSoco = local.SaidaMarcadaDeTeste;
		Godot.Input.ActionPress(_tecla);
		Virar(P2Andar);
	}

	private void EsperarFlick(LocalPlayer local)
	{
		if (_vultos.Count == _antesDaAfterimage)
		{
			if (_t < 2) return;
			Conferir(false, Rodada + "o flick da Afterimage chegou em dois segundos");
			Fechar();
			return;
		}

		Nota(_defeito ? "CENA 2 DA RODADA DO DEFEITO: o soco no ar e o prazo" : "CENA 2: o soco no ar e o prazo");
		Vulto flick = _vultos[_antesDaAfterimage];
		float afastou = local.GlobalPosition.DistanceTo(_marcaDoSoco);
		Conferir(afastou >= 2 * T, $"o corpo andou pra longe do soco antes da Afterimage ({afastou:0} px, e mais de um segundo depois dele)");
		Regua(flick.Onde.DistanceTo(flick.DoServidor) <= NoPonto && flick.Onde.DistanceTo(_marcaDoSoco) >= LongeDaMarca,
			  $"o FLICK nasce onde o corpo esta, e nao no soco que nao investiu: a marca dele venceu na janela ({P(flick.Onde)};"
			  + $" pacote {P(flick.DoServidor)}; soco {P(_marcaDoSoco)})");
		Virar(P2Esvaziar);
	}

	private void Esvaziar()
	{
		// A AFTERIMAGE ACABA SOZINHA: a rodada seguinte comeca sem miragem nenhuma no ar.
		if (_vida - _ligouEm < DuracaoDaAfterimage) return;
		if (_defeito) { Fechar(); return; }
		_defeito = true;
		LocalPlayer.MarcaDeSaidaEternaDeTeste = true;
		Virar(P1Marca);
	}

	// =====================================================================
	// O FIM
	// =====================================================================
	private static void Soltar()
	{
		// O DEFEITO NAO PODE SOBREVIVER A BANCADA: o campo e estatico e de producao.
		LocalPlayer.MarcaDeSaidaEternaDeTeste = false;
		foreach (string tecla in (string[])["attack", "move_right", "move_left", "move_down", "move_up"])
			Godot.Input.ActionRelease(tecla);
	}

	private void Fechar()
	{
		_acabou = true;
		Soltar();
		GD.Print("\n[miragem] ===== A MIRAGEM POR ONDE PASSO =====");
		foreach (string l in _linhas) GD.Print("[miragem] " + l);
		GD.Print(_falhas.Count == 0
			? "[miragem] ===== TUDO OK ====="
			: $"[miragem] ===== {_falhas.Count} FALHA(S) =====\n[miragem]   " + string.Join("\n[miragem]   ", _falhas));
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
