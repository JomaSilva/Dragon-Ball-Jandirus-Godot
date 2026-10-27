using Jandirus.Core.Tech;

namespace Jandirus.Core.Items;

/// <summary>
/// A TECH DE BOLSO -- os itens fabricados que se CARREGAM e que ate a v0.2.0 caiam na mochila sem acao nenhuma
/// (dono, 2026-10-13: *"tem muita tech q n foi portada, faça o port delas todas"*).
///
/// ============================ COMO UM ITEM DESTES EXISTE ============================
/// O id e o do `construcoes.json` (e ele que a aba Tech fabrica), e a arte, o nome e o preco continuam vindo de la.
/// O que este arquivo acrescenta e o que o catalogo de construcoes nao tem: a FRASE em portugues, as ACOES de cada
/// um e o ESTADO de cada unidade.
///
/// O ESTADO VAI NO PROPRIO ID, como no livro de ensinamentos e na roupa guardada (`LivroDeEnsinamentos`,
/// `RoupaGuardada` -- la esta contado por que): a mochila guarda `(id, quantidade)` e mais nada. `Handgun|12|2|1`
/// E o revolver com doze balas. Tres regras de quem escreve um id destes:
///   * sem BARRA (o canal de itens do servidor parte o argumento na primeira, `GameServer.ComandoDeItem`);
///   * sem o proprio separador dentro de um campo (<see cref="Limpo"/>);
///   * no maximo <see cref="MaxDoId"/> letras: o pacote da mochila le o id com esse teto, e o `GetString` do
///     LiteNetLib devolve VAZIO acima dele -- o item sumiria da tela.
/// O que nao cabe num id (o texto de um livro, as amostras de DNA) mora no servidor, num cofre numerado
/// (`GameServer.TechDeBolso.cs`), e o id leva so o numero.
///
/// As regras de cada sistema (a pesca, as armas, as bombas) moram em `Core/Tech`; aqui fica a FICHA.
/// ==================================================================================
/// </summary>
public static class TechDeBolso
{
	// ---- os ids (os do `construcoes.json`) ----
	public const string Pilula = "Nutrient_Pill", BolsaDeSangue = "Blood_Bag", Vara = "Fishing_Pole", Livro = "Books",
						Pda = "PDA", Chave = "Key", Fabricador = "Fabricator", Agulha = "Dungeon_Needle",
						ContainerDeDna = "DNA_Container";

	// ---- as acoes (o botao da mochila e o `case` do servidor leem daqui) ----
	public const string AcaoDoar = "doar", AcaoBeber = "beber", AcaoPescar = "pescar", AcaoEscrever = "escrever",
						AcaoFolhear = "folhear", AcaoGravar = "gravar", AcaoTrancar = "trancar", AcaoReforcar = "reforcar",
						AcaoFabricar = "fabricar", AcaoApontar = "apontar", AcaoExtrair = "extrair", AcaoAmostras = "amostras";

	/// <summary>
	/// GUARDAR NO BAU ABERTO. Como "largar", vale pra todo item e por isso nao esta em ficha nenhuma: quem a oferece
	/// e a mochila, enquanto a tela de um bau estiver aberta (`TelaDeInventario`), e quem a cobra e o servidor.
	/// </summary>
	public const string AcaoGuardar = "guardar";

	/// <summary>O separador dos campos de estado dentro do id.</summary>
	public const char Sep = '|';

	/// <summary>O teto do id inteiro -- o do pacote da mochila (`Protocol.GetInventario`).</summary>
	public const int MaxDoId = 64;

	/// <summary>O teto do que se escreve num livro ou num PDA. O argumento do canal de verbos tem 256 letras, e o id vai junto.</summary>
	public const int MaxDeTexto = 200;

	/// <summary>
	/// A GAVETA DO COFRE num campo do id: `c12` e a gaveta 12; qualquer outra coisa, nenhuma (zero).
	///
	/// POR QUE HA NUMERO QUE NAO MORA NO ID: o id viaja no pacote da mochila, e ha estado que e um numero na escala
	/// de PODER -- a forca de uma arma reforcada, a armadura de um campo de forca -- tirado do teto de tecnologia de
	/// quem pagou, que e funcao do poder dele. A regra do dono e que ninguem le o proprio poder sem medidor, em
	/// tela ou em pacote nenhum; entao esses numeros ficam no servidor (o cofre, `GameServer.TechDeBolso.cs`), e o
	/// id leva so a gaveta.
	/// </summary>
	public static int Gaveta(string campo) =>
		campo.Length > 1 && campo[0] == 'c'
		&& int.TryParse(campo.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) && n > 0 ? n : 0;

	/// <summary>O campo do id que aponta pra uma gaveta do cofre; vazio quando nao ha gaveta.</summary>
	public static string EscreverGaveta(int gaveta) =>
		gaveta > 0 ? "c" + gaveta.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";

	/// <summary>O teto de uma senha gravada numa chave (o da porta e maior; a chave tem de caber no id).</summary>
	public const int MaxDeSenhaNaChave = 16;

