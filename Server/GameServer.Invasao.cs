using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>Em que ponto da campanha uma invasao esta. E o `for(w = 1 to waves.len)` do DM virado estado.</summary>
public enum FaseDaInvasao : byte
{
	/// <summary>Bandeira fincada, a tensao antes da primeira onda (`sleep(30)`, :499).</summary>
	Tensao,

	/// <summary>Uma onda de pe. Acaba quando o ultimo defensor cai ou e nocauteado.</summary>
	Lutando,

	/// <summary>O respiro entre ondas (`CONQ_WAVE_BREATHER`, :548).</summary>
	Respiro,

	/// <summary>Mundo SEM POVO: nao ha onda, a bandeira so precisa firmar. Ver `Conquista.OndasSemPovo`.</summary>
	Plantio,
}

/// <summary>Uma invasao em andamento -- o `datum/conq_invasion` (PlanetConquest.dm:431).</summary>
public sealed class Invasao
{
	/// <summary>A MESMA chave do dominio e da destruicao. Ver o cabecalho do `Dominio`.</summary>
	public ChaveDePlaneta Chave;

	public ZoneKey Zona;
	public string Planeta = "";

	/// <summary>Tem cidadao? Ver `GameServer.TemPovo` -- e o que substituiu o `conq_premade`.</summary>
	public bool TemPovo;

	/// <summary>`native`: verdadeiro = o povo defende a si mesmo; falso = guarnicao DO DONO atual (:435).</summary>
	public bool Nativa = true;

	public int Invasor;
	public string AssinaturaDoInvasor = "";

	/// <summary>Onde a bandeira foi fincada. E o centro de tudo -- e onde os defensores nascem.</summary>
	public Vec2 Bandeira;

	/// <summary>Relogio do mundo (`_relogioDoMundo`): quando a campanha inteira vence o prazo.</summary>
	public double PrazoEm;

	/// <summary>Quando a fase atual termina (tensao, respiro, plantio).</summary>
	public double PassoEm;

	/// <summary>`base_bp` (:438): o expresso do invasor no plantio -- o piso anti-destransformar.</summary>
	public double ExpressoNoPlantio;

	public FaseDaInvasao Fase = FaseDaInvasao.Tensao;

	/// <summary>Indice da onda ATUAL (0 = a primeira).</summary>
	public int Onda;

	public bool Arrancada;
	public int Mortos;

	/// <summary>Quem era o dono, pra o recado e pra a LIBERTACAO (:572).</summary>
	public string DonoAntigoAssinatura = "", DonoAntigoNome = "", DonoAntigoConta = "";

	/// <summary>Lealdade do dominio invadido, 100 quando nao ha dono. Enfraquece a guarnicao esquecida.</summary>
	public double LealdadeDaGuarnicao = Conquista.LealdadeMaxima;

	/// <summary>Os corpos da onda. Id -> e o LIDER da defesa?</summary>
	public readonly Dictionary<int, bool> Defensores = [];
}

/// <summary>Um arranque de bandeira em canal -- o `rip_attempt` (:280).</summary>
public sealed class Arranque
{
	public int Quem;

	/// <summary>De que invasao. Pela chave e nao pelo objeto: a invasao pode acabar no meio do canal.</summary>
	public ChaveDePlaneta Onde;

	/// <summary>Segundos que faltam. Conta pra baixo no tique CHEIO -- ver <see cref="GameServer.TickDoArranque"/>.</summary>
	public double Faltam = Conquista.SegundosDeArranque;

	/// <summary>De onde ele comecou: sair do lugar cancela (`M.loc != T0`, :294).</summary>
	public Vec2 De;

	/// <summary>O `combatTagExpire` do inicio: qualquer golpe mexe nele e o canal cai (:298).</summary>
	public long RancorNoInicio;

	/// <summary>Pra o aviso de progresso sair de segundo em segundo, e nao trinta vezes por segundo.</summary>
	public double ProximoAviso = Conquista.SegundosDeArranque - 1;
}

