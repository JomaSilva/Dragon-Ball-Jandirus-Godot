using Godot;
using Jandirus.Core.Combat;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A MESA DE ATAQUES DE KI, VISTA NA TELA (`--diagmesa`) -- a metade que a `--tecnicateste` nao alcanca.
///
/// ============================ O QUE SO ELA RESPONDE ============================
/// A bancada do servidor prova a tabela de pontos, o disco e o disparo. O que sobra e o que o dono VE
/// (pedido de 2026-09-15: *"faca a tela de customizacao de ataques de ki (beam, blast etc)"*):
///
///   1. a GRADE DE ARTES oferece exatamente `PermitidasPara(tipo)` mais o padrao, cada uma com miniatura,
///      e muda quando o tipo muda -- com o defeito injetado (grade sem filtro) provando que a regra
///      `GradeRespeitaOTipo` sabe ficar vermelha;
///   2. clicar uma miniatura manda `ca_arte` e a PREVIA (um `ProjetilDesenhado` de producao) passa a
///      vestir a arte que o servidor confirmou; pedir pelo fio uma arte de bola pra um raio e RECUSADO;
///   3. a COR DO KI: o seletor tinge a previa ao vivo, "Aplicar" leva a cor ao servidor (lida do
///      `GameServer` no mesmo processo) e ela VOLTA pelo `PeerLook` pro `World`, que e de onde todo
///      tiro meu tira a cor; 999,0,0 e recusado; "Sortear" da outra cor.
///
/// As duas fotos (`mesa-1-raio.png`, `mesa-2-bola.png`) sao a mesa aberta nos dois tipos.
/// ==============================================================================
///
/// COMO RODAR -- um processo so, com `--host` (a cor guardada e lida do servidor) e JANELA (as fotos):
///
///     Godot --path . --host --rede 7963 --diagmesa --position 1920,0 --resolution 1280x720 \
///           --raca Human --conta bancada_mesa --nome Mesa
/// </summary>
public partial class RoboDaMesaDeKi : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	private readonly List<string> _passos = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _avisos = [];

	private bool _acabou;
	private double _t, _vida;
	private int _passo;

	private const double Paciencia = 120;

	/// <summary>A cor pedida pelo seletor -- longe de qualquer sorteio provavel.</summary>
	private static readonly Color CorPedida = new(200 / 255f, 40 / 255f, 40 / 255f);

	private Color _corAntes;

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _passos.Add("  --     " + oque);

	public override void _Ready() { if (C is { } cli) cli.Falou += AoOuvir; }
	public override void _ExitTree() { if (C is { } cli) cli.Falou -= AoOuvir; }

	private void AoOuvir(Protocol.Fala canal, string autor, string texto)
	{
		if (canal == Protocol.Fala.Sistema) _avisos.Add(texto);
	}

	private bool Ouviu(string pedaco) => _avisos.Exists(a => a.Contains(pedaco, StringComparison.OrdinalIgnoreCase));

	private static bool Perto(Color a, Color b) =>
		Mathf.Abs(a.R - b.R) < 0.01f && Mathf.Abs(a.G - b.G) < 0.01f && Mathf.Abs(a.B - b.B) < 0.01f;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (S is not { } srv) { Nota("sem servidor no processo (`--diagmesa` precisa de `--host`)"); Fechar(); return; }
		if (TelaDeTecnicas.Instancia is not { } tela) return;

		_vida += delta;
		if (_vida > Paciencia) { Nota($"acabou a paciencia ({Paciencia:0} s)"); Fechar(); return; }
		_t += delta;

		switch (_passo)
		{
			case 0: Abrir(tela, cli, mundo); break;
			case 1: AGradeDoRaio(tela, cli); break;
			case 2: AArteEscolhida(tela, cli); break;
			case 3: ARecusaDaBolaNoRaio(tela, cli); break;
			case 4: AGradeDaBola(tela, cli); break;
			case 5: OSeletorDeCor(tela, srv, cli); break;
			case 6: ACorAplicada(tela, srv, cli, mundo); break;
			case 7: ACorRecusada(tela, srv, cli); break;
			case 8: OSorteio(tela, srv, cli); break;
			case 9: ODescarte(tela, cli); break;
			default: Fechar(); break;
		}
	}

	private void Virar(int proximo) { _passo = proximo; _t = 0; }

	// =====================================================================
	// 0) ABRIR
	// =====================================================================
	private void Abrir(TelaDeTecnicas tela, GameClient cli, World mundo)
	{
		if (_t < 2.5) return;

		_corAntes = mundo.CorDoKiDe(cli.LocalId);
		tela.Abrir();
		Conferir(tela.Aberta && tela.ModoDeTeste == "lista", "a mesa abre na LISTA (o servidor nao tem rascunho aberto)");
		Conferir(tela.Previa is { Tipo: TipoDeProjetil.Beam, Folha: not null } p && Perto(p.Cor, _corAntes),
				 $"na lista a previa mostra o raio padrao com folha, na MINHA cor ({Hex(_corAntes)})");
		Conferir(tela.ClicarBotao("Inventar uma técnica nova"), "o botao de inventar existe e foi apertado");
		Virar(1);
	}

	// =====================================================================
	// 1) A GRADE DO RAIO
	// =====================================================================
	private void AGradeDoRaio(TelaDeTecnicas tela, GameClient cli)
	{
		if (_t < 1.0) return;

		Conferir(cli.Mesa is { Tipo: TipoDeProjetil.Beam } && tela.ModoDeTeste == "mesa",
				 "o servidor abriu a mesa (rascunho tipo raio) e a tela trocou pro modo mesa");

		var artes = tela.ArtesNaGrade();
		HashSet<ArteDeKi> esperadas = [ArteDeKi.Nenhuma, .. ArteDeProjetil.PermitidasPara(TipoDeProjetil.Beam)];
		HashSet<ArteDeKi> naGrade = [.. artes.Select(a => a.Arte)];
		Conferir(naGrade.SetEquals(esperadas) && artes.Count == esperadas.Count,
				 $"a grade do RAIO oferece o padrao + PermitidasPara(Beam) = {esperadas.Count} artes, sem repetir ({artes.Count} na tela)");
		Conferir(artes.All(a => a.TemMiniatura),
				 $"toda arte da grade tem miniatura (folha convertida e com quadro que serve) ({artes.Count(a => a.TemMiniatura)}/{artes.Count})");
		Conferir(artes.Count(a => a.Marcada) == 1 && artes.First(a => a.Marcada).Arte == ArteDeKi.Nenhuma,
				 "so o PADRAO esta marcado num rascunho novo");
		Conferir(tela.Previa is { Tipo: TipoDeProjetil.Beam, Arte: ArteDeKi.Beam3, Folha: not null },
				 "a previa e um raio na arte padrao do custom (Beam3, o `beamicon` do DM)");
		Conferir(TelaDeTecnicas.GradeRespeitaOTipo(naGrade, TipoDeProjetil.Beam), "a regra da grade aprova a grade do raio");

		Conferir(tela.ClicarArte(ArteDeKi.BeamMasenko), "a miniatura do Masenko existe e foi apertada");
		Virar(2);
	}

	// =====================================================================
	// 2) A ARTE ESCOLHIDA
	// =====================================================================
	private void AArteEscolhida(TelaDeTecnicas tela, GameClient cli)
	{
		if (_t < 1.0) return;

		Conferir(cli.Mesa?.Arte == ArteDeKi.BeamMasenko, "o servidor confirmou a arte (`ca_arte`) e o pacote voltou com ela");
		Conferir(tela.Previa is { Arte: ArteDeKi.BeamMasenko, Folha: not null }, "...e a PREVIA passou a vestir o Masenko");
		var artes = tela.ArtesNaGrade();
		Conferir(artes.Count(a => a.Marcada) == 1 && artes.First(a => a.Marcada).Arte == ArteDeKi.BeamMasenko,
				 "...e so a miniatura do Masenko esta marcada");

		Fotografar("user://mesa-1-raio.png", "a mesa aberta num RAIO, com o Masenko escolhido");

		_avisos.Clear();
		cli.SendVerbo("ca_arte", ((int)ArteDeKi.Blast12).ToString());   // uma BOLA num raio, pelo fio
		Virar(3);
	}

	// =====================================================================
	// 3) A RECUSA
	// =====================================================================
	private void ARecusaDaBolaNoRaio(TelaDeTecnicas tela, GameClient cli)
	{
		if (_t < 1.0) return;

		Conferir(cli.Mesa?.Arte == ArteDeKi.BeamMasenko && Ouviu("nao serve"),
				 "(contra-exemplo) uma arte de BOLA pedida pelo fio pra um RAIO e recusada, com motivo, e a arte fica");
		Conferir(tela.ClicarBotao("Bola"), "o botao de tipo 'Bola' existe e foi apertado");
		Virar(4);
	}

	// =====================================================================
	// 4) A GRADE DA BOLA + O DEFEITO INJETADO
	// =====================================================================
	private void AGradeDaBola(TelaDeTecnicas tela, GameClient cli)
	{
		if (_t < 1.0) return;

		Conferir(cli.Mesa is { Tipo: TipoDeProjetil.Blast }, "o servidor trocou o rascunho pra BOLA");
		var artes = tela.ArtesNaGrade();
		HashSet<ArteDeKi> esperadas = [ArteDeKi.Nenhuma, .. ArteDeProjetil.PermitidasPara(TipoDeProjetil.Blast)];
		HashSet<ArteDeKi> naGrade = [.. artes.Select(a => a.Arte)];
		Conferir(naGrade.SetEquals(esperadas),
				 $"a grade da BOLA oferece o padrao + PermitidasPara(Blast) = {esperadas.Count} artes ({artes.Count} na tela)");
		Conferir(naGrade.All(a => a == ArteDeKi.Nenhuma || ArteDeProjetil.Folha(a).Pasta == "Blasts"),
				 "...e nenhuma delas e de Beams ou Techniques (uma bola com cauda de raio nao se oferece)");
		Conferir(tela.Previa is { Tipo: TipoDeProjetil.Blast, Arte: ArteDeKi.Blast12, Folha: not null },
				 "a previa virou uma bola na arte padrao do custom (12.dmi, o `CustomMakeBlast` do DM)");

		Fotografar("user://mesa-2-bola.png", "a mesa aberta numa BOLA");

		// O DEFEITO INJETADO: a grade sem o recorte por tipo. A regra tem que ficar VERMELHA.
		TelaDeTecnicas.GradeSemFiltroDeTeste = true;
		try
		{
			tela.ForcarRedesenho();
			HashSet<ArteDeKi> suja = [.. tela.ArtesNaGrade().Select(a => a.Arte)];
			Conferir(suja.Count > esperadas.Count && !TelaDeTecnicas.GradeRespeitaOTipo(suja, TipoDeProjetil.Blast),
					 $"(injetado) sem o filtro a grade cresce ({suja.Count} artes) e a regra `GradeRespeitaOTipo` REPROVA");
		}
		finally
		{
			TelaDeTecnicas.GradeSemFiltroDeTeste = false;
			tela.ForcarRedesenho();
		}
		Conferir(TelaDeTecnicas.GradeRespeitaOTipo(tela.ArtesNaGrade().Select(a => a.Arte), TipoDeProjetil.Blast),
				 "(controle) com o filtro de volta, a grade da bola passa de novo");
		Virar(5);
	}

	// =====================================================================
	// 5) O SELETOR DE COR
	// =====================================================================
	private void OSeletorDeCor(TelaDeTecnicas tela, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.6) return;

		tela.CorNoSeletor = CorPedida;
		Conferir(tela.Previa != null && Perto(tela.Previa.Cor, CorPedida),
				 $"mover o seletor TINGE a previa ao vivo ({Hex(CorPedida)}), antes de aplicar");
		Jandirus.Core.Appearance.Rgb? noServidor = srv.CorDoKiDeFoto(cli.LocalId);
		Conferir(noServidor == null || !(noServidor.Value.R == 200 && noServidor.Value.G == 40 && noServidor.Value.B == 40),
				 "...e nada foi pro servidor ainda (so a tela mudou)");
		Conferir(tela.ClicarBotao("Aplicar"), "o botao 'Aplicar' existe e foi apertado");
		Virar(6);
	}

	// =====================================================================
	// 6) A COR APLICADA -- ida e volta
	// =====================================================================
	private void ACorAplicada(TelaDeTecnicas tela, Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		if (_t < 1.2) return;

		Jandirus.Core.Appearance.Rgb? noServidor = srv.CorDoKiDeFoto(cli.LocalId);
		Conferir(noServidor is { R: 200, G: 40, B: 40 },
				 $"o servidor guardou a cor no `Appearance.CorKi` ({noServidor?.R},{noServidor?.G},{noServidor?.B})");
		Color noMundo = mundo.CorDoKiDe(cli.LocalId);
		Conferir(Perto(noMundo, CorPedida),
				 $"...e ela VOLTOU pelo `PeerLook`: o mundo desenha meus tiros em {Hex(noMundo)} (era {Hex(_corAntes)})");
		Conferir(tela.Previa != null && Perto(tela.Previa.Cor, CorPedida) && Perto(tela.CorNoSeletor, CorPedida),
				 "...e a tela redesenhou com a cor que VALE (previa e seletor)");

		_avisos.Clear();
		cli.SendVerbo("ca_cor", "999,0,0");
		Virar(7);
	}

	// =====================================================================
	// 7) A COR RECUSADA
	// =====================================================================
	private void ACorRecusada(TelaDeTecnicas tela, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 1.0) return;

		Jandirus.Core.Appearance.Rgb? noServidor = srv.CorDoKiDeFoto(cli.LocalId);
		Conferir(noServidor is { R: 200, G: 40, B: 40 } && Ouviu("cor invalida"),
				 "(contra-exemplo) `ca_cor 999,0,0` pelo fio e recusado com motivo, e a cor fica");
		Conferir(tela.ClicarBotao("Sortear"), "o botao 'Sortear' existe e foi apertado");
		Virar(8);
	}

	// =====================================================================
	// 8) O SORTEIO
	// =====================================================================
	private void OSorteio(TelaDeTecnicas tela, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 1.2) return;

		Jandirus.Core.Appearance.Rgb? s = srv.CorDoKiDeFoto(cli.LocalId);
		Conferir(s != null && !(s.Value.R == 200 && s.Value.G == 40 && s.Value.B == 40),
				 $"'Sortear' deu outra cor no servidor ({s?.R},{s?.G},{s?.B}) -- o rand(0,255) da criacao");
		Conferir(tela.ClicarBotao("Descartar"), "o botao 'Descartar' existe e foi apertado");
		Virar(9);
	}

	// =====================================================================
	// 9) O DESCARTE
	// =====================================================================
	private void ODescarte(TelaDeTecnicas tela, GameClient cli)
	{
		if (_t < 1.0) return;

		Conferir(cli.Mesa == null && tela.ModoDeTeste == "lista" && cli.Customizadas.Count == 0,
				 "descartar fecha a mesa no servidor, a tela volta pra lista e nenhuma tecnica foi criada");
		Conferir(tela.ClicarBotao("Fechar (Esc)") && !tela.Aberta, "'Fechar' fecha a tela");
		Fechar();
	}

	// =====================================================================
	// AS FOTOS E O FIM
	// =====================================================================
	private void Fotografar(string destino, string rotulo)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"{rotulo}: sem foto (headless nao renderiza)"); return; }
		try
		{
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_passos.Add($"  ok     {rotulo}: {caminho}");
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
	}

	private static string Hex(Color c) => $"#{c.ToHtml(false)}";

	private void Fechar()
	{
		if (_acabou) return;
		_acabou = true;
		GD.Print("\n[mesa] ===== A MESA DE ATAQUES DE KI =====");
		foreach (string l in _passos) GD.Print("[mesa] " + l);
		GD.Print(_falhas.Count == 0
			? "[mesa] ===== TUDO OK ====="
			: $"[mesa] ===== {_falhas.Count} FALHA(S) =====\n[mesa]   " + string.Join("\n[mesa]   ", _falhas));
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