	/// <summary>`upgraded`: mil zeni pra a chave guardar mais de uma senha (`Tier 1.dm:219-232`).</summary>
	public const double CustoDeReforcarAChave = 1000;

	/// <summary>A parte do id que diz QUE item e: tudo antes do primeiro separador.</summary>
	public static string Base(string id)
	{
		int i = id.IndexOf(Sep);
		return i < 0 ? id : id[..i];
	}

	/// <summary>Os campos de estado, na ordem. Vazio pro item recem-fabricado.</summary>
	public static string[] Campos(string id)
	{
		int i = id.IndexOf(Sep);
		return i < 0 ? [] : id[(i + 1)..].Split(Sep);
	}

	/// <summary>O campo <paramref name="i"/>, ou vazio.</summary>
	public static string Campo(string id, int i)
	{
		string[] c = Campos(id);
		return i < c.Length ? c[i] : "";
	}

	/// <summary>Monta o id de um item com estado. Sem campos = o id cru, o do item recem-fabricado.</summary>
	public static string Com(string @base, params string[] campos) =>
		campos.Length == 0 ? @base : @base + Sep + string.Join(Sep, campos);

	/// <summary>
	/// O TEXTO COMO ELE PODE MORAR NUM ID: sem separador, sem barra, sem quebra de linha, cortado no teto. Vazio quer
	/// dizer "nada". Quem limpa e o servidor; a caixa do cliente so limita o comprimento.
	/// </summary>
	public static string Limpo(string? cru, int teto)
	{
		if (string.IsNullOrWhiteSpace(cru)) return "";
		var sb = new System.Text.StringBuilder(cru.Length);
		foreach (char c in cru.Trim())
			if (!char.IsControl(c) && c != Sep && c != '/') sb.Append(c);
		string s = sb.ToString().Trim();
		return s.Length > teto ? s[..teto] : s;
	}

	// =====================================================================
	// A CHAVE -- `obj/items/Key` (`Tier 1.dm:175-237`)
	// =====================================================================
	/// <summary>A chave foi reforcada (`upgraded`)? Reforcada, guarda mais de uma senha.</summary>
	public static bool ChaveReforcada(string id) => Campo(id, 0) == "m";

	/// <summary>As senhas gravadas nesta chave.</summary>
	public static string[] SenhasDaChave(string id)
	{
		string[] c = Campos(id);
		return c.Length <= 1 ? [] : [.. c[1..].Where(s => s.Length > 0)];
	}

	/// <summary>O id da chave com estas senhas.</summary>
	public static string ChaveCom(bool reforcada, IEnumerable<string> senhas) =>
		Com(Chave, [reforcada ? "m" : "", .. senhas]);

	/// <summary>Esta mochila carrega uma chave com esta senha? (A porta pergunta -- `GameServer.PortaAbrePara`.)</summary>
	public static bool TemChaveCom(Inventario mochila, string senha)
	{
		if (senha.Length == 0) return false;
		foreach (Pilha p in mochila.Pilhas)
			if (Base(p.Id) == Chave && Array.IndexOf(SenhasDaChave(p.Id), senha) >= 0) return true;
		return false;
	}

