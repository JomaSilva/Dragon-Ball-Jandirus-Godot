using Jandirus.Core.Combat;
using Jandirus.Core.Ranks;

namespace Jandirus.Core.Ai;

/// <summary>
/// O KIT DO DEUS DA DESTRUICAO NAS DECISOES DO CEREBRO (dono, 2026-10-11: *"faca o bills usar o kit completo"*).
///
/// ============================ NAO E UM SEGUNDO CEREBRO: SAO QUATRO PERGUNTAS NO MESMO ============================
/// Tudo aqui pendura no que ja existia e sai pelo MESMO cano de toda tecnica da IA -- o `Comando.Habilidade`, que o
/// atuador entrega a funcao de habilidade do jogador. Nenhuma linha daqui confere titulo, Ki ou recarga: quem
/// recusa e o verb. O que este arquivo escreve e QUANDO o cerebro pede cada um:
///
///   QUANDO ATIRA     os dois tiros (Destruction God's Fury e Destroyer Sphere) sao linhas do arsenal, e quem os
///                    escolhe e o `EscolherTiro` de todo NPC que atira -- janela, linha de visao, Ki e risco. Nao
///                    ha uma linha sobre eles aqui. (Os numeros da janela: `KitDoDeusNaIa`.)
///   QUANDO ANULA     ha ki alheio VINDO PRA CIMA dele que a Energy Nullification apagaria agora
///                    (`Percepcao.HaKiPraAnular`): o pulso vai por cima do gesto do plano, sem parar o corpo.
///   QUANDO AGARRA    colado no alvo de pe, com a pausa do peteleco vencida (de <see cref="PausaEntrePetelecosMin"/>
///   E PETELECA       a <see cref="PausaEntrePetelecosMax"/> segundos): pede o `agarrar` do jogador. Com alguem nos
///                    bracos o corpo PARA (andar segurando arremessa; socar segurando estrangula) e, depois de um
///                    instante que da pra ver, da o Power Flick -- se quem esta nos bracos e QUEM ELE ENFRENTA. O
///                    bote pegou outro corpo (um caido, um curioso)? Ele larga, sem ferir.
///   QUANDO EXECUTA   o Hakai PEGA o alvo (`Percepcao.HakaiPegaOAlvo`) -- de pe ou, quase sempre, ja no chao: o
///                    plano vira <see cref="Plano.Executar"/>. Ao alcance da mao, o Hakai; longe, com a esfera na
///                    janela, a Destroyer Sphere (o Hakai a distancia); senao ele anda ate la.
///
/// O God's Temper nao aparece: e passiva, e dispara sozinha no funil de dano. A Fury tambem nao: e forma, e quem a
/// veste (ou nao) e quem dirige o corpo, e nao o cerebro.
/// ==============================================================================================================
/// </summary>
public sealed partial class Cerebro
{
	/// <summary>
	/// Entre dois pedidos do MESMO gesto do kit. O verb pode recusar (o corpo atordoado, um raio na mao, a presa
	/// que escapou): meio segundo e o que impede o cerebro de marretar a porta a 30 Hz.
	/// </summary>
	public const double PausaDoGestoDoKit = 0.5;

	/// <summary>O primeiro agarrao de uma luta nao sai no primeiro encontro: uns segundos de soco antes.</summary>
	public const double SegundosAteOPrimeiroPeteleco = 4;

	/// <summary>De quanto em quanto tempo, colado, ele agarra pra dar o peteleco (sorteado nesta faixa a cada agarrao).</summary>
	public const double PausaEntrePetelecosMin = 9, PausaEntrePetelecosMax = 15;

	/// <summary>Com a presa nos bracos, o tempo ate o peteleco sair: o agarrao tem de ser VISTO antes do golpe.</summary>
	public const double SegundosComAPresaNaMao = 0.35;

