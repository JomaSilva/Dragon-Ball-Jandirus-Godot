using Jandirus.Core.Combat;

namespace Jandirus.Core.Races;

/// <summary>
/// O QUE O SERVIDOR SABE DO CORPO NESTE TIQUE -- e tudo o que as passivas precisam. Uma por corpo, reaproveitada (e escrita a cada
/// tique pelo `GameServer.Passivas`): a lista de campos e longa, e um `record` novo por tique por corpo seria lixo pro coletor.
/// </summary>
public sealed class EntradaRacial
{
	public KitRacial Kit;

	/// <summary>
	/// (2026-10-17) AS SKILLS DOS KITS DE RACA QUE VALEM NESTE CORPO, uma por bit (`SkillRacial`): o kit inteiro da raca, ou as que o
	/// BIO-ANDROIDE escolheu do sangue (o adendo do dono). O tique pergunta por elas (<see cref="Tem"/>); o <see cref="Kit"/> fica pros kits
	/// de CLASSE (Primal, Lendario, Lendario Primal, mutante).
	/// </summary>
	public SkillRacial Skills;

	/// <summary>Esta skill de kit de raca vale neste corpo.</summary>
	public bool Tem(SkillRacial s) => (Skills & s) != 0;

	/// <summary>A tag de combate esta de pe (`CombatState.EmCombate`, 90 s).</summary>
	public bool EmCombate;

	/// <summary>Segundos de combate CONTINUO (`EstadoDeForma.CombateSegundos`).</summary>
	public double CombateSegundos;

	/// <summary>A forma de agora (o id do catalogo; "base" fora de forma) e o multiplicador dela neste instante (>= 1).</summary>
	public string Forma = "base";
	public double MultDaForma = 1;

	/// <summary>A maestria da forma sobe sustentando-a (`Catalogo.SustentarTreina`) -- as "formas que podem ser dominadas".</summary>
	public bool FormaTreinavel;

	/// <summary>A forma de agora esta dominada (100%).</summary>
	public bool FormaDominada;

	/// <summary>A forma de agora e DIVINA (God Ki, Instinto, Ego, Beast); o resto e mortal.</summary>
	public bool FormaDivina;

	/// <summary>A forma de agora e de uma linha Saiyajin (Saiyajin, Legendary, Legendary Primal, Futuro).</summary>
	public bool FormaSaiyajin;

	/// <summary>A forma de agora e Lendaria (linhas Legendary e Legendary Primal) -- o "LSSJ" do Demonic Saiyan.</summary>
	public bool FormaLendaria;

	/// <summary>O Legendary Super Saiyan deste corpo ja despertou (o `primal_legendary` no Primal Lendario).</summary>
	public bool LssjAberto;

	/// <summary>A maior forma mortal DOMINADA: 0 nenhuma, 1 SSJ1/Wrathful, 2 SSJ2/C-Type, 3 SSJ3/Legendary (`Passivas.DegrauMortal`).</summary>
	public int MaiorMortalDominada;

	/// <summary>O degrau SSJ4 da forma de agora (`Passivas.DegrauSsj4`).</summary>
	public int DegrauSsj4;

	/// <summary>Os recursos, em fracao do maximo: Ki, vigor, vida (o corpo inteiro) e o tronco.</summary>
	public double Ki = 1, Vigor = 1, Vida = 1, Tronco = 1;

	/// <summary>A barra de poder (`Fighter.powerMod`, 0,01 a 1) e o relaxar.</summary>
	public double Barra = 1;
	public bool Relaxado;

	/// <summary>O rabo esta no corpo (Primal Evolution).</summary>
	public bool TemRabo;

	/// <summary>Treinando (a tecla T) -- Persistence.</summary>
	public bool Treinando;

	/// <summary>Nocauteado ou morto: as ativas de prazo caem, as cargas se desfazem.</summary>
	public bool Caido;

	/// <summary>Nao andou neste tique (a carga do Demonic Saiyan e parada).</summary>
	public bool Parado = true;

	/// <summary>O corpo, pros membros (Blazing Spirit, Warrior's Vitality). Nulo = sem corpo de combate.</summary>
	public Body? Corpo;
}

/// <summary>
/// O QUE AS PASSIVAS FAZEM NESTE TIQUE -- e o servidor que escreve isto nos canais. Tudo neutro (1 ou 0) quando nada vale.
/// </summary>
public sealed class EfeitoRacial
{
	/// <summary>Multiplica o BP (familia 1 do `PowerLevel`) e SOMA no multiplicador da forma (False Advantage, Prodigy).</summary>
	public double Bp = 1, SomaNaForma;

	/// <summary>Os stats (fator do R inteiro no `Statify`, como o canal `forma*`).</summary>
	public double Physoff = 1, Physdef = 1, Speed = 1, Kioff = 1, Kidef = 1, Kiskill = 1, Tecnica = 1;

	/// <summary>Regeneracao de Ki (a passiva -- parado -- e a ativa -- C e meditacao) e de vigor.</summary>
	public double RegenKiPassiva = 1, RegenKiAtiva = 1, RegenVigor = 1;

	/// <summary>Dreno de forma e de toggle (0 = nenhum), consumo das tecnicas de ki, e os tetos de Ki e de vigor.</summary>
	public double Dreno = 1, CustoKi = 1, MaxKi = 1, MaxVigor = 1;

	/// <summary>O teto da carga de Ki (o 200%) e a velocidade da carga de TECNICA (o tempo ate o raio sair).</summary>
	public double TetoDaCarga = 1, CargaDeTecnica = 1;

	/// <summary>Dano DADO (todo tipo), um a mais so pro de ki, e o dano RECEBIDO (antes da reducao).</summary>
	public double DanoDado = 1, DanoDeKi = 1, DanoRecebido = 1;

	/// <summary>Reducao de dano em %, 0 a 100 -- as fontes se COMBINAM multiplicando o que sobra.</summary>
	public double ReducaoDeDano;