	// =====================================================================
	// A FICHA
	// =====================================================================
	/// <summary>
	/// A ficha de um item de bolso, ou nulo quando o id nao e de um -- a porta do <see cref="CatalogoDeItens.Get"/>.
	/// O `Id` da ficha e o id INTEIRO (com o estado): e ele que a pilha guarda.
	/// </summary>
	public static ItemDef? Ficha(string id, CatalogoDeObras? obras)
	{
		if (id.Length > MaxDoId) return null;
		string b = Base(id);
		Construcao? c = obras?.Get(b);
		if (c == null) return null;

		ItemDef Feita(string nome, string frase, string[] acoes, bool empilha = false, double nutricao = 0) =>
			new(id, nome, frase, c.Arte, c.Estado, empilha, nutricao, acoes);

		switch (b)
		{
			// `obj/items/food/Nutrient_Pill` (`Consumables.dm:41-46`): `nutrition = 5`, `stackable = 1`.
			case Pilula when id == b:
				return Feita(c.Nome, "Uma pílula que alimenta um pouco. Nojenta -- mas dá conta do recado.", ["comer"],
						   empilha: true, nutricao: 5);

			// `obj/items/Blood_Bag` (`Consumables.dm:19-40`).
			case BolsaDeSangue:
			{
				bool cheia = Campo(id, 0) == "cheia";
				return Feita(cheia ? $"{c.Nome} (cheia)" : c.Nome,
						   "Guarda sangue por tempo indeterminado. Doar tira um pouco da sua vida; quem tira proveito de beber "
						   + "é um vampiro.", [cheia ? AcaoBeber : AcaoDoar]);
			}

			// `obj/items/FishingPole` (`Stamina/Fishing.dm`).
			case Vara when id == b:
				return Feita(c.Nome, "Pesca comida perto da água. Lance a linha e fique parado; quando o peixe fisgar, "
						   + "puxe (use a vara de novo) antes que ele escape.", [AcaoPescar]);

			// `obj/items/Book` (`Inventory.dm:7-19`): um bloco de notas.
			case Livro:
			{
				bool escrito = Campo(id, 0).Length > 0;
				return Feita(escrito ? $"{c.Nome} nº {Campo(id, 0)}" : c.Nome,
						   "Um livro em branco: escreva o que quiser nele, e quem o tiver nas mãos lê.",
						   escrito ? [AcaoFolhear, AcaoEscrever] : [AcaoEscrever]);
			}

			// `obj/items/PDA` (`Camera.dm:31-114`): as notas do livro, mais as frequencias das cameras e a biblioteca global.
			// O id e `PDA|<nota>|<frequencias>|<marca do dono>` -- ver `Vigilancia`.
			case Pda:
			{
				bool escrito = Campo(id, 0).Length > 0;
				List<string> frequencias = Vigilancia.FrequenciasDoPda(id);
				var acoes = new List<string>();
				if (escrito) acoes.Add(AcaoFolhear);
				acoes.Add(AcaoEscrever);
				if (frequencias.Count > 0) acoes.Add(Vigilancia.AcaoVer);
				acoes.Add(Vigilancia.AcaoMaisFrequencia);
				if (frequencias.Count > 0) acoes.Add(Vigilancia.AcaoMenosFrequencia);
				acoes.Add(Vigilancia.AcaoDna);
				acoes.Add(Vigilancia.AcaoBiblioteca);
				acoes.Add(Vigilancia.AcaoImprimir);
				acoes.Add(Vigilancia.AcaoPublicar);
				return Feita((escrito ? $"{c.Nome} nº {Campo(id, 0)}" : c.Nome)
							 + (frequencias.Count > 0 ? $" [{string.Join(", ", frequencias)}]" : "")
							 + (Vigilancia.DnaDoPda(id).Length > 0 ? " (travado)" : ""),
						   "Um bloco de notas eletrônico. Também consulta as câmeras das frequências dele, e publica e imprime "
						   + "livros da biblioteca global.", [.. acoes]);
			}

			// `obj/items/Key` (`Tier 1.dm:175-237`).
			case Chave:
			{
				string[] senhas = SenhasDaChave(id);
				bool reforcada = ChaveReforcada(id);
				var acoes = new List<string> { AcaoGravar };
				if (senhas.Length > 0) acoes.Add(AcaoTrancar);
				if (!reforcada) acoes.Add(AcaoReforcar);
				return Feita(senhas.Length == 0 ? c.Nome : $"{c.Nome} ({senhas.Length} senha{(senhas.Length > 1 ? "s" : "")})",
						   "Grave nela a senha de uma porta: quem CARREGA a chave passa pela porta sem digitar nada. "
						   + "Também tranca a sua própria porta com a senha gravada."
						   + (reforcada ? " Reforçada: guarda mais de uma senha." : ""), [.. acoes]);
			}

			// `obj/items/Fabricator` (`TechResearch.dm:18-24`): `usr.OpenTechWindow()`.
			case Fabricador when id == b:
				return Feita(c.Nome, "Abre a fabricação de tecnologia de onde você estiver.", [AcaoFabricar]);

			// `obj/items/Dungeon_Needle` (`dungeon_obj.dm:210-264`).
			case Agulha when id == b:
				return Feita(c.Nome, "Aponta a direção da masmorra mais próxima -- sem dizer as coordenadas.", [AcaoApontar]);

			// `obj/items/DNA_Container` (`DNALabs.dm:280-326`).
			case ContainerDeDna:
				return Feita(Campo(id, 0).Length > 0 ? $"{c.Nome} nº {Campo(id, 0)}" : c.Nome,
						   "Extrai e guarda amostras de DNA de quem está nocauteado. Leve-as a um Bio-Android Lab: "
						   + "\"Colher DNA\" no laboratório despeja as amostras do recipiente no tanque.",
						   [AcaoExtrair, AcaoAmostras]);

			// =============================== (lote T2) o que atira, o que explode, o que se veste ===============================
			// `obj/items/Ammo` (`Guns.dm:60-63`).
			case ArmasDeFogo.Municao when id == b:
				return Feita(c.Nome, "Uma caixa de munição enche o pente inteiro de uma arma de pólvora. Blasters não usam.",
						   [], empilha: true);

			// `obj/items/Stungun` (`Tier2.dm:298-357`).
			case Atordoador.Id:
			{
				bool carregado = Atordoador.Carregado(id);
				return Feita($"{c.Nome} ({(carregado ? "carregado" : "vazio")})",
						   "Um tiro só: paralisa quem acerta e derruba na hora quem já está quase caído (ou é fraco demais). "
						   + "Não fere. Recarregar e reforçar custam pelo teto da sua tecnologia.",
						   carregado ? [ArmasDeFogo.AcaoAtirar, ArmasDeFogo.AcaoForca, ArmasDeFogo.AcaoInfo]
									 : [ArmasDeFogo.AcaoRecarregar, ArmasDeFogo.AcaoForca, ArmasDeFogo.AcaoInfo]);
			}

			// `obj/items/Bomb/Nuke` (`Bombs.dm:100-107`).
			case Bombas.Nuke:
				return Feita(c.Nome,
						   $"Arrasa tudo num raio de {Bombas.RaioDaNuke} tiles: fere, arranca membros, envenena e derruba o cenário. "
						   + "Armar a larga no chão já contando; quem a pega do chão antes do zero a desarma. Com um código "
						   + "gravado, um detonador de mesmo código a estoura de longe.",
						   [Bombas.AcaoArmar, Bombas.AcaoCodigo, Bombas.AcaoReforcar, Bombas.AcaoInfo]);

			// `obj/items/Toxic_Waste` (`Bombs.dm:296-310`).
			case Bombas.LixoToxico when id == b:
				return Feita(c.Nome, "Um barril de lixo tóxico. Rompido -- na sua mão, ou largado no raio de uma bomba --, "
						   + "solta uma nuvem que arremessa e envenena quem está perto.", [Bombas.AcaoRomper]);

			// `obj/items/Smoke_Bomb` (`Bombs.dm:312-324`).
			case Bombas.Fumaca when id == b:
				return Feita(c.Nome, $"Não fere ninguém: enche o lugar de fumaça por {Bombas.SegundosDeFumaca:0} segundos.",
						   [Bombas.AcaoSoltarFumaca], empilha: true);

			// `obj/items/Detonator` (`Tier2.dm:254-297`).
			case Bombas.Detonador:
			{
				string codigo = Bombas.Codigo(id);
				return Feita(codigo.Length > 0 ? $"{c.Nome} [{codigo}]" : c.Nome,
						   "Grave um código e dispare: no fim da contagem, toda bomba com o mesmo código estoura onde estiver.",
						   [Bombas.AcaoDetonar, Bombas.AcaoCodigo]);
			}

			// `obj/items/Forcefield` (`Tier2.dm:209-253`).
			case CampoDeForca.Id:
				return Feita(CampoDeForca.Ligado(id) ? $"{c.Nome} (ligado)" : c.Nome,
						   "Ligado, leva no lugar do seu corpo todo tiro que o acertaria -- bola, raio ou bala --, ao preço da "
						   + "armadura dele. Sem armadura, estoura. Não protege de soco nem de explosão.",
						   [CampoDeForca.AcaoLigar, CampoDeForca.AcaoInfo]);

			// `obj/items/Portable_Repairer` e `obj/items/Destructor` (`buildable.dm:129-176`). A regra esta em `MurosDeTech`.
			case MurosDeTech.ReparadorPortatil when id == b:
				return Feita(c.Nome, "Cura a armadura de toda construção a um tile de você. O que já caiu não volta.",
						   [MurosDeTech.AcaoReparar]);
			case MurosDeTech.Destruidor when id == b:
				return Feita(c.Nome, "Derruba o que há a um tile de você -- construções, paredes erguidas e o próprio cenário --, "
						   + "desde que a sua tecnologia alcance a resistência (ou que seja seu). Cada uso é pago.",
						   [MurosDeTech.AcaoDestruir]);

			// =============================== (lote T5) os kits do androide de absorcao ===============================
			// `obj/items/Repair_Kit` e `obj/items/Mechanical_Kit` (`Cyborgs.dm:147-180`). A regra -- a do dono, de
			// 2026-10-13 -- esta em `KitsDeAndroide`.
			case KitsDeAndroide.Reparo when id == b:
				return Feita(c.Nome, "Conserta o corpo de um androide de absorção, que o regenerador não cura: dez segundos parado "
						   + "e sem apanhar, e cada parte ferida ganha dez de vida.", [KitsDeAndroide.AcaoReparar]);
			case KitsDeAndroide.Mecanico when id == b:
				return Feita(c.Nome, "Refaz um membro perdido de um androide de absorção -- o seu, ou o de um caído à sua frente: "
						   + "dez segundos parado e sem apanhar.", [KitsDeAndroide.AcaoRefazer]);
			// (2026-10-26) o upgrade do androide de absorcao -- o pedido 95c. A regra esta em `KitsDeAndroide.Upgrade`.
			case KitsDeAndroide.Upgrade when id == b:
				return Feita(c.Nome, "Solta o potencial que o corpo de um androide de absorção guarda -- "
						   + "os caminhos de sempre não o soltam: dez segundos parado e sem apanhar, e o banco inteiro vai pro poder. Uma vez por hora.", [KitsDeAndroide.AcaoUpgrade]);

			// =============================== (lote T5) os companheiros ===============================
			// As quatro capsulas (`obj/items/companion_obj`, `NPCs/companion_list.dm`). A regra esta em `Companheiros`; o id
			// e `<base>|c<gaveta>|<inteligencia>`.
			case Companheiros.Bola or Companheiros.CapsulaDeSaibaman or Companheiros.NucleoDeBoneca or Companheiros.NucleoDoInterceptador:
			{
				if (b == Companheiros.Bola && Companheiros.Gaveta(id) == 0)
					return Feita(c.Nome, "Uma bola vazia: aponte pra uma criatura nocauteada à sua frente, e ela guarda a forma, o nome "
							   + "e o poder dela.", [Companheiros.AcaoCapturar]);
				int inteligencia = Companheiros.Inteligencia(id);
				var acoes = new List<string> { Companheiros.AcaoSoltar, Companheiros.AcaoInfo, Companheiros.AcaoSeguir, Companheiros.AcaoAtacar };
				if (inteligencia >= Companheiros.IntDeMatar) acoes.Add(Companheiros.AcaoMatar);
				acoes.Add(Companheiros.AcaoParar);
				if (inteligencia >= Companheiros.IntDeQuandoAtaco) acoes.Add(Companheiros.AcaoQuandoAtaco);
				if (inteligencia >= Companheiros.IntDeQuandoApanho) acoes.Add(Companheiros.AcaoQuandoApanho);
				if (inteligencia >= Companheiros.IntDeFalar) acoes.Add(Companheiros.AcaoFalar);
				acoes.Add(Companheiros.AcaoNome);
				acoes.Add(Companheiros.AcaoMelhorar);
				acoes.Add(Companheiros.AcaoPoder);
				return Feita(c.Nome, "Guarda uma criatura que luta por você: solte-a, e ela segue e obedece. Lutando ela sobe de "
						   + "nível, e cada nível dá uma melhoria. Quanto mais esperta, mais ordens entende.", [.. acoes]);
			}

			// `obj/items/Pet_Food` (`companion_list.dm:50-62`).
			case Companheiros.Racao when id == b:
				return Feita(c.Nome, "Dê à sua criatura solta ao lado: ela fica três pontos mais esperta, e passa a entender mais "
						   + "ordens.", [Companheiros.AcaoRacao]);

			// =============================== (lote T4) o teleporte e a vigilancia ===============================
			// `obj/items/Omniwatch` (`Teleports.dm:64-169`). A regra esta em `Teleporte`.
			case Teleporte.Relogio:
			{
				string canal = Teleporte.Canal(id);
				var acoes = new List<string>();
				if (canal.Length > 0) { acoes.Add(Teleporte.AcaoDestinos); acoes.Add(Teleporte.AcaoIr); acoes.Add(Teleporte.AcaoFalar); }
				acoes.Add(Teleporte.AcaoCanal);
				acoes.Add(Teleporte.AcaoHenshin);
				acoes.Add(Teleporte.AcaoDna);
				return Feita((canal.Length > 0 ? $"{c.Nome} [{canal}]" : c.Nome) + (Teleporte.Dna(id).Length > 0 ? " (travado)" : ""),
						   "Um relógio que só teleporta: leva você aos telepads aparafusados e aos outros relógios do MESMO canal, "
						   + "dois segundos depois de acionado. Também fala no canal.", [.. acoes]);
			}

			// `obj/items/Teleport_Nullifier` (`Teleports.dm:29-63`).
			case Teleporte.Anulador when id == b:
				return Feita(c.Nome, "Ligado, impede o teleporte dos relógios no lugar em que você está, por um minuto ou pouco mais "
						   + "-- o seu também.", [Teleporte.AcaoAnular]);

			// `obj/items/Camera` (`Camera.dm:117-124`) e `obj/items/Communicator` (`Communicator.dm`): MAQUINAS DE CHAO. O
			// catalogo extraido as da como pessoais, e a ficha corrige -- ver o cabecalho do `Vigilancia`.
			case Vigilancia.Camera when id == b:
				return Feita(c.Nome, "Assente no chão, aparafuse e dê uma frequência a ela: quem tiver um PDA, um computador de "
						   + "câmeras ou um comunicador nessa frequência fica sabendo quem ela vê.", [CatalogoDeItens.AcaoPosicionar]);
			case Vigilancia.Comunicador when id == b:
				return Feita(c.Nome, "Um console de comunicação: assentado no chão, envia frases a quem escuta as frequências "
						   + "dele (relógios, scouters, outros comunicadores) e guarda o que recebe.", [CatalogoDeItens.AcaoPosicionar]);

			// `obj/items/Camera_Computer` (`Camera.dm:126-162`). O id e `Camera_Computer|<frequencia>|<marca do dono>`.
			case Vigilancia.Computador:
			{
				string frequencia = Vigilancia.FrequenciaDoComputador(id);
				return Feita((frequencia.Length > 0 ? $"{c.Nome} [{frequencia}]" : c.Nome)
							 + (Vigilancia.DnaDoComputador(id).Length > 0 ? " (travado)" : ""),
						   "Consulta, de qualquer lugar, as câmeras da frequência dele.",
						   frequencia.Length > 0 ? [Vigilancia.AcaoVer, Vigilancia.AcaoFrequencia, Vigilancia.AcaoDna]
												 : [Vigilancia.AcaoFrequencia, Vigilancia.AcaoDna]);
			}

			// `obj/items/Recreator` (`buildable.dm:225-460`). A regra esta em `Recriador`.
			case Recriador.Id:
			{
				bool gravado = Campo(id, 0).Length > 0;
				return Feita(gravado ? $"{c.Nome} (planta nº {Campo(id, 0)})" : c.Nome,
						   "Grava a planta dos SEUS blocos erguidos num quadrado de 30 por 30 em volta de você, e depois ergue de "
						   + "novo o que cair -- por bloco, bem mais barato que construir.",
						   gravado ? [Recriador.AcaoRecriar, Recriador.AcaoInfo, Recriador.AcaoGravar] : [Recriador.AcaoGravar]);
			}

			// `obj/Boat` (`Boat.dm`). A regra esta em `Barco`.
			case Barco.Id when id == b:
				// (ele continua sendo uma construcao do catalogo: tambem se instala no chao, como enfeite de cais)
				return Feita(c.Nome, "Com ele na mochila você veleja: na margem, de frente pra água, zarpe -- a água deixa de "
						   + "parar você, sem o fôlego que nadar cobra. Atraca sozinho quando chega no seco.",
						   [Barco.AcaoVelejar, CatalogoDeItens.AcaoPosicionar]);

			// `obj/items/Energy_Drain_Gloves` e `Energy_Drain_Boots` (`Tier 1.5.dm:252-323`).
			case DrenoDeEnergia.Luvas or DrenoDeEnergia.Botas:
				return Feita(DrenoDeEnergia.Vestida(id) ? $"{c.Nome} (vestidas)" : c.Nome,
						   "Vestidas, drenam o seu Ki sem parar -- e fazem a sua energia máxima crescer mais depressa em tudo o "
						   + "que a treina.", [DrenoDeEnergia.AcaoVestir]);
		}

		// AS ARMAS DE FOGO (`Guns.dm`, e o Bolter de `buildable.dm:177-224`): o modelo sai da tabela do `ArmasDeFogo`.
		if (ArmasDeFogo.De(b) is { } arma)
		{
			ArmasDeFogo.Estado e = ArmasDeFogo.Ler(id);
			var acoes = new List<string> { ArmasDeFogo.AcaoAtirar };
			if (arma.Fonte == ArmasDeFogo.Fonte.Polvora) acoes.Add(ArmasDeFogo.AcaoRecarregar);
			if (arma.Fonte == ArmasDeFogo.Fonte.Energia) { acoes.Add(ArmasDeFogo.AcaoEncher); acoes.Add(ArmasDeFogo.AcaoReserva); }
			acoes.Add(ArmasDeFogo.AcaoForca);
			acoes.Add(ArmasDeFogo.AcaoCritico);
			if (!e.Empurra) acoes.Add(ArmasDeFogo.AcaoEmpurrao);
			if (!e.Atordoa) acoes.Add(ArmasDeFogo.AcaoAtordoar);
			if (b == ArmasDeFogo.Bolter) acoes.Add(ArmasDeFogo.AcaoAparafusar);
			acoes.Add(ArmasDeFogo.AcaoInfo);

			string carga = arma.Fonte == ArmasDeFogo.Fonte.Ki ? "Ki" : $"{e.Carga}/{e.Pente}";
			string frase = arma.Fonte switch
			{
				ArmasDeFogo.Fonte.Polvora => "Arma de pólvora: o tiro vale o que a ARMA vale, seja quem for o dono. Uma caixa de munição enche o pente.",
				ArmasDeFogo.Fonte.Energia => "Arma de energia: o tiro vale o que a ARMA vale e não mata. A reserva se enche e se dobra pagando.",
				_ => "Arma de ki: o tiro vale o que a ARMA vale e não mata, e cada um cobra um pouco do seu Ki.",
			};
			if (b == ArmasDeFogo.Bolter) frase += " Também aparafusa (e solta) o que é seu, a um tile.";
			return Feita($"{c.Nome} ({carga})", frase + " Dá pra pôr o gatilho numa tecla (verb \"Atirar\").", [.. acoes]);
		}
		return null;
	}

