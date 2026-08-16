using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ BANCADA VIVA DA CURA -- `--curaviva` ============================
/// A bancada `cura` (Tools/AssetPipeline) mede o Core: as taxas, os prazos, o eixo do genoma. Ela
/// **nao alcanca metade do pedido do dono**, e a metade que falta e literal:
///
///   *"um NAMEK com a skill ATIVA dele pode CURAR O MEMBRO MAIS FERIDO dele ou REGENERAR UM MEMBRO
///   INTEIRO PERDIDO gastando ENORMES QUANTIDADES DE ENERGIA"*.
///
/// A ativa nao mora no Core: ela e `GameServer.Raciais.Regenerar`, mora no servidor, e depende de
/// tres coisas que so existem depois do boot -- o catalogo de skills (pro gate de quem COMPROU
/// `Regenerate`), o `races.json` carregado (pro eixo de regeneracao do corpo) e um `ServerPlayer` de
/// verdade no `_players` (pro `Avisar` e pro `AjustarGanhoDoRabo`). Medir isso no Core exigiria
/// copiar a funcao pra dentro da bancada, que e exatamente o atalho que deixa a copia verde no dia
/// em que a producao muda.
///
/// ============================ E ELA MEDE A PASSIVA PELO FUNIL DO SERVIDOR ============================
/// A familia 4 nao repete o que a bancada `cura` ja diz. La quem e chamado e `Regeneracao.Tique`;
/// aqui quem e chamado e **`RegenerarPassivo`**, que e onde moravam as tres guardas que sairam
/// (`KO`, `EmCombate`, `HP >= 99.99`). Uma delas voltar -- por merge, por descuido, por "isso aqui
/// parece que faltou" -- deixaria a bancada do Core verde e o jogo errado. Esta familia e o cadeado
/// daquele arquivo.
///
/// ============================ POR QUE HA UMA FAMILIA 6 ============================
/// "Ninguem cura em combate" fica verde de graca se o laco da bancada nao curar NADA -- um erro de
/// cadencia, um `dt` zerado, um corpo sem `Combate` montado. A familia 6 injeta o EIXO DO MAJIN
/// dentro do corpo do Namekuseijin e exige que ele passe a curar no mesmo laco: e o que separa
/// *"nao curou porque a regra proibe"* de *"nao curou porque nada aqui cura"*.
///
/// ============================ COMO CADA FAMILIA REPROVA -- MEDIDO, NAO SUPOSTO ============================
/// O placar limpo e **43 OK, 0 FALHAS**. Os defeitos abaixo foram postos no CODIGO DE PRODUCAO, um
/// por vez, com `dotnet build` no meio, e desfeitos depois.
///
///  * **O GATE ANTIGO** (`PodeRegenerar` voltando a perguntar `canheallopped`) -> **31 OK, 12
///    FALHAS**, e o estrago e nos DOIS sentidos, que e mais do que se supunha: Majin e Bio-Androide
///    passam a regenerar de proposito (**"seu Braco esquerdo volta a crescer"** num corpo que nao
///    deveria ter o botao) **e o Namekuseijin PERDE a habilidade inteira** -- porque depois do rework
///    do eixo o `DeathRegen` dele e ZERO. O atalho nao dava so a habilidade a quem nao devia: tirava
///    do dono dela. As familias 1, 2, 3 e 4 caem juntas, e e o unico defeito do arquivo que faz isso.
///
///  * **`RegenCustoKi = 0`** -> **41 OK, 2 FALHAS**: "cobra exatamente 70% do Ki maximo" (gastou
///    0,000) e a recusa com o tanque em 69%. **A cura continua acontecendo** -- e por isso que "a
///    ativa cura" sozinho nunca foi uma prova: e o preco que separa uma habilidade de um botao.
///
///  * **SEM A PREFERENCIA PELO DECEPADO** (`alvo` comecando nulo, so o mais ferido) -> **37 OK, 6
///    FALHAS**: o braco arrancado nao volta, a mao nao volta, e a perna arranhada e curada no lugar
///    -- 70% do Ki gastos e o corpo continua manco. E o desperdicio que o DM evita por ordem de
///    busca, e ele nao aparece em nenhuma medicao de "a ativa cura?".
///
///  * (no vizinho `--cidadeteste`, a outra metade da frase do dono) **`SegundosDoRegeneradorPorMembro
///    = 30`** -> **27 OK, 2 FALHAS**; **a maquina parando de devolver membro** -> **27 OK, 2 FALHAS**.
///
///  * E os dois do Core (`cura`), que esta bancada NAO pega e nem deveria: apagar o ramo do
///    Namekuseijin em `PerfilDeRegen.De` -> aqui **39 OK, 4 FALHAS**; tirar o `!emCombate ||` do
///    `PodeCurar` -> aqui **36 OK, 7 FALHAS**. As duas metades enxergam o mesmo defeito por caminhos
///    diferentes, e e por isso que as duas existem.
/// =======================================================================================================
///
///     Godot --headless --path . --server --rede 7906 --curaviva
/// </summary>
public sealed partial class GameServer
{
	private int _curaOk, _curaFalhou;

