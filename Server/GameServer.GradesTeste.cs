using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// ============================ A BANCADA DOS TRES PEDIDOS DO DONO -- roda dentro do `--formasteste` ============================
/// Tres coisas mudaram no motor de formas e cada uma quebra CALADA por um motivo diferente:
///
///   1. **a % de BP efetivo** deixou de sair de `expressedBP / peakexBP` e passou a ser o produto dos
///      fatores de condicao (`Fighter.Inteireza`). O defeito antigo nao dava erro nenhum: a razao
///      saia `1,0000` EXATO com o Ki a 200%, e "100% efetivo" e um numero perfeitamente plausivel;
///   2. **os stats dos grades** entraram por um canal novo (`FormaDef.Mods` -> os sete `forma*` da
///      ficha). Um canal escrito e nunca lido e a falha assinatura deste port -- e aqui ele tem
///      QUATRO consumidores diferentes (`Rspeed`, `Rphysoff`/`Rkioff`, `Pontaria`, `Cadencia`);
///   3. **a preferencia dos grades** e um `bool?` que atravessa o disco. Este projeto ja perdeu duas
///      escolhas de jogador exatamente nessa costura.
///
/// ============================ POR QUE ELA E AO VIVO, E NAO NO CORE ============================
/// Nada aqui e conta nova: `Inteireza`, `StatCap` e `Pontaria` sao funcoes puras e o Core saberia
/// testa-las. O que o Core NAO pode testar e a CADEIA, e as tres mudancas sao cadeia inteira:
///
///   * o catalogo -> `AplicarForma` -> `Statify` -> `Ephysoff`/`Espeed` -> `CombatMath`. Sao cinco
///     casas, e cada uma delas ja falhou sozinha na historia deste repo;
///   * o `PowerLevel` do corpo VIVO, com a idade, o peso e a gravidade que aquele personagem tem --
///     um `Fighter` escrito a mao na bancada nasce com todos os divisores em 1 e por isso passa
///     verde em defeito que so morde corpo de verdade;
///   * o verb -> o switch -> o `Proxima` -> o save -> o JSON -> o login.
///
/// Ela roda DEPOIS do <see cref="OsGradesNoCaminhoDoC"/> de proposito: aquele bloco prova o CAMINHO
/// (por onde a tecla C anda), este prova o PRECO (o que cada degrau faz com o corpo) e a
/// PERSISTENCIA de verdade -- a ida e volta pelo arquivo .json, e nao so pelo `CharacterSave` em
/// memoria.
/// ==========================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// O funil das tres secoes. Existe pra o `RodarBancadaDeFormas` ter UMA linha e pra a reposicao
	/// do corpo ficar num lugar so: os tres blocos escrevem BP, Ki, idade, peso, gravidade, maestria
	/// e formas liberadas no personagem de verdade, e o que roda depois deles (as ferramentas de
	/// admin e as escadas raciais) mediria o estranho desta bancada se ela nao devolvesse o corpo.
	/// </summary>
	private void AsContasDosGrades(ServerPlayer pl, Action<string, bool, string> Checa,
								   Func<List<string>> Ouvido)
	{
		// MESMO FUNIL DO LOGIN pra fotografar e repor (ver `OsGradesNoCaminhoDoC`): reescrever a
		// reposicao a mao seria a copia da migracao morando dentro do teste que deveria vigia-la.
		CharacterSave antes = AccountStore.DeJogador(pl, 0);
		double bpAntes = pl.Ficha.BP;

		try
		{
			APorcentagemDeBpEfetivo(pl, Checa);
			OsStatsDosGradesNoCorpo(pl, Checa);
			APreferenciaNoSsj1CruENoDisco(pl, Checa, Ouvido);
		}
		finally
		{
			RestaurarFormaEDisciplina(pl, antes);
			pl.Ficha.BP = bpAntes;
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			pl.Ficha.KO = false;
			pl.Ficha.dead = false;
			AplicarForma(pl);
			Medir(pl);
		}
	}

	// =====================================================================
	// 1. A % DE BP EFETIVO -- ELA PASSA DE 100%, E NAO SE MEXE AO TRANSFORMAR
	// =====================================================================
	/// <summary>
	/// ============================ O DEFEITO QUE ESTA SECAO EXISTE PRA PEGAR ============================
	/// O dono: *"o bp efetivo pode SUBIR caso eu tenha um ki acima de 100%"*. Ele nao subia: a %
	/// saia de `expressedBP / peakexBP`, e o `peakexBP` tem um `Math.Max(..., expressedBP)` herdado do
	/// DM. A partir de `statusBuff > 1` o `Math.Max` troca de ramo, o pico vira o proprio expresso e a
	/// razao devolve **1,0000 exato -- por construcao, nao por arredondamento**.
	///
	/// E E POR ISSO QUE ELE NUNCA APARECEU: nada quebra, nada loga, e "100% efetivo" com o Ki no
	/// talo e o numero que qualquer um esperaria ver. So aparece quando alguem PROCURA o 110%.
	/// ==============================================================================================
	///
	/// ============================ AS DUAS PONTAS SAO A CHECAGEM, NAO UMA DELAS ============================
	/// Medir so "acima de 100%" da verde num defeito que apague o teto E o piso -- por exemplo trocar a
	/// conta por `kiratio` cru sem os outros fatores, ou devolver 1 sempre. Por isso cada afirmacao
	/// aqui vem em par: a % SOBE com o Ki alto e DESCE com o Ki baixo, sobe com a gravidade dominada e
	/// desce com a gravidade que pesa, e a serie inteira e monotonica entre uma ponta e a outra.
	/// ================================================================================================
	///
	/// ============================ O CORPO E LIMPADO ANTES, E ISSO E O TESTE ============================
	/// A % tem SEIS pernas (Ki, vida, folego, idade, peso/restricao e gravidade) e elas se multiplicam.
	/// Medir uma sem zerar as outras da um numero que ninguem consegue conferir -- e foi assim que a
	/// versao antiga passou meses dizendo "98%" pra um corpo perfeitamente inteiro (era o `DmMath.Round`
	/// do `expressedBP` vazando na divisao). Aqui cada perna e exercitada sozinha, e o ponto de partida
	/// e `1,000000000`: a exatidao E a afirmacao.
	/// ==========================================================================================
	/// </summary>
	private void APorcentagemDeBpEfetivo(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		Fighter f = pl.Ficha;

		// ---------------------------------------------------------------- o que sai como entrou
		double kiAntes = f.Ki, idadeAntes = f.Idade, pesoAntes = f.weight, restricaoAntes = f.BPrestriction;
		double gravAntes = f.Planetgrav, maestriaGravAntes = f.GravMastered;
		double controleAntes = f.canPower, reunirAntes = f.MeditateGivesKiRegen;
		double folegoAntes = f.stamina;

		try
		{
			// ---------------------------------------------------------------- o corpo de partida
			while (!pl.Forma.NaBase && pl.Forma.Def != null) Transformar(pl, subir: false);
			f.KO = false;
			f.dead = false;
			pl.Combate.Corpo.Restaurar();
			pl.Combate.SincronizarVida();

			// PESO E RESTRICAO EM 1: os dois entram no `deBuff` e sao herdados do genoma deste corpo.
			// Sem zera-los o "100% exato" viraria "97% e alguma coisa" -- correto, e inconferivel.
			f.weight = 1;
			f.BPrestriction = 1;

			// GRAVIDADE NEUTRA: 1g com 1g de maestria da `gravBuff` 1 (ver `StatCurves.GravFelt`).
			f.Planetgrav = 1;
			f.GravMastered = 1;

			// A IDADE E PROCURADA, NAO CRAVADA: o auge e por RACA (`Envelhecimento.AugeDaRaca`), e um
			// numero fixo aqui viraria bancada quebrada no dia em que a curva do Saiyajin mudar.
			double auge = Jandirus.Core.Races.Envelhecimento.AugeDaRaca(f.Race);
			f.Idade = auge > 0 ? auge : Jandirus.Core.Races.Envelhecimento.IdadeAdulta;

			AplicarForma(pl);

			// A % SAI DO `PowerLevel`, ENTAO E ELE QUE RODA -- `Medir` e literalmente `f.PowerLevel()`,
			// o mesmo que o tique do jogo chama. Ler `Inteireza` sem tiquear leria o corpo de ontem.
			double Com(double razaoDeKi)
			{
				f.Ki = f.MaxKi * razaoDeKi;
				Medir(pl);
				return f.Inteireza;
			}

			// ---------------------------------------------------------------- 1. O ZERO DA REGUA
			double inteiro = Com(1);
			Checa("[efetiva] corpo inteiro, Ki cheio, gravidade dominada: 100% EXATO",
				  Math.Abs(inteiro - 1) < 1e-9, $"{inteiro:0.#########}");

			// ---------------------------------------------------------------- 2. AS DUAS PONTAS
			double a60 = Com(0.6);
			Checa("[efetiva] Ki a 60%: a % CAI abaixo de 100 (a ponta de baixo)",
				  a60 < 1 - 1e-9, $"{a60:0.######}");
			Checa("[efetiva] ...e vale o proprio 60% (o `kiratio` no piso, e mais nada)",
				  Math.Abs(a60 - 0.6) < 1e-9, $"{a60:0.#########}");

			double a150 = Com(1.5);
			Checa("[efetiva] Ki a 150%: a % PASSA de 100 -- o pedido do dono",
				  a150 > 1 + 1e-9, $"{a150:0.######}");
			Checa("[efetiva] ...e vale 1,5: acima de 100% o Ki e LINEAR (`PowerLevel`)",
				  Math.Abs(a150 - 1.5) < 1e-9, $"{a150:0.#########}");

			// ---------------------------------------------------------------- 3. A SERIE INTEIRA
			//
			// DUAS PONTAS NAO DISTINGUEM RAMPA DE DEGRAU, e um teto que morde so em 200% passaria nas
			// duas checagens acima. A serie e o que fecha o intervalo.
			double[] razoes = [0.6, 0.75, 0.9, 1.0, 1.1, 1.25, 1.5, 2.0];
			double[] vistos = new double[razoes.Length];
			for (int k = 0; k < razoes.Length; k++) vistos[k] = Com(razoes[k]);
			GD.Print("  --   " + string.Join("  ", razoes.Zip(vistos,
				(r, v) => $"ki {r * 100:0}%={v * 100:0.#}%")));

			bool subiuSempre = true, ladoCerto = true;
			for (int k = 0; k < razoes.Length; k++)
			{
				if (k > 0 && vistos[k] <= vistos[k - 1]) subiuSempre = false;
				if (razoes[k] > 1 != vistos[k] > 1) ladoCerto = false;
			}
			Checa($"[efetiva] a serie de Ki e monotonica nos {razoes.Length} pontos (60% -> 200%)",
				  subiuSempre, string.Join(" ", vistos.Select(v => $"{v:0.###}")));
			Checa("[efetiva] ...e cada ponto cai do lado certo dos 100% (nenhum teto, nenhum piso)",
				  ladoCerto, string.Join(" ", vistos.Select(v => $"{v:0.###}")));

			// ---------------------------------------------------------------- 4. O PISO DO `peakexBP` FICOU
			//
			// ============================ AS DUAS CONTAS DIVERGEM DE PROPOSITO ============================
			// O `Math.Max(..., expressedBP)` do pico e heranca fiel do DM e tem consumidor la: `buudead`
			// e `peakexBP/expressedBP` (`misc.dm:194/204/212`) -- um multiplicador de recuperacao que TEM
			// que ser >= 1 --, e o zumbi nasce com este numero (`Death.dm:44/63`). Tirar o piso plantaria
			// um `buudead < 1` pra quando essas duas vias forem portadas.
			//
			// ENTAO A % MUDOU DE CONTA EM VEZ DE O PICO MUDAR DE FORMULA, e esta checagem e a unica coisa
			// no repo que segura as duas decisoes ao mesmo tempo: se alguem "consertar" o pico tirando o
			// piso, a primeira metade reprova; se alguem devolver a % pra a razao, a segunda reprova.
			// ==========================================================================================
			Com(1.5);
			double razaoDoPico = f.peakexBP > 0 ? f.expressedBP / f.peakexBP : -1;
			Checa("[efetiva] com Ki a 150% a razao `expressedBP/peakexBP` ainda satura em 1 (o piso do DM ficou)",
				  Math.Abs(razaoDoPico - 1) < 1e-9, $"{razaoDoPico:0.#########}");
			Checa("[efetiva] ...e a % NAO sai mais dali -- ela passa de 100 no mesmo instante",
				  f.Inteireza > 1 + 1e-9, $"{f.Inteireza:0.######}");

			// ---------------------------------------------------------------- 5. E O KI ALTO CHEGA PELA TECLA C
			//
			// Ate aqui quem passou dos 100% fui eu, escrevendo no campo. Isto e o jogo: `Carregar` +
			// `TickDaCarga` sao exatamente o que a tecla C do cliente aciona, com as duas chaves de skill
			// que o DM exige (`Ki Unlocked` e `Basic Ki Control 5`). Sem esta checagem a secao inteira
			// mediria um estado que talvez nenhum jogador alcance.
			f.MeditateGivesKiRegen = 1;
			f.canPower = 1;
			f.Ki = f.MaxKi * 0.99;
			pl.Moving = false;
			Carregar(pl, true);
			for (int t = 0; t < 4000 && f.Ki <= f.MaxKi * 1.2; t++)
			{
				// O FOLEGO E REPOSTO PORQUE ELE NAO E O ASSUNTO: carregar cansa de proposito (e o freio
				// do sistema, e tem bancada propria), mas aqui ele so encurtaria a medida.
				f.stamina = f.maxstamina;
				TickDaCarga(pl, 0.1);
			}
			PararCarga(pl);
			bool passouDosCem = f.Ki > f.MaxKi;
			double razaoAlcancada = f.MaxKi > 0 ? f.Ki / f.MaxKi : 0;

			// O PRECO DO EXCESSO E DANO, e dano e OUTRA perna da conta: sem curar, a queda de vida
			// entraria na mesma medida e a checagem deixaria de ser sobre o Ki.
			pl.Combate.Corpo.Restaurar();
			pl.Combate.SincronizarVida();
			Medir(pl);

			Checa("[efetiva] segurar C leva o Ki ACIMA de 100% (o caminho do jogo, nao a minha mao)",
				  passouDosCem, $"{razaoAlcancada * 100:0.#}%");
			Checa("[efetiva] ...e a % de BP efetivo passa de 100 junto com ele",
				  f.Inteireza > 1 + 1e-9, $"{f.Inteireza * 100:0.##}%");

			// ---------------------------------------------------------------- 6. TRANSFORMAR NAO A MEXE
			//
			// ============================ A REGRA DO DONO, E A CONTRA-PROVA JUNTO ============================
			// Nenhum fator de forma entra no produto da `Inteireza` -- ela e sobre o CORPO, nao sobre o
			// poder. Uma checagem so de invariancia, porem, fica verde num defeito que devolva constante:
			// por isso o `MultiplicadorTotal` e medido no mesmo laco e TEM que mudar. Uma afirmacao diz
			// "isto nao mexeu"; a outra diz "e o corpo mexeu de verdade".
			//
			// A RAZAO DE KI ATRAVESSA A ESCADA sozinha (o `AplicarForma` a repoe contra o teto novo), e
			// e por isso que nao reponho Ki entre os degraus: repor mascararia justamente o unico jeito
			// de a forma entrar nesta conta pela porta dos fundos.
			// ==========================================================================================
			pl.Forma.Liberar("ssj1");
			pl.Forma.Liberar("ssj2");
			pl.Forma.Liberar("grade2");
			pl.Forma.Liberar("grade3");
			pl.Forma.Maestria.Por("ssj1", 100);
			f.BP = 1e13;
			f.Ki = f.MaxKi * 1.2;
			Medir(pl);

			double naBase = f.Inteireza, multNaBase = f.MultiplicadorTotal;
			var vistas = new List<(string Forma, double Pct, double Ki, double Mult)>
				{ ("base", naBase, f.Ki / f.MaxKi, multNaBase) };
			foreach (string id in new[] { "ssj1", "grade2", "grade3", "ssj2" })
			{
				Verbo(pl, "forma", id);
				PassarACena(pl);
				Medir(pl);
				vistas.Add((pl.Forma.Atual, f.Inteireza, f.Ki / f.MaxKi, f.MultiplicadorTotal));
			}
			GD.Print("  --   " + string.Join("  ", vistas.Select(v =>
				$"{v.Forma}: {v.Pct * 100:0.##}% (ki {v.Ki * 100:0.##}%) / {v.Mult:0.##}x")));

			// ============================ A AFIRMACAO E "ELA E O KI", E NAO "ELA E CONSTANTE" ============================
			// A primeira versao desta checagem exigia o mesmo numero nos cinco degraus e reprovou por
			// 0,23% -- corretamente, e por um motivo que nao e forma: **o dreno**. O tique que encerra a
			// cinematica ja cobra Ki do degrau, entao o tanque relativo desce um tico a cada
			// transformacao, e a % desce junto porque ela E o tanque.
			//
			// Repor o Ki entre os degraus esconderia isso -- e esconderia tambem o unico jeito de a forma
			// entrar nesta conta pela porta dos fundos (mexer no `MaxKi` e nao na `Inteireza`). Entao a
			// afirmacao ficou mais forte em vez de mais frouxa: com o corpo limpo (vida cheia, peso 1,
			// idade no auge, gravidade dominada) a % tem que ser **exatamente** a razao de Ki do momento,
			// em qualquer forma. Um fator de forma que vazasse pro produto quebraria a igualdade no
			// degrau em que ele existe, por menor que fosse.
			// ========================================================================================================
			Checa("[efetiva] em CADA degrau a % e exatamente a razao de Ki do momento (nenhum fator de forma entra)",
				  vistas.All(v => Math.Abs(v.Pct - v.Ki) < 1e-9),
				  string.Join(" | ", vistas.Select(v => $"{v.Forma} {v.Pct:0.######} vs ki {v.Ki:0.######}")));
			Checa("[efetiva] ...e o corpo transformou MESMO (o multiplicador total mudou em cada degrau)",
				  vistas.Select(v => Math.Round(v.Mult, 6)).Distinct().Count() == vistas.Count,
				  string.Join(" | ", vistas.Select(v => $"{v.Forma} {v.Mult:0.###}x")));
			Checa("[efetiva] ...e ela continua ACIMA de 100% na escada toda (o Ki alto nao se perde no caminho)",
				  vistas.All(v => v.Pct > 1 + 1e-9), $"{naBase:0.####}");

			// E O TANTO QUE ELA ANDOU E O DRENO, NAO A FORMA: quatro transformacoes custam menos de 1%.
			// Um degrau que mexesse na % de verdade (um `formBuff` vazado pro produto) mudaria o numero
			// em dezenas de por cento, nao em decimos.
			double maiorDesvio = vistas.Max(v => Math.Abs(v.Pct - naBase)) / naBase;
			Checa("[efetiva] ...e a escada inteira move a % em menos de 1% (o que sobra e o dreno do degrau)",
				  maiorDesvio < 0.01, $"{maiorDesvio * 100:0.###}%");

			while (!pl.Forma.NaBase && pl.Forma.Def != null) Transformar(pl, subir: false);

			// ---------------------------------------------------------------- 7. A GRAVIDADE PESA
			//
			// Ela e FAMILIA 2 (soma na base) e por isso nunca aparecia numa % feita de multiplicadores:
			// com gravidade 100 o poder caia pra 46% e a conta antiga dizia "100% inteiro". O par de
			// checagens e o assunto -- gravidade DOMINADA nao e ferimento e nao pode descontar nada.
			f.Ki = f.MaxKi;
			f.Planetgrav = 100;
			f.GravMastered = 1;
			Medir(pl);
			double sofrendo = f.Inteireza, divisor = f.gravDiv;

			f.GravMastered = 100;
			Medir(pl);
			double aguentando = f.Inteireza;

			f.Planetgrav = 1;
			f.GravMastered = 1;
			Medir(pl);

			Checa("[efetiva] 100g num corpo que so aguenta 1g: a % CAI",
				  sofrendo < 1 - 1e-9 && divisor < 1, $"{sofrendo:0.####} (gravDiv {divisor:0.####})");
			Checa("[efetiva] ...e com a gravidade DOMINADA ela volta pros 100% (peso dominado nao e ferimento)",
				  Math.Abs(aguentando - 1) < 1e-9, $"{aguentando:0.#########}");

			// ---------------------------------------------------------------- 8. A VIDA PESA
			//
			// Pelo funil de verdade (`Corpo.Ferir` -> `SincronizarVida`), e nao escrevendo `HP`: o campo
			// da ficha e ESPELHO do corpo, e quem o escreve na mao mede o proprio espelho.
			foreach (BodyPart parte in pl.Combate.Corpo.Partes.ToList())
				if (!parte.Decepado) pl.Combate.Corpo.Ferir(parte, parte.VidaMax * 0.5, letal: false);
			pl.Combate.SincronizarVida();
			Medir(pl);
			double machucado = f.Inteireza, vida = f.HP;

			pl.Combate.Corpo.Restaurar();
			pl.Combate.SincronizarVida();
			Medir(pl);
			double curado = f.Inteireza;

			Checa("[efetiva] corpo machucado (mesmo com o Ki cheio): a % CAI",
				  machucado < 1 - 1e-9, $"{machucado:0.####} com HP {vida:0.#}");
			Checa("[efetiva] ...e curar o corpo a devolve aos 100%",
				  Math.Abs(curado - 1) < 1e-9, $"{curado:0.#########}");

			// ---------------------------------------------------------------- 9. O NOCAUTE NAO ENTRA
			//
			// Ele MULTIPLICA O PODER DEPOIS (10% do BP base) e some na conta de proposito: nocaute nao e
			// "estar inteiro", e estar cortado -- e ja tem linha propria na tela. A checagem existe
			// porque a tentacao de somar tudo o que derruba BP nesta conta e permanente.
			double antesDoKo = f.Inteireza, poderAntes = f.expressedBP;
			f.KO = true;
			Medir(pl);
			double noKo = f.Inteireza, poderNoKo = f.expressedBP;
			f.KO = false;
			Medir(pl);

			Checa("[efetiva] o nocaute CORTA o poder", poderNoKo < poderAntes * 0.99,
				  $"{poderAntes:0} -> {poderNoKo:0}");
			Checa("[efetiva] ...e NAO mexe na % (ela e sobre o corpo, nao sobre o corte)",
				  Math.Abs(noKo - antesDoKo) < 1e-9, $"{antesDoKo:0.######} -> {noKo:0.######}");
		}
		finally
		{
			f.Idade = idadeAntes;
			f.weight = pesoAntes;
			f.BPrestriction = restricaoAntes;
			f.Planetgrav = gravAntes;
			f.GravMastered = maestriaGravAntes;
			f.canPower = controleAntes;
			f.MeditateGivesKiRegen = reunirAntes;
			f.stamina = folegoAntes;
			f.Ki = kiAntes;
			f.KO = false;
			f.dead = false;
			pl.Combate.Corpo.Restaurar();
			pl.Combate.SincronizarVida();
		}
	}

	// =====================================================================
	// 2. OS STATS DOS GRADES, NO CORPO
	// =====================================================================
	/// <summary>
	/// ============================ O PEDIDO, E OS QUATRO CANAIS QUE ELE VIROU ============================
	/// O dono: *"o ssj grade 2 e grade 3 n estao dando o DEBUFF DE VELOCIDADE -- com isso menos chance
	/// de acertar golpes e socar mais devagar (o grade 2 n teria tanta diferenca, mas teria; o grade 3
	/// seria BEM mais lento e acertaria menos golpes). e tb n estao tendo os buffs de OFFENSIVE (ki e
	/// physical)"*.
	///
	/// "Mais lento" virou DOIS canais, porque no motor deste jogo eles sao dois de verdade:
	///
	///   * **acertar** e `Etechnique(de quem bate) / Espeed(de quem apanha)` (`CombatMath.Pontaria`,
	///     `calcs.dm:120`) -- a velocidade de quem BATE nao entra. Entao errar mais e `Tecnica` pra
	///     baixo, e ser acertado mais e `Speed` pra baixo. Sao mods diferentes;
	///   * **socar mais devagar** e o divisor do `Cadencia`, e NAO o `Espeed`: o `Eactspeed` nao se
	///     mexe (medido -- `Espeed` cai 31% e a cadencia fica parada em 0,333 s). O lever por
	///     personagem do DM sempre foi o `hitspeedMod` (`attack cmn.dm:100/137`).
	/// ==================================================================================================
	///
	/// ============================ AS AFIRMACOES SAO RELACOES, E ISSO E DELIBERADO ============================
	/// Os numeros dos grades **nao existem no DM** (varrido: `supersaiyanbuff.dm` inteiro, `1A
	/// Defines.dm`, e o repo atras de `Tspeed|Tphysoff|speedMod|physoffMod`), entao os sete fatores de
	/// `Catalogo.Grade2Mods`/`Grade3Mods` sao PROPOSTOS e vao ser afinados. Uma bancada que cravasse
	/// "1,60" reprovaria no primeiro ajuste de balanceamento e ensinaria a equipe a ignora-la.
	///
	/// O que ela crava e o DESENHO, que e o que o dono descreveu e o que nao muda com afinacao:
	/// os dois grades socam mais forte e ficam mais lentos, e **o Grade 3 e mais extremo que o Grade 2
	/// nos dois eixos ao mesmo tempo**. Trocar 1,60 por 1,45 continua verde; inverter os dois grades,
	/// achatar um deles ou desligar um canal reprova na hora.
	/// ======================================================================================================
	/// </summary>
	private void OsStatsDosGradesNoCorpo(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		Fighter f = pl.Ficha;
		double kiAntes = f.Ki, hitspeedAntes = f.hitspeedMod;

		// A REGUA: um corpo de referencia que NAO se mexe. Ela leva a velocidade e a tecnica do proprio
		// personagem na BASE, entao toda diferenca medida vem do grade e nao dela. O `expressedBP` e
		// igualado a cada medida de proposito -- o `BpModulus` do `Pontaria` daria 1,5x pro SSJ1
		// (6x) contra 1x pro Grade 2 (3x) e a diferenca de PODER esconderia a de tecnica, que e o
		// assunto aqui. O preco em BP de cada degrau ja tem canal proprio e bancada propria.
		var regua = new Fighter { Name = "regua de bancada", BP = 1 };

		try
		{
			// ---------------------------------------------------------------- o corpo de partida
			while (!pl.Forma.NaBase && pl.Forma.Def != null) Transformar(pl, subir: false);
			f.KO = false;
			f.dead = false;
			f.hitspeedMod = 1;
			f.BP = 1e13;
			f.Ki = f.MaxKi;
			pl.Forma.Liberar("ssj1");
			pl.Forma.Liberar("grade2");
			pl.Forma.Liberar("grade3");
			pl.Forma.Liberar("ssj2");
			pl.Forma.Maestria.Por("ssj1", 100);
			AplicarForma(pl);
			f.Ki = f.MaxKi;
			Medir(pl);

			regua.Espeed = f.Espeed;
			regua.Etechnique = f.Etechnique;

			// UMA MEDIDA = ENTRAR PELA TECLA DE FORMA, QUEIMAR A CENA, TIQUEAR E LER.
			//
			// A cena vem antes de tudo porque em cinematica o tique volta ANTES do `AplicarForma`
			// (ver `PassarACena`): quem medir sem queima-la mede o congelamento e conclui que o canal
			// nao existe. O Ki e reposto porque o dreno da forma nao e o assunto -- tanque vazio
			// derruba o degrau no meio da medida e o resto viraria medida da base.
			(double Physoff, double Kioff, double Tecnica, double Speed, double Cadencia,
			 double Acerta, double Levada, double Act, double Bp, double Pct) Medida(string id)
			{
				if (id != Catalogo.IdBase) Verbo(pl, "forma", id);
				PassarACena(pl);
				f.Ki = f.MaxKi;
				TickDaForma(pl, 0.1);
				Medir(pl);
				regua.expressedBP = f.expressedBP;
				return (f.Ephysoff, f.Ekioff, f.Etechnique, f.Espeed,
						CombatMath.Cadencia(f), CombatMath.Pontaria(f, regua, 0, 0),
						CombatMath.Pontaria(regua, f, 0, 0), f.Eactspeed, f.expressedBP, f.Inteireza);
			}

			var naBase = Medida(Catalogo.IdBase);
			var ssj1 = Medida("ssj1");
			Checa("[grades] (preparo) o corpo esta em Super Saiyajin DOMINADO", pl.Forma.Atual == "ssj1",
				  pl.Forma.Atual);

			var g2 = Medida("grade2");
			Checa("[grades] (preparo) ...e entrou no Grade 2 pela tecla de forma", pl.Forma.Atual == "grade2",
				  pl.Forma.Atual);

			var g3 = Medida("grade3");
			Checa("[grades] (preparo) ...e no Grade 3", pl.Forma.Atual == "grade3", pl.Forma.Atual);

			var ssj2 = Medida("ssj2");
			Checa("[grades] (preparo) ...e no Super Saiyajin 2", pl.Forma.Atual == "ssj2", pl.Forma.Atual);

			GD.Print("  --   forma      physoff   kioff  tecnica   speed  cadencia   acerta  e' acertado");
			void Linha(string nome, (double Physoff, double Kioff, double Tecnica, double Speed,
									 double Cadencia, double Acerta, double Levada, double Act,
									 double Bp, double Pct) m) =>
				GD.Print($"  --   {nome,-9}  {m.Physoff,7:0.###} {m.Kioff,7:0.###} {m.Tecnica,8:0.###} "
					   + $"{m.Speed,7:0.###} {m.Cadencia,8:0.###}s {m.Acerta,8:0.#}% {m.Levada,10:0.#}%");
			Linha("base", naBase);
			Linha("ssj1", ssj1);
			Linha("grade2", g2);
			Linha("grade3", g3);
			Linha("ssj2", ssj2);

			// ---------------------------------------------------------------- 1. MAIS FORTES (os dois eixos de ataque)
			Checa("[grades] o Grade 2 soca mais forte que o SSJ1 (physoff)", g2.Physoff > ssj1.Physoff,
				  $"{ssj1.Physoff:0.####} -> {g2.Physoff:0.####}");
			Checa("[grades] ...e o Grade 3 soca mais forte que o Grade 2", g3.Physoff > g2.Physoff,
				  $"{g2.Physoff:0.####} -> {g3.Physoff:0.####}");
			Checa("[grades] o Grade 2 tem mais ofensiva de KI que o SSJ1", g2.Kioff > ssj1.Kioff,
				  $"{ssj1.Kioff:0.####} -> {g2.Kioff:0.####}");
			Checa("[grades] ...e o Grade 3 mais que o Grade 2", g3.Kioff > g2.Kioff,
				  $"{g2.Kioff:0.####} -> {g3.Kioff:0.####}");

			// ---------------------------------------------------------------- 2. MAIS LENTOS -- CANAL DA VELOCIDADE
			Checa("[grades] o Grade 2 e mais lento que o SSJ1 (Espeed)", g2.Speed < ssj1.Speed,
				  $"{ssj1.Speed:0.####} -> {g2.Speed:0.####}");
			Checa("[grades] ...e o Grade 3 e mais lento que o Grade 2", g3.Speed < g2.Speed,
				  $"{g2.Speed:0.####} -> {g3.Speed:0.####}");

			// ---------------------------------------------------------------- 3. MAIS LENTOS -- CANAL DA CADENCIA
			//
			// SEGUNDOS ENTRE SOCOS: numero MAIOR e mais devagar. E um canal separado do `Espeed` de
			// verdade -- o `Eactspeed` das cinco medidas e o mesmo, entao a unica coisa que mudou a
			// cadencia foi o `formaCadencia`. Se um dia alguem ligar a cadencia ao `Espeed`, esta
			// checagem e a que avisa que passaram a ser um canal so.
			Checa("[grades] o Grade 2 soca mais DEVAGAR que o SSJ1 (segundos por golpe)",
				  g2.Cadencia > ssj1.Cadencia, $"{ssj1.Cadencia:0.####}s -> {g2.Cadencia:0.####}s");
			Checa("[grades] ...e o Grade 3 mais devagar que o Grade 2",
				  g3.Cadencia > g2.Cadencia, $"{g2.Cadencia:0.####}s -> {g3.Cadencia:0.####}s");
			Checa("[grades] e o `Eactspeed` nao se mexe em nenhuma delas -- a cadencia veio SO do canal da forma",
				  Math.Abs(ssj1.Act - g2.Act) < 1e-9 && Math.Abs(ssj1.Act - g3.Act) < 1e-9,
				  $"{ssj1.Act:0.###} / {g2.Act:0.###} / {g3.Act:0.###}");

			// ---------------------------------------------------------------- 4. MENOS CHANCE DE ACERTAR
			Checa("[grades] o Grade 2 ACERTA menos que o SSJ1 (tecnica contra a mesma regua)",
				  g2.Acerta < ssj1.Acerta, $"{ssj1.Acerta:0.##}% -> {g2.Acerta:0.##}%");
			Checa("[grades] ...e o Grade 3 acerta menos que o Grade 2",
				  g3.Acerta < g2.Acerta, $"{g2.Acerta:0.##}% -> {g3.Acerta:0.##}%");
			Checa("[grades] o Grade 2 e ACERTADO mais que o SSJ1 (a mesma regua batendo nele)",
				  g2.Levada > ssj1.Levada, $"{ssj1.Levada:0.##}% -> {g2.Levada:0.##}%");
			Checa("[grades] ...e o Grade 3 e acertado mais que o Grade 2",
				  g3.Levada > g2.Levada, $"{g2.Levada:0.##}% -> {g3.Levada:0.##}%");

			// ---------------------------------------------------------------- 5. A RELACAO QUE NAO ENVELHECE
			//
			// Tudo acima e "um e maior que o outro". Isto e a FORMA do desenho: cada grade e um desvio
			// do SSJ1 nos dois sentidos ao mesmo tempo (ganho na ofensiva, custo na velocidade), e o do
			// Grade 3 e maior nos dois. Um afinamento de numero passa por aqui; achatar um grade,
			// inverter os dois ou dar so bonus a um deles nao passa.
			double GanhoDe(double v, double b) => v / b - 1;
			double CustoDe(double v, double b) => 1 - v / b;

			bool ganhamOsDois = GanhoDe(g2.Physoff, ssj1.Physoff) > 0 && GanhoDe(g2.Kioff, ssj1.Kioff) > 0
							 && GanhoDe(g3.Physoff, ssj1.Physoff) > 0 && GanhoDe(g3.Kioff, ssj1.Kioff) > 0;
			bool pagamOsDois = CustoDe(g2.Speed, ssj1.Speed) > 0 && CustoDe(g2.Tecnica, ssj1.Tecnica) > 0
							&& CustoDe(g3.Speed, ssj1.Speed) > 0 && CustoDe(g3.Tecnica, ssj1.Tecnica) > 0;
			Checa("[grades] os DOIS grades ganham nos dois eixos de ataque (o Grade 2 tem pouco, mas tem)",
				  ganhamOsDois,
				  $"g2 +{GanhoDe(g2.Physoff, ssj1.Physoff) * 100:0.#}%/{GanhoDe(g2.Kioff, ssj1.Kioff) * 100:0.#}% "
				+ $"g3 +{GanhoDe(g3.Physoff, ssj1.Physoff) * 100:0.#}%/{GanhoDe(g3.Kioff, ssj1.Kioff) * 100:0.#}%");
			Checa("[grades] ...e os DOIS pagam em velocidade e em tecnica", pagamOsDois,
				  $"g2 -{CustoDe(g2.Speed, ssj1.Speed) * 100:0.#}%/{CustoDe(g2.Tecnica, ssj1.Tecnica) * 100:0.#}% "
				+ $"g3 -{CustoDe(g3.Speed, ssj1.Speed) * 100:0.#}%/{CustoDe(g3.Tecnica, ssj1.Tecnica) * 100:0.#}%");

			Checa("[grades] O GRADE 3 E MAIS EXTREMO NO GANHO que o Grade 2 (physoff e kioff)",
				  GanhoDe(g3.Physoff, ssj1.Physoff) > GanhoDe(g2.Physoff, ssj1.Physoff)
			   && GanhoDe(g3.Kioff, ssj1.Kioff) > GanhoDe(g2.Kioff, ssj1.Kioff),
				  $"{GanhoDe(g2.Physoff, ssj1.Physoff):0.####} vs {GanhoDe(g3.Physoff, ssj1.Physoff):0.####}");
			Checa("[grades] ...E MAIS EXTREMO NO CUSTO (velocidade, tecnica e cadencia)",
				  CustoDe(g3.Speed, ssj1.Speed) > CustoDe(g2.Speed, ssj1.Speed)
			   && CustoDe(g3.Tecnica, ssj1.Tecnica) > CustoDe(g2.Tecnica, ssj1.Tecnica)
			   && g3.Cadencia / ssj1.Cadencia > g2.Cadencia / ssj1.Cadencia,
				  $"speed {CustoDe(g2.Speed, ssj1.Speed):0.####} vs {CustoDe(g3.Speed, ssj1.Speed):0.####}");

			// ---------------------------------------------------------------- 6. O CONTRA-EXEMPLO
			//
			// ============================ NINGUEM MAIS FICOU LENTO ============================
			// O canal e do CATALOGO, e a base e as 38 formas sem `Mods` valem 1 em tudo. A prova de que
			// isso nao virou "todo mundo ficou lento" nao e ler o catalogo (isso e tautologia) -- e ler
			// o CORPO depois de entrar em cada forma. Tres checagens, e a do meio e a que importa:
			// o SSJ2 e medido VINDO DO GRADE 3, entao ela pega o defeito classico deste tipo de campo,
			// que e escrever e nunca limpar ("AFIRMA, nao acumula" -- ver `AplicarForma`).
			// ============================================================================
			bool ssj1Neutro = Math.Abs(ssj1.Physoff - naBase.Physoff) < 1e-9
						   && Math.Abs(ssj1.Speed - naBase.Speed) < 1e-9
						   && Math.Abs(ssj1.Cadencia - naBase.Cadencia) < 1e-9;
			Checa("[grades] CONTRA-EXEMPLO: o SSJ1 nao mexe em stat nenhum (nem forca, nem velocidade)",
				  ssj1Neutro, $"physoff {naBase.Physoff:0.####}/{ssj1.Physoff:0.####} "
							+ $"speed {naBase.Speed:0.####}/{ssj1.Speed:0.####}");

			bool ssj2Neutro = Math.Abs(ssj2.Physoff - naBase.Physoff) < 1e-9
						   && Math.Abs(ssj2.Speed - naBase.Speed) < 1e-9
						   && Math.Abs(ssj2.Cadencia - naBase.Cadencia) < 1e-9;
			Checa("[grades] ...e o SSJ2 VINDO DO GRADE 3 volta ao normal (o campo e afirmado, nao acumulado)",
				  ssj2Neutro, $"physoff {naBase.Physoff:0.####}/{ssj2.Physoff:0.####} "
							+ $"speed {naBase.Speed:0.####}/{ssj2.Speed:0.####} "
							+ $"cadencia {naBase.Cadencia:0.####}/{ssj2.Cadencia:0.####}");

			// E OS SETE CAMPOS DO CORPO SAO OS DO CATALOGO DA FORMA ATUAL, nas cinco formas visitadas.
			// E daqui que sai a garantia geral: uma forma so fica lenta se a ENTRADA DELA disser isso.
			bool seteBatem = true;
			string erradas = "";
			foreach (string id in new[] { Catalogo.IdBase, "ssj1", "grade2", "grade3", "ssj2" })
			{
				if (id != Catalogo.IdBase) Verbo(pl, "forma", id);
				else while (!pl.Forma.NaBase && pl.Forma.Def != null) Transformar(pl, subir: false);
				PassarACena(pl);
				f.Ki = f.MaxKi;
				TickDaForma(pl, 0.1);

				ModsDeForma m = Catalogo.Def(pl.Forma.Atual)?.Mods ?? Catalogo.SemMods;
				bool bate = Math.Abs(f.formaPhysoff - m.Physoff) < 1e-12
						 && Math.Abs(f.formaPhysdef - m.Physdef) < 1e-12
						 && Math.Abs(f.formaKioff - m.Kioff) < 1e-12
						 && Math.Abs(f.formaKidef - m.Kidef) < 1e-12
						 && Math.Abs(f.formaTecnica - m.Tecnica) < 1e-12
						 && Math.Abs(f.formaSpeed - m.Speed) < 1e-12
						 && Math.Abs(f.formaCadencia - m.Cadencia) < 1e-12;
				if (!bate) { seteBatem = false; erradas += $" {pl.Forma.Atual}"; }
			}
			Checa("[grades] os SETE campos do corpo sao os do catalogo da forma atual, nas 5 formas visitadas",
				  seteBatem, erradas.Length > 0 ? $"divergiram em:{erradas}" : "");

			// ---------------------------------------------------------------- 7. E ELES FICAM FORA DO PODER
			//
			// ============================ A BANCADA INJETA O DEFEITO ELA MESMA ============================
			// Duas perguntas de uma vez, e uma nao vale sem a outra:
			//
			//   * apagar os mods do Grade 3 NAO pode mexer no `expressedBP` -- stat de forma e `statify`,
			//     nao `powerlevel()`; um Grade 3 lento marca o MESMO numero no scouter, e e por isso que
			//     ele e "lento" e nao "mais fraco";
			//   * mas TEM que mexer no `Espeed`. Sem esta segunda metade, a primeira ficaria verde com o
			//     canal inteiro morto -- que e exatamente o estado de onde este trabalho partiu.
			//
			// A troca e no campo `FormaDef.Mods` do catalogo vivo e e desfeita no `finally` de fora
			// (a entrada e a mesma instancia que o jogo le, entao esquecer de repor deixaria o servidor
			// sem grades ate o proximo boot).
			// ==========================================================================================
			FormaDef? defG3 = Catalogo.Def("grade3");
			ModsDeForma? modsG3 = defG3?.Mods;
			try
			{
				Verbo(pl, "forma", "grade3");
				PassarACena(pl);
				f.Ki = f.MaxKi;
				TickDaForma(pl, 0.1);
				Medir(pl);
				double comMods = f.expressedBP, speedComMods = f.Espeed, pctComMods = f.Inteireza;

				if (defG3 != null) defG3.Mods = Catalogo.SemMods;
				f.Ki = f.MaxKi;
				TickDaForma(pl, 0.1);
				Medir(pl);
				double semMods = f.expressedBP, speedSemMods = f.Espeed, pctSemMods = f.Inteireza;

				Checa("[grades] apagar os mods do Grade 3 NAO mexe no poder (stat e statify, nao powerlevel)",
					  Math.Abs(comMods - semMods) < 1e-6, $"{comMods:0} -> {semMods:0}");
				Checa("[grades] ...nem na % de BP efetivo", Math.Abs(pctComMods - pctSemMods) < 1e-12,
					  $"{pctComMods:0.######} -> {pctSemMods:0.######}");
				Checa("[grades] ...mas MEXE no Espeed -- prova de que a bancada leu o catalogo de verdade",
					  Math.Abs(speedComMods - speedSemMods) > 1e-9,
					  $"{speedComMods:0.####} -> {speedSemMods:0.####}");
			}
			finally
			{
				if (defG3 != null) defG3.Mods = modsG3;
				TickDaForma(pl, 0.1);
			}

			// ---------------------------------------------------------------- 8. OS DOIS DONOS DA CADENCIA
			//
			// `hitspeedMod` e do EQUIPAMENTO (`Equipment.dm:289/307/329/349/387`, ainda sem escritor no
			// port) e `formaCadencia` e da forma. Sao campos separados porque a forma AFIRMA o valor
			// dela a cada `AplicarForma`: escrever os dois no mesmo campo apagaria a espada do sujeito
			// toda vez que ele transformasse -- calado, e so ate ele desequipar pra descobrir.
			Verbo(pl, "forma", "grade3");
			PassarACena(pl);
			f.Ki = f.MaxKi;
			TickDaForma(pl, 0.1);
			Medir(pl);
			double soAForma = CombatMath.Cadencia(f);

			f.hitspeedMod = 2;                   // uma arma que acelera o soco
			AplicarForma(pl);                    // a forma se reafirma por cima
			double comArma = CombatMath.Cadencia(f);

			Checa("[grades] o `hitspeedMod` do equipamento SOBREVIVE ao `AplicarForma` (a forma nao apaga a arma)",
				  Math.Abs(f.hitspeedMod - 2) < 1e-12, $"{f.hitspeedMod:0.###}");
			Checa("[grades] ...e os dois canais compoem: com a arma o Grade 3 soca na metade do tempo",
				  Math.Abs(comArma - soAForma / 2) < 1e-9, $"{soAForma:0.####}s -> {comArma:0.####}s");

			f.hitspeedMod = 1;
			AplicarForma(pl);
		}
		finally
		{
			while (!pl.Forma.NaBase && pl.Forma.Def != null) Transformar(pl, subir: false);
			f.hitspeedMod = hitspeedAntes;
			f.Ki = Math.Min(kiAntes, f.MaxKi);
			AplicarForma(pl);
		}
	}

	// =====================================================================
	// 3. A PREFERENCIA: O SSJ1 **CRU**, E O DISCO DE VERDADE
	// =====================================================================
	/// <summary>
	/// ============================ O QUE ESTA SECAO ACRESCENTA AO `OsGradesNoCaminhoDoC` ============================
	/// Aquele bloco mede o caminho da tecla C com o SSJ1 **dominado** -- que e o caso onde a
	/// preferencia mais aparece, porque o seletor "mais forte vence" apaga os grades (3x e 4x) de um
	/// SSJ1 de 6x. Faltavam as duas outras metades do pedido:
	///
	///   * *"no ssj1 (**masterizado ou nao**)"*. Com maestria 50 o SSJ1 vale 2x e o Grade 2 vale 3x --
	///     ou seja o seletor de sempre JA passaria por ele, e desligar os grades tem que TIRA-LO do
	///     caminho mesmo assim. E o Grade 3 (que pede 70%) continua trancado: a escada com os grades
	///     ligados e `ssj1 -> grade2 -> ssj2`, e nao "trava no Grade 2";
	///   * **o disco de verdade**. Aquele bloco vai ate o `CharacterSave` em memoria; o campo, porem,
	///     atravessa um arquivo .json. Um `bool?` que virasse propriedade sem `set`, ou que ganhasse um
	///     `[JsonIgnore]`, sumiria exatamente ali -- e a bancada em memoria continuaria verde.
	/// ============================================================================================================
	///
	/// ============================ O GATE POR DESPERTAR, QUE E OUTRO GATE ============================
	/// O `OsGradesNoCaminhoDoC` prova que desligar os grades nao fura o gate de **BP** do SSJ2. Aqui e
	/// o gate de **despertar** (o tronco Saiyajin desperta no luto): a recusa tem que ser a do SSJ2,
	/// e nao a maestria do Grade -- e no mesmo estado, com os grades LIGADOS, o Grade 2 tem que ser
	/// oferecido. Sem esse segundo par, "nao foi pro grade" seria indistinguivel de "nao havia grade".
	/// ==========================================================================================
	/// </summary>
	private void APreferenciaNoSsj1CruENoDisco(ServerPlayer pl, Action<string, bool, string> Checa,
											   Func<List<string>> Ouvido)
	{
		Fighter f = pl.Ficha;

		void ABase()
		{
			while (!pl.Forma.NaBase && pl.Forma.Def != null) Transformar(pl, subir: false);
			f.Ki = f.MaxKi;
			f.KO = false;
			f.dead = false;
		}

		string SobeUm()
		{
			f.Ki = f.MaxKi;
			Transformar(pl, subir: true);
			return pl.Forma.Atual;
		}

		// ---------------------------------------------------------------- 1. O SSJ1 **CRU** (maestria 50)
		ABase();
		f.BP = 1e13;
		pl.Forma.Liberar("ssj1");
		pl.Forma.Liberar("ssj2");
		pl.Forma.Maestria.Por("ssj1", Catalogo.Grade2Pct);
		pl.Forma.GradesLigados = true;
		Verbo(pl, "forma", "ssj1");
		PassarACena(pl);

		Checa("[graus] (preparo) o corpo esta num SSJ1 NAO dominado", pl.Forma.Atual == "ssj1"
			  && pl.Forma.Maestria.De("ssj1") < 100,
			  $"{pl.Forma.Atual} com {pl.Forma.Maestria.De("ssj1"):0}% de maestria");

		Checa("[graus] ligados, o C sai do SSJ1 CRU pro Grade 2", SobeUm() == "grade2", pl.Forma.Atual);
		PassarACena(pl);

		// O GRADE 3 PEDE 70% E ELE TEM 50: a escada nao pode TRAVAR no Grade 2 -- ela escorrega pro
		// tronco. E o `ProximoRamoLateral` devolvendo nulo e a escolha caindo no caminho normal.
		Checa("[graus] ...e com o Grade 3 ainda trancado por maestria, o C segue pro SSJ2 (nao trava)",
			  SobeUm() == "ssj2", pl.Forma.Atual);

		ABase();
		pl.Forma.GradesLigados = false;
		Verbo(pl, "forma", "ssj1");
		PassarACena(pl);
		Checa("[graus] desligados, o C sai do SSJ1 CRU direto pro SSJ2 (o grade nao entra no caminho)",
			  SobeUm() == "ssj2", pl.Forma.Atual);

		// ---------------------------------------------------------------- 2. O RAMO NAO AFROUXA GATE NENHUM
		//
		// ============================ E O CONTRAPONTO E METADE DA MEDIDA ============================
		// O bloco irmao prova que, com o SSJ2 trancado por BP e os grades DESLIGADOS, o C recusa em vez
		// de desviar pro grade. Sozinha, aquela checagem tambem ficaria verde num jogo onde nao houvesse
		// grade nenhum pra oferecer -- ela nao distingue "o desvio foi tirado do caminho" de "nunca
		// houve desvio". Aqui o MESMO estado e medido dos dois lados da preferencia: com os grades
		// ligados o Grade 2 aparece, com eles desligados o mesmo corpo ouve a recusa do SSJ2.
		//
		// O `Limiares = null` e pra a porta ser a CONSTANTE do catalogo: o limiar sorteado no nascimento
		// deste corpo pode estar abaixo do BP escrito aqui, e a bancada mediria "nao ha recusa" achando
		// que mediu a recusa certa.
		// ========================================================================================
		ABase();
		pl.Forma.Maestria.Por("ssj1", 100);
		LimiaresPessoais? limiaresAntes = pl.Forma.Limiares;
		pl.Forma.Limiares = null;
		f.BP = Catalogo.PortaSsj2 - 1;
		pl.Forma.GradesLigados = true;
		Verbo(pl, "forma", "ssj1");
		PassarACena(pl);
		Checa("[graus] com o SSJ2 fora de alcance por BP e os grades LIGADOS, o C oferece o Grade 2",
			  SobeUm() == "grade2", pl.Forma.Atual);
		PassarACena(pl);

		ABase();
		pl.Forma.GradesLigados = false;
		Verbo(pl, "forma", "ssj1");
		PassarACena(pl);
		EscutaDeAvisos = [];
		Transformar(pl, subir: true);
		List<string> semPoder = Ouvido();
		Checa("[graus] ...e no MESMO estado, desligados, ele NAO desvia -- fica onde esta",
			  pl.Forma.Atual == "ssj1", pl.Forma.Atual);
		Checa("[graus] ...e a recusa e a do SSJ2, sem falar da maestria do Grade",
			  semPoder.Any(a => a.Contains("Super Saiyajin 2", StringComparison.Ordinal))
			  && !semPoder.Any(a => a.Contains("Grade", StringComparison.OrdinalIgnoreCase)),
			  string.Join(" | ", semPoder));

		// ---------------------------------------------------------------- 2a. O UNICO ESTADO ONDE `NoCaminhoDoC` E QUEM SEGURA
		//
		// ============================ ISTO AQUI FOI ACHADO POR INJECAO, E NAO POR LEITURA ============================
		// Com o `NoCaminhoDoC` trocado por `=> true` (ou seja: desligar os grades deixa de tira-los do
		// caminho) a bancada inteira ficou **VERDE** -- 634 de 634. O motivo e que o seletor do
		// `Proxima` so aceita degrau que valha MAIS que a forma atual, e nos estados medidos ate aqui o
		// grade ja perdia por multiplicador: com o SSJ1 **dominado** (6x) o Grade 2 (3x) nem entra na
		// disputa, e com o SSJ2 alcancavel (8x) ele ganha de todo mundo.
		//
		// Sobra UM estado onde a preferencia e a unica coisa segurando o desvio, e e este: **SSJ1 CRU**
		// (2x, mas com maestria suficiente pro Grade 2) e **SSJ2 fora de alcance por BP**. Ai o Grade 2
		// vale 3x, e maior que os 2x de agora, esta liberado -- e a unica razao pra o C nao ir pra la e
		// o jogador ter desligado os grades.
		//
		// E O CONTRAPONTO VEM JUNTO pelo mesmo motivo de sempre: no mesmo estado, com os grades ligados,
		// o C TEM que ir pro Grade 2. Sem essa metade, "nao foi" nao se distingue de "nao havia".
		// ==========================================================================================
		ABase();
		pl.Forma.Maestria.Por("ssj1", Catalogo.Grade2Pct);
		pl.Forma.Limiares = null;
		f.BP = Catalogo.PortaSsj2 - 1;

		pl.Forma.GradesLigados = false;
		Verbo(pl, "forma", "ssj1");
		PassarACena(pl);
		EscutaDeAvisos = [];
		Transformar(pl, subir: true);
		List<string> cruSemGrade = Ouvido();
		Checa("[graus] SSJ1 CRU (2x) + SSJ2 trancado por BP + grades DESLIGADOS: o C recusa e NAO cai no Grade 2 (3x)",
			  pl.Forma.Atual == "ssj1", pl.Forma.Atual);
		Checa("[graus] ...e a recusa continua sendo a do SSJ2",
			  cruSemGrade.Any(a => a.Contains("Super Saiyajin 2", StringComparison.Ordinal)),
			  string.Join(" | ", cruSemGrade));

		ABase();
		pl.Forma.GradesLigados = true;
		Verbo(pl, "forma", "ssj1");
		PassarACena(pl);
		Checa("[graus] ...e no MESMO estado, LIGADOS, ele vai pro Grade 2 (3x contra os 2x de agora)",
			  SobeUm() == "grade2", pl.Forma.Atual);
		PassarACena(pl);

		// ---------------------------------------------------------------- 2b. NEM O RAMO PULA A PORTA DELE
		//
		// A outra metade da mesma regra: `ProximoRamoLateral` passa cada candidato pelo `Avaliar`
		// inteiro. Com maestria abaixo de 50% o Grade 2 nao existe pra a tecla C, e a escolha escorrega
		// pro tronco em vez de travar. Uma preferencia que virasse atalho ("ligou, entao pode") seria
		// exatamente o portao pulado que o `TransformarPara` se recusou a ser.
		ABase();
		f.BP = 1e13;
		pl.Forma.GradesLigados = true;
		pl.Forma.Maestria.Por("ssj1", Catalogo.Grade2Pct - 10);
		Verbo(pl, "forma", "ssj1");
		PassarACena(pl);
		Checa("[graus] com maestria ABAIXO da porta do Grade 2, o C ligado nao o oferece -- vai pro SSJ2",
			  SobeUm() == "ssj2", pl.Forma.Atual);

		pl.Forma.Maestria.Por("ssj1", 100);
		pl.Forma.Limiares = limiaresAntes;

		// ---------------------------------------------------------------- 3. O DISCO DE VERDADE
		ABase();
		bool temDisco = _store != null && pl.Peer != null && pl.Slot >= 0
					 && _contas.TryGetValue(pl.Peer, out AccountSave? conta) && conta != null;
		Checa("[graus] (preparo) o corpo de bancada tem conta e disco -- senao este bloco nao mede nada",
			  temDisco, $"store={_store != null} peer={pl.Peer != null} slot={pl.Slot}");
		if (!temDisco) return;

		AccountSave acc = _contas[pl.Peer!];
		string caminho = Path.Combine(_store!.Pasta, AccountStore.NomeDeArquivo(acc.Conta) + ".json");

		bool? NoArquivo()
		{
			Persistir(pl);
			AccountSave? lida = _store.Carregar(acc.Conta);
			return lida?.Slots[pl.Slot]?.GradesLigados;
		}

		// O VERB DESLIGA E O DISCO GUARDA. `Verbo` e nao `pl.Forma.GradesLigados = false`: o assunto e
		// a cadeia inteira (switch -> `VerboGrades` -> `Persistir` -> JSON), e escrever o campo a mao
		// pularia justamente as casas que ja perderam escolha de jogador neste projeto.
		Verbo(pl, "graus", "");
		bool? desligadoNoDisco = NoArquivo();
		Checa("[graus] o verb desliga e o ARQUIVO .json guarda `false`", desligadoNoDisco == false,
			  $"{desligadoNoDisco?.ToString() ?? "nulo"}");

		Verbo(pl, "graus", "");
		bool? ligadoNoDisco = NoArquivo();
		Checa("[graus] ...e ligar de novo guarda `true` (o campo nao e so 'apagado')", ligadoNoDisco == true,
			  $"{ligadoNoDisco?.ToString() ?? "nulo"}");

		// E ELE VOLTA DO DISCO PRO CORPO, pelo funil do login.
		Verbo(pl, "graus", "");
		Persistir(pl);
		pl.Forma.GradesLigados = true;                       // o corpo "esquece" de proposito
		AccountSave? doDisco = _store.Carregar(acc.Conta);
		RestaurarFormaEDisciplina(pl, doDisco?.Slots[pl.Slot]);
		Checa("[graus] o login traz a preferencia DE VOLTA DO ARQUIVO pro corpo",
			  pl.Forma.GradesLigados == false, $"{pl.Forma.GradesLigados?.ToString() ?? "nulo"}");

		// ---------------------------------------------------------------- 4. O SAVE DE ANTES DO VERB
		//
		// ============================ ESTE E O UNICO JEITO HONESTO DE MEDIR MIGRACAO ============================
		// O bloco irmao poe `GradesLigados = null` no objeto em memoria. Isso prova a TRADUCAO, nao a
		// LEITURA: um save de verdade de antes desta mudanca nao tem `null` escrito -- ele nao tem a
		// PROPRIEDADE. Sao coisas diferentes pro desserializador, e a diferenca so aparece no texto.
		//
		// Entao a linha e apagada do .json com a mao, e o arquivo e lido pelo `AccountStore` de sempre.
		// Um campo que virasse `bool` cru chegaria `false` aqui e desligaria os grades do servidor
		// inteiro, calado, no primeiro login depois da atualizacao.
		// ====================================================================================================
		// APAGAR A LINHA INTEIRA NAO SERVE, e a primeira versao desta bancada reprovou por isso: o campo
		// e o ULTIMO da classe (foi acrescentado no fim de proposito), entao tirar a linha deixa uma
		// virgula sobrando antes do `}`. O JSON fica invalido, o `Carregar` devolve nulo -- e a checagem
		// reprova por sintaxe em vez de medir migracao. A virgula sai junto com o campo.
		string texto = File.ReadAllText(caminho);
		string semCampo = System.Text.RegularExpressions.Regex.Replace(
			texto, ",?\\s*\"GradesLigados\"\\s*:\\s*(true|false|null)\\s*,?", m =>
				m.Value.TrimStart().StartsWith(',') && m.Value.TrimEnd().EndsWith(',') ? "," : "");
		Checa("[graus] (preparo) o campo `GradesLigados` existia mesmo no .json e foi removido pro teste",
			  semCampo.Length < texto.Length && !semCampo.Contains("GradesLigados", StringComparison.Ordinal),
			  $"{texto.Length} -> {semCampo.Length} bytes");
		File.WriteAllText(caminho, semCampo);

		AccountSave? antiga = _store.Carregar(acc.Conta);
		CharacterSave? slotAntigo = antiga?.Slots[pl.Slot];
		Checa("[graus] (preparo) ...e o .json sem o campo continua LEGIVEL (senao o teste abaixo mede sintaxe)",
			  antiga != null && slotAntigo != null,
			  antiga == null ? "arquivo ilegivel" : "slot vazio");
		Checa("[graus] um save de ANTES do verb (campo ausente do .json) chega NULO, e nao `false`",
			  slotAntigo != null && slotAntigo.GradesLigados == null,
			  $"{slotAntigo?.GradesLigados?.ToString() ?? "nulo"}");

		pl.Forma.GradesLigados = false;
		RestaurarFormaEDisciplina(pl, slotAntigo);
		Checa("[graus] ...e o login o traduz pra LIGADO -- o personagem antigo continua com a escada dele",
			  pl.Forma.GradesLigados == true, $"{pl.Forma.GradesLigados?.ToString() ?? "nulo"}");

		// O ARQUIVO VOLTA A SER VALIDO: a bancada mexeu no .json de uma conta viva, e deixa-lo sem o
		// campo faria o proximo save-por-cima parecer bom enquanto o disco continuava de ontem.
		Persistir(pl);
	}
}
