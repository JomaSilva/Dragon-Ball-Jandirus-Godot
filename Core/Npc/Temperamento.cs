using Jandirus.Core.Ai;

namespace Jandirus.Core.Npc;

/// <summary>
/// DA FICHA PRO CEREBRO -- os quatro numeros de temperamento do molde viram as manivelas do
/// <see cref="Cerebro"/>.
///
/// ============================ ELES ESTAVAM NO ARQUIVO E NAO CHEGAVAM A NINGUEM ============================
/// `inteligencia`, `agressividade`, `coragem` e `furia` ja existiam no `npcs.json`, ja eram lidos
/// pelo `CatalogoDeMoldes.Parse` e ja tinham comentario citando o `behavior_vals` do DM -- e
/// **nenhum consumidor**. Eram quatro campos que o dono podia editar o dia inteiro sem nada mudar
/// em jogo: exatamente a armadilha 1 da PARTE 3 ("regra escrita nao e regra ligada"), na forma mais
/// silenciosa dela, porque um dado inerte nem sequer quebra.
/// ======================================================================================================
///
/// ============================ AS ESCALAS FORAM ESCOLHIDAS PRA O 50 SER O PADRAO ============================
/// Cada conversao passa exatamente pelo default que o <see cref="Cerebro"/> ja tinha quando o campo
/// vale 50 (e 35 no caso da inteligencia, que e o `NPC_AI_INT_DEFAULT` literal). Isso nao e enfeite:
/// significa que ligar este mapeamento **nao muda o comportamento de nenhum corpo que ja existia** --
/// o clone da mente, a fera e a furia lendaria continuam com os cerebros que eles proprios montam --,
/// e que um molde omisso se comporta como o cerebro de fabrica. Uma escala que nao passasse pelo
/// default teria mexido na IA inteira junto com o povoamento, e nao daria pra saber qual dos dois
/// mudou o que.
/// ======================================================================================================
/// </summary>
public static class Temperamento
{
	/// <summary>
	/// O CEREBRO DESTE MOLDE. Chamado no nascimento, uma vez por corpo.
	///
	/// `faseDasCapacidades` DESFASA a leitura cara de 1 Hz -- ver
	/// <see cref="Cerebro.DesfasarCapacidades"/> e o comentario la, que explica por que um numero
	/// por corpo importa mais aqui do que em qualquer outro lugar.
	/// </summary>
	public static Cerebro Montar(MoldeDeNpc molde, double faseDasCapacidades, ulong semente = 0)
	{
		// ============================ DOIS SAIBAMEN DO MESMO MOLDE NAO SAO O MESMO BICHO ============================
		// **LITERAL**: `behavior_vals[a] *= (rand(8,13) / 10)` (`NPCAI.dm:816`), com o comentario do
		// autor explicando a calibragem -- *"mild personality variation (~0.8x-1.3x); was 0.1x-10x
		// which turned ~half the mobs into cowards that flee to the corner"*.
		//
		// E o que impede o exercito de clones: o molde diz o que a ESPECIE e, e o sorteio diz quem
		// ESTE corpo e. Sem ele, quarenta cidadaos da Terra recuam na mesma fracao de vida, socam
		// com o mesmo peso e mudam de ideia no mesmo instante -- e nada denuncia mais um NPC do que
		// o vizinho dele fazer exatamente a mesma coisa.
		//
		// O sorteio e por TRACO (quatro numeros independentes) e nao um fator so pro bicho inteiro:
		// senao a variacao daria "bicho bom" e "bicho ruim" em vez de covarde-e-furioso,
		// corajoso-e-frio. E ele e DETERMINISTICO na semente do corpo, como todo o resto do sorteio
		// de NPC -- o mesmo saibaman renasce o mesmo saibaman.
		//
		// SEMENTE ZERO = SEM VARIACAO, e as bancadas contam com isso: quem quer medir a receita
		// (e nao o sorteio) pede o molde cru e recebe os numeros do arquivo, sem tempero por cima.
		// ======================================================================================================
		double intel = Fracao(molde.Inteligencia * Tempero(semente, 4));

		var c = new Cerebro
		{
			// `ai_intelligence` (NPCAI.dm:57), o gate `prob(ai_intelligence)` de todas as decisoes
			// "espertas" (economia de Ki, recarga, rasante). 35 = `NPC_AI_INT_DEFAULT`.
			Inteligencia = intel,

			// A DISCIPLINA SAI DA MESMA FONTE, e o proprio cabecalho do `Cerebro` ja dizia por que: o
			// gate da guarda no DM e `e_behavior_vals[4] >= 35` (NPCAI.dm:291) -- a LOGICA --, e o
			// default 0,35 daquele campo foi escrito como sendo o `NPC_AI_INT_DEFAULT`. Sao o mesmo
			// numero no original; separa-los aqui inventaria uma quinta manivela que o `npcs.json`
			// nao tem como preencher.
			Disciplina = intel,

			// CORAGEM E O AVESSO DA CAUTELA. `courage determines when the mob flees` (NPCAI.dm:79).
			// 0 -> 0,90 (foge cedo); 50 -> 0,45 (o default); 100 -> 0 (nunca recua, o valor que a
			// fera e a furia lendaria escrevem na mao).
			VidaCautelosa = 0.9 * (1 - Fracao(molde.Coragem * Tempero(semente, 1))),

			// FURIA E O PESO DO GOLPE. `rage ... will cause strength increases` e o comentario do
			// Citizen -- "high rage (barrages)" (PlanetPopulation.dm:73). 50 -> 0,35 (o default).
			ChanceDePesado = 0.10 + 0.5 * Fracao(molde.Furia * Tempero(semente, 2)),

			// AGRESSIVIDADE E A CADENCIA DA DECISAO -- e ela tem original: o `chase_speed`, que e por
			// mob e que o Citizen BAIXA de 3 pra 2 exatamente por ser agressivo, com o comentario
			// *"snappier reactions & movement than the default"* (PlanetPopulation.dm:75).
			// 3 tiques do DM = 0,30 s, 2 = 0,20 s, e o meio da escala da os 0,25 s de fabrica.
			//
			IntervaloDeDecisao = 0.30 - 0.10 * Fracao(molde.Agressividade),

			// ============================ E O SEGUNDO USO DO MESMO CAMPO, QUE ESTAVA PROMETIDO AQUI ============================
			// O comentario acima dizia, palavra por palavra: *"o `ai_aggression` do DM gateia o MODO
			// FINALIZADOR, que este cerebro ainda nao tem. No dia em que ele existir, e daqui que sai
			// o gate -- nao de um campo novo"*. O modo existe (`Cerebro.Agressividade`), e o gate
			// saiu daqui: **nenhum campo novo no `npcs.json`**, e o que o dono ja editava passa a
			// valer nas duas pontas -- a cadencia da decisao e a decisao de fechar a luta.
			//
			// Nao ha tempero de sorteio nesta: no DM ela nao e um `behavior_val` (nao entra no
			// `rand(8,13)/10` do `bhv_set`), e sim um knob por mob ao lado da inteligencia.
			// ==========================================================================================================
			Agressividade = Fracao(molde.Agressividade),
		};

		c.DesfasarCapacidades(faseDasCapacidades);
		return c;
	}

