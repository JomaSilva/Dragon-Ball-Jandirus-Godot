using Godot;
using Jandirus.Core.Items;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// O INVENTARIO -- guardar, tirar, usar, e o pacote que leva isso pro dono.
///
/// ============================ O SERVIDOR E QUEM CARREGA ============================
/// A tela do inventario e do cliente, mas a mochila e daqui. Nao ha "usar item" que o cliente
/// resolva sozinho: ele manda o verbo, e este arquivo confere se o item existe na mochila, se a
/// acao pertence aquele item, e so entao aplica. Um cliente mexido que mandasse "comer Senzu" sem
/// ter Senzu nao come nada.
/// ===================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O INVENTARIO SO SAI QUANDO MUDA, como o corpo e os atributos. Ele muda em colheita e em
	/// consumo -- eventos raros --, entao a assinatura corta o trafego a praticamente zero.
	/// </summary>
	private static void MandarMochila(ServerPlayer pl, bool forcar = false)
	{
		string sig = string.Join(';', pl.Mochila.Pilhas.Select(p => $"{p.Id}x{p.Quantidade}"));
		if (!forcar && sig == pl.SigMochila) return;
		pl.SigMochila = sig;

		var w = Protocol.Begin(Protocol.S2C.Inventario);
		w.PutInventario(pl.Mochila);
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// GUARDA UM ITEM e avisa o dono. Devolve `false` quando nao coube.
	///
	/// O AVISO E DAQUI porque a mochila cheia e a coisa que o jogador precisa entender na hora: sem
	/// ela, colher numa mochila cheia seria um clique que nao faz nada.
	/// </summary>
	private bool Guardar(ServerPlayer pl, string item, int quantos = 1)
	{
		int sobrou = pl.Mochila.Guardar(item, quantos);
		MandarMochila(pl);

		if (sobrou <= 0) return true;
		Avisar(pl, $"sua mochila está cheia ({Inventario.Slots} espaços).");
		return false;
	}

	/// <summary>
	/// O CANAL DOS ITENS: `item_<acao>` com o id do item no argumento.
	///
	/// Duas conferencias, e as duas importam:
	///   * O ITEM ESTA NA MOCHILA? (senao qualquer um "come" o que nao tem)
	///   * A ACAO PERTENCE AO ITEM? (senao da pra "equipar" uma maca, e o que acontece depois disso
	///     depende de qual `case` cair primeiro -- que e como um bug vira um exploit)
	/// </summary>
	private bool ComandoDeItem(ServerPlayer pl, string cmd, string arg)
	{
		if (!cmd.StartsWith("item_", StringComparison.Ordinal)) return false;

		string acao = cmd[5..];

		// O ARGUMENTO CARREGA DUAS COISAS quando a acao pede um numero: o id e o valor, separados
		// por barra ("Weights/40"). O canal de verbos so tem um argumento, e criar um segundo campo
		// no pacote pra um caso seria mudar o protocolo por causa de um teclado numerico.
		int barra = arg.IndexOf('/');
		string arg2 = barra >= 0 ? arg[(barra + 1)..] : "";
		if (barra >= 0) arg = arg[..barra];

		ItemDef? def = CatalogoDeItens.Get(arg);

		if (def == null) { Avisar(pl, "isso não existe."); return true; }
		if (pl.Mochila.Quantos(def.Id) <= 0) { Avisar(pl, $"você não tem {def.Nome}."); return true; }

		// "largar" vale pra todo item e por isso nao esta no catalogo -- ver `ItemDef.AcoesDoItem`.
		if (acao != "largar" && Array.IndexOf(def.AcoesDoItem, acao) < 0)
		{
			Avisar(pl, $"não dá pra {acao} {def.Nome}.");
			return true;
		}

		switch (acao)
		{
			// A SEMENTE SENZU cura o corpo inteiro alem de alimentar (`Food.dm:38-48`), e tem o gesto de
			// acudir um caido (`:57-66`). Ver o lote G12.
			case "comer" when def.Id == CatalogoDeItens.Senzu: ComerSenzuG12(pl, def); break;
			case "acudir": AcudirComSenzuG12(pl, def); break;
			case "comer": ComerItem(pl, def); break;
			case "equipar": Equipar(pl, def); break;
			case "ajustar": AjustarPeso(pl, arg2); break;
			case "tirar": TirarPeso(pl); break;
			case "usar": UsarRemedio(pl, def); break;
			case "cavar": Cavar(pl, def); break;

			// O LIVRO DE ENSINAMENTOS -- `Study_Book` (`KiStatsModule.dm:190-203`). Ver o lote G13.
			case "ler": LerOsEnsinamentosG13(pl, def); break;

			// OS BRINCOS POTARA: o clique NAO funde -- ele OFERECE. Ver `OferecerOsBrincos`, e la
			// esta escrito por que o `checkEarringDist()` do DM (que funde a forca, sem perguntar)
			// nao foi portado.
			case "jogar":
				if (def.Id == CatalogoDeItens.BrincosPotara) OferecerOsBrincos(pl);
				else Avisar(pl, $"não dá pra jogar {def.Nome} em ninguém.");
				break;

			// LARGAR APAGA, por enquanto. O item cair no chao pede um sistema inteiro (objeto no
			// mundo, quem ve, quem pega, o que acontece no reinicio) que ainda nao existe -- e
			// prometer que a maca fica no chao e pior que dizer que ela se perde.
			case "largar":
				pl.Mochila.Tirar(def.Id);
				MandarMochila(pl);
				Avisar(pl, $"você joga fora {def.Nome}.");
				break;

			default: Avisar(pl, $"'{acao}' ainda não faz nada."); break;
		}
		return true;
	}

	// =====================================================================
	// EQUIPAR
	// =====================================================================
	/// <summary>
	/// LIGA OU DESLIGA UM EQUIPAMENTO. Por ora so o scouter -- e ele sozinho ja vale o caminho.
	///
	/// ============================ ELE ACENDE UM SISTEMA MORTO ============================
	/// O corte de leitura de BP existe no port desde sempre: `GameServer.Sigilo.cs` troca o poder
	/// de luta por `SemLeitura` pra quem nao tem o bit `Poder.Scouter`, e a tela desenha "???".
	/// Estava tudo ligado -- e nada nunca acendia o bit. O proprio comentario de la dizia: "hoje o
	/// bit nunca acende, o port ainda nao tem o item scouter".
	///
	/// O BIT VAI PRA `PoderesConcedidos`, e nao pra `Poderes`: este ultimo e refeito do zero a cada
	/// recalculo de skill (`AplicarPoderes`), e escrever ali seria escrever pra ser apagado na
	/// proxima skill aprendida. E a mesma armadilha que ja engoliu o bit de admin uma vez.
	/// ====================================================================================
	/// </summary>
	private void Equipar(ServerPlayer pl, ItemDef def)
	{
		if (def.Id != CatalogoDeItens.Scouter) { Avisar(pl, $"não dá pra equipar {def.Nome}."); return; }

		bool tinha = (pl.PoderesConcedidos & Protocol.Poder.Scouter) != 0;
		if (tinha) pl.PoderesConcedidos &= ~Protocol.Poder.Scouter;
		else pl.PoderesConcedidos |= Protocol.Poder.Scouter;

		AplicarPoderes(pl);
		MandarAtributos(pl);

		// ============================ A FICHA TEM QUE IR JUNTO, E ISSO ERA UM BUG ============================
		// Achado pela bancada `--diagbancada`: equipar o scouter acendia o bit, o cliente recebia os
		// atributos novos... e continuava com `BP = NaN` na ficha. A aba Stats, que ja entrava no ramo
		// "tem scouter", passava a imprimir **"NaN (base NaN)"**, e a HUD seguia em "???".
		//
		// A CAUSA E DA MESMA FAMILIA do bug da barra de Ki, so que um passo mais fundo: o `TickFichas`
		// so reenvia a ficha quando algum campo COMPARADO muda, e ele compara os campos CRUS do
		// `Fighter` (`EnvBP == Ficha.expressedBP`...). O que sai no fio, porem, passa pelo
		// `FichaVisivel`, que troca BP e BP expresso por NaN pra quem nao tem scouter. Ligar o aparelho
		// muda o CONTEUDO ENVIADO sem mexer em um unico campo comparado -- entao, pro tique, nao mudou
		// nada, e o cliente ficava com a ficha censurada ate que outra coisa qualquer se mexesse.
		//
		// Era intermitente por isso: num corpo carregando Ki a ficha ia junto no tique seguinte e
		// ninguem via; num corpo parado ela nao ia. Nao da pra "consertar no tique" sem por o bit de
		// sigilo na lista de comparacao -- e a casa certa e esta, que e onde o bit muda.
		// ================================================================================================
		MandarFicha(pl);

		Avisar(pl, tinha
			? "você tira o scouter. Os números voltam a ser \"???\"."
			: "o scouter liga com um bipe. Agora você lê o poder de quem olha.");
	}

	// =====================================================================
	// PESO -- o `Weights` do original
	// =====================================================================
	/// <summary>
	/// O TETO DE PESO, como fracao do proprio corpo. O original pede um numero de 0 a 1.
	///
	/// Aqui ele e dito em PORCENTAGEM porque o teclado numerico so tem inteiros -- e porque "40" e
	/// mais facil de pensar que "0,4" quando se esta escolhendo o quanto sofrer.
	/// </summary>
	private const double PesoMaximoPct = 100;

	/// <summary>
	/// ============================ O PESO ESTAVA EM UNIDADE ERRADA, E ISSO O MATAVA ============================
	/// No DM, `Weighted` e o peso da roupa em **KG**, e o teto que o Upgrade destrava e
	/// `weight_cap_hw * WEIGHT_ITEM_CAP_MULT` (`Tier 1.dm:145`) -- ou seja **duas vezes** o limite do
	/// corpo. O `Change_Weight` escolhe uma FRACAO desse teto (`pounds = pounds_max * frac`), e a
	/// razao que sai disso (`weight_ratio = Weighted * grav / weight_cap_hw`) vai de 0 a 2 no chao da
	/// Terra: 50% = razao 1 (o limite, ganho 2x), 100% = razao 2 (o dobro, ganho 4x e ja esmagando).
	///
	/// O port guardava a FRACAO direto em `Weighted` (0 a 1). Como `weight_cap_hw` vale
	/// `expressedBP x Ephysoff x 20` -- centenas ja num personagem novo, bilhoes num veterano --, a
	/// razao dava praticamente ZERO, e o `WeightTick` devolvia `weight = 1` sempre. Consequencias, e
	/// as duas eram mudas:
	///
	///   * o multiplicador de ganho por peso (ate 8x) **nunca saia de 1**;
	///   * o `weight = 1 + Weighted` escrito logo abaixo desta linha durava ate o proximo tique de
	///     ficha (200 ms), que e quando o `WeightTick` o reescrevia -- o bonus aparecia e sumia.
	///
	/// Ou seja o sistema de peso inteiro estava morto, e nao "sem penalidade": ele nao dava nem o
	/// premio. Nada disso aparecia em teste porque nada reclamava -- e o numero na tela (13.4 item 5)
	/// e justamente o que teria mostrado o 1x parado.
	/// ======================================================================================================
	/// </summary>
	private static double TetoDePeso(Jandirus.Core.Stats.Fighter f) =>
		Math.Max(f.weight_cap_hw, 1) * Jandirus.Core.Stats.GainKnobs.WeightItemCapMult;

	private void AjustarPeso(ServerPlayer pl, string valor)
	{
		if (!double.TryParse(valor, out double pct)) { Avisar(pl, "número inválido."); return; }
		pct = Math.Clamp(pct, 0, PesoMaximoPct);

		// `Weighted` E O QUANTO SE VESTE (em kg, na escala do `weight_cap_hw` -- ver `TetoDePeso`) e
		// `weight` e o multiplicador que sai disso. Sao dois campos porque o segundo entra na conta de
		// poder (`deBuff`) e o primeiro e a escolha do jogador -- juntar os dois faria tirar o peso
		// exigir desfazer uma conta.
		//
		// O `weight` NAO E MAIS ESCRITO A MAO AQUI. Ele era `1 + Weighted`, um palpite que o
		// `WeightTick` reescrevia no tique seguinte -- duas contas pro mesmo campo, e a que o jogador
		// via era a que morria. Quem escreve `weight` e o `WeightTick`, tres linhas abaixo, e so ele.
		pl.Ficha.Weighted = pct / 100.0 * TetoDePeso(pl.Ficha);

		// ============================ O TIQUE INTEIRO, E NAO `Statify` + `WeightTick` ============================
		// "Cada passo pesa" era uma frase VAZIA ate a camada 2: o peso nao custava nada. Agora custa
		// (`Esmagamento.FatorDePasso`), e o custo tem que valer no mesmo instante em que o jogador
		// aperta o botao -- senao ele ajusta o peso, sai andando na velocidade antiga, e o servidor
		// corrige trinta vezes por segundo.
		//
		// **A ORDEM E A DO `Fighter.Tick` E TEM QUE SER ELA.** Chamar `Statify` + `WeightTick` pulando
		// o `PowerLevel` no meio parece equivalente e nao e: o `weight_cap_hw` e um recorde que sobe
		// com `expressedBP x (weight x BPrestriction)`, e esse produto so se cancela porque o
		// `expressedBP` ja veio DIVIDIDO pelo peso anterior. Com o `expressedBP` velho, o recorde subia
		// 4x a cada ajuste e o peso novo virava metade do anterior -- vestir mais peso pesava MENOS. A
		// bancada pegou isso na segunda troca (50% e depois 100%), que e o gesto mais comum que existe.
		// ====================================================================================================
		pl.Ficha.Tick(agoraMs: NowMs());
		RecalcularVelocidade(pl);
		MandarFicha(pl);
		pl.SigAtributos = "";

		if (pct <= 0) { Avisar(pl, "você tira os pesos."); return; }

		// A FRASE DIZ OS TRES NUMEROS DA DECISAO, e e o mesmo trio que o painel de peso do DM mostra
		// (`HtmlUI.dm:641-655`): quanto se vestiu, quanto isso e do limite DO CORPO NESTA GRAVIDADE, e
		// quanto rende. Sem o do meio o jogador nao teria como saber que 40% na Terra e 40% num chao
		// 10x mais pesado sao coisas completamente diferentes -- o `weight_ratio` multiplica a
		// gravidade local, e e essa multiplicacao que vira panqueca.
		double r = Jandirus.Core.Stats.Esmagamento.Razao(pl.Ficha);
		Avisar(pl, $"você veste {pct:0}% do peso máximo: {r:0.##}x o que seu corpo aguenta nesta "
				   + $"gravidade, e {pl.Ficha.weight:0.##}x de ganho no treino.");

		// E O AVISO DE QUE PASSOU DO PONTO. O peso esmaga pela MESMA regra da gravidade (a pior das
		// duas razoes manda), entao vestir demais nao e "mais lento": e perder vida por segundo, e
		// acima de 4x e ficar preso no chao sem conseguir tirar os pesos andando ate alguem. Descobrir
		// isso pela barra de vida caindo seria uma pegadinha.
		if (r > 1)
			Avisar(pl, r >= Jandirus.Core.Stats.Esmagamento.RazaoQuePrende
				? "ATENÇÃO: você não consegue nem andar com isso, e vai perder vida enquanto estiver assim."
				: "seu corpo range sob o peso: você vai perder vida e velocidade enquanto estiver assim.");
	}

	private void TirarPeso(ServerPlayer pl) => AjustarPeso(pl, "0");

	// =====================================================================
	// CURATIVO
	// =====================================================================
	/// <summary>Quanto cada item devolve de vida, espalhado pelo corpo.</summary>
	private const double CuraDaBandagem = 8;
	private const double CuraDoKit = 25;

	/// <summary>
	/// CURA FORA DE COMBATE. A trava e a mesma da regeneracao passiva, e pelo mesmo motivo: um kit
	/// usado no meio da briga faria a luta nao acabar nunca.
	/// </summary>
	private void UsarRemedio(ServerPlayer pl, ItemDef def)
	{
		// ============================ O RADAR ENTRA PELA MESMA PORTA "usar", E SAI ANTES ============================
		// Ele nao e remedio, e por isso o desvio e a primeira linha: as tres guardas abaixo (estar em
		// combate, ter o que tratar, ter um `Combate`) recusariam o radar por motivos que nao tem nada
		// a ver com ele -- e a recusa que o jogador leria seria "voce nao tem o que tratar".
		//
		// A alternativa era um `case "localizar"` proprio no `ComandoDeItem`. Nao vale: "usar" e o
		// verbo que o menu do inventario ja oferece, e um verbo novo pra um item so seria um botao a
		// mais numa tela que ja tem os botoes certos. Ver `GameServer.Esferas.UsarORadar`.
		// ======================================================================================================
		if (def.Id == CatalogoDeItens.Radar) { UsarORadar(pl); return; }

		if (pl.Combate is not { } c) return;
		if (c.EmCombate > 0) { Avisar(pl, "não dá pra se tratar no meio de uma briga."); return; }
		if (pl.Ficha.HP >= 99.99) { Avisar(pl, "você não tem o que tratar."); return; }

		double cura = def.Id == CatalogoDeItens.KitMedico ? CuraDoKit : CuraDaBandagem;
		c.Corpo.Curar(cura);
		c.SincronizarVida();

		// O KIT TEM DEZ USOS no original, e a bandagem e consumida de uma vez. Sem um campo de
		// "usos restantes" por item (a pilha guarda quantidade, nao estado), o kit e tratado como
		// dez bandagens: cada uso tira um da pilha. Anotado -- o dia em que um item precisar de
		// estado proprio, a pilha vira lista de instancias.
		pl.Mochila.Tirar(def.Id);
		MandarMochila(pl);
		Avisar(pl, $"você usa {def.Nome}.");
	}

	// =====================================================================
	// CAVAR -- Pa e Furadeira
	// =====================================================================
	/// <summary>Quanto tempo entre duas cavadas, por ferramenta.</summary>
	private const long MsDaPa = 6_000;
	private const long MsDaFuradeira = 2_500;

	private readonly Dictionary<int, long> _proximaCavada = [];

	/// <summary>
	/// CAVAR RENDE ZENI E TECNOLOGIA -- o `Shovel`/`Hand_Drill` do original (Tier 1.dm:254-331).
	///
	/// O GANHO PASSA PELO `techmod`, como la: quem nasceu esperto tira mais do mesmo buraco. E a
	/// unica fonte de zeni do jogo que nao depende de bater em ninguem nem de ter um banco por
	/// perto -- e por isso ela e lenta.
	/// </summary>
	private void Cavar(ServerPlayer pl, ItemDef def)
	{
		if (pl.Ficha.KO || pl.Ficha.dead) return;

		long agora = NowMs();
		if (_proximaCavada.TryGetValue(pl.Id, out long livre) && agora < livre)
		{
			Avisar(pl, "espere um pouco antes de cavar de novo.");
			return;
		}

		bool furadeira = def.Id == CatalogoDeItens.Furadeira;
		_proximaCavada[pl.Id] = agora + (furadeira ? MsDaFuradeira : MsDaPa);

		double mod = Math.Max(pl.Ficha.techmod, 1);
		double zeni = Math.Round(mod * (furadeira ? 3 : 1) * (1 + _rng.NextDouble()));
		pl.Ficha.Zeni += zeni;

		// UM EM CADA CINCO cava alguma coisa que ENSINA. E o `prob(20)` do original, e ele existe
		// pra cavar nao virar uma segunda bancada de pesquisa: o caminho da tecnologia continua
		// sendo estudar.
		string extra = "";
		if (_rng.NextDouble() < 0.20)
		{
			pl.Ficha.Estudar(mod * 4);
			pl.SigAtributos = "";
			extra = " Você desenterra algo curioso e aprende com isso.";
		}

		Avisar(pl, $"você cava e encontra {zeni:N0} zeni.{extra}");
		MandarCatalogoDeObras(pl);
	}

	private void ComerItem(ServerPlayer pl, ItemDef def)
	{
		if (def.Nutricao <= 0) { Avisar(pl, $"{def.Nome} não é comida."); return; }

		// CHEIO NAO COME -- e a comida fica na mochila: a refeicao recusada nao e consumida.
		Jandirus.Core.Stats.Nutricao.Refeicao r = Jandirus.Core.Stats.Nutricao.Comer(pl.Ficha, def.Nutricao);
		if (!r.Comeu) { Avisar(pl, r.Aviso); return; }

		pl.Mochila.Tirar(def.Id);
		MandarMochila(pl);

		Avisar(pl, $"você come {def.Nome}.");
		if (r.Aviso.Length > 0) Avisar(pl, r.Aviso);
	}
}
