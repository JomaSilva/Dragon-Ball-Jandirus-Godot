using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DO SORTEIO DO ARREMESSO (`--kbteste`) -- roda no BOOT, sem ninguem em jogo.
///
///     Godot --headless --path . -- --server --rede 7912 --kbteste
///
/// ============================ A QUEIXA QUE ELA MEDE ============================
/// *"o SOCO DO DASH ta SEMPRE dando KNOCK BACK (acho q o soco forte tb). eles deveriam ter uma
/// CHANCE UM POUCO MAIOR de dar knock back, pq fui lutar com uns npcs e era UM JOGANDO O OUTRO PRA
/// LONGE e tava estranha a luta"*.
///
/// O "soco do dash" e o soco PESADO: o arranque e o `Aproximar` de dentro do `Atacar`, e so o
/// pesado busca a 160 px (480 com alvo marcado). Nao sao dois defeitos, e um.
///
/// A CAUSA, medida antes de qualquer conserto: `attack cmn.dm:115` manda o pesado direto pro
/// `Impact` num `else` sem `prob()` nenhum, e o port copiava isso ao pe da letra. Resultado com dois
/// Saiyajins de BP 5.551: **100% dos pesados que encostam arremessam**, em qualquer razao de BP a
/// partir de 1,0x, jogando o corpo 517 a 576 px em menos de um segundo.
/// ==============================================================================
///
/// ============================ POR QUE A PERGUNTA NAO E "O SORTEIO EXISTE?" ============================
/// Porque essa e a pergunta que fica verde com o jogo errado -- e este projeto ja tem isso por
/// escrito ("uma regra escrita nao e uma regra ligada"). Um sorteio pode existir e:
///
///   * estar largo demais (98%) ou apertado demais (5%) -- as duas viram queixa do dono;
///   * ter comido junto o CAMBALEIA, que e o unico efeito que o pesado tem contra quem e mais forte
///     que ele -- e ai o pesado fraco fica MUDO, que e um defeito pior e mais silencioso;
///   * ter mexido na FORCA sem querer, e o dono nao reclamou da intensidade: reclamou da
///     frequencia. Um voo mais curto seria outro jogo, e ninguem pediu.
///
/// Por isso as familias medem as TRES coisas juntas, com numero na frente: quantos por cento
/// arremessam, quantos por cento cambaleiam, e quantos PIXELS voa quem voou.
/// ======================================================================================================
///
/// ============================ E ELA MEDE PELO FUNIL DE PRODUCAO ============================
/// Nenhuma familia chama <see cref="Empurrao.DoSoco"/> direto. Todas passam pelo
/// <c>TentarEmpurrar</c> -- a MESMA funcao que o `Atacar` chama na linha 543 do
/// `GameServer.Combat.cs` -- e leem o resultado no CORPO (`TiquesDeVoo`, `Combate.Stun`), que e o
/// que o jogador sente. A familia 7 fecha o fio inteiro pelo `Atacar` de verdade: sem ela, alguem
/// poderia desligar a chamada da linha 543 e as seis primeiras familias continuariam verdes.
/// ==========================================================================================
///
/// ============================ AS DUAS CAMADAS DELA, E POR QUE PRECISA DAS DUAS ============================
/// **1 a 7 SAO DE MESA.** Repetem um golpe quatro mil vezes e leem o corpo. Respondem "com que
/// CHANCE o pesado arremessa" -- e essa nao e a pergunta do dono. Uma chance de 33% num golpe que
/// sai tres vezes por segundo continuaria sendo pingue-pongue.
///
/// **8 E 9 SAO AO VIVO.** Um NPC de cerebro de producao contra um corpo dirigido como jogador,
/// quatro brigas de 45 s no relogio de parede, com e sem o defeito injetado
/// (<c>_kbPesadoSemSorteio</c>), na mesma praca. Respondem as duas frases do dono na unidade delas:
/// **quantos arremessos por minuto** (a 8) e **quantos socos caem no vazio** (a 9). Sao tres minutos
/// de relogio, e sao a unica parte da bancada que ve a briga em vez do golpe.
///
/// As duas camadas se seguram: sem as de mesa, uma mudanca de CADENCIA (socar mais devagar) passaria
/// por "arremessa menos"; sem as ao vivo, um sorteio certo num caminho que a briga nao usa passaria
/// por consertado.
/// ==========================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// QUANTOS GOLPES POR AMOSTRA. Com 4.000 e uma chance de 33%, o desvio padrao da proporcao e
	/// 0,7 ponto -- ou seja as faixas de aprovacao abaixo (que tem 8 pontos de folga) nunca piscam
	/// por sorte. Uma bancada de frequencia que reprova as vezes e uma bancada que ninguem le.
	/// </summary>
	private const int GolpesPorAmostra = 4_000;

	private int _kbOk, _kbFalhou;

	private void AfirmarKb(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _kbOk++; GD.Print($"[kb]   OK    {nome}   {detalhe}"); }
		else { _kbFalhou++; GD.PrintErr($"[kb]   FALHA {nome}   {detalhe}"); }
	}

	/// <summary>
	/// O que uma amostra de golpes produziu NO CORPO de quem apanhou -- e o que ela TERIA produzido
	/// antes do conserto, sobre EXATAMENTE os mesmos golpes.
	///
	/// ============================ POR QUE O "ANTES" ANDA JUNTO, E NAO NUMA CONSTANTE ============================
	/// A primeira versao desta bancada cravou os numeros medidos na Fase 0 (100% de arremesso, 92,8%
	/// de cambaleia a 0,5x, 517-576 px de voo) como faixas de aprovacao. **Tres linhas reprovaram, e
	/// nenhuma delas era regressao**: a Fase 0 mediu Saiyajins nascidos pelo `races.json` (com
	/// genetica sorteada) e a bancada forja Humanos com `Fighter` cru. Sao populacoes diferentes,
	/// entao `Ephysoff`, `Ewillpower` e o dano tipico sao outros -- e o "antes" daquela medicao nao e
	/// o "antes" desta. Uma faixa importada de outra populacao mede a populacao, nao a mudanca.
	///
	/// O "antes" desta e calculavel EXATAMENTE, e sem sorteio nenhum: o ramo que existia era
	/// `Avaliar(ForcaDoPesado(dmg, a, check), limiar, false)` -- as tres funcoes continuam publicas
	/// e intocadas no Core. Rodando as duas sobre o MESMO golpe, a diferenca que sobra e a mudanca, e
	/// so ela. E de quebra e daqui que sai a resposta pedida: **quanto caiu**.
	/// ==========================================================================================================
	/// </summary>
	private readonly record struct AmostraDoArremesso(
		int Tentou, int Encostou, int Arremesso, int Cambaleia, int Nada, double PxMedio, double Check,
		int AntesArremesso, int AntesCambaleia, int AntesNada, double AntesPxMedio, int TiquesDiferentes)
	{
		public double PcArremesso => Encostou == 0 ? 0 : 100.0 * Arremesso / Encostou;
		public double PcCambaleia => Encostou == 0 ? 0 : 100.0 * Cambaleia / Encostou;
		public double PcAntesArremesso => Encostou == 0 ? 0 : 100.0 * AntesArremesso / Encostou;
		public double PcAntesCambaleia => Encostou == 0 ? 0 : 100.0 * AntesCambaleia / Encostou;

		/// <summary>Quanto a frequencia de arremesso CAIU, em pontos percentuais.</summary>
		public double QuantoCaiu => PcAntesArremesso - PcArremesso;
	}

	/// <summary>
	/// N GOLPES DE UM CORPO NO OUTRO, contados pelo que aconteceu COM O CORPO.
	///
	/// ============================ OS TRES RESETS QUE A MEDICAO EXIGE ============================
	/// 1. **`Reviver(1)` NOS DOIS, a cada golpe.** O nocaute derruba o `expressedBP` a um decimo, e o
	///    `check` e uma RAZAO entre poderes -- sem isto o alvo nocauteia na centesima amostra e as
	///    3.900 seguintes medem um `check` inflado, com tudo arremessando. Foi exatamente esse o
	///    artefato que quase enterrou a medicao da Fase 0 (a primeira rodada deu 99% no LEVE).
	/// 2. **`TiquesDeVoo = 0`.** O `TentarEmpurrar` recusa quem ja esta no ar na PRIMEIRA linha; sem
	///    zerar, o primeiro arremesso calaria todos os outros e a bancada mediria 1 em 4.000.
	/// 3. **`Stun = 0`.** O cambaleio e lido como "o atordoamento subiu neste golpe". Um stun
	///    herdado do golpe anterior contaria cambaleia onde nao houve nenhuma.
	/// ==========================================================================================
	///
	/// O DANO E DO RESOLVEDOR DE VERDADE (`MeleeResolver.Resolver`), e nao um numero escolhido: a
	/// forca do pesado e `(dmg + Ephysoff*2 + 1) * check`, entao um dano inventado moveria o limiar
	/// junto e a medicao responderia sobre um golpe que o jogo nao da.
	/// </summary>
	private AmostraDoArremesso MedirGolpes(ServerPlayer a, ServerPlayer d, Protocol.Golpe golpe,
								bool garantido = false, int golpes = GolpesPorAmostra)
	{
		double pxPorTique = Empurrao.TilesPorTique * ZoneCollision.TileSize;
		int encostou = 0, arremesso = 0, cambaleia = 0, nada = 0;
		int antesArr = 0, antesCamb = 0, antesNada = 0, tiquesDiferentes = 0;
		double px = 0, antesPx = 0;
		bool pesado = golpe == Protocol.Golpe.Pesado;

		for (int i = 0; i < golpes; i++)
		{
			a.Combate.Reviver(1);
			d.Combate.Reviver(1);
			a.Ficha.Ki = a.Ficha.MaxKi;
			d.TiquesDeVoo = 0;
			d.TiquesIniciaisDoVoo = 0;

			GolpeResultado r = MeleeResolver.Resolver(a.Combate, d.Combate, 0, _rng,
													  Protocol.PesoDoGolpe(golpe));
			if (!r.Encostou) continue;
			encostou++;

			// ============================ O STUN ZERA **DEPOIS** DO RESOLVEDOR ============================
			// **A PRIMEIRA VERSAO ZERAVA ANTES, E ISSO FEZ A BANCADA ACUSAR UM DEFEITO QUE NAO EXISTE.**
			// O `MeleeResolver` tambem atordoa (golpe critico, membro quebrado), e o contador lia
			// "cambaleia" em qualquer `Stun > 0` que sobrasse no fim -- ou seja creditava ao arremesso
			// um atordoamento que era do GOLPE. Deu 199 falsos cambaleios em 4.000 e uma FALHA vermelha.
			//
			// ISSO ERA INVISIVEL ANTES DO CONSERTO, e por uma razao que e o proprio bug: como todo
			// pesado arremessava, o contador entrava no ramo do arremesso e nunca chegava a olhar o
			// `Stun`. So quando dois tercos dos pesados pararam de arremessar e que a leitura errada
			// apareceu. Uma bancada escrita ANTES do conserto teria nascido com esse cego dentro.
			//
			// Zerando aqui, `Stun > 0` la embaixo so pode ter vindo do <c>TentarEmpurrar</c>.
			// ============================================================================================
			d.Combate.Stun = 0;

			// ---- O "ANTES", sobre ESTE golpe: o `else` que existia, verbatim ----
			// `attack cmn.dm:115-116` -> `spawn Impact(M, (dmg + Ephysoff*2 + 1) * check)`, sem
			// `prob()` nenhum. Fica ANTES da chamada de producao porque o `TentarEmpurrar` escreve
			// no corpo, e o `Limiar` le o `hpratio` dele.
			(EfeitoDeImpacto efAntes, int tqAntes) = pesado
				? Empurrao.Avaliar(Empurrao.ForcaDoPesado(r.Dano, a.Ficha, Empurrao.Check(a.Ficha, d.Ficha)),
								   Empurrao.Limiar(d.Ficha, a.Ficha.expressedBP), false)
				: (EfeitoDeImpacto.Nada, 0);   // o leve nao mudou; o "antes" dele e ele mesmo

			if (efAntes == EfeitoDeImpacto.Arremesso) { antesArr++; antesPx += tqAntes * pxPorTique; }
			else if (efAntes == EfeitoDeImpacto.Cambaleia) antesCamb++;
			else antesNada++;

			// ---- O "DEPOIS": o funil de producao ----
			TentarEmpurrar(a, d, r.Dano, golpe, garantido);

			if (d.TiquesDeVoo > 0)
			{
				arremesso++;
				px += d.TiquesIniciaisDoVoo * pxPorTique;

				// A INVARIANTE DA FORCA: o sorteio decide SE voa, nunca QUANTO voa. Se um dia alguem
				// mexer na duracao junto com a frequencia, e aqui que aparece -- e nao numa media,
				// que dilui um erro de um tique em quatro mil amostras.
				if (pesado && !garantido && d.TiquesIniciaisDoVoo != tqAntes) tiquesDiferentes++;
			}
			else if (d.Combate.Stun > 0) cambaleia++;
			else nada++;
		}

		return new AmostraDoArremesso(golpes, encostou, arremesso, cambaleia, nada,
						   arremesso == 0 ? 0 : px / arremesso, Empurrao.Check(a.Ficha, d.Ficha),
						   antesArr, antesCamb, antesNada,
						   antesArr == 0 ? 0 : antesPx / antesArr, tiquesDiferentes);
	}

	private void RelatarKb(string rotulo, AmostraDoArremesso m)
	{
		GD.Print($"[kb]        {rotulo,-30} {m.Encostou,5}/{m.Tentou} encostaram | "
				 + $"arremesso {m.PcArremesso,5:0.0}% | cambaleia {m.PcCambaleia,5:0.0}% | "
				 + $"nada {(m.Encostou == 0 ? 0 : 100.0 * m.Nada / m.Encostou),5:0.0}% | "
				 + $"voo medio {m.PxMedio,3:0} px | check {m.Check:0.###}");
		if (m.AntesArremesso > 0 || m.AntesCambaleia > 0)
			GD.Print($"[kb]        {"  ANTES do conserto",-30} {"",5} {"",4}           "
					 + $"arremesso {m.PcAntesArremesso,5:0.0}% | cambaleia {m.PcAntesCambaleia,5:0.0}% | "
					 + $"nada {(m.Encostou == 0 ? 0 : 100.0 * m.AntesNada / m.Encostou),5:0.0}% | "
					 + $"voo medio {m.AntesPxMedio,3:0} px | CAIU {m.QuantoCaiu:0.0} pontos");
	}

	// =====================================================================
	// A BANCADA
	// =====================================================================
	public void RodarBancadaDoArremesso()
	{
		_kbOk = _kbFalhou = 0;
		_pjProximoCorredor = 8;
		GD.Print("[kb] ================ O SORTEIO DO ARREMESSO (pedido do dono) ================");
		GD.Print($"[kb] chance por peso do golpe = {Empurrao.ChancePorPesoDoGolpe} "
				 + $"| peso leve {Protocol.PesoDoGolpe(Protocol.Golpe.Leve)} "
				 + $"| peso pesado {Protocol.PesoDoGolpe(Protocol.Golpe.Pesado)}");

		try
		{
			OPesadoSorteia();
			OPesadoBateOLeve();
			AFaixaDoMeioNaoCresceu();
			OMuitoMaisForteContinuaJogandoLonge();
			AForcaEADistanciaNaoMudaram();
			OGarantidoContinuaGarantido();
			OFioAteOAtacar();
			ORastroSegueOsPixels();
			OArremessoNoAr();
			OMuroQueParaEOMuroQueCai();
			AObraQueACaixaToca();
			NoArAParedeDePredioPara();

			// ---- AS DUAS QUEIXAS DO DONO, CRONOMETRADAS NUMA BRIGA DE VERDADE ----
			// Um par de brigas de 60 s (com e sem o defeito injetado) alimenta as DUAS familias
			// seguintes: elas leem a MESMA cena por dois angulos, e rodar quatro brigas pra medir duas
			// coisas da mesma briga seria pagar dois minutos de relogio por nada.
			(RitmoDaBriga marcAntes, RitmoDaBriga marcDepois,
			 RitmoDaBriga soltoAntes, RitmoDaBriga soltoDepois) = AsQuatroBrigas();
			OPinguePongueParou(marcAntes, marcDepois);
			OsSocosPassamAAcertar(soltoAntes, soltoDepois, marcAntes, marcDepois);
		}
		finally { _kbPesadoSemSorteio = false; LimparTudoDaBancada(); }

		GD.Print($"[kb] ================ {_kbOk} OK, {_kbFalhou} FALHA(S) ================");
	}

	/// <summary>Dois corpos lado a lado num corredor livre da Terra, com a razao de BP pedida.</summary>
	private (ServerPlayer A, ServerPlayer D) Dupla(string marca, double razao)
	{
		Vec2 onde = CorredorLivre(24);
		ServerPlayer a = Forjar($"kb{marca}Bate", onde, 5_551 * razao);
		ServerPlayer d = Forjar($"kb{marca}Leva", onde + new Vec2(28, 0), 5_551);
		a.Facing = Facing.East;
		d.Facing = Facing.West;
		return (a, d);
	}

	// =====================================================================
	// 1) O PESADO SORTEIA -- e a faixa e a que o DM escreve
	// =====================================================================
	/// <summary>
	/// A LINHA CENTRAL, e ela e um PAR: o mesmo golpe medido pelo funil de hoje e pela formula de
	/// ontem. O "antes" tem que dar 100% (era a queixa) e o "depois" tem que cair pra faixa do
	/// `prob(check*10*peso)` -- que em BP parelho, com `check` ~1,0, e um terco.
	///
	/// A faixa de aprovacao do "depois" vai de 20% a 60%: larga pra o `check` variar com a ficha, e
	/// apertada pra o 100% de antes -- ou um sorteio esquecido num caminho novo -- cair vermelho.
	/// </summary>
	private void OPesadoSorteia()
	{
		GD.Print("[kb] --- 1) O PESADO SORTEIA (BP parelho) ---");
		(ServerPlayer a, ServerPlayer d) = Dupla("Par", 1.0);

		AmostraDoArremesso pes = MedirGolpes(a, d, Protocol.Golpe.Pesado);
		RelatarKb("pesado, 1,0x", pes);

		// A METADE QUE PROVA QUE A BANCADA MEDE ALGUMA COISA: se o "antes" nao der 100%, ou a
		// populacao forjada nao reproduz a queixa do dono, ou a formula de ontem foi mexida -- e nas
		// duas hipoteses as linhas de baixo nao valem nada.
		AfirmarKb("o ANTES reproduz a queixa: sem sorteio, todo pesado que encosta arremessa",
				  pes.PcAntesArremesso > 99, $"{pes.PcAntesArremesso:0.0}%");
		AfirmarKb("o pesado NAO arremessa mais 100% das vezes -- era essa a queixa",
				  pes.PcArremesso < 90, $"{pes.PcArremesso:0.0}%");
		AfirmarKb("...e a frequencia caiu pra faixa do `prob(check*10*peso)` (20% a 60%)",
				  pes.PcArremesso is > 20 and < 60,
				  $"{pes.PcAntesArremesso:0.0}% -> {pes.PcArremesso:0.0}% (caiu {pes.QuantoCaiu:0.0} pontos)");

		// O CONTRA-EXEMPLO QUE IMPEDE O VERDE VAZIO: um sorteio de 0% tambem passaria na linha de
		// cima. Se o pesado parasse de arremessar, o corpo nunca mais sairia do lugar por soco.
		AfirmarKb("...e ele CONTINUA arremessando -- um sorteio de zero seria outro defeito",
				  pes.Arremesso > 0, $"{pes.Arremesso} arremessos em {pes.Encostou} golpes");
	}

	// =====================================================================
	// 2) O PESADO ARREMESSA MAIS QUE O LEVE -- o pedido, literal
	// =====================================================================
	/// <summary>
	/// *"eles deveriam ter uma CHANCE UM POUCO MAIOR de dar knock back"* -- maior que a do leve, e
	/// nao 100%. As duas metades da frase viram duas afirmacoes, porque uma so fica verde com o
	/// outro erro: "maior que o leve" e verdade em 100%, e "menor que 100%" e verdade em 1%.
	///
	/// O LEVE E O CONTROLE: ele nao foi tocado (o ramo dele saiu do `TentarEmpurrar` pro Core sem
	/// uma virgula de mudanca), entao um numero fora da faixa dele denuncia que a mudanca vazou.
	/// </summary>
	private void OPesadoBateOLeve()
	{
		GD.Print("[kb] --- 2) O PESADO ARREMESSA MAIS QUE O LEVE, E MENOS QUE SEMPRE ---");
		(ServerPlayer a, ServerPlayer d) = Dupla("Cmp", 1.0);

		AmostraDoArremesso pes = MedirGolpes(a, d, Protocol.Golpe.Pesado);
		AmostraDoArremesso lev = MedirGolpes(a, d, Protocol.Golpe.Leve);
		RelatarKb("pesado, 1,0x", pes);
		RelatarKb("leve, 1,0x", lev);

		AfirmarKb("o pesado arremessa MAIS que o leve (a chance escala com o peso do golpe)",
				  pes.PcArremesso > lev.PcArremesso,
				  $"pesado {pes.PcArremesso:0.0}% x leve {lev.PcArremesso:0.0}%");
		AfirmarKb("...mas so UM POUCO mais -- nao o dobro nem o triplo (razao entre 1,1x e 2,5x)",
				  lev.PcArremesso > 0 && pes.PcArremesso / lev.PcArremesso is > 1.1 and < 2.5,
				  $"{(lev.PcArremesso == 0 ? 0 : pes.PcArremesso / lev.PcArremesso):0.00}x");
		AfirmarKb("o LEVE nao foi tocado: continua na faixa medida antes do conserto (10% a 40%)",
				  lev.PcArremesso is > 10 and < 40, $"{lev.PcArremesso:0.0}%");
	}

	// =====================================================================
	// 3) A FAIXA DO MEIO NAO CRESCEU -- e a armadilha deste conserto
	// =====================================================================
	/// <summary>
	/// ============================ O CAMBALEIA E O CAMINHO MUDO ============================
	/// Ele nao manda pacote nenhum pro cliente: vira `Combate.Stun` e o jogador so descobre porque
	/// perdeu o proximo golpe. Havia duas maneiras faceis de este conserto engordar esse caminho:
	///
	///   * mandar o pesado que FALHA o sorteio cair no cambaleia -- 67% dos pesados iriam pra la;
	///   * gatilhar o sorteio ANTES do `Avaliar`, o que teria comido o cambaleia junto e deixado o
	///     pesado fraco (contra alguem mais forte) sem efeito NENHUM em dois de cada tres golpes.
	///
	/// O conserto nao faz nem uma nem outra: o sorteio veta so o `Arremesso`, depois do `Avaliar`.
	/// Esta familia e quem prova isso, e ela e a razao de a medicao ler `Stun` e nao so `TiquesDeVoo`.
	/// =====================================================================================
	///
	/// ============================ E A PROVA E "IGUAL AO ANTES", NAO UM NUMERO ============================
	/// A primeira versao desta familia exigia "cambaleia > 70%" contra o atacante fraco, importando o
	/// 92,8% que a Fase 0 mediu em Saiyajins nascidos do `races.json`. Nos Humanos forjados aqui o
	/// numero e outro (8,5%), e a linha reprovou sem que nada tivesse regredido -- ela media a
	/// POPULACAO, nao a mudanca. A afirmacao certa nao tem numero nenhum: **o cambaleia depois tem
	/// que ser IDENTICO ao cambaleia antes, golpe a golpe**, em qualquer populacao. Se um dia o
	/// sorteio migrar pra antes do `Avaliar`, esta linha fica vermelha na hora.
	/// ==================================================================================================
	/// </summary>
	private void AFaixaDoMeioNaoCresceu()
	{
		GD.Print("[kb] --- 3) O CAMBALEIA (a faixa do meio) CONTINUA COMO ESTAVA ---");
		(ServerPlayer a, ServerPlayer d) = Dupla("Fraco", 0.5);

		AmostraDoArremesso fraco = MedirGolpes(a, d, Protocol.Golpe.Pesado);
		RelatarKb("pesado, 0,5x (atacante fraco)", fraco);

		AfirmarKb("quem e mais fraco continua sem arremessar ninguem -- a FORCA ja barrava, e barra",
				  fraco.PcArremesso < 5 && fraco.PcAntesArremesso < 5,
				  $"antes {fraco.PcAntesArremesso:0.0}% / depois {fraco.PcArremesso:0.0}%");
		AfirmarKb("...e o cambaleio dele e IDENTICO ao de antes -- o sorteio nao tocou a faixa do meio",
				  fraco.Cambaleia == fraco.AntesCambaleia,
				  $"antes {fraco.AntesCambaleia} / depois {fraco.Cambaleia} golpes");

		// A OUTRA METADE, e a mais importante: em BP parelho o pesado que FALHA o sorteio nao pode
		// ter CAIDO no cambaleia. Se tivesse, o caminho mudo engordaria com dois tercos dos pesados
		// -- e a bancada acharia isso pela mesma igualdade, sem numero magico nenhum.
		(ServerPlayer a2, ServerPlayer d2) = Dupla("Par2", 1.0);
		AmostraDoArremesso par = MedirGolpes(a2, d2, Protocol.Golpe.Pesado);
		RelatarKb("pesado, 1,0x", par);
		AfirmarKb("em BP parelho o pesado que falha o sorteio NAO cai no cambaleia -- ele nao faz nada",
				  par.Cambaleia == par.AntesCambaleia,
				  $"antes {par.AntesCambaleia} / depois {par.Cambaleia} golpes");
	}

	// =====================================================================
	// 4) QUEM E MUITO MAIS FORTE CONTINUA JOGANDO LONGE
	// =====================================================================
	/// <summary>
	/// O desenho do DM e que a diferenca de poder APARECE, e o `check` (que ja e a razao de BP
	/// modulada) leva isso pra dentro da chance: a 3,0x ele passa de 3, e `prob(check*30)` satura.
	/// Sem esta familia o conserto poderia ter achatado a diferenca de poder num numero fixo, que e
	/// justamente o que este jogo nao quer.
	/// </summary>
	private void OMuitoMaisForteContinuaJogandoLonge()
	{
		GD.Print("[kb] --- 4) O MUITO MAIS FORTE CONTINUA ARREMESSANDO QUASE SEMPRE ---");
		(ServerPlayer a, ServerPlayer d) = Dupla("Forte", 3.0);

		AmostraDoArremesso pes = MedirGolpes(a, d, Protocol.Golpe.Pesado);
		RelatarKb("pesado, 3,0x", pes);

		AfirmarKb("a 3,0x de BP o pesado ainda arremessa quase todo golpe (o `prob` satura)",
				  pes.PcArremesso > 85, $"{pes.PcArremesso:0.0}% (check {pes.Check:0.##})");
	}

	// =====================================================================
	// 5) A FORCA E A DISTANCIA NAO FORAM TOCADAS
	// =====================================================================
	/// <summary>
	/// O dono reclamou da FREQUENCIA, nao da intensidade -- e mexer na distancia sem ele pedir seria
	/// mudar o combate por conta propria.
	///
	/// ============================ A AFIRMACAO E GOLPE A GOLPE, E NAO UMA MEDIA ============================
	/// Uma media de pixels nao serve pra isto por duas razoes. A primeira: ela muda de populacao pra
	/// populacao (nos Saiyajins da Fase 0 deu 517-576 px; nos Humanos forjados aqui da ~459), entao
	/// qualquer faixa cravada mede a ficha e nao a mudanca -- foi assim que a primeira versao desta
	/// linha reprovou sem regressao nenhuma. A segunda, pior: uma media DILUI. Um erro de um tique em
	/// vinte golpes some no arredondamento de quatro mil.
	///
	/// A pergunta certa e uma igualdade exata, contada por golpe: **entre os que voaram, quantos
	/// voaram um numero de tiques DIFERENTE do que a formula de ontem daria?** A resposta tem que ser
	/// zero, e ela e zero em qualquer ficha, porque o sorteio decide SE voa e nunca QUANTO voa.
	/// ===================================================================================================
	///
	/// A conta da duracao e do DU e satura: `round(min(max(dmg,5)/1.5, 9))` da 9 com qualquer forca a
	/// partir de 13,5, e a forca do pesado passa MUITO disso. Ou seja bater mais forte nao joga mais
	/// longe -- joga mais VEZES, que e exatamente o que o sorteio corrige.
	/// </summary>
	private void AForcaEADistanciaNaoMudaram()
	{
		GD.Print("[kb] --- 5) A FORCA E A DISTANCIA DO ARREMESSO CONTINUAM AS MESMAS ---");
		(ServerPlayer a, ServerPlayer d) = Dupla("Dist", 1.0);

		AmostraDoArremesso pes = MedirGolpes(a, d, Protocol.Golpe.Pesado);
		RelatarKb("pesado, 1,0x", pes);

		AfirmarKb("nenhum arremesso mudou de DURACAO: o sorteio decide SE voa, nunca QUANTO voa",
				  pes.TiquesDiferentes == 0,
				  $"{pes.TiquesDiferentes} de {pes.Arremesso} arremessos com tiques diferentes de antes");
		// E O SORTEIO NAO ESCOLHE OS LONGOS. A chance depende so do `check`, que nao muda dentro da
		// amostra -- entao o terco que passa tem que ter a mesma distribuicao de duracao do todo. Se
		// um dia alguem amarrar a chance a forca do golpe, os arremessos que sobram ficam mais
		// longos que a media antiga, e e esta linha que denuncia. Tolerancia de 20 px = menos de um
		// terco de tique, folga de amostragem e nao de regra.
		AfirmarKb("...e o sorteio nao escolhe os arremessos LONGOS: o voo medio segue o de antes",
				  Math.Abs(pes.PxMedio - pes.AntesPxMedio) < 20,
				  $"{pes.AntesPxMedio:0.0} px antes / {pes.PxMedio:0.0} px depois");
	}

	// =====================================================================
	// 6) O `garantido` CONTINUA GARANTIDO
	// =====================================================================
	/// <summary>
	/// O golpe de saida do Zanzo Clash sobrevivia POR ACIDENTE: como o pesado nunca sorteava,
	/// "garantido" era o comportamento de fabrica e o comentario de la descrevia uma regra que
	/// ninguem tinha escrito. Agora a regra existe -- e esta familia e quem a segura, porque a
	/// cena do embate e a UNICA do corpo a corpo em que o arremesso E o desfecho.
	/// </summary>
	private void OGarantidoContinuaGarantido()
	{
		GD.Print("[kb] --- 6) O ARREMESSO FORCADO (golpe de saida do Zanzo Clash) ---");
		(ServerPlayer a, ServerPlayer d) = Dupla("Gar", 1.0);

		AmostraDoArremesso g = MedirGolpes(a, d, Protocol.Golpe.Pesado, garantido: true, golpes: 500);
		RelatarKb("pesado GARANTIDO, 1,0x", g);

		AfirmarKb("com `garantido`, TODO golpe que encosta arremessa -- o sorteio nao se aplica",
				  g.Encostou > 0 && g.Arremesso == g.Encostou,
				  $"{g.Arremesso}/{g.Encostou}");
	}

	// =====================================================================
	// 7) O FIO ATE O `Atacar` -- sem esta, as seis de cima ficam verdes sozinhas
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ESTA FAMILIA EXISTE ============================
	/// As seis de cima chamam o `TentarEmpurrar`. Se alguem apagasse a linha 543 do
	/// `GameServer.Combat.cs` -- a unica que liga o soco ao arremesso --, todas continuariam verdes
	/// e o jogo nao arremessaria mais ninguem. Aqui o soco sai pelo `Atacar`, que e a mesma porta do
	/// `case Protocol.C2S.Golpe`, e a conta e feita no corpo do outro.
	///
	/// Ela mede uma frequencia MENOR que a familia 1 e isso e esperado, nao ruido: o `Atacar` inteiro
	/// tem esquiva, aparo e contra-ataque, e so o que ENCOSTA chega ao sorteio. O que importa aqui e
	/// o par -- houve arremesso, e nao houve em todo golpe.
	/// ====================================================================================
	/// </summary>
	private void OFioAteOAtacar()
	{
		GD.Print("[kb] --- 7) PELO `Atacar` DE PRODUCAO (o fio inteiro) ---");
		Vec2 onde = CorredorLivre(24);
		ServerPlayer a = Forjar("kbFioBate", onde, 5_551);
		ServerPlayer d = Forjar("kbFioLeva", onde + new Vec2(28, 0), 5_551);
		a.Facing = Facing.East;
		d.Facing = Facing.West;

		int socos = 0, arremessos = 0;
		for (int i = 0; i < 600; i++)
		{
			// A CENA E RECOLADA A CADA SOCO -- a mesma razao da `--tresteste`: o golpe que acerta
			// arremessa, e o corpo a dez tiles faria os 599 seguintes medirem a distancia, nao o
			// sorteio.
			a.Pos = onde;
			d.Pos = onde + new Vec2(28, 0);
			a.Facing = Facing.East;
			d.Facing = Facing.West;
			a.Combate.Reviver(1);
			d.Combate.Reviver(1);
			d.TiquesDeVoo = 0;
			a.Combate.Recarga = 0;
			a.Combate.Stun = 0;
			a.AtaqueAte = 0;
			a.DashLivreEm = 0;
			a.Ficha.Ki = a.Ficha.MaxKi;
			a.AlvoId = d.Id;

			socos++;
			Atacar(a, Protocol.Golpe.Pesado);
			if (d.TiquesDeVoo > 0) arremessos++;
		}

		double pc = 100.0 * arremessos / socos;
		GD.Print($"[kb]        pelo `Atacar`: {arremessos}/{socos} socos pesados arremessaram ({pc:0.0}%)");
		AfirmarKb("o soco pesado do `Atacar` AINDA arremessa -- o fio da linha 543 esta ligado",
				  arremessos > 0, $"{arremessos} arremessos");
		AfirmarKb("...e nao arremessa em todo golpe: o sorteio vale pelo caminho de producao tambem",
				  pc < 90, $"{pc:0.0}%");
	}

	// =====================================================================
	// A BRIGA DE DOIS NPCS -- a cena da queixa, cronometrada
	// =====================================================================
	/// <summary>
	/// QUANTO TEMPO CADA BRIGA DURA, e quantas brigas sao.
	///
	/// Sao QUATRO: o par antes/depois vezes o par "com o alvo marcado" / "sem marcar". As duas versoes
	/// do jogo precisam ser vistas nas DUAS maneiras de jogar, e a razao esta na familia 9 -- com alvo
	/// marcado o arranque busca a 480 px e o soco nunca cai no vazio (medido: 0,0% nas duas versoes),
	/// entao medir so esse caso daria um empate que nao diz nada sobre a queixa.
	///
	/// Quarenta e cinco segundos cada, e o RITMO continua sendo relatado por MINUTO -- a unidade da
	/// queixa. Sao tres minutos de relogio de parede, e e o que a bancada inteira custa.
	/// </summary>
	private const double SegundosDeBriga = 45;

	/// <summary>
	/// O QUE SOBROU DE UM MINUTO DE BRIGA. Tudo contado por campo que o codigo de PRODUCAO escreve --
	/// nenhum contador da bancada mora dentro do `Atacar`.
	/// </summary>
	private readonly record struct RitmoDaBriga(
		double Segundos, int Tiques, int Socos, int Vazios, int Arremessos, int TiquesComAlguemNoAr,
		double SomaDaDistancia, double ChanceDePesado, int Recentragens)
	{
		/// <summary>
		/// QUANTO TEMPO DE JOGO A LINHA MEDIU -- e nao quanto tempo de relogio ela levou.
		///
		/// Os dois nao sao iguais e a diferenca importa: o tique de mundo desta bancada (grades, combate,
		/// IA, arremesso) custa mais que os 33 ms do alvo, entao 45 s de parede rendem ~32 s de jogo. A
		/// cadencia do soco, a duracao do voo e a recarga do arremesso contam todas em TIQUES, e e nessa
		/// moeda que o dono sente o ritmo -- entao e nela que o ritmo e relatado.
		///
		/// (O relogio de PAREDE continua sendo o que faz a cena andar, e por uma razao que nao muda: a
		/// recarga do arranque e carimbada com <see cref="NowMs"/>. Ver <see cref="Brigar"/>.)
		/// </summary>
		public double SegundosDeJogo => Tiques * Protocol.TickSeconds;

		/// <summary>A queixa, na unidade dela: quantas vezes por minuto um corpo sai voando.</summary>
		public double ArremessosPorMinuto => SegundosDeJogo <= 0 ? 0 : Arremessos * 60.0 / SegundosDeJogo;

		/// <summary>O intervalo entre dois arremessos. E o numero que o dono SENTE.</summary>
		public double SegundosEntreArremessos =>
			Arremessos == 0 ? double.PositiveInfinity : SegundosDeJogo / Arremessos;

		/// <summary>
		/// Quantos por cento dos MEUS socos sairam sem ninguem na frente. Sao os do corpo dirigido como
		/// jogador -- os do NPC nao entram, e o motivo esta no cabecalho do <see cref="Brigar"/>:
		/// o cerebro de producao nao soca o ar, entao contar os dele so diluiria a queixa do dono.
		/// </summary>
		public double PcNoVazio => Socos == 0 ? 0 : 100.0 * Vazios / Socos;

		/// <summary>Quantos por cento do tempo a briga tem um corpo no ar em vez de dois trocando socos.</summary>
		public double PcComAlguemNoAr => Tiques == 0 ? 0 : 100.0 * TiquesComAlguemNoAr / Tiques;

		/// <summary>A distancia media entre os dois, tique a tique.</summary>
		public double DistanciaMedia => Tiques == 0 ? 0 : SomaDaDistancia / Tiques;
	}

	/// <summary>
	/// UM MINUTO DE BRIGA ENTRE DOIS NPCS, no relogio de parede e pelo mundo de producao inteiro.
	///
	/// ============================ POR QUE ESTA FAMILIA NAO PODE SER UM LACO SINCRONO ============================
	/// As seis primeiras familias sao de mesa: elas repetem um GOLPE quatro mil vezes e leem o corpo.
	/// Isso responde "com que chance o pesado arremessa" e nao responde a queixa, que e sobre o
	/// **ritmo da luta** -- *"era UM JOGANDO O OUTRO PRA LONGE"*. Ritmo pede relogio.
	///
	/// E pede o relogio DE PAREDE, e nao um `for` de 1800 voltas com `dt` fixo, porque as duas pecas
	/// que fecham a distancia sao carimbadas com <see cref="NowMs"/> e nao abatidas por `dt`: a
	/// recarga do arranque (`RecargaDashMs`, 500 ms) e a janela do embate. Num laco sincrono o relogio
	/// de parede nao anda -- o primeiro arranque trava o dash pra sempre e a bancada mediria O LACO,
	/// dando o mesmo numero com o jogo consertado ou quebrado. E a licao que a `--tresteste` ja pagou e
	/// escreveu no <see cref="RodarEmTempoReal"/>, e esta familia so a reusa: mesmo tique de mundo
	/// (<see cref="UmTiqueDeMundoDaBancada"/>: grades, combate, IA, arremesso), mesma ordem.
	/// ==========================================================================================================
	///
	/// ============================ A CENA E "EU CONTRA UM NPC", E NAO DOIS NPCS ============================
	/// **A PRIMEIRA VERSAO POS CEREBRO NOS DOIS, E MEDIU 0,0% DE SOCO NO VAZIO NAS DUAS LINHAS.** Nao era
	/// erro de contagem: o cerebro de producao **nao soca o ar**. Ele so emite `Pesado` quando a receita
	/// dele diz que o alvo esta perto, e o `Aproximar` fecha o resto -- entao um NPC praticamente nunca
	/// erra por distancia. A metade da queixa que fala em socos que nao acertam **e do JOGADOR**, que
	/// aperta o botao no ritmo dele e nao no ritmo da geometria.
	///
	/// Entao a cena e a do relato: **um NPC de cerebro de producao contra um corpo dirigido como
	/// jogador** -- marca o alvo, corre atras e segura o soco forte. Os dois lados passam pelo mesmo
	/// funil (`AplicarComando`, que e por onde a IA tambem manda o passo e o soco), e nenhum deles tem
	/// atalho: quem recusa o golpe em recarga, atordoado ou no ar e o `PodeAtacar()` de producao, e quem
	/// fecha a distancia e o `Aproximar` de producao.
	///
	/// **O LIVRO DE SKILLS NASCE VAZIO, E ISSO E DELIBERADO.** O molde da `Ki_Wave`, e com ela no livro
	/// o cerebro passa a escolher o plano `Atirar` -- e o tique desta bancada nao move projetil
	/// (`UmTiqueDeMundoDaBancada` tem quatro linhas, e nenhuma e o `TickDosProjeteis`). Um raio que
	/// nasce e nunca anda nao e uma briga: e meia briga, e a metade que falta e a que esta sendo medida.
	/// Sem arsenal o cerebro nunca pede tiro, e sobra o punho -- que e a queixa.
	/// ==========================================================================================================
	/// </summary>
	/// <param name="semSorteio">
	/// Liga o <see cref="_kbPesadoSemSorteio"/> -- o `else` de `attack cmn.dm:115`, o defeito. E o
	/// "ANTES" desta medicao, e ele e o codigo de verdade rodando errado, nao uma formula copiada aqui.
	/// </param>
	private RitmoDaBriga Brigar(bool semSorteio, bool marcado, double segundos)
	{
		// AS DUAS BRIGAS NA MESMA PRACA, E ISSO E O CONSERTO DE UM ERRO MEDIDO -- ver <see cref="PracaDaBriga"/>.
		(Vec2 centro, float raio) = PracaDaBriga();
		ServerPlayer npc = Forjar("kbNpc", centro - new Vec2(2 * ZoneCollision.TileSize, 0), 5_551);
		ServerPlayer eu = Forjar("kbJogador", centro + new Vec2(2 * ZoneCollision.TileSize, 0), 5_551);
		npc.Facing = Facing.East;
		eu.Facing = Facing.West;

		MoldeDeNpc molde = _moldes?.Get("rival_do_mundo") ?? new MoldeDeNpc();
		npc.Cerebro = Temperamento.Montar(molde, 0, 4242);

		// NINGUEM MORRE NESTA CENA. Um minuto que acaba aos 12 s mede 12 s -- e pior, o morto SAI DO
		// MUNDO (`TickCombate`) e o outro fica sem presa, entao a briga acaba pra valer.
		npc.Combate.Letal = false;
		eu.Combate.Letal = false;

		ServerPlayer[] dois = [npc, eu];
		int socos = 0, vazios = 0, comAlguemNoAr = 0, recentragens = 0;
		double somaDist = 0;
		long arremessosNoComeco = _arremessosFeitos;

		_kbPesadoSemSorteio = semSorteio;
		int tiques;
		try
		{
			tiques = RodarEmTempoReal(segundos,
				antesDoTique: () =>
				{
					TrazerADuplaProCentro(npc, eu, centro, raio, ref recentragens);

					foreach (ServerPlayer p in dois)
					{

						// ============================ OS DOIS FICAM INTEIROS, E POR TRES RAZOES ============================
						// 1. **A BRIGA TEM QUE DURAR O MINUTO.** Ver `Letal = false` acima.
						// 2. **O LIMIAR DO ARREMESSO CAI COM A VIDA** (`Empurrao.Limiar` le o `hpratio`):
						//    sem restaurar, os ultimos 40 s mediriam dois corpos moribundos, que voam com
						//    qualquer coisa -- e as duas linhas envelheceriam em ritmos diferentes,
						//    porque quem apanha mais e quem e arremessado mais.
						// 3. **O TANQUE E O FOLEGO PAGAM O ARRANQUE** (`Aproximar` cobra Ki; `PodeCorrer`
						//    cobra por segundo de corrida). Com eles vazios a perseguicao para, e a
						//    familia 9 mediria o combustivel em vez da distancia.
						// E o mesmo trio de resets das familias de mesa, com o mesmo argumento.
						// ==============================================================================================
						p.Combate.Corpo.Restaurar();
						p.Combate.SincronizarVida();
						if (p.Ficha.KO) p.Combate.Levantar();
						p.Ficha.dead = false;
						p.Ficha.Ki = p.Ficha.MaxKi;
						p.Ficha.stamina = p.Ficha.maxstamina;
					}
					somaDist += (npc.Pos - eu.Pos).Length;
				},
				depoisDoTique: () =>
				{
					// ============================ O JOGADOR JOGA AQUI, DEPOIS DE TUDO ============================
					// A ordem do tique de mundo e grades -> combate -> IA -> arremesso. O corpo dirigido
					// como jogador entra DEPOIS dos quatro, e nao antes, porque e isso o que uma pessoa
					// faz: ela ve o quadro pronto -- o inimigo ja andou, o proprio corpo ja voou o que
					// tinha de voar -- e so entao aperta a tecla.
					//
					// **E ELE NAO TEM ATALHO NENHUM.** `AplicarComando` e o mesmo funil por onde a IA
					// manda passo e soco; dentro dele, `Atacar` cobra `PodeAtacar()` (recarga,
					// atordoamento, estar no ar) e `Aproximar` cobra Ki, recarga de arranque e parede.
					// Segurar a tecla de soco forte todo tique nao produz um soco por tique: produz um
					// soco por CADENCIA, que e a mesma que o jogador tem.
					// ==========================================================================================
					Vec2 rumo = npc.Pos - eu.Pos;

					// OS DOIS CARIMBOS SAO LIDOS COLADOS NA CHAMADA -- nada corre entre eles.
					//
					// `AtaqueAte` e escrito pelo `Atacar` na linha 422, ANTES de ele procurar alvo nenhum:
					// mudou, houve um soco -- aparado, esquivado ou no ar, tanto faz, o que se conta e o
					// GESTO. (E o mesmo carimbo que a `--iateste` ja usa pra contar socos.)
					//
					// `UltimoSocoMs` e escrito na linha 547, que so e alcancada DEPOIS do
					// `if (alvo == null) { ...; return; }`. Entao **soco que nao mexeu o `UltimoSocoMs` e
					// soco no vazio**, por construcao do proprio caminho.
					//
					// ---- POR QUE NAO PERGUNTAR `AlvoNaFrente(eu)` DEPOIS DO TIQUE ----
					// Porque a resposta ja teria mudado, e mudado A FAVOR do resultado que eu quero: o
					// `TickDoEmpurrao` move ~21 px quem acabou de ser arremessado, entao um soco que
					// ACERTOU e jogou o outro longe seria lido como "vazio". Esse erro cairia
					// desproporcionalmente na linha ANTES -- a que tem mais arremessos --, e a bancada
					// estaria inventando parte da melhora que ela existe pra medir.
					long ataqueAntes = eu.AtaqueAte, acertoAntes = eu.UltimoSocoMs;
					AplicarComando(eu, new Comando
					{
						Rumo = rumo,
						Olhar = rumo,
						Correndo = true,
						// MARCAR MUDA O ALCANCE DO ARRANQUE, e e por isso que esta cena e medida das duas
						// maneiras: com o alvo marcado o `Aproximar` busca a 480 px (`AlcanceDoDashMarcado`,
						// os 15 tiles do DM) e sem marcar ele busca a 160. O arremesso joga o corpo ate
						// 576 px -- ou seja, a marca e o que decide se o arranque alcanca de volta o que o
						// arremesso afastou. Ver a familia 9.
						Marcar = marcado ? npc.Id : 0,
						Pesado = true,     // a tecla do relato: SHIFT+ESPACO, o soco do dash
					}, Protocol.TickSeconds);

					if (eu.AtaqueAte != ataqueAntes)
					{
						socos++;
						if (eu.UltimoSocoMs == acertoAntes) vazios++;
					}

					// E O TEMPO EM QUE A BRIGA NAO E UMA BRIGA: um corpo no ar (ou voltando) e um corpo
					// que nao esta trocando socos com ninguem. E a queixa desenhada, e nao deduzida.
					if (npc.TiquesDeVoo > 0 || eu.TiquesDeVoo > 0) comAlguemNoAr++;
				});
		}
		finally
		{
			_kbPesadoSemSorteio = false;
			LimparTudoDaBancada();
		}

		// O ARREMESSO E CONTADO NA PORTA DELE (<see cref="_arremessosFeitos"/>) e nao no corpo: o voo
		// pode comecar e acabar dentro do mesmo tique de mundo, e ai nao ha o que olhar.
		return new RitmoDaBriga(segundos, tiques, socos, vazios,
								(int)(_arremessosFeitos - arremessosNoComeco), comAlguemNoAr, somaDist,
								npc.Cerebro?.ChanceDePesado ?? 0, recentragens);
	}

	/// <summary>
	/// A PRACA ONDE AS DUAS BRIGAS ACONTECEM -- **a mesma nas duas**, e achada uma vez so.
	///
	/// ============================ A PRIMEIRA VERSAO MEDIU A GEOGRAFIA DA TERRA ============================
	/// Ela usava o <see cref="CorredorLivre"/> (uma FILEIRA livre de N tiles) e um corredor DIFERENTE
	/// pra cada briga, com um argumento que parecia bom: o soco pesado racha o chao e o corpo
	/// arremessado derruba parede, entao a segunda briga herdaria o estrago da primeira. O resultado
	/// medido foi pior que o problema que ele evitava:
	///
	///   * **os dois corredores nao sao o mesmo lugar.** A briga ANTES deu 0% de socos no vazio e a
	///     DEPOIS deu 83,9% -- nao porque o sorteio piorou a mira, mas porque os corpos da segunda
	///     caíram num pedaco de mapa com parede no meio. A bancada estava comparando dois cenarios;
	///   * **uma fileira livre nao e uma arena.** O arremesso sai na direcao em que o atacante esta
	///     olhando, e dois corpos que se circulam olham pros quatro lados. Num corredor de UM tile de
	///     altura, o arremesso pro norte ou pro sul bate na parede na primeira amostra do caminho, e o
	///     voo ACABA no mesmo tique em que comecou. Com um teto e um chao a um tile, o arremesso vira
	///     um empurraozinho -- e a familia mediria a espessura do corredor.
	///
	/// Agora e uma PRACA (um quadrado livre, com chao de verdade -- `ServeDeChao` recusa agua e
	/// buraco), a MESMA nas duas brigas, e o estrago herdado deixou de importar porque nao ha o que
	/// derrubar num quadrado ja aberto: `RacharChao` numa celula livre nao muda colisao nenhuma.
	/// ====================================================================================================
	/// </summary>
	/// <returns>O centro da praca e o raio em pixels em que a briga fica confinada.</returns>
	private (Vec2 Centro, float Raio) PracaDaBriga()
	{
		if (_kbPraca is { } jaAchada) return jaAchada;

		ZoneCollision? mapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		int lado = 0;
		Vec2 centro = new(64, 64);

		// DO MAIOR PRO MENOR, e o maior que couber ganha: 33 tiles poem o centro a 16 tiles de
		// qualquer parede -- 512 px, que e a distancia de um arremesso inteiro (`TiquesMax` x
		// `TilesPorTique` x 32 = 640 px). Nao ha garantia de que a Terra tenha um quadrado desses, e por
		// isso o tamanho achado VAI PRO RELATO: uma praca pequena nao invalida a comparacao (as duas
		// brigas correm na mesma), mas encurta os voos das duas, e quem le tem que poder ver isso.
		foreach (int tentativa in new[] { 33, 27, 21, 15, 11 })
		{
			if (mapa == null) break;
			if (QuadradoLivre(mapa, tentativa) is not { } achado) continue;
			lado = tentativa;
			centro = achado;
			break;
		}

		float raio = lado > 0 ? (lado / 2 - 1) * ZoneCollision.TileSize : 4 * ZoneCollision.TileSize;
		GD.Print($"[kb]        praca da briga: {lado}x{lado} tiles livres em {centro} "
				 + $"(confinamento a {raio:0} px do centro)");
		AfirmarKb("(preparo) ha uma PRACA livre pras duas brigas -- e nao um corredor de um tile, "
				+ "onde todo arremesso morre na parede no tique em que nasce",
				  lado >= 15, $"{lado}x{lado} tiles");

		_kbPraca = (centro, raio);
		return _kbPraca.Value;
	}

	private (Vec2 Centro, float Raio)? _kbPraca;

	/// <summary>O centro do primeiro quadrado de <paramref name="lado"/> tiles todo pisavel, ou nada.</summary>
	private static Vec2? QuadradoLivre(ZoneCollision mapa, int lado)
	{
		for (int y = 4; y + lado < mapa.Height - 4; y += 2)
			for (int x = 4; x + lado < mapa.Width - 4; x += 2)
			{
				bool livre = true;
				for (int dy = 0; dy < lado && livre; dy++)
					for (int dx = 0; dx < lado && livre; dx++)
						livre &= mapa.ServeDeChao(x + dx, y + dy) && !mapa.BlockedCell(x + dx, y + dy);
				if (!livre) continue;

				float t = ZoneCollision.TileSize;
				return new Vec2((x + lado / 2f) * t, (y + lado / 2f) * t);
			}
		return null;
	}

	/// <summary>
	/// TRAZ A DUPLA DE VOLTA PRO CENTRO DA PRACA -- os dois juntos, na MESMA translacao.
	///
	/// ============================ ISTO E UM RECURSO DE BANCADA, E ELE NAO MEXE NO QUE SE MEDE ============================
	/// Uma briga de um minuto ANDA: quem apanha recua, quem bate persegue, e o arremesso empurra a
	/// dupla inteira meia dezena de tiles por vez. Sem isto os dois saem da praca pelo meio do teste e
	/// o resto da medicao volta a ser sobre a geografia da Terra -- exatamente o que a praca existe pra
	/// tirar da conta.
	///
	/// O DESLOCAMENTO E RIGIDO: os dois andam o MESMO vetor, entao **a distancia entre eles nao muda**
	/// -- e ela e o numero da familia 9. Direcao do olhar, recarga, marcacao, vida, nada mais e tocado.
	///
	/// E ele so acontece com os DOIS NO CHAO: mexer em `Pos` no meio de um voo seria escrever por cima
	/// do `TickDoEmpurrao`, encurtando um arremesso -- ou seja, a bancada mexendo justamente na
	/// grandeza que a familia 5 promete que ninguem mexeu. Enquanto alguem voa, a dupla fica onde esta.
	/// ==============================================================================================================
	/// </summary>
	private void TrazerADuplaProCentro(ServerPlayer a, ServerPlayer b, Vec2 centro, float raio,
									   ref int quantas)
	{
		if (a.TiquesDeVoo > 0 || b.TiquesDeVoo > 0) return;

		Vec2 desvio = (a.Pos + b.Pos) * 0.5f - centro;
		if (desvio.Length <= raio) return;

		// SO SE OS DOIS CAIREM EM CHAO BOM. Um par muito aberto pode nao caber na praca; nesse caso
		// nao ha translacao que sirva, e arrastar assim mesmo poria alguem dentro de uma parede.
		Vec2 novaA = a.Pos - desvio, novaB = b.Pos - desvio;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(a.Zone);
		if (mapa != null && (MoveRules.Occupied(mapa, novaA, ModoDeTravessiaDe(a))
							 || MoveRules.Occupied(mapa, novaB, ModoDeTravessiaDe(b)))) return;

		a.Pos = novaA;
		b.Pos = novaB;
		quantas++;
	}

	/// <summary>
	/// AS QUATRO BRIGAS: o par antes/depois, nas duas maneiras de jogar. Rodam todas na MESMA praca
	/// (<see cref="PracaDaBriga"/>) e na mesma ordem, pra que o unico que muda entre duas linhas
	/// comparadas seja o que a linha diz que mudou.
	/// </summary>
	private (RitmoDaBriga MarcAntes, RitmoDaBriga MarcDepois, RitmoDaBriga SoltoAntes, RitmoDaBriga SoltoDepois)
		AsQuatroBrigas()
	{
		GD.Print($"[kb] --- 8 e 9) BRIGA DE VERDADE: um NPC contra um corpo dirigido como JOGADOR, "
				 + $"{SegundosDeBriga:0} s por linha ---");
		GD.Print("[kb]        (relogio de PAREDE -- esta parte leva tres minutos; ver `Brigar`)");

		RitmoDaBriga marcAntes = Brigar(semSorteio: true, marcado: true, SegundosDeBriga);
		RelatarBriga("alvo MARCADO   | ANTES ", marcAntes);
		RitmoDaBriga marcDepois = Brigar(semSorteio: false, marcado: true, SegundosDeBriga);
		RelatarBriga("alvo MARCADO   | DEPOIS", marcDepois);

		RitmoDaBriga soltoAntes = Brigar(semSorteio: true, marcado: false, SegundosDeBriga);
		RelatarBriga("SEM marcar     | ANTES ", soltoAntes);
		RitmoDaBriga soltoDepois = Brigar(semSorteio: false, marcado: false, SegundosDeBriga);
		RelatarBriga("SEM marcar     | DEPOIS", soltoDepois);

		return (marcAntes, marcDepois, soltoAntes, soltoDepois);
	}

	private void RelatarBriga(string rotulo, RitmoDaBriga m)
	{
		GD.Print($"[kb]        {rotulo,-30} {m.SegundosDeJogo,4:0} s de jogo em {m.Segundos:0} s de parede | {m.Socos,4} socos | "
				 + $"arremessos {m.Arremessos,3} = {m.ArremessosPorMinuto,5:0.0}/min "
				 + $"(1 a cada {m.SegundosEntreArremessos,4:0.0} s) | corpo no ar {m.PcComAlguemNoAr,4:0.0}% do tempo | "
				 + $"soco no vazio {m.PcNoVazio,4:0.0}% | dist media {m.DistanciaMedia,3:0} px | "
				 + $"pesado {100 * m.ChanceDePesado:0}% dos golpes | {m.Recentragens} recentragens");
	}

	// =====================================================================
	// 8) A BRIGA DEIXA DE SER PINGUE-PONGUE -- a queixa literal
	// =====================================================================
	/// <summary>
	/// *"fui lutar com uns npcs e era UM JOGANDO O OUTRO PRA LONGE e tava estranha a luta"*.
	///
	/// Esta familia e a unica que mede a frase inteira, e nao a peca dela: as familias 1 a 7 dizem com
	/// que CHANCE um golpe arremessa; esta diz com que FREQUENCIA isso acontece numa briga -- que e o
	/// que o dono viu na tela. As duas coisas nao sao a mesma: uma chance de 33% num golpe que sai tres
	/// vezes por segundo ainda seria pingue-pongue.
	///
	/// ============================ COMO ELA REPROVA ============================
	///   * **o ANTES nao reproduz a queixa** -- se a briga com o defeito ligado nao arremessar muito,
	///     ou a cena nao e a do dono ou o interruptor nao esta ligado, e nos dois casos a linha de
	///     baixo nao vale nada. Ela e a metade que prova que a bancada mede alguma coisa;
	///   * **a frequencia nao caiu** -- o sorteio existe e nao mudou o RITMO. E o caso de alguem
	///     "consertar" o sorteio num caminho que a briga nao usa;
	///   * **a frequencia caiu a ZERO** -- o contra-exemplo. Um pesado que nunca mais arremessa passaria
	///     em "caiu", e seria outro defeito: o soco pesado perde o que o torna pesado;
	///   * **o tempo com corpo no ar nao caiu** -- e possivel arremessar menos vezes e mesmo assim
	///     passar o mesmo tempo com alguem voando (voos mais longos). Ninguem pediu voo mais longo, e
	///     esta e a linha que denuncia se a intensidade tiver sido mexida no lugar da frequencia.
	/// ==========================================================================
	/// </summary>
	private void OPinguePongueParou(RitmoDaBriga antes, RitmoDaBriga depois)
	{
		GD.Print("[kb] --- 8) A BRIGA DE DOIS NPCS DEIXOU DE SER PINGUE-PONGUE ---");

		AfirmarKb("(preparo) a briga aconteceu mesmo nos dois minutos -- houve troca de socos dos dois lados",
				  antes.Socos > 20 && depois.Socos > 20 && antes.Arremessos > 0,
				  $"antes {antes.Socos} socos / depois {depois.Socos} socos");

		AfirmarKb("o ANTES reproduz a queixa: com o `else` sem sorteio, alguem voa toda hora",
				  antes.ArremessosPorMinuto >= 8,
				  $"{antes.ArremessosPorMinuto:0.0} arremessos/min (1 a cada {antes.SegundosEntreArremessos:0.0} s)");

		AfirmarKb("...e o sorteio derruba a FREQUENCIA da briga em pelo menos um terco",
				  depois.ArremessosPorMinuto < antes.ArremessosPorMinuto * 0.67,
				  $"{antes.ArremessosPorMinuto:0.0}/min -> {depois.ArremessosPorMinuto:0.0}/min "
				  + $"(1 a cada {antes.SegundosEntreArremessos:0.0} s -> 1 a cada {depois.SegundosEntreArremessos:0.0} s)");

		AfirmarKb("...sem parar de arremessar: um pesado que nunca joga longe seria outro defeito",
				  depois.Arremessos > 0, $"{depois.Arremessos} arremessos no minuto");

		AfirmarKb("...e a briga passa MENOS tempo com um corpo no ar -- a frequencia caiu, nao a duracao",
				  depois.PcComAlguemNoAr < antes.PcComAlguemNoAr * 0.75,
				  $"{antes.PcComAlguemNoAr:0.0}% -> {depois.PcComAlguemNoAr:0.0}% do tempo");
	}

	// =====================================================================
	// 9) E OS SOCOS PASSAM A ACERTAR -- a OUTRA queixa dele
	// =====================================================================
	/// <summary>
	/// *"meus socos nao acertam"*, o segundo relato. A medicao da Fase 0 achou a causa por acaso e ela
	/// e a MESMA deste conserto: o arremesso joga o corpo 512 a 576 px, o arranque em recarga nao fecha
	/// de volta, e o soco seguinte sai no vazio. Ou seja **arremessar menos vezes ataca as duas queixas
	/// de uma vez** -- e esta familia e quem mede o quanto.
	///
	/// Ela e um SUBPRODUTO, e o texto diz isso de proposito: se um dia o remedio da queixa 1 deixar de
	/// aliviar a queixa 2, o numero aqui cai sozinho e ninguem precisa lembrar da ligacao entre as duas.
	///
	/// ============================ E A RESPOSTA DEPENDE DE MARCAR O ALVO, o que foi uma surpresa ============================
	/// A primeira versao desta familia mediu so o caso com ALVO MARCADO e deu **0,0% de soco no vazio
	/// nas duas versoes** -- um empate perfeito, e nao um erro de contagem. A conta explica: o arranque
	/// de quem marcou busca a 480 px (`AlcanceDoDashMarcado`, os 15 tiles do DM) e o arremesso mais
	/// longo joga o corpo 576 px, entao **quem mira quase sempre alcanca de volta**, mesmo no jogo
	/// errado. Sem marcar, o arranque busca a 160 px e o resto tem que ser corrido a pe.
	///
	/// Por isso a familia mede as duas maneiras: a AFIRMACAO e sobre quem nao marca (o caso em que a
	/// distancia decide), e o caso marcado entra como o par que **prova que a queixa nao e universal** --
	/// se ele deixar de dar zero um dia, o arranque marcado deixou de alcancar o arremesso.
	/// ==================================================================================================================
	///
	/// ============================ COMO ELA REPROVA ============================
	///   * **o vazio nao caiu (sem marcar)** -- o conserto nao aliviou nada, e a segunda queixa continua
	///     inteira;
	///   * **a distancia media nao caiu** -- e a razao FISICA do alivio. Sem ela, uma queda no vazio
	///     poderia vir de qualquer outra coisa (menos socos, socos mais lentos), e a familia estaria
	///     dando o credito ao reu errado;
	///   * **os socos sumiram** -- o contra-exemplo obrigatorio: a maneira mais facil de zerar "socos no
	///     vazio" e parar de socar. Se a contagem de socos despencar junto, a porcentagem nao vale nada;
	///   * **o caso marcado deixou de dar zero** -- o arranque de 480 px parou de cobrir o voo de 576.
	/// ==========================================================================
	/// </summary>
	private void OsSocosPassamAAcertar(RitmoDaBriga antes, RitmoDaBriga depois,
									   RitmoDaBriga marcAntes, RitmoDaBriga marcDepois)
	{
		GD.Print("[kb] --- 9) OS SOCOS PASSAM A ACERTAR MAIS (a OUTRA queixa) ---");

		AfirmarKb("SEM MARCAR o alvo, os socos no vazio CAIRAM -- o corpo deixa de ser jogado pra fora "
				+ "do alcance do arranque curto",
				  depois.PcNoVazio < antes.PcNoVazio - 3,
				  $"{antes.PcNoVazio:0.0}% -> {depois.PcNoVazio:0.0}% dos socos "
				  + $"({antes.Vazios}/{antes.Socos} -> {depois.Vazios}/{depois.Socos})");

		AfirmarKb("...e a razao disso e a DISTANCIA: os dois passam a briga mais perto um do outro",
				  depois.DistanciaMedia < antes.DistanciaMedia,
				  $"{antes.DistanciaMedia:0} px -> {depois.DistanciaMedia:0} px "
				  + $"({antes.DistanciaMedia / ZoneCollision.TileSize:0.0} -> "
				  + $"{depois.DistanciaMedia / ZoneCollision.TileSize:0.0} tiles)");

		AfirmarKb("...e nao e porque pararam de socar: o numero de socos ficou no mesmo patamar",
				  depois.Socos > antes.Socos * 0.7,
				  $"antes {antes.Socos} socos / depois {depois.Socos} socos");

		AfirmarKb("QUEM MARCA O ALVO nao soca o vazio em NENHUMA das duas versoes -- o arranque marcado "
				+ "(480 px) cobre o arremesso mais longo (576 px), e por isso a queixa 2 nao e universal",
				  marcAntes.PcNoVazio < 10 && marcDepois.PcNoVazio < 10,
				  $"antes {marcAntes.PcNoVazio:0.0}% ({marcAntes.Vazios}/{marcAntes.Socos}) / "
				  + $"depois {marcDepois.PcNoVazio:0.0}% ({marcDepois.Vazios}/{marcDepois.Socos})");
	}

	// =====================================================================
	// O RASTRO SEGUE OS PIXELS
	// =====================================================================
	/// <summary>
	/// O dono (2026-09-04): *"a trilha do knock back fica em cima do tile mais proximo, so que ja que o
	/// player se move por pixel a trilha fica muitas vezes torta"*. Um arremesso na DIAGONAL, medido
	/// no fio: as marcas nascem sobre a reta do voo, uma a cada tile andado, e nenhuma encaixada no
	/// centro da celula -- o encaixe era exatamente a escadinha que ele viu. Ver `CarimbarSulco`.
	/// </summary>
	private void ORastroSegueOsPixels()
	{
		GD.Print("[kb] -- o rastro do arremesso segue os PIXELS, e nao a grade --");
		const int T = ZoneCollision.TileSize;

		ServerPlayer d = Forjar("kbRastro", new Vec2(8 * T, 8 * T), 5_551);
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(d.Zone);
		Vec2? canto = mapa == null ? null : PracaSeca(mapa, 16);
		AfirmarKb("(montagem) achei uma praca seca de 16x16 tiles pro arremesso na diagonal", canto != null);
		if (canto is not { } c) return;

		// A ORIGEM FORA DO CENTRO DA CELULA, de proposito: e o caso do jogo (ninguem para no centro).
		Vec2 origem = c + new Vec2(T + 5, T + 9);
		d.Pos = origem;
		d.Altitude = 0;
		Vec2 rumo = new Vec2(1, 1).Normalized();
		d.RumoDoVoo = rumo;
		d.TiquesDeVoo = d.TiquesIniciaisDoVoo = 6;   // 12 tiles: acima do minimo que deixa rastro
		d.ForcaDoVoo = 0;                              // abaixo da resistencia: nao derruba nada
		d.UltimoSulco = default;

		// SO O MEU VOO: as familias anteriores deixaram corpos ainda no ar, e o `TickDoEmpurrao` e de
		// TODOS -- a primeira rodada desta familia colheu 24 marcas e 8 pontas de quatro arremessos
		// diferentes. Os outros pousam agora, e o que se le do fio e so o que caiu DENTRO da praca.
		foreach (ServerPlayer o in TodosOsCorpos().ToList())
			if (o != d) { o.TiquesDeVoo = 0; o.TiquesIniciaisDoVoo = 0; }
		Vec2 fimDaPraca = c + new Vec2(16 * T, 16 * T);
		bool NaPraca(Vec2 v) => v.X >= c.X && v.Y >= c.Y && v.X <= fimDaPraca.X && v.Y <= fimDaPraca.Y;

		EscutaDeDecalques = [];
		MarcarSulco(d, Protocol.Decal.SulcoPonta);
		for (int i = 0; i < 60 && d.TiquesDeVoo > 0; i++) TickDoEmpurrao();

		var sulcos = new List<Vec2>();
		var pontas = new List<Vec2>();
		foreach ((ulong zona, Protocol.Decal t, byte[] fio) in EscutaDeDecalques ?? [])
		{
			if (zona != d.Zone.Hash || t is not (Protocol.Decal.Sulco or Protocol.Decal.SulcoPonta)) continue;
			(_, Vec2 onde, _, _, _) = LerDecalque(fio);
			if (!NaPraca(onde)) continue;
			(t == Protocol.Decal.Sulco ? sulcos : pontas).Add(onde);
		}
		EscutaDeDecalques = null;

		AfirmarKb("um arremesso na DIAGONAL deixou uma fileira de sulcos", sulcos.Count >= 6, $"{sulcos.Count} marca(s)");
		if (sulcos.Count < 2) return;

		int saltos = 0;
		for (int i = 1; i < sulcos.Count; i++)
			if (Math.Abs((sulcos[i] - sulcos[i - 1]).Length - T) > 0.5f) saltos++;
		AfirmarKb("...cada marca a exatamente um tile da anterior AO LONGO DO CAMINHO (a distancia manda, e nao a celula)",
				  saltos == 0, $"{saltos} salto(s)");

		Vec2 pes0 = origem + new Vec2(0, MoveRules.FeetOffsetY);
		float foraDaReta = sulcos.Max(m => Math.Abs((m.X - pes0.X) * rumo.Y - (m.Y - pes0.Y) * rumo.X));
		AfirmarKb("...todas EM CIMA da reta do voo: o rastro nasce por onde o corpo passou, nos pixels",
				  foraDaReta < 1.5f, $"maior desvio {foraDaReta:0.##} px");

		int noCentro = sulcos.Count(m => Math.Abs(m.X % T - T / 2f) < 0.5f && Math.Abs(m.Y % T - T / 2f) < 0.5f);
		AfirmarKb("...e NENHUMA encaixada a forca no centro da celula -- a escadinha 'torta' que o dono viu era esse encaixe",
				  noCentro == 0, $"{noCentro} de {sulcos.Count} no centro");

		Vec2 pesFim = d.Pos + new Vec2(0, MoveRules.FeetOffsetY);
		AfirmarKb("...com a PONTA nas duas extremidades (a tampa sempre carimba; distancia nenhuma a segura)",
				  pontas.Count == 2 && Vec2.Distance(pontas[0], pes0) < 0.5f && Vec2.Distance(pontas[1], pesFim) < 0.5f,
				  $"{pontas.Count} ponta(s)");
	}

	/// <summary>Um quadrado de `lado` tiles sem parede e sem agua, longe da borda. Devolve o canto (pixel) ou nulo.</summary>
	/// <summary>
	/// O ARREMESSO NO AR (dono, 2026-09-07): *"ao estar voando, efeitos de knock back como a trilha e
	/// cratera nao devem acontecer pois se o personagem ta voando ele nao encosta no chao, logo nao
	/// causa destruicao (isso conta pra paredes que dao pra voar por cima)"*. O mesmo arremesso, duas
	/// vezes: pairando e no chao. No ar: sem sulco, sem cratera, altura intacta, e o muro no caminho
	/// continua de pe com o corpo do outro lado. No chao: o rastro aparece e o muro para o corpo.
	/// </summary>
	private void OArremessoNoAr()
	{
		GD.Print("[kb] -- 10) O ARREMESSO NO AR: sem sulco, sem cratera, por cima do muro --");
		const int T = ZoneCollision.TileSize;
		ServerPlayer d = Forjar("kbNoAr", new Vec2(8 * T, 8 * T), 5_551);
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(d.Zone);
		Vec2? canto = mapa == null ? null : PracaSeca(mapa, 16);
		AfirmarKb("(montagem) achei uma praca seca de 16x16 tiles pro arremesso no ar", canto != null);
		if (canto is not { } c || mapa == null) return;
		Vec2 origem = c + new Vec2(2 * T + 16, 8 * T + 16);
		int muroCx = (int)(origem.X / T) + 6, muroCy = (int)(origem.Y / T);
		Vec2 fimDaPraca = c + new Vec2(16 * T, 16 * T);
		bool NaPraca(Vec2 v) => v.X >= c.X && v.Y >= c.Y && v.X <= fimDaPraca.X && v.Y <= fimDaPraca.Y;
		int Rastro()
		{
			int n = 0;
			foreach ((ulong zona, Protocol.Decal t, byte[] fio) in EscutaDeDecalques ?? [])
			{
				if (zona != d.Zone.Hash || t is not (Protocol.Decal.Sulco or Protocol.Decal.SulcoPonta or Protocol.Decal.Cratera or Protocol.Decal.Fumaca)) continue;
				(_, Vec2 onde, _, _, _) = LerDecalque(fio);
				if (NaPraca(onde)) n++;
			}
			return n;
		}
		void Arremesso()
		{
			foreach (ServerPlayer o in TodosOsCorpos().ToList())
				if (o != d) { o.TiquesDeVoo = 0; o.TiquesIniciaisDoVoo = 0; }
			EscutaDeDecalques = [];
			Arremessar(d, new Vec2(1, 0), 0, 6);   // 12 tiles, forca abaixo da resistencia: o muro nao cai
			for (int i = 0; i < 90 && d.TiquesDeVoo > 0; i++) { TickDoEmpurrao(); TickDoVoo(d, Protocol.TickSeconds); }
		}
		mapa.Bloquear(muroCx, muroCy);
		try
		{
			AfirmarKb("(montagem) ha um muro a 6 tiles no caminho", mapa.BlockedCell(muroCx, muroCy));

			// NO AR
			d.Pos = origem;
			d.Voando = true;
			d.Altitude = Voo.AlturaDePairar;
			d.Ficha.Ki = d.Ficha.MaxKi;
			d.Ficha.KO = false;
			Arremesso();
			int rastroNoAr = Rastro();
			AfirmarKb("VOANDO, o arremesso nao deixa sulco, ponta, cratera nem fumaca", rastroNoAr == 0, $"{rastroNoAr} marca(s)");
			AfirmarKb("...a altura fica onde estava durante o voo do golpe", Mathf.IsEqualApprox(d.Altitude, Voo.AlturaDePairar) && d.Voando, $"altitude {d.Altitude:0}, voando {d.Voando}");
			AfirmarKb("...e o corpo passa POR CIMA do muro", d.Pos.X > (muroCx + 1) * T, $"x {d.Pos.X:0} vs muro ate {(muroCx + 1) * T}");
			AfirmarKb("...que continua de pe", mapa.BlockedCell(muroCx, muroCy));

			// CONTRA-EXEMPLO: NO CHAO
			d.Pos = origem;
			d.Voando = false;
			d.Altitude = 0f;
			Arremesso();
			int rastroNoChao = Rastro();
			AfirmarKb("CONTRA-EXEMPLO: no CHAO o mesmo arremesso deixa rastro", rastroNoChao >= 1, $"{rastroNoChao} marca(s)");
			AfirmarKb("...e o muro PARA o corpo (forca abaixo da resistencia)", d.Pos.X < muroCx * T, $"x {d.Pos.X:0} vs muro em {muroCx * T}");
		}
		finally
		{
			// O MURO SAI PELA PORTA QUE REFAZ A CAMADA INTEIRA. `mapa.Bloquear` escreveu o muro na camada das OBRAS, e o
			// `LimparObras` sozinho apagava junto a parede de TODA obra densa da Terra (os bancos, as macieiras, a Research
			// Station do berco) pelo resto da sessao: as familias seguintes rodavam num mundo em que a mobilia nao barra
			// ninguem, e o servidor do `--host` continuava assim depois da bancada. `AplicarColisaoDasObras` limpa a camada
			// e a refaz pela lista -- o muro da bancada (que nao e obra) some, e o resto volta.
			AplicarColisaoDasObras(d.Zone);
			EscutaDeDecalques = null;
			d.Voando = false;
			d.Altitude = 0f;
		}
	}

	private static Vec2? PracaSeca(ZoneCollision mapa, int lado)
	{
		for (int y = 4; y + lado < mapa.Height - 4; y += 2)
			for (int x = 4; x + lado < mapa.Width - 4; x += 2)
			{
				bool bom = true;
				for (int j = 0; j < lado && bom; j++)
					for (int i = 0; i < lado && bom; i++)
						bom &= !mapa.BlockedCell(x + i, y + j) && !mapa.EhAgua(x + i, y + j);
				if (bom) return new Vec2(x * ZoneCollision.TileSize, y * ZoneCollision.TileSize);
			}
		return null;
	}

	// =====================================================================
	// 11) O MURO QUE PARA E O MURO QUE CAI
	// =====================================================================
	/// <summary>
	/// ============================ `Movement Effects.dm:65-73`: O TURF QUE BARRA E O QUE SE PERGUNTA ============================
	/// O arremesso de forca 20 contra um muro de resistencia 20 DERRUBA o muro e segue. O port perguntava
	/// "parou?" pelas quatro quinas da caixa dos pes e "caiu?" pela celula do ponto dos pes -- e nos 5 px entre
	/// uma coisa e outra o muro "resistia". Achado pela `--doiscorposteste` (a porta da casa do berco da Terra);
	/// o conserto e o `DerrubarOQueBarraOsPes`, e a medida mora aqui porque o arremesso e daqui.
	///
	/// A CENA E MONTADA NO CASO DIFICIL, e a primeira linha prova que ela e: na amostra em que a caixa encosta no
	/// muro, o ponto dos pes ainda esta FORA dele. Sem essa precondicao o verde poderia vir de uma amostra que ja
	/// caisse com o ponto dos pes dentro -- o caso que sempre funcionou, e que nao mede o conserto. Com a sonda
	/// antiga (a celula do ponto dos pes) a linha principal fica VERMELHA: e o contra-exemplo do CODIGO. O
	/// contra-exemplo do DADO e o mesmo arremesso abaixo da resistencia -- o muro fica e o corpo para antes dele.
	/// ==========================================================================================================
	/// </summary>
	private void OMuroQueParaEOMuroQueCai()
	{
		GD.Print("[kb] -- 11) o muro que PARA o arremesso e o muro que CAI --");
		const int T = ZoneCollision.TileSize;
		ServerPlayer d = Forjar("kbMuro", new Vec2(8 * T, 8 * T), 5_551);
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(d.Zone);
		(int Cx, int Cy)? achado = mapa == null ? null : MuroQueCai(mapa);
		AfirmarKb("(montagem) achei um muro que cai, com tres celulas de chao ao norte e duas ao sul", achado != null);
		if (achado is not { } muro || mapa == null) return;

		// A AMOSTRA E A DO LACO DO VOO (`TickDoEmpurrao`): o passo do tique fatiado, varrido em pedacos de no
		// maximo meio tile. A origem fica SEIS amostras antes do caso dificil: la a quina de baixo da caixa
		// (`FeetOffsetY + BodyHalfH` abaixo do centro) esta 2,5 px DENTRO do muro e o ponto dos pes (`FeetOffsetY`)
		// 2,5 px FORA -- folga dos dois lados, e nao um fio de ponto flutuante como o 8375,999 da porta da Terra.
		float passo = (float)(Empurrao.TilesPorTique * T * (Protocol.TickSeconds / Empurrao.SegundosPorTique));
		float amostra = passo / Math.Max(1, (int)MathF.Ceiling(passo / (T / 2f)));
		float topo = muro.Cy * T;
		var origem = new Vec2(muro.Cx * T + T / 2f, topo - MoveRules.FeetOffsetY - 2.5f - 6 * amostra);
		Vec2 aSexta = origem + new Vec2(0, 6 * amostra);
		AfirmarKb("(montagem) na amostra em que a caixa dos pes ENCOSTA no muro, o ponto dos pes ainda esta FORA "
				+ "dele -- o caso que a sonda de um ponto so lia como 'resistiu'",
				  MoveRules.Occupied(mapa, aSexta, ModoDeTravessia.Arremessado)
				  && !MoveRules.Occupied(mapa, origem + new Vec2(0, 5 * amostra), ModoDeTravessia.Arremessado)
				  && CelulaDoPonto(aSexta) != (muro.Cx, muro.Cy));

		HashSet<(int X, int Y)> caidasAntes =
			_cenarioCaido.TryGetValue(d.Zone.Name, out HashSet<(int X, int Y)>? jaCaidas) ? [.. jaCaidas] : [];

		void Jogar(double forca)
		{
			// SO O MEU VOO -- o mesmo cuidado do `ORastroSegueOsPixels`: o `TickDoEmpurrao` e de todos.
			foreach (ServerPlayer o in TodosOsCorpos().ToList())
				if (o != d) { o.TiquesDeVoo = 0; o.TiquesIniciaisDoVoo = 0; }
			d.Pos = origem;
			d.Altitude = 0f;
			d.Voando = false;
			Arremessar(d, new Vec2(0, 1), forca, 6);
			for (int i = 0; i < 60 && d.TiquesDeVoo > 0; i++) TickDoEmpurrao();
		}

		try
		{
			// ---- O CONTRA-EXEMPLO DO DADO: abaixo da resistencia, o muro para o corpo e fica ----
			Jogar(0);
			// E PARA NO LUGAR CERTO: na ULTIMA amostra livre antes do muro (a quinta), e nao antes. So "ficou
			// acima do muro" a origem ja cumpria -- um arremesso que nem saisse do lugar (uma guarda nova de
			// "forca zero nao arremessa", um corpo esquecido na coluna) passaria calado por esta linha.
			float ultimaLivre = origem.Y + 5 * amostra;
			AfirmarKb("CONTRA-EXEMPLO: abaixo da resistencia o muro PARA o corpo, na ultima amostra livre antes dele, "
					+ "e continua de pe",
					  mapa.BlockedCell(muro.Cx, muro.Cy) && Math.Abs(d.Pos.Y - ultimaLivre) < 1f,
					  $"y {d.Pos.Y:0.#} vs ultima amostra livre {ultimaLivre:0.#} (muro em {topo:0})");

			// ---- O CASO: forca 20 contra resistencia 20 -- o muro em que a caixa encostou CAI ----
			Jogar(Empurrao.ResistenciaPadrao);
			AfirmarKb("O MURO QUE PARA E O MURO QUE CAI: com a forca do muro, a celula em que a caixa encostou cai "
					+ "e o corpo passa (`Movement Effects.dm:66-68`)",
					  !mapa.BlockedCell(muro.Cx, muro.Cy) && d.Pos.Y > topo + T,
					  $"muro de pe={mapa.BlockedCell(muro.Cx, muro.Cy)} | y {d.Pos.Y:0} vs fim do muro {topo + T:0}");
		}
		finally
		{
			// O MURO VOLTA. O `_cenarioCaido` vive so em memoria (o mapa volta ao normal quando o servidor
			// reinicia), mas esta bancada roda no servidor de verdade e esta Terra e a de todo mundo: sem isto o
			// buraco ficaria no mapa da sessao inteira -- e no retrato mandado a quem entrar na Terra depois.
			if (_cenarioCaido.TryGetValue(d.Zone.Name, out HashSet<(int X, int Y)>? caidas))
				foreach ((int X, int Y) cel in caidas.Where(c => !caidasAntes.Contains(c)).ToList())
				{
					caidas.Remove(cel);
					mapa.Fechar(cel.X, cel.Y);
				}
			d.TiquesDeVoo = 0;
		}
	}

	/// <summary>
	/// O PRIMEIRO MURO QUE CAI: parede DO ARQUIVO (nao obra, nao lacre, nao porta aberta), fora do `.duro` e da
	/// beirada, com tres celulas de chao ao norte -- por onde o corpo chega -- e duas ao sul, por onde ele passa.
	/// </summary>
	private static (int Cx, int Cy)? MuroQueCai(ZoneCollision mapa)
	{
		for (int y = 8; y < mapa.Height - 8; y++)
			for (int x = 8; x < mapa.Width - 8; x++)
			{
				if (!mapa.BloqueadaNoArquivo(x, y) || !mapa.BlockedCell(x, y) || mapa.Selada(x, y)
					|| mapa.Indestrutivel(x, y) || mapa.NaBorda(x, y)) continue;
				bool bom = true;
				for (int k = 1; k <= 3 && bom; k++) bom &= mapa.ServeDeChao(x, y - k);
				for (int k = 1; k <= 2 && bom; k++) bom &= mapa.ServeDeChao(x, y + k);
				if (bom) return (x, y);
			}
		return null;
	}

	// =====================================================================
	// 12) A OBRA QUE A CAIXA TOCA
	// =====================================================================
	/// <summary>
	/// ============================ `Movement Effects.dm:74-76`: A OBRA QUE O CORPO ATRAVESSA ============================
	/// `for(var/obj/O in get_step(target,dir)) if(O.fragile) O.takeDamage(pow)` -- e no `Ticked` isso vem ANTES do
	/// `step`, entao a obra que cede deixa o corpo passar no mesmo tique. O port perguntava pela celula do PONTO DOS
	/// PES, enquanto quem para o corpo sao as QUINAS da caixa: o descompasso que a familia 11 mediu pro muro, agora na
	/// obra. O conserto e o `EstragarObrasNoCaminho` pela caixa (`CelulasDaCaixa`), uma pancada por travessia.
	///
	/// TRES CENAS, e a sonda antiga (a celula do ponto dos pes, a cada amostra) fica VERMELHA nas duas primeiras --
	/// e o contra-exemplo do CODIGO, como na familia 11 (a C e a da primeira amostra do voo, ver la):
	///
	///   A. DE RASPAO numa obra que NAO barra (a bancada de artesao): o corpo desce uma coluna com a quina direita
	///      4 px dentro da coluna vizinha, onde a obra esta. O ponto dos pes NUNCA entra na celula dela, em fase
	///      nenhuma. E a pancada e UMA: a armadura 16 foi escolhida pra forca 20 tirar metade (20 - 0,75 x 16 = 8) e a
	///      SEGUNDA pancada derruba-la -- as quinas sem a regra da entrada (umas quatro amostras tocando a celula)
	///      poriam a obra no chao, e isso tambem fica vermelho.
	///
	///   B. DE FRENTE numa obra que BARRA, com a armadura 1 da mobilia do mapa, na geometria da familia 11: na amostra
	///      em que a quina de baixo encosta, o ponto dos pes esta 2,5 px fora. Com forca 20 ela cai e o corpo passa;
	///      com a sonda antiga ela nao apanhava ali, o laco lia "resistiu" e o corpo se arrebentava numa obra de
	///      armadura 1. O contra-exemplo do DADO e a forca 0: nao passa do piso de 75%, a obra fica e o corpo para na
	///      ultima amostra livre.
	///
	/// AS OBRAS SAO `DoMapa`, e isso e a guarda do disco: se cairem, o `Estragar` nao chama o `GravarMundo` -- esta
	/// bancada roda com a pasta de saves de verdade (o `testar-knockback.bat` nao desvia o APPDATA). Pelo mesmo motivo
	/// a praca tem de estar VAZIA de obra e nave alheias: a forca 20 derrubaria a bancada que o dono ergueu ali, e a
	/// queda iria pro `mundo.json` dele. NAVE NAO ENTRA NA CENA porque o `EstragarNave` grava o `naves.json` a cada
	/// arranhao.
	/// ==================================================================================================================
	/// </summary>
	private void AObraQueACaixaToca()
	{
		GD.Print("[kb] -- 12) a obra que a CAIXA toca, e nao so o ponto dos pes --");
		const int T = ZoneCollision.TileSize;
		const string TipoRasteiro = "Crafting_Bench", TipoDenso = "Research_Station";
		ServerPlayer d = Forjar("kbObra", new Vec2(8 * T, 8 * T), 5_551);
		ZoneKey zona = d.Zone;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		Vec2? canto = mapa == null ? null : PracaSeca(mapa, 16);
		bool catalogo = _obras?.Get(TipoRasteiro) is { Densa: false } && _obras?.Get(TipoDenso) is { Densa: true };
		AfirmarKb("(montagem) achei uma praca seca de 16x16 tiles, e o catalogo tem uma obra que nao barra e uma que barra",
				  canto != null && catalogo);
		if (canto is not { } c || mapa == null || !catalogo) return;

		int px0 = (int)(c.X / T), py0 = (int)(c.Y / T);
		bool NaPraca((int X, int Y) cel) => cel.X >= px0 && cel.Y >= py0 && cel.X < px0 + 16 && cel.Y < py0 + 16;
		bool vazia = !_noChao.Any(ob => ob.Zona.Equals(zona) && NaPraca(Jandirus.Core.Tech.CatalogoDeObras.Celula(ob.X, ob.Y)))
					 && !NavesParadasEm(zona).Any(nv => NaPraca(Jandirus.Core.Tech.CatalogoDeObras.Celula(nv.X, nv.Y)));
		AfirmarKb("(montagem) a praca nao tem obra nem nave de ninguem -- a forca 20 derrubaria o que o dono ergueu ali",
				  vazia);
		if (!vazia) return;

		Obra ErguerNaCelula(string tipo, int cx, int cy, double armadura)
		{
			var nova = new Obra
			{
				Id = _proximaObraId++, Tipo = tipo,
				// OS PES NO MEIO DA CELULA: e a conta do `CatalogoDeObras.Celula` (a ancora e o ponto dos pes).
				X = cx * T + T / 2f, Y = cy * T + T / 2f - MoveRules.FeetOffsetY,
				DonoConta = "bancada_kb", DonoNome = "bancada", ErguidaEm = NowMs(),
				DoMapa = true,   // se cair, nao vai pro `mundo.json` -- ver o cabecalho
				ArmaduraMax = armadura, Armadura = armadura,
			};
			nova.PorZona(zona);
			_noChao.Add(nova);
			return nova;
		}

		void JogarPraBaixo(Vec2 origem, double forca, int tiques)
		{
			// SO O MEU VOO -- o mesmo cuidado do `ORastroSegueOsPixels`: o `TickDoEmpurrao` e de todos.
			foreach (ServerPlayer outro in TodosOsCorpos().ToList())
				if (outro != d) { outro.TiquesDeVoo = 0; outro.TiquesIniciaisDoVoo = 0; }
			d.Pos = origem;
			d.Altitude = 0f;
			d.Voando = false;
			Arremessar(d, new Vec2(0, 1), forca, tiques);
			for (int i = 0; i < 60 && d.TiquesDeVoo > 0; i++) TickDoEmpurrao();
		}

		Obra? rasteira = null, densa = null, daFrente = null;
		try
		{
			// ---- A. DE RASPAO: a quina direita 4 px DENTRO da coluna da obra, o ponto dos pes 4 px FORA ----
			int colA = px0 + 15, filaA = py0 + 8;
			rasteira = ErguerNaCelula(TipoRasteiro, colA, filaA, 16);
			var origemA = new Vec2(colA * T - 4f, (py0 + 2) * T + T / 2f - MoveRules.FeetOffsetY);
			AfirmarKb("(montagem) A: a quina direita da caixa esta na coluna da obra e o ponto dos pes na vizinha -- e o voo "
					+ "e reto pro sul, entao o ponto dos pes nunca entra nela",
					  (int)MathF.Floor((origemA.X + MoveRules.BodyHalfW) / T) == colA && CelulaDoPonto(origemA).Cx == colA - 1);
			JogarPraBaixo(origemA, Empurrao.ResistenciaPadrao, 6);
			AfirmarKb("(montagem) A: o corpo atravessou a fileira da obra de ponta a ponta",
					  d.Pos.Y + MoveRules.FeetOffsetY - MoveRules.BodyHalfH >= (filaA + 1) * T,
					  $"topo da caixa em y {d.Pos.Y + MoveRules.FeetOffsetY - MoveRules.BodyHalfH:0} vs fim da obra {(filaA + 1) * T}");
			double umaPancada = Armadura.Bater(16, 16, Empurrao.ResistenciaPadrao);
			AfirmarKb("A OBRA QUE SO A QUINA TOCA APANHA: de raspao, com o ponto dos pes sempre na coluna vizinha "
					+ "(`Movement Effects.dm:74-76`)",
					  _noChao.Contains(rasteira) && rasteira.Armadura < rasteira.ArmaduraMax,
					  $"armadura {rasteira.Armadura:0.##}/{rasteira.ArmaduraMax:0.##} | de pe={_noChao.Contains(rasteira)}");
			AfirmarKb("...e UMA pancada por travessia, e nao uma por amostra: sobrou exatamente o que uma pancada de forca 20 "
					+ "deixa -- a segunda a teria derrubado",
					  _noChao.Contains(rasteira) && Math.Abs(rasteira.Armadura - umaPancada) < 1e-9,
					  $"armadura {rasteira.Armadura:0.##}, esperado {umaPancada:0.##}");

			// ---- C. A CAIXA JA ENCOSTAVA NA OBRA DA FRENTE QUANDO O VOO COMECOU ----
			// Os pes 3 px ACIMA da fileira da obra: a caixa (5 px abaixo dos pes) ja entra 2 px nela no instante do golpe.
			// Comparando a primeira amostra com a caixa de PARTIDA inteira, ela nunca "entrava" e a obra passava ilesa;
			// o DM olha o `get_step` -- o turf da frente -- ja no primeiro passo. Uma pancada, como na cena A.
			int colC = px0 + 4, filaC = py0 + 6;
			daFrente = ErguerNaCelula(TipoRasteiro, colC, filaC, 16);
			var origemC = new Vec2(colC * T + T / 2f, filaC * T - 3f - MoveRules.FeetOffsetY);
			AfirmarKb("(montagem) C: no instante do golpe a caixa ja toca a fileira da obra e o ponto dos pes esta na de cima",
					  (int)MathF.Floor((origemC.Y + MoveRules.FeetOffsetY + MoveRules.BodyHalfH) / T) == filaC
					  && CelulaDoPonto(origemC).Cy == filaC - 1);
			JogarPraBaixo(origemC, Empurrao.ResistenciaPadrao, 6);
			AfirmarKb("A OBRA DA FRENTE QUE A CAIXA JA TOCAVA NO GOLPE APANHA UMA VEZ -- o `get_step` e olhado ja no "
					+ "primeiro passo, e o que ja tocava e so o turf dos pes",
					  _noChao.Contains(daFrente) && Math.Abs(daFrente.Armadura - umaPancada) < 1e-9,
					  $"armadura {daFrente.Armadura:0.##}, esperado {umaPancada:0.##} | de pe={_noChao.Contains(daFrente)}");

			// ---- B. DE FRENTE numa obra que BARRA: a geometria da familia 11 ----
			float passo = (float)(Empurrao.TilesPorTique * T * (Protocol.TickSeconds / Empurrao.SegundosPorTique));
			float amostra = passo / Math.Max(1, (int)MathF.Ceiling(passo / (T / 2f)));
			int colB = px0 + 11, filaB = py0 + 10;
			densa = ErguerNaCelula(TipoDenso, colB, filaB, Armadura.Padrao);
			AplicarColisaoDasObras(zona);   // a obra densa vira parede -- o mesmo funil do boot e do `Posicionar`
			float topo = filaB * T;
			var origemB = new Vec2(colB * T + T / 2f, topo - MoveRules.FeetOffsetY - 2.5f - 6 * amostra);
			Vec2 aSexta = origemB + new Vec2(0, 6 * amostra);
			AfirmarKb("(montagem) B: a obra BARRA, e na amostra em que a caixa encosta nela o ponto dos pes ainda esta FORA "
					+ "da celula dela -- o caso que a sonda de um ponto so deixava sem pancada",
					  mapa.BlockedCell(colB, filaB)
					  && MoveRules.Occupied(mapa, aSexta, ModoDeTravessia.Arremessado)
					  && !MoveRules.Occupied(mapa, origemB + new Vec2(0, 5 * amostra), ModoDeTravessia.Arremessado)
					  && CelulaDoPonto(aSexta) != (colB, filaB));

			// O CONTRA-EXEMPLO DO DADO: forca 0 nao passa do piso de 75% -- a obra fica, e o corpo para antes dela.
			JogarPraBaixo(origemB, 0, 3);
			float ultimaLivre = origemB.Y + 5 * amostra;
			AfirmarKb("CONTRA-EXEMPLO: forca 0 nao arranha a obra (o piso de 75% do `takeDamage`), ela fica de pe e o corpo "
					+ "para na ultima amostra livre antes dela",
					  _noChao.Contains(densa) && Math.Abs(densa.Armadura - densa.ArmaduraMax) < 1e-9
					  && Math.Abs(d.Pos.Y - ultimaLivre) < 1f,
					  $"armadura {densa.Armadura:0.##} | y {d.Pos.Y:0.#} vs ultima amostra livre {ultimaLivre:0.#}");

			// O CASO: forca 20 contra armadura 1 -- a obra em que a QUINA encostou apanha, cede, e o corpo passa.
			JogarPraBaixo(origemB, Empurrao.ResistenciaPadrao, 3);
			AfirmarKb("A OBRA QUE BARRA E A OBRA QUE CAI: a quina encostou, a obra levou o `takeDamage` e cedeu, e o corpo "
					+ "passou no mesmo voo (no `Ticked` a pancada vem antes do `step`)",
					  !_noChao.Contains(densa) && !mapa.BlockedCell(colB, filaB)
					  && d.Pos.Y + MoveRules.FeetOffsetY - MoveRules.BodyHalfH >= topo + T,
					  $"de pe={_noChao.Contains(densa)} | topo da caixa em y "
					  + $"{d.Pos.Y + MoveRules.FeetOffsetY - MoveRules.BodyHalfH:0} vs fim da obra {topo + T:0}");
		}
		finally
		{
			// AS OBRAS DA BANCADA SAEM, e o `MandarObras` refaz a camada de colisao e o pacote da zona pela lista.
			if (rasteira != null) _noChao.Remove(rasteira);
			if (densa != null) _noChao.Remove(densa);
			if (daFrente != null) _noChao.Remove(daFrente);
			if (rasteira != null || densa != null || daFrente != null) MandarObras(zona);
			d.TiquesDeVoo = 0;
		}
	}

	// =====================================================================
	// 13) NO AR, A PAREDE DE PREDIO PARA
	// =====================================================================
	/// <summary>
	/// ============================ O QUE NAO SE VOA POR CIMA, O ARREMESSADO NAO PASSA POR CIMA ============================
	/// A familia 10 mede a frase do dono de 2026-09-07 -- no ar o arremesso passa por cima do muro, "isso conta pra
	/// paredes que dao pra voar por cima". Em 2026-10-08 ele tirou uma parede dessa lista: "voar por cima de parede
	/// de base de player n deveria ser possivel". Entao a parede de PREDIO (a celula que barra e esta sob teto, ver
	/// `ClasseDePredio`) para o corpo arremessado em qualquer altura -- senao bastava um soco pra por alguem dentro da
	/// casa dos outros, por cima da parede.
	///
	/// A CENA E A PAREDE SUL DO BANCO DA TERRA, numa coluna sem porta: o corpo paira quatro celulas ao sul dela e e
	/// jogado pro norte, tres vezes.
	///   A. abaixo da resistencia: para na ultima amostra livre antes da parede, e ela fica de pe;
	///   B. o contra-exemplo (o defeito injetado do Core): o mesmo arremesso atravessa o Banco inteiro;
	///   C. com a forca da parede: ela CAI la de cima tambem, como cai rente ao chao (familia 11), e o corpo passa dela.
	/// O muro solto por cima do qual se continua passando e a familia 10, que nao mudou.
	/// ==================================================================================================================
	/// </summary>
	private void NoArAParedeDePredioPara()
	{
		GD.Print("[kb] -- 13) NO AR, a parede de PREDIO para o arremesso --");
		const int T = ZoneCollision.TileSize;
		const int Coluna = 76, Parede = 262;   // a parede sul do Banco; (76,263..266) e chao livre ao ar livre
		ServerPlayer d = Forjar("kbPredio", new Vec2(8 * T, 8 * T), 5_551);
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(d.Zone);

		if (mapa == null) { AfirmarKb("(montagem) a zona da bancada tem mapa de colisao", false, $"zona {d.Zone.Name}"); return; }

		bool cena = mapa.BlockedCell(Coluna, Parede) && CelulaInterna.SobTeto(mapa, Coluna, Parede, caiu: false)
					&& mapa.BloqueadaNoArquivo(Coluna, Parede) && !mapa.Indestrutivel(Coluna, Parede);
		for (int k = 1; k <= 4 && cena; k++)
			cena = mapa.ServeDeChao(Coluna, Parede + k) && !CelulaInterna.SobTeto(mapa, Coluna, Parede + k, caiu: false);
		AfirmarKb("(montagem) a parede sul do Banco barra, esta sob teto e cai; ao sul dela ha quatro celulas de chao ao ar livre",
				  cena, $"zona {d.Zone.Name}");
		if (!cena) return;

		var origem = new Vec2(Coluna * T + T / 2f, (Parede + 4) * T + T / 2f);
		float baseDaParede = (Parede + 1) * T;
		HashSet<(int X, int Y)> caidasAntes =
			_cenarioCaido.TryGetValue(d.Zone.Name, out HashSet<(int X, int Y)>? jaCaidas) ? [.. jaCaidas] : [];

		void Jogar(double forca)
		{
			// SO O MEU VOO -- o `TickDoEmpurrao` e de todos (o mesmo cuidado das familias 10 e 11).
			foreach (ServerPlayer o in TodosOsCorpos().ToList())
				if (o != d) { o.TiquesDeVoo = 0; o.TiquesIniciaisDoVoo = 0; }
			d.Pos = origem;
			d.Voando = true;
			d.Altitude = Voo.AlturaDePairar;
			d.Ficha.Ki = d.Ficha.MaxKi;
			d.Ficha.KO = false;
			Arremessar(d, new Vec2(0, -1), forca, 6);
			for (int i = 0; i < 90 && d.TiquesDeVoo > 0; i++) { TickDoEmpurrao(); TickDoVoo(d, Protocol.TickSeconds); }
		}
		// a borda de CIMA da caixa dos pes: e ela que encosta numa parede que fica ao norte
		float TopoDaCaixa() => d.Pos.Y + MoveRules.FeetOffsetY - MoveRules.BodyHalfH;

		try
		{
			AfirmarKb("(montagem) pairando, o corpo esta acima do cenario", Voo.AtravessaCenario(Voo.AlturaDePairar));

			// ---- A. abaixo da resistencia ----
			Jogar(0);
			AfirmarKb("VOANDO acima do cenario, o arremesso PARA na parede de predio: a caixa dos pes fica ao sul dela",
					  TopoDaCaixa() >= baseDaParede - 0.5f, $"topo da caixa em y {TopoDaCaixa():0.#}, a parede acaba em {baseDaParede:0}");
			AfirmarKb("...depois de ter voado ate ela (mais de duas celulas pro norte)",
					  origem.Y - d.Pos.Y > 2 * T, $"andou {origem.Y - d.Pos.Y:0} px");
			AfirmarKb("...a parede continua de pe (forca abaixo da resistencia)", mapa.BlockedCell(Coluna, Parede));
			AfirmarKb("...e a altura fica onde estava", Mathf.IsEqualApprox(d.Altitude, Voo.AlturaDePairar) && d.Voando,
					  $"altitude {d.Altitude:0}, voando {d.Voando}");

			// ---- B. o contra-exemplo: o mundo de antes ----
			ClasseDePredio.QuemVoaAtravessaDeTeste = true;
			try { Jogar(0); }
			finally { ClasseDePredio.QuemVoaAtravessaDeTeste = false; }
			AfirmarKb("(defeito injetado: parede de predio nao para quem voa) o mesmo arremesso passa POR CIMA da parede, pra dentro do Banco",
					  TopoDaCaixa() < Parede * T && mapa.BlockedCell(Coluna, Parede),
					  $"topo da caixa em y {TopoDaCaixa():0.#}, a parede comeca em {Parede * T}");

			// ---- C. com a forca da parede, ela cai -- la de cima tambem ----
			Jogar(Empurrao.ResistenciaPadrao);
			AfirmarKb("com a forca da parede ela CAI tambem pra quem vem pelo ar, e o corpo passa dela (`Movement Effects.dm:66-68`)",
					  !mapa.BlockedCell(Coluna, Parede) && TopoDaCaixa() < baseDaParede,
					  $"parede de pe={mapa.BlockedCell(Coluna, Parede)} | topo da caixa em y {TopoDaCaixa():0.#}");
		}
		finally
		{
			// A PAREDE VOLTA -- esta Terra e a de todo mundo (o mesmo `finally` da familia 11).
			if (_cenarioCaido.TryGetValue(d.Zone.Name, out HashSet<(int X, int Y)>? caidas))
				foreach ((int X, int Y) cel in caidas.Where(c => !caidasAntes.Contains(c)).ToList())
				{
					caidas.Remove(cel);
					mapa.Fechar(cel.X, cel.Y);
				}
			ClasseDePredio.QuemVoaAtravessaDeTeste = false;
			d.TiquesDeVoo = 0;
			d.Voando = false;
			d.Altitude = 0f;
		}
	}
}
