using Godot;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// O CASTIGO DA GRAVIDADE E DO PESO -- porte do `Grav_Handler()` (`Gravity.dm:41-72`).
///
/// ============================ O QUE FALTAVA, E POR QUE ISSO IMPORTA ============================
/// O port ja pagava o PREMIO da gravidade alta (`Fighter.GravGain` roda todo tique e escala com a
/// gravidade absoluta) e nao cobrava NADA por ela. A unica coisa que a gravidade tirava era o
/// `gravFelt` -- uma reducao no poder EXPRESSO, visivel so no scouter. Resultado: um planeta de
/// gravidade 80 era ganho de graca, e "onde eu treino?" tinha uma resposta so.
///
/// Com o esmagamento ligado, gravidade alta e uma APOSTA: rende muito e machuca o tempo todo, e a
/// maestria (`GravMastered`, que so sobe encarando gravidade acima dela) e o que transforma o
/// castigo em premio. Que e literalmente a camara de gravidade do Vegeta no anime.
/// ============================================================================================
///
/// ============================ E O PESO ESMAGA PELA MESMA REGRA ============================
/// `r = max(gravidade/maestria, weight_ratio)`. O peso ja embute a gravidade local, entao vestir
/// 100% de peso num chao 10x e a mesma conta -- e a pior das duas razoes manda. Antes disto, peso
/// rendia ate 8x de BP e nao custava um passo: usar o maximo era decisao obvia.
/// ======================================================================================
///
/// AS TRES SAIDAS DO CASTIGO, e cada uma mora onde ja havia um funil pra ela:
///
///   * DANO e DRENO DE FOLEGO -- aqui, uma vez por segundo (o `world.time >= gravcrush_dmg_next`
///     do DM, que e um throttle de 1 s justamente porque o `Grav` e chamado em taxas diferentes);
///   * LENTIDAO -- no `RecalcularVelocidade`, via `Esmagamento.FatorDePasso`, porque velocidade
///     tem um dono so neste port e ele precisa valer nas duas pontas;
///   * PRISAO NO CHAO (razao >= 4) -- no `PodeMexerOCorpo`, que e o funil de VETOR (o mesmo do
///     nocaute, da tecla C e da paralisia de tecnica), pra a regra valer tambem pra IA.
///     (2026-10-23) O JOGADOR preso -- e o desmaiado por este castigo -- se ARRASTA a 1/4 (dono): ver `GameServer.Rastejo.cs`.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// `GRAVCRUSH_WARN_CD = 100` ticks = 10 segundos entre avisos no chat.
	///
	/// O aviso e metade do sistema: um corpo perdendo vida sem nada na tela dizendo por que e
	/// indistinguivel de bug. Mas repetido por segundo ele vira spam e o jogador para de ler -- que
	/// da no mesmo. Dez segundos e o numero do DM.
	/// </summary>
	private const double SegundosEntreAvisosDeEsmagamento = 10;

	/// <summary>Quanto falta pro proximo aviso de cada corpo. Ver <see cref="SegundosEntreAvisosDeEsmagamento"/>.</summary>
	private readonly Dictionary<int, double> _avisoDeEsmagamento = [];

	/// <summary>
	/// ESTE CORPO ESTA PRENSADO NO CHAO? -- entra no <see cref="PodeMexerOCorpo"/>.
	///
	/// A pergunta e do Core e e derivada do estado atual (<see cref="Esmagamento.Prende"/>): nao ha
	/// bit guardado, entao nao ha o que vazar. Sair do planeta, tirar o peso ou subir a maestria
	/// solta o corpo no mesmo tique -- sem ninguem precisar lembrar de apagar nada, que e o defeito
	/// que o proprio DM tem que remendar a mao (`if(testgrav != 3) gravParalysis = 0`).
	///
	/// QUEM O CHAO PRENDE E QUEM ELE ESMAGA: <see cref="SenteOPesoDoChao"/> -- desde 2026-10-23, o morto de pe tambem.
	/// </summary>
	private static bool PrensadoPelaGravidade(ServerPlayer pl) =>
		SenteOPesoDoChao(pl) && Esmagamento.Prende(pl.Ficha);

	/// <summary>
	/// ESTE CORPO ESTA SENDO ESMAGADO AGORA? -- a pergunta que o <see cref="TickDoEsmagamento"/> faz antes de ferir: acima do limite
	/// (<see cref="Esmagamento.Esmaga"/>) e um corpo que o chao pega (<see cref="SenteOPesoDoChao"/>, a mesma do <see cref="PrensadoPelaGravidade"/>).
	///
	/// (2026-10-19) NUMA CASA SO porque ganhou um segundo leitor: o Sleep do Majin recusa o corpo esmagado, e acorda quem passa a ser esmagado
	/// dormindo (`GameServer.SonoDoMajin.cs`) -- o corpo que o castigo fere e o corpo que nao dorme, e duas copias da conta divergiriam no dia
	/// em que alguem mexesse numa.
	/// </summary>
	private static bool EsmagadoAgora(ServerPlayer pl) =>
		SenteOPesoDoChao(pl) && Esmagamento.Esmaga(pl.Ficha);

	/// <summary>
	/// QUEM O PESO E A GRAVIDADE PEGAM: o vivo, o MORTO DE PE e quem ficou com o corpo (`KeepsBody`) -- todo corpo, menos o cadaver dos 2 s.
	///
	/// ============================ O MORTO SENTE O CHAO COMO O VIVO (dono, 2026-10-23) ============================
	/// *"faça com q morto sofra dano de peso e gravidade, isso virou meta do jogo"* -- e, perguntado, "Tudo, inclusive o 10x do Alem".
	/// Ate aqui valia o `if(r > 1 &amp;&amp; (!dead || KeepsBody))` do DM (`Gravity.dm:47`): o morto nao era prensado nem ferido, e com o vigor
	/// travado no cheio (`Nutricao.VigorSempreCheio`, 2026-10-15) ele treinava horas com o peso no teto onde o vivo para em segundos (os 20
	/// bilhoes de 2026-10-20 eram de um morto). Agora o fantasma do Outro Mundo, o do passe do Enma, o de passagem e o da mente (o
	/// `MortoDePe`) sentem o chao em todo lugar como o vivo: o aperto a partir da razao 4, o dano, e o corpo em farrapos que se desfaz --
	/// e a "morte" dele e a de sempre do morto: curado, de volta a mesa do Enma, em coma curto (`MorrerDeNovoNoAlem`). O Inferno e as
	/// ilhas do Outro Mundo puxam 10x: a alma que domina pouca gravidade fica presa e se machuca la ate dominar (a maestria sobe a cada
	/// passe, `GravGain`) -- ou ate a pena acabar, ou ate ela morrer de novo e acordar na mesa. DIVERGENCIA DECLARADA DO DM. O vigor do morto
	/// nao mudou: continua cheio (o dreno do esmagamento volta no mesmo tique, `Nutricao.ManterOsInfinitos`).
	///
	/// O CADAVER DOS 2 s FICA DE FORA, e nao e isencao: e o corpo no chao antes da viagem, que nao anda nem treina -- e o funil de dano ja o
	/// recusa (`EspalharDanoG3`, `EhCadaver`). Quem ficou com o corpo (`KeepsBody`, `GameServer.Tecnicas.G8.ManterOCorpoG8`) continua como no
	/// DM, o cadaver que fica entre os vivos: *"dead people can no longer abuse not dying for grav gains without an actual body"*.
	/// ===========================================================================================================
	/// </summary>
	private static bool SenteOPesoDoChao(ServerPlayer pl) => OCorpoSeDesfazNoChao(pl) || pl.Ficha.KeepsBody;

	/// <summary>
	/// QUEM O CORPO EM FARRAPOS DESFAZ (a ultima saida do <see cref="TickDoEsmagamento"/>): quem tem uma morte a sofrer -- o vivo, que morre, e
	/// o MORTO DE PE, que "morre de novo" (curado, de volta a mesa do Enma). O cadaver -- o dos 2 s e o que ficou com o corpo -- nao: nele o
	/// `EstourarG2` so o deixaria desmaiado e morto ao mesmo tempo.
	/// </summary>
	private static bool OCorpoSeDesfazNoChao(ServerPlayer pl) => !pl.Ficha.dead || (pl.MortoDePe && !MortoSemPesoDeTeste);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--gravmetateste`): o morto de pe volta a nao sentir o chao -- o `(!dead || KeepsBody)` do DM, o jogo de antes
	/// de 2026-10-23. Lido pelo <see cref="OCorpoSeDesfazNoChao"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool MortoSemPesoDeTeste;

	/// <summary>
	/// UMA VEZ POR SEGUNDO -- chamado do bloco de 1 Hz do <see cref="Tick"/>.
	///
	/// A CADENCIA E A REGRA, e nao arredondamento: as constantes do DM sao **por segundo**
	/// (`GRAVCRUSH_DMG_BASE` e dano/seg, `stamina -= maxstamina*0.002*r` idem). Rodar isto no tique
	/// cheio multiplicaria o castigo por trinta e mataria qualquer um em gravidade 4x em segundos --
	/// a mesma armadilha que o `TickDoEstomago` ja documenta.
	/// </summary>
	private void TickDoEsmagamento()
	{
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			Fighter f = pl.Ficha;

			// O CADAVER DOS 2 s NAO E ESMAGADO -- o morto de pe e (2026-10-23, dono) e quem ficou com o corpo tambem: ver
			// `SenteOPesoDoChao`, a mesma pergunta que o `PrensadoPelaGravidade` faz pro andar. Nem quem esta com a razao dentro do
			// limite -- que e a esmagadora maioria dos corpos do servidor, e por isso o teste barato vem primeiro. A conta mora no
			// `EsmagadoAgora`.
			if (!EsmagadoAgora(pl))
			{
				_avisoDeEsmagamento.Remove(pl.Id);
				continue;
			}

			double r = Esmagamento.Razao(f);

			// O DANO VAI PELO FUNIL DE SEMPRE (`EspalharDanoG3`, o `SpreadDamage` deste port): o
			// mesmo valor em cada membro, LETAL -- e `DamageMe(damage, 0)` do DM, com o `nonlethal`
			// em zero. Nao ha um caminho de dano proprio pro esmagamento, e nao pode haver: e o
			// funil que sabe nocautear quem perdeu um membro vital e matar quem continuou no chao.
			//
			// AUTOR = VITIMA porque nao ha algoz. O funil trata esse caso (a Final Explosion o usa) e
			// manda a derrota pro `ZenkaiPorDerrota` em vez de enfurecer contra ninguem.
			double dano = Esmagamento.DanoPorSegundo(f);
			// (2026-10-23) O DESMAIO QUE ESTE DANO FIZER E DA GRAVIDADE: quem cai AQUI se arrasta ate levantar (dono) -- e so quem estava
			// de pe: o que ja estava no chao continua desmaiado pelo motivo de antes. Ver `GameServer.Rastejo.cs`.
			bool dePe = !f.KO;
			// (2026-10-28, o pedido 143) ACIMA DE 5x A MAESTRIA O NOCAUTE E NA HORA -- e o dano de 20/s zera o corpo caido em cinco segundos.
			// Pelo `Blindado`, a mesma pergunta do funil sem algoz: o corpo que o dano nao alcanca nao cai. E so em quem tem uma morte a
			// sofrer (`OCorpoSeDesfazNoChao`): o cadaver que ficou com o corpo (`KeepsBody`) o funil de dano ja recusa, e cair nao e com ele.
			// Ver `Esmagamento.NocauteiaNaHora`.
			if (dePe && Esmagamento.NocauteiaNaHora(f) && OCorpoSeDesfazNoChao(pl) && pl.Combate is { Blindado: false } preso)
				preso.Nocautear(Jandirus.Core.Combat.MeleeResolver.TetoDoNocaute, porVital: false);
			// (2026-10-28, o pedido 143) e a gravidade MATA quem regenera (o coma e o "se desfazer" nao valem aqui): o dono, "Todas essas tambem"
			bool mortoAntes = f.dead, porPeso = Esmagamento.PorPeso(f);
			Jandirus.Core.World.ZoneKey zonaAntes = pl.Zone;
			if (dano > 0) EspalharDanoG3(pl, pl, dano, letal: true, mataQuemRegenera: !GravidadePoupaQuemRegeneraDeTeste);
			// O CORPO QUE ESTE DANO MATOU (o vivo virou cadaver; o morto de pe "morreu de novo" e acordou na mesa do Enma) ja nao esta aqui:
			// le a causa (a frase do corpo que se desfaz, logo abaixo -- com os degraus, o dano chega antes dele) e nada mais de dreno, de
			// estouro nem de aviso de esmagamento pra quem acabou de sair do chao pesado.
			if (f.dead != mortoAntes || !pl.Zone.Equals(zonaAntes))
			{
				Avisar(pl, porPeso ? "o peso vence o que restava do seu corpo." : "a gravidade vence o que restava do seu corpo.");
				_avisoDeEsmagamento.Remove(pl.Id);
				continue;
			}
			if (dePe && f.KO) MarcarODesmaioDaGravidade(pl);

			f.stamina = Math.Max(0, f.stamina - Esmagamento.DrenoDeVigorPorSegundo(f));

			// ============================ O CORPO EM FARRAPOS SE DESFAZ ============================
			// `if(r >= GRAVCRUSH_EXPLODE_R && HP <= 5 && !dead) spawn Body_Parts()`. As DUAS
			// condicoes juntas: muito alem do limite E ja em pedacos. Nao e uma morte por gravidade
			// alta -- e o fim de quem ficou caido no chao pesado depois de o dano ja ter feito o
			// trabalho, e o teto de 3/seg existe justamente pra dar tempo de alguem chegar antes.
			//
			// REUSA O `EstourarG2`, que e o porte do `Body_Parts()` feito pro Kaio-ken. E o MESMO
			// evento do original chamado por outra causa; um segundo "estourar" seria a mesma regra
			// escrita duas vezes, e a segunda envelhece calada.
			//
			// (2026-10-23) O `!dead` DO DM VIROU "quem tem uma morte a sofrer": o morto de pe tambem se desfaz, e a morte dele e a volta
			// a mesa do Enma (dono) -- ver `OCorpoSeDesfazNoChao`.
			// ===================================================================================
			if (r >= Esmagamento.RazaoQuePrende && f.HP <= 5 && OCorpoSeDesfazNoChao(pl))
			{
				EstourarG2(pl, Esmagamento.PorPeso(f)
					? "o peso vence o que restava do seu corpo."
					: "a gravidade vence o que restava do seu corpo.");
				_avisoDeEsmagamento.Remove(pl.Id);
				continue;
			}

			AvisarDoEsmagamento(pl, r);
		}
	}

	/// <summary>
	/// O AVISO, e ele diz O QUE AFROUXAR.
	///
	/// As tres frases sao as tres do DM (`Gravity.dm:58-62`) e a diferenca entre elas nao e enfeite:
	/// quem esta sendo esmagado pelo PESO resolve com um verbo (tirar os pesos) e quem esta sendo
	/// esmagado pela GRAVIDADE resolve com uma viagem. Um aviso unico ("seu corpo range") faria o
	/// jogador tentar a saida errada -- e, com o dreno de folego correndo, ele nao tem muitas
	/// tentativas.
	/// </summary>
	private void AvisarDoEsmagamento(ServerPlayer pl, double razao)
	{
		if (pl.Peer == null && pl.Cerebro == null) return;   // NPC sem tela nao le chat

		double falta = _avisoDeEsmagamento.GetValueOrDefault(pl.Id) - 1;
		if (falta > 0) { _avisoDeEsmagamento[pl.Id] = falta; return; }
		_avisoDeEsmagamento[pl.Id] = SegundosEntreAvisosDeEsmagamento;

		Fighter f = pl.Ficha;
		bool porPeso = Esmagamento.PorPeso(f);

		// (2026-10-23) "MAL CONSEGUE SE ARRASTAR", e nao mais "nao consegue dar um passo": preso, o corpo se arrasta a 1/4 (dono) --
		// ver `GameServer.Rastejo.cs`. A frase diz a saida, e agora a saida e andando.
		if (razao >= Esmagamento.RazaoQuePrende)
		{
			Avisar(pl, porPeso
				? "O PESO ESTÁ ESMAGANDO SEU CORPO! Você mal consegue se arrastar. "
				  + "Tire os pesos AGORA (menu P, aba Tech) ou ele não vai aguentar."
				: "A GRAVIDADE ESTÁ ESMAGANDO SEU CORPO! Você mal consegue se arrastar. "
				  + "Saia daqui AGORA ou ele não vai aguentar.");
			return;
		}

		Avisar(pl, porPeso
			? $"seu corpo range sob o PESO... ({razao:0.#}x o limite do seu corpo nesta gravidade)"
			: $"seu corpo range sob a gravidade... ({Esmagamento.Gravidade(f):0.#}x contra "
			  + $"{f.GravMastered:0.#}x que você domina)");
	}

	/// <summary>O corpo foi embora: esquece o relogio do aviso dele. Ver `EsquecerParalisia`.</summary>
	private void EsquecerEsmagamento(int id) => _avisoDeEsmagamento.Remove(id);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--gravmetateste`, familia M): a gravidade volta a poupar quem regenera -- o coma, e o Majin que se desfaz
	/// e volta, o jogo de antes do pedido 143. Falso em jogo, sempre.
	/// </summary>
	internal static bool GravidadePoupaQuemRegeneraDeTeste;
}
