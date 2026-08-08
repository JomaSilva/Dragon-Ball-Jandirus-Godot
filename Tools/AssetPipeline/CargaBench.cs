using System.Linq;
﻿using Jandirus.Core.Combat;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DA CARGA DE KI (`dotnet run --project Tools/AssetPipeline -- carga [races.json]`).
///
/// POR QUE ELA EXISTE. Carregar Ki e um sistema que so se ve em jogo depois de segurar uma tecla
/// por dez segundos, e o que ele produz -- BP -- e justamente o numero que o jogo esconde do
/// jogador. Ou seja: a UNICA forma de saber se as tres etapas acontecem, em quanto tempo, e quanto
/// de poder rendem, e medir fora do jogo. Sem isto o sistema so poderia ser conferido por "parece
/// que subiu".
///
/// O QUE ELA PROVA, em ordem:
///   1. As duas CHAVES separam mesmo (sem Ki Unlocked nada acontece; sem Ki Control nao passa dos 100%).
///   2. O TEMPO de cada etapa bate com o do original (9 s pra encher com controle, 15 s sem).
///   3. O BP SOBE de verdade ao ultrapassar -- e quanto.
///   4. O EXCESSO cobra: acima de `kicapacity` sai dano e o Ki vaza sozinho de volta.
/// </summary>
public static class CargaBench
{
	/// <summary>O tick do servidor. Medir na cadencia real e o que torna os segundos comparaveis.</summary>
	private const double Dt = 1.0 / 30.0;

	/// <summary>
	/// ============================ O PLACAR, E POR QUE ELE NAO EXISTIA ============================
	/// Esta bancada nasceu pra DIAGNOSTICAR: ela imprimia tabelas e quem lia decidia. Isso serviu
	/// enquanto havia alguem olhando -- e e exatamente o que faz uma bancada envelhecer calada. O
	/// `SegurandoEmForma` chegou a contar `reprovadas` e a imprimir "N forma(s) presa(s) no teto", e o
	/// programa devolvia **0** do mesmo jeito: o defeito do SSJ3 podia voltar inteiro sem nada
	/// reprovar, e a linha se perderia no meio de 200 linhas de tabela.
	///
	/// Agora as afirmacoes passam por aqui, no formato das outras bancadas do projeto
	/// (`FormasBench`), e o `Run` devolve o numero de falhas -- que o `Program` usa como codigo de
	/// saida. As tabelas continuam sendo impressas: elas sao o diagnostico de quando uma linha cai.
	/// ==========================================================================================
	/// </summary>
	private static int _ok, _falhou;

	private static void Conferir(bool ok, string oque)
	{
		if (ok) { _ok++; Console.WriteLine($"  ok     {oque}"); }
		else { _falhou++; Console.WriteLine($"  FALHA  {oque}"); }
	}

	public static int Run(RaceCatalog? cat)
	{
		Console.WriteLine("=== CARGA DE KI (a tecla C segurada) ===\n");
		_ok = _falhou = 0;
		Elo();
		Chaves(cat);
		Tempos(cat);
		Poder(cat);
		Preco(cat);
		Segurando(cat);
		SegurandoEmForma();
		TetoPorForma();

		Console.WriteLine(_falhou == 0
			? $"\n===== TUDO OK ===== ({_ok} checagens)"
			: $"\n===== {_falhou} FALHA(S) ===== ({_ok} ok)");
		return _falhou;
	}