	/// <summary>
	/// Se o gesto nao saiu neste prazo (o verb recusou todas as vezes), o cerebro larga de mao e volta ao plano -- e
	/// o primeiro passo do plano ARREMESSA quem ele segurava, que e a regra do agarrao pra todo mundo.
	/// </summary>
	public const double PrazoComAPresaNaMao = 2.5;

	/// <summary>
	/// O ALCANCE DA MAO: a distancia em que os verbs de agarrao acham alguem -- o alcance do golpe
	/// (`CombatKnobs.Alcance`), com uma folga. NAO e a distancia de que a <see cref="Pressao"/> soca: o soco tem
	/// ARRANQUE (o corpo salta ate o alvo) e por isso sai de mais longe; o agarrao e o Hakai nao tem, e quem os pede de
	/// fora da mao ouve "nao ha ninguem ao seu alcance" pra sempre, parado. Entre um alcance e o outro o cerebro da o
	/// passo que falta.
	/// </summary>
	private static float AlcanceDaMao => CombatKnobs.Alcance * 0.9f;

	private double _pausaDoKit;                                       // entre dois pedidos de Hakai / peteleco
	private double _pausaDaAnulacao;                                  // entre dois pedidos de anulacao
	private double _pausaDoPeteleco = SegundosAteOPrimeiroPeteleco;   // ate o proximo agarrao
	private double _segurandoHa;                                      // ha quanto tempo ha alguem nos bracos

	/// <summary>Os relogios do kit, no mesmo lugar em que os outros andam (o topo do <see cref="Pensar"/>).</summary>
	private void TicarOKit(in Percepcao p, double dt)
	{
		if (_pausaDoKit > 0) _pausaDoKit -= dt;
		if (_pausaDaAnulacao > 0) _pausaDaAnulacao -= dt;
		if (_pausaDoPeteleco > 0) _pausaDoPeteleco -= dt;
		_segurandoHa = p.Segurando != 0 ? _segurandoHa + dt : 0;
	}

	// =====================================================================
	// QUANDO EXECUTA -- o Hakai, e a Destroyer Sphere como Hakai a distancia
	// =====================================================================

	/// <summary>
	/// O alvo esta no ponto do Hakai, e este corpo carrega o kit? E a pergunta do <see cref="Escolher"/>. (2026-10-17) Ou o alvo esta CAIDO
	/// e a execucao do Ruthless Mind esta na mao -- a mesma porta, a mesma receita (`Cerebro.Raciais.cs`).
	/// </summary>
	private bool ExecutaOAlvo(in Percepcao p) =>
		(Poderes.KitDoDeus && p.HakaiPegaOAlvo) || (Poderes.ExecucaoRacialPronta && p.TemAlvo && p.AlvoCaido);

	/// <summary>
	/// A RECEITA DE EXECUTAR, e ela tem tres degraus, do mais certo pro mais longe:
	///
	///   1. AO ALCANCE DA MAO (<see cref="AlcanceDaMao"/>): o verb do Hakai -- *"executa quem voce esta agarrando (ou
	///      o alvo colado a frente)"*. Um pulso, e a pausa.
	///   2. LONGE, com a Destroyer Sphere no arsenal e o alvo na janela dela (linha livre, Ki que paga): a esfera,
	///      que tenta o Hakai em quem fere. E um tiro: paga a pausa do tiro.
	///   3. Senao, anda ate ele.
	///
	/// A esfera pode FALHAR e isso e do kit, nao daqui: os criterios sao medidos depois do dano dela, e um impacto
	/// nao-letal no proprio torso o levanta acima dos 15%. Quando falha, o alvo deixa de estar no ponto e o plano
	/// se desfaz sozinho na decisao seguinte.
	/// </summary>
	private Comando Execucao(in Percepcao p, bool guarda, Random rng)
	{
		if (p.AlcancoPelaAltura && p.Distancia <= AlcanceDaMao)
		{
			if (_pausaDoKit > 0) return new Comando { Guardar = guarda, Olhar = p.Direcao };
			_pausaDoKit = PausaDoGestoDoKit;
			// O HAKAI pra quem o carrega; senao, a execucao do RUTHLESS MIND (2026-10-17) -- de frente pro caido, porque o verb executa quem
			// esta NA FRENTE (`GameServer.QuemEstaNaFrente`).
			return Poderes.KitDoDeus && p.HakaiPegaOAlvo
				? new Comando { Habilidade = KitDoDeus.VerbHakai }
				: new Comando { Habilidade = Races.Passivas.MenteCruel, Olhar = p.Direcao };
		}

		if (EsferaNaJanela(p, out Tiro esfera))
		{
			if (_pausaDoTiro > 0) return new Comando { Guardar = guarda };
			_pausaDoTiro = PausaDepoisDoTiro;
			Dizer(Momento.Disparar, rng);
			return new Comando { Habilidade = esfera.Id, Guardar = guarda };
		}

		return new Comando
		{
			Rumo = p.Direcao,
			Guardar = guarda,
			QuerSubir = QuerSubirPara(p),
			QuerDescer = QuerDescerPara(p),
		};
	}

