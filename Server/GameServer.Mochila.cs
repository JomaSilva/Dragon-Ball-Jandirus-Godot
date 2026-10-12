using Godot;
using Jandirus.Core.Economia;
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
		// O QUE SE VESTE VAI NA ASSINATURA E NO PACOTE: a mochila mostra a fileira "vestindo" (ver
		// `RoupaGuardada`), e tirar uma peca com a mochila ja igual a de antes nao pode ser um pacote engolido.
		string sig = string.Join(';', pl.Mochila.Pilhas.Select(p => $"{p.Id}x{p.Quantidade}"))
					 + "|" + string.Join(';', pl.Visual.Roupa.Select(p => $"{p.Caminho}#{p.Cor}"));
		if (!forcar && sig == pl.SigMochila) return;
		pl.SigMochila = sig;

		var w = Protocol.Begin(Protocol.S2C.Inventario);
		w.PutInventario(pl.Mochila, pl.Visual.Roupa);
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

		// TIRAR UMA PECA DO CORPO sai ANTES das duas conferencias de baixo: a peca vestida nao esta na mochila
		// (ver `RoupaGuardada`), entao "voce nao tem" seria a resposta errada pra quem a esta vestindo. Quem
		// confere se ela esta mesmo no corpo e o proprio `TirarRoupa`.
		if (acao == RoupaGuardada.AcaoTirar) { TirarRoupa(pl, arg); return true; }

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
			case "melhorar": MelhorarOsPesos(pl, arg2); break;
			// A ARMADURA CRUA GANHA A ARTE que o jogador escolheu na tela (dono, 2026-10-12). Ver `MoldarAArmadura`.
			case ArmaduraDeTech.AcaoMoldar: MoldarAArmadura(pl, arg2); break;
			case "tirar": TirarPeso(pl); break;
			case "usar": UsarRemedio(pl, def); break;

			// "cavar" NAO ESTA AQUI, e de proposito: a acao da pa e da furadeira SAIU (dono, 2026-10-10: *"tire a
			// opcao de cavar da pa e furadeira pq e redundante"* -- o que elas fazem agora e acelerar o verb Minerar,
			// ver `GameServer.Recursos.cs`). Um cliente antigo que ainda mande `item_cavar` para no portao la em
			// cima ("nao da pra cavar ..."): nenhuma das duas declara a acao (`CatalogoDeItens`).

			// O LIVRO DE ENSINAMENTOS -- `Study_Book` (`KiStatsModule.dm:190-203`). Ver o lote G13.
			case "ler": LerOsEnsinamentosG13(pl, def); break;

			// A PECA DE ROUPA GUARDADA volta pro corpo. Ver `VestirRoupa`.
			case RoupaGuardada.AcaoVestir: VestirRoupa(pl, def); break;

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
	// ROUPA -- o que se veste e o que se guarda (dono, 2026-10-08)
	// =====================================================================
	/// <summary>
	/// DA PRA MEXER NA ROUPA AGORA? Nao enquanto o que o mundo ve nao e o guarda-roupa de quem joga: fundido, o
	/// corpo veste a roupa DA FUSAO (`LookDeFusao`); disfarcado, a de quem ele imita (`Disfarce`). Trocar a peca
	/// de verdade por baixo disso seria um clique que nao muda nada na tela -- e que so apareceria na separacao.
	/// </summary>
	private static bool PodeMexerNaRoupa(ServerPlayer pl, out string porque)
	{
		porque = pl.LookDeFusao != null ? "fundido, o corpo veste a roupa da fusão -- espere a fusão acabar."
			: pl.Disfarce != null ? "disfarçado, o que você veste é a roupa de quem você imita."
			: "";
		return porque.Length == 0;
	}

	/// <summary>
	/// A PECA SAI DA MOCHILA E VAI PRO CORPO. As recusas, na ordem em que o jogador as entenderia: a arte nao
	/// existe mais, o corpo nao e o dele agora, ele ja veste a mesma folha, ou as quatro vagas estao ocupadas
	/// (`Appearance.MaxRoupa`, o `WARDROBE_MAX` do `Wardrobe.dm:15`).
	/// </summary>
	private void VestirRoupa(ServerPlayer pl, ItemDef def)
	{
		if (RoupaGuardada.Ler(def.Id)?.Peca(_visual) is not { } peca) { Avisar(pl, $"não dá pra vestir {def.Nome}."); return; }
		if (!PodeMexerNaRoupa(pl, out string porque)) { Avisar(pl, porque); return; }

		// UMA FOLHA SO UMA VEZ NO CORPO: duas camadas do mesmo desenho, uma por cima da outra, sao uma peca so na
		// tela -- e o `VisualCatalog.Sanear` do proximo login jogaria a segunda fora, calado, com a cor dela junto.
		if (pl.Visual.Roupa.Any(p => p.Caminho == peca.Caminho))
		{ Avisar(pl, $"você já veste {def.Nome}: tire a que está no corpo antes."); return; }
		if (pl.Visual.Roupa.Count >= Jandirus.Core.Appearance.Appearance.MaxRoupa)
		{ Avisar(pl, $"você já veste {Jandirus.Core.Appearance.Appearance.MaxRoupa} peças: tire uma antes."); return; }

		// UMA ARMADURA POR CORPO -- o "You already have armor equipped." do `Equip()` (`Armor.dm:21-23`).
		bool armadura = _visual?.EhArmadura(peca.Caminho) == true;
		if (armadura && pl.Visual.Roupa.Exists(p => _visual!.EhArmadura(p.Caminho)))
		{ Avisar(pl, "você já veste uma armadura: tire a que está no corpo antes."); return; }

		if (pl.Mochila.Tirar(def.Id) <= 0) return;
		pl.Visual.Roupa.Add(peca);
		ReapresentarAparencia(pl);
		MandarMochila(pl);
		Avisar(pl, armadura ? $"você veste a armadura ({def.Nome}): ela protege o corpo contra golpes físicos."
							: $"você veste {def.Nome}.");
	}

	/// <summary>
	/// ESTE CORPO VESTE UMA ARMADURA QUE O PROTEGE? -- quem escreve o `Fighter.armadura`, a cada tique de ficha.
	///
	/// A ARMADURA E UMA PECA DE ROUPA cuja folha esta na lista de armaduras do catalogo (`VisualCatalog.EhArmadura`),
	/// e quem ela protege e o corpo SEM cerebro de IA: no DM o NPC a veste como desenho, sem defesa nenhuma
	/// (`npc_wear_armor_icon`, PlanetPopulation.dm:246-252), e a que se rouba dele protege o ladrao -- ver
	/// `Core/Items/ArmaduraDeTech.cs`.
	/// </summary>
	private bool VesteArmadura(ServerPlayer pl) =>
		_visual != null && pl.Cerebro == null && pl.Visual.Roupa.Exists(p => _visual.EhArmadura(p.Caminho));

	/// <summary>Tira da roupa de uma ficha de CRIACAO toda peca de armadura, e diz quantas tirou. Ver `ReceberCriacao`.</summary>
	private int TirarArmaduraDaCriacao(Jandirus.Core.Appearance.Appearance visual) =>
		_visual is { } catalogo ? visual.Roupa.RemoveAll(p => catalogo.EhArmadura(p.Caminho)) : 0;

	/// <summary>
	/// A ARMADURA RECEM-FABRICADA GANHA A ARTE -- o `verb/Icon()` do original (`Armor.dm:67-125`), na hora de fabricar:
	/// *"ao fabricar uma armadura da tech, vai abrir uma tela pra vc escolher qual sprite vc quer"*. O item cru
	/// (`ArmaduraDeTech.Id`) sai da mochila e a PECA de armadura (`Armadura|nome|`) entra no lugar dele; dali em
	/// diante ela se veste, se tira e se rouba como toda roupa.
	///
	/// A lista e a da pasta `Clothes/Armor` (o catalogo), e nao os estilos por nivel de tecnologia do DM: quem ja
	/// fabricou a armadura escolhe qualquer uma. So a do Beerus fica de fora (`ArmaduraDeTech.Escolhivel`).
	/// </summary>
	private void MoldarAArmadura(ServerPlayer pl, string arte)
	{
		if (_visual == null || !ArmaduraDeTech.Escolhivel(arte) || _visual.PecaDeArmadura(arte) is not { } caminho)
		{ Avisar(pl, "essa arte de armadura não existe."); return; }

		// (o `ComandoDeItem` ja conferiu que o item cru esta na mochila; o slot que ele deixa e o que a peca ocupa)
		if (pl.Mochila.Tirar(ArmaduraDeTech.Id) <= 0) return;
		var peca = RoupaGuardada.De(new Jandirus.Core.Appearance.PecaDeRoupa(caminho, null));
		pl.Mochila.Guardar(peca.Id);
		MandarMochila(pl);
		Avisar(pl, $"você molda a armadura: {peca.Nome}. Vista pela mochila (tecla I).");
	}

	/// <summary>
	/// A PECA SAI DO CORPO E VAI PRA MOCHILA -- o `id` e o de <see cref="RoupaGuardada"/> (a folha e a cor), que e
	/// como a tela aponta qual das pecas vestidas e. Mochila cheia recusa ANTES de despir: a peca nao tem pra onde
	/// ir, e roupa que some ao tirar e o defeito que este metodo existe pra nao ter.
	/// </summary>
	private void TirarRoupa(ServerPlayer pl, string id)
	{
		int i = pl.Visual.Roupa.FindIndex(p => RoupaGuardada.De(p).Id == id);
		if (i < 0) { Avisar(pl, "você não está vestindo isso."); return; }
		if (!PodeMexerNaRoupa(pl, out string porque)) { Avisar(pl, porque); return; }

		// GUARDA PRIMEIRO, DESPE DEPOIS: se a peca nao coube (mochila cheia) ela continua no corpo.
		if (!RoupaGuardada.TiradaSomeDeTeste && pl.Mochila.Guardar(id) > 0)
		{ Avisar(pl, $"sua mochila está cheia ({Inventario.Slots} espaços): não há onde guardar a peça."); return; }

		string nome = RoupaGuardada.De(pl.Visual.Roupa[i]).Nome;
		pl.Visual.Roupa.RemoveAt(i);
		ReapresentarAparencia(pl);
		MandarMochila(pl);
		Avisar(pl, $"você tira {nome} e guarda na mochila.");
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
	// ============================ (2026-10-12) O TETO E O QUE FOI PAGO ============================
	// Ate aqui o teto era o do DM: duas vezes o limite do corpo, destravado de graca (`Tier 1.dm:145`). O dono: os
	// pesos vem com 50 kg e o maximo se COMPRA -- `PesoDeTreino`. O limite do corpo continua onde estava (o
	// `weight_ratio` do `WeightTick` e o `Esmagamento`): vestir mais do que ele aguenta esmaga, como sempre.
	// ==============================================================================================
	private static double TetoDePeso(Jandirus.Core.Stats.Fighter f) =>
		PesoDeTreino.TetoDoCorpoDeGracaDeTeste
			? Math.Max(f.weight_cap_hw, 1) * Jandirus.Core.Stats.GainKnobs.WeightItemCapMult
			: PesoDeTreino.Maximo(f.pesoMaximoKg);

	/// <summary>
	/// A MELHORIA PAGA DOS PESOS: o maximo dobra, por zeni ou (o dobro) por recursos. Quem cobra e o contrato de toda
	/// melhoria de maquina (`CobrarOuExplicar`). O que se VESTE nao muda sozinho: continua a mesma quantidade de kg, e
	/// quem quiser mais ajusta a porcentagem.
	/// </summary>
	private void MelhorarOsPesos(ServerPlayer pl, string comQue)
	{
		Moeda moeda = string.Equals(comQue, Recurso.PalavraDeRecursos, StringComparison.OrdinalIgnoreCase) ? Moeda.Recursos : Moeda.Zeni;
		double antes = PesoDeTreino.Maximo(pl.Ficha.pesoMaximoKg);
		double custo = PesoDeTreino.PrecoEmZeni(pl.Ficha.pesoMaximoKg);
		if (!CobrarOuExplicar(pl, custo, moeda, "melhorar os pesos")) return;

		pl.Ficha.pesoMaximoKg = PesoDeTreino.ProximoMaximo(pl.Ficha.pesoMaximoKg);
		pl.SigAtributos = "";
		Avisar(pl, $"você melhora os pesos por {Recurso.PrecoEscrito(custo, moeda)}: o peso máximo vai de {antes:N0} kg pra "
				   + $"{pl.Ficha.pesoMaximoKg:N0} kg. A próxima melhoria custa {Recurso.PrecoEscrito(PesoDeTreino.PrecoEmZeni(pl.Ficha.pesoMaximoKg), Moeda.Zeni)}. "
				   + $"Ajuste quanto vestir pela mochila. Restam {CarteiraEscrita(pl)}.");
	}

	private void AjustarPeso(ServerPlayer pl, string valor)
	{
		if (!double.TryParse(valor, out double pct) || !NumeroFinito(pct)) { Avisar(pl, "número inválido."); return; }
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
		//
		// (2026-10-12) O DO MEIO VIROU PALAVRA, e nao numero: agora que os pesos tem QUILOS escritos (50, 100, 200...),
		// dizer "0,05% do que o seu corpo aguenta" entregaria o poder de quem le -- o limite do corpo e funcao direta
		// do poder expresso (`WeightTick`), e a regra principal do dono e que sem scouter ninguem sabe o proprio poder.
		double r = Jandirus.Core.Stats.Esmagamento.Razao(pl.Ficha);
		string noCorpo = pl.Ficha.weight_ratio > 1 ? "mais do que o seu corpo aguenta nesta gravidade"
					   : pl.Ficha.weight > 1 ? "pesa de verdade no seu corpo"
					   : "leve demais pro seu corpo -- o treino só rende mais com pesos mais pesados (melhore o peso máximo)";
		Avisar(pl, $"você veste {pct:0}% do peso máximo ({pl.Ficha.Weighted:N0} de {TetoDePeso(pl.Ficha):N0} kg): {noCorpo}, "
				   + $"e {pl.Ficha.weight:0.##}x de ganho no treino.");

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

		// ============================ A SEMENTE QUE SOBRA DA FRUTA ============================
		// `if(planttype&&prob(25))` (`Food.dm:140-143`): um quarto das frutas de plantar deixa a semente,
		// que vai direto pra mochila de quem comeu. O dono (2026-10-10) pediu exatamente isto pra maca --
		// *"pra n passar fome em planetas procedurais"*. A regra e o sorteio moram no `Plantio`.
		//
		// DEPOIS de a fruta sair da mochila, e a ordem importa: a ultima maca da pilha libera o slot em
		// que a semente cabe. Com a mochila cheia de verdade a semente se perde, e o jogador fica sabendo
		// (no DM o `contents` nao tem teto nesse caminho; aqui a mochila tem trinta espacos pra todo mundo).
		// ====================================================================================
		double sorteio = SorteioDeSementeDeTeste ?? _rng.NextDouble();
		if (Plantio.SaiSemente(def.Id, sorteio) is not { } semente) return;

		string nomeDaSemente = CatalogoDeItens.Get(semente)?.Nome ?? semente;
		if (Guardar(pl, semente))
			Avisar(pl, $"sobrou uma semente: {nomeDaSemente} foi pra mochila. Dá pra plantar onde quiser.");
		else
			Avisar(pl, $"sobrou uma semente, mas ela não coube na mochila e se perdeu.");
	}

	/// <summary>
	/// PALCO DA `--macieirateste`: o sorteio da semente cravado (0 a 1), pra a bancada pisar nos dois lados
	/// da fronteira do `prob(25)` sem depender da sorte. Nulo em jogo -- o sorteio e o do servidor.
	/// </summary>
	internal double? SorteioDeSementeDeTeste;
}