	/// <summary>
	/// SEGURAR O C **DENTRO DE UMA FORMA** -- a queixa do dono: "nao consigo passar do 100% de ki ao
	/// carregar o ki no ssj3; na base funciona".
	///
	/// ============================ POR QUE ESTE TESTE E DIFERENTE DO DE CIMA ============================
	/// O <see cref="Segurando"/> mede o corpo na BASE, onde nada disputa o Ki com a tecla C. Dentro de
	/// uma forma ha um SEGUNDO relogio mexendo no mesmo tanque: o `TickDaForma` cobra
	/// `DrenoPorSegundo() * MaxKi` por segundo, e ele roda ANTES da carga no mesmo tique
	/// (`GameServer.cs:2012-2018`). Medir a carga sozinha nao pode revelar isso -- o saldo e a SOMA
	/// dos dois, e so um deles mora no `CargaDeKi`.
	///
	/// Por isso este teste repete a ordem inteira do servidor:
	///   1) dreno da forma (`GameServer.Formas.cs:285`)
	///   2) regeneracao, com o `formRegen` de 20% que o dreno liga (`GameServer.Carga.cs:120-121`)
	///   3) a tecla C (`CargaDeKi.Passo`)
	///   4) o preco do excesso
	///   5) `Statify`
	///
	/// O `trueKiMod` da forma entra junto porque ele MULTIPLICA o MaxKi -- e a suspeita obvia ("o
	/// tanque de 350 demora 3,5x mais pra encher") tem que ser descartada com numero, nao com leitura:
	/// todas as taxas da carga sao fracao do MaxKi, entao o TEMPO nao muda com o teto. O que muda e o
	/// dreno, que e fracao do MaxKi TAMBEM -- e a comparacao dos dois em %/s e a resposta.
	/// ================================================================================================
	/// </summary>
	private static void SegurandoEmForma()
	{
		Console.WriteLine("-- SEGURANDO O C DENTRO DE CADA FORMA (ordem do servidor: dreno -> regen -> carga) --");

		string cs = "Assets/Data/skills.json", ct = "Assets/Data/skilltrees.json", cn = "Assets/Data/niveis.json";
		if (!File.Exists(cs) || !File.Exists(ct) || !File.Exists(cn)) { Console.WriteLine("  (sem os .json)\n"); return; }
		var sc = Jandirus.Core.Skills.SkillCatalog.Parse(File.ReadAllText(cs), File.ReadAllText(ct));
		Jandirus.Core.Skills.RegrasDoDisco.Carregar(File.ReadAllText(cn));

		const double dt = 1.0 / 30.0;
		const int Segundos = 30;

		// ============================ O TETO E O DAQUELA FORMA, E ISSO PRECISA SER PROVADO ============================
		// A afirmacao do dono e "passa de 100% do teto DAQUELA forma". Medir contra `f.MaxKi` ja faz
		// isso -- so que faria igual se o `TetoDeKi` devolvesse 1,0 pra tudo: as cinco medicoes seriam
		// a MESMA medicao repetida, com o tanque da base, e o placar sairia verde afirmando cinco
		// coisas quando afirmou uma. Este dicionario guarda o MaxKi de cada uma e a linha logo abaixo
		// do laco cobra que os cinco sejam diferentes.
		//
		// Nao e paranoia: o `TetoPorForma` deste mesmo arquivo existe porque o catalogo JA ESTEVE
		// assim -- "a escada estava no catalogo e as 36 entradas valiam 1,0x".
		// ========================================================================================================
		var tanque = new Dictionary<string, double>();
		var cruzouEm = new Dictionary<string, double>();

		// O MESMO corpo do `--kiteste` toda vez: BP de bancada e as duas chaves do Ki liberado. Virou
		// funcao porque cada forma e medida DUAS vezes -- do tanque cheio (o pico) e do tanque VAZIO
		// (o tempo) --, e um corpo reaproveitado entre as duas levaria folego e `powerMod` gastos da
		// primeira pra segunda, que e justamente o que faria o tempo medido depender da ordem.
		Fighter Corpo(string id)
		{
			var novo = new Fighter { Race = "Saiyan", BP = 3_000_000 };
			novo.Statify();
			Jandirus.Core.Skills.EfeitosDeSkill.Aplicar(novo, sc, ["/datum/skill/mind/Ki_Unlocked"]);
			var nv = new Jandirus.Core.Skills.NiveisDeSkill();
			nv.Por("/datum/skill/mind/Basic_Ki_Control", 5);
			nv.Aplicar(novo);
			novo.Statify();
			novo.trueKiMod = Jandirus.Core.Forms.Catalogo.TetoDeKi(Jandirus.Core.Forms.Catalogo.Def(id), novo);
			novo.Statify();
			return novo;
		}

		int reprovadas = 0;
		foreach (string id in new[] { Jandirus.Core.Forms.Catalogo.IdBase, "ssj1", "ssj2", "ssj3", "ssj4" })
		{
			Fighter f = Corpo(id);
			f.Ki = f.MaxKi;   // parte do tanque cheio: o que se mede aqui e o TERCEIRO estagio

			// MAESTRIA ZERO de proposito -- e o estado de quem acabou de destravar a forma, que e
			// exatamente quem reclama. Maestria alta alivia o dreno (ver `Catalogo.DrenoPorSegundo`).
			var maestria = new Jandirus.Core.Forms.Maestrias();
			double drenoFrac = Jandirus.Core.Forms.Catalogo.DrenoPorSegundo(id, maestria);

			Console.WriteLine($"\n  [{id}]  trueKiMod={f.trueKiMod:0.00}  MaxKi={f.MaxKi:0.0}  "
							+ $"teto da carga={CargaDeKi.TetoDeCarga(f):0.0}  dreno da forma={drenoFrac * 100:0.00}%/s");

			tanque[id] = f.MaxKi;

			double pico = f.Ki / f.MaxKi;
			for (int t = 0; t < (int)(Segundos / dt); t++)
			{
				// 1) O DRENO DA FORMA, primeiro -- e a ordem do Tick() do servidor.
				double dreno = drenoFrac * f.MaxKi * dt;
				f.Ki -= dreno;

				// 2) regeneracao (com o corte de 20% que o dreno liga), 3) a tecla C, 4) o preco
				Jandirus.Core.Stats.RegenDeKi.Passo(f, dt, drenoFrac);
				EstagioDaCarga e = CargaDeKi.Passo(f, dt, mexendo: false);
				double dano = CargaDeKi.PrecoDoExcesso(f, dt);
				f.Statify();

				pico = Math.Max(pico, f.Ki / f.MaxKi);

				if (t % 150 == 0 || t == (int)(Segundos / dt) - 1)
					Console.WriteLine($"    {t * dt,4:0}s  Ki={f.Ki,7:0.0} ({f.Ki / f.MaxKi * 100,6:0.0}%)  "
									+ $"estagio={e,-14}  folego={f.staminapercent * 100,5:0.0}%  dano={dano:0.000}");
			}

			bool passou = pico > 1.01;
			if (!passou) reprovadas++;
			Console.WriteLine($"    pico = {pico * 100:0.0}% do MaxKi   "
							+ (passou ? "OK: passou dos 100%." : "<<< NAO PASSA DOS 100% -- e a queixa do dono."));

			// ============================ E AGORA O TEMPO, DO TANQUE VAZIO ============================
			// O laco de cima comeca EM 100% (ele mede o terceiro estagio), e por isso o instante da
			// travessia ali nao vale nada: sai um tique em qualquer forma, inclusive numa que estivesse
			// quebrada logo depois. O "em quanto tempo" so tem sentido a partir do estado em que o
			// jogador aperta a tecla -- tanque vazio -- porque e a soma dos TRES estagios:
			//
			//   1) encher ate o MaxKi, 2) retomar o `powerMod`, 3) ultrapassar.
			//
			// E ele mede exatamente o que quebrou: a barreira absorvente do clamp era um defeito do
			// PRIMEIRO estagio que so aparecia no terceiro. Um numero medido daqui atravessa os tres.
			//
			// O CORPO E NOVO (ver `Corpo`): folego e `powerMod` gastos pelos 30 s de cima mudariam o
			// tempo medido, e o `PrecoDoExcesso` roda aqui pelo mesmo motivo que roda la -- e a ordem
			// do `Tick()` do servidor, e ele e quem cobra o folego que o estagio 3 consome.
			// =====================================================================================
			Fighter g = Corpo(id);
			g.Ki = 0;
			const int TetoDaMedida = 60;
			double cruzou = -1, encheu = -1;
			for (int t = 0; t < (int)(TetoDaMedida / dt) && cruzou < 0; t++)
			{
				g.Ki -= drenoFrac * g.MaxKi * dt;
				Jandirus.Core.Stats.RegenDeKi.Passo(g, dt, drenoFrac);
				CargaDeKi.Passo(g, dt, mexendo: false);
				CargaDeKi.PrecoDoExcesso(g, dt);
				g.Statify();

				if (encheu < 0 && g.Ki >= g.MaxKi) encheu = (t + 1) * dt;
				if (g.Ki > g.MaxKi) cruzou = (t + 1) * dt;
			}
			cruzouEm[id] = cruzou;
			Console.WriteLine($"    do ZERO: enche em {(encheu < 0 ? "nunca" : $"{encheu:0.00}s")}, "
							+ $"cruza os 100% em {(cruzou < 0 ? "NUNCA" : $"{cruzou:0.00}s")}");

			// ============================ AS DUAS AFIRMACOES, POR FORMA ============================
			// COMO REPROVAM SE A REGRA SUMIR: devolva o `f.Ki = Math.Min(f.Ki + ..., f.MaxKi); return;`
			// ao estagio 1 do `CargaDeKi.Passo` -- o clamp vira barreira absorvente no MaxKi assim que
			// ha um dreno concorrente, e o `ssj3` (o pior dreno da linha) cai nas duas linhas: nunca
			// cruza e para no pico de 100,0%. As outras quatro caem junto ou nao conforme o `troco` do
			// `RegenDeKi` as empurre por cima -- e essa e a razao de as CINCO serem medidas, e nao so
			// a reclamada: o sistema ja funcionou por acidente em quatro delas.
			//
			// O TETO DE 20 s existe pra pegar o defeito LENTO, que o `cruzou >= 0` sozinho nao pega:
			// uma carga que so cruzasse aos 45 s passaria no "cruzou em algum momento" e em jogo seria
			// a MESMA queixa do dono ("nao passa dos 100%"), porque ninguem segura a tecla meio minuto.
			// E ele nao e chute: o pior dos cinco enche em 9 s e cruza logo depois, entao 20 s e o
			// dobro com folga -- apertado o bastante pra acusar uma regressao de ritmo, largo o
			// bastante pra nao cair por um ajuste de balanceamento.
			// ==================================================================================
			Conferir(cruzou >= 0,
					 $"[{id}] segurando C do zero, a carga PASSA dos 100% do teto da propria forma "
				   + (cruzou >= 0 ? $"(aos {cruzou:0.00}s; enche aos {encheu:0.00}s; pico {pico * 100:0.0}%)"
								  : $"-- NAO PASSOU em {TetoDaMedida}s (pico {pico * 100:0.0}%)"));
			if (cruzou >= 0)
				Conferir(cruzou <= 20.0, $"[{id}] e em tempo de jogo ({cruzou:0.00}s, teto de 20s)");
		}

		// ============================ E OS CINCO TANQUES SAO CINCO ============================
		// Ver o comentario do `tanque` la em cima: sem esta linha, um `TetoDeKi` de 1,0x faria as cinco
		// medicoes serem a mesma, e o placar afirmaria cinco coisas tendo afirmado uma.
		// =================================================================================
		Conferir(tanque.Values.Distinct().Count() == tanque.Count,
				 "cada forma mede contra o TETO DELA, e nao contra o da base ("
			   + string.Join(", ", tanque.Select(p => $"{p.Key}={p.Value:0}")) + ")");

		Console.WriteLine("\n  em quanto tempo cada uma cruza os 100% do proprio teto:");
		foreach ((string id, double q) in cruzouEm)
			Console.WriteLine($"    {id,-8} {(q < 0 ? "NUNCA" : $"{q:0.00}s"),-8} (MaxKi {tanque[id]:0})");

		// AS TAXAS EM %/s DO MaxKi -- e o que explica o grafico acima sem depender dele.
		//
		// A COLUNA DO `troco` ESTA AQUI POR CAUSA DO DEFEITO QUE ESTE TESTE ACHOU, e e ela que o
		// diagnostica se ele voltar. Enquanto o estagio 1 da carga clampava no MaxKi e dava `return`,
		// a carga sozinha NAO conseguia passar dos 100% dentro de forma nenhuma -- ela reenchia ate o
		// MaxKi cravado e parava. Quem ainda assim ultrapassava so ultrapassava por uma porta lateral:
		// o `troco` do `RegenDeKi` (`RegenDeKi.cs:124-131`), o unico ramo do jogo inteiro que soma Ki
		// FORA do teto de MaxKi -- e o proprio comentario de la diz isso.
		//
		// Ou seja o sistema funcionava por acidente: quem tinha dreno pequeno o bastante era empurrado
		// por cima da barreira pelo troco e caia no estagio 3; o SSJ3, que tem o pior dreno da linha,
		// nao era -- e ficava cravado em 100,0%. Comparar `troco` com `dreno` e o que torna esse
		// retrocesso legivel em vez de "o ssj3 e esquisito".
		//
		// A `regen` sai em coluna separada da do troco de proposito: ela tem teto no MaxKi (:113) e
		// portanto NUNCA pode ultrapassar, por maior que seja. Somar as duas esconderia justamente a
		// diferenca que importa.
		Console.WriteLine("\n  as taxas, em %/s do MaxKi:");
		Console.WriteLine("    forma   ganho da carga   dreno da forma   saldo    regen (tem teto)   troco (SEM teto)");
		foreach (string id in new[] { Jandirus.Core.Forms.Catalogo.IdBase, "ssj1", "ssj2", "ssj3", "ssj4" })
		{
			var f = new Fighter { Race = "Saiyan", BP = 3_000_000 };
			f.Statify();
			Jandirus.Core.Skills.EfeitosDeSkill.Aplicar(f, sc, ["/datum/skill/mind/Ki_Unlocked"]);
			var nv = new Jandirus.Core.Skills.NiveisDeSkill();
			nv.Por("/datum/skill/mind/Basic_Ki_Control", 5);
			nv.Aplicar(f);
			f.Statify();
			var d = Jandirus.Core.Forms.Catalogo.Def(id);
			f.trueKiMod = Jandirus.Core.Forms.Catalogo.TetoDeKi(d, f);
			f.Statify();
			f.Ki = f.MaxKi;

			double antes = f.Ki;
			CargaDeKi.Passo(f, 1.0, mexendo: false);
			double ganho = (f.Ki - antes) / f.MaxKi;
			double dreno = Jandirus.Core.Forms.Catalogo.DrenoPorSegundo(id, new Jandirus.Core.Forms.Maestrias());

			// AS DUAS METADES DO `RegenDeKi`, medidas SEPARADAS -- ver o comentario do cabecalho.
			// As duas se leem num tanque nao-cheio, porque no MaxKi o portao do regen nem abre.
			//
			// 1) regen puro: `extracharge` ZERADO, entao o bloco do troco nem roda.
			f.Ki = f.MaxKi * 0.5;
			f.extracharge = 0;
			double antesRegen = f.Ki;
			Jandirus.Core.Stats.RegenDeKi.Passo(f, 1.0, dreno);
			double regen = (f.Ki - antesRegen) / f.MaxKi;

			// 2) o troco: `extracharge` no PICO da parabola (25), que e quanto ele rende no melhor
			// caso. Comparar o dreno com o melhor caso e o teste justo -- se nem assim o troco
			// levantava o Ki acima do MaxKi, a forma nao tinha como escapar da barreira.
			f.Ki = f.MaxKi * 0.5;
			f.extracharge = 25;
			double antesTroco = f.Ki;
			Jandirus.Core.Stats.RegenDeKi.Passo(f, 1.0, dreno);
			double troco = (f.Ki - antesTroco) / f.MaxKi - regen;

			Console.WriteLine($"    {id,-8} {ganho * 100,10:0.000}%/s {dreno * 100,13:0.000}%/s "
							+ $"{(ganho - dreno) * 100,7:+0.000;-0.000}%/s {regen * 100,15:0.000}%/s {troco * 100,14:0.000}%/s"
							// O PREDITOR E A SOMA DAS DUAS, e a ordem dentro do `RegenDeKi` diz por que: a
							// regeneracao repaga o dreno ate o MaxKi (e para la, :113) e so entao o troco
							// soma por cima, sem teto. Entao o tique terminava acima do MaxKi quando
							// `regen + troco > dreno`. Marcar so o troco erraria o SSJ2, que escapava por
							// uma margem de 0,1 ponto -- e um preditor que erra um caso e um preditor que
							// nao se pode usar pra julgar o proximo.
							+ (dreno > regen + troco ? "   <- o dreno vencia as duas: e o caso que travava" : ""));
		}

		Console.WriteLine(reprovadas == 0
			? "\n  todas as formas passam dos 100%."
			: $"\n  {reprovadas} forma(s) presa(s) no teto de 100%.");
		Console.WriteLine();
	}