	/// <summary>A reducao POR MEMBRO pela vida dele (Extreme Tenacity).</summary>
	public bool Tenaz;

	/// <summary>Os debuffs de membro escalados (Warrior's Vitality 0,5; Primitive Rage 1,1) e o nocaute (0,5 = metade do tempo).</summary>
	public double FatorDosMembros = 1, FatorDoNocaute = 1;

	/// <summary>O ganho de treino (Persistence).</summary>
	public double GanhoDeTreino = 1;

	/// <summary>Sem arremesso e sem atordoamento; sem efeitos negativos; a morte negada; invulneravel.</summary>
	public bool Inabalavel, SemEfeitosNegativos, NegaMorte, Invulneravel;

	/// <summary>A barra presa em 100% (Unmatched Ferocity).</summary>
	public bool BarraCheia;

	/// <summary>Quanto o tronco perde por segundo, em % (Overflowing Power e Overflowing Ki).</summary>
	public double TroncoPorSegundo;

	/// <summary>Ki e vigor que voltam por segundo, em fracao do maximo (Second Wind), e o Ki que um toggle cobra por segundo.</summary>
	public double KiPorSegundo, VigorPorSegundo, KiDrenadoPorSegundo;

	/// <summary>O tanque do Demonic Saiyan acabou de abrir: Ki e vigor enchem ate o teto (que esta 3x).</summary>
	public bool EncherReservas;

	/// <summary>Pro HUD: o que valeu neste tique e nao cabe num fator -- a soma da False Advantage, o bonus da escalada, o degrau do
	/// orgulho.</summary>
	public double FalsaVantagem, Escalada;
	public int Orgulho;

	public void Zerar()
	{
		Bp = 1; SomaNaForma = 0;
		Physoff = Physdef = Speed = Kioff = Kidef = Kiskill = Tecnica = 1;
		RegenKiPassiva = RegenKiAtiva = RegenVigor = 1;
		Dreno = CustoKi = MaxKi = MaxVigor = 1;
		TetoDaCarga = CargaDeTecnica = 1;
		DanoDado = DanoDeKi = DanoRecebido = 1;
		ReducaoDeDano = 0;
		Tenaz = false;
		FatorDosMembros = FatorDoNocaute = GanhoDeTreino = 1;
		Inabalavel = SemEfeitosNegativos = NegaMorte = Invulneravel = BarraCheia = EncherReservas = false;
		TroncoPorSegundo = KiPorSegundo = VigorPorSegundo = KiDrenadoPorSegundo = 0;
		FalsaVantagem = Escalada = 0;
		Orgulho = 0;
	}

	/// <summary>Soma uma reducao a outra: o que sobra de dano MULTIPLICA (20% e 10% dao 28%, e nunca passam de 100%).</summary>
	public void SomarReducao(double porcento) =>
		ReducaoDeDano = 100 - (100 - ReducaoDeDano) * (100 - Math.Clamp(porcento, 0, 100)) / 100;

	public void TodosOsStats(double f)
	{
		Physoff *= f; Physdef *= f; Speed *= f; Kioff *= f; Kidef *= f; Kiskill *= f; Tecnica *= f;
	}
}

/// <summary>
/// ============================ O ESTADO RACIAL DE UM CORPO (v0.4.0) ============================
/// O que as passivas e as ativas LEMBRAM de um tique pro outro: as janelas das ativas, os degraus que se acumulam, os debuffs que o
/// corpo levou de skill inimiga. Os relogios sao absolutos, em ms (o `NowMs` do servidor).
///
/// **NADA DAQUI VAI PRO SAVE** -- e o mesmo motivo do "buff nao vai pro save": quem desloga transbordando nao volta transbordando.
/// O que atravessa o relog sao as RECARGAS, e elas moram na ficha (`Fighter.RecargasRaciais`), pelo motivo oposto: deslogar nao pode
/// ser o jeito de zerar a recarga de 20 min do Demonic.
///
/// QUALQUER CORPO TEM UM, com kit ou sem: os debuffs da Spirit Disruption e da Ruthless Mind caem em quem quer que seja acertado.
/// ==============================================================================================
/// </summary>
public sealed class EstadoRacial
{
	public readonly EntradaRacial Entrada = new();
	public readonly EfeitoRacial Efeito = new();

	// ---- Seasoned Warrior --------------------------------------------------------------------
	private readonly HashSet<string> _veteranas = new(StringComparer.Ordinal);
	private string _formaDoRelogio = "base";
	private double _multDoRelogio = 1, _segundosNaForma;

	/// <summary>Quantas formas ja pagaram o Seasoned Warrior nesta subida.</summary>
	public int FormasVeteranas => _veteranas.Count;

	// ---- Saiyan's Resolve --------------------------------------------------------------------
	private TipoDeGolpe _tipoDoResolve;
	private int _golpesDoTipo;
	private long _resolveAte;

	/// <summary>Os golpes do mesmo tipo em fila (o Resolve de agora sai deles).</summary>
	public int GolpesDoResolve(long agora) => agora < _resolveAte ? _golpesDoTipo : 0;

	// ---- as ativas com prazo -----------------------------------------------------------------
	public long FerocidadeAte, ImparavelAte, FolegoAte, VontadeAte, SadismoAte, MetamorfoseAte;

	/// <summary>Quando o Overflowing Power / Overflowing Ki foi ligado (-1 = desligado).</summary>
	public long TransbordarDesde = -1, KiTransbordandoDesde = -1;

	/// <summary>
	/// (2026-10-17) O GRITO DO DESABAFO (o segundo aperto de um dos dois transbordar -- `Passivas.SegundosDoGrito`): desde quando (-1 =
	/// calado), os instantes sorteados dos tiros e quantos ja sairam. Quem solta os tiros e o servidor (`GameServer.AndarOGrito`); cair
	/// cala.
	/// </summary>
	public long GritoDesde = -1;
	public double[] GritoInstantes = [];
	public int GritoTiros;

