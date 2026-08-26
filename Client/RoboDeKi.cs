using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A BANCADA DO SISTEMA DE KI, DE PONTA A PONTA (`--diagki`) -- a metade VIVA.
///
///     Godot --headless --path . --rede 7961 --kideponta --diagki
///
/// A outra metade (`--kideponta`, `Server/GameServer.KiDePontaTeste.cs`) mede com corpos forjados,
/// dentro do servidor, sem ninguem no fio. Esta aqui e o contrario: **conta NOVA, socket de
/// verdade, e nenhuma chamada de funcao do servidor**. Tudo que ela sabe do jogo chegou num pacote.
///
/// ============================ O QUE SO ELA RESPONDE ============================
///  1. A TABELA DE PONTOS E DO SERVIDOR. O cliente pede seis compras de um ponto e o servidor
///     devolve cinco -- a tela nao decide preco nenhum, e o teto vale pra quem manda o pacote na
///     mao (que e o unico jeito de burlar uma tela).
///  2. O SERVIDOR DECIDE O ACERTO. O robo MENTE de quatro jeitos diferentes -- posicao forjada,
///     tecnica que ele nao inventou, tecnica de skill que ele nao aprendeu, tecla de embate fora de
///     embate -- e nenhuma delas tira um ponto de vida de ninguem. E logo antes disso o tiro
///     HONESTO tira: sem essa metade, "nada aconteceu" seria compativel com um jogo quebrado.
///  3. O RELOGIN. A tecnica inventada sobrevive a cair a conexao e voltar -- com nome, grito,
///     numeros e pontos gastos --, e ela AINDA DISPARA depois.
/// ==============================================================================
///
/// ============================ POR QUE ELA NAO MONTA O MUNDO ============================
/// Ela e registrada ANTES da tela de login (como a `--diagslot`) e nunca passa pelo
/// `Boot.AoEntrarNoMundo`. Duas razoes:
///   * o RELOGIN. Entrando pelo caminho normal, cada volta ao mundo montaria um `World`, um `Hud` e
///     um `Chat` novos por cima dos velhos -- e o teste de persistencia viraria um teste de quantas
///     arvores de cena cabem na memoria;
///   * o que ela mede nao precisa de desenho nenhum. Posicao autoritativa, vida alheia, golpe
///     confirmado e lista de tecnicas chegam todos em pacote, e e do pacote que ela le -- que e
///     exatamente a disciplina que ela existe pra provar.
/// ==================================================================================
/// </summary>
public partial class RoboDeKi : Node
{
	// ============================ A CONTA E NOVA, E O NOME DELA CARREGA A PORTA ============================
	// Ha outras sessoes neste repositorio, e uma bancada que reusa "teste" atropelaria o personagem
	// de outra pessoa. O numero da porta no nome faz duas coisas: separa esta rodada das outras e
	// deixa obvio, olhando a pasta de saves, de quem e o arquivo.
	// =================================================================================================
	private const string Prefixo = "bancadaki";
	private const string Senha = "bancadaki";
	private const string Personagem = "Pontaponta";

	/// <summary>O nome da tecnica inventada -- e ele tem que voltar do disco letra por letra.</summary>
	private const string NomeDaTecnica = "Onda da Bancada";
	private const string GritoDaTecnica = "TOMA ESSA!";

	/// <summary>Um passo a cada 0,4 s: da tempo de a resposta do servidor voltar antes do proximo.</summary>
	private const double Passo = 0.4;

	/// <summary>Prazo total. Uma bancada que trava e uma bancada que ninguem roda duas vezes.</summary>
	private const double PrazoTotal = 180;

	private enum Fase { Conectando, Selecao, Mundo, Voltando }

	private readonly List<string> _passos = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _avisos = [];

	private Fase _fase = Fase.Conectando;
	private int _passo;
	private double _t, _vida;
	private bool _acabou;
	private int _espera;

	private List<SlotInfo> _slots = [];
	private int _meuId;
	private Vec2 _minhaPos;
	private int _idDoBoneco;