	/// <summary>
	/// O TETO DE KI QUE CADA FORMA DA, e a prova de que ele CHEGA no `MaxKi`.
	///
	/// Existe porque este e o defeito classico do projeto -- o dado certo e ninguem consumindo. A
	/// escada estava no catalogo e as 36 entradas valiam 1,0x: virar Super Saiyajin nao aumentava o
	/// tanque em nada. Conferir a TABELA nao bastaria; o que se testa aqui e o `MaxKi` DEPOIS do
	/// `Statify`, que e o numero que o jogador ve.
	/// </summary>
	private static void TetoPorForma()
	{
		Console.WriteLine("-- O TETO DE KI POR FORMA (trueKiMod -> MaxKi) --");

		var f = new Fighter { Race = "Saiyan", BP = 3_000_000 };
		f.Statify();
		double baseMax = f.MaxKi;
		Console.WriteLine($"  na base: MaxKi={baseMax:0.0}");

		(string id, double esperado)[] casos =
		[
			("ssj1", 2.0), ("grade2", 2.0), ("grade3", 2.0),
			("ssj2", 3.0), ("ssj3", 3.5),
			("ssj4", 4.5), ("ssj4_limit_breaker", 4.5),
			("future_ssj", 2.0),
			("wrathful", 1.3), ("c_type", 2.0), ("legendary", 4.0),
			("blue", 1.0), ("ui_perfected", 1.0), ("ultra_ego", 1.0), ("beast", 1.0),
		];

		int erros = 0;
		foreach ((string id, double esperado) in casos)
		{
			var d = Jandirus.Core.Forms.Catalogo.Def(id);
            if (d == null) { Console.WriteLine($"  {id,-20} <<< NAO EXISTE NO CATALOGO"); erros++; continue; }
			f.trueKiMod = Jandirus.Core.Forms.Catalogo.TetoDeKi(d, f);
			f.Statify();
			double razao = f.MaxKi / baseMax;
			bool ok = Math.Abs(razao - esperado) < 0.01;
			if (!ok) erros++;
			Console.WriteLine($"  {id,-20} teto x{f.trueKiMod:0.00}  ->  MaxKi={f.MaxKi,7:0.0} "
							+ $"({razao:0.00}x da base)   {(ok ? "OK" : $"<<< ESPERADO {esperado:0.00}x")}");
		}
		f.trueKiMod = 1; f.Statify();
		Console.WriteLine(erros == 0 ? "  todos batem com o DM." : $"  {erros} FORA DO ESPERADO");

		// NO PLACAR TAMBEM. Este bloco ja media tudo e ja sabia a resposta -- o que faltava era a
		// resposta CONTAR: um "3 FORA DO ESPERADO" impresso no meio da tabela com o programa saindo
		// zero e a mesma bancada decorativa que o cabecalho do `Conferir` descreve.
		Conferir(erros == 0, $"o teto de Ki das {casos.Length} formas bate com o DM ({erros} fora)");
	}

