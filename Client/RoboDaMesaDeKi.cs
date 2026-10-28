using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
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
///   0. (dono, 2026-10-12) A MESA ABRE POR PARTES: o personagem novo, que nao sabe soltar ki, ve o botao de
///      inventar APAGADO com o motivo escrito, e o `ca_criar` mandado na mao e recusado pelo servidor. So entao
///      o palco lhe da as tres genericas e o resto do roteiro segue.
///   2b. (dono, 2026-10-27, o pedido 116) CADA NUMERO DIZ O QUE FAZ: embaixo de cada compra, a linha "O que faz" com os numeros deste
///      corpo -- a potencia (o dano pelo QUADRADO dela), a energia (o Ki do tiro pelo dreno que o servidor manda, conferido contra o
///      `BaseDrain` dele), a velocidade (tiles por segundo), a carga (os segundos de verdade), o alcance, a forca pela distancia e o
///      folego (passos 41 e 42: duas compras de potencia, o folego ligado, a linha, e tudo desfeito);
///   4. (dono, 2026-10-19) QUEM NUNCA CANSA NAO VENDE FOLEGO: no fim do roteiro o corpo vira MAJIN; o fato do vigor
///      infinito chega pelo tique dos fatos, a caixinha do folego SOME (a frase do porque no lugar dela, e a nota do
///      orcamento sem o folego na lista), e o `ca_comprar StaminaLigar` mandado na mao e recusado pelo servidor --
///      com o defeito injetado (`TecnicaCustomizada.FolegoSemPortaoDeTeste`) a caixinha volta e a regua reprova.
///
/// As quatro fotos: `mesa-0-fechada.png` (o personagem novo, com o botao de inventar apagado), a mesa aberta nos
/// dois tipos (`mesa-1-raio.png`, `mesa-2-bola.png`) e a do Majin rolada ate o folego (`mesa-3-sem-folego.png`).
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

	/// <summary>
	/// Onde o passo 0 esta: 0 = a mesa fechada do personagem novo, 1 = a recusa do servidor chegou e o palco abre a
	/// mesa, 2 = esperando os verbs novos chegarem.
	/// </summary>
	private int _fasePalco;

	/// <summary>Onde o passo 10 esta: 0 = esperando o fato do vigor infinito, 1 = a coluna se medindo, 2 = rolada (a foto).</summary>
	private int _faseDoFolego;

	/// <summary>(2026-10-27) Onde o passo 41 esta: 0 = mandar as compras, 1 = conferir a linha do folego.</summary>
	private int _faseDoFolegoNaMesa;

	/// <summary>
	/// O pedaco da nota do orcamento intacto que lista o folego entre as desvantagens. Nao e so "gastar fôlego": a frase de quem
	/// nunca cansa (`TecnicaCustomizada.SemFolegoNaTela`) tambem diz isso.
	/// </summary>
	private const string ListaComOFolego = "tiro mais lento, gastar fôlego";

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _passos.Add("  --     " + oque);

	public override void _Ready() { if (C is { } cli) cli.Falou += AoOuvir; }
	public override void _ExitTree()
	{
		if (C is { } cli) cli.Falou -= AoOuvir;
		TecnicaCustomizada.FolegoSemPortaoDeTeste = false;   // o defeito do passo 10 nunca sobrevive ao robo
	}

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
			case 0: Abrir(tela, srv, cli, mundo); break;
			case 1: AGradeDoRaio(tela, cli); break;
			case 2: AArteEscolhida(tela, srv, cli); break;
			case 3: ARecusaDaBolaNoRaio(tela, cli); break;
			case 4: AGradeDaBola(tela, cli); break;
			case 41: OFolegoNaMesa(tela, cli); break;      // (2026-10-27) o pedido 116
			case 42: OFolegoDesfeito(tela, cli); break;
			case 5: OSeletorDeCor(tela, srv, cli); break;
			case 6: ACorAplicada(tela, srv, cli, mundo); break;
			case 7: ACorRecusada(tela, srv, cli); break;
			case 8: OSorteio(tela, srv, cli); break;
			case 9: OFolegoDeQuemCansa(tela, srv, cli); break;
			case 10: OFolegoDeQuemNaoCansa(tela, cli); break;
			case 11: OPedidoDeFolego(cli); break;
			case 12: ARecusaDoFolego(tela, srv, cli); break;
			case 13: ODescarte(tela, cli); break;
			default: Fechar(); break;
		}
	}

	private void Virar(int proximo) { _passo = proximo; _t = 0; }

	// =====================================================================
	// 0) ABRIR
	// =====================================================================
	private void Abrir(TelaDeTecnicas tela, Jandirus.Server.GameServer srv, GameClient cli, World mundo)
	{
		if (_t < 2.5) return;

		// A MESA ABRE POR PARTES (dono, 2026-10-12). O personagem deste robo acabou de nascer e nao sabe soltar ki:
		// a tela abre (a cor do ki e de todo mundo), mas o botao de inventar esta APAGADO e diz por que. So depois o
		// palco lhe da as tres genericas -- o resto do roteiro e sobre arte e cor, e nao sobre o portao.
		const string inventar = "Inventar uma técnica nova";
		if (_fasePalco == 0)
		{
			tela.Abrir();
			Conferir(TecnicaCustomizada.TiposDaMesa.All(t => !TelaDeTecnicas.Aberto(t)),
					 "(palco) o personagem novo nao sabe nenhuma das tres genericas (Bola de Ki, Onda de Ki, Esfera Teleguiada)");
			Conferir(tela.BotaoApagado(inventar) && !tela.ClicarBotao(inventar),
					 "sem saber soltar ki, o botao de inventar esta na tela e APAGADO");
			Conferir(tela.TemTexto(TecnicaCustomizada.SemControleDeKiNaTela) && !tela.TemTexto("A mesa abre por partes"),
					 "...e a tela diz por que, na frase curta do dono (2026-10-12), sem a lista do que abre cada tipo");
			cli.SendVerbo("ca_criar");   // quem manda o pacote na mao tem de ouvir nao do servidor
			_fasePalco = 1;
			_t = 1.5;   // um segundo pro pacote ir e a recusa voltar
			return;
		}
		if (_fasePalco == 1)
		{
			Fotografar("user://mesa-0-fechada.png", "a mesa de quem ainda nao sabe soltar ki: o botao de inventar apagado, com o motivo");
			Conferir(cli.Mesa == null && Ouviu(TecnicaCustomizada.SemControleDeKi),
					 "o `ca_criar` mandado na mao com a mesa fechada foi RECUSADO pelo servidor, com o motivo (nenhum rascunho abriu)");
			Conferir(srv.AbrirAMesaDeBancada(cli.LocalId), "(palco) o servidor da ao corpo as tres genericas que abrem a mesa");
			_fasePalco = 2;
			_t = 1.0;   // um segundo e meio pro pacote de skills chegar com os verbs novos
			return;
		}
		if (TecnicaCustomizada.TiposDaMesa.Any(t => !TelaDeTecnicas.Aberto(t))) return;   // o pacote ainda nao chegou

		_corAntes = mundo.CorDoKiDe(cli.LocalId);
		tela.Abrir();
		Conferir(tela.Aberta && tela.ModoDeTeste == "lista", "a mesa abre na LISTA (o servidor nao tem rascunho aberto)");
		Conferir(tela.Previa is { Tipo: TipoDeProjetil.Beam, Vestido: true } p && Perto(p.Cor, _corAntes),
				 $"na lista a previa mostra o raio padrao, vestido, na MINHA cor ({Hex(_corAntes)})");
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
				 $"toda arte da grade tem miniatura (o shader carregou e a amostra se vestiu) ({artes.Count(a => a.TemMiniatura)}/{artes.Count})");
		Conferir(artes.Count(a => a.Marcada) == 1 && artes.First(a => a.Marcada).Arte == ArteDeKi.Nenhuma,
				 "so o PADRAO esta marcado num rascunho novo");
		Conferir(tela.Previa is { Tipo: TipoDeProjetil.Beam, Arte: ArteDeKi.Beam3, Vestido: true },
				 "a previa e um raio na arte padrao do custom (Beam3, o `beamicon` do DM)");
		Conferir(TelaDeTecnicas.GradeRespeitaOTipo(naGrade, TipoDeProjetil.Beam), "a regra da grade aprova a grade do raio");

		Conferir(tela.ClicarArte(ArteDeKi.BeamMasenko), "a miniatura do Masenko existe e foi apertada");
		Virar(2);
	}

	// =====================================================================
	// 2) A ARTE ESCOLHIDA
	// =====================================================================
	private void AArteEscolhida(TelaDeTecnicas tela, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 1.0) return;

		Conferir(cli.Mesa?.Arte == ArteDeKi.BeamMasenko, "o servidor confirmou a arte (`ca_arte`) e o pacote voltou com ela");
		Conferir(tela.Previa is { Arte: ArteDeKi.BeamMasenko, Vestido: true }, "...e a PREVIA passou a vestir o Masenko");
		var artes = tela.ArtesNaGrade();
		Conferir(artes.Count(a => a.Marcada) == 1 && artes.First(a => a.Marcada).Arte == ArteDeKi.BeamMasenko,
				 "...e so a miniatura do Masenko esta marcada");

		// (2026-10-27) O QUE CADA NUMERO FAZ (o pedido 116): as linhas embaixo das compras, com os numeros deste corpo
		if (cli.Mesa is { } raio)
		{
			double drenoLa = srv.DrenoDeKiDeTeste(cli.LocalId);
			Conferir(drenoLa > 0 && Math.Abs(cli.DrenoDeKiDaMesa - drenoLa) <= 1e-4 * drenoLa
					 && tela.TemTexto(TelaDeTecnicas.OQueAEnergiaFaz(raio, cli.DrenoDeKiDaMesa, cli.Sheet.MaxKi)) && tela.TemTexto("segurá-lo gasta mais"),
					 $"(116) a linha da ENERGIA diz quanto Ki o raio gasta neste corpo -- o dreno veio do servidor ({cli.DrenoDeKiDaMesa:0.###} no fio, {drenoLa:0.###} la)");
			Conferir(tela.TemTexto("o dano cresce com o QUADRADO da potência") && tela.TemTexto(TelaDeTecnicas.OQueAPotenciaFaz(raio)),
					 "(116) a linha da POTENCIA diz que o dano cresce com o QUADRADO dela, e compara com as tecnicas prontas");
			Conferir(tela.TemTexto(TelaDeTecnicas.OQueAVelocidadeFaz(raio)), "(116) a linha da VELOCIDADE diz quantos tiles por segundo o raio anda");
			Conferir(cli.CargaDaMesa > 0 && tela.TemTexto(TelaDeTecnicas.OQueACargaFaz(raio, cli.CargaDaMesa)) && tela.TemTexto("s parado reunindo energia"),
					 $"(116) a linha da CARGA diz os segundos de verdade, com as pericias deste corpo ({cli.CargaDaMesa:0.###} s por segundo da mesa)");
			Conferir(tela.TemTexto($"O que faz: o raio vai até {raio.Alcance:0} tiles") && tela.TemTexto("o raio chega com"),
					 "(116) as linhas do ALCANCE e da FORCA PELA DISTANCIA");
		}
		else Conferir(false, "(116) a mesa do raio estava aberta pra ler as linhas");

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
		Conferir(tela.Previa is { Tipo: TipoDeProjetil.Blast, Arte: ArteDeKi.Blast12, Vestido: true },
				 "a previa virou uma bola na arte padrao do custom (12.dmi, o `CustomMakeBlast` do DM)");

		// (2026-10-27) na BOLA a linha da energia e a do tiro, sem o "segurar" do raio (o pedido 116)
		Conferir(cli.Mesa is { } bola && tela.TemTexto(TelaDeTecnicas.OQueAEnergiaFaz(bola, cli.DrenoDeKiDaMesa, cli.Sheet.MaxKi))
				 && !tela.TemTexto("segurá-lo gasta mais") && tela.TemTexto(TelaDeTecnicas.OQueAVelocidadeFaz(bola)),
				 "(116) na BOLA a linha da energia diz o Ki de cada tiro (sem o 'segurar' do raio), e a da velocidade os tiles por segundo");

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
		Virar(41);
	}

	// =====================================================================
	// 41-42) (2026-10-27) A LINHA DO FOLEGO -- o pedido 116
	// =====================================================================
	/// <summary>
	/// O degrau do folego so aparece com a caixinha ligada, e ligar exige dois pontos ja gastos: duas compras de potencia, o folego ligado, a
	/// linha "O que faz" conferida -- e tudo desfeito no 42 (o resto do roteiro quer o orcamento intacto).
	/// </summary>
	private void OFolegoNaMesa(TelaDeTecnicas tela, GameClient cli)
	{
		if (_faseDoFolegoNaMesa == 0)
		{
			if (_t < 0.6) return;
			cli.SendVerbo("ca_comprar", nameof(Compra.DanoMais));
			cli.SendVerbo("ca_comprar", nameof(Compra.DanoMais));
			cli.SendVerbo("ca_comprar", nameof(Compra.StaminaLigar));
			_faseDoFolegoNaMesa = 1;
			_t = 0;
			return;
		}
		if (_t < 1.2) return;
		Conferir(cli.Mesa is { UsaStamina: true } m
				 && tela.TemTexto($"O que faz: cada tiro tira {m.CustoStamina:0} do seu vigor (o fôlego: você tem {cli.Sheet.VigorMax:0} no máximo"),
				 $"(116) com o folego ligado, a linha diz quanto vigor cada tiro tira, contra o vigor maximo do corpo ({cli.Sheet.VigorMax:0})");
		cli.SendVerbo("ca_comprar", nameof(Compra.StaminaDesligar));
		cli.SendVerbo("ca_comprar", nameof(Compra.DanoMenos));
		cli.SendVerbo("ca_comprar", nameof(Compra.DanoMenos));
		Virar(42);
	}

	private void OFolegoDesfeito(TelaDeTecnicas tela, GameClient cli)
	{
		if (_t < 1.2) return;
		Conferir(cli.Mesa is { Gasto: 0, UsaStamina: false }, "(116, montagem) ...e o orcamento volta intacto, sem o folego, pro resto do roteiro");
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
		Virar(9);
	}

	// =====================================================================
	// 9) O FOLEGO DE QUEM CANSA (o controle) -- e o corpo vira Majin
	// =====================================================================
	private void OFolegoDeQuemCansa(TelaDeTecnicas tela, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.6) return;

		Conferir(!TelaDeTecnicas.VigorInfinito() && tela.TemBotao(TelaDeTecnicas.RotuloDoFolego)
				 && !tela.TemTexto(TecnicaCustomizada.SemFolegoNaTela),
				 "(controle) o HUMANO cansa: a caixinha do folego esta na mesa, sem a frase de quem nunca cansa");
		Conferir(cli.Mesa is { Gasto: 0 } && tela.TemTexto(ListaComOFolego),
				 "(controle) ...e a nota do orcamento intacto lista o folego entre as desvantagens");
		Conferir(srv.MajinNaMesaDeBancada(cli.LocalId, majin: true), "(palco) o servidor faz do corpo um MAJIN, de vigor infinito");
		Virar(10);
	}

	// =====================================================================
	// 10) O FOLEGO DE QUEM NUNCA CANSA (dono, 2026-10-19) + O DEFEITO INJETADO
	// =====================================================================
	/// <summary>
	/// Tres tempos: o fato chega e a tela redesenha; a coluna (ja medida) rola ate o folego; a foto, a regua e o contra-exemplo.
	/// O fato vem pelo tique dos fatos, cinco por segundo: espera ate 4 s, e se nao chegar e a primeira afirmacao que reprova.
	/// </summary>
	private void OFolegoDeQuemNaoCansa(TelaDeTecnicas tela, GameClient cli)
	{
		if (_faseDoFolego == 0)
		{
			if (!TelaDeTecnicas.VigorInfinito() && _t < 4) return;
			Conferir(TelaDeTecnicas.VigorInfinito(), "**O FATO DO VIGOR INFINITO CHEGOU** pelo tique dos fatos (`Fatos.VigorInfinito`)");
			tela.ForcarRedesenho();
			_faseDoFolego = 1;
			_t = 0;
			return;
		}
		if (_faseDoFolego == 1)
		{
			if (_t < 0.4) return;
			tela.RolarAteOFim();
			_faseDoFolego = 2;
			_t = 0;
			return;
		}
		if (_t < 0.4) return;

		bool SemACaixa() => !tela.TemBotao(TelaDeTecnicas.RotuloDoFolego) && tela.TemTexto(TecnicaCustomizada.SemFolegoNaTela);
		Conferir(SemACaixa(), "**A CAIXINHA DO FOLEGO SOME** na mesa do Majin, e no lugar dela vem a frase do porque");
		Conferir(cli.Mesa is { Gasto: 0 } && !tela.TemTexto(ListaComOFolego), "**A NOTA DO ORCAMENTO NAO PROMETE O FOLEGO** a quem nunca cansa");
		Fotografar("user://mesa-3-sem-folego.png", "a mesa do MAJIN rolada ate o folego: sem a caixinha, com o motivo");

		// O DEFEITO INJETADO: a mesa volta a vender folego a todos. A regua tem que ficar VERMELHA.
		TecnicaCustomizada.FolegoSemPortaoDeTeste = true;
		try
		{
			tela.ForcarRedesenho();
			Conferir(!SemACaixa() && tela.TemBotao(TelaDeTecnicas.RotuloDoFolego),
					 "(defeito injetado: a mesa vende folego a quem nunca cansa) a caixinha volta pro Majin -- a mesma regua REPROVA");
		}
		finally
		{
			TecnicaCustomizada.FolegoSemPortaoDeTeste = false;
			tela.ForcarRedesenho();
		}
		Conferir(SemACaixa(), "**SEM O DEFEITO A CAIXINHA SOME DE NOVO** (controle do contra-exemplo)");

		// DOIS PONTOS GASTOS: o folego so rende, e com o orcamento intacto o piso o recusaria por OUTRO motivo
		cli.SendVerbo("ca_comprar", nameof(Compra.DanoMais));
		cli.SendVerbo("ca_comprar", nameof(Compra.DanoMais));
		Virar(11);
	}

	// =====================================================================
	// 11) O FOLEGO PEDIDO PELO FIO -- o que a tela nao oferece mais, e um cliente mexido mandaria
	// =====================================================================
	private void OPedidoDeFolego(GameClient cli)
	{
		if (_t < 1.0) return;

		Conferir(cli.Mesa is { Gasto: 2, UsaStamina: false }, "(palco) dois pontos gastos em potencia: o folego teria o que devolver");
		_avisos.Clear();
		cli.SendVerbo("ca_comprar", nameof(Compra.StaminaLigar));
		Virar(12);
	}

	// =====================================================================
	// 12) A RECUSA DO SERVIDOR
	// =====================================================================
	private void ARecusaDoFolego(TelaDeTecnicas tela, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 1.0) return;

		Conferir(cli.Mesa is { UsaStamina: false, Gasto: 2 } && Ouviu(TecnicaCustomizada.SemFolegoPraVender),
				 "**O FOLEGO PEDIDO PELO FIO E RECUSADO** pelo servidor, com o motivo, e os 2 pontos continuam gastos");
		Conferir(srv.MajinNaMesaDeBancada(cli.LocalId, majin: false), "(palco) o corpo volta a ser Humano");
		Conferir(tela.ClicarBotao("Descartar"), "o botao 'Descartar' existe e foi apertado");
		Virar(13);
	}

	// =====================================================================
	// 13) O DESCARTE
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
