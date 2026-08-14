using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DA NAVE -- A CAMADA 4: **A VIAGEM, O TETO, O REINICIO E A ESTRELA**.
///
/// Roda na mesma flag `--naveteste`, no mesmo contador das outras tres.
///
/// ============================ POR QUE ESTE ARQUIVO EXISTE ============================
/// As camadas 1 a 3 provam a CORRENTE (fabricar → assentar → embarcar → lancar → pousar) e os
/// ESTADOS (casco, interior, ejecao, uso unico). Elas deixaram quatro perguntas sem medida, e as
/// quatro sao exatamente as que a spec pediu em voz alta:
///
///   1. **"medir a viagem Terra→Namek com e sem nave, pra a diferenca aparecer"** -- o que existia
///      era a CONTA (`Naves.SegundosDeViagem`) impressa numa linha. Uma conta nao e uma medida: se o
///      `SpeedStat` da nave nunca chegasse ao `ValidateStep`, a conta continuaria imprimindo 4,3
///      minutos e o jogador continuaria levando sete dias. Aqui o passo e PEDIDO como um cliente
///      pede e CONFERIDO pelo `MoveRules.ValidateStep` do servidor -- o numero sai do que o servidor
///      **aceitou andar**, e nao da formula que a tela promete. As duas sao comparadas.
///   2. **o teto de 39x** -- a camada 1 afirmava `n.Velocidade == VelocidadeMaxima` duas vezes
///      seguidas: a segunda linha nao exercia recusa nenhuma. Um teto que nao e provado por uma
///      TENTATIVA que falha e um teto que ninguem sabe se dispara (regra 0.7).
///   3. **o reinicio** -- a camada 1 provou o CRIVO em memoria (`NavesParadasEm` com duas zonas
///      homonimas). O crivo nao e o disco: o defeito da `Obra` e de SERIALIZACAO (ela joga fora o
///      tipo e a seed), e so uma volta pelo `GravarNaves`/`CarregarNaves` de producao o alcanca.
///   4. **a estrela** -- o dono pediu explicitamente: *"uma nave que viaja depressa passa perto de
///      estrelas -- confira o que acontece"*. A bancada de contas media FORMULAS; aqui um corpo
///      atravessa uma estrela de verdade, a pe e de nave, e a vida perdida e contada.
/// ====================================================================================
///
/// ============================ ELA NAO TOCA NO PERSONAGEM DO DONO ============================
/// Tudo aqui roda em corpos FORJADOS (o mesmo <see cref="Forjar"/> da bancada do sol), e nao no
/// personagem vivo. As camadas 1 a 3 emprestam o corpo do dono porque precisam do funil da aba Tech
/// e da tecla E; esta camada precisa de VIAGEM -- levar o personagem de alguem numa travessia de
/// estrela e um jeito de a bancada matar quem a rodou.
/// ==========================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// O DESLOCAMENTO DOS IDS DESTA CAMADA dentro do <see cref="Forjar"/>, que soma o que recebe ao
	/// `IdBaseDoSolDeTeste` (90.400). Duzentos pra frente: longe dos corpos da bancada do sol, que
	/// usa 1 e 2, e longe da faixa do convivio.
	/// </summary>
	private const int IdBaseDaViagemDeTeste = 200;

	/// <summary>
	/// Quantos tiques cada medicao de velocidade dura.
	///
	/// Dez segundos in-game. Nao e "quanto tempo a viagem leva" -- e quanto tempo basta pra o passo
	/// por tique estabilizar (o orcamento do `ValidateStep` acumula folga nos primeiros tiques). A
	/// viagem inteira e ARITMETICA em cima do passo medido, e a bancada diz isso em voz alta.
	/// </summary>
	private const int TiquesDaMedicaoDeViagem = 300;

	// =====================================================================
	// A PORTA
	// =====================================================================
	private void RodarBancadaDaViagem(ServerPlayer dono, Action<string, bool, string> Checa)
	{
		AViagemMedida(Checa);
		AChegada(Checa);
		OTetoQueDispara(Checa);
		OReinicioDoServidor(Checa);
		AEstrelaNoCaminho(Checa);
		_ = dono;   // o dono nao entra: ver o cabecalho
	}

	// =====================================================================
	// 1) A VIAGEM MEDIDA -- O NUMERO QUE A SPEC PEDIU
	// =====================================================================
	/// <summary>
	/// TERRA→NAMEK, A PE E DE NAVE, MEDIDO NO PASSO QUE O SERVIDOR ACEITOU.
	///
	/// ============================ O PASSO E PEDIDO, E NAO AFIRMADO ============================
	/// O corpo de nave PEDE `Naves.PassoPorTique(39)` = 208 px por tique -- e o que um cliente que
	/// sabe estar numa nave rapida afirmaria. Quem decide se ele anda isso e o
	/// `MoveRules.ValidateStep`, com o `pl.SpeedStat` que o SERVIDOR escreveu.
	///
	/// E por isso que esta medicao pega o defeito que a formula nao pegaria: se o `RecalcularVelocidade`
	/// deixasse de aplicar a nave, o pedido de 208 px seria APARADO em ~22 px (o orcamento de um corpo
	/// a pe mais o `MinCorrectionPx`), a medicao cairia dez vezes e a bancada ficaria vermelha --
	/// enquanto `Naves.SegundosDeViagem` continuaria imprimindo os mesmos 4,3 minutos de sempre.
	/// ======================================================================================
	/// </summary>
	private void AViagemMedida(Action<string, bool, string> checa)
	{
		void Checa(string nome, bool cond, string detalhe = "") => checa(nome, cond, detalhe);

		GD.Print("\n----- CAMADA 4: A VIAGEM TERRA->NAMEK, MEDIDA -----");

		PlanetaNoEspaco terra = Espaco.PreFeitos().First(p => p.Nome == "Earth");
		PlanetaNoEspaco namek = Espaco.PreFeitos().First(p => p.Nome == "Namek");
		double d = (namek.Pos - terra.Pos).Length;
		Vec2 rumo = (namek.Pos - terra.Pos).Normalized();

		// O PONTO DE PARTIDA E O DE PRODUCAO: e onde o `Decolar` larga quem sobe da Terra.
		Vec2 partida = Espaco.PontoDeDecolagem(terra);

		ServerPlayer aPe = Forjar(IdBaseDaViagemDeTeste + 1, "bancada: a pe", partida, 1_000_000);
		ServerPlayer deNave = Forjar(IdBaseDaViagemDeTeste + 2, "bancada: de nave", partida, 1_000_000);
		Nave? pod = null;

		try
		{
			// A VELOCIDADE DOS DOIS CORPOS SAI DO MESMO METODO DE PRODUCAO que o tique usa.
			RecalcularVelocidade(aPe);
			RecalcularVelocidade(deNave);
			float doCorpo = aPe.SpeedStat;

			// ---------------------------------------------------------------- A PE, COM O CORPO QUE ELE TEM
			double pxCorpo = AndarNoEspaco(aPe, rumo, TiquesDaMedicaoDeViagem,
										   MoveRules.SpeedPx(doCorpo) / 30.0, out int corrPe);

			Checa("a pe, o servidor aceita o passo do CORPO sem uma unica correcao", corrPe == 0,
				  $"{corrPe} correcao(oes)");
			Checa("...e o passo medido e o do `MoveRules.SpeedPx` deste corpo",
				  Math.Abs(pxCorpo - MoveRules.SpeedPx(doCorpo) / 30.0) < 0.05,
				  $"{pxCorpo:0.000} px/tique contra {MoveRules.SpeedPx(doCorpo) / 30.0:0.000}");

			// ---------------------------------------------------------------- A PE, EM STATS BASE
			// ============================ ESTE E O "SEM NAVE" DA SPEC, E ELE PRECISA SER 1,00x ============================
			// A escala do universo -- os sete dias do anime -- foi escrita sobre `MoveRules.BaseSpeedPx`,
			// ou seja sobre um corpo em 1,00x. O corpo desta bancada nao esta em 1,00x: `Espeed` sai dos
			// atributos, e um corpo forjado anda mais devagar que isso (0,55x na medicao de hoje).
			//
			// Medir SO com ele responderia outra pergunta ("quanto tempo ESTE corpo leva"), e a spec pediu
			// o numero do jogo. Entao o mesmo corpo e medido DUAS vezes, e o `SpeedStat` e posto em 1,00x
			// na segunda -- que e mexer no numero de ENTRADA, e nao no caminho: quem continua decidindo
			// quanto ele anda e o `ValidateStep` de producao.
			// ==========================================================================================================
			aPe.SpeedStat = 1f;
			aPe.OrcamentoPx = 0f;
			double pxPe = AndarNoEspaco(aPe, rumo, TiquesDaMedicaoDeViagem,
										MoveRules.BaseSpeedPx / 30.0, out int corrBase);

			Checa("em stats base (1,00x) o servidor aceita os 160 px/s da escala do universo",
				  corrBase == 0 && Math.Abs(pxPe - MoveRules.BaseSpeedPx / 30.0) < 0.05,
				  $"{pxPe:0.000} px/tique, {corrBase} correcao(oes)");

			// ---------------------------------------------------------------- DE NAVE, NO TETO
			pod = AssentarPodDeTeste(deNave);
			Checa("a bancada consegue um pod pelo funil de producao (fabricar + posicionar)", pod != null);
			if (pod == null) return;

			ComandoDeInteracao(deNave, "nave_usar", "");
			int degraus = 0;
			while (pod.Velocidade < Naves.VelocidadeMaxima && degraus++ < 100)
				ComandoDeInteracao(deNave, "nave_melhorar", "");

			Checa("o pod da medicao esta no teto e com o piloto dentro",
				  pod.Velocidade == Naves.VelocidadeMaxima && pod.PilotoId == deNave.Id,
				  $"velocidade {pod.Velocidade}, piloto {pod.PilotoId}");

			// O PEDIDO E O DA NAVE. Ver o cabecalho: e aqui que a medicao deixa de ser uma formula.
			double pxNave = AndarNoEspaco(deNave, rumo, TiquesDaMedicaoDeViagem,
										  Naves.PassoPorTique(pod.Velocidade), out int corrNave);

			Checa("de nave, o servidor ACEITA o passo de nave (o `SpeedStat` chegou ao `ValidateStep`)",
				  corrNave == 0, $"{corrNave} correcao(oes) -- o passo pedido foi aparado");
			Checa($"...e o passo medido e o de {Naves.VelocidadeMaxima}x",
				  Math.Abs(pxNave - Naves.PassoPorTique(Naves.VelocidadeMaxima)) < 1.0,
				  $"{pxNave:0.0} px/tique contra {Naves.PassoPorTique(Naves.VelocidadeMaxima):0.0}");
			Checa("...e a nave veio junto o tempo todo (o `TickDasNaves` copiou o piloto)",
				  Math.Abs(pod.X - deNave.Pos.X) < 1e-3 && Math.Abs(pod.Y - deNave.Pos.Y) < 1e-3,
				  $"nave ({pod.X:0},{pod.Y:0}) x corpo ({deNave.Pos.X:0},{deNave.Pos.Y:0})");

			// ---------------------------------------------------------------- O NUMERO
			// A VIAGEM INTEIRA E ARITMETICA EM CIMA DO PASSO MEDIDO, e nao uma simulacao de 300 mil
			// tiques: andar 1,6 milhao de px a 5,33 px por tique sao 301.507 tiques, e uma bancada que
			// demora nao e rodada. O que se mede e o PASSO; o resto e uma divisao, e ela esta dita.
			double segPe = d / (pxPe * 30.0);
			double segCorpo = d / (pxCorpo * 30.0);
			double segNave = d / (pxNave * 30.0);

			GD.Print($"       Terra->Namek: {d:N0} px medidos entre os dois corpos");
			GD.Print($"         SEM NAVE (1,00x)   : {pxPe,6:0.00} px/tique -> "
					 + $"{segPe / 60,7:0.0} min reais, {segPe / Espaco.SegundosPorDiaInGame,5:0.00} dias in-game");
			GD.Print($"         COM NAVE ({Naves.VelocidadeMaxima}x)     : {pxNave,6:0.0} px/tique -> "
					 + $"{segNave / 60,7:0.0} min reais, {segNave / Espaco.SegundosPorDiaInGame,5:0.00} dias in-game");
			GD.Print($"         a diferenca: {segPe / segNave:0.0}x mais rapido");
			GD.Print($"         (este corpo forjado anda {doCorpo:0.00}x: {pxCorpo:0.00} px/tique, "
					 + $"{segCorpo / Espaco.SegundosPorDiaInGame:0.0} dias -- ver o comentario)");

			// ============================ A MEDIDA CONTRA A PROMESSA ============================
			// `Naves.SegundosDeViagem` e o numero que o `InfoDaNave` escreve no chat e que o upgrade
			// promete a cada degrau. Se ele nao bater com o que o servidor deixa andar, o jogo esta
			// mentindo pro jogador -- e essa e a familia de defeito que so aparece comparando os dois.
			// ================================================================================
			double prometido = Naves.SegundosDeViagem(d, pod.Velocidade);
			Checa("o tempo MEDIDO bate com o que o `InfoDaNave` promete ao jogador",
				  Math.Abs(segNave - prometido) < prometido * 0.01,
				  $"medido {segNave / 60:0.00} min, prometido {prometido / 60:0.00} min");

			Checa("...e a pe ele bate com o `Espaco.SegundosDeViagem`, que e o que a carta estelar usa",
				  Math.Abs(segPe - Espaco.SegundosDeViagem(d)) < Espaco.SegundosDeViagem(d) * 0.01,
				  $"medido {segPe / 60:0.0} min, prometido {Espaco.SegundosDeViagem(d) / 60:0.0} min");

			// E A ESCALA DO ANIME: a viagem medida a pe TEM que ser os sete dias, senao o universo
			// deixou de ter o tamanho que a `DistanciaTerraNamek` diz que ele tem.
			Checa($"sem nave, a viagem MEDIDA da os {Espaco.DiasTerraNamek:0} dias do anime",
				  Math.Abs(segPe / Espaco.SegundosPorDiaInGame - Espaco.DiasTerraNamek) < 0.05,
				  $"{segPe / Espaco.SegundosPorDiaInGame:0.00} dias");
			Checa("e a nave a corta pra menos de um quarto de dia",
				  Naves.DiasInGame(d, Naves.VelocidadeMaxima) < 0.25,
				  $"{Naves.DiasInGame(d, Naves.VelocidadeMaxima):0.000} dias");
		}
		finally
		{
			if (pod != null) _naves.Remove(pod);
			Recolher(aPe);
			Recolher(deNave);
			GravarNaves();
		}
	}

	// =====================================================================
	// 2) A CHEGADA -- E ELA E A PERGUNTA DO TUNELAMENTO, FEITA NO ALVO DE VERDADE
	// =====================================================================
	/// <summary>
	/// CHEGAR EM NAMEK A 208 PX POR TIQUE.
	///
	/// ============================ POR QUE ISTO E UM TESTE E NAO UMA FORMALIDADE ============================
	/// Namek tem raio 200 -- 400 px de diametro -- e o passo no teto e 208 px. O pouso
	/// (`Espaco.PlanetaSob`) e uma pergunta POR PONTO a 30 Hz: a nave no teto tem *duas* amostras
	/// dentro do planeta numa aproximacao pelo centro, e a bancada de contas ja afirmava que 208 &lt; 300
	/// (o diametro do menor mundo GERADO). Este teste faz a coisa: ele aproxima de verdade e ve o
	/// `TickDoEspaco` de producao pousar.
	///
	/// E ele mede tambem a APROXIMACAO RASANTE, que a conta do diametro nao cobre: quem passa de
	/// raspao corta uma CORDA e nao o diametro, e uma corda pode ser menor que o passo. O numero sai
	/// impresso -- se um dia o teto de velocidade subir, e aqui que a conta vai estar.
	/// ================================================================================================
	/// </summary>
	private void AChegada(Action<string, bool, string> checa)
	{
		void Checa(string nome, bool cond, string detalhe = "") => checa(nome, cond, detalhe);

		GD.Print("\n----- A CHEGADA (o pouso a 208 px por tique) -----");

		PlanetaNoEspaco terra = Espaco.PreFeitos().First(p => p.Nome == "Earth");
		PlanetaNoEspaco namek = Espaco.PreFeitos().First(p => p.Nome == "Namek");
		Vec2 rumo = (namek.Pos - terra.Pos).Normalized();

		const float antesDoAlvo = 6000f;
		Vec2 partida = namek.Pos - rumo * (namek.Raio + antesDoAlvo);

		ServerPlayer piloto = Forjar(IdBaseDaViagemDeTeste + 3, "bancada: a chegada", partida, 1_000_000);
		Nave? pod = null;

		try
		{
			RecalcularVelocidade(piloto);
			pod = AssentarPodDeTeste(piloto);
			if (pod == null) { Checa("a chegada tem um pod", false, "sem pod"); return; }

			ComandoDeInteracao(piloto, "nave_usar", "");
			int g = 0;
			while (pod.Velocidade < Naves.VelocidadeMaxima && g++ < 100)
				ComandoDeInteracao(piloto, "nave_melhorar", "");

			// APROXIMACAO PELO CENTRO. O laco para quando o `TickDoEspaco` de producao tirar o corpo do
			// espaco -- que e exatamente o que pousar quer dizer.
			int tiques = 0;
			int teto = (int)(antesDoAlvo / Naves.PassoPorTique(pod.Velocidade)) * 3 + 30;
			int corrigidos = 0;
			while (Espaco.EhEspaco(piloto.Zone) && tiques++ < teto)
				TiqueDeViagem(piloto, rumo, Naves.PassoPorTique(pod.Velocidade), tiques, ref corrigidos);

			Checa($"a nave no teto POUSA em Namek (levou {tiques} tiques pros ultimos {antesDoAlvo:N0} px)",
				  piloto.Zone.Kind == ZoneKey.KindPremade && piloto.Zone.Name == "Namek",
				  $"zona: {piloto.Zone}");
			Checa("...e a nave desceu junto, na mesma `ZoneKey`",
				  pod.Zona.Equals(piloto.Zone), $"nave em {pod.Zona}");

			double esperados = antesDoAlvo / Naves.PassoPorTique(pod.Velocidade);
			Checa($"...no numero de tiques que o passo medido previa (~{esperados:0})",
				  tiques <= esperados * 1.15 + 3, $"{tiques} tiques contra ~{esperados:0}");

			// ---------------------------------------------------------------- A RASANTE, MEDIDA
			// Nao e uma afirmacao: e o numero na mesa. A corda a `f` do raio mede `2*R*sqrt(1-f^2)`.
			GD.Print($"       rasante em Namek (R={namek.Raio:0}), passo {Naves.PassoPorTique(Naves.VelocidadeMaxima):0} px:");
			foreach (float f in new[] { 0f, 0.5f, 0.9f })
			{
				double corda = 2 * namek.Raio * Math.Sqrt(1 - f * f);
				GD.Print($"         a {f * 100:0}% do raio: corda de {corda:0} px "
						 + $"-> {corda / Naves.PassoPorTique(Naves.VelocidadeMaxima):0.0} amostras");
			}
			Checa("numa aproximacao pelo CENTRO sobra amostra: a corda e maior que o passo",
				  2 * namek.Raio > Naves.PassoPorTique(Naves.VelocidadeMaxima),
				  $"{2 * namek.Raio:0} px de diametro contra {Naves.PassoPorTique(Naves.VelocidadeMaxima):0} px de passo");

			// ============================ E A RASANTE, VOADA DE VERDADE -- DEZ VEZES ============================
			// A tabela acima e aritmetica. Esta parte e a coisa: a nave passa a 90% do raio (corda de 174
			// px contra passo de 208) e o `TickDoEspaco` de producao decide.
			//
			// DEZ FASES, e nao uma: com a corda MENOR que o passo, cabe no maximo UMA amostra dentro do
			// disco -- e se ela cai dentro ou no vao depende de ONDE a viagem comecou. Uma unica rasante
			// mediria a sorte daquele ponto de partida e chamaria isso de regra; a primeira versao desta
			// secao fez exatamente isso e AFIRMOU "passa reto", e a bancada reprovou a frase (aquela fase
			// pousou). O que ha de verdade e uma LOTERIA DE FASE, e ela se mede varrendo a fase.
			//
			// ISTO NAO E UM ENDOSSO: e o LIMITE MEDIDO do pouso por PONTO, e ele so existe porque a nave
			// existe (a pe o passo e 5 px e nenhuma corda escapa). O conserto e teste de SEGMENTO -- o
			// `MoveRules.PathOccupied` ja e esse padrao --, e ele nao foi feito aqui porque mexe no tique
			// do espaco de todo mundo. No dia em que entrar, a linha do "passa reto" fica VERMELHA e deve
			// ser trocada por "pousa em todas as fases": e assim que se sabe que o conserto pegou.
			// ================================================================================================
			double passoDaNave = Naves.PassoPorTique(pod.Velocidade);
			var perp = new Vec2(-rumo.Y, rumo.X);
			Vec2 rasante = namek.Pos - rumo * (namek.Raio + 3000f) + perp * (namek.Raio * 0.9f);

			int pousou = 0, passouReto = 0;
			const int fases = 10;
			for (int f = 0; f < fases; f++)
			{
				MoveToZone(piloto.Id, ZonaDoEspaco, rasante + rumo * (float)(passoDaNave * f / fases));
				piloto.OrcamentoPx = 0f;

				int corrR = 0;
				int passosDaRasante = (int)(6000 / passoDaNave) + 2;
				for (int t = 0; t < passosDaRasante && Espaco.EhEspaco(piloto.Zone); t++)
					TiqueDeViagem(piloto, rumo, passoDaNave, t, ref corrR);

				if (Espaco.EhEspaco(piloto.Zone)) passouReto++; else pousou++;
			}

			GD.Print($"       rasante voada a 90% do raio, {fases} fases: {pousou} pousaram, "
					 + $"{passouReto} passaram RETO (corda 174 px contra passo {passoDaNave:0} px)");
			Checa("de raspao, ALGUMAS fases atravessam sem pousar -- e o limite medido do pouso por PONTO",
				  passouReto > 0, $"{passouReto} de {fases} passaram reto");
			Checa("...e nas outras ele pega: e loteria de fase, e nao 'a nave nunca pousa de raspao'",
				  pousou > 0, $"{pousou} de {fases} pousaram");
			Checa("...e a pe NENHUMA fase escaparia: o passo de 5 px nao pula corda nenhuma",
				  MoveRules.BaseSpeedPx / 30.0 < 2 * namek.Raio * Math.Sqrt(1 - 0.9 * 0.9),
				  $"passo a pe {MoveRules.BaseSpeedPx / 30.0:0.0} px contra corda de "
				  + $"{2 * namek.Raio * Math.Sqrt(1 - 0.9 * 0.9):0} px");
		}
		finally
		{
			if (pod != null) _naves.Remove(pod);
			Recolher(piloto);
			GravarNaves();
		}
	}

	// =====================================================================
	// 3) O TETO QUE DISPARA -- E O PRECO DE CHEGAR NELE
	// =====================================================================
	/// <summary>
	/// O UPGRADE, DEGRAU A DEGRAU, E A RECUSA NO FIM.
	///
	/// ============================ O QUE ESTAVA FALTANDO ============================
	/// A camada 1 afirmava duas vezes a MESMA coisa (`n.Velocidade == VelocidadeMaxima`) e chamava a
	/// segunda de "o teto DISPARA". Nao dispara nada: ela nao tenta melhorar, nao olha o zeni e nao
	/// olha a recusa. Um teto so esta provado quando uma TENTATIVA passa por ele e volta -- com o
	/// dinheiro intacto e com a frase dita (regra 0.7 + regra 5 da casa, falhar alto).
	/// ==========================================================================
	/// </summary>
	private void OTetoQueDispara(Action<string, bool, string> checa)
	{
		void Checa(string nome, bool cond, string detalhe = "") => checa(nome, cond, detalhe);

		GD.Print("\n----- O TETO DE VELOCIDADE, E O PRECO ATE ELE -----");

		ServerPlayer dono = Forjar(IdBaseDaViagemDeTeste + 4, "bancada: o comprador", new Vec2(0, 0), 1_000_000);
		Nave? pod = null;

		try
		{
			pod = AssentarPodDeTeste(dono);
			if (pod == null) { Checa("o comprador tem um pod", false, "sem pod"); return; }

			// ---------------------------------------------------------------- UM DEGRAU COBRA `1000*Speed`
			dono.Ficha.Zeni = 10_000_000;
			double zeniAntes = dono.Ficha.Zeni;
			int velAntes = pod.Velocidade;
			double custoEsperado = Naves.CustoDoUpgrade(velAntes);

			ComandoDeInteracao(dono, "nave_melhorar", "");
			Checa($"um degrau sobe a velocidade ({velAntes} -> {pod.Velocidade})", pod.Velocidade == velAntes + 1);
			Checa($"...e cobra exatamente `1000*Speed` ({custoEsperado:N0} z)",
				  Math.Abs(zeniAntes - dono.Ficha.Zeni - custoEsperado) < 1e-6,
				  $"cobrou {zeniAntes - dono.Ficha.Zeni:N0}");

			// ---------------------------------------------------------------- SEM ZENI, RECUSA
			dono.Ficha.Zeni = Naves.CustoDoUpgrade(pod.Velocidade) - 1;
			int velPobre = pod.Velocidade;
			EscutaDeAvisos = [];
			ComandoDeInteracao(dono, "nave_melhorar", "");
			List<string> ditosPobre = EscutaDeAvisos!;
			EscutaDeAvisos = null;

			Checa("sem zeni, o degrau e recusado", pod.Velocidade == velPobre, $"velocidade {pod.Velocidade}");
			Checa("...e a recusa DIZ o preco, em vez de calar",
				  ditosPobre.Any(l => l.Contains("custa")), string.Join(" | ", ditosPobre));

			// ---------------------------------------------------------------- ATE O TETO, SOMANDO O GASTO
			dono.Ficha.Zeni = 10_000_000;
			double antesDaEscada = dono.Ficha.Zeni;
			int passos = 0;
			while (pod.Velocidade < Naves.VelocidadeMaxima && passos++ < Naves.VelocidadeMaxima + 10)
				ComandoDeInteracao(dono, "nave_melhorar", "");

			double gastoTotal = antesDaEscada - dono.Ficha.Zeni;
			double escadaInteira = 0;
			for (int v = Naves.VelocidadeInicial; v < Naves.VelocidadeMaxima; v++)
				escadaInteira += Naves.CustoDoUpgrade(v);

			GD.Print($"       do degrau 2 ao {Naves.VelocidadeMaxima}: {gastoTotal:N0} z "
					 + $"(a escada inteira, do 1, custa {escadaInteira:N0} z -- sete pods)");
			Checa($"a escada chega no teto do DM ({Naves.VelocidadeMaxima}x)",
				  pod.Velocidade == Naves.VelocidadeMaxima, $"velocidade {pod.Velocidade}");

			// ---------------------------------------------------------------- O TETO DISPARA
			double zeniNoTeto = dono.Ficha.Zeni;
			EscutaDeAvisos = [];
			ComandoDeInteracao(dono, "nave_melhorar", "");
			List<string> ditosTeto = EscutaDeAvisos!;
			EscutaDeAvisos = null;

			Checa("NO TETO, melhorar de novo NAO sobe a velocidade",
				  pod.Velocidade == Naves.VelocidadeMaxima, $"velocidade {pod.Velocidade}");
			Checa("...e NAO cobra nada (a recusa vem antes da cobranca)",
				  Math.Abs(dono.Ficha.Zeni - zeniNoTeto) < 1e-6,
				  $"{zeniNoTeto:N0} -> {dono.Ficha.Zeni:N0}");
			Checa("...e diz o limite em voz alta",
				  ditosTeto.Any(l => l.Contains("limite")), string.Join(" | ", ditosTeto));

			// ---------------------------------------------------------------- E O TETO DE CIMA, O DA AMOSTRAGEM
			// O `Fator` apara mesmo um numero que o verbo nunca produziria. Ele e a ultima linha de
			// defesa do dia em que alguem subir `VelocidadeMaxima` -- e um teto que so existe no verbo
			// nao protege quem edita o `naves.json` na mao.
			pod.Velocidade = 500;
			Checa($"e um degrau absurdo no disco ainda e aparado em {Naves.TetoSemTunelamento}x",
				  Naves.Fator(pod.Velocidade) <= Naves.TetoSemTunelamento,
				  $"fator {Naves.Fator(pod.Velocidade)}");
			Checa("...e o passo aparado continua abaixo do menor planeta gerado (300 px)",
				  Naves.PassoPorTique(pod.Velocidade) < 300,
				  $"{Naves.PassoPorTique(pod.Velocidade):0} px/tique");
			pod.Velocidade = Naves.VelocidadeMaxima;
		}
		finally
		{
			if (pod != null) _naves.Remove(pod);
			Recolher(dono);
			GravarNaves();
		}
	}

	// =====================================================================
	// 4) O REINICIO DO SERVIDOR
	// =====================================================================
	/// <summary>
	/// A NAVE SOBREVIVE AO REBOOT -- e a `ZoneKey` inteira sobrevive junto.
	///
	/// ============================ O CRIVO NAO E O DISCO ============================
	/// A camada 1 provou que `NavesParadasEm` distingue dois planetas gerados homonimos. Isso e o
	/// CRIVO em memoria. O defeito que a `Obra` tinha nao morava la: morava na SERIALIZACAO (`Obra.Zona`
	/// era uma `string`, remontada com `ZoneKey.Premade` -- hoje sao os tres campos, como aqui). Uma
	/// nave pode passar no crivo e voltar do disco noutro mundo -- e ninguem veria, porque o crivo
	/// continuaria concordando consigo mesmo.
	///
	/// Por isso este teste passa pelo `GravarNaves` e pelo `CarregarNaves` DE PRODUCAO -- os mesmos
	/// dois metodos que o boot chama --, e nao por uma copia de JSON escrita aqui.
	/// ==========================================================================
	///
	/// ============================ E ELE DEVOLVE A LISTA VIVA ============================
	/// `CarregarNaves` cria objetos NOVOS: as naves que estao sendo pilotadas agora perderiam o
	/// `PilotoId` (que e `[JsonIgnore]` de proposito). Por isso a lista viva e guardada antes e
	/// reposta depois -- a bancada mede a volta pelo disco sem entregar ao servidor as copias que ela
	/// mesma leu.
	/// ================================================================================
	/// </summary>
	private void OReinicioDoServidor(Action<string, bool, string> checa)
	{
		void Checa(string nome, bool cond, string detalhe = "") => checa(nome, cond, detalhe);

		GD.Print("\n----- O REINICIO: A NAVE VOLTA NO MUNDO CERTO -----");

		// A LISTA VIVA, pra devolver no fim.
		List<Nave> vivas = [.. _naves];
		int proximoAntes = _proximaNaveId;

		ServerPlayer dono = Forjar(IdBaseDaViagemDeTeste + 5, "bancada: o dono da frota", new Vec2(0, 0), 1_000_000);
		var forjadas = new List<Nave>();

		try
		{
			// AS TRES ZONAS DIFICEIS. As duas primeiras sao o caso que o `% 1000` do
			// `SistemaSolar.Planeta` produz de verdade (dois mundos gerados de mesmo nome); a terceira
			// e a que a `Obra` nem sabe descrever -- o espaco e `KindProcedural` com a seed do universo.
			var geradoA = ZoneKey.Procedural("Verdejante-1042", 111);
			var geradoB = ZoneKey.Procedural("Verdejante-1042", 222);
			ZoneKey espaco = ZonaDoEspaco;

			foreach (ZoneKey z in new[] { geradoA, geradoB, espaco })
			{
				// NASCEM PELO FUNIL DE PRODUCAO (fabricar + posicionar) e depois trocam de mundo pelo
				// MESMO `PorZona` que o `TickDasNaves` usa quando uma nave pousa noutro planeta. Levar
				// o corpo forjado ate um planeta gerado so pra assentar la geraria um mundo inteiro
				// dentro do tique -- e o que se testa aqui e o disco, e nao o gerador.
				Nave? n = AssentarPodDeTeste(dono);
				if (n == null) continue;
				n.PorZona(z);
				n.Velocidade = 7;
				forjadas.Add(n);
			}

			Checa("as tres naves de teste existem antes do reinicio", forjadas.Count == 3,
				  $"{forjadas.Count} nave(s)");
			if (forjadas.Count != 3) return;

			// ---------------------------------------------------------------- O REINICIO
			GravarNaves();
			_naves.Clear();
			_proximaNaveId = 1;
			CarregarNaves();          // <- o MESMO metodo que o boot chama

			var voltouA = _naves.FirstOrDefault(x => x.Id == forjadas[0].Id);
			var voltouB = _naves.FirstOrDefault(x => x.Id == forjadas[1].Id);
			var voltouE = _naves.FirstOrDefault(x => x.Id == forjadas[2].Id);

			Checa("as tres voltaram do disco", voltouA != null && voltouB != null && voltouE != null);
			if (voltouA == null || voltouB == null || voltouE == null) return;

			Checa("a nave do planeta GERADO volta com a zona inteira (tipo + nome + seed)",
				  voltouA.Zona.Equals(geradoA),
				  $"voltou em {voltouA.Zona} (tipo {voltouA.ZonaTipo}, seed {voltouA.ZonaSeed})");
			Checa("...e a homonima volta na SUA, e nao na da outra",
				  voltouB.Zona.Equals(geradoB) && !voltouB.Zona.Equals(voltouA.Zona),
				  $"voltou em {voltouB.Zona}");
			Checa("a nave largada NO ESPACO volta no espaco (a zona que a `Obra` nem sabe escrever)",
				  voltouE.Zona.Equals(espaco) && Espaco.EhEspaco(voltouE.Zona), $"voltou em {voltouE.Zona}");
			Checa("...e o crivo continua separando as duas homonimas DEPOIS do reinicio",
				  NavesParadasEm(geradoA).Count(x => x.Id == voltouA.Id) == 1
				  && NavesParadasEm(geradoB).Count(x => x.Id == voltouA.Id) == 0);

			Checa("o degrau de velocidade comprado sobreviveu ao reboot", voltouA.Velocidade == 7,
				  $"velocidade {voltouA.Velocidade}");
			Checa("...e o dono tambem", voltouA.DonoNome == dono.Name, $"dono {voltouA.DonoNome}");
			Checa("o `_proximaNaveId` foi refeito acima do maior id (senao a proxima nave apagaria uma)",
				  _proximaNaveId > _naves.Max(x => x.Id), $"proximo {_proximaNaveId}");

			// O QUE NAO PODE VOLTAR: pilotar e lancar sao SESSAO. Uma nave que voltasse "pilotada"
			// teria um dono fantasma que ninguem consegue desembarcar.
			Checa("nenhuma nave volta do disco pilotada ou lancando",
				  _naves.All(x => x.PilotoId == 0 && x.LancaEm == 0 && x.VoltaEm == 0));

			// ============================ A PROVA POR CONTRASTE: O FORMATO DA `Obra` ============================
			// Este e o teste que reprovaria a `Obra` se ela fosse quem guardasse a nave. Ele nao e uma
			// opiniao sobre o codigo dela: e o formato dela (uma `string` de nome, remontada com
			// `ZoneKey.Premade`) aplicado aos MESMOS tres dados.
			// ==============================================================================================
			int erradas = 0;
			foreach (Nave n in new[] { voltouA, voltouB, voltouE })
				if (!ZoneKey.Premade(n.ZonaNome).Equals(n.Zona)) erradas++;

			Checa("...e o formato da `Obra` (so o nome) devolveria as TRES no mundo errado",
				  erradas == 3, $"{erradas} de 3 erradas");
		}
		finally
		{
			// DEVOLVE A LISTA VIVA -- as copias lidas do disco vao embora com ela.
			_naves.Clear();
			_naves.AddRange(vivas);
			_proximaNaveId = proximoAntes;
			GravarNaves();
			Recolher(dono);
		}
	}

	// =====================================================================
	// 5) A ESTRELA NO CAMINHO -- O QUE O DONO MANDOU MEDIR
	// =====================================================================
	/// <summary>
	/// PASSAR PERTO (E POR DENTRO) DE UMA ESTRELA, A PE E DE NAVE.
	///
	/// ============================ TRES PERGUNTAS, TRES MEDIDAS ============================
	/// 1. **Quem esta AO LEME queima?** Hoje sim -- o corpo dele esta na zona do espaco. Isto nao e
	///    uma escolha deste port: e o que sai de graca do desenho, e o dono ainda nao decidiu se quer
	///    mudar. A bancada AFIRMA o comportamento de hoje pra que trocar de ideia custe uma linha
	///    vermelha, e nao um bug descoberto meses depois.
	/// 2. **Quem esta DENTRO do casco queima?** Nao -- o interior e `ZoneKey.Interior`, e o
	///    `TickDoEspaco` devolve na primeira linha pra quem nao esta no espaco. A camada 2 afirmava
	///    isso por uma pergunta de Core (`!Espaco.EhEspaco(dentro)`); aqui um corpo fica dentro de uma
	///    nave parada NO CENTRO DE UMA ESTRELA por 300 tiques e a vida dele e contada.
	/// 3. **A nave rapida ATRAVESSA a estrela sem ser vista?** Nao, e este e o numero que impede a
	///    velocidade de virar imunidade por acidente: a travessia e simulada tique a tique e os tiques
	///    passados DENTRO do raio letal sao contados.
	/// ==================================================================================
	/// </summary>
	private void AEstrelaNoCaminho(Action<string, bool, string> checa)
	{
		void Checa(string nome, bool cond, string detalhe = "") => checa(nome, cond, detalhe);

		GD.Print("\n----- A ESTRELA NO CAMINHO (o que o dono mandou medir) -----");

		Estrela e = EstrelaDeTeste(ClasseDeEstrela.AnaVermelha, out _);
		if (e.Raio <= 0) { Checa("achei uma ana vermelha pra travessia", false, "raio zero"); return; }

		GD.Print($"       {e.Classe}: raio letal {e.Raio:0} px, coroa a {e.Raio * CalorDaEstrela.RazaoDaCoroa:0} px");

		// O RUMO ATRAVESSA O CENTRO, de sul pra norte, e a travessia comeca fora da coroa.
		var rumo = new Vec2(0, -1);
		float largura = e.Raio * (float)CalorDaEstrela.RazaoDaCoroa * 2.2f;
		Vec2 partida = e.Pos + new Vec2(0, largura / 2);

		// ---------------------------------------------------------------- A PE
		ServerPlayer aPe = Forjar(IdBaseDaViagemDeTeste + 6, "bancada: atravessa a pe", partida, 1_000_000);
		double perdeuAPe;
		int dentroAPe;
		try
		{
			RecalcularVelocidade(aPe);
			(perdeuAPe, dentroAPe, _) = Atravessar(aPe, e, rumo, largura, MoveRules.SpeedPx(aPe.SpeedStat) / 30.0);
			GD.Print($"       a pe   ({MoveRules.SpeedPx(aPe.SpeedStat) / 30.0,6:0.0} px/tique): "
					 + $"{dentroAPe,4} tiques dentro do raio letal, {perdeuAPe,7:0.0} de vida perdida");
		}
		finally { Recolher(aPe); }

		// ---------------------------------------------------------------- DE NAVE, NO TETO
		ServerPlayer piloto = Forjar(IdBaseDaViagemDeTeste + 7, "bancada: atravessa de nave", partida, 1_000_000);
		Nave? pod = null;
		double perdeuDeNave;
		int dentroDeNave;
		try
		{
			RecalcularVelocidade(piloto);
			pod = AssentarPodDeTeste(piloto);
			if (pod == null) { Checa("a travessia de nave tem um pod", false, "sem pod"); return; }

			ComandoDeInteracao(piloto, "nave_usar", "");
			int g = 0;
			while (pod.Velocidade < Naves.VelocidadeMaxima && g++ < 100)
				ComandoDeInteracao(piloto, "nave_melhorar", "");

			(perdeuDeNave, dentroDeNave, _) =
				Atravessar(piloto, e, rumo, largura, Naves.PassoPorTique(pod.Velocidade));

			GD.Print($"       de nave ({Naves.PassoPorTique(pod.Velocidade),6:0.0} px/tique): "
					 + $"{dentroDeNave,4} tiques dentro do raio letal, {perdeuDeNave,7:0.0} de vida perdida");

			// ============================ AS TRES AFIRMACOES QUE DECIDEM A PERGUNTA ============================
			Checa("QUEM PILOTA QUEIMA: o corpo esta na zona do espaco, e o casco nao o protege hoje",
				  perdeuDeNave > 0, $"perdeu {perdeuDeNave:0.0}");
			Checa("...mas a VELOCIDADE protege: de nave se perde menos vida que a pe na mesma travessia",
				  perdeuDeNave < perdeuAPe, $"nave {perdeuDeNave:0.0} contra pe {perdeuAPe:0.0}");
			Checa("...e a nave rapida NAO atravessa sem ser vista (a amostragem a pega dentro)",
				  dentroDeNave > 0, $"{dentroDeNave} tiques dentro");
			Checa("...e o corpo a pe sofre muito mais tiques de fogo",
				  dentroAPe > dentroDeNave * 5, $"{dentroAPe} contra {dentroDeNave}");

			// ---------------------------------------------------------------- 2) O CASCO DA CAPITAL SHIP
			// UMA NAVE GRANDE PARADA NO CENTRO DA ESTRELA, com gente dentro. E a unica das tres naves
			// que tem "dentro", e a pergunta do dono ("o casco protege do sol?") so existe por causa dela.
			ATripulacaoNaoQueima(e, checa);
		}
		finally
		{
			if (pod != null) _naves.Remove(pod);
			Recolher(piloto);
			GravarNaves();
		}
	}

	/// <summary>
	/// UM CORPO DENTRO DO CASCO, COM A NAVE NO CENTRO DE UMA ESTRELA.
	///
	/// A nave grande e forjada direto na lista (e nao assentada pela aba Tech): o que esta sob teste e
	/// o INTERIOR contra o `TickDoEspaco`, e assentar uma Capital Ship de dois milhoes de zeni dentro
	/// de uma estrela nao e um caminho que exista no jogo -- ela chega la voando.
	/// </summary>
	private void ATripulacaoNaoQueima(Estrela e, Action<string, bool, string> checa)
	{
		void Checa(string nome, bool cond, string detalhe = "") => checa(nome, cond, detalhe);

		var grande = new Nave
		{
			Id = _proximaNaveId++,
			Tipo = NaveGrande.Tipo,
			X = e.Pos.X,
			Y = e.Pos.Y,
			ArmaduraMax = 1000,
			Armadura = 1000,
		};
		grande.PorZona(ZonaDoEspaco);
		_naves.Add(grande);

		ZoneKey dentro = NaveGrande.ZonaDoInterior(grande.Id);
		ServerPlayer tripulante = Forjar(IdBaseDaViagemDeTeste + 8, "bancada: a tripulacao", e.Pos, 1_000_000);

		try
		{
			MoveToZone(tripulante.Id, dentro, NaveGrande.PixelDe(NaveGrande.CelDaChegada));
			double vidaAntes = tripulante.Combate!.Corpo.Vida();

			// TRINTA SEGUNDOS DENTRO DE UMA ESTRELA. Um corpo de 1e6 la fora cede em ~14 s.
			int corr = 0;
			for (int t = 0; t < 900; t++) TiqueDeViagem(tripulante, new Vec2(0, 0), 0, t, ref corr);

			Checa("dentro do casco, 900 tiques no CENTRO de uma estrela nao custam um ponto de vida",
				  tripulante.Combate.Corpo.Vida() >= vidaAntes - 1e-6,
				  $"{vidaAntes:0.##} -> {tripulante.Combate.Corpo.Vida():0.##}");
			Checa("...e a nave que os carrega esta MESMO dentro do raio letal (o teste nao e vazio)",
				  Sistemas.EstrelaPerto(SeedDoUniverso, new Vec2(grande.X, grande.Y), out _, out double dist)
				  && dist <= e.Raio, $"distancia {dist:0} px do centro");
			Checa("...porque o interior nao e o espaco -- o `TickDoEspaco` nem chega a perguntar",
				  !Espaco.EhEspaco(dentro), $"zona: {dentro}");
		}
		finally
		{
			Recolher(tripulante);
			_naves.Remove(grande);
		}
	}

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// UM POD PELO FUNIL DE PRODUCAO: aba Tech (`construir`) + verbo `posicionar`, com o corpo em pe
	/// onde estiver. Devolve a nave assentada, ou nulo.
	///
	/// Ele existe pra que nenhuma secao desta camada monte uma `Nave` na mao: uma bancada que
	/// instancia o objeto sob teste mede o construtor, e nao o jogo. E o mesmo motivo pelo qual o
	/// `ForjarPassageiro` da camada 2 nasce pelo `NascerNpc`.
	/// </summary>
	private Nave? AssentarPodDeTeste(ServerPlayer pl)
	{
		pl.Ficha.techskill = 60;
		if (pl.Ficha.Zeni < 1_000_000) pl.Ficha.Zeni = 10_000_000;

		ComandoDeTech(pl, "construir", "Spacepod");
		if (pl.Mochila.Quantos("Spacepod") <= 0) return null;

		ComandoDeTech(pl, "posicionar", $"Spacepod/{pl.Pos.X:0}/{pl.Pos.Y:0}");
		pl.Mochila.Tirar("Spacepod");   // se o assentamento foi recusado, o item nao fica sobrando
		return NavePerto(pl);
	}

	/// <summary>
	/// UM TIQUE DE SERVIDOR PRA QUEM VIAJA NO ESPACO -- na ordem e na cadencia de producao.
	///
	/// ============================ AS QUATRO COISAS, E POR QUE SAO ESTAS QUATRO ============================
	///   * `Ficha.Tick` a 5 Hz (`TicksPorFicha`) -- `expressedBP` e RECALCULADO e cai conforme o corpo
	///     se machuca; como o dano da estrela escala com ele, congela-lo mediria outro jogo. E a mesma
	///     licao que o `Cozinhar` da bancada do sol ja tinha aprendido;
	///   * `RegenerarPassivo` a 30 Hz -- e o que o `TickCombate` chama, e a estrela nao a desliga;
	///   * `MoveRules.ValidateStep` -- **o passo e PEDIDO e o servidor decide**. O mapa vai nulo porque
	///     no espaco ele e nulo mesmo (a zona nao esta no manifesto; ver a bancada do sol);
	///   * `TickDoEspaco` + `TickDasNaves` -- o pouso, o sol, e a nave copiando o piloto.
	/// ================================================================================================
	/// </summary>
	private void TiqueDeViagem(ServerPlayer pl, Vec2 rumo, double passoPedidoPx, int t, ref int corrigidos)
	{
		if (t % TicksPorFicha == 0) pl.Ficha.Tick(agoraMs: NowMs());
		RegenerarPassivo(pl, Protocol.TickSeconds);

		if (passoPedidoPx > 0)
		{
			Vec2 querido = pl.Pos + rumo * (float)passoPedidoPx;
			if (!MoveRules.ValidateStep(pl.Pos, querido, Protocol.TickSeconds, pl.SpeedStat,
										null, ref pl.OrcamentoPx, out Vec2 ok)) corrigidos++;
			pl.Pos = ok;
		}

		TickDoEspaco(pl);
		TickDasNaves();
	}

	/// <summary>
	/// ANDA `tiques` tiques no espaco e devolve o passo MEDIO por tique, em px.
	///
	/// O passo pedido e o do chamador (o do corpo ou o da nave); quem decide quanto entra na conta e o
	/// `ValidateStep` -- ver <see cref="TiqueDeViagem"/>.
	/// </summary>
	private double AndarNoEspaco(ServerPlayer pl, Vec2 rumo, int tiques, double passoPedidoPx,
								 out int corrigidos)
	{
		corrigidos = 0;
		Vec2 saiuDe = pl.Pos;
		for (int t = 0; t < tiques; t++) TiqueDeViagem(pl, rumo, passoPedidoPx, t, ref corrigidos);
		return (pl.Pos - saiuDe).Length / tiques;
	}

	/// <summary>
	/// ATRAVESSA UMA ESTRELA de ponta a ponta e devolve o que custou.
	///
	/// Devolve (vida perdida, tiques passados DENTRO do raio letal, tiques gastos).
	/// </summary>
	private (double Perdeu, int Dentro, int Tiques) Atravessar(ServerPlayer pl, Estrela e, Vec2 rumo,
															   float distancia, double passoPx)
	{
		double vidaAntes = pl.Combate!.Corpo.Vida();
		double pior = 0;
		int dentro = 0, corr = 0;

		int teto = (int)(distancia / Math.Max(passoPx, 0.1)) + 5;
		for (int t = 0; t < teto; t++)
		{
			TiqueDeViagem(pl, rumo, passoPx, t, ref corr);
			if ((pl.Pos - e.Pos).Length <= e.Raio) dentro++;
			pior = Math.Max(pior, vidaAntes - pl.Combate.Corpo.Vida());
			if (pl.Ficha.dead) break;
		}
		return (pior, dentro, teto);
	}
}
