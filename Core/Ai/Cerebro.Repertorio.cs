using Jandirus.Core.World;

namespace Jandirus.Core.Ai;

/// <summary>
/// O REPERTORIO DE LUTA NAS DECISOES DO CEREBRO (dono, 2026-10-13: *"IAs q controlam o personagem (oozaru, formas de
/// lssj descontroladas etc) deveriam usar todas as skills disponiveis do jogador descontrolado, se a IA n sabe usar,
/// melhore ela pra deixa-la mais inteligente"*).
///
/// ============================ O MESMO DESENHO DO KIT, E NAO UM SEGUNDO CEREBRO ============================
/// Nada daqui e um plano novo. Sao perguntas penduradas no gesto que o plano ja montou, e o que sai delas e UM
/// pulso pelo mesmo cano de toda tecnica da IA -- o `Comando.Habilidade`, que o atuador entrega a funcao de
/// habilidade do jogador. Quem confere se o corpo sabe, se tem Ki, se a recarga venceu e o verb; o que este arquivo
/// escreve e QUANDO o cerebro pede cada tipo (o <see cref="Uso"/> da linha):
///
///   O SOCO VIRA GOLPE     no tique em que a receita ia socar, de vez em quando sai uma tecnica de perto no lugar
///                         (<see cref="ChanceDeGolpeEspecial"/>) -- sorteada entre as que servem, e nao a mais cara.
///   COM ALGUEM NOS BRACOS o agarrao deixou a presa na mao: para, e da outro golpe de agarrao. Sem nenhum pronto,
///                         aperta o pescoco; vencido o prazo, afrouxa e da o passo que arremessa.
///   A INVESTIDA           pressionando, com o alvo na janela da corrida, a linha livre e o mesmo andar.
///   O SOCORRO             a vida abaixo de <see cref="VidaQuePedeSocorro"/>, ou o tanque no fim.
///   REBATER               ha ki alheio vindo pra cima dele e ele sabe rebater.
///   OS REFORCOS           ligam um a um quando a luta comeca; desligam quando o Ki fica baixo (pra ele poder
///                         carregar) e quando a luta acaba.
///
/// E o RODIZIO, que vale tambem pro arsenal de longe: entre as tecnicas que servem, sai a que esta HA MAIS TEMPO
/// SEM SER USADA (<see cref="UltimoUso"/>). E o que faz um corpo com trinta tecnicas usar as trinta -- a nota do
/// tiro, sozinha, elegia a mais cara a luta inteira.
/// ==========================================================================================================
/// </summary>
public sealed partial class Cerebro
{
	/// <summary>
	/// O DESCANSO MINIMO de cada uso, em segundos, pra linha que nao declara o dela (<see cref="Tiro.Descanso"/>). O
	/// tiro nao tem: quem o espaca e a pausa do tiro, e quem o varia e o rodizio.
	/// </summary>
	public const double DescansoDoGolpe = 5, DescansoDaInvestida = 8, DescansoDoReforco = 3, DescansoDoSocorro = 10;

	/// <summary>
	/// O REFORCO QUE O JOGO RECUSOU: pedido, e nao ficou de pe (o Elemental de quem nunca escolheu o elemento, a
	/// Expansao com a lamina de Ki na mao). Sem um descanso proprio ele voltava a ser pedido a cada
	/// <see cref="DescansoDoReforco"/> -- bater na mesma porta a luta inteira.
	/// </summary>
	public const double DescansoDoReforcoRecusado = 45;

	/// <summary>
	/// Entre dois PUNHOS do repertorio (golpe, investida, agarrao). Maior que a recarga comum dos punhos nomeados
	/// (`basicCD += 15`, um segundo e meio): pedir o segundo antes dela e ouvir "ainda se recompoe" e gastar o descanso
	/// da tecnica a toa.
	/// </summary>
	public const double PausaEntreTecnicas = 1.6;

	/// <summary>
	/// Entre dois pulsos de APOIO (reforco, socorro) -- que nao passam pela recarga dos punhos, e por isso tem relogio
	/// proprio. Com um relogio so, cada reforco reacendido (as miragens, a copia, os recusados) gastava a vez de um
	/// golpe: medido na `--v021teste`, a fera dava um golpe a cada 6,4 s de luta colada com as maos livres.
	/// </summary>
	public const double PausaEntreApoios = 0.6;