	/// <summary>
	/// SEGURAR O C POR 25 SEGUNDOS, na MESMA ordem do servidor.
	///
	/// ============================ POR QUE ESTE TESTE EXISTE ============================
	/// O dono: "to apertando C e a aura n aparece, e o ki n ta passando de 100%", com o chat
	/// repetindo "a energia passa do que o corpo comporta" varias vezes. Aquela frase so e dita na
	/// MUDANCA de estagio (`AnunciarEstagio`, com `when antes != Ultrapassando`) -- repetir
	/// significa que o estagio esta OSCILANDO, entrando e saindo da ultrapassagem.
	///
	/// Ler o codigo nao resolveu: cada peca, lida sozinha, esta certa. Entao este teste roda o
	/// relogio de verdade, na ordem exata do `GameServer.Carga.cs` (regeneracao, depois carga,
	/// depois o preco do excesso) e imprime o estado a cada segundo. Numero por segundo mostra
	/// oscilacao; leitura de codigo nao mostra.
	/// ================================================================================
	/// </summary>
	private static void Segurando(RaceCatalog? cat)
	{
		Console.WriteLine("-- SEGURANDO O C POR 25 s (ordem do servidor) --");

		string cs = "Assets/Data/skills.json", ct = "Assets/Data/skilltrees.json", cn = "Assets/Data/niveis.json";
		if (!File.Exists(cs) || !File.Exists(ct) || !File.Exists(cn)) { Console.WriteLine("  (sem os .json)"); return; }
		var sc = Jandirus.Core.Skills.SkillCatalog.Parse(File.ReadAllText(cs), File.ReadAllText(ct));
		Jandirus.Core.Skills.RegrasDoDisco.Carregar(File.ReadAllText(cn));

		// O MESMO personagem do `--kiteste`: BP de bancada e as duas pecas do Ki liberado.
		var f = new Fighter { Race = "Saiyan", BP = 3_000_000 };
		f.Statify();
		Jandirus.Core.Skills.EfeitosDeSkill.Aplicar(f, sc, ["/datum/skill/mind/Ki_Unlocked"]);
		var nv = new Jandirus.Core.Skills.NiveisDeSkill();
		nv.Por("/datum/skill/mind/Basic_Ki_Control", 5);
		nv.Aplicar(f);
		f.Statify();

		Console.WriteLine($"  canPower={f.canPower:0}  MaxKi={f.MaxKi:0.0}  kicapacity={f.kicapacity:0.00}  "
						+ $"powerupcap={f.powerupcap:0.00}  teto={Jandirus.Core.Combat.CargaDeKi.TetoDeCarga(f):0.0}");

		const double dt = 1.0 / 30.0;
		var vistos = new List<string>();
		Jandirus.Core.Combat.EstagioDaCarga antes = Jandirus.Core.Combat.EstagioDaCarga.Nada;
		int trocas = 0;

		for (int t = 0; t < (int)(25 / dt); t++)
		{
			Jandirus.Core.Stats.RegenDeKi.Passo(f, dt, 0);                       // 1) regeneracao
			var e = Jandirus.Core.Combat.CargaDeKi.Passo(f, dt, mexendo: false); // 2) a tecla C
			double dano = Jandirus.Core.Combat.CargaDeKi.PrecoDoExcesso(f, dt);  // 3) o preco
			f.Statify();                                                         // 4) o que o servidor faz por tique
			if (e != antes) { trocas++; vistos.Add($"{t * dt:0.0}s {antes}->{e}"); antes = e; }

			if (t % 30 == 0)
				Console.WriteLine($"    {t * dt,4:0}s  Ki={f.Ki,7:0.0} ({f.Ki / f.MaxKi * 100,5:0.0}%)  "
								+ $"estagio={e,-14}  folego={f.staminapercent * 100,5:0.0}%  "
								+ $"powerMod={f.powerMod:0.000}  dano={dano:0.000}");
		}

		Console.WriteLine($"  fim: Ki={f.Ki:0.0} de MaxKi={f.MaxKi:0.0} ({f.Ki / f.MaxKi * 100:0.0}%)");
		Console.WriteLine($"  trocas de estagio: {trocas}"
						+ (trocas > 4 ? "   <<< OSCILANDO -- e o defeito que o dono viu" : ""));
		if (vistos.Count > 0) Console.WriteLine("  " + string.Join(" | ", vistos.Take(12)));
		Console.WriteLine(f.Ki > f.MaxKi * 1.05
			? "  OK: o Ki passou dos 100%."
			: "  <<< O KI NAO PASSOU DOS 100% -- e exatamente a queixa.");
		Console.WriteLine();
	}