	// =====================================================================
	// O BOTAO DE CADA ACAO -- o que ele diz, e se ele pergunta alguma coisa
	// =====================================================================
	/// <summary>Como uma acao aparece na mochila: o rotulo, e a pergunta que ela faz antes de mandar o verbo (se faz).</summary>
	/// <param name="Paga">
	/// A ACAO E PAGA, e aceita as duas moedas (dono, 2026-10-13, sobre os pesos: *"so tem a opçao de zenni e nao de
	/// recurso, adicione essa opçao"*): a mochila desenha DOIS botoes, e o de recursos manda a palavra da moeda colada
	/// no id (`Handgun|.../recursos`), como o dos pesos. <paramref name="CustoEmZeni"/> e o preco quando ele se sabe so
	/// pelo item; zero quando depende de quem paga (o teto de tecnologia) -- ai o servidor e quem diz.
	/// </param>
	public readonly record struct Botao(string Rotulo, Interacoes.Forma Forma = Interacoes.Forma.Direta, double Min = 0,
										double Max = 0, string Pergunta = "", bool Paga = false, double CustoEmZeni = 0)
	{
		/// <summary>O que o botao diz quando a moeda e esta: o rotulo, e o preco (ou so a moeda, quando o preco e de quem paga).</summary>
		public string RotuloEm(Economia.Moeda moeda) =>
			!Paga ? Rotulo
			: CustoEmZeni > 0 ? $"{Rotulo} — {Economia.Recurso.PrecoEscrito(CustoEmZeni, moeda)}"
			: $"{Rotulo} — em {(moeda == Economia.Moeda.Recursos ? "recursos" : "zeni")}";
	}

