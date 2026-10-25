using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ A SAGA DO CORRUPTED MAJIN NA TELA (`--diagsagamajin`, 2026-10-16) ============================
/// O dono: *"quando terminar tire prints de cada parte dita a cima pra ver se ta tudo certo, e teste a saga majin e veja se ta
/// funcionando bem (no dm tem tudo certinho la, cada sprite, cada trigger tudo)"*.
///
/// A REGRA INTEIRA E DA `--sagamajinteste` (sem janela, com os defeitos injetados). O que so existe AQUI e o que o jogador VE: as
/// quatro cenas da saga tocando pelo gatilho de verdade (o Kai absorvido, o amigo morto, a outra metade vencida, os tres jogadores
/// selados), a metamorfose desenhada (a passagem) no meio delas, o corpo novo depois da virada, a outra metade de pe, a Forma Pura
/// com o Kid Boo na cor do jogador, a vinheta da loucura fechando, o tunel e os gritos da insana, o sono, a Sycophantic Rage num
/// raio na mao -- e o RELOG de verdade (a conexao cai e volta) com um NPC devorado dentro: desde 2026-10-23 (dono) ele FICA, o corpo
/// volta FORA da Forma Pura com o botao de expelir dele no menu, e expelir pelo botao devolve a Forma Pura.
///
/// (dono, no meio do lote: *"lembrando q majin absorver npc faz ele sumir sem deixar um corpo, e o conserto na regeneraçao do majin
/// é pra todos, nao so o corrupted"*) ANTES DA SAGA, a regeneracao de morte num Majin COMUM, com a mesma regua da Forma Pura
/// (<see cref="Regenerar"/>); e cada NPC absorvido tem de SUMIR da tela, sem cadaver (<see cref="ConferirOSumico"/>).
/// ===================================================================================================================================
///
/// SANIDADE, KI E O FIM DO SONO SAO ESCRITOS A MAO (quinze minutos de Pura sa, o Ki da insana acabando e dois minutos de sono nao
/// cabem numa foto); quem reage a eles e o tique de producao. Ver `GameServer.FotoDaSagaMajin.cs`.
///
/// COMO RODAR -- um processo so, com JANELA (no headless o `GetImage` volta vazio), no segundo monitor e mudo:
///
///     Godot --path . --host --rede 8435 --campoteste 8 --horateste 0.5 --semfauna --bpteste 3000000 --diagsagamajin \
///           --position 1920,0 --resolution 1280x720 --raca Majin --conta bancada_dsagamajin --nome Bubu
///
/// As fotos saem em `user://dsaga-*.png`, prints da tela inteira. A grade de cada cena e a `dsaga-N-formaN.png` (sem o sufixo do
/// instante): a onda, a metamorfose andando, ela parada, a virada e o corpo novo com a poeira assentada.
/// </summary>
public partial class RoboDaSagaMajin : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	/// <summary>A regeneracao do Majin comum, quatro cenas de 27,5 s, a da Forma Pura, a loucura, o sono, o relog e o expelir -- e folga.</summary>
	private const double Paciencia = 540;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private bool _acabou;
	private double _t, _vida, _delta;
	private int _passo, _sub;
	private Boot? _boot;

	/// <summary>O MEU id no servidor -- muda no relog.</summary>
	private int _eu;

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		_linhas.Add((ok ? "  ok     " : "  FALHA  ") + oque + (ok || detalhe.Length == 0 ? "" : $"   [{detalhe}]"));
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --     " + oque);

	/// <summary>
	/// O ROBO VIVO -- um so por processo. O relog remonta o mundo pelo `Boot.MontarMundo`, que cria OUTRO robo: o segundo recomecava a
	/// saga do zero no meio do primeiro (r2 e d1 de 2026-10-24: logo depois do login a classe ia pra Normal, o corpo se desfazia e a lista
	/// de quem esta dentro esvaziava -- era o passo do Majin comum do robo novo, e nao o jogo).
	/// </summary>
	private static RoboDaSagaMajin? _vivo;

	public override void _Ready()
	{
		if (_vivo is { } outro && IsInstanceValid(outro) && outro != this)
		{
			_acabou = true;
			QueueFree();
			return;
		}
		_vivo = this;
		ProcessMode = ProcessModeEnum.Always;
		if (GetParent() is Boot boot) _boot = boot;
		if (C is { } cli) cli.Falou += AoOuvir;
		// O RELOG DERRUBA O MUNDO, e o robo nao pode ir junto: ele mora na raiz (o mesmo gesto da `--diagqueda`).
		CallDeferred(nameof(Emancipar));
	}

	public override void _ExitTree()
	{
		// SO NA SAIDA DE VERDADE: a mudanca pra raiz tambem passa por aqui.
		if (!IsQueuedForDeletion() || _vivo != this) return;
		_vivo = null;
		if (C is { } cli) cli.Falou -= AoOuvir;
	}

	private void Emancipar()
	{
		if (GetParent() is not { } pai || GetTree() is not { } arv || pai == arv.Root) return;
		Node raiz = arv.Root;
		pai.RemoveChild(this);
		raiz.AddChild(this);
	}

	/// <summary>Os gritos de bicho que chegaram no meu chat (os `SagaDoMajin.Gritos`, ditos pela insana).</summary>
	private readonly List<string> _gritos = [];

	private void AoOuvir(Protocol.Fala canal, string quem, string texto)
	{
		if (Array.IndexOf(SagaDoMajin.Gritos, texto) >= 0) _gritos.Add(texto);
	}

	private const int PAssentar = 0, PApresentar = 1, PForma1 = 2, PForma2 = 3, PMetade = 4, PForma3 = 5, PForma4 = 6, PPura = 7,
					  PSanidade = 8, PLoucura = 9, PSono = 10, PFuria = 11, PAbsorvido = 12, PRelog = 13, PVolta = 14, PDesfeito = 15,
					  PComum = 16;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		_delta = delta;
		_vida += delta;
		_t += delta;
		if (_vida > Paciencia)
		{
			Conferir(false, $"a bancada coube na paciencia ({Paciencia:0} s; parou no passo {_passo}.{_sub})");
			Fechar();
			return;
		}
		if (S is not { } srv)
		{
			if (_vida > 20) { Nota("sem servidor no processo (`--diagsagamajin` precisa de `--host`)"); Fechar(); }
			return;
		}

		// O RELOG ANDA POR FORA DO MUNDO: a conexao cai, o mundo cai, e ele volta.
		if (_passo == PRelog) { Relogar(srv); return; }
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo || !IsInstanceValid(mundo)) return;

		switch (_passo)
		{
			case PAssentar: Assentar(srv, cli); break;
			case PComum: OMajinComum(mundo, srv); break;
			case PApresentar: Apresentar(mundo); break;
			case PForma1: FormaUm(mundo, srv); break;
			case PForma2: FormaDois(mundo, srv); break;
			case PMetade: AOutraMetade(mundo, srv); break;
			case PForma3: FormaTres(mundo); break;
			case PForma4: FormaQuatro(mundo, srv); break;
			case PPura: AFormaPura(mundo, srv); break;
			case PSanidade: ASanidade(mundo, srv); break;
			case PLoucura: ALoucura(mundo, srv); break;
			case PSono: OSono(mundo, srv); break;
			case PFuria: AFuria(mundo, srv); break;
			case PDesfeito: ADesfeita(mundo, srv); break;
			case PAbsorvido: Absorvido(mundo, srv); break;
			case PVolta: DeVolta(mundo, srv); break;
			default: Fechar(); break;
		}
	}

	private void Ir(int proximo) { _passo = proximo; _sub = 0; _t = 0; }

	private void Sub(int proximo) { _sub = proximo; _t = 0; }

	// =====================================================================
	// 0) O BERCO
	// =====================================================================
	private void Assentar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 3) return;

		// UM CORPO RECEM-CRIADO ENTRA NOCAUTEADO POR UM INSTANTE (a `--diagvariedade` perdeu o primeiro tiro assim).
		(bool ko, bool morto, _, _, _) = srv.EstadoDaVariedade(cli.LocalId);
		if ((ko || morto) && _t < 30) return;
		Conferir(!ko && !morto, "o corpo entrou em pe e acordado");

		bool praca = srv.AssentarNaBoca(cli.LocalId, 4);
		Conferir(praca, "achei uma PRACA com os quatro lados livres (quatro tiles em cada)");
		if (!praca) { Fechar(); return; }
		srv.CravarMeioDiaDaVariedade(cli.LocalId);

		_eu = cli.LocalId;
		Conferir(srv.MajinComumNaFotoDaSaga(_eu), "o corpo local e um MAJIN COMUM (classe de fabrica, rosa): a saga ainda nao o tocou");
		Ir(PComum);
	}

	// =====================================================================
	// 0b) A REGENERACAO DE MORTE NUM MAJIN COMUM, antes da saga
	// =====================================================================
	/// <summary>
	/// O dono (2026-10-16): *"o conserto na regeneraçao do majin é pra todos, nao so o corrupted"*. A MESMA regeneracao da Forma Pura
	/// (<see cref="Regenerar"/>), num Majin que a saga ainda nao tocou: a reconstrucao com a folha DELE, e ele volta como era. So
	/// depois o corpo e corrompido e a saga anda.
	/// </summary>
	private void OMajinComum(World mundo, Jandirus.Server.GameServer srv)
	{
		if (!Regenerar(mundo, srv, "dsaga-0b", "MAJIN COMUM", espera: 1.5, folhaDaVolta: null)) return;
		Conferir(_camadasAntes.Contains("olho") && _camadasAntes.Any(c => c.StartsWith("roupa", StringComparison.Ordinal)),
					"MAJIN COMUM: ...e ele tinha OLHOS e ROUPA acesos antes do soco (a regua de cima cobrou as duas camadas)",
					$"[{string.Join(", ", _camadasAntes)}]");
		Conferir(srv.CorromperNaFotoDaSaga(_eu), "o corpo local e um CORRUPTED MAJIN (a classe rara da saga), rosa, sabendo absorver");
		(int forma, bool pura, _, _, _, _, _, _, _) = srv.SagaNaFoto(_eu);
		Conferir(forma == 0 && !pura, "...e comeca do zero: forma 0, sem a Forma Pura", $"forma {forma}, pura {pura}");
		Ir(PApresentar);
	}

	private void Apresentar(World mundo)
	{
		if (_t < 2) return;   // a aparencia rosa atravessa o fio
		Foto("dsaga-0-majin.png", "0. o Corrupted Majin no corpo de criacao, antes da saga");
		Ir(PForma1);
	}

	// =====================================================================
	// 1..4) OS QUATRO DEGRAUS, PELO GATILHO DE VERDADE
	// =====================================================================
	/// <summary>
	/// OS INSTANTES DAS FOTOS DE UM DEGRAU, no relogio DA CENA (25,5 s ate a virada): a onda rosa, a metamorfose andando (ela comeca
	/// aos 14 s), a metamorfose PARADA no ultimo quadro (as folhas medem de 9,2 a 9,9 s), e o corpo novo logo depois da virada.
	/// </summary>
	private static readonly (double Em, string Sufixo, string Rotulo)[] InstantesDoDegrau =
	[
		(2.0, "a", "a onda de energia rosa (o tremor, o `rockmoving`)"),
		(17.0, "b", "a metamorfose tocando no lugar do corpo, com a aura"),
		(24.8, "c", "a metamorfose parada no ultimo quadro, esperando a virada"),
		(26.6, "d", "a virada: o corpo NOVO"),
	];

	private int _vitima, _metade, _dentroAntes;

	private void FormaUm(World mundo, Jandirus.Server.GameServer srv)
	{
		switch (_sub)
		{
			case 0:
				_vitima = srv.VitimaNaFotoDaSaga(_eu, "Kai", pessoa: false);
				Conferir(_vitima != 0, "um KAI nocauteado nasceu a um tile");
				Sub(1);
				break;
			case 1:
				if (_t < 1.5) return;
				Foto("dsaga-1-0-kai.png", "1. o Kai caido aos pes do Majin");
				AntesDeAbsorver(srv);
				srv.HabilidadeNaFotoDoMajin(_eu, "absorver");
				Conferir(srv.SagaNaFoto(_eu).Forma == 1, "ABSORVER o Kai (o verb de producao) sobe a saga pra FORMA 1");
				Sub(2);
				break;
			default:
				ConferirOSumico(mundo, srv, "o Kai");
				if (FotografarACena(mundo, "dsaga-1-forma1", InstantesDoDegrau, i => ConferirDegrau(mundo, 1, i), Cinematicas.MajinForma1)) Ir(PForma2);
				break;
		}
	}

	private void FormaDois(World mundo, Jandirus.Server.GameServer srv)
	{
		if (_sub == 0)
		{
			Conferir(srv.AmigoMortoNaFotoDaSaga(_eu),
					 "um AMIGO declarado morre pelas maos de um algoz, na vista do Majin (o luto de producao): FORMA 2");
			Sub(1);
			return;
		}
		if (FotografarACena(mundo, "dsaga-2-forma2", InstantesDoDegrau, i => ConferirDegrau(mundo, 2, i), Cinematicas.MajinForma2)) Ir(PMetade);
	}

	private void AOutraMetade(World mundo, Jandirus.Server.GameServer srv)
	{
		switch (_sub)
		{
			case 0:
				// ELA SALTA NO FIM DA CENA (o `majin_spawn_clone()` vem depois do `MajinSagaCinematic`, `MajinSaga.dm:425`).
				_metade = srv.SagaNaFoto(_eu).Metade;
				if (_metade == 0 && _t < 5) return;
				Conferir(_metade != 0, "com a cena acabada, a OUTRA METADE saltou do corpo");
				if (_metade == 0) { Ir(PForma3); return; }
				Sub(1);
				break;
			case 1:
				if (_t < 1.5) return;   // o corpo dela atravessa o fio
				CharacterVisual? vm = VisualDe(mundo, _metade);
				Conferir(vm?.FolhaDoCorpoDeTeste == SagaDoMajin.Corpos[0],
						 "...e esta na minha tela com o corpo da FORMA 1 (o `majin_form_icon(1)` do clone)", vm?.FolhaDoCorpoDeTeste ?? "nao desenhada");
				Foto("dsaga-2-metade.png", "2. a outra metade, de pe e cacando o mestre");
				Conferir(srv.VencerAOutraMetadeNaFoto(_eu) == _metade, "a outra metade foi VENCIDA (nocauteada) ao lado");
				Sub(2);
				break;
			default:
				if (_t < 1.5) return;
				Foto("dsaga-3-0-vencida.png", "3. a outra metade vencida, aos pes do mestre");
				srv.HabilidadeNaFotoDoMajin(_eu, "absorver");
				Conferir(srv.SagaNaFoto(_eu).Forma == 3 && srv.SagaNaFoto(_eu).Metade == 0,
						 "ABSORVER a outra metade vencida a devolve pra dentro: FORMA 3 (o Super Majin)");
				Ir(PForma3);
				break;
		}
	}

	private void FormaTres(World mundo)
	{
		if (FotografarACena(mundo, "dsaga-3-forma3", InstantesDoDegrau, i => ConferirDegrau(mundo, 3, i), Cinematicas.MajinForma3)) Ir(PForma4);
	}

	private void FormaQuatro(World mundo, Jandirus.Server.GameServer srv)
	{
		// SEIS SUB-PASSOS: tres jogadores, cada um nasce caido e e selado um segundo e meio depois. O Kai da forma 1 continua
		// dentro (o NPC devorado fica no registro), entao a conta parte de quantos ja havia.
		if (_sub < 6)
		{
			if (_sub % 2 == 0)
			{
				if (_sub == 0) _dentroAntes = srv.SagaNaFoto(_eu).Dentro;
				_vitima = srv.VitimaNaFotoDaSaga(_eu, "Human", pessoa: true);
				Sub(_sub + 1);
				return;
			}
			if (_t < 1.5) return;
			if (_sub == 1) Foto("dsaga-4-0-jogador.png", "4. um jogador caido ao lado (o primeiro de tres)");
			srv.HabilidadeNaFotoDoMajin(_eu, "absorver");
			int dentro = srv.SagaNaFoto(_eu).Dentro - _dentroAntes;
			Conferir(dentro == (_sub + 1) / 2, $"o jogador {(_sub + 1) / 2} foi SELADO dentro do Majin ({dentro} jogador(es) dentro)");
			if (_sub == 5)
				Conferir(srv.SagaNaFoto(_eu).Forma == 4, "o TERCEIRO jogador selado na forma 3 sobe pra FORMA 4");
			Sub(_sub + 1);
			return;
		}
		if (FotografarACena(mundo, "dsaga-4-forma4", InstantesDoDegrau, i => ConferirDegrau(mundo, 4, i), Cinematicas.MajinForma4)) Ir(PPura);
	}

	/// <summary>O que cada foto de um degrau confere -- a metamorfose da folha certa, parada no fim, e o corpo novo.</summary>
	private void ConferirDegrau(World mundo, int forma, int foto)
	{
		CharacterVisual? v = mundo.VisualLocalDeTeste;
		(string Folha, string Estado)? passagem = SagaDoMajin.PassagemPara(forma);
		switch (foto)
		{
			case 1:
				if (passagem is { } p)
					Conferir(v is { } vp && vp.PassagemDeTeste == p.Estado && vp.QuadroDaPassagemDeTeste > 0,
							 $"forma {forma}: a METAMORFOSE `{p.Estado}` (da folha `{p.Folha.GetFile()}`) toca no lugar do corpo",
							 $"{v?.PassagemDeTeste ?? "nenhuma"}, quadro {v?.QuadroDaPassagemDeTeste}");
				else
					Conferir(v?.PassagemDeTeste == null, "forma 1: sem metamorfose (o `majin_advance_form(1, null)` do Kai)");
				break;
			case 2:
				if (passagem is { } q)
				{
					int ultimo = UltimoQuadro(q.Folha, q.Estado);
					Conferir(v?.QuadroDaPassagemDeTeste == ultimo,
							 $"forma {forma}: perto da virada ela esta PARADA no ultimo quadro", $"{v?.QuadroDaPassagemDeTeste} de {ultimo}");
				}
				break;
			case 3:
				string folha = SagaDoMajin.Corpos[forma - 1];
				Conferir(v is { PassagemDeTeste: null } && v.FolhaDoCorpoDeTeste == folha,
						 $"forma {forma}: depois da virada o corpo e o `{folha.GetFile()}` (e a metamorfose saiu)", v?.FolhaDoCorpoDeTeste ?? "");
				break;
		}
	}

	private static int UltimoQuadro(string folha, string estado) =>
		ResourceLoader.Load<SpriteFrames>(folha) is { } f && f.HasAnimation(estado) ? f.GetFrameCount(estado) - 1 : -1;

	// =====================================================================
	// 5) A FORMA PURA
	// =====================================================================
	/// <summary>Os instantes da cena da Forma Pura (12 s ate a virada): a carne fervendo, o `kid trans` andando, parado, e o Kid Boo.</summary>
	private static readonly (double Em, string Sufixo, string Rotulo)[] InstantesDaPura =
	[
		(1.0, "a", "a carne ferve: o `kid trans` comeca"),
		(6.0, "b", "a metamorfose `kid trans` no meio"),
		(11.6, "c", "a metamorfose parada no ultimo quadro"),
		(12.8, "d", "a virada: o KID BOO, na cor do jogador"),
	];

	private void AFormaPura(World mundo, Jandirus.Server.GameServer srv)
	{
		switch (_sub)
		{
			case 0:
				// O BP BASE NA PORTA (2 bilhoes) e a lista vazia: o `majin_check_pure_unlock()` do ultimo absorvido solto (`:278`).
				srv.BpBaseNaFotoDaSaga(_eu, 3e9);
				srv.HabilidadeNaFotoDoMajin(_eu, "expelir");
				var s = srv.SagaNaFoto(_eu);
				Conferir(s.Dentro == 0 && s.Pura, "EXPULSAR os tres (o corpo vazio) com o BP base na porta DESTRAVA a Forma Pura",
						 $"{s.Dentro} dentro, pura {s.Pura}");
				Sub(1);
				break;
			case 1:
				if (FotografarACena(mundo, "dsaga-5-pura", InstantesDaPura, i => ConferirPura(mundo, srv, i), Cinematicas.MajinPura)) Sub(2);
				break;
			case 2:
				// O PALCO LIMPO pra loucura: a insana caca o que estiver perto, e os corpos da foto estao caidos em volta.
				srv.LimparAFotoDaSaga(_eu);
				Sub(3);
				break;
			default:
				if (_t < 1.0) return;
				Ir(PSanidade);
				break;
		}
	}

	private void ConferirPura(World mundo, Jandirus.Server.GameServer srv, int foto)
	{
		CharacterVisual? v = mundo.VisualLocalDeTeste;
		switch (foto)
		{
			case 0:
				Conferir(_cena?.CenaDeTeste.Musica == SagaDoMajin.TemaDaPura,
						 $"a estreia da Forma Pura toca o tema do DM (`{SagaDoMajin.TemaDaPura}`)", _cena?.CenaDeTeste.Musica ?? "sem cena");
				break;
			case 1:
				Conferir(v is { } vk && vk.PassagemDeTeste == SagaDoMajin.PassagemDaPura.Estado && vk.QuadroDaPassagemDeTeste > 0,
						 "a metamorfose `kid_trans` (da folha da forma 4) toca no lugar do corpo",
						 $"{v?.PassagemDeTeste ?? "nenhuma"}, quadro {v?.QuadroDaPassagemDeTeste}");
				break;
			case 3:
				Conferir(v is { PassagemDeTeste: null } && v.FolhaDoCorpoDaFormaDeTeste == SagaDoMajin.FolhaDaFormaPura,
						 "depois da virada o corpo e o KID BOO (`MajinForm5`)", v?.FolhaDoCorpoDaFormaDeTeste ?? "");
				// O DONO: *"na pure forme ta bugado o sprite, ta com 2 sprites um em cima do outro"* -- o corpo da forma 4 aparecia em volta.
				Conferir(v is { CorpoBaseVisivelDeTeste: false }, "...e o corpo da forma 4 SOME embaixo dele: um corpo so, e nao dois um em cima do outro");
				Conferir(v is { TintaDoCorpoDaFormaDeTeste: { } a, TintaDoCorpoDeTeste: { } b }
						 && a.Cor.IsEqualApprox(b.Cor) && a.Modo == b.Modo && a.Cor.LengthSquared() > 0.1f,
						 "...TINGIDO com a cor que o jogador escolheu, a mesma do corpo (`icon += majin_color`, `MajinSaga.dm:537`)",
						 $"{v?.TintaDoCorpoDaFormaDeTeste} contra {v?.TintaDoCorpoDeTeste}");
				Conferir(srv.SagaNaFoto(_eu).FormaAtual == Catalogo.IdDaFormaPura, "...e o servidor o pos na Forma Pura (56x)");
				break;
		}
	}

	// =====================================================================
	// 6..8) A SANIDADE, A LOUCURA E O SONO
	// =====================================================================
	private void ASanidade(World mundo, Jandirus.Server.GameServer srv)
	{
		LoucuraNaTela? l = mundo.LoucuraDeTeste;
		switch (_sub)
		{
			case 0:
				srv.SanidadeNaFotoDaSaga(_eu, 60);
				Sub(1);
				break;
			case 1:
				if (_t < 3) return;
				Conferir(l != null && Math.Abs(l.VinhetaNaTela - 0.40f) < 0.05f && l.TunelNaTela < 0.01f,
						 "com 60% de sanidade a VINHETA vermelha e preta aparece (40% dela), e nada de tunel",
						 l == null ? "sem tela da loucura" : $"vinheta {l.VinhetaNaTela:0.00}, tunel {l.TunelNaTela:0.00}");
				Foto("dsaga-6-sanidade60.png", "6. sanidade 60%: a vinheta comeca");
				srv.SanidadeNaFotoDaSaga(_eu, 15);
				Sub(2);
				break;
			default:
				if (_t < 3) return;
				Conferir(l != null && Math.Abs(l.VinhetaNaTela - 0.85f) < 0.05f,
						 "com 15% ela FECHA das bordas pra dentro (85%)", l == null ? "" : $"vinheta {l.VinhetaNaTela:0.00}");
				Foto("dsaga-6-sanidade15.png", "6. sanidade 15%: a vinheta fechando");
				Ir(PLoucura);
				break;
		}
	}

	private void ALoucura(World mundo, Jandirus.Server.GameServer srv)
	{
		LoucuraNaTela? l = mundo.LoucuraDeTeste;
		switch (_sub)
		{
			case 0:
				_gritos.Clear();
				// UM FIO ACIMA DE ZERO: a loucura so entra na DESCIDA, e quem gasta o resto e a Pura sa (ver o gancho).
				srv.SanidadeNaFotoDaSaga(_eu, 0.05);
				Sub(1);
				break;
			case 1:
			{
				var s = srv.SagaNaFoto(_eu);
				if (s.FormaAtual != Catalogo.IdDaFormaPuraInsana && _t < 5) return;
				Conferir(s.FormaAtual == Catalogo.IdDaFormaPuraInsana && s.Dirigido,
						 "no ZERO a cabeca se vai: a Forma Pura INSANA entra sozinha (sem cena) e a IA toma as redeas",
						 $"{s.FormaAtual}, dirigido {s.Dirigido}");
				Sub(2);
				break;
			}
			case 2:
				if (_t < 3) return;
				Conferir(l != null && l.TunelNaTela > 0.9f, "...e a tela vira TUNEL", l == null ? "" : $"tunel {l.TunelNaTela:0.00}");
				Foto("dsaga-7-tunel.png", "7. insano: a visao de tunel");
				Foto("dsaga-7-insano.png", "7. a Forma Pura insana (a aura vermelha)");
				Conferir(_gritos.Count > 0, $"...e ele GRITA como bicho no chat assim que enlouquece: \"{(_gritos.Count > 0 ? _gritos[0] : "")}\"");
				// O BALAO DESSE PRIMEIRO GRITO JA SE FOI: a foto espera o PROXIMO (5 a 10 s depois).
				_gritos.Clear();
				Sub(3);
				break;
			case 3:
				if (_gritos.Count == 0 && _t < 15) return;
				Conferir(_gritos.Count > 0, $"...e grita de novo, de 5 a 10 s depois: \"{(_gritos.Count > 0 ? _gritos[0] : "")}\"");
				Sub(4);
				break;
			default:
				if (_t < 0.4) return;   // o balao sobe
				Foto("dsaga-7-grito.png", "7. o grito da insana");
				Ir(PSono);
				break;
		}
	}

	private void OSono(World mundo, Jandirus.Server.GameServer srv)
	{
		LoucuraNaTela? l = mundo.LoucuraDeTeste;
		switch (_sub)
		{
			case 0:
				srv.KiNaFotoDaSaga(_eu, 0);
				Sub(1);
				break;
			case 1:
				if (!srv.SagaNaFoto(_eu).Dormindo && _t < 4) return;
				Conferir(srv.SagaNaFoto(_eu).Dormindo, "o Ki da insana ACABOU: ela apaga e DORME (dois minutos)");
				Sub(2);
				break;
			case 2:
				if (_t < 2.5) return;
				Conferir(mundo.VisualLocalDeTeste?.DeitadoDeTeste == true, "...deitada na minha tela");
				Conferir(l != null && l.TunelNaTela < 0.05f && l.VinhetaNaTela > 0.95f,
						 "...e dormindo o tunel apaga (o corpo dorme), com a vinheta ainda cheia",
						 l == null ? "" : $"tunel {l.TunelNaTela:0.00}, vinheta {l.VinhetaNaTela:0.00}");
				Foto("dsaga-8-sono.png", "8. o sono de dois minutos");
				Conferir(srv.AcordarNaFotoDaSaga(_eu), "(montagem) os dois minutos vencem agora");
				Sub(3);
				break;
			case 3:
			{
				var s = srv.SagaNaFoto(_eu);
				if (s.Dormindo && _t < 4) return;
				Conferir(!s.Dormindo && s.Sanidade >= 99.9 && Math.Abs(s.Ki - 0.10) < 0.03 && !s.Dirigido,
						 "ACORDA com a cabeca cheia, 10% de Ki e as redeas de volta",
						 $"dormindo {s.Dormindo}, sanidade {s.Sanidade:0.#}, Ki {s.Ki:P0}, dirigido {s.Dirigido}");
				Sub(4);
				break;
			}
			default:
				// A PURA SA VOLTA COM A CENA DELA (o degrau da maestria) -- espera ela acabar.
				if ((mundo.CenaEmCursoDeTeste(_eu) != null || srv.SagaNaFoto(_eu).FormaAtual != Catalogo.IdDaFormaPura) && _t < 15) return;
				if (_t < 1.5) return;
				Conferir(srv.SagaNaFoto(_eu).FormaAtual == Catalogo.IdDaFormaPura && l != null && l.VinhetaNaTela < 0.05f,
						 "...de volta a Forma Pura sa, e a tela limpa", l == null ? "" : $"vinheta {l.VinhetaNaTela:0.00}");
				Foto("dsaga-8-acordado.png", "8. acordado: a Forma Pura sa de novo");
				Ir(PFuria);
				break;
		}
	}

	// =====================================================================
	// 9) A SYCOPHANTIC RAGE
	// =====================================================================
	private void AFuria(World mundo, Jandirus.Server.GameServer srv)
	{
		switch (_sub)
		{
			case 0:
				// DE FRENTE PRO OESTE, a praca aberta: o `Ki_Wave` dispara sozinho quando a carga fecha, e a leste ha mata -- o raio
				// batia nela, morria e fechava o canal antes de a Rage entrar.
				srv.ApontarNaBoca(_eu, Jandirus.Core.World.Facing.West);
				srv.KiNaFotoDaSaga(_eu, 1);
				Conferir(srv.RaioNaFotoDaSaga(_eu), "um raio de Ki (o verb `Ki_Wave`) carregando na mao");
				Sub(1);
				break;
			case 1:
			{
				// A RAGE ENTRA COM A CARGA AINDA CORRENDO -- e pra isso que ela existe (*"increases the power of currently charging ki
				// attacks"*). O fator vale pra todo tiro desse ataque, entao o raio que sai depois ja sai com ele.
				if (_t < 0.12) return;
				double fator = srv.FuriaNaFotoDaSaga(_eu);
				double sanidade = srv.SagaNaFoto(_eu).Sanidade;
				Conferir(Math.Abs(fator - 2) < 1e-9 && Math.Abs(sanidade - 50) < 0.5,
							 "a SYCOPHANTIC RAGE arma o raio que carrega em +100% e cobra METADE da sanidade", $"x{fator}, sanidade {sanidade:0.#}");
				Sub(2);
				break;
			}
			case 2:
				if (_t < 0.3) return;
				Foto("dsaga-9-furia.png", "9. a furia armada no raio que carrega -- e a vinheta volta (metade da sanidade)");
				Sub(3);
				break;
			default:
				if (_t < 0.9) return;
				Foto("dsaga-9-raio.png", "9. o raio sai com o dobro do poder");
				Ir(PDesfeito);
				break;
		}
	}

	// =====================================================================
	// 10) A REGENERACAO DE MORTE NA FORMA PURA
	// =====================================================================
	/// <summary>
	/// A MESMA regeneracao do Majin comum (<see cref="OMajinComum"/>), agora no Kid Boo: quem se reconstroi e o corpo que esta na tela
	/// (a `regenerate` do Kid Boo, e nao a do corpo escondido da forma 4), e ele volta sem o corpo da forma 4 por baixo.
	/// </summary>
	private void ADesfeita(World mundo, Jandirus.Server.GameServer srv)
	{
		if (Regenerar(mundo, srv, "dsaga-10", "FORMA PURA", espera: 2.5, folhaDaVolta: SagaDoMajin.FolhaDaFormaPura,   // espera: o raio acaba
						  alemDisso: v => (v.FolhaDoCorpoDaFormaDeTeste == SagaDoMajin.FolhaDaFormaPura && !v.CorpoBaseVisivelDeTeste,
								"...e e o Kid Boo de volta, sem o corpo da forma 4 por baixo",
								$"forma {v.FolhaDoCorpoDaFormaDeTeste}, base {v.CorpoBaseVisivelDeTeste}")))
			Ir(PAbsorvido);
	}

	// =====================================================================
	// A REGENERACAO DE MORTE -- a mesma regua pra todo Majin
	// =====================================================================
	private string[] _camadasAntes = [];
	private string? _folhaDaVolta;

	/// <summary>
	/// O SOCO QUE MATA DESFAZ O MAJIN EM PEDACOS (`DeathRegen.dm:152-173`), e ele se refaz -- na minha tela, um passo por quadro
	/// (verdadeiro = acabou). O dono: *"quando tiver na regeneraçao de morte, o sprite do olho e overlays (roupa etc) vao sumir"* e
	/// *"o conserto na regeneraçao do majin é pra todos, nao so o corrupted"*. As reguas sao as mesmas pra QUALQUER Majin:
	///
	///   * dos pedacos ate o fim da reconstrucao, o boneco INTEIRO fora da tela -- nenhuma camada acesa (corpo, olhos, roupa, cabelo);
	///   * a reconstrucao toca a `regenerate` da folha do corpo que esta na tela (<paramref name="folhaDaVolta"/>; nula = a do corpo
	///     base, ou a da `Majin1` se ele nao a tiver -- ver <see cref="FolhaQueSeRefaz"/>);
	///   * refeito, o boneco volta com as MESMAS camadas acesas de antes do soco (os olhos e a roupa inclusive) -- e o que mais
	///     <paramref name="alemDisso"/> pedir.
	/// </summary>
	private bool Regenerar(World mundo, Jandirus.Server.GameServer srv, string arquivo, string quem, double espera, string? folhaDaVolta,
								   Func<CharacterVisual, (bool Ok, string Oque, string Detalhe)>? alemDisso = null)
	{
		CharacterVisual? v = mundo.VisualLocalDeTeste;
		PedacosDoMajin? pedacos = mundo.CorpoDeTeste(_eu)?.GetNodeOrNull<PedacosDoMajin>(PedacosDoMajin.NomeDoNode);
		string Acesas() => string.Join(", ", v?.CamadasAcesasDeTeste() ?? []);
		bool Apagado() => v != null && !v.IsVisibleInTree() && v.CamadasAcesasDeTeste().Length == 0;
		switch (_sub)
		{
			case 0:
				if (_t < espera || v == null) return false;
				_camadasAntes = v.CamadasAcesasDeTeste();
				_folhaDaVolta = folhaDaVolta ?? FolhaQueSeRefaz(v.FolhaDoCorpoDeTeste);
				Nota($"{quem}: as camadas acesas antes do soco: [{string.Join(", ", _camadasAntes)}]; a reconstrucao esperada: {_folhaDaVolta}");
				Conferir(srv.DesfazerNaFotoDaSaga(_eu), $"{quem}: o soco que mata DESFAZ o Majin em pedacos -- a regeneracao de morte (`DeathRegen.dm:152`)");
				Sub(1);
				return false;
			case 1:
				if ((pedacos == null && _t < 4) || _t < 1.5) return false;
				Conferir(pedacos != null, $"{quem}: ...e os pedacos estao na minha tela");
				Conferir(Apagado(), $"{quem}: ...e o boneco INTEIRO some com eles: corpo, OLHOS e ROUPA (nenhuma camada acesa sobre os pedacos)",
							  $"camadas acesas [{Acesas()}]");
				Foto($"{arquivo}-pedacos.png", $"{quem}: desfeito em pedacos -- so os pedacos na tela");
				srv.LevantarNaFotoDaSaga(_eu);   // os 30 s vencem agora
				Sub(2);
				return false;
			case 2:
			{
				(SpriteFrames? folha, string anim) = pedacos?.RefazendoDeTeste ?? (null, "");
				if (folha == null && _t < 4) return false;
				Conferir(folha?.ResourcePath == _folhaDaVolta && anim == PedacosDoMajin.AnimDaReconstrucao,
							  $"{quem}: a RECONSTRUCAO toca a `regenerate` da folha do corpo que esta na tela",
							  $"{folha?.ResourcePath ?? "nenhuma"} / {anim}; esperada {_folhaDaVolta}");
				Conferir(Apagado(), $"{quem}: ...e durante ela os olhos e a roupa continuam fora: o corpo se refaz sem nada por cima",
							  $"camadas acesas [{Acesas()}]");
				Foto($"{arquivo}-refazendo.png", $"{quem}: a reconstrucao (a `regenerate`)");
				Sub(3);
				return false;
			}
			case 3:
				if (pedacos != null && _t < 8) return false;
				Sub(4);
				return false;
			default:
			{
				if (_t < 0.6) return false;
				string[] depois = v?.CamadasAcesasDeTeste() ?? [];
				Conferir(v != null && v.IsVisibleInTree() && depois.ToHashSet().SetEquals(_camadasAntes),
							  $"{quem}: refeito -- o boneco volta com as MESMAS camadas acesas de antes (os olhos e a roupa de volta)",
							  $"antes [{string.Join(", ", _camadasAntes)}]; depois [{string.Join(", ", depois)}]");
				if (alemDisso != null && v != null)
				{
					(bool ok, string oque, string detalhe) = alemDisso(v);
					Conferir(ok, $"{quem}: {oque}", detalhe);
				}
				Foto($"{arquivo}-refeito.png", $"{quem}: refeito");
				return true;
			}
		}
	}

	/// <summary>A folha que a reconstrucao TEM de tocar pra este corpo: a dele, se tiver a `regenerate`; senao a da `Majin1`.</summary>
	private static string FolhaQueSeRefaz(string folhaDoCorpo) =>
		FolhasPresas.Carregar(folhaDoCorpo) is { } f && f.HasAnimation(PedacosDoMajin.AnimDaReconstrucao) ? folhaDoCorpo : PedacosDoMajin.FolhaDosPedacos;

	// =====================================================================
	// 11..13) ABSORVER TIRA DA PURA; O NPC FICA NO RELOG (dono, 2026-10-23); EXPELIR A DEVOLVE
	// =====================================================================
	private int _idAntesDoRelog;

	/// <summary>O nome do NPC devorado como o servidor o registrou -- o `Expelir: nome` que o menu tem de mostrar depois do relog.</summary>
	private string _nomeDoDevorado = "";

	/// <summary>A chave do botao `Expelir: nome` do NPC que voltou com o Majin (`hab:expelir:numero`).</summary>
	private string _botaoDoDevorado = "";

	private void Absorvido(World mundo, Jandirus.Server.GameServer srv)
	{
		switch (_sub)
		{
			case 0:
				if (_t < 2.5) return;   // o raio acaba
				_vitima = srv.VitimaNaFotoDaSaga(_eu, "Human", pessoa: false);
				Sub(1);
				break;
			case 1:
			{
				if (_t < 1.5) return;
				AntesDeAbsorver(srv);
				srv.HabilidadeNaFotoDoMajin(_eu, "absorver");
				var s = srv.SagaNaFoto(_eu);
				Conferir(s.Dentro == 1 && s.FormaAtual != Catalogo.IdDaFormaPura && s.FormaAtual != Catalogo.IdDaFormaPuraInsana,
						 "ABSORVER alguem tira da Forma Pura (o corpo de absorcao, sem os 56x)", $"{s.Dentro} dentro, {s.FormaAtual}");
				_nomeDoDevorado = srv.AbsorvidosPor(_eu).FirstOrDefault()?.Nome ?? "";
				Sub(2);
				break;
			}
			default:
				if (_t < 2) return;
				ConferirOSumico(mundo, srv, "o NPC");
				Foto("dsaga-11-absorveu.png", "11. com alguem dentro: fora da Forma Pura -- e o NPC absorvido sumiu, sem corpo no chao");
				Ir(PRelog);
				break;
		}
	}

	/// <summary>
	/// O RELOG DE VERDADE, pelo fio, UMA vez (o defeito injetado da lista que o login manda e da `--majinteste`, familia 15d). A volta
	/// passa outra vez pelo `Boot.MontarMundo`, que cria outro robo -- e quem o cala e o <see cref="_vivo"/>.
	/// </summary>
	private void Relogar(Jandirus.Server.GameServer srv)
	{
		switch (_sub)
		{
			case 0:
				_idAntesDoRelog = _eu;
				Conferir(srv.DerrubarDeBancada(_eu), "A CONEXAO CAI (o servidor derruba a minha) -- o relog de verdade, pelo fio");
				Sub(1);
				break;
			case 1:
				if (_boot?.AvisoDeQuedaNaTela() == null && _t < 10) return;
				// (dono, 2026-10-23) o NPC devorado FICA: o que sai com a sessao e o REGISTRO dela -- o id nao volta, quem volta e a ficha
				Conferir(srv.AbsorvidosPor(_idAntesDoRelog).Count == 0,
						 "...e o registro da sessao saiu com ela (o NPC devorado vai pro disco na ficha, e nao no id de sessao)");
				Conferir(_boot?.ApertarOkDoAvisoDeQueda() == true, "(montagem) o aviso de queda tem OK");
				Sub(2);
				break;
			case 2:
				if (_t < 1) return;
				_boot?.DiscarDeNovoDeTeste();
				Sub(3);
				break;
			default:
				if (!(C is { Connected: true, LocalId: not 0 } cli && _boot is { MundoMontado: true }
					  && World.Instancia is { } novo && IsInstanceValid(novo)))
				{
					if (_t > 25) { Conferir(false, "voltei pro jogo (o login de novo)"); Fechar(); }
					return;
				}
				_eu = cli.LocalId;
				Ir(PVolta);
				break;
		}
	}

	/// <summary>
	/// DE VOLTA AO JOGO com o NPC devorado dentro -- o dono (2026-10-23): ele FICA, e pro Corrupted "Conta" (o corpo volta fora da Forma
	/// Pura ate expelir). O que so a tela mostra: o corpo de absorcao no lugar do Kid Boo, e o `Expelir: nome` dele no menu -- a lista
	/// que o login manda DEPOIS do `JoinAccepted` (que a zera). E expelir pelo BOTAO devolve a Forma Pura.
	/// </summary>
	private void DeVolta(World mundo, Jandirus.Server.GameServer srv)
	{
		GameClient? cli = C;
		CharacterVisual? v = mundo.VisualLocalDeTeste;
		switch (_sub)
		{
			case 0:
			{
				if (_t < 4) return;   // o mundo monta; a aparencia e a lista chegam
				var s = srv.SagaNaFoto(_eu);
				Conferir(s.Pura && s.Dentro == 1 && s.FormaAtual != Catalogo.IdDaFormaPura && s.FormaAtual != Catalogo.IdDaFormaPuraInsana,
						 "DE VOLTA AO JOGO o NPC devorado CONTINUA dentro (dono, 2026-10-23) -- e o Corrupted volta FORA da Forma Pura, no corpo de absorcao",
						 $"{s.FormaAtual}, {s.Dentro} dentro");
				Conferir(v != null && v.FolhaDoCorpoDaFormaDeTeste != SagaDoMajin.FolhaDaFormaPura && v.CorpoBaseVisivelDeTeste,
						 "...desenhado no corpo de absorcao, e nao no Kid Boo", v?.FolhaDoCorpoDaFormaDeTeste ?? "sem boneco");
				GameClient.AbsorvidoDoMajin? a = cli is { AbsorvidosDoMajin.Count: 1 } ? cli.AbsorvidosDoMajin[0] : null;
				_botaoDoDevorado = a is { } x ? $"hab:expelir:{x.Numero}" : "";
				Conferir(a is { Devorado: true } d && d.Nome == _nomeDoDevorado && Botao(_botaoDoDevorado) != null && Botao("hab:expelir") is { PodeAgora: true },
						 $"...e a LISTA chegou no login: o `{_nomeDoDevorado}` devorado, com o botao `Expelir: nome` dele e o Expelir todos aceso",
						 $"{cli?.AbsorvidosDoMajin.Count ?? 0} na lista do cliente");
				Foto("dsaga-12-relog.png", "12. depois do relog: o NPC continua dentro -- fora da Forma Pura, no corpo de absorcao");
				if (MenuJogo.Instancia is { } menu) { menu.Abrir(); menu.IrPara(Verbos.Skills); }
				Sub(1);
				break;
			}
			case 1:
			{
				if (_t < 0.6) return;
				// (a pagina remonta quando o que ela mostra muda: o botao e procurado por ate 3 s, e a falha diz o que a pagina tinha)
				Button? b = MenuJogo.Instancia is { } menu ? BotaoNaPagina(menu, $"Expelir: {_nomeDoDevorado}") : null;
				if (b == null && _t < 3) return;
				Conferir(b != null, $"o botao `Expelir: {_nomeDoDevorado}` esta DESENHADO na aba de skills do menu",
						 MenuJogo.Instancia is { } m ? "botoes na pagina: " + string.Join(" | ", BotoesNaPagina(m).Take(14)) : "sem menu");
				if (b != null) RoloDe(b)?.EnsureControlVisible(b);
				Sub(2);
				break;
			}
			case 2:
				if (_t < 0.4) return;
				Foto("dsaga-12b-menu.png", "12b. o menu depois do relog: o `Expelir: nome` do NPC que voltou com o Majin");
				MenuJogo.Instancia?.Fechar();
				// O BOTAO, e nao o pacote: o `Acionar` e o que o clique no menu (e a tecla ligada a ele) chama.
				Botao(_botaoDoDevorado)?.Acionar?.Invoke();
				Sub(3);
				break;
			default:
			{
				// A PURA VOLTA COM A CENA DELA (o corpo novo so aparece na virada) -- espera ela acabar e o Kid Boo chegar a tela.
				var s = srv.SagaNaFoto(_eu);
				if ((s.Dentro > 0 || s.FormaAtual != Catalogo.IdDaFormaPura || mundo.CenaEmCursoDeTeste(_eu) != null
					 || v?.FolhaDoCorpoDaFormaDeTeste != SagaDoMajin.FolhaDaFormaPura) && _t < 15) return;
				if (_t < 1.5) return;
				Conferir(s.Dentro == 0 && s.FormaAtual == Catalogo.IdDaFormaPura && cli is { AbsorvidosDoMajin.Count: 0 },
						 "EXPELIR o NPC pelo botao do menu devolve a Forma Pura -- e a lista do meu cliente esvazia",
						 $"{s.FormaAtual}, {s.Dentro} dentro, {cli?.AbsorvidosDoMajin.Count} na lista");
				Conferir(v?.FolhaDoCorpoDaFormaDeTeste == SagaDoMajin.FolhaDaFormaPura, "...desenhado como o Kid Boo", v?.FolhaDoCorpoDaFormaDeTeste ?? "");
				Foto("dsaga-13-expeliu.png", "13. expelido o NPC: a Forma Pura de volta");
				Fechar();
				break;
			}
		}
	}

	/// <summary>Um verb do catalogo do personagem local pela chave (o botao que o menu desenha).</summary>
	private static Verbo? Botao(string chave) => chave.Length == 0 ? null : Verbos.Todos.FirstOrDefault(v => v.Chave == chave);

	/// <summary>Os botoes vivos da aba de skills do menu (os que a remontagem ja jogou fora ficam de fora), pela arvore inteira da pagina.</summary>
	private static IEnumerable<Button> BotoesDaPagina(MenuJogo menu)
	{
		if (menu.PaginaDeTeste(Verbos.Skills) is not { } pagina) yield break;
		var pilha = new Stack<Node>([pagina]);
		while (pilha.Count > 0)
		{
			Node n = pilha.Pop();
			if (n.IsQueuedForDeletion()) continue;
			if (n is Button b) yield return b;
			foreach (Node f in n.GetChildren()) pilha.Push(f);
		}
	}

	/// <summary>O botao da aba de skills cujo texto comeca assim -- o que o jogador clicaria (o molde da `--diagmajin`).</summary>
	private static Button? BotaoNaPagina(MenuJogo menu, string comeco) =>
		BotoesDaPagina(menu).FirstOrDefault(b => b.Text.StartsWith(comeco, StringComparison.Ordinal));

	/// <summary>O texto de cada botao da aba de skills -- o detalhe da falha quando o procurado nao esta la.</summary>
	private static IEnumerable<string> BotoesNaPagina(MenuJogo menu) => BotoesDaPagina(menu).Select(b => b.Text);

	private static ScrollContainer? RoloDe(Control c)
	{
		for (Node? n = c.GetParent(); n != null; n = n.GetParent())
			if (n is ScrollContainer s) return s;
		return null;
	}

	// =====================================================================
	// O FOTOGRAFO DE CENA
	// =====================================================================
	private Transformacao? _cena;
	private int _fotoDaCena;
	private double _semCena, _depoisDaCena;
	private readonly List<Image> _tira = [];

	/// <summary>
	/// FOTOGRAFA A CENA EM CURSO NO MEU CORPO nos instantes dados, pelo relogio DELA (`Transformacao.TempoDeTeste`), e chama
	/// <paramref name="conferir"/> com o indice de cada foto antes de tira-la. Uma foto por quadro (a tela tem que redesenhar entre
	/// duas); se a cena morrer antes de um instante, as que faltam saem na hora. Verdadeiro = acabou -- a cena terminou (e a colagem
	/// saiu), ou nunca nasceu.
	/// </summary>
	private bool FotografarACena(World mundo, string arquivo, (double Em, string Sufixo, string Rotulo)[] instantes, Action<int> conferir,
														  Cinematica esperada)
	{
		if (_cena == null)
		{
			// A CENA CERTA, e nao a primeira que aparecer: a morte do amigo acende a furia JUNTO com o degrau 2 (`Death.dm:80-81`).
			// A que o `World` devolve e a que guarda o corpo novo (a da virada mais longe) -- e ela TEM que ser a do degrau.
			_cena = mundo.CenaEmCursoDeTeste(_eu);
			if (_cena != null && _cena.CenaDeTeste != esperada && _semCena < 6) { _semCena += _delta; _cena = null; return false; }
			if (_cena == null)
			{
				_semCena += _delta;
				if (_semCena < 6) return false;
				Conferir(false, $"{arquivo}: a cena nasceu no meu corpo");
				_semCena = 0;
				return true;
			}
			Conferir(_cena.CenaDeTeste == esperada, $"{arquivo}: a cena que guarda o corpo novo e a do degrau (e nao a furia que nasce junto)",
							  $"musica `{_cena.CenaDeTeste.Musica}`");
			_semCena = 0;
			_fotoDaCena = 0;
			_tira.Clear();
		}

		bool viva = IsInstanceValid(_cena);
		if (_fotoDaCena < instantes.Length)
		{
			(double em, string sufixo, string rotulo) = instantes[_fotoDaCena];
			if (viva && _cena.TempoDeTeste < em) return false;
			if (!viva) Nota($"{arquivo}: a cena acabou antes dos {em:0.#} s -- a foto sai assim mesmo");
			conferir(_fotoDaCena);
			Guardar(_tira, Foto($"{arquivo}-{sufixo}.png", $"{arquivo}: {rotulo}"));
			_fotoDaCena++;
			return false;
		}
		if (viva) return false;   // a cauda (a poeira) assenta

		// O CORPO NOVO LIMPO: a cratera levanta poeira por cima dele na virada, e a ultima foto espera ela baixar.
		_depoisDaCena += _delta;
		if (_depoisDaCena < 1.5) return false;
		_depoisDaCena = 0;
		Guardar(_tira, Foto($"{arquivo}-e.png", $"{arquivo}: depois da cena, com a poeira assentada"));
		Colar($"{arquivo}.png", _tira, $"{arquivo}: a cena inteira, em grade");
		_cena = null;
		return true;
	}

	// =====================================================================
	// O NPC ABSORVIDO SOME
	// =====================================================================
	private int _cadaveresAntes;
	private bool _sumicoConferido;

	/// <summary>Anota os cadaveres da zona antes do aperto de absorver -- a regua do <see cref="ConferirOSumico"/>.</summary>
	private void AntesDeAbsorver(Jandirus.Server.GameServer srv)
	{
		_cadaveresAntes = srv.VitimaNoMundoNaFotoDaSaga(_vitima, _eu).Cadaveres;
		_sumicoConferido = false;
	}

	/// <summary>
	/// O NPC ABSORVIDO SUMIU SEM DEIXAR CORPO: fora do servidor, fora da minha tela, e nenhum cadaver novo na zona. O dono (2026-10-16):
	/// *"lembrando q majin absorver npc faz ele sumir sem deixar um corpo"* -- as fotos de antes mostravam a vitima caida, com o
	/// "[E] revistar" em cima. Uma vez por absorcao, um segundo depois do aperto (o corpo sai no dreno do fim do tique, e a tela o solta
	/// quando o `PeerLeft` chega).
	/// </summary>
	private void ConferirOSumico(World mundo, Jandirus.Server.GameServer srv, string quem)
	{
		if (_sumicoConferido || _t < 1.0) return;
		_sumicoConferido = true;
		(bool noMundo, int cadaveres) = srv.VitimaNoMundoNaFotoDaSaga(_vitima, _eu);
		bool naTela = mundo.CorpoDeTeste(_vitima) != null;
		Conferir(!noMundo && !naTela && cadaveres == _cadaveresAntes,
					   $"{quem} absorvido SUMIU sem deixar corpo: fora do mundo, fora da minha tela, e nenhum cadaver novo na zona",
					   $"no mundo {noMundo}; na tela {naTela}; cadaveres {_cadaveresAntes} -> {cadaveres}");
	}

	// =====================================================================
	// AS FERRAMENTAS DE CENA E DE TELA
	// =====================================================================
	private static CharacterVisual? VisualDe(World mundo, int id) => mundo.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");

	private static void Guardar(List<Image> tira, Image? foto) { if (foto != null) tira.Add(foto); }

	private Image? Tela()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;
		img.Convert(Image.Format.Rgba8);
		return img;
	}

	/// <summary>
	/// UM PRINT DA TELA INTEIRA, como o jogador a ve: o chat com as frases da saga e o HUD com o estado (FURIOSO, a nutricao...) entram
	/// junto, e sao parte do que se confere. Gravado, e devolvido pras colagens.
	/// </summary>
	private Image? Foto(string nome, string rotulo)
	{
		if (Tela() is not { } tela) { Nota($"{rotulo}: a janela nao devolveu imagem"); return null; }
		Gravar(tela, nome, rotulo);
		return tela;
	}

	private void Gravar(Image img, string nome, string rotulo)
	{
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + nome);
			img.SavePng(caminho);
			_linhas.Add($"  foto   {rotulo}: {caminho}");
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
	}

	/// <summary>
	/// AS FOTOS DE UMA CENA EM GRADE, duas por linha e pela metade do tamanho -- uma imagem so pra ver a cena do comeco ao fim. O
	/// `BlitRect` exige o mesmo formato dos dois lados (e cala quando nao tem): toda foto ja sai em RGBA8 da <see cref="Tela"/>.
	/// </summary>
	private void Colar(string nome, List<Image> pedacos, string rotulo)
	{
		if (pedacos.Count == 0) return;

		const int Vao = 6, Colunas = 2;
		int w = Math.Max(1, pedacos[0].GetWidth() / 2), h = Math.Max(1, pedacos[0].GetHeight() / 2);
		int linhas = (pedacos.Count + Colunas - 1) / Colunas;
		Image colagem = Image.CreateEmpty(Colunas * w + (Colunas - 1) * Vao, linhas * h + (linhas - 1) * Vao, false, Image.Format.Rgba8);
		colagem.Fill(new Color(0.06f, 0.06f, 0.06f));
		for (int i = 0; i < pedacos.Count; i++)
		{
			if (pedacos[i].Duplicate() is not Image p) continue;
			p.Resize(w, h, Image.Interpolation.Bilinear);
			colagem.BlitRect(p, new Rect2I(Vector2I.Zero, p.GetSize()), new Vector2I(i % Colunas * (w + Vao), i / Colunas * (h + Vao)));
		}
		Gravar(colagem, nome, rotulo);
	}

	private void Fechar()
	{
		if (_acabou) return;
		_acabou = true;

		if (S is { } srv) srv.LimparAFotoDaSaga(_eu);

		GD.Print("\n[dsaga] ===== A SAGA DO CORRUPTED MAJIN NA TELA =====");
		foreach (string l in _linhas) GD.Print("[dsaga] " + l);
		GD.Print(_falhas.Count == 0
			? "[dsaga] ===== TUDO OK ====="
			: $"[dsaga] ===== {_falhas.Count} FALHA(S) =====\n[dsaga]   " + string.Join("\n[dsaga]   ", _falhas));
		GetTree().Quit();
	}
}