	/// <summary>De quanto em quanto ele olha a tabela inteira (socorro, investida, reforco). O soco que vira golpe e o tiro vindo nao esperam.</summary>
	public const double OlhadaNoRepertorio = 0.25;

	/// <summary>
	/// A CHANCE DE UM SOCO SAIR COMO TECNICA. Tempero, como a <see cref="ChanceDeInvestir"/>: um terco deixa o soco
	/// continuar sendo a base da luta e faz o repertorio aparecer varias vezes por minuto.
	/// </summary>
	public double ChanceDeGolpeEspecial = 0.35;

	/// <summary>A chance, por olhada, de pedir a investida que cabe. Sem sorteio ela sairia sempre no primeiro instante da janela.</summary>
	public const double ChanceDeInvestidaPorOlhada = 0.30;

	public const double VidaQuePedeSocorro = 0.55;

	/// <summary>Acima disto a tecnica que cobra do proprio corpo (o Kikoho) ainda e uma boa troca.</summary>
	public const double VidaQueSobra = 0.5;

	/// <summary>O reforco nao sai no primeiro quadro da luta, nem fica de pe depois que ela acabou.</summary>
	public const double SegundosDeLutaAteOReforco = 0.8, SegundosSemAlvoAteDesligar = 6;

	/// <summary>Cortados por Ki baixo, os reforcos so voltam com o tanque acima disto.</summary>
	public const double KiQueReligaOReforco = 0.6;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia U): o repertorio de luta nao e consultado -- a IA da v0.2.0, que so socava.</summary>
	public static bool SemRepertorioDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia U): o cerebro esquece o que ja usou -- a escolha volta a eleger sempre a mesma.</summary>
	public static bool SemRodizioDeTeste;

	/// <summary>
	/// ESTA LUTA E UM TREINO AMISTOSO -- o spar da vida diaria dos habitantes (`Rotina`, o `Ordem.Lutar`). Quem
	/// escreve e o servidor, a cada tique de rotina (`GameServer.TicarARotina`), e a presa de verdade o apaga
	/// (`GameServer.TicarUmCorpo`).
	///
	/// NO TREINO O REPERTORIO DE LUTA FICA CALADO: o spar dos habitantes acaba quando a vida de um deles chega a 60%,
	/// e e isso que o faz nunca derrubar ninguem. Um punho nomeado nao respeita esse limiar -- medido na
	/// `--rotinateste`: um golpe no abdomen nocauteou o parceiro com a vida em 84%, e o habitante ficou um minuto
	/// caido no meio da praca. O soco (e o ki de quem tem) continuam, como na v0.2.0.
	/// </summary>
	public bool LutaDeTreino;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia U): o cerebro ignora que a luta e um treino -- o spar com punho nomeado.</summary>
	public static bool TreinoComRepertorioDeTeste;

	/// <summary>O repertorio de luta esta fora desta luta? (O defeito que o desliga inteiro, ou o treino amistoso.)</summary>
	private bool RepertorioCalado => SemRepertorioDeTeste || (LutaDeTreino && !TreinoComRepertorioDeTeste);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`, familia U): vencido o prazo com a presa nos bracos a receita devolve o
	/// corpo ao plano, como na primeira versao -- e o plano, colado no alvo, nao da o passo que arremessa.
	/// </summary>
	public static bool PresaFicaNosBracosDeTeste;

	/// <summary>
	/// Entre dois pulsos no pescoco de quem esta nos bracos (e entre duas tentativas do passo que arremessa). Maior
	/// que a recarga do aperto no servidor (meio segundo, o `choke_cooldown`): o pulso dado antes dela e engolido, e
	/// o cerebro ficaria achando que alternou.
	/// </summary>
	public const double PausaDoPescoco = 0.6;

	private Dictionary<string, double>? _usadaEm;   // id da tecnica -> o instante (relogio do cerebro) do ultimo uso
	private double _pausaDoPunho, _pausaDoApoio;     // os dois relogios do repertorio -- ver `PausaEntreTecnicas` e `PausaEntreApoios`
	private double _olhadaEm;
	private double _lutandoHa, _semAlvoHa;
	private bool _reforcosCortados;
	private byte _grupoRecemLigado;                  // o grupo do reforco que acabou de ligar...
	private double _grupoRecemLigadoAte;             // ...e ate quando os irmaos dele esperam (ver `ReforcoPraLigar`)
	private HashSet<string>? _desliguei;             // os reforcos que EU apaguei: esses voltam logo; o resto dos apagados foi recusa
	private double _pescocoEm;                       // quando o proximo pulso no pescoco pode sair (relogio do cerebro)

	/// <summary>
	/// QUANDO ESTA TECNICA FOI USADA PELA ULTIMA VEZ, no relogio do cerebro. Nunca usada = menos infinito -- e por
	/// isso a primeira volta do rodizio sai na ordem de sempre (a melhor nota primeiro), e so entao ele gira.
	/// </summary>
	private double UltimoUso(string id) =>
		SemRodizioDeTeste || _usadaEm == null || !_usadaEm.TryGetValue(id, out double quando) ? double.NegativeInfinity : quando;

	/// <summary>Ainda no descanso minimo? O da linha, ou o padrao do uso quando ela nao declara (zero = sem descanso).</summary>
	private bool Descansando(in Tiro t, double padrao) =>
		_relogio - UltimoUso(t.Id) < (t.Descanso > 0 ? t.Descanso : padrao);

	private void Usei(string id) => (_usadaEm ??= new Dictionary<string, double>(StringComparer.Ordinal))[id] = _relogio;

	// O ultimo pulso PEDIDO: a linha (o id que entra no rodizio) e o verb que viaja (o que desliga a Expansao nao e o que liga).
	private string? _linhaDoPulso, _verbDoPulso;

	private void Pedi(string linha, string verb) { _linhaDoPulso = linha; _verbDoPulso = verb; }

	/// <summary>
	/// O VERB SAIU -- quem avisa e o atuador, depois de apertar (`GameServer.DepoisDoApertoDaIa`).
	///
	/// ============================ O RODIZIO CONTA O QUE SAIU, E NAO O QUE FOI PEDIDO ============================
	/// O cerebro pede e o MUNDO responde o que o corpo pode: com um raio na mao (doze segundos de canal) o atuador
	/// descarta o pulso, e o cerebro nao sabe disso -- de proposito (`GameServer.AplicarComando`). Marcando o uso na
	/// hora de PEDIR, cada raio "gastava" oito tecnicas que nunca sairam, e o rodizio pulava justamente as mais
	/// caras (medido na `--v021teste`: doze dos trinta e dois tiros nunca apertados numa luta de dez minutos).
	///
	/// Entao a marca e deste aviso. Pulso descartado nao conta: a escolha seguinte pede a mesma tecnica de novo, e
	/// ela sai quando as maos soltarem. Verb APERTADO conta mesmo recusado la dentro (recarga, falta de Ki) -- e o
	/// que impede o cerebro de bater na mesma porta a luta inteira.
	/// ==========================================================================================================
	/// </summary>
	public void Apertou(string verb)
	{
		if (_linhaDoPulso == null || !string.Equals(verb, _verbDoPulso, StringComparison.Ordinal)) return;
		Usei(_linhaDoPulso);
		_linhaDoPulso = _verbDoPulso = null;
	}

	/// <summary>Os relogios do repertorio, no topo do <see cref="Pensar"/> com os outros.</summary>
	private void TicarORepertorio(in Percepcao p, double dt)
	{
		if (_pausaDoPunho > 0) _pausaDoPunho -= dt;
		if (_pausaDoApoio > 0) _pausaDoApoio -= dt;
		bool lutando = p.TemAlvo && !p.AlvoCaido;
		_lutandoHa = lutando ? _lutandoHa + dt : 0;
		_semAlvoHa = lutando ? 0 : _semAlvoHa + dt;
	}

	/// <summary>Ha folego pra esta tecnica? (A segunda moeda; a de Ki cada receita confere com a reserva dela.)</summary>
	private static bool PagaOFolego(in Percepcao p, in Tiro t) => t.CustoDeFolego <= 0 || p.Folego >= t.CustoDeFolego;

	/// <summary>A cena e a que a tecnica pede? Ver <see cref="Exige"/>.</summary>
	private static bool ACenaServe(in Percepcao p, in Tiro t) =>
		((t.Exige & Exige.AlvoNoAr) == 0 || p.AlvoVoando)
		&& ((t.Exige & Exige.AlvoNoChao) == 0 || !p.AlvoVoando)
		&& ((t.Exige & Exige.VidaSobrando) == 0 || p.VidaFrac >= VidaQueSobra)
		&& ((t.Exige & Exige.TiroVindo) == 0 || p.HaTiroVindo);

	/// <summary>
	/// UMA TECNICA DESTE USO QUE SERVE AGORA: a que esta ha mais tempo sem ser usada; entre as empatadas (as que ele
	/// nunca usou), uma sorteada -- sem lista, numa passada so. <paramref name="marca"/> e a exigencia que ela TEM de
	/// ter (o agarrao, o tiro vindo); sem marca, as de reacao ficam de fora. <paramref name="semJanela"/>: quem ja
	/// esta nos bracos (ou o tiro que vem) nao tem distancia a conferir.
	/// </summary>
	private bool Escolher(in Percepcao p, Random rng, Uso uso, Exige marca, bool semJanela, double descansoPadrao, out Tiro escolhida)
	{
		escolhida = default;
		Arsenal luta = Poderes.DeLuta;
		int empatadas = 0;
		double maisAntiga = double.PositiveInfinity;
		for (int i = 0; i < luta.Quantas; i++)
		{
			Tiro t = luta[i];
			if (t.Uso != uso) continue;
			if (marca != Exige.Nada ? (t.Exige & marca) == 0 : (t.Exige & Exige.TiroVindo) != 0) continue;
			if (Descansando(t, descansoPadrao)) continue;
			if (!semJanela && (p.Distancia < t.AlcanceMin || p.Distancia > t.AlcanceMax)) continue;
			if (p.Ki - t.CustoDeKi < ReservaDeKi(p) || !PagaOFolego(p, t) || !ACenaServe(p, t)) continue;

			double usada = UltimoUso(t.Id);
			if (usada > maisAntiga) continue;
			if (usada < maisAntiga) { maisAntiga = usada; empatadas = 0; }
			empatadas++;
			if (rng.Next(empatadas) == 0) escolhida = t;
		}
		return empatadas > 0;
	}

	/// <summary>
	/// O PULSO: o repertorio paga a pausa -- a do relogio do USO dele -- e o gesto do plano leva o verb. O uso se marca
	/// quando ele SAI -- ver <see cref="Apertou"/>.
	/// </summary>
	private Comando Soltar(Comando gesto, in Tiro t, string id, string porque)
	{
		Pedi(t.Id, id);
		if (t.Uso is Uso.Reforco or Uso.Socorro) _pausaDoApoio = PausaEntreApoios;
		else _pausaDoPunho = PausaEntreTecnicas;
		if (Explicando) Porque = $"repertorio: {id} ({porque})";
		return gesto with { Habilidade = id };
	}

	/// <summary>
	/// O REPERTORIO POR CIMA DO GESTO DO PLANO. Todo tique -- e por isso a primeira linha e a poda: um `int` contra
	/// zero pra todo corpo que nao sabe tecnica de luta nenhuma.
	/// </summary>
	private Comando ComORepertorio(in Percepcao p, Comando gesto, Random rng)
	{
		if (!Poderes.DeLuta.TemAlguma || RepertorioCalado) return gesto;
		if (gesto.Habilidade is { Length: > 0 }) return gesto;   // o tique ja leva um verb
		// Quem carrega planta o pe; quem foge nao luta; quem tem alguem nos bracos e do `ComAPresaNaMao`.
		if (gesto.Carregar || Atual == Plano.Fugir || p.Segurando != 0 || p.Arremessado || p.Atordoado) return gesto;

		// OS DOIS RELOGIOS: os punhos (rebater, golpe, investida) e o apoio (socorro, reforco) nao se esperam.
		bool punhoPronto = _pausaDoPunho <= 0, apoioPronto = _pausaDoApoio <= 0;
		if (!punhoPronto && !apoioPronto) return gesto;

		// --- REBATER: o tiro vem agora, e nao daqui a um quarto de segundo ---
		if (punhoPronto && p.HaTiroVindo
			&& Escolher(p, rng, Uso.Golpe, Exige.TiroVindo, semJanela: true, DescansoDoGolpe, out Tiro rebate))
			return Soltar(gesto, rebate, rebate.Id, "ha tiro vindo");

		bool lutando = p.TemAlvo && !p.AlvoCaido;

		// --- O SOCO VIRA GOLPE: so no tique em que a receita ia socar ---
		if (punhoPronto && lutando && (gesto.Leve || gesto.Pesado) && rng.NextDouble() < ChanceDeGolpeEspecial
			&& Escolher(p, rng, Uso.Golpe, Exige.Nada, semJanela: false, DescansoDoGolpe, out Tiro golpe))
			return Soltar(gesto with { Leve = false, Pesado = false }, golpe, golpe.Id, "no lugar do soco");

		// As outras perguntas olham a tabela inteira: no relogio delas, e nao a 30 Hz.
		if (_relogio < _olhadaEm) return gesto;
		_olhadaEm = _relogio + OlhadaNoRepertorio;

		// --- O SOCORRO: a vida, e depois o tanque ---
		if (apoioPronto && p.VidaFrac < VidaQuePedeSocorro && Socorro(p, doTanque: false, out Tiro cura))
			return Soltar(gesto, cura, cura.Id, $"vida {p.VidaFrac:0.00}");
		if (apoioPronto && p.KiFrac < KiBaixo && Socorro(p, doTanque: true, out Tiro folego))
			return Soltar(gesto, folego, folego.Id, $"ki {p.KiFrac:0.00}");

		// --- A INVESTIDA: pressionando, com o alvo na janela, a linha livre e o mesmo andar ---
		if (punhoPronto && lutando && Atual is Plano.Pressionar or Plano.Alcancar
			&& p.LinhaLivre && !p.TemParede && p.AlcancoPelaAltura
			&& rng.NextDouble() < ChanceDeInvestidaPorOlhada
			&& Escolher(p, rng, Uso.Investida, Exige.Nada, semJanela: false, DescansoDaInvestida, out Tiro corrida))
			return Soltar(gesto, corrida, corrida.Id, $"alvo a {p.Distancia:0} px");

		// --- OS REFORCOS ---
		if (!apoioPronto) return gesto;
		if (lutando)
		{
			// COM O KI BAIXO ELES SAEM: quase todos alugam o tanque, e e com o tanque que ele vai se afastar e carregar.
			if (p.KiFrac <= KiBaixo)
			{
				_reforcosCortados = true;
				return Desligar(p, gesto, "ki baixo");
			}
			if (_reforcosCortados && p.KiFrac < KiQueReligaOReforco) return gesto;
			_reforcosCortados = false;
			if (_lutandoHa >= SegundosDeLutaAteOReforco && ReforcoPraLigar(p, out Tiro liga))
			{
				// O `Ligada` e lido a 1 Hz: no segundo seguinte ao pulso a tabela ainda diz que o grupo esta apagado, e
				// o cerebro pediria o irmao. Os irmaos esperam a leitura seguinte.
				if (liga.Grupo != 0) { _grupoRecemLigado = liga.Grupo; _grupoRecemLigadoAte = _relogio + DescansoDoReforco; }
				_desliguei?.Remove(liga.Id);
				return Soltar(gesto, liga, liga.Id, "a luta comecou");
			}
		}
		else if (_semAlvoHa >= SegundosSemAlvoAteDesligar)
			return Desligar(p, gesto, "a luta acabou");

		return gesto;
	}

	/// <summary>O socorro que serve: o da vida, ou o do tanque (<see cref="Exige.KiNoFim"/>). O primeiro da tabela que estiver pronto.</summary>
	private bool Socorro(in Percepcao p, bool doTanque, out Tiro socorro)
	{
		Arsenal luta = Poderes.DeLuta;
		for (int i = 0; i < luta.Quantas; i++)
		{
			Tiro t = luta[i];
			if (t.Uso != Uso.Socorro || ((t.Exige & Exige.KiNoFim) != 0) != doTanque) continue;
			if (Descansando(t, DescansoDoSocorro) || p.Ki < t.CustoDeKi || !PagaOFolego(p, t)) continue;
			socorro = t;
			return true;
		}
		socorro = default;
		return false;
	}

	/// <summary>
	/// O PROXIMO REFORCO A LIGAR: apagado, descansado, pagavel -- e sem um irmao de grupo ja de pe (os quatro do
	/// mesmo slot nao se sustentam juntos, e pedir o segundo e so ouvir a recusa). Um por pulso; entre os que
	/// servem, o que esta ha mais tempo sem ser usado -- e o que faz os quatro do mesmo slot se revezarem de uma
	/// luta pra outra, em vez de o primeiro da tabela ser o unico que existe.
	/// </summary>
	private bool ReforcoPraLigar(in Percepcao p, out Tiro liga)
	{
		liga = default;
		Arsenal luta = Poderes.DeLuta;
		bool achei = false;
		double maisAntiga = double.PositiveInfinity;
		for (int i = 0; i < luta.Quantas; i++)
		{
			Tiro t = luta[i];
			if (t.Uso != Uso.Reforco || t.Ligada) continue;
			// APAGADO, E POR QUE? Se fui eu que desliguei (o Ki baixo, o fim da luta), volta logo. Se eu pedi e ele
			// nao ficou de pe, o jogo recusou -- e bater de novo na mesma porta e so gastar a vez dos golpes.
			if (Descansando(t, _desliguei?.Contains(t.Id) == true ? DescansoDoReforco : DescansoDoReforcoRecusado)) continue;
			if (p.Ki - t.CustoDeKi < ReservaDeKi(p) || !PagaOFolego(p, t)) continue;
			if (t.Grupo != 0 && (GrupoDePe(luta, t.Grupo) || (t.Grupo == _grupoRecemLigado && _relogio < _grupoRecemLigadoAte))) continue;
			double usada = UltimoUso(t.Id);
			if (achei && usada >= maisAntiga) continue;
			achei = true;
			maisAntiga = usada;
			liga = t;
		}
		return achei;
	}

	private static bool GrupoDePe(Arsenal luta, byte grupo)
	{
		for (int i = 0; i < luta.Quantas; i++)
			if (luta[i].Uso == Uso.Reforco && luta[i].Grupo == grupo && luta[i].Ligada) return true;
		return false;
	}

	/// <summary>DESLIGA UM REFORCO que esteja de pe (um por pulso). Os que nao se desligam (a copia, as miragens) ficam.</summary>
	private Comando Desligar(in Percepcao p, Comando gesto, string porque)
	{
		Arsenal luta = Poderes.DeLuta;
		for (int i = 0; i < luta.Quantas; i++)
		{
			Tiro t = luta[i];
			if (t.Uso != Uso.Reforco || !t.Ligada || (t.Exige & Exige.NaoSeDesliga) != 0 || Descansando(t, DescansoDoReforco)) continue;
			(_desliguei ??= new HashSet<string>(StringComparer.Ordinal)).Add(t.Id);
			return Soltar(gesto, t, t.IdDeDesligar ?? t.Id, "desligar: " + porque);
		}
		return gesto;
	}

	/// <summary>
	/// COM ALGUEM NOS BRACOS, pra quem NAO carrega o kit do Deus (o dele tem receita propria -- `Cerebro.Kit.cs`).
	///
	/// So chega aqui quem agarrou por um golpe de agarrao do repertorio (o Aperto e a Chave deixam a presa na mao).
	/// A regra do agarrao e a de todo mundo -- o PASSO de quem segura arremessa, o SOCO de quem segura ALTERNA o
	/// aperto no pescoco, e quem aperta nao anda -- e a receita daqui e inteira, com as constantes do kit:
	///
	///   1. PARADO um instante que da pra ver (<see cref="SegundosComAPresaNaMao"/>);
	///   2. OUTRO GOLPE DE AGARRAO, se houver um pronto (o rodizio de sempre);
	///   3. sem nenhum, APERTA O PESCOCO: UM pulso de soco, e fica parado apertando;
	///   4. vencido o <see cref="PrazoComAPresaNaMao"/>, AFROUXA (outro pulso) e da O PASSO QUE ARREMESSA -- ou, com a
	///      presa NO COLO (o Espigao e o Arrasto a levantam), o toque na tecla de agarrar que a SOLTA: no colo, andar
	///      a leva junto.
	///
	/// ============================ POR QUE ELA NAO "VOLTA AO PLANO" ============================
	/// A primeira versao devolvia nulo sem golpe pronto e depois do prazo, contando que o plano andasse e a presa
	/// fosse arremessada. O plano de pressionar NAO anda com o alvo colado -- a distancia ja e a ideal --, e quem
	/// esta nos bracos esta colado por definicao. Medido na `--v021teste`: a fera passava a luta com o boneco nos
	/// bracos, o aperto piscando a cada soco da rajada (cada soco alterna), e com alguem nos bracos o repertorio
	/// inteiro fica calado -- dez golpes de perto em doze minutos de luta.
	/// ========================================================================================
	/// </summary>
	private Comando? ComAPresaNaMaoDoRepertorio(in Percepcao p, Random rng)
	{
		if (!Poderes.DeLuta.TemAlguma || RepertorioCalado) return null;

		if (_segurandoHa > PrazoComAPresaNaMao)
		{
			if (PresaFicaNosBracosDeTeste) return null;
			// QUEM APERTA NAO ANDA (`GameServer.PodeMexerOCorpo`): afrouxa primeiro, e o passo vem no tique seguinte.
			if (p.Estrangulando) return PulsoNoPescoco();
			// NO COLO O PASSO NAO ARREMESSA -- leva junto. Solta o terceiro toque da tecla de agarrar, o mesmo verb do
			// jogador (e o do kit, quando o bote pega o corpo errado).
			if (p.CarregandoAPresa) return PulsoDeSoltar();
			return new Comando { Rumo = RumoDoArremesso(p), Olhar = p.Direcao };
		}

		if (_segurandoHa < SegundosComAPresaNaMao) return new Comando();
		if (_pausaDoPunho <= 0
			&& Escolher(p, rng, Uso.Golpe, Exige.Agarra, semJanela: true, DescansoDoGolpe, out Tiro g))
			return Soltar(new Comando(), g, g.Id, "quem esta nos bracos");
		return p.Estrangulando ? new Comando() : PulsoNoPescoco();
	}

	/// <summary>UM soco com a presa nos bracos: alterna o aperto no pescoco. Espacado pela <see cref="PausaDoPescoco"/>.</summary>
	private Comando PulsoNoPescoco()
	{
		if (_relogio < _pescocoEm) return new Comando();
		_pescocoEm = _relogio + PausaDoPescoco;
		return new Comando { Leve = true };
	}

	/// <summary>O toque na tecla de agarrar com a presa no colo: solta. No relogio do pescoco -- um pulso de cada vez.</summary>
	private Comando PulsoDeSoltar()
	{
		if (_relogio < _pescocoEm) return new Comando();
		_pescocoEm = _relogio + PausaDoPescoco;
		return new Comando { Habilidade = KitDoDeusNaIa.VerbAgarrar };
	}

	/// <summary>
	/// O PASSO QUE ARREMESSA. Pra TRAS, olhando pra frente: a presa esta na frente (um passo contra o corpo dela pode
	/// nao sair do lugar, e quem nao sai do lugar nao arremessa), e e pra frente que ela voa. Se o passo nao sai --
	/// uma parede nas costas --, a cada <see cref="PausaDoPescoco"/> ele tenta o lado seguinte: nao ha corpo que
	/// fique nos bracos dele pra sempre.
	/// </summary>
	private Vec2 RumoDoArremesso(in Percepcao p)
	{
		Vec2 frente = p.Direcao.LengthSquared > 1e-6f ? p.Direcao : new Vec2(1, 0);
		return ((int)((_segurandoHa - PrazoComAPresaNaMao) / PausaDoPescoco) & 3) switch
		{
			0 => frente * -1f,
			1 => new Vec2(-frente.Y, frente.X),
			2 => new Vec2(frente.Y, -frente.X),
			_ => frente,
		};
	}
}