	/// <summary>Primal Bloodlust: golpes sem recuo que faltam, o bonus de dano juntado e ate quando ele vale.</summary>
	public int SedeGolpes;
	public double SedeBonus;
	public long SedeAte;

	/// <summary>Demonic Saiyan: a carga (desde quando, -1 = sem carga) e o estado aceso (desde quando, -1 = apagado).</summary>
	public long DemonicoCargaDesde = -1, DemonicoDesde = -1;
	private long _ultimoCicloDemonico = -1;

	/// <summary>Os dois toggles sem prazo: Max Power e Unrestrained Wrath (este com a hora em que ligou).</summary>
	public bool PoderMaximo;
	public long IraDesde = -1;

	/// <summary>Spirit Disruption: armada, e os acertos limpos e os golpes perdidos desde que armou.</summary>
	public bool DisrupcaoArmada;
	public int DisrupcaoAcertos, DisrupcaoFalhas;

	/// <summary>Sadistic Nature: o bonus juntado.</summary>
	public double SadismoBonus;

	/// <summary>Limitless Power: o dano guardado do ki bloqueado e ate quando ele espera.</summary>
	public double LimitlessGuardado;
	public long LimitlessAte;

	/// <summary>Legendary Resurrection: a animacao invulneravel e o buff de 3 min.</summary>
	public long RessurreicaoAnimacaoAte, RessurreicaoBuffAte;

	/// <summary>Ruthless Mind: a execucao em curso (o alvo e quando ela fecha; 0 = nenhuma).</summary>
	public int ExecucaoAlvo;
	public long ExecucaoFechaEm;

	// ---- Roaring Evolution -------------------------------------------------------------------
	private readonly Dictionary<string, (int Degraus, long Ultimo)> _rugidos = new(StringComparer.Ordinal);

	// ---- Hidden Talent -----------------------------------------------------------------------
	private string _ultimaTecnica = "";

	// ---- Blazing Spirit ----------------------------------------------------------------------
	public long EspiritoReducaoAte, EspiritoLivreAte;
	private readonly HashSet<string> _membrosAbaixo = new(StringComparer.Ordinal);

	// ---- Persistence -------------------------------------------------------------------------
	public double TreinoSeguido;

	// ---- Long-Awaited 100% -------------------------------------------------------------------
	private bool _veioDeBaixo;
	private double _barraAnterior = 1;
	public bool EsperaCumprida;

	// ---- os debuffs RECEBIDOS de skill inimiga -----------------------------------------------
	/// <summary>Spirit Disruption: a fracao do Ki maximo que foi tirada (0 a 0,4) e ate quando.</summary>
	public double KiDesregulado;
	public long KiDesreguladoAte;

	/// <summary>Ruthless Mind: a fracao tirada de todos os stats (0,10 inteira, menos com o rugido) e ate quando.</summary>
	public double MenteCruel;
	public long MenteCruelAte;

	// ---- o que o SERVIDOR lembra entre tiques ---------------------------------------------
	/// <summary>O fator de Ki maximo e o de vigor ja aplicados: quando o teto DESCE, o servidor preserva a razao (o que passa se perde).</summary>
	public double KiAplicado = 1, VigorAplicado = 1;

	/// <summary>A forma do tique anterior: a troca e o que a Revitalizing Metamorphosis olha.</summary>
	public string FormaAnterior = "base";

	/// <summary>Este corpo levou a Ruthless Mind de alguem: -<paramref name="fracao"/> em todos os stats por 2 min.</summary>
	public void AplicarMenteCruel(double fracao, long agora)
	{
		if (fracao <= 0) return;
		MenteCruel = Math.Max(agora < MenteCruelAte ? MenteCruel : 0, fracao);
		MenteCruelAte = agora + (long)(Passivas.SegundosDaMenteCruel * 1000);
	}

	// =====================================================================
	// AS RECARGAS -- moram na ficha (`Fighter.RecargasRaciais`), em ms absolutos
	// =====================================================================
	public static double FaltaDaRecarga(IDictionary<string, long> recargas, string id, long agora) =>
		recargas.TryGetValue(id, out long ate) && ate > agora ? (ate - agora) / 1000.0 : 0;

	public static void Recarregar(IDictionary<string, long> recargas, string id, long agora, double segundos) =>
		recargas[id] = agora + (long)(segundos * 1000);

	// =====================================================================
	// OS EVENTOS
	// =====================================================================
	/// <summary>
	/// ESTE CORPO LEVOU UM GOLPE de outro corpo (o soco que encostou, o tiro que feriu). Saiyan's Resolve (o tipo em fila) e Primal
	/// Bloodlust (um golpe de armadura gasto, +2% de dano).
	/// </summary>
	public void AoLevarGolpe(TipoDeGolpe tipo, long agora)
	{
		if (Entrada.Tem(SkillRacial.Resolucao))
		{
			if (agora < _resolveAte && _tipoDoResolve == tipo) _golpesDoTipo++;
			else { _tipoDoResolve = tipo; _golpesDoTipo = 1; }
			_resolveAte = agora + (long)(Passivas.SegundosDoResolve * 1000);
		}
		if (SedeGolpes > 0)
		{
			SedeGolpes--;
			SedeBonus += Passivas.DanoPorGolpeDaSede;
		}
	}

	/// <summary>
	/// ESTE CORPO DEU UM GOLPE corpo a corpo. <paramref name="limpo"/> = encostou sem guarda (acerto ou critico); falso = aparado,
	/// bloqueado ou no vazio. Sadistic Nature (+5% por golpe sem guarda) e a contagem da Spirit Disruption (o servidor aplica o debuff
	/// no alvo e chama <see cref="AplicarDisrupcao"/> nele). Devolve se a Disrupcao deste golpe vale -- e desarma no 4o.
	/// </summary>
	public bool AoDarGolpe(bool limpo, long agora)
	{
		if (agora < SadismoAte && limpo) SadismoBonus = Math.Min(SadismoBonus + Passivas.SadismoPorGolpe, Passivas.TetoDoSadismo);
		if (!DisrupcaoArmada) return false;
		if (limpo) DisrupcaoAcertos++;
		else DisrupcaoFalhas++;
		if (DisrupcaoAcertos >= Passivas.GolpesDaDisrupcao || DisrupcaoFalhas >= Passivas.GolpesDaDisrupcao) DisrupcaoArmada = false;
		return limpo;
	}