	/// <summary>
	/// AS SKILLS REALMENTE LIGAM AS CHAVES?
	///
	/// ============================ POR QUE ESTE TESTE EXISTE ============================
	/// Este e o defeito que o projeto ja cometeu meia duzia de vezes: o extrator tira o dado do
	/// DM, o arquivo sai certo, e NINGUEM CONSOME. Foi exatamente o caso do `canPower` -- ele
	/// estava no `niveis.json` desde o comeco, com `"flags": ["canPower=1"]`, e o leitor de
	/// degraus so olhava `buffs`. A tecla C teria ficado pela metade sem nada acusar: sem erro,
	/// sem aviso, so um power-up que nunca passa dos 100%.
	///
	/// Entao aqui nao se testa a formula -- testa-se o ELO. Aprender a skill de verdade, pelo
	/// mesmo caminho do servidor, e ver se o campo do lutador mudou.
	/// ================================================================================
	/// </summary>
	private static void Elo()
	{
		Console.WriteLine("-- O ELO: a skill liga a chave? --");

		string cs = "Assets/Data/skills.json", ct = "Assets/Data/skilltrees.json", cn = "Assets/Data/niveis.json";
		if (!File.Exists(cs) || !File.Exists(ct)) { Console.WriteLine("  (sem skills.json -- rode da raiz do projeto)\n"); return; }

		var cat = Jandirus.Core.Skills.SkillCatalog.Parse(File.ReadAllText(cs), File.ReadAllText(ct));
		var f = new Fighter { Race = "Human", BP = 500 };

		Console.WriteLine($"  antes de tudo            MeditateGivesKiRegen={f.MeditateGivesKiRegen:0}  canPower={f.canPower:0}");

		// 1) COMPRAR Ki Unlocked -> MeditateGivesKiRegen (canal de flags da COMPRA)
		Jandirus.Core.Skills.EfeitosDeSkill.Aplicar(f, cat, ["/datum/skill/mind/Ki_Unlocked"]);
		Console.WriteLine($"  comprei Ki Unlocked      MeditateGivesKiRegen={f.MeditateGivesKiRegen:0}  canPower={f.canPower:0}"
						  + (f.MeditateGivesKiRegen != 0 ? "   OK" : "   <<< ELO ROTO"));

		// 2) SUBIR Basic Ki Control ao nivel 5 -> canPower (canal de flags do NIVEL)
		if (!File.Exists(cn)) { Console.WriteLine("  (sem niveis.json)\n"); return; }
		Jandirus.Core.Skills.RegrasDoDisco.Carregar(File.ReadAllText(cn));

		var niveis = new Jandirus.Core.Skills.NiveisDeSkill();
		niveis.Por("/datum/skill/mind/Basic_Ki_Control", 5);
		niveis.Aplicar(f);

		Console.WriteLine($"  Basic Ki Control nivel 5 MeditateGivesKiRegen={f.MeditateGivesKiRegen:0}  canPower={f.canPower:0}"
						  + (f.canPower != 0 ? "   OK" : "   <<< ELO ROTO (flags de degrau nao aplicadas)"));
		Console.WriteLine();
	}