	/// <summary>
	/// A Destroyer Sphere esta no arsenal e o alvo cabe na janela dela AGORA? As mesmas quatro perguntas do
	/// `EscolherTiro` (alcance, linha, Ki, reserva de voo), menos o risco: quem esta no ponto do Hakai quase sempre
	/// esta no chao, e alvo no chao nao desvia.
	/// </summary>
	private bool EsferaNaJanela(in Percepcao p, out Tiro esfera)
	{
		esfera = default;
		for (int i = 0; i < Poderes.DeLonge.Quantas; i++)
		{
			Tiro t = Poderes.DeLonge[i];
			if (t.Id != KitDoDeus.VerbEsfera) continue;
			esfera = t;
			return p.Distancia >= t.AlcanceMin && p.Distancia <= t.AlcanceMax
				&& (!t.PrecisaDeLinhaLivre || p.LinhaLivre)
				&& p.Ki - t.CustoDeKi >= ReservaDeKi(p);
		}
		return false;
	}

	// =====================================================================
	// QUANDO AGARRA E PETELECA -- o Power Flick
	// =====================================================================

	/// <summary>
	/// E HORA DE AGARRAR? Colado (o alcance do soco, na altura certa) num alvo de pe, sem a guarda erguida, de maos
	/// vazias e com a pausa do peteleco vencida. Perguntado no topo da <see cref="Pressao"/> -- agarrar e gesto de
	/// quem esta na troca de socos, e nao de quem recua, atira ou foge.
	/// </summary>
	private bool QuerAgarrar(in Percepcao p, bool guarda) =>
		Poderes.KitDoDeus && _pausaDoPeteleco <= 0 && !guarda && p.Segurando == 0
		&& p.TemAlvo && !p.AlvoCaido && p.AlcancoPelaAltura && p.Distancia <= DistanciaIdeal * 1.6f;

	/// <summary>
	/// ...e o gesto. FORA DA MAO (<see cref="AlcanceDaMao"/>) ele da o passo que falta, sem soco -- o soco tem
	/// arranque, e o corpo passaria do ponto. Na mao, UM pulso do `agarrar` do jogador, com a pausa cobrada na hora
	/// (acerte ou erre o bote: quem responde se pegou e o agarrao, e o cerebro so fica sabendo pelo
	/// <see cref="Percepcao.Segurando"/>).
	/// </summary>
	private Comando Agarrar(in Percepcao p, Random rng)
	{
		if (p.Distancia > AlcanceDaMao)
			return new Comando { Rumo = p.Direcao, QuerSubir = QuerSubirPara(p), QuerDescer = QuerDescerPara(p) };

		_pausaDoPeteleco = PausaEntrePetelecosMin + rng.NextDouble() * (PausaEntrePetelecosMax - PausaEntrePetelecosMin);
		Porque = "kit: agarrar pra dar o peteleco";
		return new Comando { Habilidade = KitDoDeusNaIa.VerbAgarrar };
	}

