using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO VIGOR DO TREINO (`--vigortreinoteste`) -- o relato dos jogadores de 2026-10-23 (*"bp congela do nada e os ganhos ficam
/// mt baixos"*) e as quatro decisoes do dono de 2026-10-24. Cada familia pelo funil de producao, com o defeito injetado dela:
///
///   1. O PESO: o vigor do treino e proporcional ao peso (1x, 2x, 4x, 8x) e nao ao quadrado dele, e o tanque dura 1/peso dos passes.
///   2. O T EXAUSTO: recusado ja no pedido, dizendo quanto falta -- e o numero dito confere com o tempo que o estomago leva -- e que a
///      comida recupera; meditar exausto continua; exausto de estomago vazio, a recusa diz que o vigor nao volta.
///   3. O ESTOMAGO VAZIO: o aviso a cada tentativa de treinar (duas tentativas, dois avisos); com comida, nada; com o vigor travado no
///      cheio (o androide de energia infinita), nada.
///   4. A FRASE DO CORTE: o treino que esvazia o vigor para sem o "ou medite" de antes, e falando da comida.
///   5. O VIGOR NO FUNDO PESA NO PODER (`StaminaDrain.dm:40`): a curva do DM ponto a ponto; o BP expresso a 0,3x, os atributos de luta e
///      o `KIregen` a 0,8x; nada a 5%, onde o treino para; so de jogador (o NPC fica no 100); o piso que mantem o save gravavel.
///   6. A JANELA DO TIRO (`minuteshot`): o primeiro tiro rende metade, os de dentro da janela o detrator, e 45 s depois ela fecha
///      (`spawn(450)`); o acumulador volta a esperar; e a janela e a contagem nao vao pro save (o save da v0.4.4, com a contagem alta,
///      entra zerado).
///
/// O JOGADOR ENTRA PELA PORTA (um cliente cru): o pedido de treinar so e conferido pra quem tem `Peer`, e o vigor so pesa no poder de
/// jogador. O habitante da familia 5 e um corpo forjado.
///
///     Godot --headless --path . --server --port 11010 --vigortreinoteste
/// </summary>
public sealed partial class GameServer
{
	private const string ContaDoVigorDoTreino = "bancada_vigortreino";

	private bool _vtJaRodou;
	private int _vtOk, _vtFalhou;

	private void AfirmarVt(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _vtOk++; GD.Print($"[vigortreino]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _vtFalhou++; GD.PrintErr($"[vigortreino]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoVigorDoTreino(int porta)
	{
		if (_vtJaRodou) return;
		_vtJaRodou = true;
		_vtOk = _vtFalhou = 0;
		GD.Print("[vigortreino] ================ O VIGOR DO TREINO: O PESO, O T EXAUSTO, A COMIDA, O FOLEGO NO PODER E A JANELA DO TIRO (dono, 2026-10-24) ================");

		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		double topoDeAntes = GainKnobs.TopBP;
		List<(int Quem, string Id, long Ms)>? efeitosDeAntes = EscutaDeEfeitos;
		try
		{
			ServerPlayer? pl = mesa.EntraNoMundo(cli, ContaDoVigorDoTreino, "Suado");
			AfirmarVt("V0a. (montagem) o cliente entra pela porta e e jogador", pl != null && EhJogador(pl) && pl.Combate != null);
			if (pl == null || pl.Combate == null) return;

			AfirmarVt("V0b. (montagem) o passe do treino do servidor e o mesmo que o HUD usa (`Cansaco.SegundosDoPasseDoTreino`)",
					  Math.Abs(SegundosPorFicha - Cansaco.SegundosDoPasseDoTreino) < 1e-6, $"servidor {SegundosPorFicha} s, Core {Cansaco.SegundosDoPasseDoTreino} s");

			VtOPeso(pl);
			VtOTExausto(pl);
			VtOEstomagoVazio(pl);
			VtAFraseDoCorte(pl);
			VtOFolegoNoPoder(pl);
			VtAJanelaDoTiro(pl, mesa, cli);
		}
		catch (Exception e)
		{
			AfirmarVt("V0z. a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			Cansaco.VigorPeloQuadradoDoPesoDeTeste = false;
			Nutricao.FolegoNaoPesaNoPoderDeTeste = false;
			Fighter.JanelaDoTiroNaoFechaDeTeste = false;
			TExaustoPassaDeTeste = false;
			EstomagoVazioCaladoDeTeste = false;
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			GainKnobs.TopBP = topoDeAntes;
			EscutaDeEfeitos = efeitosDeAntes;
			mesa.Fecha();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, AccountStore.NomeDeArquivo(ContaDoVigorDoTreino) + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch (Exception e) { GD.PushWarning($"[vigortreino] nao apaguei '{ContaDoVigorDoTreino}': {e.Message}"); }
			GD.Print($"[vigortreino] ================ {_vtOk} OK, {_vtFalhou} FALHA(S) ================");
		}
	}

	/// <summary>O corpo parado, de pe e em paz, sem atividade, com o vigor e o estomago dados -- o ponto de partida de cada medida.</summary>
	private void VtDePe(ServerPlayer pl, double vigor, double nutricao)
	{
		Fighter f = pl.Ficha;
		pl.Moving = false;
		pl.Voando = false;
		pl.Altitude = 0f;
		pl.Nadando = false;
		f.KO = false;
		f.IsInFight = false;
		pl.Combate.EmCombate = pl.Combate.LutandoDeVerdade = pl.Combate.BrigandoComAlguem = 0;
		DefinirAtividade(pl, Protocol.Activity.Parado);
		f.weight = 1;
		f.stamina = vigor * f.maxstamina;
		f.CurrentNutrition = nutricao;
	}

	// =====================================================================
	// 1) O PESO
	// =====================================================================
	private void VtOPeso(ServerPlayer pl)
	{
		GD.Print("[vigortreino] -- 1) o vigor do treino e proporcional ao peso (\"Cobrar proporcional ao peso\") --");
		Fighter f = pl.Ficha;

		// N PASSES DO `Treinar` de producao (o tique de ficha, 5 Hz) com o peso dado, do vigor cheio: quanto do tanque saiu.
		double Gasto(double peso, int passes)
		{
			VtDePe(pl, 1, 30);
			DefinirAtividade(pl, Protocol.Activity.Treinando);
			f.weight = peso;
			double antes = f.stamina;
			for (int i = 0; i < passes; i++) Treinar(pl);
			double gastou = (antes - f.stamina) / f.maxstamina;
			VtDePe(pl, 1, 30);
			return gastou;
		}
		// QUANTOS PASSES o tanque cheio aguenta com o peso dado, ate o corte.
		int PassesAteOCorte(double peso)
		{
			VtDePe(pl, 1, 30);
			DefinirAtividade(pl, Protocol.Activity.Treinando);
			f.weight = peso;
			int n = 0;
			List<string> falas = Ouvir(() => { while (f.train && n < 5_000) { Treinar(pl); n++; } });
			VtDePe(pl, 1, 30);
			return n;
		}

		double um = Gasto(1, 50), dois = Gasto(2, 50), quatro = Gasto(4, 50), oito = Gasto(8, 50);
		AfirmarVt("V1a. **O VIGOR DO TREINO E PROPORCIONAL AO PESO** (dono, 2026-10-24): 10 s de treino gastam 5% do tanque sem peso, 10% com o "
				  + "peso 2x, 20% com 4x e 40% com 8x -- o dobro do peso, o dobro do vigor (era o quadruplo: `0.01*weight*weight`, `Train.dm:88`)",
				  Math.Abs(um - 0.05) < 0.002 && Math.Abs(dois / um - 2) < 0.04 && Math.Abs(quatro / um - 4) < 0.08 && Math.Abs(oito / um - 8) < 0.16,
				  $"sem peso {um:P2}; 2x {dois:P2} ({dois / um:0.###}x); 4x {quatro:P2} ({quatro / um:0.###}x); 8x {oito:P2} ({oito / um:0.###}x)");

		int semPeso = PassesAteOCorte(1), comQuatro = PassesAteOCorte(4);
		AfirmarVt("V1b. ...e o tanque cheio dura 1/peso dos passes: com o peso 4x o treino para em 1/4 dos passes de quem treina sem peso (cada "
				  + "passe rendendo 4x -- o mesmo poder por tanque)",
				  semPeso > 800 && Math.Abs((double)semPeso / Math.Max(comQuatro, 1) - 4) < 0.12,
				  $"sem peso {semPeso} passes ({semPeso * SegundosPorFicha:0} s); 4x {comQuatro} passes ({comQuatro * SegundosPorFicha:0} s)");

		Cansaco.VigorPeloQuadradoDoPesoDeTeste = true;
		double umQ, doisQ;
		try { umQ = Gasto(1, 50); doisQ = Gasto(2, 50); }
		finally { Cansaco.VigorPeloQuadradoDoPesoDeTeste = false; }
		AfirmarVt("V1c. (defeito injetado: o vigor pelo QUADRADO do peso, o DM e o jogo de antes) a mesma regua REPROVA: o peso 2x gasta 4x",
				  Math.Abs(doisQ / umQ - 4) < 0.08, $"sem peso {umQ:P2}; 2x {doisQ:P2} ({doisQ / umQ:0.###}x)");
	}

	// =====================================================================
	// 2) O T EXAUSTO
	// =====================================================================
	/// <summary>O numero que a frase da recusa disse ("faltam uns N s"), ou -1.</summary>
	private static int VtSegundosDitos(List<string> falas)
	{
		foreach (string fala in falas)
		{
			int i = fala.IndexOf("faltam uns ", StringComparison.Ordinal);
			if (i < 0) continue;
			string resto = fala[(i + "faltam uns ".Length)..];
			int fim = resto.IndexOf(' ');
			if (fim > 0 && int.TryParse(resto[..fim], out int n)) return n;
		}
		return -1;
	}

	private void VtOTExausto(ServerPlayer pl)
	{
		GD.Print("[vigortreino] -- 2) o T exausto e recusado ja no pedido, dizendo quanto falta e que a comida recupera --");
		Fighter f = pl.Ficha;
		List<(int Quem, string Id, long Ms)> efeitos = [];
		EscutaDeEfeitos = efeitos;
		bool AvisouOCliente() => efeitos.Exists(e => e.Quem == pl.Id && e.Id == TreinoEMeditacao.EfeitoDeQueCaiu);
		try
		{
			VtDePe(pl, 1, 30);
			List<string> falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Treinando));
			AfirmarVt("V2a. (montagem) com o vigor cheio e comida no estomago o T liga o treino, e nada e dito", f.train && falas.Count == 0, Ultimos(falas));

			// EXAUSTO: 4% do tanque, abaixo dos 5% do corte -- com comida no estomago
			VtDePe(pl, 0.04, 30);
			efeitos.Clear();
			double previsto = Cansaco.SegundosAteAguentarTreinar(f, SegundosPorFicha);
			falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Treinando));
			int dito = VtSegundosDitos(falas);
			AfirmarVt("V2b. **O T EXAUSTO E RECUSADO JA NO PEDIDO** (dono, 2026-10-24): o treino nao liga, o cliente e avisado (larga a pose), e a "
					  + "frase diz QUANTO FALTA e que a COMIDA recupera o vigor",
					  !f.train && AvisouOCliente() && Disse(falas, "você está exausto") && Disse(falas, "comida que recupera o vigor")
					  && double.IsFinite(previsto) && dito == (int)Math.Max(Math.Ceiling(previsto), 1),
					  $"treina {f.train}; previsto {previsto:0.0} s, dito {dito} s | {Ultimos(falas)}");

			// O TEMPO DITO CONFERE: o estomago de producao, tique a tique (30 Hz), ate o corpo aguentar um passe
			double t = 0;
			while (!Cansaco.AguentaTreinar(f, SegundosPorFicha) && t < 600)
			{
				TickDoEstomago(pl, Protocol.TickSeconds);
				t += Protocol.TickSeconds;
			}
			AfirmarVt("V2c. ...e o tempo dito confere com o estomago: o vigor volta a pagar um passe dentro de uma passada (3 s) do previsto",
					  Math.Abs(t - previsto) <= Nutricao.SegundosPorPassada + 0.1, $"previsto {previsto:0.0} s, o estomago levou {t:0.0} s");
			falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Treinando));
			AfirmarVt("V2d. ...e passado esse tempo o mesmo pedido passa", f.train && !Disse(falas, "exausto"), Ultimos(falas));

			// MEDITAR NAO CUSTA VIGOR: exausto, medita
			VtDePe(pl, 0.04, 30);
			falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Meditando));
			AfirmarVt("V2e. ...e exausto ainda se MEDITA (meditar nao custa vigor: so o T pergunta)", f.med && !Disse(falas, "exausto"), Ultimos(falas));

			// EXAUSTO E SEM COMIDA: nao ha tempo que chegue
			VtDePe(pl, 0.04, 0);
			efeitos.Clear();
			falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Treinando));
			AfirmarVt("V2f. ...exausto e de ESTOMAGO VAZIO a recusa diz que sem comida o vigor nao volta (e nao promete tempo nenhum)",
					  !f.train && AvisouOCliente() && Disse(falas, Cansaco.FraseDoExaustoSemComida) && VtSegundosDitos(falas) < 0, Ultimos(falas));

			// O JOGO DE ANTES: o pedido passava e o treino caia no primeiro passe
			VtDePe(pl, 0.04, 30);
			efeitos.Clear();
			bool ligou;
			TExaustoPassaDeTeste = true;
			try
			{
				falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Treinando));
				ligou = f.train;
				falas.AddRange(Ouvir(() => Treinar(pl)));
			}
			finally { TExaustoPassaDeTeste = false; }
			AfirmarVt("V2g. (defeito injetado: o pedido exausto passa, o jogo de antes) a mesma regua REPROVA: o treino liga e cai no primeiro passe, "
					  + "sem dizer quanto falta",
					  ligou && !f.train && Disse(falas, "o treino para") && VtSegundosDitos(falas) < 0, $"ligou {ligou}, treina {f.train} | {Ultimos(falas)}");
		}
		finally
		{
			EscutaDeEfeitos = null;
			VtDePe(pl, 1, 30);
		}
	}

	// =====================================================================
	// 3) O ESTOMAGO VAZIO
	// =====================================================================
	private void VtOEstomagoVazio(ServerPlayer pl)
	{
		GD.Print("[vigortreino] -- 3) o estomago vazio e avisado a cada tentativa de treinar --");
		Fighter f = pl.Ficha;

		// DUAS TENTATIVAS de treinar (T, parar, T de novo): quantos avisos de estomago vazio, e se o treino ligou nas duas
		(int Avisos, bool Ligou) Tentativas(double nutricao)
		{
			VtDePe(pl, 0.8, nutricao);
			int avisos = 0;
			bool ligou = true;
			for (int i = 0; i < 2; i++)
			{
				List<string> falas = Ouvir(() => DefinirAtividade(pl, Protocol.Activity.Treinando));
				avisos += falas.Count(x => x == Cansaco.AvisoDoEstomagoVazio);
				ligou &= f.train;
				DefinirAtividade(pl, Protocol.Activity.Parado);
			}
			VtDePe(pl, 1, 30);
			return (avisos, ligou);
		}

		var vazio = Tentativas(0);
		AfirmarVt("V3a. **COM O ESTOMAGO VAZIO, CADA TENTATIVA DE TREINAR AVISA** que sem comida o vigor nao volta (dono, 2026-10-24): duas "
				  + "tentativas, dois avisos -- e o treino liga (o vigor ainda paga)", vazio.Avisos == 2 && vazio.Ligou,
				  $"avisos {vazio.Avisos}, ligou {vazio.Ligou}");
		var cheio = Tentativas(30);
		AfirmarVt("V3b. ...com comida no estomago, nenhum aviso", cheio.Avisos == 0 && cheio.Ligou, $"avisos {cheio.Avisos}");

		bool infinitoAntes = f.AndroideInfinito;
		(int Avisos, bool Ligou) travado;
		f.AndroideInfinito = true;
		try { travado = Tentativas(0); }
		finally { f.AndroideInfinito = infinitoAntes; }
		AfirmarVt("V3c. ...e quem tem o vigor travado no cheio (o androide de energia infinita) nao ouve: pra ele comida nao conta",
				  travado.Avisos == 0 && travado.Ligou, $"avisos {travado.Avisos}");

		EstomagoVazioCaladoDeTeste = true;
		(int Avisos, bool Ligou) calado;
		try { calado = Tentativas(0); }
		finally { EstomagoVazioCaladoDeTeste = false; }
		AfirmarVt("V3d. (defeito injetado: a tentativa com o estomago vazio nao diz nada, o jogo de antes) a mesma regua REPROVA",
				  calado.Avisos == 0, $"avisos {calado.Avisos}");
	}

	// =====================================================================
	// 4) A FRASE DO CORTE
	// =====================================================================
	private void VtAFraseDoCorte(ServerPlayer pl)
	{
		GD.Print("[vigortreino] -- 4) a frase do corte: sem o \"ou medite\", e com a comida --");
		Fighter f = pl.Ficha;

		// O TREINO QUE ESVAZIA O VIGOR, pelo `Treinar` de producao: do pouco acima do corte ate ele parar. O que foi dito no corte.
		List<string> Cortar(double nutricao)
		{
			VtDePe(pl, 0.06, nutricao);
			DefinirAtividade(pl, Protocol.Activity.Treinando);
			int n = 0;
			List<string> falas = Ouvir(() => { while (f.train && n < 200) { Treinar(pl); n++; } });
			VtDePe(pl, 1, 30);
			return falas;
		}
		// A REGUA DA FRASE: diz que esta exausto e que o treino para, e nao manda meditar.
		static bool FraseCerta(List<string> falas) => Disse(falas, "você está exausto: o treino para") && !Disse(falas, "medite");

		List<string> comComida = Cortar(30), semComida = Cortar(0);
		AfirmarVt("V4a. **O CORTE SEM O \"OU MEDITE\"** (meditar nao enche o vigor mais depressa) e falando da comida que o recupera",
				  FraseCerta(comComida) && Disse(comComida, "Descanse") && Disse(comComida, "comida que você digere"), Ultimos(comComida));
		AfirmarVt("V4b. ...e de estomago vazio o corte diz que o vigor nao volta e manda comer",
				  FraseCerta(semComida) && Disse(semComida, "estômago vazio o vigor não volta") && Disse(semComida, "coma alguma coisa"), Ultimos(semComida));
		AfirmarVt("V4c. (a frase de antes, pela mesma regua) REPROVA: \"Descanse -- ou medite -- até o vigor voltar\"",
				  !FraseCerta(["você está exausto: o treino para. Descanse -- ou medite -- até o vigor voltar."]));
	}

	// =====================================================================
	// 5) O VIGOR NO FUNDO PESA NO PODER
	// =====================================================================
	/// <summary>A REGUA: a linha do DM escrita de novo (`StaminaDrain.dm:40`), com o piso em zero do port -- e nao a funcao do Core.</summary>
	private static double VtCurvaDoDm(double vigor, double vigorMaximo, double estaminamod)
	{
		double r = vigorMaximo / vigor;
		return Math.Clamp(Math.Min(101.047 - Math.Pow(1.047, r) * 0.008 * r / estaminamod, 100), 0, 100);
	}

	private void VtOFolegoNoPoder(ServerPlayer pl)
	{
		GD.Print("[vigortreino] -- 5) o vigor no fundo pesa no poder, como no DM (`StaminaDrain.dm:40`) --");
		Fighter f = pl.Ficha;
		long t = NowMs();

		// O VIGOR NUMA FRACAO DO TANQUE, e o tique do estomago de producao SEM passada (dt 0: so a linha de cada tique), e a ficha.
		void Vigor(double fracao)
		{
			VtDePe(pl, fracao, 30);
			TickDoEstomago(pl, 0);
			f.Tick(agoraMs: t);
		}

		// a curva, ponto a ponto
		double[] pontos = [1, 0.05, 0.03, 0.02, 0.015, 0.0125, 0.01, 0.0098, 0.005];
		List<string> tabela = [];
		bool bate = true;
		foreach (double p in pontos)
		{
			// (a regua com os numeros de ANTES do tique da ficha: o `Statify` engorda o tanque de quem esta abaixo de 5%, `master.dm:134`)
			VtDePe(pl, p, 30);
			double esperado = VtCurvaDoDm(f.stamina, f.maxstamina, f.Estaminamod);
			TickDoEstomago(pl, 0);
			double obtido = f.staminadeBuff;
			f.Tick(agoraMs: t);
			bate &= Math.Abs(obtido - esperado) < 1e-6;
			tabela.Add($"{p:P2}: {obtido:0.##} (DM {esperado:0.##})");
		}
		AfirmarVt("V5a. **A CURVA DO DM, PONTO A PONTO** (`staminadeBuff`, `StaminaDrain.dm:40`): 100 com o tanque cheio e a 5%, e so no fundo ela "
				  + "desce", bate, string.Join("; ", tabela));

		Vigor(1);
		(double Inteireza, double Expresso, double Rphysoff, double KIregen, double Sdb) cheio =
			(f.Inteireza, f.expressedBP, f.Rphysoff, f.KIregen, f.staminadeBuff);
		Vigor(0.05);
		double aCinco = f.Inteireza / cheio.Inteireza;
		Vigor(0.005);
		(double Inteireza, double Expresso, double Rphysoff, double KIregen, double Sdb) fundo =
			(f.Inteireza, f.expressedBP, f.Rphysoff, f.KIregen, f.staminadeBuff);
		double fadigaEsperada = Math.Clamp(0.8 * f.Ewillpower, 0.6, 1) / Math.Clamp(f.Ewillpower, 0.6, 1);
		AfirmarVt("V5b. **SEM VIGOR O CORPO FICA NERFADO COMO NO DM** (dono, 2026-10-24): a 0,5% do tanque o BP expresso cai a 0,3x (o "
				  + "`staminaratio` no piso, `base.dm:78`) -- e a % efetivo da tela junto",
				  Math.Abs(fundo.Inteireza / cheio.Inteireza - 0.3) < 0.003 && fundo.Expresso < cheio.Expresso * 0.31 && fundo.Expresso > cheio.Expresso * 0.29,
				  $"efetivo {cheio.Inteireza:P1} -> {fundo.Inteireza:P1}; BP expresso {cheio.Expresso:N0} -> {fundo.Expresso:N0}; staminadeBuff {cheio.Sdb:0.##} -> {fundo.Sdb:0.##}");
		AfirmarVt("V5c. ...os atributos de luta e o `KIregen` a 0,8x (o `statstamina` no piso, `master.dm:133`/:154-158/:184 -- a fadiga sobe com a vontade)",
				  Math.Abs(fundo.Rphysoff / cheio.Rphysoff - fadigaEsperada) < 0.002 && Math.Abs(fundo.KIregen / cheio.KIregen - 0.8) < 0.002,
				  $"Rphysoff x{fundo.Rphysoff / cheio.Rphysoff:0.###} (esperado x{fadigaEsperada:0.###}, vontade {f.Ewillpower:0.##}); KIregen x{fundo.KIregen / cheio.KIregen:0.###}");
		AfirmarVt("V5d. ...e a 5% (onde o treino para) o poder fica inteiro: a curva so morde no fundo do tanque", Math.Abs(aCinco - 1) < 1e-9, $"x{aCinco:0.######}");

		// SO DE JOGADOR: o habitante fica no 100 da normalizacao, como no DM (`if(client)`, `StaminaDrain.dm:16`)
		ServerPlayer habitante = ForjarComSkills("Habitante do vigor", pl.Pos + new Vec2(3 * ZoneCollision.TileSize, 0), 1_000);
		try
		{
			habitante.Ficha.stamina = 0.005 * habitante.Ficha.maxstamina;
			TickDoEstomago(habitante, 0);
			habitante.Ficha.Tick(agoraMs: t);
			AfirmarVt("V5e. ...so de JOGADOR: o habitante sem vigor fica com o poder inteiro (o `CheckNutrition` do DM abre com `if(client)`)",
					  !EhJogador(habitante) && habitante.Ficha.staminadeBuff == 100 && habitante.Ficha.staminaratio == 1,
					  $"staminadeBuff {habitante.Ficha.staminadeBuff:0.##}");
		}
		finally { RemoverNpc(habitante); }

		// O PISO: perto de zero a conta do DM vai a menos infinito, e o campo vai pro save
		Vigor(1e-7);
		bool gravou;
		try { JsonSerializer.Serialize(f, new JsonSerializerOptions { IncludeFields = true }); gravou = true; }
		catch (Exception) { gravou = false; }
		double cru = 101.047 - Math.Pow(1.047, 1e7) * 0.008 * 1e7 / f.Estaminamod;
		bool cruEstoura;
		try { JsonSerializer.Serialize(cru); cruEstoura = false; }
		catch (Exception) { cruEstoura = true; }
		AfirmarVt("V5f. ...e o piso em zero mantem o save gravavel: com o tanque a 0,00001% o numero do DM e menos infinito (o JSON o recusa), o do "
				  + "port e zero e a ficha grava", f.staminadeBuff == 0 && gravou && cruEstoura && f.staminaratio == 0.3 && f.statstamina == 0.8,
				  $"staminadeBuff {f.staminadeBuff}; o do DM {cru}; gravou {gravou}");

		Nutricao.FolegoNaoPesaNoPoderDeTeste = true;
		double semAPerna;
		try
		{
			Vigor(1);
			f.staminadeBuff = 100;
			Vigor(0.005);
			semAPerna = f.Inteireza / cheio.Inteireza;
		}
		finally { Nutricao.FolegoNaoPesaNoPoderDeTeste = false; }
		AfirmarVt("V5g. (defeito injetado: o vigor no fundo nao pesa no poder, o jogo de antes) a mesma regua REPROVA: a 0,5% o poder ficaria inteiro",
				  Math.Abs(semAPerna - 1) < 1e-9, $"x{semAPerna:0.###}");

		Vigor(1);
	}

	// =====================================================================
	// 6) A JANELA DO TIRO
	// =====================================================================
	/// <summary>
	/// UM TIRO QUE ACERTA, pelo funil do Core que os dois chamadores de producao usam (`GameServer.Ganhos.PagarUm` e o acerto do
	/// `GameServer.Projeteis`): o `BlastGain`. O corpo e arrumado pra o tiro so ter a variavel da janela -- o acumulador zerado, a direcao
	/// trocada (sem o castigo do mesmo lado), o vigor cheio, e o topo do servidor longe (o `gainscale` em 1, que a bancada guarda e
	/// devolve). Devolve o BP que entrou.
	/// </summary>
	private static double VtUmTiro(Fighter f)
	{
		f.BPBuffer = 0;
		f.tmp_activ_gains = 0;
		f.dir = f.lastdir == Facing.South ? Facing.North : Facing.South;
		f.stamina = f.maxstamina;
		f.Statify();
		double antes = f.BP;
		f.BlastGain(new Random(7), 1);
		return f.BP - antes;
	}

	private void VtAJanelaDoTiro(ServerPlayer pl, MesaDeClientesCrus mesa, ClienteCruDeTeste cli)
	{
		GD.Print("[vigortreino] -- 6) a janela do tiro de ki fecha em 45 s (`spawn(450)`) e nao vai pro save --");
		GainKnobs.TopBP = 1e300;
		Fighter f = pl.Ficha;
		VtDePe(pl, 1, 30);
		f.Tick(agoraMs: NowMs());

		// UMA JANELA INTEIRA: o tiro que abre, um dentro, 44 s (ainda aberta), 45 s (fechada) e o tiro de depois
		(double Primeiro, double Segundo, double Terceiro, double Depois, bool AbriuEm1, bool AbertaAos44, bool FechadaAos45, int ContagemDepois) Janela()
		{
			f.minuteshot = false;
			f.minuteshotFechaEm = 0;
			double g1 = VtUmTiro(f);
			bool abriu = f.minuteshot && f.minuteshot_ig_ki == 1;
			f.Tick(agoraMs: NowMs());            // o tique seguinte ao tiro marca o prazo
			double g2 = VtUmTiro(f);
			AdiantoDoRelogioDeTeste += 44_000;
			f.Tick(agoraMs: NowMs());
			bool aos44 = f.minuteshot;
			double g3 = VtUmTiro(f);
			AdiantoDoRelogioDeTeste += 1_100;
			f.Tick(agoraMs: NowMs());
			bool aos45 = !f.minuteshot;
			double g4 = VtUmTiro(f);
			return (g1, g2, g3, g4, abriu, aos44, aos45, f.minuteshot_ig_ki);
		}

		var j = Janela();
		double dentro2 = 0.18 / Math.Log(3, 1.3) / 0.5, dentro3 = 0.18 / Math.Log(5, 1.3) / 0.5;
		AfirmarVt("V6a. (montagem) o primeiro tiro rende e abre a janela (contagem 1)", j.Primeiro > 0 && j.AbriuEm1, $"BP +{j.Primeiro:0.########}");
		AfirmarVt("V6b. ...dentro da janela cada tiro rende menos que o anterior (o detrator `0.18/log(1.3, contagem)`, `combatgains.dm:198-200`)",
				  Math.Abs(j.Segundo / j.Primeiro - dentro2) < 0.01 * dentro2 && Math.Abs(j.Terceiro / j.Primeiro - dentro3) < 0.01 * dentro3,
				  $"2o x{j.Segundo / j.Primeiro:0.####} (esperado {dentro2:0.####}); 3o x{j.Terceiro / j.Primeiro:0.####} (esperado {dentro3:0.####})");
		AfirmarVt("V6c. **A JANELA FECHA EM 45 S** (`spawn(450) minuteshot=0`, `combatgains.dm:191`; dono, 2026-10-24: \"Consertar como no DM\"): aos "
				  + "44 s ainda esta aberta, aos 45 s fecha, e o tiro seguinte volta a ser o primeiro",
				  j.AbertaAos44 && j.FechadaAos45 && Math.Abs(j.Depois / j.Primeiro - 1) < 0.01 && j.ContagemDepois == 1,
				  $"aberta aos 44 s {j.AbertaAos44}; fechada aos 45 s {j.FechadaAos45}; o de depois x{j.Depois / j.Primeiro:0.####}, contagem {j.ContagemDepois}");

		// O ACUMULADOR DE QUEM NAO GANHA le a mesma janela (`Stats.dm:163`): fechada, ele espera o `Gaintimer`
		f.minuteshot = false;
		f.Gaintimer = 50;
		f.BPBuffer = 0;
		f.BufferTick();
		bool esperou = f.BPBuffer == 0 && f.Gaintimer == 49;
		f.minuteshot = true;
		f.BufferTick();
		bool encheu = f.BPBuffer > 0;
		f.minuteshot = false;
		f.BPBuffer = 0;
		AfirmarVt("V6d. ...e o acumulador de quem nao ganha volta a esperar o `Gaintimer` com a janela fechada (aberta, ele enche sem esperar)",
				  esperou && encheu);

		var preso = (Primeiro: 0.0, Depois: 0.0, FechadaAos45: true);
		Fighter.JanelaDoTiroNaoFechaDeTeste = true;
		try { var p = Janela(); preso = (p.Primeiro, p.Depois, p.FechadaAos45); }
		finally { Fighter.JanelaDoTiroNaoFechaDeTeste = false; }
		AfirmarVt("V6e. (defeito injetado: a janela nunca fecha, o jogo de antes) a mesma regua REPROVA: aos 45 s ela segue aberta e o tiro paga o detrator",
				  !preso.FechadaAos45 && preso.Depois / preso.Primeiro < 0.1, $"o de depois x{preso.Depois / preso.Primeiro:0.####}");

		// ---------------------------------------------------------------- O SAVE
		double referencia = j.Primeiro;
		f.minuteshot = true;
		f.minuteshot_ig_ki = 101;
		f.minuteshotFechaEm = NowMs() + 1;
		bool saiu = mesa.SaiDoJogo(cli, ContaDoVigorDoTreino);
		string arquivo = System.IO.Path.Combine(_store!.Pasta, AccountStore.NomeDeArquivo(ContaDoVigorDoTreino) + ".json");
		string texto = System.IO.File.Exists(arquivo) ? System.IO.File.ReadAllText(arquivo) : "";
		AfirmarVt("V6f. **A JANELA E A CONTAGEM NAO VAO PRO SAVE** (o `tmp` do DM, `combatgains.dm:106-109` e `attack_bck.dm:5-8`): quem sai com a "
				  + "janela aberta e a contagem em 101 deixa no disco uma ficha sem nenhuma das duas",
				  saiu && texto.Contains("\"Ficha\"", StringComparison.Ordinal) && !texto.Contains("minuteshot", StringComparison.Ordinal),
				  $"saiu {saiu}; arquivo {texto.Length} letras; 'minuteshot' no disco {texto.Contains("minuteshot", StringComparison.Ordinal)}");

		// O SAVE DA v0.4.4: a mesma ficha com a janela presa e a contagem alta, como o jogo de antes gravava
		JsonNode? raiz = texto.Length > 0 ? JsonNode.Parse(texto) : null;
		if (raiz?["Slots"]?[0]?["Ficha"] is not JsonObject fichaNoDisco)
		{
			AfirmarVt("V6g. (montagem) o save no disco tem a ficha no slot 0", false);
			return;
		}
		fichaNoDisco["minuteshot"] = true;
		fichaNoDisco["minuteshot_ig_ki"] = 101;
		System.IO.File.WriteAllText(arquivo, raiz.ToJsonString());
		ServerPlayer? de = mesa.EntraNoMundo(cli, ContaDoVigorDoTreino, "Suado");
		AfirmarVt("V6h. (montagem) o mesmo personagem entra de novo pela porta, com o save de antes no disco", de != null && EhJogador(de));
		if (de == null) return;
		Fighter g = de.Ficha;
		bool zerado = !g.minuteshot && g.minuteshot_ig_ki == 0 && g.minuteshotFechaEm == 0;
		g.Tick(agoraMs: NowMs());
		double primeiroDeVolta = VtUmTiro(g);
		AfirmarVt("V6i. **O SAVE DE ANTES, COM A CONTAGEM EM 101, ENTRA ZERADO**: a janela fechada, a contagem em zero, e o primeiro tiro rende o "
				  + "primeiro tiro (e nao o detrator de 101)",
				  zerado && Math.Abs(primeiroDeVolta / referencia - 1) < 0.02 && g.minuteshot_ig_ki == 1,
				  $"janela {g.minuteshot}, contagem {g.minuteshot_ig_ki}; o tiro x{primeiroDeVolta / referencia:0.####} do primeiro");

		// O JOGO DE ANTES LENDO O MESMO DISCO: a mesma ficha, com a janela e a contagem que o disco traz postas de volta nos campos -- o que
		// a v0.4.4 fazia, com os dois campos publicos e sem o `JsonIgnore`
		Fighter? velha = fichaNoDisco.Deserialize<Fighter>(new JsonSerializerOptions { IncludeFields = true });
		double tiroVelho = 0;
		if (velha != null)
		{
			velha.minuteshot = fichaNoDisco["minuteshot"]?.GetValue<bool>() ?? false;
			velha.minuteshot_ig_ki = fichaNoDisco["minuteshot_ig_ki"]?.GetValue<int>() ?? 0;
			velha.Tick();
			tiroVelho = VtUmTiro(velha);
		}
		AfirmarVt("V6j. (defeito injetado: a ficha lida como a v0.4.4 a lia, com a contagem de volta) a mesma regua REPROVA: a contagem 101 volta e o "
				  + "primeiro tiro paga o detrator",
				  velha is { minuteshot_ig_ki: > 100 } && tiroVelho / referencia < 0.1,
				  $"contagem {velha?.minuteshot_ig_ki}; o tiro x{tiroVelho / referencia:0.####} do primeiro");
	}
}