	/// <summary>Monta um lutador pronto, pelo mesmo caminho do servidor.</summary>
	private static Fighter Novo(RaceCatalog? cat, double kiControl = 0, bool kiUnlocked = true)
	{
		Fighter f = cat != null
			? Birth.Nascer(cat, "Human", "", new Random(20260803), "Human")
			: new Fighter { Race = "Human", BP = 500 };

		f.Tick();
		f.MeditateGivesKiRegen = kiUnlocked ? 1 : 0;
		f.canPower = kiControl;
		f.Ki = f.MaxKi * 0.25;   // um quarto de tanque: da pra ver as tres etapas em fila
		f.Tick();
		return f;
	}

	// =====================================================================
	private static void Chaves(RaceCatalog? cat)
	{
		Console.WriteLine("-- AS DUAS CHAVES (10 s segurando C) --");
		Console.WriteLine("  quem                        Ki final    passou dos 100%?");

		foreach ((string nome, double control, bool unlock) in new[]
		{
			("sem Ki Unlocked", 0.0, false),
			("so Ki Unlocked", 0.0, true),
			("Ki Unlocked + Control 5", 1.0, true),
		})
		{
			Fighter f = Novo(cat, control, unlock);
			double antes = f.Ki;
			for (int i = 0; i < (int)(10 / Dt); i++) { CargaDeKi.Passo(f, Dt, mexendo: false); f.Tick(); }

			string passou = f.Ki > f.MaxKi * 1.001 ? "SIM" : "nao";
			string mexeu = Math.Abs(f.Ki - antes) < 1e-6 ? "  (a tecla e muda)" : "";
			Console.WriteLine($"  {nome,-26}  {f.Ki / f.MaxKi * 100,6:0.0}%    {passou}{mexeu}");
		}
		Console.WriteLine();
	}

