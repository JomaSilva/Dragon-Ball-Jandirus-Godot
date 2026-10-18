using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// O DESPERTAR DO KI DIVINO, LADO DO SERVIDOR -- quem absorve, quanto, e o instante em que desperta.
///
/// ============================ O ACHADO (2026-10-10) ============================
/// `godki.awakened = true` so era escrito em bancada. O perfil de formas (`Perfil`, `GameServer.Formas.cs`),
/// o fato que abre o "Challenge God of Destruction" (`FatosDe`, `GameServer.Fatos.cs`), a porta do duelo
/// (`GameServer.CargoDuelo.cs`) e a das disciplinas divinas (`GameServer.Disciplinas.cs`) ja LIAM o ki
/// divino -- e nenhum caminho de producao o ligava. As formas divinas e o desafio ao Deus eram
/// inalcancaveis no jogo de verdade.
/// ===============================================================================
///
/// ============================ O QUE MORA AQUI ============================
/// As regras e as constantes estao no Core (`Core/Forms/DespertarDivino.cs`, `Core/World/AtmosferaDivina.cs`).
/// Aqui mora o que e do servidor:
///
///   * <see cref="TickDoGodKi"/> (1 Hz): quem esta num mundo de atmosfera divina absorve -- e ouve, ao
///     chegar, que o ar dali e outro;
///   * <see cref="ExposicaoPorGolpe"/>: o gancho do desfecho de todo golpe que encosta (soco, contra e
///     tiro de ki passam os tres pelo `ResolverDesfecho`);
///   * <see cref="Expor"/>: o funil dos dois caminhos -- e o unico lugar que soma no medidor;
///   * <see cref="DespertarOGodKi"/>: o unico lugar do servidor que desperta alguem;
///   * <see cref="TickDaMaestriaDivina"/>: o `godki_mastery_tick()` do DM, que tambem nao tinha motor;
///   * os dois verbs de admin, pra testar sem os dez minutos nem a viagem.
///
/// O QUE NAO E PRECISO AVISAR A NINGUEM: o perfil de formas e os fatos sao LIDOS do estado (o fato, cinco
/// vezes por segundo). No tique seguinte ao despertar a tecla C ja oferece a forma divina e o botao do
/// desafio ja existe.
/// =========================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// OS BOTOES DE DEFEITO (bancada --godkiteste) -- falsos em jogo, sempre
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada --godkiteste): ninguem pergunta pelo requisito -- o corpo de BP baixo
	/// acumula e desperta como qualquer outro. Lido por <see cref="AlcancaOKiDivino"/>.
	/// </summary>
	internal bool GodKiSemRequisitoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --godkiteste): todo chao conta como atmosfera divina -- o planeta comum
	/// desperta tanto quanto o raro. Lido por <see cref="EmAtmosferaDivina"/>.
	/// </summary>
	internal bool GodKiEmQualquerChaoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --godkiteste): o golpe conta sem troca nenhuma -- bater num deus parado
	/// (ou caido) e apanhar de um sem revidar enchem o medidor. Lido por <see cref="ExposicaoPorGolpe"/>.
	/// </summary>
	internal bool GodKiSemRevideDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --godkiteste): a luta nao tem teto por minuto -- uma rajada de golpes
	/// enche o medidor em segundos. Lido por <see cref="ExporPelaLuta"/>.
	/// </summary>
	internal bool GodKiSemTetoDeLutaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --godkiteste): o medidor enche e ninguem liga `awakened` -- o jogo de
	/// antes, em que so bancada despertava. Lido por <see cref="DespertarOGodKi"/>.
	/// </summary>
	internal bool GodKiNaoDespertaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --godkiteste): a maestria divina nao tem motor -- quem desperta fica em
	/// 0% pra sempre, e o Blue (33%) continua inalcancavel. Lido por <see cref="TickDaMaestriaDivina"/>.
	/// </summary>
	internal bool MaestriaDivinaParadaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada de tela --diaggodki): o retrato do ki divino so sai uma vez -- o medidor
	/// anda no servidor e o cartao da aba World fica parado. Lido por <see cref="MandarGodKi"/>.
	/// </summary>
	internal bool GodKiCongeladoDeTeste;

	/// <summary>O que saiu no fio pelo `S2C.GodKi`: (quem, bits, exposicao em %, maestria em decimos). Ligada so pela bancada.</summary>
	internal static List<(int Quem, byte Bits, byte Pct, ushort Maestria)>? EscutaDoGodKi;

	/// <summary>Quem teve o despertar anunciado pra vizinhanca (`S2C.GodKiDespertou`), e pra quantas telas. Ligada so pela bancada.</summary>
	internal static List<(int Quem, int Telas)>? EscutaDoDespertar;

	// =====================================================================
	// O ESTADO DE SESSAO
	// =====================================================================
	/// <summary>
	/// O RELOGIO DESTE SISTEMA, em segundos -- e ele so anda no <see cref="TickDoGodKi"/>.
	///
	/// Nao e o `NowMs()` de proposito: as duas regras que olham o tempo aqui (a janela do revide e o teto
	/// por minuto) sao sobre tempo de JOGO simulado, e com o relogio de parede a bancada teria de esperar
	/// minutos de verdade pra ver o teto abrir de novo. Com este, dez minutos sao seiscentas chamadas.
	/// Um golpe leva a hora do ultimo tique: um segundo de grao, contra janelas de dez e de sessenta.
	/// </summary>
	private double _relogioDoGodKi;

	/// <summary>O que este sistema lembra de cada jogador ENQUANTO ele esta online. O que persiste e so o medidor, na ficha.</summary>
	private sealed class ExposicaoDeSessao
	{
		/// <summary>A tela de quem este estado e. Id se reusa: quem herda o id de quem saiu comeca do zero.</summary>
		public NetPeer? Tela;
		/// <summary>A zona conferida no ultimo tique -- e por ela que se percebe a CHEGADA.</summary>
		public ulong Zona;
		/// <summary>Ja ouviu, nesta visita, que o corpo esta absorvendo o ar.</summary>
		public bool Absorvendo;
		/// <summary>Ja ouviu, nesta visita, que o corpo ainda nao alcanca. A frase e UMA.</summary>
		public bool OuviuQueNaoAlcanca;
		/// <summary>Ja ouviu, nesta sessao, que trocar golpes com um deus acorda o ki dele.</summary>
		public bool OuviuDaLuta;
		/// <summary>Ja ouviu, nesta sessao, que sente o ki do outro e o corpo nao alcanca.</summary>
		public bool OuviuQueNaoAlcancaNaLuta;
		/// <summary>O teto por minuto do que entra por luta.</summary>
		public TetoDeLuta Teto;
		/// <summary>O ultimo retrato que saiu no fio (-1 = nenhum ainda).</summary>
		public int Mandado = -1;
		/// <summary>O despertar aconteceu no meio de um golpe: o save vai no tique seguinte, com o golpe resolvido.</summary>
		public bool Gravar;
	}

	private readonly Dictionary<int, ExposicaoDeSessao> _godKiDe = [];

	/// <summary>
	/// QUANDO X ACERTOU Y PELA ULTIMA VEZ (relogio deste sistema) -- so dos pares em que um dos dois
	/// carrega ki divino. E o que responde "ele revidou?". As entradas vencidas saem no tique.
	/// </summary>
	private readonly Dictionary<(int De, int Em), double> _golpesComUmDeus = [];

	private ExposicaoDeSessao SessaoDoGodKi(ServerPlayer pl)
	{
		if (_godKiDe.TryGetValue(pl.Id, out ExposicaoDeSessao? e) && ReferenceEquals(e.Tela, pl.Peer)) return e;
		return _godKiDe[pl.Id] = new ExposicaoDeSessao { Tela = pl.Peer };
	}

	// =====================================================================
	// AS PERGUNTAS
	// =====================================================================
	/// <summary>
	/// A SEMENTE DESTE PERSONAGEM -- a que sorteou os limiares dele no nascimento (`LimiaresPessoais.Semente`,
	/// gravada no save). Quem nasceu antes de ela existir cai na que se reconstroi do nome e do instante de
	/// criacao, a mesma da cor da aura e da arte do Kamehameha (<see cref="SementeDeArte"/>).
	/// </summary>
	private static ulong SementeDoGodKi(ServerPlayer pl) =>
		pl.Forma.Limiares is { Rolado: true, Semente: not 0UL } limiares ? limiares.Semente : SementeDeArte(pl);

	/// <summary>O BP base que ESTE personagem precisa ter pra o ki divino despertar nele.</summary>
	internal static double RequisitoDeGodKi(ServerPlayer pl) => DespertarDivino.RequisitoDeBpBase(SementeDoGodKi(pl));

	/// <summary>O corpo ja alcanca o ki divino? BP BASE contra o requisito pessoal -- ver `DespertarDivino.AlcancaORequisito`.</summary>
	private bool AlcancaOKiDivino(ServerPlayer pl) =>
		GodKiSemRequisitoDeTeste || DespertarDivino.AlcancaORequisito(pl.Ficha, SementeDoGodKi(pl));

	/// <summary>Este corpo esta num chao de atmosfera divina? Funcao pura da zona -- o cliente responde igual.</summary>
	private bool EmAtmosferaDivina(ServerPlayer pl) =>
		GodKiEmQualquerChaoDeTeste ? Espaco.EhPlaneta(pl.Zone) : AtmosferaDivina.Tem(SeedDoUniverso, pl.Zone);

	/// <summary>
	/// ESTE CORPO E UM "DEUS" PRO SEGUNDO CAMINHO? Carrega ki divino (desperto, ou numa forma divina) E e
	/// gente: jogador ou NPC do mundo. O clone da mente, o boneco do corpo largado e a copia de uma divisao
	/// ficam de fora -- eles levam a ficha do dono, e treinar contra o reflexo de um deus nao e lutar com um.
	/// </summary>
	private static bool EhUmDeus(ServerPlayer corpo) =>
		(EhJogador(corpo) || EhNpcDoMundo(corpo))
		&& DespertarDivino.CarregaKiDivino(corpo.Ficha, corpo.Forma.Atual);

	// =====================================================================
	// 1. A ATMOSFERA -- o tique de 1 Hz
	// =====================================================================
	/// <summary>
	/// UM SEGUNDO DE MUNDO pro ki divino de cada jogador: a chegada num mundo divino, o que o ar dali
	/// enche no medidor, a maestria de quem ja e deus e o retrato que vai pro cliente.
	///
	/// SO JOGADOR. Um NPC nao desperta por ficar parado num planeta -- se um chefe tiver ki divino, e o
	/// molde dele que diz.
	/// </summary>
	/// <param name="dt">Os segundos que passaram. O tique manda 1; a bancada manda o que quiser medir.</param>
	private void TickDoGodKi(double dt = 1)
	{
		if (GodKiDeTeste) DispararABancadaDoGodKi();   // a bancada `--godkiteste` (so com a flag) -- ver `GameServer.GodKiTeste.cs`

		// A `--marcosdeformateste` PEGA CARONA NESTE TIQUE, e o motivo e so um: ela poe e tira corpos do mundo, e
		// o lugar natural dela (o laco de fichas, onde a conferencia dos marcos mora) esta no meio de um `foreach`
		// sobre os corpos. Ver `GameServer.MarcosDeFormaTeste.cs`.
		if (MarcosDeFormaDeTeste) DispararABancadaDosMarcosDeForma();

		_relogioDoGodKi += dt;

		foreach (ServerPlayer pl in _players.Values)
		{
			if (!EhJogador(pl)) continue;
			ExposicaoDeSessao e = SessaoDoGodKi(pl);

			bool divino = EmAtmosferaDivina(pl);
			if (e.Zona != pl.Zone.Hash)
			{
				e.Zona = pl.Zone.Hash;
				e.Absorvendo = false;
				e.OuviuQueNaoAlcanca = false;
				if (divino) AvisarDaAtmosfera(pl);
			}

			if (divino) RespirarOArDivino(pl, e, dt);
			TickDaMaestriaDivina(pl, dt);

			if (e.Gravar) { e.Gravar = false; Persistir(pl); }
			MandarGodKi(pl, e);
		}

		FaxinaDoGodKi();
	}

	/// <summary>A CHEGADA: o jogador fica sabendo que o ar daqui e outro. Uma vez por entrada no mundo.</summary>
	private void AvisarDaAtmosfera(ServerPlayer pl)
	{
		Avisar(pl, DespertarDivino.JaDespertou(pl.Ficha)
			? $"o ar de {NomeDoMundo(pl.Zone)} é divino -- e o seu ki responde a ele."
			: $"o ar de {NomeDoMundo(pl.Zone)} é diferente de tudo o que você já respirou: este mundo tem uma ATMOSFERA DIVINA.");
		GD.Print($"[godki] {pl.Name} chegou a um mundo de atmosfera divina ({pl.Zone})");
	}

	/// <summary>
	/// UM PASSO DE AR DIVINO. As duas frases desta visita saem daqui -- a de quem absorve e a de quem
	/// ainda nao alcanca --, cada uma UMA vez; quem cruza o requisito sem sair do mundo ouve a segunda
	/// virar a primeira.
	/// </summary>
	private void RespirarOArDivino(ServerPlayer pl, ExposicaoDeSessao e, double dt)
	{
		Fighter f = pl.Ficha;
		if (DespertarDivino.JaDespertou(f) || EhCadaver(pl)) return;   // o deus ja tem; o corpo caido morto nao respira

		if (!AlcancaOKiDivino(pl))
		{
			// ABAIXO DO REQUISITO: uma frase, e o tempo nao conta.
			e.Absorvendo = false;
			if (e.OuviuQueNaoAlcanca) return;
			e.OuviuQueNaoAlcanca = true;
			Avisar(pl, "você sente esse ki na pele, mas o seu corpo ainda não o alcança: falta poder de BASE "
					   + "pra ele despertar em você. Aqui o tempo não conta enquanto isso não mudar.");
			return;
		}

		if (!e.Absorvendo)
		{
			e.Absorvendo = true;
			Avisar(pl, $"o seu corpo já aguenta este ar e começa a absorvê-lo: fique neste mundo e o ki divino "
					   + $"desperta em você ({f.ExposicaoDivina:0}% até agora -- menu P > World).");
		}

		Expor(pl, DespertarDivino.PorSegundoNaAtmosfera * dt, FonteDaExposicao.Atmosfera);
	}

	// =====================================================================
	// 2. A LUTA -- o gancho do golpe que encosta
	// =====================================================================
	/// <summary>
	/// UM GOLPE ENCOSTOU (chamado do `ResolverDesfecho`, por onde passam o soco, o contra-ataque e o tiro
	/// de ki). Se um dos dois carrega ki divino e os dois estao TROCANDO golpes, o outro lado se expoe.
	///
	/// ============================ AS QUATRO RECUSAS ============================
	///   * GOLPE QUE NAO CONECTA nem chega aqui: quem chama so chama com `r.Encostou`;
	///   * EM CADAVER tambem nao: o `ResolverDesfecho` sai antes, no `EhCadaver`;
	///   * EM QUEM JA ESTAVA NO CHAO: `KO` de antes deste golpe (o que derrubou AGORA ainda conta -- o corpo
	///     estava de pe quando levou);
	///   * SEM REVIDE: o golpe so vale se o outro lado tambem acertou voce ha menos de
	///     `DespertarDivino.SegundosDeRevide`. Um deus parado nao ensina nada a quem bate nele, e ficar
	///     parado apanhando de um deus tambem nao e lutar.
	/// E, passando por elas, o <see cref="ExporPelaLuta"/> ainda cobra o teto por minuto.
	/// ===========================================================================
	///
	/// A PRIMEIRA LINHA E O CAMINHO DE QUASE TODO GOLPE DO SERVIDOR: sem ki divino em nenhum dos dois lados
	/// ela sai em duas consultas, e a caderneta de golpes so guarda pares em que ha um deus.
	/// </summary>
	private void ExposicaoPorGolpe(ServerPlayer autor, ServerPlayer vitima, GolpeResultado r)
	{
		if (autor == vitima) return;
		bool autorDeus = EhUmDeus(autor), vitimaDeus = EhUmDeus(vitima);
		if (!autorDeus && !vitimaDeus) return;

		bool jaEstavaCaida = vitima.Ficha.KO && !r.Nocauteou;
		if (jaEstavaCaida && !GodKiSemRevideDeTeste) return;

		_golpesComUmDeus[(autor.Id, vitima.Id)] = _relogioDoGodKi;
		bool revidou = _golpesComUmDeus.TryGetValue((vitima.Id, autor.Id), out double quando)
					   && _relogioDoGodKi - quando <= DespertarDivino.SegundosDeRevide;
		if (!revidou && !GodKiSemRevideDeTeste) return;

		if (vitimaDeus) ExporPelaLuta(autor, vitima, DespertarDivino.PorGolpeDado);
		if (autorDeus) ExporPelaLuta(vitima, autor, DespertarDivino.PorGolpeRecebido);
	}

	/// <summary><paramref name="quem"/> trocou um golpe com <paramref name="deus"/>: o que cabe no teto entra no medidor.</summary>
	private void ExporPelaLuta(ServerPlayer quem, ServerPlayer deus, double valor)
	{
		if (!EhJogador(quem) || DespertarDivino.JaDespertou(quem.Ficha) || EhCadaver(quem)) return;
		ExposicaoDeSessao e = SessaoDoGodKi(quem);

		if (!AlcancaOKiDivino(quem))
		{
			// ABAIXO DO REQUISITO, pelo segundo caminho: a mesma regra, e a frase tambem e uma.
			if (e.OuviuQueNaoAlcancaNaLuta) return;
			e.OuviuQueNaoAlcancaNaLuta = true;
			Avisar(quem, $"você sente no golpe um ki que não é deste mundo -- o de {deus.Name} é divino. "
						 + "O seu corpo ainda não o alcança: falta poder de BASE pra ele despertar em você.");
			return;
		}

		double cabe = GodKiSemTetoDeLutaDeTeste ? valor : e.Teto.Conceder(_relogioDoGodKi, valor);
		if (cabe <= 0) return;

		if (!e.OuviuDaLuta)
		{
			e.OuviuDaLuta = true;
			Avisar(quem, $"o ki de {deus.Name} é divino, e o seu corpo já o alcança: cada golpe trocado com ele "
						 + "acorda o seu. Lute de verdade -- bater em quem não revida não ensina nada.");
		}
		Expor(quem, cabe, FonteDaExposicao.Luta);
	}

	// =====================================================================
	// 3. O FUNIL -- os dois caminhos somam aqui
	// =====================================================================
	/// <summary>
	/// SOMA NO MEDIDOR, conta a etapa ao jogador e, quando ele enche, desperta. Devolve quanto entrou.
	///
	/// A PORTA E CONFERIDA DE NOVO AQUI, e nao so em quem chama: desperto, cadaver ou abaixo do requisito
	/// nao acumula por caminho NENHUM -- e o proximo caminho que alguem escrever nao tem como esquecer.
	///
	/// O "CADAVER" E O DO COMBATE (`EhCadaver`): o corpo morto no chao dos vivos. A alma de pe no Outro
	/// Mundo nao e cadaver -- la ela luta, apanha e treina como um vivo, e desperta como um.
	/// </summary>
	private double Expor(ServerPlayer pl, double quanto, FonteDaExposicao fonte)
	{
		Fighter f = pl.Ficha;
		if (quanto <= 0 || DespertarDivino.JaDespertou(f) || EhCadaver(pl) || !AlcancaOKiDivino(pl)) return 0;

		double antes = f.ExposicaoDivina;
		double entrou = DespertarDivino.Somar(f, quanto);

		if (f.ExposicaoDivina >= DespertarDivino.ExposicaoCheia) DespertarOGodKi(pl, fonte);
		else if (DespertarDivino.EtapaCruzada(antes, f.ExposicaoDivina) is > 0 and var etapa)
			Avisar(pl, etapa switch
			{
				>= 75 => $"o ki divino quase transborda em você ({etapa:0}%): falta pouco.",
				>= 50 => $"metade do caminho ({etapa:0}%): o seu ki já não é o mesmo -- há algo sereno e imenso por baixo dele.",
				_ => $"o ki divino se junta em você ({etapa:0}%).",
			});
		return entrou;
	}

	// =====================================================================
	// 4. O DESPERTAR
	// =====================================================================
	/// <summary>
	/// O KI DIVINO DESPERTA NESTE CORPO -- o unico lugar do servidor que faz isso. A ficha e escrita pelo
	/// `DespertarDivino.Despertar` (o `get_godki()` do DM, com o que veio e o que nao veio dito la); aqui e
	/// o que so o servidor pode fazer: recalcular o poder, contar ao jogador e a quem esta em volta, e gravar.
	///
	/// NAO HA CINEMATICA DE DESPERTAR pra reusar: as cenas do `Cinematicas.cs` sao das FORMAS, e a do Super
	/// Saiyajin God (com o clarao do `do_first_godki_appearance`) toca quando ele subir nela pela primeira
	/// vez. O que sai daqui e o som do ki divino chegando e um anel de luz (`S2C.GodKiDespertou`) -- o
	/// `GodkiIncreaseScene()` do `godki.dm:156-163`, menos o terremoto.
	/// </summary>
	/// <returns>Despertou agora. Falso pra quem ja era deus.</returns>
	private bool DespertarOGodKi(ServerPlayer pl, FonteDaExposicao fonte)
	{
		if (GodKiNaoDespertaDeTeste) return false;

		ResultadoDoDespertar r = DespertarDivino.Despertar(pl.Ficha);
		if (!r.Despertou) return false;

		// O PODER MUDA AGORA (o `godki_give_mult`): a conta e refeita ja, e nao no proximo tique de ficha --
		// quem desperta no meio de uma luta ja bate com o poder novo no golpe seguinte.
		pl.Ficha.PowerLevel(agoraMs: NowMs());
		MandarFicha(pl);

		Avisar(pl, "O KI DIVINO DESPERTOU EM VOCÊ. O seu corpo guarda agora uma energia que não se sente de fora -- "
				   + "calma, imensa, e sua.");
		if (r.BonusDeBp > 0)
			Avisar(pl, $"o seu poder passa a valer x{r.BonusDeBp:0.##}, pra sempre -- transformado ou não.");
		else if (pl.Ficha.SaiyanLineage == Oozaru.LinhagemPrimal)
			Avisar(pl, "o seu sangue Primal não bebe desse poder: nele o ki divino só abre o caminho do Limit Breaker.");
		if (r.Marco > 0)
			Avisar(pl, $"MARCO DE PODER! o seu corpo rompeu um novo patamar: todo ganho agora é x{r.Marco:0.##}.");
		if (r.SangueDivino)
			Avisar(pl, $"o seu sangue já conhecia esse ki: você desperta com {r.Maestria:0}% de maestria divina.");
		Avisar(pl, "as formas divinas da sua linhagem (tecla C) e o desafio ao Deus da Destruição (menu P) passam a "
				   + "depender só de você. A maestria divina cresce USANDO o ki divino, numa forma que o peça.");

		AnunciarODespertar(pl);
		GD.Print($"[godki] {pl.Name} DESPERTOU o ki divino ({fonte}) | BP base {pl.Ficha.BP:N0} "
				 + $"(requisito {RequisitoDeGodKi(pl):N0}) | bonus x{r.BonusDeBp:0.##} | marco x{r.Marco:0.##} | maestria {r.Maestria:0}%");

		// O SAVE VAI NO PROXIMO TIQUE DESTE SISTEMA, e nao aqui: o despertar pode acontecer no meio da
		// resolucao de um golpe, e gravar a foto de um corpo com o golpe pela metade nao e gravar o despertar.
		ExposicaoDeSessao e = SessaoDoGodKi(pl);
		e.Gravar = true;
		MandarGodKi(pl, e);
		return true;
	}

	/// <summary>
	/// QUEM ESTA EM VOLTA VE E OUVE: a frase no chat e o `S2C.GodKiDespertou` (o som e o anel de luz), pra
	/// quem esta na mesma zona e dentro da vista -- no espaco a "zona" e o universo inteiro.
	/// </summary>
	private void AnunciarODespertar(ServerPlayer pl)
	{
		var w = Protocol.Begin(Protocol.S2C.GodKiDespertou);
		w.Put(pl.Id);

		int telas = 0;
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
		{
			if ((o.Pos - pl.Pos).LengthSquared > RaioDaVista * RaioDaVista) continue;
			if (o != pl) Avisar(o, $"{pl.Name} é tomado por uma luz que não é deste mundo: o KI DIVINO despertou nele!");
			if (o.Peer == null) continue;
			o.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			telas++;
		}
		// (o proprio jogador pode ainda nao estar na lista da zona -- e o caso do 1o login -- e recebe do mesmo jeito)
		if (pl.Peer != null && !ZoneList(pl.Zone.Hash).Contains(pl))
		{
			pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			telas++;
		}
		EscutaDoDespertar?.Add((pl.Id, telas));
	}

	// =====================================================================
	// 5. A MAESTRIA DIVINA -- o `godki_mastery_tick()` que tambem nao tinha motor
	// =====================================================================
	/// <summary>
	/// A MAESTRIA DIVINA DE QUEM USA O KI DIVINO SOBE. Sem isto o despertar abriria o Super Saiyajin God
	/// (0%) e mais nada: o Blue pede 33%, o Evolution e o Beast 50%, as disciplinas divinas 70% -- e
	/// `godki.mastery` nao tinha um unico escritor de producao.
	///
	/// E O `godki_mastery_tick()` (`godki.dm:123-130`): so de quem despertou, so com o ki divino em uso, o
	/// dobro com a tag de combate (`combatTag`), ate 100. "EM USO" e estar numa forma que o usa -- ver
	/// `DespertarDivino.UsaOKiDivino` pro porque de nao haver o interruptor do DM aqui.
	///
	/// OS MARCOS (`godki_mastery_milestone`, `godki.dm:132-148`) saem com as frases do original, traduzidas
	/// pro que este port tem. O que NAO veio: os 37 pontos de especializacao por marco (o port nao tem os
	/// focos Fortitude/Power/Body do ki divino) e a cena de terremoto.
	/// </summary>
	private void TickDaMaestriaDivina(ServerPlayer pl, double dt)
	{
		if (MaestriaDivinaParadaDeTeste) return;
		// (2026-10-16) A ZONA QUE NAO DEIXA MASTERIZAR FORMA (a mente -- `RitmoDaZona`) para a do ki divino tambem: ela e a
		// maestria das formas divinas. So o "nao" vale aqui -- o ritmo de zona (a Sala do Tempo, 4x) nunca acelerou esta.
		if (pl.Ficha.zoneMasteryMult <= 0) return;
		if (!DespertarDivino.JaDespertou(pl.Ficha) || EhCadaver(pl) || pl.Ficha.KO) return;
		if (!DespertarDivino.UsaOKiDivino(Catalogo.Def(pl.Forma.Atual), pl.Ficha.Class, pl.Ficha.SaiyanLineage)) return;

		// (2026-10-16) O ECHOED INSANITY do Corrupted Majin com a Forma Pura: *"master everything 20% slower"*. Ver `FatorDeMaestriaDoCorpo`.
		double marco = DespertarDivino.SubirMaestria(pl.Ficha, dt * FatorDeMaestriaDoCorpo(pl), emCombate: pl.Combate is { EmCombate: > 0 });
		if (marco <= 0) return;

		string classe = pl.Ficha.Class;
		bool prodigial = string.Equals(classe, Catalogo.ClasseProdigial, StringComparison.OrdinalIgnoreCase);
		bool elite = string.Equals(classe, Catalogo.ClasseElite, StringComparison.OrdinalIgnoreCase)
					 || string.Equals(classe, Catalogo.ClasseRose, StringComparison.OrdinalIgnoreCase);
		Avisar(pl, marco switch
		{
			>= 100 => "maestria TOTAL do ki divino.",
			>= Catalogo.GodkiUiUePct => $"maestria divina {marco:0}%: o seu corpo está pronto pros segredos dos Anjos e dos Deuses da "
										+ "Destruição (Ultra Instinct e Poder da Destruição) -- falta quem os ensine.",
			>= Catalogo.GodkiRoyalePct => prodigial
				? $"maestria divina {marco:0}%: algo BESTIAL espreita no fundo do seu potencial."
				: elite ? $"maestria divina {marco:0}%: a evolução da sua forma divina está ao alcance do seu sangue."
						: $"maestria divina {marco:0}%: o seu corpo agora aguenta o Kaio-ken por cima da forma divina.",
			_ => prodigial
				? $"o seu ki divino amadurece ({marco:0}%): o Místico agora expressa o poder inteiro dele."
				: $"o seu ki divino amadurece ({marco:0}%): o degrau seguinte das formas divinas está ao seu alcance.",
		});
		GD.Print($"[godki] {pl.Name} cruzou {marco:0}% de maestria divina");
	}

	// =====================================================================
	// O FIO
	// =====================================================================
	/// <summary>
	/// O RETRATO DO KI DIVINO DESTE JOGADOR (`S2C.GodKi`), so quando muda: os bits, a exposicao em pontos
	/// inteiros e a maestria em decimos. E o que a aba World desenha no cartao "Ki divino".
	///
	/// O REQUISITO NAO VIAJA, so o bit de que o corpo o alcanca: o numero e BP BASE, e manda-lo seria
	/// entregar o proprio poder a quem nao tem scouter. A maestria so vai pra quem despertou.
	/// </summary>
	private void MandarGodKi(ServerPlayer pl, ExposicaoDeSessao e)
	{
		if (pl.Peer is not { } tela) return;

		Fighter f = pl.Ficha;
		bool desperto = DespertarDivino.JaDespertou(f);
		Protocol.BitsDoGodKi bits = Protocol.BitsDoGodKi.Nenhum;
		if (desperto) bits |= Protocol.BitsDoGodKi.Desperto;
		if (!desperto && AlcancaOKiDivino(pl)) bits |= Protocol.BitsDoGodKi.AlcancaORequisito;
		if (EmAtmosferaDivina(pl)) bits |= Protocol.BitsDoGodKi.NaAtmosfera;

		// PONTOS INTEIROS, com um fio de folga: sessenta somas de um sexto dao 9,999999999999998, e sem a
		// folga o cartao mostraria "9%" no instante em que o medidor fez dez.
		byte pct = (byte)Math.Clamp(Math.Floor(f.ExposicaoDivina + 1e-6), 0, 100);
		ushort maestria = desperto ? (ushort)Math.Clamp(Math.Floor(f.godki!.mastery * 10 + 1e-6), 0, 1000) : (ushort)0;

		int retrato = (byte)bits | pct << 8 | maestria << 16;
		if (e.Mandado == retrato || (GodKiCongeladoDeTeste && e.Mandado != -1)) return;
		e.Mandado = retrato;

		EscutaDoGodKi?.Add((pl.Id, (byte)bits, pct, maestria));

		var w = Protocol.Begin(Protocol.S2C.GodKi);
		w.Put((byte)bits);
		w.Put(pct);
		w.Put(maestria);
		tela.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// A FAXINA: o estado de quem saiu e os golpes que ja venceram a janela do revide. So roda quando sobra
	/// entrada -- num servidor sem deus nenhum os dois dicionarios ficam do tamanho da lista de jogadores e de zero.
	/// </summary>
	private void FaxinaDoGodKi()
	{
		if (_godKiDe.Count > _players.Count)
			foreach (int id in _godKiDe.Keys.ToList())
				if (!_players.ContainsKey(id)) _godKiDe.Remove(id);

		if (_golpesComUmDeus.Count == 0) return;
		foreach (((int, int) par, double quando) in _golpesComUmDeus.ToList())
			if (_relogioDoGodKi - quando > DespertarDivino.SegundosDeRevide) _golpesComUmDeus.Remove(par);
	}

	// =====================================================================
	// OS DOIS VERBS DE ADMIN -- ferramentas de teste, como o `admin_forma`
	// =====================================================================
	/// <summary>
	/// A LINHA DO KI DIVINO NO `Assess Target` (`AdminFicha`). E o unico lugar do jogo que mostra o REQUISITO
	/// em numero: pro jogador ele e sigilo (e BP base), e o admin precisa dele pra julgar "por que eu nao desperto?".
	/// </summary>
	private string LinhaDoKiDivinoPraAdmin(ServerPlayer p) =>
		DespertarDivino.JaDespertou(p.Ficha)
			? $"  ki divino DESPERTO · maestria {p.Ficha.godki!.mastery:0.#}% · o requisito dele era {RequisitoDeGodKi(p):N0} de BP base"
			: $"  ki divino: requisito pessoal {RequisitoDeGodKi(p):N0} de BP base ({(AlcancaOKiDivino(p) ? "alcança" : "não alcança")})"
			  + $" · medidor {p.Ficha.ExposicaoDivina:0.#}%" + (EmAtmosferaDivina(p) ? " · numa atmosfera divina" : "");

	/// <summary>
	/// `admin_godki`: DESPERTA O KI DIVINO do alvo marcado (sem alvo, o do proprio admin), IGNORANDO o
	/// requisito e o medidor. E o "Force God Ki" do `Godki_Settings` (`godkiattain.dm:76-77`,
	/// `gain_godki(100, TRUE)`).
	///
	/// PASSA PELO MESMO FUNIL do despertar de verdade (<see cref="DespertarOGodKi"/>): o que o admin ve e o
	/// que o jogador vai ver -- o bonus, o marco, a frase, o anel de luz.
	/// </summary>
	private void AdminDespertarOGodKi(ServerPlayer adm, string alvo)
	{
		ServerPlayer p = PorNome(alvo) ?? adm;
		if (!EhPessoa(adm, p)) return;   // ficha de corpo sem dono nao vai pra disco nenhum

		if (!DespertarOGodKi(p, FonteDaExposicao.Admin))
		{
			Avisar(adm, $"{p.Name} já tem o ki divino desperto ({p.Ficha.godki?.mastery ?? 0:0.#}% de maestria).");
			return;
		}
		Avisar(adm, $"{p.Name}: ki divino DESPERTO pela sua mão (BP base {p.Ficha.BP:N0}; o requisito dele é "
					+ $"{RequisitoDeGodKi(p):N0}).");
	}

	/// <summary>
	/// `admin_mundo_divino`: LEVA O ADMIN A ORBITA DE UM MUNDO DE ATMOSFERA DIVINA -- o mais perto que o corpo
	/// dele aguenta. Nao tem original (o DM nao tem atmosfera). Existe pelo motivo do `admin_estrela`: o mundo
	/// divino mais perto do nascimento fica a varios minutos de voo, e conferir a regra em jogo sem isto
	/// custaria a viagem a cada tentativa.
	///
	/// SO NO ESPACO, como o `admin_estrela` e pelo motivo escrito la: fora dele o verb RECUSA em vez de decolar
	/// sozinho -- um verb que muda de zona por conta propria faz duas coisas, e a segunda ninguem pediu. (E a
	/// `--diagadmin` dispara todos os verbs de admin de uma vez com o corpo no chao: um que o largasse no vacuo
	/// o mataria em vinte segundos, no meio da bancada dos outros.)
	///
	/// ELE LARGA EM ORBITA, logo acima do disco (`Espaco.PontoDeDecolagem`), e nao no chao: pousar e
	/// encostar no planeta, e e pelo pouso de verdade que o aviso da chegada tem de aparecer.
	///
	/// "QUE O CORPO AGUENTA": gravidade ate a maestria de gravidade do admin. O mundo divino mais perto
	/// pode puxar 40 vezes, e uma ferramenta de teste que larga o corpo esmagado ensina a coisa errada. Se
	/// nenhum cabe, vai o mais LEVE -- e a frase diz quanto ele puxa.
	/// </summary>
	private void AdminIrParaOMundoDivino(ServerPlayer adm)
	{
		if (!Espaco.EhEspaco(adm.Zone))
		{
			Avisar(adm, "decole primeiro: do espaço, este verb salta até a órbita de um mundo de atmosfera divina.");
			return;
		}

		MundoDivino? perto = null, leve = null;
		double menor = double.MaxValue, maisLeve = double.MaxValue;
		SistemaId c = SistemaId.De(adm.Pos);
		foreach (MundoDivino m in AtmosferaDivina.NaRegiao(SeedDoUniverso, c.Sx, c.Sy, AtmosferaDivina.RaioGarantidoEmCelulas * 2))
		{
			if (ZonaMorta(Espaco.ZonaDe(m.Corpo))) continue;   // um planeta destruido nao tem ar nenhum
			double g = MundoProcedural.GravidadeDaSeed(m.Corpo.Seed);
			double d = (m.Corpo.Pos - adm.Pos).Length;
			if (g < maisLeve || (g == maisLeve && leve is { } l && d < (l.Corpo.Pos - adm.Pos).Length)) { maisLeve = g; leve = m; }
			if (g <= adm.Ficha.GravMastered && d < menor) { menor = d; perto = m; }
		}

		if ((perto ?? leve) is not { } mundo)
		{
			Avisar(adm, "nenhum mundo de atmosfera divina nas células em volta.");
			return;
		}

		MoveToZone(adm.Id, ZonaDoEspaco, Espaco.PontoDeDecolagem(mundo.Corpo));

		// O CARIMBO DE CHUNK E A VIZINHANCA VAO JUNTO -- o mesmo par de linhas do `AdminIrParaAEstrela`:
		// sem eles quem salta pra outra chunk chega sem corpo nenhum desenhado, inclusive o planeta.
		adm.ChunkAtual = ChunkId.De(adm.Pos);
		MandarVizinhanca(adm);

		MundoProcedural ficha = MundoProcedural.DaSeed(mundo.Corpo.Seed, mundo.Corpo.Nome);
		Avisar(adm, $"você salta até a órbita de {NomeDoMundo(mundo.Corpo)} ({ficha.Descricao()}), um mundo de atmosfera "
					+ $"divina{(mundo.PorGarantia ? " (pela garantia da região)" : "")}. Encoste nele pra pousar."
					+ (perto == null ? $" ATENÇÃO: nenhum mundo divino por perto puxa menos que a sua maestria de gravidade "
									   + $"({adm.Ficha.GravMastered:0.#}x) -- este é o mais leve." : ""));
		GD.Print($"[server] {adm.Name} foi levado a orbita do mundo divino {mundo.Corpo.Nome} [{mundo.Sx}:{mundo.Sy}]k{mundo.K}");
	}
}