	/// <summary>
	/// COM ALGUEM NOS BRACOS, o que o corpo faz -- ou nulo, e ai vale o plano.
	///
	/// PARADO, e isso e a regra do agarrao e nao gosto: o primeiro PASSO de quem segura ARREMESSA, e o soco de quem
	/// segura vira estrangulamento. Entao o comando sai sem rumo, sem golpe e sem guarda; depois do instante de
	/// <see cref="SegundosComAPresaNaMao"/> vem o pulso -- o Power Flick, ou o Hakai se o Hakai pega o alvo (o verb
	/// do Hakai E um movimento de agarrao).
	///
	/// SO EM QUEM ELE ENFRENTA. O agarrao pega o corpo mais perto na frente, e esse pode nao ser o alvo: quem ja
	/// caiu, um cadaver, um curioso parado ao lado da briga. Nesse caso o pulso e o proprio `agarrar`, de novo -- o
	/// segundo toque levanta, o terceiro SOLTA (os tres estados da tecla) --, e ninguem que nao esta na luta leva
	/// peteleco nem e arremessado.
	///
	/// Passado o <see cref="PrazoComAPresaNaMao"/> sem o gesto ter saido, devolve nulo: o plano volta, anda, e quem
	/// estava preso e arremessado. Nao ha corpo que fique nos bracos dele pra sempre.
	/// </summary>
	private Comando? ComAPresaNaMao(in Percepcao p, Random rng)
	{
		if (p.Segurando == 0) return null;
		// (quem agarrou por um golpe do repertorio, e nao pelo kit: a receita dele mora em `Cerebro.Repertorio.cs`)
		if (!Poderes.KitDoDeus) return ComAPresaNaMaoDoRepertorio(p, rng);
		if (_segurandoHa > PrazoComAPresaNaMao) return null;
		if (_segurandoHa < SegundosComAPresaNaMao || _pausaDoKit > 0) return new Comando();

		_pausaDoKit = PausaDoGestoDoKit;
		if (p.Segurando != p.IdDoAlvo)
		{
			Porque = "kit: o bote pegou outro corpo -- larga";
			return new Comando { Habilidade = KitDoDeusNaIa.VerbAgarrar };
		}

		Porque = p.HakaiPegaOAlvo ? "kit: Hakai em quem esta nos bracos" : "kit: peteleco em quem esta nos bracos";
		return new Comando { Habilidade = p.HakaiPegaOAlvo ? KitDoDeus.VerbHakai : KitDoDeus.VerbFlick };
	}

	// =====================================================================
	// QUANDO ANULA -- a Energy Nullification
	// =====================================================================

	/// <summary>
	/// HA KI VINDO QUE A ANULACAO APAGARIA: o pulso entra no comando deste tique, por cima do gesto do plano (o corpo
	/// continua andando, guardando ou socando). Se o tique ja leva outro verb, a anulacao espera o proximo -- a
	/// percepcao continua dizendo sim enquanto o tiro estiver no ar.
	///
	/// A recarga de cinco minutos e do verb, e so arma se algo sumiu; a pausa daqui e so contra a marretada.
	/// </summary>
	private Comando AnularOQueVem(in Percepcao p, Comando gesto)
	{
		if (!Poderes.KitDoDeus || !p.HaKiPraAnular || _pausaDaAnulacao > 0) return gesto;
		if (gesto.Habilidade is { Length: > 0 }) return gesto;

		_pausaDaAnulacao = PausaDoGestoDoKit;
		Porque = "kit: anular o ki que vem";
		return gesto with { Habilidade = KitDoDeus.VerbAnular };
	}
}