/// <summary>
/// **A INVASAO** -- as ondas, o prazo, o fracasso e o arranque da bandeira.
///
/// ============================ O DM ROSCA UM LOOP; AQUI E ESTADO ============================
/// `conq_invasion_run` (:474) e um proc com `sleep()` dentro de dois lacos aninhados: ele DORME
/// esperando a onda cair e acorda de segundo em segundo pra reconferir. No BYOND isso e barato
/// (cada `spawn` e uma corrotina do proprio motor); aqui seria uma thread mexendo em `_players` fora
/// do tique -- exatamente o que a regra 0.4 proibe.
///
/// Entao o laco virou <see cref="FaseDaInvasao"/> e o `sleep` virou prazo. O que se ganha, alem do
/// custo: o WATCHDOG do DM (:185-193, *"invasao cuja rotina morreu num runtime error nao pode trancar
/// o planeta pra sempre"*) deixa de ser necessario -- nao ha rotina pra morrer, so um dicionario que
/// o tique varre.
/// ======================================================================================
///
/// ============================ E O DEFENSOR PRECISA SER MANDADO PRA CIMA ============================
/// O DM chama `N.foundTarget(M)` no nascimento (:529) e RE-ENGAJA o defensor ocioso a cada segundo
/// (:540-542). Aqui isso nao e opcional, e um degrau maior: o defensor e um CIDADAO, e cidadao **nao
/// caca ninguem** (`PapelDeNpc.Pacifico` -- *"peaceful until hit"*). Sem engajar, a onda nasceria e
/// ficaria parada olhando o invasor.
///
/// O canal e o `MarcarAgressao`, que e o funil unico de "quem me agrediu" deste port. Usar o campo
/// cru seria a nona atribuicao solta que aquele funil existe pra ter matado.
/// ==============================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>As invasoes vivas. Um planeta so aguenta uma por vez (`conq_invasions`, :64).</summary>
	private readonly Dictionary<ChaveDePlaneta, Invasao> _invasoes = [];

	/// <summary>Os arranques em canal, por jogador.</summary>
	private readonly Dictionary<int, Arranque> _arranques = [];

	/// <summary>assinatura -> quando a proxima campanha e permitida. E o `conq_next_try` (:69).</summary>
	private readonly Dictionary<string, double> _proximaCampanha = new(StringComparer.Ordinal);

	/// <summary>O contador de lugares dos defensores -- a mesma disciplina do `_lugarDasSagas`.</summary>
	private ulong _lugarDaInvasao = 9_000_000;

	/// <summary>A invasao que esta acontecendo nesta superficie, ou nula.</summary>
	public Invasao? InvasaoAqui(ZoneKey z) =>
		ChaveDePlaneta.Da(z) is { } c && _invasoes.TryGetValue(c, out Invasao? i) ? i : null;

	private string NomeDoCorpo(int id) =>
		_players.TryGetValue(id, out ServerPlayer? p) ? p.Name : "alguém";

	/// <summary>Anuncia pro mundo E pra bancada. Ver `AnunciarNoMundo` (a das sagas) -- e o mesmo canal.</summary>
	private void AnunciarConquista(string texto)
	{
		AnunciarNoMundo(texto);
		EscutaDaConquista?.Add(texto);
	}

	// =====================================================================
	// O VERB
	// =====================================================================
	/// <summary>
	/// **CONQUISTAR PLANETA** -- o `mob/verb/Conquistar_Planeta` (:357), guarda por guarda, na ordem
	/// dele. A ordem importa: e ela que decide QUAL recusa o jogador le quando duas valem.
	/// </summary>
	private void ConquistaInvadir(ServerPlayer pl, string arg)
	{
		if (pl.Ficha.dead || pl.Ficha.KO) { Avisar(pl, "não dá pra fincar bandeira de joelhos."); return; }
		if (pl.Assinatura.Length == 0) { Avisar(pl, "só um personagem de verdade conquista mundos."); return; }

		if (_invasoes.Values.Any(i => i.Invasor == pl.Id))
		{ Avisar(pl, "você já está no meio de uma conquista."); return; }

		// ============================ "E UM PLANETA?" VEM ANTES DE "QUAL PLANETA?" ============================
		// O crivo e o `Espaco.EhPlaneta` (a definicao unica das duas pontas) e ele so precisa da ZONA.
		// A ordem importa e a bancada foi quem mostrou: perguntando primeiro pelo CORPO, um mundo que
		// existe mas nao esta com a superficie carregada caia na frase "pise na superficie de um
		// planeta" -- e a mesma frase engolia o caso de verdade que vem logo abaixo, o planeta morto.
		// ================================================================================================
		if (ChaveDePlaneta.Da(pl.Zone) is not { } chave)
		{ Avisar(pl, "este lugar não pode ser conquistado -- pise na superfície de um planeta."); return; }

		// PLANETA MORTO OU MORRENDO. O DM so tem o primeiro caso (:378); o segundo nasceu com a
		// destruicao deste port, e recusar nele e o que impede alguem de conquistar um mundo cujo
		// pavio ja esta aceso e perde-lo trinta segundos depois.
		//
		// AS DUAS PERGUNTAS SAO SOBRE A ZONA e por isso cabem aqui, antes do corpo: quem esta sendo
		// evacuado de um planeta em explosao continua pisando naquela zona.
		if (ZonaMorta(pl.Zone)) { Avisar(pl, "não há nada para dominar num planeta morto."); return; }
		if (ZonaCondenada(pl.Zone))
		{ Avisar(pl, "este mundo está morrendo. Não há o que dominar aqui."); return; }

		// O CORPO CELESTE. Nulo aqui so acontece com mundo gerado cuja superficie nao esta carregada
		// -- estado que em jogo nao existe (quem pisa num gerado pousou nele), e que merece a frase
		// certa em vez da de "isto nao e um planeta".
		if (CorpoDaZona(pl.Zone) is not { } p)
		{ Avisar(pl, "o chão ainda está se formando sob você -- espere o mundo assentar."); return; }

		Dominio? dono = DominioDe(chave);
		if (dono != null && string.Equals(dono.Assinatura, pl.Assinatura, StringComparison.Ordinal))
		{ Avisar(pl, $"{NomeVisivel(p.Nome)} já é seu domínio."); return; }

		if (_invasoes.ContainsKey(chave))
		{ Avisar(pl, $"já há uma invasão em andamento em {NomeVisivel(p.Nome)}."); return; }

		if (QuantosDomina(pl.Assinatura) >= Conquista.MaximoDeDominios)
		{
			Avisar(pl, $"você já rege {Conquista.MaximoDeDominios} domínios -- abandone um para conquistar outro.");
			return;
		}

		if (_proximaCampanha.TryGetValue(pl.Assinatura, out double quando) && _relogioDoMundo < quando)
		{
			Avisar(pl, "suas forças ainda se reorganizam da última campanha "
					 + $"({(quando - _relogioDoMundo) / 60:0} min).");
			return;
		}

		bool povo = TemPovo(p.Nome);
		double minimo = povo ? Conquista.BpMinimoComPovo : Conquista.BpMinimoSemPovo;
		if (pl.Ficha.expressedBP < minimo)
		{
			Avisar(pl, $"você é fraco demais para subjugar este mundo (poder mínimo: {minimo:N0}; "
					 + $"o seu é {pl.Ficha.expressedBP:N0}).");
			return;
		}

		// ============================ A REIVINDICACAO PACIFICA -- e o "forca" que a recusa ============================
		// No DM isto e um `alert()` de tres botoes (:400). Sem modal, a regra vira: HEROI de um povo
		// SEM dono reivindica em paz por padrao, e quem quiser sangue pede sangue com o argumento.
		// Inverter (invadir por padrao) faria um heroi massacrar o proprio povo por ter apertado um
		// botao -- e a reputacao dele desabaria por isso, calada.
		// =========================================================================================================
		bool querForca = arg.Trim().StartsWith("forc", StringComparison.OrdinalIgnoreCase);
		if (povo && dono == null && ReputacaoDe(p.Nome, pl.Conta) >= Reputacao.LimiarDeHeroi && !querForca)
		{
			Avisar(pl, $"o povo de {NomeVisivel(p.Nome)} te considera um HERÓI e aceita seu domínio "
					 + "SEM DERRAMAR SANGUE. (Argumento 'forca' se você quiser invadir mesmo assim.)");
			ReivindicarEmPaz(pl, p);
			return;
		}

		ComecarInvasao(pl, chave, p, povo, dono);
	}

	private void ComecarInvasao(ServerPlayer pl, ChaveDePlaneta chave, PlanetaNoEspaco p,
								bool povo, Dominio? dono)
	{
		var inv = new Invasao
		{
			Chave = chave,
			Zona = pl.Zone,
			Planeta = p.Nome,
			TemPovo = povo,
			Nativa = dono == null,
			Invasor = pl.Id,
			AssinaturaDoInvasor = pl.Assinatura,
			Bandeira = pl.Pos,
			PrazoEm = _relogioDoMundo + Conquista.SegundosDeInvasao,
			ExpressoNoPlantio = Math.Max(pl.Ficha.expressedBP, 1),
			DonoAntigoAssinatura = dono?.Assinatura ?? "",
			DonoAntigoNome = dono?.Nome ?? "",
			DonoAntigoConta = dono?.Conta ?? "",
			LealdadeDaGuarnicao = dono?.Lealdade ?? Conquista.LealdadeMaxima,
		};

		// SEM POVO NAO HA ONDA -- ver `Conquista.OndasSemPovo`. A bandeira so precisa FIRMAR, e nesse
		// tempo qualquer um arranca. E a contestacao sozinha, que e o que sobra onde nao mora ninguem.
		OndaDeDefesa[] ondas = povo ? Conquista.OndasComPovo : Conquista.OndasSemPovo;
		inv.Fase = ondas.Length == 0 ? FaseDaInvasao.Plantio : FaseDaInvasao.Tensao;
		inv.PassoEm = _relogioDoMundo
					+ (ondas.Length == 0 ? Conquista.SegundosDePlantio : Conquista.SegundosDeTensao);

		_invasoes[chave] = inv;
		_proximaCampanha[pl.Assinatura] = _relogioDoMundo + Conquista.SegundosEntreCampanhas;

		AnunciarConquista(povo
			? $"{pl.Name} fincou a bandeira de INVASÃO em {NomeVisivel(p.Nome)}! As forças de defesa respondem!"
			: $"{pl.Name} fincou a bandeira de conquista em {NomeVisivel(p.Nome)} -- um mundo sem povo. "
			  + $"Ela firma em {Conquista.SegundosDePlantio:0}s; quem chegar antes pode arrancá-la.");

		if (inv.DonoAntigoAssinatura.Length > 0)
			RecadoAoDono(inv.DonoAntigoAssinatura,
				$"Seu domínio {NomeVisivel(p.Nome)} está sendo INVADIDO por {pl.Name}! "
				+ "Arranque a bandeira dele ou reze pela guarnição.");

		GD.Print($"[conquista] INVASAO de {p.Nome} por {pl.Name} | expresso {inv.ExpressoNoPlantio:N0} "
			   + $"| {(povo ? $"{ondas.Length} onda(s)" : "sem povo: plantio")} "
			   + $"| guarnicao {(inv.Nativa ? "nativa" : $"do dono {inv.DonoAntigoNome} (lealdade {inv.LealdadeDaGuarnicao:0})")}");
	}

	// =====================================================================
	// O TIQUE DA INVASAO (1 Hz -- o `sleep(10)` do DM)
	// =====================================================================
	private void TickDasInvasoes()
	{
		if (_invasoes.Count == 0) return;

		foreach (Invasao inv in _invasoes.Values.ToList())
		{
			if (MotivoDoFracasso(inv) is { } porque) { FracassarInvasao(inv, porque); continue; }

			switch (inv.Fase)
			{
				case FaseDaInvasao.Plantio:
					if (_relogioDoMundo >= inv.PassoEm) VencerInvasao(inv);
					break;

				case FaseDaInvasao.Tensao:
				case FaseDaInvasao.Respiro:
					if (_relogioDoMundo >= inv.PassoEm) NascerOnda(inv);
					break;

				case FaseDaInvasao.Lutando:
					TocarAOnda(inv);
					break;
			}
		}
	}

	/// <summary>
	/// `fail_reason` (:449). O caso "a bandeira caiu" do DM nao existe aqui: a bandeira e uma posicao
	/// no livro (ver o cabecalho de `GameServer.Conquista.cs`) e nao tem como cair sozinha. Em troca
	/// entra um que o DM nao tinha: o planeta explodir no meio da invasao.
	/// </summary>
	private string? MotivoDoFracasso(Invasao inv)
	{
		if (inv.Arrancada) return "a bandeira foi arrancada";
		if (!_players.TryGetValue(inv.Invasor, out ServerPlayer? m) || !EhJogador(m))
			return "o invasor desapareceu";
		if (m.Ficha.dead) return "o invasor foi morto";
		if (m.Zone.Hash != inv.Zona.Hash) return "o invasor fugiu do planeta";
		if (ZonaCondenada(inv.Zona)) return "o planeta começou a morrer sob os pés dele";
		if (_relogioDoMundo > inv.PrazoEm) return "os defensores resistiram até o fim";
		return null;
	}

	/// <summary>
	/// UMA ONDA NASCE. `bpref` e relido AGORA com piso no plantio (`max(M.expressedBP, base_bp)`,
	/// :509), e o LIDER entra junto na ULTIMA (:518).
	/// </summary>
	private void NascerOnda(Invasao inv)
	{
		OndaDeDefesa[] ondas = inv.TemPovo ? Conquista.OndasComPovo : Conquista.OndasSemPovo;
		if (inv.Onda >= ondas.Length) { VencerInvasao(inv); return; }

		OndaDeDefesa onda = ondas[inv.Onda];
		bool ultima = inv.Onda == ondas.Length - 1;

		double expresso = _players.TryGetValue(inv.Invasor, out ServerPlayer? m) ? m.Ficha.expressedBP : 0;
		double bpref = Conquista.ReferenciaDaOnda(expresso, inv.ExpressoNoPlantio);

		// A GUARNICAO DE UM DOMINIO ESQUECIDO LUTA PIOR -- e a terceira consequencia da lealdade (ver
		// `Conquista.FatorDaGuarnicao`). Defesa NATIVA nao tem dono a quem ser leal: vale 1.
		double guarnicao = inv.Nativa ? 1 : Conquista.FatorDaGuarnicao(inv.LealdadeDaGuarnicao);

		int nasceram = 0;
		for (int i = 0; i < onda.Quantos; i++)
			if (NascerDefensor(inv, Conquista.BpDoDefensor(bpref, onda.Fator, guarnicao), lider: false))
				nasceram++;

		if (ultima
			&& NascerDefensor(inv, Conquista.BpDoDefensor(bpref, Conquista.FatorDoLider, guarnicao), lider: true))
			nasceram++;

		inv.Fase = FaseDaInvasao.Lutando;
		AnunciarNaZonaDaInvasao(inv,
			$"Onda {inv.Onda + 1}/{ondas.Length} de defensores de {NomeVisivel(inv.Planeta)}!"
			+ (ultima ? " O LÍDER da defesa entra na batalha!" : ""));

		GD.Print($"[conquista] {inv.Planeta}: onda {inv.Onda + 1}/{ondas.Length} -- {nasceram} defensor(es), "
			   + $"referencia {bpref:N0} x {onda.Fator:0.00} x {guarnicao:0.00}");

		// A ONDA VAZIA NAO TRANCA A INVASAO. Ela so acontece quando o teto de lotacao da zona recusou
		// TODO MUNDO (`CabeMaisUmCorpo`), e ai a onda esta vencida por ausencia -- o proximo tique ve
		// zero de pe e segue. Isto e um recuo, e por isso ele GRITA: uma invasao que passa sozinha
		// porque o planeta estava cheio e informacao, e nao um detalhe.
		if (nasceram == 0)
			GD.PushWarning($"[conquista] {inv.Planeta}: a onda {inv.Onda + 1} nasceu VAZIA -- a zona esta "
						 + "no teto de lotacao. A onda passa sem luta.");
	}

	/// <summary>
	/// UM DEFENSOR. Pelo molde `cidadao` e pela MESMA <see cref="NascerNpc"/> de todo mundo -- e e o
	/// que o DM faz: o `mob/npc/Citizen/Defender` dele e literalmente um subtipo de cidadao, nascido
	/// pelo `init_citizen` (:641).
	///
	/// O BP E PINADO DEPOIS DO NASCIMENTO, como o `conq_pin_bp` (:686) e pelo mesmo motivo escrito la:
	/// a fabrica sorteia o BP na faixa do molde (500-5000) e a onda precisa do numero da CONTA. A raca
	/// sai do BERCO do planeta -- um defensor de Namek e Namekuseijin sem ninguem escrever isso.
	/// </summary>
	private bool NascerDefensor(Invasao inv, double bp, bool lider)
	{
		if (_moldes?.Get("cidadao") == null)
		{
			GD.PushError("[conquista] sem o molde 'cidadao' no npcs.json -- a defesa nao tem de onde sair");
			return false;
		}

		// O TETO DE LOTACAO E DO POVOAMENTO, e vale pra todo corpo sem dono. Uma onda que o
		// atropelasse poria 11 corpos num planeta que ja tem 40 cidadaos num teto de 80 -- e o teto
		// deixaria de ser teto por uma porta lateral.
		if (!CabeMaisUmCorpo(inv.Zona, Povoamento.MaxPorZona, Povoamento.MaxNoServidor, out string porque))
		{
			GD.Print($"[conquista] defensor nao nasceu: {porque}");
			return false;
		}

		ulong lugar = ++_lugarDaInvasao;
		ServerPlayer? d = NascerNpc("cidadao", inv.Zona, PontoPertoDaBandeira(inv, lugar), lugar);
		if (d == null) return false;

		// O NOME DIZ O QUE ELE E (:649-659). O sorteio ja deu um nome proprio; aqui so vem a patente.
		d.Name = (lider ? "General " : "Soldado ") + d.Name;

		// O PINO: o BP da conta, e a ficha fechada por cima dele (`Normalizar` e o resto do
		// `conq_pin_bp` -- Ki cheio, vigor cheio, raiva em 100).
		d.Ficha.BP = bp;
		SorteioDeNpc.Normalizar(d.Ficha);
		d.SigAtributos = "";

		inv.Defensores[d.Id] = lider;
		EngajarDefensor(inv, d);
		TrocarAparencias(d);
		return true;
	}

	/// <summary>
	/// ONDE UM DEFENSOR APARECE -- `conq_spawn_turf_near` (:591): perto da bandeira, mas **nunca em
	/// cima do invasor**. O DM tenta 30 vezes exigindo `get_dist >= 2`; aqui a colisao ja responde
	/// celula por celula (`PontoLivrePerto`), entao basta sortear o deslocamento com um anel morto no
	/// meio -- e o sorteio e por semente estavel, como todo nascimento deste port.
	/// </summary>
	private Vec2 PontoPertoDaBandeira(Invasao inv, ulong lugar)
	{
		const int t = ZoneCollision.TileSize;
		Random r = SorteioDeNpc.Sorteador(Espaco.Misturar(SeedDoUniverso, inv.Zona.Hash, lugar), "defensor");

		int dx = r.Next(2, 6) * (r.Next(2) == 0 ? -1 : 1);
		int dy = r.Next(2, 6) * (r.Next(2) == 0 ? -1 : 1);
		var desejado = new Vec2(inv.Bandeira.X + dx * t, inv.Bandeira.Y + dy * t);
		return MapaDaZonaOuCatalogo(inv.Zona)?.PontoLivrePerto(desejado) ?? desejado;
	}

	/// <summary>
	/// **MANDA O DEFENSOR PRA CIMA DO INVASOR.** O `foundTarget(M)` do DM (:529), pelo unico canal que
	/// este port tem: cidadao so conhece quem bateu nele (`PresaDoNpc`), e o funil de "quem me bateu"
	/// e o <see cref="MarcarAgressao"/>.
	///
	/// Semanticamente isto diz "o invasor agrediu o defensor", o que ainda nao aconteceu -- e e a
	/// traducao honesta de `foundTarget`: fincar bandeira num mundo com povo E a agressao, e e por
	/// isso que ela e o gatilho. Escrever o campo cru em vez do funil seria a nona atribuicao solta
	/// que aquele funil existe pra ter matado.
	/// </summary>
	private void EngajarDefensor(Invasao inv, ServerPlayer defensor)
	{
		if (!_players.TryGetValue(inv.Invasor, out ServerPlayer? m) || m.Ficha.dead) return;
		MarcarAgressao(defensor, m);
	}

	/// <summary>
	/// A ONDA DE PE: conta quem sobrou, re-engaja o ocioso e decide se a onda caiu.
	///
	/// **NOCAUTEADO CONTA COMO VENCIDO** -- e o coracao do desenho do dono no DM (:13, *"da pra
	/// conquistar SEM MATAR"*), e e o que faz o custo de reputacao ser uma ESCOLHA e nao um pedagio.
	/// </summary>
	private void TocarAOnda(Invasao inv)
	{
		int dePe = 0;
		foreach (int id in inv.Defensores.Keys.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? d) || d.Ficha.dead || d.Ficha.KO) continue;
			dePe++;

			// RE-ENGAJA O OCIOSO -- o `:540-542` do DM. O rancor do cidadao dura 90 s
			// (`Povoamento.SegundosDeRancor`); uma onda pode durar mais que isso, e sem esta linha o
			// defensor PARARIA no meio da luta e voltaria a ser um habitante.
			if (d.UltimoAgressor != inv.Invasor || NowMs() >= d.RancorAte) EngajarDefensor(inv, d);
		}

		if (dePe > 0) return;

		// A ONDA CAIU. Os nocauteados recuam (`conq_wave_cleanup(I, 0)`, :544) -- ficar deitados no
		// campo faria a proxima onda nascer em cima de um monte de corpos que a IA ainda enxerga.
		LimparOnda(inv, tudo: false);

		OndaDeDefesa[] ondas = inv.TemPovo ? Conquista.OndasComPovo : Conquista.OndasSemPovo;
		inv.Onda++;
		if (inv.Onda >= ondas.Length) { VencerInvasao(inv); return; }

		inv.Fase = FaseDaInvasao.Respiro;
		inv.PassoEm = _relogioDoMundo + Conquista.SegundosDeRespiro;
		AnunciarNaZonaDaInvasao(inv, "Os defensores caídos são arrastados para longe... a próxima onda se aproxima!");
	}

	/// <summary>
	/// TIRA OS DEFENSORES DO MUNDO. `tudo = false` recolhe so os NOCAUTEADOS (fim de onda);
	/// `tudo = true` recolhe todos os que sobraram (fim de campanha). O `conq_wave_cleanup` (:598).
	///
	/// MORTO NAO PASSA POR AQUI: ele ja saiu pelo caminho de sempre (o `_npcsPraTirar` do
	/// `TickCombate`), e e la que a reputacao dele foi cobrada.
	/// </summary>
	private void LimparOnda(Invasao inv, bool tudo)
	{
		foreach (int id in inv.Defensores.Keys.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? d)) { inv.Defensores.Remove(id); continue; }
			if (d.Ficha.dead) { inv.Defensores.Remove(id); continue; }
			if (!tudo && !d.Ficha.KO) continue;

			RemoverNpc(d);
			inv.Defensores.Remove(id);
		}
	}

	// =====================================================================
	// O FIM
	// =====================================================================
	private void FracassarInvasao(Invasao inv, string porque)
	{
		_invasoes.Remove(inv.Chave);
		LimparOnda(inv, tudo: true);

		AnunciarConquista($"A invasão de {NomeVisivel(inv.Planeta)} por {NomeDoCorpo(inv.Invasor)} "
						+ $"FRACASSOU -- {porque}.");

		if (inv.DonoAntigoAssinatura.Length > 0)
			RecadoAoDono(inv.DonoAntigoAssinatura,
				$"Seu domínio {NomeVisivel(inv.Planeta)} RESISTIU à invasão de {NomeDoCorpo(inv.Invasor)}!");

		GD.Print($"[conquista] invasao de {inv.Planeta} fracassou ({porque}) | {inv.Mortos} defensor(es) mortos");
	}

	private void VencerInvasao(Invasao inv)
	{
		_invasoes.Remove(inv.Chave);
		LimparOnda(inv, tudo: true);

		if (!_players.TryGetValue(inv.Invasor, out ServerPlayer? pl) || !EhJogador(pl))
		{
			// Venceu e sumiu no ultimo instante (:566): ninguem reivindica, e o planeta fica livre.
			GD.Print($"[conquista] {inv.Planeta}: a invasao venceu mas o invasor sumiu -- ninguem reivindica");
			return;
		}

		// A BANDEIRA DO DONO ANTIGO CAI (`conq_drop_flags(I.planet, 1)`, :570).
		if (DominioDe(inv.Chave) is { } antigo) PerderDominio(antigo, "", "tomado-por-invasao", anunciar: false);

		// ============================ LIBERTACAO: tomar o mundo de um TIRANO ============================
		// `:571-577`. A reputacao do EX-DONO com aquele povo e quem decide: abaixo do limiar de vilao,
		// tomar o planeta dele nao e conquista, e libertacao -- e o povo paga por ela.
		//
		// A pergunta e pela CONTA porque a reputacao deste port e por conta (ver `SomarReputacao`), e
		// e por isso que o `Dominio` guarda o `ckey` do DM junto com a assinatura.
		// ==========================================================================================
		if (inv.TemPovo && inv.DonoAntigoConta.Length > 0
			&& ReputacaoDe(inv.Planeta, inv.DonoAntigoConta) <= Reputacao.LimiarDeVilao)
		{
			SomarReputacao(inv.Planeta, pl, Conquista.RepPorLibertar, "libertacao");
			Avisar(pl, $"o povo de {NomeVisivel(inv.Planeta)} celebra a queda do tirano {inv.DonoAntigoNome}!");
		}

		if (CorpoDaZona(inv.Zona) is not { } p)
		{
			GD.PushError($"[conquista] {inv.Planeta}: a invasao venceu mas o corpo sumiu do ceu -- "
					   + "nenhum dominio foi fincado");
			return;
		}

		FincarDominio(pl, p, inv.Bandeira);

		if (inv.DonoAntigoAssinatura.Length > 0)
			RecadoAoDono(inv.DonoAntigoAssinatura,
				$"Você PERDEU o domínio de {NomeVisivel(inv.Planeta)} para {pl.Name}!");

		AnunciarConquista($"{NomeVisivel(inv.Planeta)} CAIU! {pl.Name} é o novo soberano do planeta!");
		Avisar(pl, "colete o tributo na sua bandeira -- e apareça: domínio sem soberano por perto se perde.");
		GD.Print($"[conquista] {pl.Name} conquistou {inv.Planeta} ({inv.Mortos} defensores mortos)");
	}

	/// <summary>Uma linha pra quem esta vendo a batalha. E o `view(F) &lt;&lt; output(...)` do DM.</summary>
	private void AnunciarNaZonaDaInvasao(Invasao inv, string texto)
	{
		foreach (ServerPlayer o in ZoneList(inv.Zona.Hash)) if (o.Peer != null) Avisar(o, texto);
		EscutaDaConquista?.Add(texto);
	}

	// =====================================================================
	// ARRANCAR A BANDEIRA -- a CONTESTACAO
	// =====================================================================
	/// <summary>
	/// ============================ QUALQUER UM MENOS O INVASOR ============================
	/// E o que da vida ao sistema: uma invasao nao e um evento de PvE resolvido a sos. Oito segundos
	/// de canal, quebrado por MOVIMENTO ou por DANO -- quem quiser arrancar tem que chegar ao lugar,
	/// PARAR, e ficar de pe diante de um invasor que sabe exatamente o que ele esta fazendo.
	///
	/// Vale contra a IA nova de graca: os defensores nao ligam pra bandeira (eles cacam o invasor, que
	/// e quem os agrediu), entao um terceiro pode arrancar no meio da luta -- que e exatamente a cena
	/// que o DM descreve.
	/// ================================================================================
	/// </summary>
	private void ConquistaArrancar(ServerPlayer pl)
	{
		if (pl.Ficha.dead || pl.Ficha.KO) { Avisar(pl, "não dá pra arrancar nada de joelhos."); return; }
		if (_arranques.ContainsKey(pl.Id)) { Avisar(pl, "você já está puxando a bandeira."); return; }

		if (InvasaoAqui(pl.Zone) is not { } inv || ChaveDePlaneta.Da(pl.Zone) is not { } chave)
		{ Avisar(pl, "não há bandeira de invasão aqui."); return; }

		if (inv.Invasor == pl.Id)
		{ Avisar(pl, "sua invasão está em andamento -- defenda a bandeira."); return; }

		if (Math.Abs(inv.Bandeira.X - pl.Pos.X) > AlcanceDeUso || Math.Abs(inv.Bandeira.Y - pl.Pos.Y) > AlcanceDeUso)
		{ Avisar(pl, "chegue mais perto da bandeira de invasão."); return; }

		_arranques[pl.Id] = new Arranque
		{
			Quem = pl.Id,
			Onde = chave,
			De = pl.Pos,
			RancorNoInicio = pl.RancorAte,
		};

		AnunciarNaZonaDaInvasao(inv, $"{pl.Name} agarra a bandeira de invasão e começa a arrancá-la do chão!");
	}

	/// <summary>
	/// O CANAL, no tique CHEIO e nao no de 1 Hz -- e aqui este port e MAIS DURO que o original de
	/// proposito.
	///
	/// O DM confere de segundo em segundo (`sleep(10)`, :290) e compara TURF: dar um passo e voltar
	/// dentro do mesmo segundo nao cancelaria nada. A 30 Hz nao ha essa janela. O aviso de progresso
	/// continua de segundo em segundo (o `i % 3` do DM virou um por segundo) -- trinta linhas por
	/// segundo no chat seriam o contrario de informar.
	/// </summary>
	private void TickDoArranque(double dt)
	{
		if (_arranques.Count == 0) return;

		foreach (Arranque a in _arranques.Values.ToList())
		{
			if (!_players.TryGetValue(a.Quem, out ServerPlayer? m) || m.Ficha.dead || m.Ficha.KO)
			{ _arranques.Remove(a.Quem); continue; }

			if (!_invasoes.TryGetValue(a.Onde, out Invasao? inv))
			{ _arranques.Remove(a.Quem); continue; }

			// MOVIMENTO: meio tile de folga, porque aqui a posicao e continua e nao uma grade -- o
			// mesmo argumento do `Contato` de 48 px na absorcao do Cell.
			if ((m.Pos - a.De).LengthSquared > 16 * 16)
			{
				Avisar(m, "você se moveu -- a bandeira continua fincada.");
				_arranques.Remove(a.Quem);
				continue;
			}

			// DANO: o `combatTagExpire` mudou. Aqui o campo e o `RancorAte`, escrito pelo mesmo funil
			// (`MarcarAgressao`) -- ou seja, tomar UM golpe derruba o canal.
			if (m.RancorAte != a.RancorNoInicio)
			{
				Avisar(m, "o combate interrompe o arranque!");
				_arranques.Remove(a.Quem);
				continue;
			}

			a.Faltam -= dt;
			if (a.Faltam <= a.ProximoAviso)
			{
				a.ProximoAviso = Math.Floor(a.Faltam);
				if (a.Faltam > 0)
					Avisar(m, $"a bandeira cede... ({Conquista.SegundosDeArranque - a.Faltam:0}/"
							+ $"{Conquista.SegundosDeArranque:0}s)");
			}
			if (a.Faltam > 0) continue;

			// ARRANCOU. O fracasso em si e do `TickDasInvasoes` (uma casa so pra terminar campanha).
			_arranques.Remove(a.Quem);
			inv.Arrancada = true;

			// O POVO AGRADECE QUEM DEFENDEU UM MUNDO SEM DONO (:309). Guarnicao de OCUPANTE nao:
			// arrancar a bandeira ali e favor ao ocupante, e nao ao povo.
			if (inv.TemPovo && inv.Nativa)
				SomarReputacao(inv.Planeta, m, Conquista.RepPorArrancarBandeira, "defendeu-a-invasao");

			AnunciarConquista($"{m.Name} ARRANCA a bandeira de invasão de {NomeVisivel(inv.Planeta)}! "
							+ "A conquista fracassa!");
		}
	}

	// =====================================================================
	// A MORTE DE UM CORPO SEM DONO
	// =====================================================================
	/// <summary>
	/// ============================ MATAR CUSTA; NOCAUTEAR NAO ============================
	/// Chamado do MESMO lugar que ja tira um NPC morto do mundo (o `_npcsPraTirar` do `TickCombate`),
	/// que e o unico ponto por onde toda morte de corpo sem dono passa. Um segundo caminho aqui seria
	/// repetir o erro que este port ja nomeou como o mais caro dele.
	///
	/// Sao DOIS ganchos, e a ordem entre eles e a regra do DM:
	///   * DEFENSOR de invasao NATIVA tem delta proprio (-6, ou -15 pro lider) e **pula** o generico --
	///     e o `rep_death_counted = 1` do `Defender.mobDeath` (:627), com o comentario dizendo
	///     exatamente isso. Guarnicao de OCUPANTE (`Nativa` falsa) nao custa NADA: matar o capanga de
	///     um tirano nao ofende o povo;
	///   * qualquer outro CIDADAO cai no generico (-10, `REP_KILL_CITIZEN`), que era o item 1 do
	///     <see cref="Reputacao.OqueFalta"/> e que a conquista obrigou a existir -- o defensor so pode
	///     "pular o generico" se o generico existir.
	///
	/// SO O `UltimoAgressor` COM DONO CONTA, e so dentro do prazo de rancor -- e o
	/// `(C.IsInFight || world.time &lt;= C.combatTagExpire) ? C.lastDamager : null` (PlanetReputation.dm:114),
	/// com o motivo escrito la: sem isso, um chefe de saga massacrando cidadaos empurraria a culpa pro
	/// jogador que por acaso treinou com aquele cidadao ontem.
	/// ================================================================================
	/// </summary>
	private void MorreuUmCorpoSemDono(ServerPlayer morto)
	{
		if (morto.Papel == null) return;

		ServerPlayer? matador = NowMs() <= morto.RancorAte
			&& _players.TryGetValue(morto.UltimoAgressor, out ServerPlayer? k)
			&& EhJogador(k) && !k.Ficha.dead ? k : null;

		// --- 1. era um defensor de invasao?
		foreach (Invasao inv in _invasoes.Values)
		{
			if (!inv.Defensores.TryGetValue(morto.Id, out bool lider)) continue;

			inv.Defensores.Remove(morto.Id);
			inv.Mortos++;

			if (inv.Nativa && inv.TemPovo && matador != null)
				SomarReputacao(inv.Planeta, matador,
					lider ? Conquista.RepPorMatarLider : Conquista.RepPorMatarDefensor, "matou-defensor");
			return;
		}

		// --- 2. era um habitante comum -> o gancho generico do `PlanetReputation.dm:113`
		if (morto.Papel.Tipo != TipoDeNpc.Cidadao || matador == null) return;

		string planeta = morto.Berco.Natal.Length > 0 ? morto.Berco.Natal : morto.Zone.Name;
		SomarReputacao(planeta, matador, Conquista.RepPorMatarHabitante, "matou-habitante");

		// E A ALMA PAGA JUNTO. No original as duas contas saem da MESMA linha
		// (`lose_npc_kill_karma`, pendurado no `PlanetReputation.dm:113`) e sao coisas diferentes: a
		// reputacao e o que ESTE planeta pensa de voce e some quando voce muda de mundo; o karma e o
		// que a sua alma virou e te segue ate a mesa do Enma. Ver `GameServer.Karma.cs`.
		KarmaPorMatarHabitante(matador);
	}
}
