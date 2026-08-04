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
			case "comer": ComerItem(pl, def); break;
			case "equipar": Equipar(pl, def); break;
			case "ajustar": AjustarPeso(pl, arg2); break;
			case "tirar": TirarPeso(pl); break;
			case "usar": UsarRemedio(pl, def); break;
			case "cavar": Cavar(pl, def); break;

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

	private void AjustarPeso(ServerPlayer pl, string valor)
	{
		if (!double.TryParse(valor, out double pct)) { Avisar(pl, "número inválido."); return; }
		pct = Math.Clamp(pct, 0, PesoMaximoPct);

		// `Weighted` E O QUANTO SE VESTE e `weight` e o DEBUFF que sai disso. Sao dois campos porque
		// o segundo entra na conta de poder (`deBuff`) e o primeiro e a escolha do jogador -- juntar
		// os dois faria tirar o peso exigir desfazer uma conta.
		pl.Ficha.Weighted = pct / 100.0;
		pl.Ficha.weight = 1 + pl.Ficha.Weighted;
		pl.Ficha.Statify();
		pl.SigAtributos = "";

		Avisar(pl, pct <= 0
			? "você tira os pesos."
			: $"você ajusta os pesos para {pct:0}% do próprio corpo. Cada passo pesa -- e cada treino rende mais.");
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

		pl.Mochila.Tirar(def.Id);
		MandarMochila(pl);

		string demais = Jandirus.Core.Stats.Nutricao.Comer(pl.Ficha, def.Nutricao);
		Avisar(pl, $"você come {def.Nome}.");
		if (demais.Length > 0) Avisar(pl, demais);
	}
}