	/// <summary>Faixa de ids so desta bancada -- entra e sai dentro do mesmo bloco sincrono.</summary>
	private const int IdBaseDaCuraViva = 991_000;

	private void AfirmarCura(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _curaOk++; GD.Print($"[curaviva]   OK    {oque}"); return; }
		_curaFalhou++;
		GD.PrintErr($"[curaviva]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaCuraViva()
	{
		_curaOk = 0; _curaFalhou = 0;
		GD.Print("\n===== BANCADA VIVA DA CURA (--curaviva) =====\n");

		CvAtivaCuraECusta();
		CvAtivaDevolveMembro();
		CvQuemTemAAtiva();
		CvPassivaPeloFunilDoServidor();
		CvMajinRefazPeloFunil();
		CvABancadaSeCobra();

		GD.Print($"\n[curaviva] ===== {_curaOk} OK, {_curaFalhou} FALHA(S) =====\n");
	}

	// =====================================================================
	// 1. A ATIVA CURA -- E CUSTA
	// =====================================================================
	/// <summary>
	/// ============================ AS DUAS METADES, E A SEGUNDA E A QUE IMPORTA ============================
	/// "A ativa cura" sozinho seria uma afirmacao boba: qualquer botao que zera ferimento cura. O que
	/// o dono descreveu foi um PRECO -- *"gastando ENORMES QUANTIDADES DE ENERGIA"* --, e preco so se
	/// prova medindo o tanque antes e depois.
	///
	/// O numero e do original: `#define NAMEK_REGEN_KI_COST 0.7` (`namekian.dm:155`), cobrado como
	/// `usr.MaxKi * 0.7` (`:170`). **Do MAXIMO**, e a igualdade e afirmada com folga de 1e-6 de
	/// proposito: e ela que faz esta familia ficar vermelha se alguem "arredondar" a constante.
	///
	/// E o alvo tambem e afirmado, e nao so o efeito: fere-se DOIS membros em profundidades
	/// diferentes e exige-se que o pior suba a 100% e que **o outro nao seja tocado**. Uma
	/// implementacao que curasse o corpo inteiro passaria no teste do Ki e estaria errada -- e teria
	/// dado ao Namekuseijin uma cura geral que o DM nao da.
	/// ==================================================================================================
	/// </summary>
	private void CvAtivaCuraECusta()
	{
		GD.Print("--- 1. A ATIVA DO NAMEK: cura o MEMBRO MAIS FERIDO, e COBRA 70% do Ki maximo ---");

		ServerPlayer pl = ForjarCorpo(1, "bancada_namek", "Namekian");
		try
		{
			BodyPart perna = pl.Combate.Corpo.Achar("Perna direita")!;
			BodyPart braco = pl.Combate.Corpo.Achar("Braco esquerdo")!;

			// PELO FUNIL DO DANO (`Ferir`), e nao escrevendo `Vida`: e o `Ferir` que decide o piso do
			// nao-letal e quem marca o membro como quebrado.
			pl.Combate.Corpo.Ferir(perna, perna.VidaMax * 0.90, letal: true);   // o PIOR
			pl.Combate.Corpo.Ferir(braco, braco.VidaMax * 0.40, letal: true);   // o outro
			pl.Combate.SincronizarVida();

			double kiAntes = pl.Ficha.Ki, maxKi = pl.Ficha.MaxKi;
			double bracoAntes = braco.Vida;

			AfirmarCura("PRECONDICAO: dois membros feridos, e a perna esta pior que o braco",
						perna.Fracao < braco.Fracao && perna.Fracao < 0.2, $"perna {perna.Fracao:0.00}");

			// PELO CANAL DE VERDADE: o mesmo `UsarHabilidade` que o pacote do cliente entrega.
			// Chamar `Regenerar` direto pularia o dispatch, que e onde uma habilidade nova se perde.
			UsarHabilidade(pl, "regenerar");

			double gasto = kiAntes - pl.Ficha.Ki;
			GD.Print($"  Ki: {kiAntes:0} -> {pl.Ficha.Ki:0} de {maxKi:0} (gasto {gasto:0}, "
					 + $"{gasto / maxKi * 100:0.0}% do maximo) | perna {perna.Vida:0}/{perna.VidaMax:0}");

			AfirmarCura("a ativa cura o MEMBRO MAIS FERIDO ate o cheio",
						perna.Vida >= perna.VidaMax - 1e-6, $"{perna.Vida:0.0}");
			AfirmarCura("...e NAO toca o outro membro ferido (nao e cura geral)",
						Math.Abs(braco.Vida - bracoAntes) < 1e-9, $"{braco.Vida:0.0} x {bracoAntes:0.0}");
			AfirmarCura("...e COBRA exatamente 70% do Ki MAXIMO (`namekian.dm:155/170`)",
						Math.Abs(gasto - maxKi * 0.7) < 1e-6, $"gastou {gasto:0.000}, esperado {maxKi * 0.7:0.000}");

			// ---- A RECARGA: `NAMEK_REGEN_CD 100` = 10 s (`namekian.dm:156`) ----------------
			pl.Combate.Corpo.Ferir(braco, braco.VidaMax * 0.50, letal: true);
			pl.Combate.SincronizarVida();
			double kiDepois = pl.Ficha.Ki = pl.Ficha.MaxKi;   // tanque cheio de novo: so a recarga barra
			double bracoDoido = braco.Vida;

			List<string>? escutaAntes = EscutaDeAvisos;
			EscutaDeAvisos = [];
			UsarHabilidade(pl, "regenerar");
			string disse = string.Join(" | ", EscutaDeAvisos);
			EscutaDeAvisos = escutaAntes;

			AfirmarCura("a SEGUNDA vez seguida e recusada pela recarga, e nao cura nada",
						Math.Abs(braco.Vida - bracoDoido) < 1e-9 && disse.Contains("ainda nao"), disse);
			AfirmarCura("...e a recusa NAO cobra Ki (recusa cara seria pior que nao ter o botao)",
						Math.Abs(pl.Ficha.Ki - kiDepois) < 1e-9, $"{pl.Ficha.Ki:0.0} x {kiDepois:0.0}");
			AfirmarCura("...e a recarga marcada e de ~10 s (`NAMEK_REGEN_CD 100`)",
						pl.RegenLivreEm - NowMs() is > 8_000 and <= 10_000,
						$"{(pl.RegenLivreEm - NowMs()) / 1000.0:0.0} s");

			// ---- SEM KI NAO HA CURA: o preco e um portao, e nao um enfeite -----------------
			pl.RegenLivreEm = 0;                       // a recarga ja foi provada acima; aqui mede-se o CUSTO
			pl.Ficha.Ki = pl.Ficha.MaxKi * 0.69;       // um fio abaixo do preco
			double vidaMagra = braco.Vida;
			EscutaDeAvisos = [];
			UsarHabilidade(pl, "regenerar");
			string recusa = string.Join(" | ", EscutaDeAvisos);
			EscutaDeAvisos = escutaAntes;

			AfirmarCura("com 69% do tanque a ativa RECUSA -- o custo e do MAXIMO, nao do que sobrou",
						Math.Abs(braco.Vida - vidaMagra) < 1e-9 && recusa.Contains("insuficiente"), recusa);
		}
		finally { RecolherCorpo(pl); }
		GD.Print("");
	}

	// =====================================================================
	// 2. A ATIVA DEVOLVE O MEMBRO PERDIDO
	// =====================================================================
	/// <summary>
	/// ============================ O MEMBRO INTEIRO, E A ORDEM DE PREFERENCIA ============================
	/// A outra metade da frase do dono: *"ou REGENERAR UM MEMBRO INTEIRO PERDIDO"*. Como o
	/// Namekuseijin tem `DeathRegen = 0` (o `return` proprio de `Genetic_Datum.dm:253-258`), **esta e
	/// a unica coisa no jogo inteiro que devolve um braco a ele** -- fora a maquina.
	///
	/// A PREFERENCIA E AFIRMADA JUNTO, e ela nao e detalhe: um Namekuseijin sem braco que gastasse
	/// 70% do Ki costurando um arranhao na perna teria jogado a habilidade fora e continuaria manco,
	/// com 10 s de recarga pela frente. Por isso o corpo desta familia entra com as DUAS coisas: um
	/// membro decepado E um membro ferido.
	///
	/// E o membro volta **inteiro**: `Body.Regenerar` devolve a 70% (`VidaAoRegenerar`), e a
	/// habilidade completa o resto (`GameServer.Raciais.cs`, o `alvo.Vida = alvo.VidaMax` logo
	/// depois). Afirmar 100% e o que separa "voltou" de "voltou pela metade".
	/// ================================================================================================
	/// </summary>
	private void CvAtivaDevolveMembro()
	{
		GD.Print("--- 2. A ATIVA regenera MEMBRO PERDIDO (a unica saida do Namekuseijin) ---");

		ServerPlayer pl = ForjarCorpo(2, "bancada_namek_manco", "Namekian");
		try
		{
			BodyPart braco = pl.Combate.Corpo.Achar("Braco esquerdo")!;
			BodyPart mao = pl.Combate.Corpo.Achar("Mao esquerda")!;
			BodyPart perna = pl.Combate.Corpo.Achar("Perna direita")!;

			pl.Combate.Corpo.Decepar(braco);                                  // leva a mao junto
			pl.Combate.Corpo.Ferir(perna, perna.VidaMax * 0.85, letal: true); // a isca
			pl.Combate.SincronizarVida();

			AfirmarCura("PRECONDICAO: braco decepado (mao junto) e perna ferida na mesma pessoa",
						braco.Decepado && mao.Decepado && perna.Fracao < 0.2);

			double pernaAntes = perna.Vida;
			UsarHabilidade(pl, "regenerar");

			GD.Print($"  braco {(braco.Decepado ? "AINDA DECEPADO" : $"de volta com {braco.Vida:0}/{braco.VidaMax:0}")}"
					 + $" | mao {(mao.Decepado ? "decepada" : "de volta")} | perna {perna.Vida:0}");

			AfirmarCura("o braco DECEPADO volta", !braco.Decepado);
			AfirmarCura("...e volta INTEIRO (100%, e nao os 70% do `Body.Regenerar`)",
						braco.Vida >= braco.VidaMax - 1e-6, $"{braco.Vida:0.0}");
			AfirmarCura("...e a MAO volta junto (a cascata do `RegrowLimb`)", !mao.Decepado);
			AfirmarCura("...e ela PREFERIU o decepado a perna ferida (nao desperdicou os 70% do Ki)",
						Math.Abs(perna.Vida - pernaAntes) < 1e-9, $"perna {perna.Vida:0.0} x {pernaAntes:0.0}");
		}
		finally { RecolherCorpo(pl); }
		GD.Print("");
	}

	// =====================================================================
	// 3. QUEM TEM A ATIVA
	// =====================================================================
	/// <summary>
	/// ============================ O GATE, E O DEFEITO QUE ELE ACABOU DE FECHAR ============================
	/// `PodeRegenerar` perguntava `Regenera(pl.Race)` -- **o `canheallopped`**. Sao duas perguntas
	/// diferentes: `canheallopped` diz se o membro perdido volta SOZINHO; o gate diz quem sabe
	/// faze-lo voltar DE PROPOSITO. Com as duas coladas, **Majin, Bio-Androide e Shapeshifter
	/// ganhavam de graca a habilidade racial do Namekuseijin**.
	///
	/// O DM separa e e explicito (`namekian.dm:163`):
	/// `if(!(usr.Race == "Namekian" || usr.Parent_Race == "Namekian")) return`.
	///
	/// A SEGUNDA METADE do gate e o desvio consciente do port: a skill `Regenerate` pende das arvores
	/// de Alien, Android e Demonio (`Race Trees/alien.dm:75-99`), e quem a COMPRA sabe regenerar sem
	/// ser de Namek. Por isso esta familia tem as duas listas, e nao uma.
	/// ==================================================================================================
	/// </summary>
	private void CvQuemTemAAtiva()
	{
		GD.Print("--- 3. QUEM TEM A ATIVA (e quem ganhava de graca) ---");

		int i = 10;
		foreach (string raca in new[] { "Majin", "BioAndroid", "Shapeshifter", "Saiyan", "Human" })
		{
			ServerPlayer pl = ForjarCorpo(i++, $"bancada_gate_{raca}", raca);
			try
			{
				BodyPart braco = pl.Combate.Corpo.Achar("Braco esquerdo")!;
				pl.Combate.Corpo.Decepar(braco);
				pl.Combate.SincronizarVida();

				var escutaAntes = EscutaDeAvisos;
				EscutaDeAvisos = [];
				UsarHabilidade(pl, "regenerar");
				string disse = string.Join(" | ", EscutaDeAvisos);
				EscutaDeAvisos = escutaAntes;

				AfirmarCura($"...{raca} NAO tem a ativa do Namekuseijin (o braco continua fora)",
							braco.Decepado && disse.Contains("nao regenera assim"), disse);
			}
			finally { RecolherCorpo(pl); }
		}

		// ---- E QUEM COMPRA A SKILL, TEM -----------------------------------------------
		OAlienQueComprou(i);
		GD.Print("");
	}

	/// <summary>
	/// O ALIEN QUE COMPROU `Regenerate` (`/datum/skill/general/regenerate`). Ele entra pelo `Livro`,
	/// que e o mesmo caminho por onde a skill chega em jogo -- e o gate o enxerga pelo `SabeTecnica`,
	/// que le os VERBOS da skill e nao o nome dela.
	/// </summary>
	private void OAlienQueComprou(int i)
	{
		ServerPlayer pl = ForjarCorpo(i, "bancada_alien_comprador", "Alien");
		try
		{
			BodyPart braco = pl.Combate.Corpo.Achar("Braco esquerdo")!;
			pl.Combate.Corpo.Decepar(braco);
			pl.Combate.SincronizarVida();

			AfirmarCura("PRECONDICAO: o Alien SEM a skill e recusado igual aos outros",
						!PodeRegenerarDaBancada(pl));

			pl.Livro!.Dar("/datum/skill/general/regenerate");
			UsarHabilidade(pl, "regenerar");

			AfirmarCura("...e COM a skill comprada ele regenera (as arvores de Alien/Android/Demonio)",
						!braco.Decepado, "comprou a skill e continuou manco");
		}
		finally { RecolherCorpo(pl); }
	}

	/// <summary>O gate, exposto pra bancada sem duplicar a regra (chama o mesmo predicado).</summary>
	private bool PodeRegenerarDaBancada(ServerPlayer pl) => PodeRegenerar(pl);

	// =====================================================================
	// 4. A PASSIVA, PELO FUNIL DO SERVIDOR
	// =====================================================================
	/// <summary>
	/// ============================ AQUI QUEM E CHAMADO E `RegenerarPassivo` ============================
	/// A bancada `cura` do Core chama `Regeneracao.Tique`. Esta chama a funcao do SERVIDOR, na
	/// cadencia do servidor (`Protocol.TickSeconds`, com `Ficha.Tick` a 5 Hz como o `TickCombate`
	/// faz) -- porque as tres guardas que sairam (`KO`, `EmCombate`, `HP >= 99.99`) moravam
	/// **naquela funcao**, e voltar uma delas nao muda uma linha do Core.
	///
	/// A frase do dono, inteira, esta nas quatro afirmacoes desta familia:
	///   *"ate mesmo um NAMEK N TEM REGENERACAO PASSIVA EM COMBATE, somente a ATIVA"*.
	/// ==============================================================================================
	/// </summary>
	private void CvPassivaPeloFunilDoServidor()
	{
		GD.Print("--- 4. A PASSIVA pelo `RegenerarPassivo` (o funil do servidor) ---");

		foreach ((string raca, bool curaEmCombate) in new[]
				 { ("Namekian", false), ("Human", false), ("Saiyan", false), ("Majin", true), ("BioAndroid", true) })
		{
			ServerPlayer pl = ForjarCorpo(30 + raca.Length, $"bancada_passiva_{raca}", raca);
			try
			{
				BodyPart braco = pl.Combate.Corpo.Achar("Braco esquerdo")!;
				pl.Combate.Corpo.Ferir(braco, braco.VidaMax * 0.95, letal: true);
				pl.Combate.SincronizarVida();

				double antes = braco.Vida;
				CorrerNoServidor(pl, 30, brigando: true);
				double emBriga = braco.Vida - antes;

				// ============================ "PAROU DE BRIGAR" NAO E "ESTA EM PAZ" ============================
				// A primeira versao desta familia media os 30 s seguintes e chamava aquilo de paz. Deu
				// ZERO pras tres racas comuns, e o zero estava CERTO: `EntrarEmCombate` arma
				// `CombatKnobs.TagDeCombate` = **90 s**, e a tag continua no ar muito depois do ultimo
				// soco. E o `combatTag` do DM, e ele e um dos numeros que o jogador mais sente sem
				// nunca ver: quem sai de uma briga com o braco quebrado passa um minuto e meio sem
				// costurar nada.
				//
				// Entao a medicao virou TRES: brigando, com a tag ESCOANDO (ainda no ar, e tem que
				// continuar sem curar) e so entao em paz de verdade.
				// ==========================================================================================
				antes = braco.Vida;
				CorrerNoServidor(pl, CombatKnobs.TagDeCombate - 10, brigando: false);
				double escoando = braco.Vida - antes;

				CorrerNoServidor(pl, 20, brigando: false);   // a tag vence no meio deste trecho
				antes = braco.Vida;
				CorrerNoServidor(pl, 30, brigando: false);
				double emPaz = braco.Vida - antes;

				GD.Print($"  {raca,-12} 30 s de BRIGA -> {(emBriga > 0.01 ? $"+{emBriga:0.00}" : "NADA")}"
						 + $"  |  80 s de tag ESCOANDO -> {(escoando > 0.01 ? $"+{escoando:0.00}" : "NADA")}"
						 + $"  |  30 s de PAZ -> +{emPaz:0.00}");

				AfirmarCura($"...{raca} {(curaEmCombate ? "CURA" : "NAO cura")} com a tag de combate no ar",
							curaEmCombate ? emBriga > 0.5 : emBriga < 1e-6, $"{emBriga:0.000}");
				AfirmarCura($"...e {raca} {(curaEmCombate ? "continua curando" : "continua SEM curar")} "
							+ "nos 80 s em que a tag escoa (ela dura 90 s depois do ultimo soco)",
							curaEmCombate ? escoando > 0.5 : escoando < 1e-6, $"{escoando:0.000}");
				AfirmarCura($"...e {raca} cura em PAZ (senao o 'nao cura' acima seria vacuo)",
							emPaz > 0.05, $"{emPaz:0.000}");
			}
			finally { RecolherCorpo(pl); }
		}

		// ============================ O PAR CENTRAL DO PEDIDO, NUMA PESSOA SO ============================
		// O Namekuseijin perde o braco, espera DEZ MINUTOS de paz absoluta -- e continua manco. Ai usa
		// a ativa e o braco volta na hora. As duas linhas juntas sao a frase do dono; separadas, cada
		// uma delas passaria por engano num jogo errado.
		// =============================================================================================
		ServerPlayer nk = ForjarCorpo(40, "bancada_namek_espera", "Namekian");
		try
		{
			BodyPart braco = nk.Combate.Corpo.Achar("Braco esquerdo")!;
			nk.Combate.Corpo.Decepar(braco);
			nk.Combate.SincronizarVida();

			CorrerNoServidor(nk, 600, brigando: false);
			AfirmarCura("o NAMEK espera 10 MINUTOS em paz e o braco NAO volta sozinho", braco.Decepado);

			UsarHabilidade(nk, "regenerar");
			AfirmarCura("...e a ATIVA devolve o mesmo braco na hora -- e ela e a unica saida dele",
						!braco.Decepado);
		}
		finally { RecolherCorpo(nk); }
		GD.Print("");
	}

	// =====================================================================
	// 5. O MAJIN REFAZ O MEMBRO SOZINHO, PELO FUNIL
	// =====================================================================
	/// <summary>
	/// `Injuries.dm:295-309` rodando dentro do `RegenerarPassivo`: o buffer de 25 pontos, o `pick()`
	/// e o aviso. O prazo do Majin e ~18,8 s (medido contra o DM na bancada do Core) e o do Humano e
	/// NUNCA -- e as duas metades andam juntas, porque "volta rapido" sem "o outro nao volta" nao e
	/// privilegio de raca nenhuma.
	///
	/// **O AVISO E AFIRMADO**: um membro que volta sem uma linha no chat e um membro que o jogador so
	/// descobre olhando o boneco. Foi assim que a punicao do Oozaru ficou muda por uma fase inteira.
	/// </summary>
	private void CvMajinRefazPeloFunil()
	{
		GD.Print("--- 5. O MAJIN refaz o membro SOZINHO, no funil do servidor ---");

		ServerPlayer mj = ForjarCorpo(50, "bancada_majin", "Majin");
		try
		{
			BodyPart braco = mj.Combate.Corpo.Achar("Braco esquerdo")!;
			BodyPart mao = mj.Combate.Corpo.Achar("Mao esquerda")!;
			mj.Combate.Corpo.Decepar(braco);
			mj.Combate.SincronizarVida();

			var escutaAntes = EscutaDeAvisos;
			EscutaDeAvisos = [];
			// EM COMBATE de proposito: o Majin e a unica raca que refaz membro no meio da briga.
			double t = CorrerAte(mj, 120, brigando: true, () => !braco.Decepado);
			string disse = string.Join(" | ", EscutaDeAvisos);
			EscutaDeAvisos = escutaAntes;

			GD.Print($"  Majin: braco de volta em {t:0.0} s (EM COMBATE) | aviso: \"{disse}\"");

			AfirmarCura("o MAJIN refaz o braco sozinho, e EM COMBATE, em menos de 60 s",
						!braco.Decepado && t < 60, $"{t:0.0} s");
			AfirmarCura("...e a MAO volta junto", !mao.Decepado);
			AfirmarCura("...e o jogador e AVISADO (membro que volta calado e membro que ninguem ve)",
						disse.Contains("volta a crescer"), disse);
		}
		finally { RecolherCorpo(mj); }

		ServerPlayer hm = ForjarCorpo(51, "bancada_humano_manco", "Human");
		try
		{
			BodyPart braco = hm.Combate.Corpo.Achar("Braco esquerdo")!;
			hm.Combate.Corpo.Decepar(braco);
			hm.Combate.SincronizarVida();
			double t = CorrerAte(hm, 600, brigando: false, () => !braco.Decepado);
			GD.Print($"  Human: {(braco.Decepado ? "NUNCA (10 min de paz)" : $"voltou em {t:0.0} s")}");
			AfirmarCura("...e o HUMANO nao refaz nada em 10 minutos (a maquina e a saida dele)",
						braco.Decepado, $"voltou em {t:0.0} s");
		}
		finally { RecolherCorpo(hm); }
		GD.Print("");
	}

	// =====================================================================
	// 6. A BANCADA SE COBRA
	// =====================================================================
	/// <summary>
	/// ============================ DUAS INJECOES, E ELAS NAO SAO DECORACAO ============================
	///  1. **O EIXO TROCADO**: um corpo de Namekuseijin recebe o `CombatState` com o perfil do MAJIN
	///     e roda no MESMO laco da familia 4, com a MESMA tag de combate. Se ele curar, o "nao cura"
	///     de la e uma regra; se ele tambem nao curar, o "nao cura" de la era o laco quebrado. E a
	///     unica coisa que separa as duas leituras, e ela ja salvou este port antes ("as duas telas
	///     concordam fica verde com as duas erradas igual").
	///
	///  2. **O GATE ANTIGO**: o predicado que existia (`canheallopped`) e recalculado aqui e
	///     comparado com o de hoje. Ele tem que DISCORDAR no Majin -- se concordar, ou o gate voltou
	///     ao que era, ou o eixo do Majin mudou; nos dois casos a familia 3 esta medindo o vazio.
	/// =============================================================================================
	/// </summary>
	private void CvABancadaSeCobra()
	{
		GD.Print("--- 6. A BANCADA SE COBRA (defeito injetado) ---");

		// ---- INJECAO 1: o eixo do Majin dentro do corpo do Namekuseijin ----------------
		ServerPlayer pl = ForjarCorpo(60, "bancada_injecao_eixo", "Namekian");
		try
		{
			BodyPart braco = pl.Combate.Corpo.Achar("Braco esquerdo")!;
			pl.Combate.Corpo.Ferir(braco, braco.VidaMax * 0.95, letal: true);
			pl.Combate.SincronizarVida();

			double antes = braco.Vida;
			CorrerNoServidor(pl, 30, brigando: true);
			bool curouComOEixoCerto = braco.Vida - antes > 1e-6;

			// O DEFEITO: o MESMO corpo e o MESMO laco -- so o eixo do genoma troca de dono. E onde ele
			// mora de verdade (`Body.Regen`, escrito pelo `EixoDeRegen` no `PrepararCombate`), entao
			// isto e uma injecao no dado de producao e nao um segundo caminho de cura.
			pl.Combate.Corpo.Regen = PerfilDeRegen.De("Majin", 100);
			antes = braco.Vida;
			CorrerNoServidor(pl, 30, brigando: true);
			double comDefeito = braco.Vida - antes;

			GD.Print($"  eixo certo -> {(curouComOEixoCerto ? "curou" : "NADA")} | "
					 + $"eixo do MAJIN injetado -> +{comDefeito:0.00}");
			AfirmarCura("INJECAO: com o eixo do Majin o MESMO laco cura -- entao o 'nao cura' e regra, "
						+ "e nao laco quebrado", !curouComOEixoCerto && comDefeito > 0.5,
						$"certo={curouComOEixoCerto} defeito=+{comDefeito:0.000}");
		}
		finally { RecolherCorpo(pl); }

		// ---- INJECAO 2: o gate antigo (`canheallopped`) ---------------------------------
		ServerPlayer mj = ForjarCorpo(61, "bancada_injecao_gate", "Majin");
		try
		{
			bool gateDeHoje = PodeRegenerarDaBancada(mj);
			bool gateAntigo = EixoDeRegen(mj.Race).MembroVolta;   // era `Regenera(pl.Race)`
			GD.Print($"  Majin: gate de hoje = {gateDeHoje} | gate ANTIGO (canheallopped) = {gateAntigo}");
			AfirmarCura("INJECAO: o gate ANTIGO daria a ativa do Namek ao Majin, e o de hoje nao "
						+ "-- a familia 3 mede uma diferenca de verdade", gateAntigo && !gateDeHoje);
		}
		finally { RecolherCorpo(mj); }
		GD.Print("");
	}

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// UM CORPO DE BANCADA, pelo caminho de producao (`PorNoMundo`), com o Ki cheio e o poder ja
	/// calculado -- mesmo padrao do `Forjar` da bancada do sol, e pelo mesmo motivo: sem o
	/// `Ficha.Tick` o `expressedBP` nasce zero e metade das contas do combate mede outro personagem.
	///
	/// A ZONA E A TERRA (gravidade 1x) de proposito: Vegeta esmaga (10x), e um corpo perdendo vida
	/// por gravidade dentro de uma bancada de CURA mediria a soma das duas coisas.
	/// </summary>
	private ServerPlayer ForjarCorpo(int i, string nome, string raca)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDaCuraViva + i,
			Peer = null,
			Name = nome,
			Race = raca,
			Genero = "Male",
			Idade = 25,
			Zone = ZoneKey.Premade("Earth"),
			Pos = PontoDeNascimento(ZoneKey.Premade("Earth")),
			Conta = $"bancada_cura_{i}",
			Slot = 0,
			Ficha = new Fighter { Race = raca, BP = 5_000 },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.stamina = novo.Ficha.maxstamina;
		novo.Ficha.Tick(agoraMs: NowMs());
		return novo;
	}