	// =====================================================================
	private static void Tempos(RaceCatalog? cat)
	{
		Console.WriteLine("-- QUANTO DEMORA CADA ETAPA (do zero) --");
		Console.WriteLine("  quem                     encher 0->100%   depois, ate o teto");

		foreach ((string nome, double control) in new[] { ("so Ki Unlocked", 0.0), ("com Ki Control 5", 1.0) })
		{
			Fighter f = Novo(cat, control);
			f.Ki = 0;
			f.Tick();

			double t = 0, encheu = -1, teto = -1;
			double limite = CargaDeKi.TetoDeCarga(f);

			// 120 s de teto: se nao chegou nisso, nao chega -- e o relatorio tem que dizer isso em
			// vez de rodar pra sempre.
			while (t < 120)
			{
				CargaDeKi.Passo(f, Dt, mexendo: false);
				f.Tick();
				t += Dt;
				if (encheu < 0 && f.Ki >= f.MaxKi * 0.999) encheu = t;
				if (teto < 0 && f.Ki >= limite * 0.999) { teto = t; break; }
			}

			string ate = teto < 0 ? "nunca (sem controle)" : $"{teto - encheu,6:0.0} s";
			Console.WriteLine($"  {nome,-22}   {encheu,10:0.0} s   {ate}");
		}
		Console.WriteLine("  (o DM faz MaxKi/90 e MaxKi/150 por chamada, a ~10 chamadas/s -> 9 s e 15 s)\n");
	}