	/// <summary>
	/// A POSTURA do boneco -- o que sobrou de estado de corpo alheio no snapshot.
	///
	/// ============================ ESTA BANCADA LIA A VIDA DELE ============================
	/// Havia aqui um `_vidaDoBoneco` vindo do `EntityState.Vida`, e as duas provas de dano eram
	/// "a vida caiu no snapshot". Esse campo saiu do jogo a pedido do dono (ver `EntityState`):
	/// vida alheia nao viaja mais, nem pro jogador nem pra bancada.
	///
	/// A PROVA NAO SE PERDEU, ela so voltou pra fonte certa: quem confirma dano e o `S2C.Hit`, que
	/// chega COM O NUMERO pros dois envolvidos (`AnunciarGolpe` manda a versao "magra", sem dano,
	/// pra quem so assiste) -- e este robo E o atacante. O `_danoSomado` e o servidor falando.
	/// A pose responde a outra metade: se o boneco caiu ou continua de pe.
	/// ================================================================================
	/// </summary>
	private Protocol.Pose _poseDoBoneco;

	private int _golpesMeus;
	private double _danoSomado;
	private int _correcoes;
	private Vec2 _ultimaCorrecao;
	private int _pacotesDeSnapshot;

	/// <summary>A tecnica como ela estava ANTES do relogin -- a referencia da familia 3.</summary>
	private TecnicaCustomizada? _antesDoRelogin;