	private void RecolherCorpo(ServerPlayer pl)
	{
		_players.Remove(pl.Id);
		ZoneList(pl.Zone.Hash).Remove(pl);
	}

	/// <summary>
	/// RODA O SERVIDOR EM CIMA DESTE CORPO por `segundos`, na ordem e na cadencia do `TickCombate`:
	/// `Combate.Tick` -> `RegenerarPassivo`, com a `Ficha.Tick` a 5 Hz.
	///
	/// `brigando` renova a tag de combate a cada segundo, que e o que uma briga de verdade faz -- a
	/// tag dura 90 s (`CombatKnobs.TagDeCombate`) e um unico `EntrarEmCombate` no comeco cobriria os
	/// 30 s por acidente, medindo a duracao da tag em vez da regra.
	/// </summary>
	private void CorrerNoServidor(ServerPlayer pl, double segundos, bool brigando) =>
		CorrerAte(pl, segundos, brigando, () => false);

	private double CorrerAte(ServerPlayer pl, double segundos, bool brigando, Func<bool> ate)
	{
		int n = (int)(segundos / Protocol.TickSeconds);
		int porSegundo = (int)Math.Round(1 / Protocol.TickSeconds);
		for (int t = 0; t < n; t++)
		{
			if (brigando && t % porSegundo == 0) pl.Combate.EntrarEmCombate();
			if (t % TicksPorFicha == 0) pl.Ficha.Tick(agoraMs: NowMs());
			pl.Combate.Tick(Protocol.TickSeconds);
			RegenerarPassivo(pl, Protocol.TickSeconds);
			if (ate()) return (t + 1) * Protocol.TickSeconds;
		}
		return double.PositiveInfinity;
	}
}