	/// <summary>0..100 do arquivo vira 0..1, com o arquivo podendo mentir sem derrubar nada.</summary>
	private static double Fracao(double zeroACem) => Math.Clamp(zeroACem / 100.0, 0, 1);

	/// <summary>
	/// O TEMPERO DESTE CORPO NESTE TRACO: 0,8 a 1,3 (o `rand(8,13)/10` do DM), deterministico na
	/// semente. Semente zero devolve 1 -- ver o cabecalho do <see cref="Montar"/>.
	///
	/// O misturador e o mesmo splitmix64 que o sorteio de NPC usa pra desempatar skill: uma conta
	/// barata e sem estado, que da o mesmo numero pro mesmo corpo pra sempre.
	/// </summary>
	private static double Tempero(ulong semente, int traco)
	{
		if (semente == 0) return 1;

		ulong h = semente * 0x9E3779B97F4A7C15UL ^ (ulong)traco * 0xBF58476D1CE4E5B9UL;
		h ^= h >> 30; h *= 0xBF58476D1CE4E5B9UL;
		h ^= h >> 27; h *= 0x94D049BB133111EBUL;
		h ^= h >> 31;

		return (8 + h % 6) / 10.0;   // 0,8 .. 1,3, os seis valores inteiros do `rand(8,13)`
	}
}