	private static GameClient? C => GameClient.Instance;

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (!ok) _falhas.Add(oque + (detalhe.Length > 0 ? $" [{detalhe}]" : ""));
	}

	private void Nota(string texto) => _passos.Add("  --     " + texto);

	// =====================================================================
	// LIGACAO
	// =====================================================================
	public override void _Ready()
	{
		int porta = Protocol.DefaultPort;
		string[] args = OS.GetCmdlineArgs();
		int i = Array.IndexOf(args, "--rede");
		if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int p) && p > 0) porta = p;

		// O SERVIDOR SOBE AQUI, e na porta desta rodada. Sem isto o robo discaria no vazio -- quem
		// normalmente chama o `Start` e a tela de login, e esta bancada nao passa por ela.
		if (Jandirus.Server.GameServer.Instance is { } srv && !srv.Running && !srv.Start(porta))
		{
			GD.PushError($"[diagki] nao consegui abrir a porta {porta} -- ha outro servidor nela?");
			return;
		}

		if (C is not { } cli) return;
		cli.SlotsRecebidos += AoReceberSlots;
		cli.Joined += AoEntrar;
		cli.SnapshotReceived += AoReceberSnapshot;
		cli.Golpe += AoLevarOuDarGolpe;
		cli.Corrected += AoSerCorrigido;
		cli.Falou += AoOuvir;
		cli.Rejected += AoSerRecusado;
		cli.PeerLooked += AoVerAFicha;

		_conta = Prefixo + porta;
		GD.Print($"[diagki] conta '{_conta}' na porta {porta}");
		cli.Conectar("127.0.0.1", porta, _conta, Senha);
	}

	private string _conta = Prefixo;

	public override void _ExitTree()
	{
		if (C is not { } cli) return;
		cli.SlotsRecebidos -= AoReceberSlots;
		cli.Joined -= AoEntrar;
		cli.SnapshotReceived -= AoReceberSnapshot;
		cli.Golpe -= AoLevarOuDarGolpe;
		cli.Corrected -= AoSerCorrigido;
		cli.Falou -= AoOuvir;
		cli.Rejected -= AoSerRecusado;
		cli.PeerLooked -= AoVerAFicha;
	}

	/// <summary>
	/// O SERVIDOR FECHOU A PORTA. Reprovar na hora em vez de esperar o prazo de 180 s: a primeira
	/// rodada desta bancada ficou tres minutos calada porque a conta ja existia com OUTRA senha (de
	/// um teste manual anterior), e a mensagem que explicava tudo -- *"senha incorreta"* -- estava no
	/// log do servidor e nunca chegava ao placar.
	/// </summary>
	private void AoSerRecusado(string motivo)
	{
		Conferir(false, "o servidor aceitou o login desta bancada", motivo);
		if (motivo.Contains("senha", StringComparison.OrdinalIgnoreCase))
			Nota($"a conta '{_conta}' ja existe com outra senha -- apague o arquivo dela na pasta de "
				 + "saves do servidor, ou rode com outra porta em `--rede`.");
		Terminar();
	}

	private void AoReceberSlots(List<SlotInfo> slots)
	{
		_slots = slots;
		_fase = Fase.Selecao;
	}

	private void AoEntrar(int id, ZoneKey zona, Vec2 spawn, string nome)
	{
		_meuId = id;
		_minhaPos = spawn;
		// O RELOGIN VOLTA NOUTRO PONTO DO ROTEIRO: a primeira entrada comeca do zero; a segunda cai
		// direto na familia da persistencia.
		_passo = _antesDoRelogin == null ? 0 : PassoDepoisDoRelogin;
		_fase = Fase.Mundo;
	}

	private void AoReceberSnapshot(List<EntityState> estados)
	{
		_pacotesDeSnapshot++;
		foreach (EntityState e in estados)
		{
			if (e.Id == _meuId) { _minhaPos = e.Pos; continue; }
			if (e.Id == _idDoBoneco) { _poseDoBoneco = e.Pose; _posDoBoneco = e.Pos; }
		}
	}

	/// <summary>
	/// QUEM E O BONECO -- pelo NOME, e nao pelo corpo mais proximo.
	///
	/// ============================ A TERRA NAO ESTA VAZIA ============================
	/// A primeira versao pegava a entidade mais perto de mim no snapshot, e pegou um CIDADAO de
	/// molde que estava por ali: a rajada acertava o coitado (o servidor confirmava os golpes
	/// direitinho) e a vida do boneco -- que nunca foi tocado -- continuava em 100. O placar dizia
	/// "o tiro nao machucou ninguem" sobre um tiro que machucou a pessoa errada.
	///
	/// O nome vem no `S2C.PeerLook`, que o cliente ja recebe de todo corpo da zona.
	/// ===========================================================================
	/// </summary>
	/// <param name="fusao">O tipo de fusao do `PeerLook`. Este robo nao funde ninguem -- ver `GameClient.PeerLooked`.</param>
	private void AoVerAFicha(int quem, string nome, string raca, string genero,
							 Jandirus.Core.Appearance.Appearance visual, Jandirus.Core.Social.TipoDeFusao? fusao)
	{
		if (quem != _meuId && nome.Contains("Boneco", StringComparison.OrdinalIgnoreCase))
			_idDoBoneco = quem;
	}

	private void AoLevarOuDarGolpe(Protocol.HitEvent h)
	{
		if (h.Atacante != _meuId) return;
		_golpesMeus++;
		_danoSomado += h.Dano;
	}

	private void AoSerCorrigido(Vec2 onde) { _correcoes++; _ultimaCorrecao = onde; }

	private void AoOuvir(Protocol.Fala canal, string autor, string texto)
	{
		if (canal == Protocol.Fala.Sistema) _avisos.Add(texto);
	}

	/// <summary>O servidor disse alguma coisa com este pedaco depois do ultimo `Limpar`?</summary>
	private bool Ouviu(string pedaco)
		=> _avisos.Exists(a => a.Contains(pedaco, StringComparison.OrdinalIgnoreCase));

	private void Limpar() { _avisos.Clear(); _golpesMeus = 0; _danoSomado = 0; _correcoes = 0; }

	// =====================================================================
	// O RELOGIO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_acabou) return;

		_vida += delta;
		if (_vida > PrazoTotal)
		{
			Conferir(false, $"a bancada terminou dentro do prazo de {PrazoTotal:0}s",
					 $"travou na fase {_fase}, passo {_passo}");
			Terminar();
			return;
		}

		_t += delta;
		if (_t < Passo) return;
		_t = 0;

		if (C is not { } cli) return;

		switch (_fase)
		{
			case Fase.Selecao: PassoNaSelecao(cli); break;
			case Fase.Mundo: PassoNoMundo(cli); break;
			case Fase.Voltando:
				// ============================ NAO SE RECONECTA NO MESMO QUADRO ============================
				// `Desconectar` so ENFILEIRA a saida: o `NetManager` ainda tem o peer velho na lista
				// dele ate a proxima volta do `PollEvents`, e um `Connect` pro mesmo endereco antes
				// disso nao abre conexao nova nem dispara `PeerConnected` -- ou seja, o login nunca
				// e mandado e a lista de slots nunca volta. Foi exatamente o que a primeira rodada
				// mediu ("a lista de slots nao voltou"), com o robo esperando calado.
				//
				// Um segundo de folga e a mesma dose que a `--diagslot` usa entre ciclos.
				// ====================================================================================
				if (++_espera == 3) Reconectar();
				if (_espera > 40)
				{
					Conferir(false, "o servidor aceitou o relogin", "a lista de slots nao voltou");
					Terminar();
				}
				break;
		}
	}

	// =====================================================================
	// FASE 1 -- A CONTA NOVA
	// =====================================================================
	private void PassoNaSelecao(GameClient cli)
	{
		switch (_passo++)
		{
			case 0:
				// ============================ TERRENO LIMPO ============================
				// A conta e reusada entre rodadas (o arquivo fica no disco do servidor). Sem apagar,
				// a segunda execucao comecaria com a tecnica da primeira ja pronta -- e a familia 1,
				// que afirma "conta nova nao tem tecnica nenhuma", reprovaria por sujeira.
				//
				// Apagar aqui NAO enfraquece a familia 3: o relogin dela acontece no meio da mesma
				// rodada, com o personagem vivo.
				// ==================================================================
				for (int i = 0; i < _slots.Count; i++)
					if (_slots[i].Ocupado) cli.SendDeleteChar(i, _slots[i].Nome);
				break;

			case 1:
				Conferir(_slots.Count(s => s.Ocupado) == 0,
						 "a conta comeca vazia (conta NOVA, pelo fio)",
						 $"{_slots.Count(s => s.Ocupado)} slot(s) ocupado(s)");
				CriarPersonagem(cli);
				break;

			// ---- daqui em diante quem manda e o `Joined`; se ele nao vier, o prazo reprova ----
			case 2:
				// RELOGIN: a lista voltou porque o robo desconectou de proposito.
				if (_antesDoRelogin != null) { cli.PedirSlot(0); break; }
				_passo = 2;   // ainda esperando o primeiro `Joined`
				break;

			default:
				_passo = 2;
				break;
		}
	}

	private static void CriarPersonagem(GameClient cli)
	{
		var ficha = new Jandirus.Core.Races.CharacterDraft
		{
			Name = Personagem, Race = "Human", Planet = "Earth", Gender = "Male",
			Age = 18, Porte = "Medium",
			Backstory = "Personagem da bancada de ki, criado por linha de comando.",
		};
		cli.CriarPersonagem(0, ficha, new Jandirus.Core.Appearance.Appearance { Cabelo = "Goku" });
	}

	// =====================================================================
	// FASE 2 -- O MUNDO
	// =====================================================================
	/// <summary>Onde o roteiro recomeca depois do relogin. Ver <see cref="AoEntrar"/>.</summary>
	private const int PassoDepoisDoRelogin = 100;

	private void PassoNoMundo(GameClient cli)
	{
		switch (_passo++)
		{
			// ---------------------------------------------------------- o chao
			case 0:
				// ESPERA O MUNDO CHEGAR: sem snapshot nao ha posicao autoritativa nem boneco, e
				// medir qualquer coisa antes disso seria medir o vazio.
				if (_pacotesDeSnapshot < 5 || _idDoBoneco == 0) { _passo = 0; return; }
				Conferir(cli.Customizadas.Count == 0,
						 "conta nova entra SEM tecnica inventada nenhuma",
						 $"{cli.Customizadas.Count}");
				Conferir(_idDoBoneco != 0, "o boneco do servidor esta na minha zona (o alvo da familia 2)",
						 $"id {_idDoBoneco}, pose {_poseDoBoneco}");
				Limpar();
				cli.SendHabilidade("Custom_Attack1");
				break;

			case 1:
				Conferir(Ouviu("nao tem uma tecnica") && _golpesMeus == 0,
						 "sem ter inventado nada, o verbo da tecnica 1 e recusado e nao machuca ninguem",
						 string.Join(" | ", _avisos));
				Limpar();
				cli.SendVerbo("ca_criar");
				break;

			// ---------------------------------------------------------- a mesa, pelo fio
			case 2:
				Conferir(cli.Mesa is { Id: 1, Gasto: 0 } && cli.Mesa.Tipo == TipoDeProjetil.Beam,
						 "`ca_criar` abre a mesa no id 1, tipo raio e com os 5 pontos na mao",
						 cli.Mesa == null ? "mesa fechada" : $"id {cli.Mesa.Id}, {cli.Mesa.Tipo}, gasto {cli.Mesa.Gasto}");
				cli.SendVerbo("ca_tipo", "blast");
				break;

			case 3:
				Conferir(cli.Mesa is { Tipo: TipoDeProjetil.Blast, Carregavel: false },
						 "trocar pra bola desliga a carga (o `PickAttackType` do DM)",
						 cli.Mesa == null ? "mesa fechada" : $"{cli.Mesa.Tipo}, carrega {cli.Mesa.Carregavel}");
				cli.SendVerbo("ca_texto", "nome/" + NomeDaTecnica);
				cli.SendVerbo("ca_texto", "grito/" + GritoDaTecnica);
				cli.SendVerbo("ca_grito", "grito");
				break;

			case 4:
				Conferir(cli.Mesa?.Nome == NomeDaTecnica && cli.Mesa?.Grito == GritoDaTecnica
						 && cli.Mesa?.DizGrito == true,
						 "nome e grito escolhidos pelo jogador chegam de volta inteiros",
						 $"'{cli.Mesa?.Nome}' / '{cli.Mesa?.Grito}'");
				Limpar();
				// SEIS compras de um ponto -- uma a mais do que cabe.
				for (int i = 0; i < 6; i++) cli.SendVerbo("ca_comprar", nameof(Compra.DanoMais));
				break;

			case 5:
				// ============================ O TETO E DO SERVIDOR, E ISTO E O QUE PROVA ============================
				// O cliente pediu SEIS. A tela nao tem guarda nenhuma (ela nunca chama `Aplicar`), e
				// mesmo assim voltaram CINCO -- e com a recusa escrita. Quem manda o pacote na mao
				// atravessa exatamente a mesma porta.
				// ==============================================================================================
				Conferir(cli.Mesa?.Gasto == TecnicaCustomizada.PontosTotais,
						 $"pedi 6 compras de 1 ponto e o servidor concedeu {TecnicaCustomizada.PontosTotais}",
						 $"gasto {cli.Mesa?.Gasto}");
				Conferir(Math.Abs((cli.Mesa?.BaseDano ?? 0) - 1.3) < 1e-4,
						 "...e a potencia parou em 1,3 (0,8 do padrao + 5 degraus de 0,1)",
						 $"{cli.Mesa?.BaseDano:0.###}");
				Conferir(Ouviu("ponto"), "...e a sexta ouviu o motivo da recusa",
						 string.Join(" | ", _avisos));
				cli.SendVerbo("ca_salvar");
				break;

			case 6:
				Conferir(cli.Customizadas.Count == 1 && cli.Mesa == null,
						 "confirmar fecha a mesa e a tecnica entra na lista",
						 $"{cli.Customizadas.Count} tecnica(s), mesa {(cli.Mesa == null ? "fechada" : "aberta")}");
				Conferir(cli.Customizadas.Count == 1 && cli.Customizadas[0].Criada
						 && cli.Customizadas[0].Verbo == "Custom_Attack1",
						 "...e ela ja vem com o verbo que o botao aciona",
						 cli.Customizadas.Count == 1 ? cli.Customizadas[0].Verbo : "-");
				break;

			// ---------------------------------------------------------- o tiro HONESTO
			case 7:
				// ENCARA O BONECO E MARCA. A posicao mandada e a VERDADEIRA -- este e o unico passo
				// em que o robo nao mente, e ele existe pra dar sentido aos que mentem.
				//
				// UMA RAJADA E NAO UM TIRO: emparelhado em poder (ver o `ArmarAMetadeViva` do
				// servidor), cada acerto tira meio por cento. O que se mede aqui e "o servidor
				// arrancou vida"; a CONTA do dano ja e medida decimal por decimal na outra metade.
				cli.SendState(_minhaPos, Facing.East, moving: false);
				cli.SendAlvo(_idDoBoneco);
				Limpar();
				DispararRajada(cli);
				break;

			case 8:
			case 9:
				DispararRajada(cli);   // a bola leva ~0,3 s por tile: os tiros saem espacados
				break;

			case 10:
				Conferir(_golpesMeus > 0 && _danoSomado > 0,
						 "o tiro HONESTO acerta -- e quem confirmou foi o servidor (`S2C.Hit`)",
						 $"{_golpesMeus} golpe(s), {_danoSomado:0.##} de dano somado");
				Conferir(_poseDoBoneco != Protocol.Pose.Nocauteado,
						 "...e o boneco continua DE PE -- a vida dele nao viaja mais (ver `_poseDoBoneco`), "
					   + "o que o cliente ve de fora e a pose e as feridas",
						 $"pose {_poseDoBoneco} | ferida {FeridaDoBoneco(cli)}");
				Limpar();
				break;

			// ============================ O CEU TEM QUE ESVAZIAR ANTES DAS MENTIRAS ============================
			// A rajada honesta deixa bolas no ar, e uma bola leva 0,3 s por tile: elas continuavam
			// acertando DEPOIS de a mentira ter sido mandada, e a checagem "ninguem se machucou"
			// reprovava contando um golpe honesto de tres passos atras. Dois passos de silencio, e
			// so entao o contador zera.
			// ==============================================================================================
			case 11:
			case 12:
				break;

			case 13:
				Conferir(_golpesMeus == 0, "o ceu esvaziou antes das mentiras (nenhum tiro honesto no ar)",
						 $"{_golpesMeus} golpe(s) atrasado(s)");
				Limpar();
				break;

			// ---------------------------------------------------------- AS MENTIRAS
			case 14:
				// MENTIRA 1: "eu estou colado no boneco" -- 40 tiles adiante, num salto so.
				Limpar();
				_posForjada = new Vec2(_minhaPos.X + 40 * ZoneCollision.TileSize, _minhaPos.Y);
				_posVerdadeira = _minhaPos;
				for (int i = 0; i < 3; i++) cli.SendState(_posForjada, Facing.East, moving: true);
				break;

			case 15:
				Conferir(_correcoes > 0,
						 "MENTIRA 1 (teleporte): o servidor CORRIGE a posicao forjada",
						 $"{_correcoes} correcao(oes), pra {_ultimaCorrecao}");
				Conferir((_minhaPos - _posForjada).Length > 20 * ZoneCollision.TileSize,
						 "...e a posicao autoritativa continua sendo a de verdade, nao a que eu disse",
						 $"eu disse {_posForjada}, o servidor diz {_minhaPos}");
				break;

			case 16:
				// MENTIRA 2: usar uma tecnica customizada que eu NAO inventei (o slot 7 esta vazio).
				Limpar();
				cli.SendHabilidade("Custom_Attack7");
				break;

			case 17:
				Conferir(_golpesMeus == 0 && Ouviu("nao tem uma tecnica"),
						 "MENTIRA 2 (tecnica que nao existe): recusada, e ninguem se machucou",
						 string.Join(" | ", _avisos));
				// MENTIRA 3: uma tecnica de SKILL que este personagem nao aprendeu.
				Limpar();
				cli.SendHabilidade("Ki_Wave");
				break;

			case 18:
				Conferir(_golpesMeus == 0 && Ouviu("nao sabe"),
						 "MENTIRA 3 (skill que nao aprendi): ouve \"voce nao sabe\" e nao sai tiro",
						 string.Join(" | ", _avisos));
				// MENTIRA 4: apertar as letras do quick time event sem estar em embate nenhum.
				Limpar();
				foreach (char c in "ABCDEFGHIJKL") cli.SendClashTecla(c);
				break;

			case 19:
			{
				int antes = _pacotesDeSnapshot;
				Conferir(_golpesMeus == 0 && cli.Connected,
						 "MENTIRA 4 (tecla de embate fora de embate): nao vence nada e nao derruba o servidor",
						 $"conectado {cli.Connected}, golpes {_golpesMeus}");
				_snapshotsNaMentira = antes;
				// MENTIRA 5: comprar por cima do teto numa tecnica JA CONFIRMADA.
				Limpar();
				cli.SendVerbo("ca_editar", "1");
				break;
			}

			case 20:
				Conferir(_pacotesDeSnapshot > _snapshotsNaMentira,
						 "...e o mundo continuou chegando depois das teclas forjadas",
						 $"{_pacotesDeSnapshot - _snapshotsNaMentira} snapshot(s) depois");
				for (int i = 0; i < 8; i++) cli.SendVerbo("ca_comprar", nameof(Compra.DanoMais));
				cli.SendVerbo("ca_salvar");
				break;

			case 21:
				Conferir(cli.Customizadas.Count == 1
						 && cli.Customizadas[0].Gasto <= TecnicaCustomizada.PontosTotais
						 && Math.Abs(cli.Customizadas[0].BaseDano - 1.3) < 1e-4,
						 "MENTIRA 5 (comprar por cima do teto): a tecnica salva continua em 5 pontos",
						 cli.Customizadas.Count == 1
							? $"gasto {cli.Customizadas[0].Gasto}, dano {cli.Customizadas[0].BaseDano:0.###}"
							: "lista vazia");

				// ---------------------------------------------------------- O RELOGIN
				_antesDoRelogin = cli.Customizadas.Count == 1 ? cli.Customizadas[0] : null;
				Conferir(_antesDoRelogin != null, "ha o que persistir antes de derrubar a conexao");
				Nota($"derrubando a conexao com '{_antesDoRelogin?.Nome}' (gasto {_antesDoRelogin?.Gasto}) na mao");
				_fase = Fase.Voltando;
				_espera = 0;
				_idDoBoneco = 0;
				_pacotesDeSnapshot = 0;
				cli.Desconectar();
				// quem reconecta e a fase `Voltando`, depois de tres passos -- ver a nota la.
				break;

			// ---------------------------------------------------------- DEPOIS DO RELOGIN
			case PassoDepoisDoRelogin:
			{
				TecnicaCustomizada? antes = _antesDoRelogin;
				TecnicaCustomizada? agora = cli.Customizadas.Count == 1 ? cli.Customizadas[0] : null;

				Conferir(agora != null, "depois de RELOGAR a tecnica continua na lista",
						 $"{cli.Customizadas.Count} tecnica(s)");
				Conferir(antes != null && agora != null
						 && agora.Nome == antes.Nome && agora.Grito == antes.Grito
						 && agora.DizGrito == antes.DizGrito && agora.Tipo == antes.Tipo,
						 "...com nome, grito e tipo iguais aos de antes",
						 agora == null ? "-" : $"'{agora.Nome}' / '{agora.Grito}' / {agora.Tipo}");
				Conferir(antes != null && agora != null
						 && Math.Abs(agora.BaseDano - antes.BaseDano) < 1e-4
						 && Math.Abs(agora.CustoKi - antes.CustoKi) < 1e-4
						 && agora.Gasto == antes.Gasto,
						 "...e com os NUMEROS e os PONTOS GASTOS, que e o que doi perder",
						 agora == null ? "-" : $"dano {agora.BaseDano:0.###}, gasto {agora.Gasto}");
				break;
			}

			// ============================ ENCARAR VEM UM PASSO ANTES DE ATIRAR ============================
			// Quem volta do disco esta olhando pra onde o save mandou, e o servidor decide a direcao
			// do tiro pelo `Facing` do ultimo input -- que chega pelo canal SEQUENCIADO, enquanto o
			// pedido da tecnica chega pelo CONFIAVEL. Sao dois canais: mandar os dois no mesmo passo
			// deixa o tiro sair pro sul enquanto o "virei pro leste" ainda esta no ar, e a rodada que
			// mediu isso gastou o tanque de Ki acertando o horizonte.
			// ==========================================================================================
			// ============================ ANDAR ATE O BONECO -- DESTA VEZ HONESTAMENTE ============================
			// Depois das mentiras o robo ficou uns tiles a oeste de onde tinha atirado (as correcoes
			// do servidor mandam, nao ele), e dali o caminho ate o boneco passa pelo cenario da
			// Terra: os tiros saiam, batiam numa pedra e a bancada lia isso como "a tecnica nao
			// voltou do disco" -- reprovando a familia certa pelo motivo errado.
			//
			// A aproximacao vai em passos de 60 px por 0,4 s, que cabe na velocidade de caminhada que
			// o `MoveRules` valida. E ela vale como o CONTRA-EXEMPLO da MENTIRA 1: o mesmo canal, com
			// um passo possivel, e aceito -- o que a corrida de la recusou foi o SALTO, e nao o
			// movimento.
			// ==================================================================================================
			case PassoDepoisDoRelogin + 1:
			{
				if (_pacotesDeSnapshot < 5 || _idDoBoneco == 0) { _passo = PassoDepoisDoRelogin + 1; return; }

				Vec2 ate = _posDoBoneco - _minhaPos;
				if (ate.Length > 3 * ZoneCollision.TileSize && _passosAndando++ < 20)
				{
					cli.SendState(_minhaPos + ate.Normalized() * 60, Facing.East, moving: true);
					_passo = PassoDepoisDoRelogin + 1;
					return;
				}
				Conferir(ate.Length <= 4 * ZoneCollision.TileSize,
						 "andando honestamente, o servidor ACEITA o passo e eu chego perto do boneco",
						 $"{ate.Length / ZoneCollision.TileSize:0.#} tiles depois de {_passosAndando} passo(s)");
				cli.SendState(_minhaPos, Facing.East, moving: false);
				cli.SendAlvo(_idDoBoneco);
				break;
			}

			case PassoDepoisDoRelogin + 2:
				Limpar();
				DispararRajada(cli);
				break;

			case PassoDepoisDoRelogin + 3:
				DispararRajada(cli);
				break;

			case PassoDepoisDoRelogin + 4:
			case PassoDepoisDoRelogin + 5:
				break;   // a bola leva ~0,3 s por tile

			case PassoDepoisDoRelogin + 6:
				Conferir(_golpesMeus > 0 && !Ouviu("nao tem uma tecnica"),
						 "...e ela AINDA DISPARA depois do relogin (o verbo voltou junto)",
						 $"{_golpesMeus} golpe(s), {_danoSomado:0.##} de dano | {string.Join(" | ", _avisos)}");
				Conferir(_danoSomado > 0 && _poseDoBoneco != Protocol.Pose.Nocauteado,
						 "...e o boneco, que continuou de pe do outro lado do relogin, levou de novo "
					   + "(o dano quem confirma e o `S2C.Hit`, nao mais a vida no snapshot)",
						 $"{_danoSomado:0.##} de dano | pose {_poseDoBoneco} | ferida {FeridaDoBoneco(cli)} "
					   + $"| eu em {_minhaPos}, ele em {_posDoBoneco}");
				break;

			default:
				Terminar();
				break;
		}
	}

	/// <summary>
	/// TRES TIROS, encarando o boneco. O `SendState` vai junto porque o servidor decide a direcao do
	/// tiro pelo `Facing` do ULTIMO input -- sem ele, o corpo volta a olhar pro sul (a pose de
	/// nascimento) e o ataque sai pro chao.
	/// </summary>
	/// <summary>
	/// AS FERIDAS DO BONECO, em texto -- pro placar, nao pra checagem.
	///
	/// NOTA E NAO `Conferir`: a mascara so sai da zona morta quando um MEMBRO passa de 15% de dano
	/// (ver `Feridas.HematomaComeca`), e a rajada emparelhada desta bancada tira meio por cento por
	/// acerto espalhado pelo corpo -- exigir que ela mude aqui seria uma checagem que reprova por
	/// motivo errado. Ela vai no relatorio porque e o canal que SUBSTITUIU a vida no fio: quem
	/// quebrar o envio de feridas ve isso ficar "limpa" com o boneco levando tiro.
	/// </summary>
	private string FeridaDoBoneco(GameClient cli) =>
		cli.Feridas.TryGetValue(_idDoBoneco, out Jandirus.Core.Combat.MascaraDeFeridas m)
			? m.ToString() : "limpa";

	private void DispararRajada(GameClient cli)
	{
		cli.SendState(_minhaPos, Facing.East, moving: false);
		for (int i = 0; i < 3; i++) cli.SendHabilidade("Custom_Attack1");
	}

	private Vec2 _posDoBoneco;
	private int _passosAndando;
	private int _snapshotsNaMentira;
	private Vec2 _posForjada, _posVerdadeira;

	private void Reconectar()
	{
		int porta = Protocol.DefaultPort;
		string[] args = OS.GetCmdlineArgs();
		int i = Array.IndexOf(args, "--rede");
		if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int p) && p > 0) porta = p;
		C?.Conectar("127.0.0.1", porta, _conta, Senha);
	}

	// =====================================================================
	// O PLACAR
	// =====================================================================
	private void Terminar()
	{
		if (_acabou) return;
		_acabou = true;

		GD.Print("\n[diagki] ===== A METADE VIVA DA BANCADA DE KI =====");
		foreach (string l in _passos) GD.Print("[diagki] " + l);
		GD.Print(_falhas.Count == 0
			? $"[diagki] ===== {_passos.Count(p => p.StartsWith("  ok")):0} passaram, 0 falharam ====="
			: $"[diagki] ===== {_passos.Count(p => p.StartsWith("  ok")):0} passaram, {_falhas.Count} falharam =====\n"
			  + "[diagki]   " + string.Join("\n[diagki]   ", _falhas));

		// SAI SOZINHA. Uma bancada que precisa ser fechada na mao e uma bancada que so roda quando
		// alguem esta olhando -- e o servidor vive neste mesmo processo, entao sair daqui derruba os
		// dois, que e o que se quer no fim de uma rodada.
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
