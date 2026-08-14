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
	/// MORTO NAO E PRENSADO: o `if(r > 1 && (!dead || KeepsBody))` do DM. Um cadaver ja nao anda por
	/// outra regra, e deixar a prisao valer sobre ele so faria a checagem mentir na ficha.
	/// </summary>
	private static bool PrensadoPelaGravidade(ServerPlayer pl) =>
		!pl.Ficha.dead && Esmagamento.Prende(pl.Ficha);

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

			// MORTO NAO E ESMAGADO (`!dead || KeepsBody`). Nem quem esta com a razao dentro do
			// limite -- que e a esmagadora maioria dos corpos do servidor, e por isso o teste barato
			// vem primeiro.
			if (f.dead || !Esmagamento.Esmaga(f)) { _avisoDeEsmagamento.Remove(pl.Id); continue; }

			double r = Esmagamento.Razao(f);

			// O DANO VAI PELO FUNIL DE SEMPRE (`EspalharDanoG3`, o `SpreadDamage` deste port): o
			// mesmo valor em cada membro, LETAL -- e `DamageMe(damage, 0)` do DM, com o `nonlethal`
			// em zero. Nao ha um caminho de dano proprio pro esmagamento, e nao pode haver: e o
			// funil que sabe nocautear quem perdeu um membro vital e matar quem continuou no chao.
			//
			// AUTOR = VITIMA porque nao ha algoz. O funil trata esse caso (a Final Explosion o usa) e
			// manda a derrota pro `ZenkaiPorDerrota` em vez de enfurecer contra ninguem.
			double dano = Esmagamento.DanoPorSegundo(f);
			if (dano > 0) EspalharDanoG3(pl, pl, dano, letal: true);

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
			// ===================================================================================
			if (r >= Esmagamento.RazaoQuePrende && f.HP <= 5 && !f.dead)
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

		if (razao >= Esmagamento.RazaoQuePrende)
		{
			Avisar(pl, porPeso
				? "O PESO ESTÁ ESMAGANDO SEU CORPO! Você não consegue nem se levantar. "
				  + "Tire os pesos AGORA (menu P, aba Tech) ou ele não vai aguentar."
				: "A GRAVIDADE ESTÁ ESMAGANDO SEU CORPO! Você não consegue dar um passo. "
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
}