	/// <summary>
	/// O botao de uma acao de tech de bolso, ou nulo pra acao que nao e daqui. A mochila do cliente pergunta ANTES do
	/// rotulo de sempre: e o que faz um item novo ganhar botao (e teclado, e caixa de texto) sem a tela saber dele.
	/// O <paramref name="id"/> e o do item em que o botao aparece: e dele que sai o preco que depende do estado.
	/// </summary>
	public static Botao? BotaoDa(string acao, string id = "") => acao switch
	{
		// ---- (lote T2) as armas ----
		ArmasDeFogo.AcaoAtirar => new Botao("Atirar"),
		ArmasDeFogo.AcaoRecarregar => Base(id) == Atordoador.Id
			? new Botao("Recarregar", Paga: true)
			: new Botao("Recarregar (gasta uma caixa de munição)"),
		ArmasDeFogo.AcaoInfo => new Botao("Ver os números"),
		ArmasDeFogo.AcaoForca => Base(id) == Atordoador.Id
			? new Botao("Força: até o teto da sua tecnologia", Paga: true)
			: new Botao("Força: até o teto da sua tecnologia", Paga: true, CustoEmZeni: ArmasDeFogo.CustoDaForca),
		ArmasDeFogo.AcaoCritico => new Botao("Crítico +1%", Paga: true, CustoEmZeni: ArmasDeFogo.CustoDoCritico(ArmasDeFogo.Ler(id).Critico)),
		ArmasDeFogo.AcaoEmpurrao => new Botao("Efeito de arremesso", Paga: true, CustoEmZeni: ArmasDeFogo.CustoDoEmpurrao),
		ArmasDeFogo.AcaoAtordoar => new Botao("Efeito de paralisia", Paga: true, CustoEmZeni: ArmasDeFogo.CustoDoAtordoar),
		ArmasDeFogo.AcaoReserva => new Botao("Dobrar a reserva", Paga: true, CustoEmZeni: ArmasDeFogo.CustoDaReserva(ArmasDeFogo.Ler(id).Pente)),
		ArmasDeFogo.AcaoEncher => new Botao("Encher a reserva", Paga: true, CustoEmZeni: ArmasDeFogo.CustoDeEncher(ArmasDeFogo.Ler(id).Pente)),
		ArmasDeFogo.AcaoAparafusar => new Botao("Aparafusar / soltar o que é meu aqui"),

		// ---- (lote T2) as bombas ----
		Bombas.AcaoArmar => new Botao("Armar e largar", Interacoes.Forma.Numero, Bombas.MinDeContagem, Bombas.MaxDeContagem, "segundos até estourar"),
		Bombas.AcaoCodigo => new Botao("Gravar o código", Interacoes.Forma.Texto, 0, Bombas.MaxDeCodigo, "O código"),
		Bombas.AcaoReforcar => new Botao("Reforçar: o dobro do teto da sua tecnologia", Paga: true),
		Bombas.AcaoInfo => new Botao("Ver os números"),
		Bombas.AcaoRomper => new Botao("Romper o barril aqui"),
		Bombas.AcaoSoltarFumaca => new Botao("Estourar a fumaça"),
		Bombas.AcaoDetonar => new Botao("Disparar", Interacoes.Forma.Numero, Bombas.MinDeContagem, Bombas.MaxDeContagem, "segundos até detonar"),

		// ---- (lote T5) os kits do androide de absorcao ----
		KitsDeAndroide.AcaoReparar => new Botao("Consertar o meu corpo (dez segundos)"),
		KitsDeAndroide.AcaoRefazer => new Botao("Refazer um membro perdido (dez segundos)"),
		KitsDeAndroide.AcaoUpgrade => new Botao("Instalar o upgrade (dez segundos)"),

		// ---- (lote T5) os companheiros ----
		Companheiros.AcaoSoltar => new Botao("Soltar / recolher"),
		Companheiros.AcaoCapturar => new Botao("Capturar o nocauteado à frente"),
		Companheiros.AcaoInfo => new Botao("Ver a criatura"),
		Companheiros.AcaoSeguir => new Botao("Ordem: seguir / ficar"),
		Companheiros.AcaoAtacar => new Botao("Ordem: atacar o meu alvo"),
		Companheiros.AcaoMatar => new Botao("Ordem: matar o meu alvo"),
		Companheiros.AcaoParar => new Botao("Ordem: parar e voltar"),
		Companheiros.AcaoQuandoAtaco => new Botao("Atacar quando eu ataco (liga / desliga)"),
		Companheiros.AcaoQuandoApanho => new Botao("Atacar quando me atacam (liga / desliga)"),
		Companheiros.AcaoFalar => new Botao("Falar por ela", Interacoes.Forma.Texto, 0, Vigilancia.MaxDeFrase, "O que ela diz?"),
		Companheiros.AcaoNome => new Botao("Mudar o nome", Interacoes.Forma.Texto, 0, Companheiros.MaxDeNome, "O nome"),
		Companheiros.AcaoMelhorar => new Botao("Gastar uma melhoria (1 físicos, 2 Ki, 3 variados, 4 poder)", Interacoes.Forma.Numero, 1, 4,
											   "1 físicos · 2 Ki · 3 variados · 4 poder"),
		Companheiros.AcaoPoder => new Botao("Atualizar o poder (gasta uma comida)", Paga: true, CustoEmZeni: Companheiros.CustoDeAtualizar),
		Companheiros.AcaoRacao => new Botao("Dar à criatura ao lado"),

		// ---- (lote T4) o teleporte e a vigilancia ----
		Teleporte.AcaoCanal => new Botao("Definir o canal", Interacoes.Forma.Texto, 0, Teleporte.MaxDeCanal, "O canal"),
		Teleporte.AcaoDestinos => new Botao("Ver os destinos do canal"),
		Teleporte.AcaoIr => new Botao("Teleportar (nº do destino)", Interacoes.Forma.Numero, 1, Teleporte.MaxDeDestinos, "número do destino"),
		Teleporte.AcaoFalar => new Botao("Falar no canal", Interacoes.Forma.Texto, 0, Vigilancia.MaxDeFrase, "O que você diz?"),
		Teleporte.AcaoHenshin => new Botao("Henshin!"),
		Teleporte.AcaoDna => new Botao(Teleporte.Dna(id).Length > 0 ? "Destravar o DNA" : "Travar pro meu DNA"),
		Teleporte.AcaoAnular => new Botao("Ligar / desligar"),
		Vigilancia.AcaoFrequencia => new Botao("Definir a frequência", Interacoes.Forma.Texto, 0, Vigilancia.MaxDeFrequencia, "A frequência"),
		Vigilancia.AcaoMaisFrequencia => new Botao("Adicionar uma frequência", Interacoes.Forma.Texto, 0, Vigilancia.MaxDeFrequencia, "A frequência"),
		Vigilancia.AcaoMenosFrequencia => new Botao("Tirar uma frequência", Interacoes.Forma.Texto, 0, Vigilancia.MaxDeFrequencia, "A frequência"),
		Vigilancia.AcaoVer => new Botao("Ver as câmeras"),
		Vigilancia.AcaoDna => new Botao(Vigilancia.DnaDoItem(id).Length > 0 ? "Destravar o DNA" : "Travar pro meu DNA"),
		Vigilancia.AcaoPublicar => new Botao("Publicar um livro na biblioteca (nº do livro)", Interacoes.Forma.Numero, 1, 999_999, "número do livro"),
		Vigilancia.AcaoBiblioteca => new Botao("Ver a biblioteca"),
		Vigilancia.AcaoImprimir => new Botao("Imprimir da biblioteca (nº)", Interacoes.Forma.Numero, 1, Vigilancia.MaxDeLivrosNaBiblioteca, "número na biblioteca"),

		// ---- (lote T3) o recriador e o barco ----
		Recriador.AcaoGravar => new Botao(Campo(id, 0).Length > 0 ? "Gravar de novo (aqui)" : "Gravar a planta daqui"),
		Recriador.AcaoRecriar => new Botao("Recriar o que caiu", Paga: true),
		Recriador.AcaoInfo => new Botao("Ver a planta"),
		Barco.AcaoVelejar => new Botao("Zarpar / atracar"),

		// ---- (lote T3) as ferramentas de parede ----
		MurosDeTech.AcaoReparar => new Botao("Reparar o que está ao redor"),
		MurosDeTech.AcaoDestruir => new Botao("Destruir o que está ao redor", Paga: true, CustoEmZeni: MurosDeTech.CustoDeDestruir),

		// ---- (lote T2) as defesas ----
		CampoDeForca.AcaoLigar => new Botao(CampoDeForca.Ligado(id) ? "Desligar" : "Ligar"),
		CampoDeForca.AcaoInfo => new Botao("Ver a armadura"),
		DrenoDeEnergia.AcaoVestir => new Botao(DrenoDeEnergia.Vestida(id) ? "Tirar" : "Vestir"),

		AcaoDoar => new Botao("Doar sangue"),
		AcaoBeber => new Botao("Beber o sangue"),
		AcaoPescar => new Botao("Pescar / puxar a linha"),
		AcaoEscrever => new Botao("Escrever", Interacoes.Forma.Texto, 0, MaxDeTexto, "O que você escreve?"),
		AcaoFolhear => new Botao("Ler"),
		AcaoGravar => new Botao("Gravar uma senha", Interacoes.Forma.Texto, 0, MaxDeSenhaNaChave, "A senha da porta"),
		AcaoTrancar => new Botao("Trancar a minha porta com a senha"),
		AcaoReforcar => new Botao("Reforçar", Paga: true, CustoEmZeni: CustoDeReforcarAChave),
		AcaoFabricar => new Botao("Abrir a fabricação"),
		AcaoApontar => new Botao("Apontar"),
		AcaoExtrair => new Botao("Extrair DNA de quem está caído"),
		AcaoAmostras => new Botao("Ver as amostras"),
		AcaoGuardar => new Botao("Guardar no baú"),
		_ => null,
	};
}