	/// <summary>A Spirit Disruption ACERTOU este corpo: -10% do Ki maximo (vezes o que sobra do rugido), ate 40%, por 30 s.</summary>
	public void AplicarDisrupcao(double fracao, long agora)
	{
		if (fracao <= 0) return;
		KiDesregulado = Math.Min((agora < KiDesreguladoAte ? KiDesregulado : 0) + fracao, Passivas.TetoDaDisrupcao);
		KiDesreguladoAte = agora + (long)(Passivas.SegundosDaDisrupcao * 1000);
	}

	/// <summary>
	/// ROARING EVOLUTION: a skill <paramref name="skill"/> do inimigo afetou este corpo. Devolve o que sobra do efeito DELA agora (1 =
	/// inteira) e ganha um degrau contra ela (no maximo um a cada 10 s). Sem o kit Primal, sempre 1.
	/// </summary>
	public double RugirContra(string skill, long agora)
	{
		if ((Entrada.Kit & KitRacial.Primal) == 0 || skill.Length == 0) return 1;
		(int degraus, long ultimo) = _rugidos.GetValueOrDefault(skill, (0, long.MinValue / 2));
		double sobra = Passivas.SobraDoRugido(degraus);
		if (agora - ultimo >= Passivas.SegundosEntreRugidos * 1000 && degraus < 10) _rugidos[skill] = (degraus + 1, agora);
		return sobra;
	}

	/// <summary>Os degraus do rugido contra uma skill (pra bancada e pro HUD).</summary>
	public int DegrausContra(string skill) => _rugidos.GetValueOrDefault(skill).Degraus;

	/// <summary>
	/// HIDDEN TALENT: o fator desta tecnica de ki agora. Dominada = +10%, e a mesma dominada duas vezes seguidas = 2x na segunda (e a
	/// fila recomeca). Sem a skill (o kit meio-Saiyajin, ou a escolha do bio), 1. Lembra a tecnica de qualquer jeito -- a fila e de quem usa.
	/// </summary>
	public double FatorDoTalento(string tecnica, bool dominada)
	{
		bool repetida = tecnica.Length > 0 && tecnica == _ultimaTecnica;
		_ultimaTecnica = repetida ? "" : tecnica;
		if (!Entrada.Tem(SkillRacial.Talento) || !dominada) return 1;
		return Passivas.TalentoDominado * (repetida ? Passivas.TalentoRepetido : 1);
	}

	/// <summary>
	/// SHATTERING THE LIMIT: o fator de um tiro CARREGADO que vai sair agora, pelo custo dele contra o Ki ATUAL. 2 com o kit Primal,
	/// numa forma Saiyajin, quando o tiro custa 10% ou mais do Ki de agora; 1 no resto.
	/// </summary>
	public double FatorDoLimite(double custoDoTiro, double kiAtual) =>
		(Entrada.Kit & KitRacial.Primal) != 0 && Entrada.FormaSaiyajin && Entrada.Forma != "base"
		&& custoDoTiro > 0 && custoDoTiro >= kiAtual * Passivas.CustoDoLimite
			? Passivas.MultDoLimite
			: 1;

	/// <summary>
	/// LIMITLESS POWER: este corpo BLOQUEOU um tiro de ki que daria <paramref name="danoDoTiro"/>. Guarda a fracao pelo degrau do SSJ4,
	/// por 1 min, se a recarga deixar. Devolve o guardado (0 = nada).
	/// </summary>
	public double AoBloquearKi(double danoDoTiro, IDictionary<string, long> recargas, long agora)
	{
		if ((Entrada.Kit & KitRacial.Primal) == 0 || danoDoTiro <= 0) return 0;
		if (FaltaDaRecarga(recargas, Passivas.Limitless, agora) > 0) return 0;
		LimitlessGuardado = danoDoTiro * Passivas.FracaoLimitless(Entrada.DegrauSsj4);
		LimitlessAte = agora + (long)(Passivas.SegundosDoLimitless * 1000);
		Recarregar(recargas, Passivas.Limitless, agora, Passivas.RecargaDoLimitless);
		return LimitlessGuardado;
	}