	// =====================================================================
	/// <summary>
	/// O PONTO DO SISTEMA INTEIRO: passar dos 100% de Ki E o buff de BP. Nao ha multiplicador
	/// escondido -- o `kiratio` entra no `statusBuff` do `PowerLevel` e o BP expresso sobe junto.
	/// </summary>
	private static void Poder(RaceCatalog? cat)
	{
		Console.WriteLine("-- O BUFF DE BP (carregando alem dos 100%) --");
		Console.WriteLine("  segundos    Ki       BP expresso    ganho");

		Fighter f = Novo(cat, kiControl: 1);
		f.Ki = f.MaxKi;
		f.Tick();
		double bp0 = f.expressedBP;

		for (int s = 0; s <= 30; s += 5)
		{
			if (s > 0)
				for (int i = 0; i < (int)(5 / Dt); i++) { CargaDeKi.Passo(f, Dt, mexendo: false); f.Tick(); }

			Console.WriteLine($"  {s,6} s   {f.Ki / f.MaxKi * 100,5:0}%   {f.expressedBP,12:N0}    {f.expressedBP / Math.Max(bp0, 1),5:0.00}x");
		}
		Console.WriteLine();
	}

	// =====================================================================
	private static void Preco(RaceCatalog? cat)
	{
		Console.WriteLine("-- O PRECO DE FICAR LA EM CIMA (sem segurar C) --");
		Console.WriteLine("  Ki inicial   dano em 5 s   Ki depois de 5 s   (kicapacity = teto seguro)");

		// AS DUAS ULTIMAS LINHAS PASSAM DO TETO SEGURO DE PROPOSITO. Carregar sozinho nao chega la
		// (a carga para em 140% e o teto e 169%), entao sem elas o ramo de dano ficaria sem prova
		// nenhuma -- e um `if` que nunca roda em teste e um `if` que ninguem sabe se funciona.
		// Quem chega nessa faixa e Kaio-ken e as tecnicas que empurram Ki, nao a tecla C.
		foreach (double razao in new[] { 1.0, 1.15, 1.3, 1.5, 1.75, 2.2 })
		{
			Fighter f = Novo(cat, kiControl: 1);
			f.Ki = f.MaxKi * razao;
			f.Tick();

			double dano = 0;
			for (int i = 0; i < (int)(5 / Dt); i++) { dano += CargaDeKi.PrecoDoExcesso(f, Dt); f.Tick(); }

			Console.WriteLine($"  {razao * 100,8:0}%   {dano,11:0.00}   {f.Ki / f.MaxKi * 100,14:0.0}%"
							  + (razao == 1.0 ? "   <- no limite: nada acontece" : ""));
		}

		// AS DUAS EM UNIDADES DIFERENTES, e imprimir as duas cruas ja escondeu um defeito uma vez:
		// `kicapacity` e ABSOLUTA (o Statify a faz `1,3*MaxKi*log(...)`) e `powerupcap` e RAZAO.
		// Aqui a primeira sai convertida pra razao, que e o unico jeito de as duas se compararem.
		Fighter amostra = Novo(cat, kiControl: 1);
		Console.WriteLine($"\n  este corpo: MaxKi {amostra.MaxKi:N0}"
						  + $"  ·  teto seguro {amostra.kicapacity / Math.Max(amostra.MaxKi, 1) * 100:0}% do MaxKi"
						  + $"  ·  a carga alcanca {amostra.powerupcap * 100:0}%");
		Console.WriteLine("  (a faixa entre os dois e a que DOI: da pra ir la, e cobra)");
	}
}