	/// <summary>O proximo tiro de ki deste corpo leva o guardado do Limitless (e ele se gasta). Devolve o dano a SOMAR.</summary>
	public double GastarLimitless(long agora)
	{
		double v = agora < LimitlessAte ? LimitlessGuardado : 0;
		LimitlessGuardado = 0;
		LimitlessAte = 0;
		return v;
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>
	/// UM TIQUE: os relogios andam, as janelas vencem e o <see cref="Efeito"/> e REESCRITO do zero pela <see cref="Entrada"/>.
	/// </summary>
	public void Calcular(long agora, double dt)
	{
		EntradaRacial e = Entrada;
		EfeitoRacial o = Efeito;
		o.Zerar();
		KitRacial k = e.Kit;
		bool combate = e.EmCombate;

		// ---- as janelas que o corpo caido perde ----------------------------------------------
		if (e.Caido)
		{
			TransbordarDesde = KiTransbordandoDesde = DemonicoCargaDesde = -1;
			IraDesde = -1;
			SedeGolpes = 0;
			ExecucaoAlvo = 0;
			GritoDesde = -1;   // (2026-10-17) o corpo que cai para de gritar: os tiros que faltavam nao saem
		}
		if (!combate)
		{
			_rugidos.Clear();          // o rugido vale "pelo resto da tag de combate"
			DemonicoDesde = -1;        // o Demonic "fica ate o fim do combate"
			DemonicoCargaDesde = -1;
		}
		if (DemonicoDesde >= 0 && !e.FormaLendaria) DemonicoDesde = -1;   // saiu das formas Lendarias: apaga (escolha declarada)

		// ---- sem efeitos negativos ----------------------------------------------------------
		// O BLAZING SPIRIT ANDA ANTES DE A PERGUNTA SER FEITA: e ele que acende o `EspiritoLivreAte`, e andado la embaixo (no bloco do
		// meio-Saiyajin) o membro que acabou de cair so tirava os efeitos negativos no tique SEGUINTE. A `--passivasteste` pegou.
		if (e.Tem(SkillRacial.Espirito)) AndarOEspirito(e, agora);
		o.SemEfeitosNegativos = agora < FerocidadeAte || agora < VontadeAte || agora < EspiritoLivreAte;

		// ---- os debuffs recebidos (qualquer corpo) -------------------------------------------
		if (!o.SemEfeitosNegativos)
		{
			if (agora < KiDesreguladoAte) o.MaxKi *= 1 - KiDesregulado;
			if (agora < MenteCruelAte) o.TodosOsStats(1 - MenteCruel);
		}

		// ---- SAIYAJIN ------------------------------------------------------------------------
		// (2026-10-17) UMA PERGUNTA POR SKILL, e nao uma pelo kit: o bio-androide escolhe as dele do sangue (`SkillRacial`). Quem nao e bio tem o
		// kit inteiro nos bits, entao pra ele as cinco valem como antes.
		AndarOVeterano(e, dt);
		if (combate && e.Tem(SkillRacial.GuerreiroVeterano)) o.Bp *= 1 + Passivas.BonusVeterano(_veteranas.Count);

		if (e.Vida < Passivas.VidaDoGuerreiro && e.Tem(SkillRacial.RacaGuerreira))
		{
			o.Physoff *= Passivas.AtaqueDoGuerreiro;
			o.Physdef *= Passivas.DefesaDoGuerreiro;
		}

		if (agora < _resolveAte && e.Tem(SkillRacial.Resolucao)) o.SomarReducao(Passivas.ReducaoDoResolve(_golpesDoTipo));

		if (combate && !e.Relaxado && e.Tem(SkillRacial.Orgulho))
		{
			o.Orgulho = Passivas.DegrauDoOrgulho(e.Barra);
			(double regen, double fisico, double kiSkill) = Passivas.Orgulho(o.Orgulho);
			o.RegenKiPassiva *= regen; o.RegenKiAtiva *= regen; o.RegenVigor *= regen;
			o.Physoff *= fisico; o.Physdef *= fisico;
			o.Kiskill *= kiSkill;
		}

		if (e.Tem(SkillRacial.FalsaVantagem) && !e.FormaDivina
			&& (e.Ki < Passivas.RecursoDaFalsaVantagem || e.Vigor < Passivas.RecursoDaFalsaVantagem || e.Vida < Passivas.RecursoDaFalsaVantagem))
		{
			o.FalsaVantagem = Passivas.SomaDaFalsaVantagem(e.MaiorMortalDominada);
			o.SomaNaForma += o.FalsaVantagem;
			o.Dreno *= Passivas.DrenoDaFalsaVantagem;
		}

		// ---- PRIMAL --------------------------------------------------------------------------
		if ((k & KitRacial.Primal) != 0)
		{
			if (e.TemRabo)
			{
				double x = Passivas.EvolucaoPrimal(e.DegrauSsj4);
				o.DanoDado *= 1 + x;
				o.CustoKi *= 1 - x;
				o.Dreno *= 1 - x;
			}
			if (agora < FerocidadeAte)
			{
				o.Dreno = 0;          // "no passive drain"
				o.BarraCheia = true;  // "puts you to 100% release"
			}
			if (agora >= LimitlessAte) LimitlessGuardado = 0;
		}

		// ---- LEGENDARY ------------------------------------------------------------------------
		if ((k & KitRacial.Lendario) != 0)
		{
			if (TransbordarDesde >= 0)
			{
				double t = (agora - TransbordarDesde) / 1000.0;
				o.Bp *= 1 + Passivas.BonusDoTransbordar(t);
				o.TroncoPorSegundo += Passivas.TroncoPorSegundoDoTransbordar(t, Passivas.SegundosPraDesabafar);
			}
			if (agora < ImparavelAte)
			{
				o.Inabalavel = true;
				o.SomarReducao(Passivas.ReducaoImparavel);
			}
			if (combate && e.Forma != "base" && !e.FormaDivina)
			{
				o.Escalada = Passivas.BonusDaEscalada(e.Tronco, e.Vigor, e.Ki);
				o.Bp *= 1 + o.Escalada;
			}
		}
		else TransbordarDesde = -1;

		// ---- LEGENDARY PRIMAL ------------------------------------------------------------------
		if ((k & KitRacial.LendarioPrimal) != 0)
		{
			if (KiTransbordandoDesde >= 0)
			{
				double t = (agora - KiTransbordandoDesde) / 1000.0;
				o.MaxKi *= 1 + Passivas.BonusDoKiTransbordando(t);
				o.TroncoPorSegundo += Passivas.TroncoPorSegundoDoTransbordar(t, Passivas.SegundosPraDesabafarKi);
			}
			if (SedeGolpes > 0)
			{
				o.Inabalavel = true;
				o.DanoRecebido *= Passivas.DanoRecebidoNaSede;
			}
			if (agora < SedeAte) o.DanoDado *= 1 + SedeBonus;
			else { SedeBonus = 0; SedeGolpes = 0; }

			if (agora < RessurreicaoAnimacaoAte) o.Invulneravel = true;
			if (agora < RessurreicaoBuffAte) o.TodosOsStats(Passivas.StatsDaRessurreicao);

			AndarODemonico(e, o, agora);
		}
		else { KiTransbordandoDesde = -1; DemonicoDesde = DemonicoCargaDesde = -1; }

		// ---- MEIO-SAIYAJIN -----------------------------------------------------------------------
		if (e.Tem(SkillRacial.Prodigio) && e.Forma != "base" && e.FormaDominada)
		{
			o.SomaNaForma += e.FormaDivina ? Passivas.SomaDoProdigioDivino : Passivas.SomaDoProdigioMortal;
			o.RegenKiAtiva *= Passivas.RegenDoProdigio;
		}
		if (e.Tem(SkillRacial.Potencial)) o.TetoDaCarga *= Passivas.TetoDoPotencial;
		if (agora < EspiritoReducaoAte && e.Tem(SkillRacial.Espirito)) o.SomarReducao(Passivas.ReducaoDoEspirito);

		// ---- HUMANO -----------------------------------------------------------------------------
		if (e.Tem(SkillRacial.Persistencia))
		{
			TreinoSeguido = e.Treinando ? TreinoSeguido + dt : 0;
			o.GanhoDeTreino = Passivas.FatorDaPersistencia(TreinoSeguido);
		}
		else TreinoSeguido = 0;
		if (agora < FolegoAte && e.Tem(SkillRacial.SegundoFolego))
		{
			o.KiPorSegundo += Passivas.FracaoDoFolego / Passivas.SegundosDoFolego;
			o.VigorPorSegundo += Passivas.FracaoDoFolego / Passivas.SegundosDoFolego;
		}
		if (agora < VontadeAte && e.Tem(SkillRacial.ForcaDeVontade)) o.NegaMorte = true;
		if (!e.Tem(SkillRacial.PoderMaximo)) PoderMaximo = false;
		if (PoderMaximo)
		{
			o.Physoff *= Passivas.MultDoPoderMaximo;
			o.Physdef *= Passivas.MultDoPoderMaximo;
			o.Speed *= Passivas.MultDoPoderMaximo;
			o.KiDrenadoPorSegundo += Passivas.DrenoDoPoderMaximo;
		}

		// ---- NAMEKUSEIJIN -------------------------------------------------------------------------
		if (e.Tem(SkillRacial.Resiliencia)) o.FatorDoNocaute *= Passivas.NocauteResiliente;
		if (e.Tem(SkillRacial.Vitalidade))
		{
			o.FatorDosMembros *= Passivas.MembrosDoNamek;
			if (MembrosInteiros(e.Corpo))
			{
				o.RegenKiPassiva *= Passivas.RegenDoNamekInteiro;
				o.RegenVigor *= Passivas.RegenDoNamekInteiro;
			}
		}
		if (!e.Tem(SkillRacial.Disrupcao)) DisrupcaoArmada = false;

		// ---- FROST DEMON --------------------------------------------------------------------------
		if (e.Tem(SkillRacial.Controle))
		{
			o.CargaDeTecnica *= Passivas.CargaDoControle;
			o.DanoDeKi *= Passivas.KiDoControle;
		}
		if (e.Tem(SkillRacial.Tenacidade))
		{
			o.FatorDoNocaute *= Passivas.NocauteResiliente;
			o.Tenaz = true;
		}
		if (!e.Tem(SkillRacial.Ira)) IraDesde = -1;
		if (IraDesde >= 0)
		{
			o.Bp *= Passivas.BpDaIra;
			o.MaxKi *= Passivas.KiDaIra;
		}
		if (agora < MetamorfoseAte && e.Tem(SkillRacial.Metamorfose)) o.RegenKiAtiva *= Passivas.KiDaMetamorfose;

		// ---- MUTANTE --------------------------------------------------------------------------------
		if ((k & KitRacial.Mutante) != 0)
		{
			AndarAEspera(e);
			if (EsperaCumprida)
			{
				o.DanoDado *= Passivas.BonusDaEspera;
				o.Physdef *= Passivas.BonusDaEspera;
				o.Kidef *= Passivas.BonusDaEspera;
				o.Speed *= Passivas.BonusDaEspera;
				o.RegenKiAtiva *= Passivas.RegenDaEspera;
				o.RegenKiPassiva *= Passivas.RegenDaEspera;
			}
			if (agora < SadismoAte) o.DanoDado *= 1 + SadismoBonus;
			else SadismoBonus = 0;
		}
		else { EsperaCumprida = false; SadismoBonus = 0; }
	}

	/// <summary>
	/// SEASONED WARRIOR: o relogio da forma. Trocou de forma? Se a nova e MAIS FRACA (menor multiplicador -- a "forma mais baixa"; a
	/// base tambem), as veteranas zeram. Dentro de uma forma de maestria ainda nao paga, so com a tag de combate, o relogio anda; fechou
	/// o degrau (10 s, 20 s, 30 s...), a forma paga.
	/// </summary>
	private void AndarOVeterano(EntradaRacial e, double dt)
	{
		if (e.Forma != _formaDoRelogio)
		{
			if (e.Forma == "base" || e.MultDaForma < _multDoRelogio - 1e-9) _veteranas.Clear();
			_formaDoRelogio = e.Forma;
			_segundosNaForma = 0;
		}
		_multDoRelogio = e.MultDaForma;
		if (!e.Tem(SkillRacial.GuerreiroVeterano) || !e.EmCombate || !e.FormaTreinavel || e.Forma == "base"
			|| _veteranas.Contains(e.Forma))
			return;
		_segundosNaForma += dt;
		if (_segundosNaForma >= Passivas.SegundosPraVeterana(_veteranas.Count))
		{
			_veteranas.Add(e.Forma);
			_segundosNaForma = 0;
		}
	}

	/// <summary>
	/// DEMONIC SAIYAN: a carga parada (andar, sair da tag ou da forma desfaz e zera) e, acesa, o ciclo de 15 s com a janela de 10 s
	/// em que Ki e vigor estao em 300% -- o tanque triplica e enche no comeco de cada ciclo. E, aceso, o Mindless Beast e o Primitive
	/// Rage. A recarga de 20 min comeca quando a carga FECHA (o servidor a escreve ao ler <see cref="DemonicoCarregou"/>).
	/// </summary>
	private void AndarODemonico(EntradaRacial e, EfeitoRacial o, long agora)
	{
		DemonicoCarregou = false;
		if (DemonicoCargaDesde >= 0)
		{
			if (!e.Parado || !e.EmCombate || !e.FormaLendaria) DemonicoCargaDesde = -1;
			else if (agora - DemonicoCargaDesde >= Passivas.SegundosDaCargaDemonica * 1000)
			{
				DemonicoCargaDesde = -1;
				DemonicoDesde = agora;
				_ultimoCicloDemonico = -1;
				DemonicoCarregou = true;
			}
		}
		if (DemonicoDesde < 0) return;

		o.CustoKi *= Passivas.ConsumoDemonico;
		o.Dreno *= Passivas.ConsumoDemonico;
		o.FatorDosMembros *= Passivas.MembrosDemonicos;

		long ciclo = (agora - DemonicoDesde) / (long)(Passivas.CicloDemonico * 1000);
		double dentro = (agora - DemonicoDesde) / 1000.0 - ciclo * Passivas.CicloDemonico;
		if (dentro < Passivas.JanelaDemonica)
		{
			o.MaxKi *= Passivas.ReservaDemonica;
			o.MaxVigor *= Passivas.ReservaDemonica;
			if (ciclo != _ultimoCicloDemonico) { o.EncherReservas = true; _ultimoCicloDemonico = ciclo; }
		}
	}

	/// <summary>A carga do Demonic acabou de fechar neste tique (o servidor escreve a recarga e avisa).</summary>
	public bool DemonicoCarregou { get; private set; }

	/// <summary>(2026-10-17) A linha do grito do desabafo no painel das raciais -- a dos dois transbordar.</summary>
	private string LinhaDoGrito(long agora) =>
		$"DESABAFO · gritando, o ki explode pra todo lado · {Math.Ceiling(Passivas.SegundosDoGrito - (agora - GritoDesde) / 1000.0):0} s";

	/// <summary>BLAZING SPIRIT: um membro que CAI abaixo de 50% (de cima pra baixo) acende os dois relogios. Ele so dispara de novo depois de voltar acima.</summary>
	private void AndarOEspirito(EntradaRacial e, long agora)
	{
		if (e.Corpo == null) return;
		foreach (BodyPart p in e.Corpo.Partes)
		{
			if (p.Aninhado) continue;
			double vida = p.Decepado ? 0 : p.Fracao;
			if (vida < Passivas.LinhaDoEspirito)
			{
				if (_membrosAbaixo.Add(p.Nome))
				{
					EspiritoReducaoAte = agora + (long)(Passivas.SegundosDaReducaoDoEspirito * 1000);
					EspiritoLivreAte = agora + (long)(Passivas.SegundosSemNegativosDoEspirito * 1000);
				}
			}
			else _membrosAbaixo.Remove(p.Nome);
		}
	}

	/// <summary>LONG-AWAITED 100%: desceu a 50% ou menos, e SUBIU DEVAGAR ate 100% (a carga, e nao o "ficar pronto" de uma vez).</summary>
	private void AndarAEspera(EntradaRacial e)
	{
		if (e.Relaxado || e.Barra < 1) EsperaCumprida = false;
		if (e.Barra <= Passivas.BarraDaEspera) _veioDeBaixo = true;
		if (e.Barra - _barraAnterior > 0.25) _veioDeBaixo = false;   // pulou de uma vez: nao foi devagar
		if (_veioDeBaixo && e.Barra >= 1 && !e.Relaxado)
		{
			EsperaCumprida = true;
			_veioDeBaixo = false;
		}
		_barraAnterior = e.Barra;
	}

	// =====================================================================
	// O HUD
	// =====================================================================
	/// <summary>
	/// AS LINHAS DO HUD -- o que esta valendo agora, uma por linha (vazio = nada). Vai na ficha (`SheetState.Raciais`) e cai nos avisos
	/// do corpo. Os nomes das passivas ficam em ingles, como os das tecnicas do jogo; o resto, em portugues.
	/// </summary>
	public string LinhasDoHud(long agora)
	{
		var sb = new System.Text.StringBuilder();
		void L(string s)
		{
			if (sb.Length > 0) sb.Append('\n');
			sb.Append(s);
		}
		static string Seg(long ate, long agora) => $"{Math.Ceiling((ate - agora) / 1000.0):0} s";
		EntradaRacial e = Entrada;
		EfeitoRacial o = Efeito;
		KitRacial k = e.Kit;

		if (agora < KiDesreguladoAte && KiDesregulado > 0 && !o.SemEfeitosNegativos)
			L($"KI DESREGULADO · Ki máximo -{KiDesregulado * 100:0}% · {Seg(KiDesreguladoAte, agora)}");
		if (agora < MenteCruelAte && MenteCruel > 0 && !o.SemEfeitosNegativos)
			L($"RUTHLESS MIND · todos os stats -{MenteCruel * 100:0}% · {Seg(MenteCruelAte, agora)}");

		// (2026-10-17) as linhas dos cinco kits de raca, uma por SKILL -- o bio so ve as que escolheu
		if (e.EmCombate && _veteranas.Count > 0 && e.Tem(SkillRacial.GuerreiroVeterano))
			L($"SEASONED WARRIOR · BP +{Passivas.BonusVeterano(_veteranas.Count) * 100:0}%");
		if (agora < _resolveAte && _golpesDoTipo >= 2 && e.Tem(SkillRacial.Resolucao))
			L($"SAIYAN'S RESOLVE · -{Passivas.ReducaoDoResolve(_golpesDoTipo):0}% de dano de {NomeDoTipo(_tipoDoResolve)}");
		if (e.Vida < Passivas.VidaDoGuerreiro && e.Tem(SkillRacial.RacaGuerreira)) L("WARRIOR RACE · ataque +20%, defesa +30%");
		if (o.Orgulho > 0) L($"PRIDEFUL · o orgulho não deixa se segurar (degrau {o.Orgulho})");
		if (o.FalsaVantagem > 0) L($"FALSE ADVANTAGE · +{o.FalsaVantagem:0.#}x na forma · dreno pela metade");
		if ((k & KitRacial.Primal) != 0)
		{
			if (agora < FerocidadeAte) L($"UNMATCHED FEROCITY · {Seg(FerocidadeAte, agora)}");
			if (agora < LimitlessAte && LimitlessGuardado > 0) L($"LIMITLESS POWER · ki guardado pro próximo ataque · {Seg(LimitlessAte, agora)}");
			if (_rugidos.Count > 0) L($"ROARING EVOLUTION · {_rugidos.Count} skill(s) perdendo força contra você");
		}
		if ((k & KitRacial.Lendario) != 0)
		{
			if (TransbordarDesde >= 0)
			{
				double t = (agora - TransbordarDesde) / 1000.0;
				L($"OVERFLOWING POWER · BP +{Passivas.BonusDoTransbordar(t) * 100:0}%"
				  + (t >= Passivas.SegundosPraDesabafar ? " · aperte de novo pra DESABAFAR" : $" · desabafo em {Math.Ceiling(Passivas.SegundosPraDesabafar - t):0} s"));
			}
			if (GritoDesde >= 0) L(LinhaDoGrito(agora));
			if (agora < ImparavelAte) L($"UNSTOPPABLE FORCE · sem recuo, -10% de dano · {Seg(ImparavelAte, agora)}");
			if (o.Escalada > 0.005) L($"ESCALATING POWER · forma +{o.Escalada * 100:0}%");
		}
		if ((k & KitRacial.LendarioPrimal) != 0)
		{
			if (KiTransbordandoDesde >= 0)
			{
				double t = (agora - KiTransbordandoDesde) / 1000.0;
				L($"OVERFLOWING KI · Ki máximo +{Passivas.BonusDoKiTransbordando(t) * 100:0}%"
				  + (t >= Passivas.SegundosPraDesabafarKi ? " · aperte de novo pra DESABAFAR" : $" · desabafo em {Math.Ceiling(Passivas.SegundosPraDesabafarKi - t):0} s"));
			}
			if (GritoDesde >= 0) L(LinhaDoGrito(agora));
			if (SedeGolpes > 0 || agora < SedeAte)
				L($"PRIMAL BLOODLUST · {SedeGolpes} golpe(s) sem recuo · dano +{SedeBonus * 100:0}%");
			if (agora < RessurreicaoAnimacaoAte) L("LEGENDARY RESURRECTION · invulnerável");
			else if (agora < RessurreicaoBuffAte) L($"LEGENDARY RESURRECTION · stats +10% · cair agora é MORRER · {Seg(RessurreicaoBuffAte, agora)}");
			if (DemonicoCargaDesde >= 0)
				L($"DEMONIC SAIYAN · carregando, fique PARADO · {Math.Ceiling(Passivas.SegundosDaCargaDemonica - (agora - DemonicoCargaDesde) / 1000.0):0} s");
			else if (DemonicoDesde >= 0) L("DEMONIC SAIYAN · reservas em 300% a cada 15 s");
		}
		if (agora < EspiritoReducaoAte && e.Tem(SkillRacial.Espirito))
			L("BLAZING SPIRIT · -20% de dano" + (agora < EspiritoLivreAte ? " · sem efeitos negativos" : "") + $" · {Seg(EspiritoReducaoAte, agora)}");
		if (TreinoSeguido >= 60 && e.Tem(SkillRacial.Persistencia)) L($"PERSISTENCE · treino {o.GanhoDeTreino:0.0}x");
		if (agora < FolegoAte && e.Tem(SkillRacial.SegundoFolego)) L($"SECOND WIND · {Seg(FolegoAte, agora)}");
		if (agora < VontadeAte && e.Tem(SkillRacial.ForcaDeVontade)) L($"SHEER WILLPOWER · nada derruba você · {Seg(VontadeAte, agora)}");
		if (PoderMaximo && e.Tem(SkillRacial.PoderMaximo)) L("MAX POWER · ataque, defesa e velocidade 1,5x · drena Ki");
		if (DisrupcaoArmada && e.Tem(SkillRacial.Disrupcao))
			L($"SPIRIT DISRUPTION · {DisrupcaoAcertos}/{Passivas.GolpesDaDisrupcao} acertos · {DisrupcaoFalhas}/{Passivas.GolpesDaDisrupcao} perdidos");
		if (IraDesde >= 0 && e.Tem(SkillRacial.Ira)) L("UNRESTRAINED WRATH · BP +20%, Ki máximo -20%");
		if (agora < MetamorfoseAte && e.Tem(SkillRacial.Metamorfose))
			L($"REVITALIZING METAMORPHOSIS · carga de Ki +40% · {Seg(MetamorfoseAte, agora)}");
		if ((k & KitRacial.Mutante) != 0)
		{
			if (EsperaCumprida) L("LONG-AWAITED 100% · dano, defesa e velocidade +10%");
			if (agora < SadismoAte) L($"SADISTIC NATURE · dano +{SadismoBonus * 100:0}% · {Seg(SadismoAte, agora)}");
			if (ExecucaoAlvo != 0) L("RUTHLESS MIND · executando...");
		}
		return sb.ToString();
	}

	private static string NomeDoTipo(TipoDeGolpe t) => t switch
	{
		TipoDeGolpe.Corte => "corte",
		TipoDeGolpe.Ki => "ki",
		_ => "contusão",
	};

	/// <summary>Todos os membros de fora estao INTEIROS -- nenhum decepado, nenhum quebrado (`BodyPart.Quebrado`). O Warrior's Vitality.</summary>
	public static bool MembrosInteiros(Body? corpo)
	{
		if (corpo == null) return false;
		foreach (BodyPart p in corpo.Partes)
			if (!p.Aninhado && (p.Decepado || p.Quebrado)) return false;
		return true;
	}
}
