using System.Text.Json;
using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.Items;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// O QUE NAO CABE NO ID DE UM ITEM -- o texto de um livro ou de um PDA, as amostras de um recipiente de DNA. O item
/// leva um NUMERO (`Books|17`), e o que o numero guarda mora aqui e vai pro `tech-itens.json`. Ver `TechDeBolso`.
/// </summary>
public sealed class CofreDeTech
{
	public int Proximo = 1;
	public Dictionary<int, string> Textos = [];
	public Dictionary<int, List<Amostra>> Amostras = [];

	/// <summary>Os numeros na escala de PODER que nao podem viajar no id de um item (ver `TechDeBolso.Gaveta`).</summary>
	public Dictionary<int, double[]> Numeros = [];

	/// <summary>As plantas gravadas pelos recriadores (ver `Jandirus.Core.Tech.Recriador`).</summary>
	public Dictionary<int, Jandirus.Core.Tech.Planta> Plantas = [];

	/// <summary>A biblioteca global dos PDAs: os textos publicados (ver `Jandirus.Core.Tech.Vigilancia`).</summary>
	public List<string> Biblioteca = [];

	/// <summary>As criaturas das capsulas de companheiro (ver `Jandirus.Core.Tech.Companheiros`).</summary>
	public Dictionary<int, Jandirus.Core.Tech.Companheiro> Companheiros = [];
}

/// <summary>
/// A TECH DE BOLSO E OS DEPOSITOS -- o lado do servidor do primeiro lote da tech portada na v0.2.1 (dono, 2026-10-13:
/// *"tem muita tech q n foi portada, faça o port delas todas"*). As fichas dos itens estao em
/// `Core/Items/TechDeBolso.cs`; as regras de cada sistema, em `Core/Tech` (`Pesca`, `Deposito`).
///
/// AS DUAS PORTAS SAO AS DE SEMPRE: o item de bolso entra pelo canal de itens (`item_&lt;acao&gt;`, e o
/// `ComandoDeItem` ja conferiu que ele esta na mochila e que a acao e dele); a maquina no chao entra pelo menu da
/// tecla E (`ComandoDeInteracao`, com o alcance e a tabela `Interacoes` conferidos pelo `ObraQueAceita`).
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// O COFRE (`tech-itens.json`)
	// =====================================================================
	private const string ArquivoDoCofreDeTech = "tech-itens.json";

	private CofreDeTech _cofreDeTech = new();

	private string CaminhoDoCofreDeTech =>
		System.IO.Path.Combine(_store?.Pasta ?? ProjectSettings.GlobalizePath("user://saves"), ArquivoDoCofreDeTech);

	private static readonly JsonSerializerOptions JsonDoCofre = new() { IncludeFields = true, WriteIndented = true };

	/// <summary>Le o cofre do disco. Pasta sem o arquivo e mundo sem nada escrito.</summary>
	private void CarregarOCofreDeTech()
	{
		_cofreDeTech = new CofreDeTech();
		string caminho = CaminhoDoCofreDeTech;
		if (!System.IO.File.Exists(caminho)) return;
		try
		{
			_cofreDeTech = JsonSerializer.Deserialize<CofreDeTech>(System.IO.File.ReadAllText(caminho), JsonDoCofre) ?? new CofreDeTech();
		}
		catch (Exception e) { GD.PushWarning($"[server] {ArquivoDoCofreDeTech} ilegivel: {e.Message}"); }
	}

	private void GravarOCofreDeTech()
	{
		try { System.IO.File.WriteAllText(CaminhoDoCofreDeTech, JsonSerializer.Serialize(_cofreDeTech, JsonDoCofre)); }
		catch (Exception e) { GD.PushWarning($"[server] nao gravei o {ArquivoDoCofreDeTech}: {e.Message}"); }
	}

	/// <summary>O numero <paramref name="i"/> de uma gaveta do cofre, ou o padrao quando ela nao existe.</summary>
	private double NumeroDaGaveta(int gaveta, int i, double padrao) =>
		gaveta > 0 && _cofreDeTech.Numeros.TryGetValue(gaveta, out double[]? v) && i < v.Length ? v[i] : padrao;

	/// <summary>
	/// Escreve os numeros de uma gaveta -- abrindo uma nova quando <paramref name="gaveta"/> e zero -- e devolve a
	/// gaveta. O disco fica pro proximo descarrego (<see cref="DescarregarOCofre"/>): a armadura de um campo de forca
	/// muda a cada tiro, e um arquivo por tiro seria o engasgo.
	/// </summary>
	private int EscreverNaGaveta(int gaveta, params double[] valores)
	{
		if (gaveta <= 0) gaveta = _cofreDeTech.Proximo++;
		_cofreDeTech.Numeros[gaveta] = valores;
		_cofreSujo = true;
		return gaveta;
	}

	private bool _cofreSujo;
	private double _relogioDoCofre;

	/// <summary>O cofre que mudou vai pro disco, no maximo de dez em dez segundos.</summary>
	private void DescarregarOCofre(double dt)
	{
		_relogioDoCofre += dt;
		if (_relogioDoCofre < 10) return;
		_relogioDoCofre = 0;
		if (!_cofreSujo) return;
		_cofreSujo = false;
		GravarOCofreDeTech();
	}

	/// <summary>O numero que o id carrega (o primeiro campo), ou zero.</summary>
	private static int NumeroDoCofre(string id) => int.TryParse(TechDeBolso.Campo(id, 0), out int n) && n > 0 ? n : 0;

	// =====================================================================
	// O CANAL: as acoes dos itens de bolso
	// =====================================================================
	/// <summary>
	/// AS ACOES DA TECH DE BOLSO. Devolve falso quando a acao nao e daqui (e ai vale o "ainda nao faz nada" do
	/// `ComandoDeItem`). Quem chama ja conferiu que o item esta na mochila e que a acao pertence a ele.
	/// </summary>
	private bool AcaoDeTechDeBolso(ServerPlayer pl, ItemDef def, string acao, string arg2)
	{
		switch (acao)
		{
			case TechDeBolso.AcaoDoar: DoarSangue(pl, def); return true;
			case TechDeBolso.AcaoBeber: BeberSangue(pl, def); return true;
			case TechDeBolso.AcaoPescar: Pescar(pl); return true;
			case TechDeBolso.AcaoEscrever: EscreverNoItem(pl, def, arg2); return true;
			case TechDeBolso.AcaoFolhear: LerOItem(pl, def); return true;
			case TechDeBolso.AcaoGravar: GravarSenhaNaChave(pl, def, arg2); return true;
			case TechDeBolso.AcaoTrancar: TrancarComAChave(pl, def); return true;
			case TechDeBolso.AcaoReforcar: ReforcarAChave(pl, def, arg2); return true;
			// O FABRICADOR: `usr.OpenTechWindow()` (`TechResearch.dm:20-24`). A janela e a aba Tech, que o cliente abre
			// no clique; o que o servidor faz e mandar o catalogo fresco pra ela.
			case TechDeBolso.AcaoFabricar: MandarCatalogoDeObras(pl); return true;
			case TechDeBolso.AcaoApontar: ApontarAAgulha(pl); return true;
			case TechDeBolso.AcaoExtrair: ExtrairDna(pl, def); return true;
			case TechDeBolso.AcaoAmostras: VerAsAmostras(pl, def); return true;
			// (lote T2) as armas, as bombas e as defesas -- `GameServer.Armas.cs`.
			default: return AcaoDeArmaOuBomba(pl, def, acao, arg2);
		}
	}

	/// <summary>
	/// TROCA O ID DE UMA UNIDADE NA MOCHILA -- e assim que o estado de um item muda (ver `TechDeBolso`). No mesmo slot:
	/// o item nao pula de lugar na grade a cada tiro dado. Falso quando o id novo nao cabe no pacote.
	/// </summary>
	private bool TrocarItem(ServerPlayer pl, string idVelho, string idNovo)
	{
		if (idNovo.Length > TechDeBolso.MaxDoId) return false;
		int i = pl.Mochila.Pilhas.FindIndex(p => string.Equals(p.Id, idVelho, StringComparison.OrdinalIgnoreCase));
		if (i < 0) return false;
		if (pl.Mochila.Pilhas[i].Quantidade > 1)
		{
			// (uma unidade da pilha muda de estado: ela sai da pilha e ganha o slot dela)
			if (pl.Mochila.Cheio) return false;
			pl.Mochila.Pilhas[i] = pl.Mochila.Pilhas[i] with { Quantidade = pl.Mochila.Pilhas[i].Quantidade - 1 };
			pl.Mochila.Pilhas.Add(new Pilha(idNovo, 1));
		}
		else pl.Mochila.Pilhas[i] = new Pilha(idNovo, 1);
		MandarMochila(pl);
		return true;
	}

	/// <summary>Texto livre (o de um livro): sem controle, sem espaco nas pontas, cortado no teto.</summary>
	private static string TextoLivre(string? cru, int teto)
	{
		if (string.IsNullOrWhiteSpace(cru)) return "";
		var sb = new System.Text.StringBuilder(cru.Length);
		foreach (char c in cru.Trim())
			if (!char.IsControl(c)) sb.Append(c);
		string s = sb.ToString().Trim();
		return s.Length > teto ? s[..teto] : s;
	}

	// =====================================================================
	// A BOLSA DE SANGUE -- `obj/items/Blood_Bag` (`Consumables.dm:19-40`)
	// =====================================================================
	/// <summary>`usr.SpreadDamage(10)`: o que doar custa.</summary>
	private const double DanoDeDoarSangue = 10;

	/// <summary>Abaixo disto de vida ninguem doa: o `SpreadDamage(10)` derrubaria quem ja esta no fim.</summary>
	private const double VidaPraDoarSangue = 30;

	/// <summary>
	/// DOAR. O original escreve `if(!usr.Race=="Android"&amp;&amp;!usr.IsAVampire)` -- o `!` casa antes do `==`, a conta e
	/// sempre falsa e NINGUEM consegue encher a bolsa. O que se porta e a intencao escrita no proprio aviso do verb
	/// (*"Will only work if you're a Non-Android"*): quem tem sangue doa, e a doacao tira dez de vida.
	/// </summary>
	private void DoarSangue(ServerPlayer pl, ItemDef def)
	{
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "caído, você não doa nada."); return; }
		if (pl.Race is "Android" or "Cyborg") { Avisar(pl, "um corpo de máquina não tem sangue pra doar."); return; }
		if (pl.Ficha.HP < VidaPraDoarSangue) { Avisar(pl, "você está ferido demais pra doar sangue."); return; }

		if (!TrocarItem(pl, def.Id, TechDeBolso.Com(TechDeBolso.BolsaDeSangue, "cheia"))) { Avisar(pl, "sua mochila está cheia."); return; }
		EspalharDanoDaCarga(pl, DanoDeDoarSangue);
		Falar(pl, Protocol.Fala.Emote, "enche uma bolsa com o próprio sangue.");
	}

	/// <summary>
	/// BEBER. So um vampiro tira proveito (`if(usr.IsAVampire)`, `Consumables.dm:33`): +30 de nutricao, +10 de vigor e
	/// dez de cura. Este port nao tem vampiros (`Vampires.dm` nao foi portado) -- a recusa e a do original pra todo
	/// mundo que nao e um, e a bolsa continua cheia.
	/// </summary>
	private void BeberSangue(ServerPlayer pl, ItemDef def) =>
		Avisar(pl, "só um vampiro tira proveito disto -- e você não é um. A bolsa continua cheia.");

	// =====================================================================
	// A PESCA -- `obj/items/FishingPole` (`Stamina/Fishing.dm`). A regra esta em `Pesca`.
	// =====================================================================
	private sealed class Pescaria
	{
		public Vec2 Onde;
		public int Agua;
		public double Relogio;
		public int Next, PegosHaPouco;
		public long FisgouAte;
	}

	private readonly Dictionary<int, Pescaria> _pescarias = [];

	/// <summary>Quantos tiles de agua ha no raio da pesca (`for(var/turf/T in view(2)) if(T.Water) waterNearby += 1`).</summary>
	private int AguaPerto(ServerPlayer pl)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		if (mapa == null) return 0;
		(int cx, int cy) = CelulaDoPonto(pl.Pos);
		int n = 0;
		for (int dy = -Pesca.RaioDeAgua; dy <= Pesca.RaioDeAgua; dy++)
			for (int dx = -Pesca.RaioDeAgua; dx <= Pesca.RaioDeAgua; dx++)
			{
				int x = cx + dx, y = cy + dy;
				if (x >= 0 && y >= 0 && x < mapa.Width && y < mapa.Height && mapa.EhAgua(x, y)) n++;
			}
		return n;
	}

	/// <summary>
	/// O VERB `Fish()`: lanca a linha, recolhe -- ou PUXA, se o peixe fisgou (no original puxar e clicar na boia; ver
	/// o cabecalho do `Pesca`).
	/// </summary>
	private void Pescar(ServerPlayer pl)
	{
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "caído, você não pesca."); return; }

		if (_pescarias.TryGetValue(pl.Id, out Pescaria? p))
		{
			if (p.FisgouAte != 0 && NowMs() <= p.FisgouAte) { PuxarOPeixe(pl, p); return; }
			_pescarias.Remove(pl.Id);
			Avisar(pl, "você recolhe a linha.");
			return;
		}

		// `if(usr.FishingSkill<1) "You don't know how to fish?"` (`Fishing.dm:14-16`).
		if (pl.Ficha.FishingSkill < 1) { Avisar(pl, "você não sabe pescar."); return; }
		if (pl.Voando || pl.Nadando) { Avisar(pl, "pesca-se com os pés no chão."); return; }
		int agua = AguaPerto(pl);
		if (agua <= 0) { Avisar(pl, "você precisa estar perto da água pra pescar."); return; }

		_pescarias[pl.Id] = new Pescaria { Onde = pl.Pos, Agua = agua };
		Avisar(pl, "você lança a linha. Não se mexa, senão espanta os peixes!");
		Falar(pl, Protocol.Fala.Emote, "lança a linha na água.");
	}

	private void PuxarOPeixe(ServerPlayer pl, Pescaria p)
	{
		p.FisgouAte = 0;
		string peixe = Pesca.Peixe(pl.Ficha.FishingSkill, () => _rng.Next(1, 4), () => _rng.NextDouble() * 100);
		if (pl.Mochila.Guardar(peixe) > 0)
		{
			Avisar(pl, $"sua mochila está cheia ({Inventario.Slots} espaços): o peixe escapa.");
			return;
		}
		MandarMochila(pl);
		p.PegosHaPouco++;
		pl.Ficha.FishingSkill += 1;   // `usr.FishingSkill += 1` (`Fishing.dm:48`)
		Avisar(pl, $"você pescou: {CatalogoDeItens.Get(peixe)?.Nome ?? peixe}! (perícia de pesca {pl.Ficha.FishingSkill:0})");
		Falar(pl, Protocol.Fala.Emote, "pesca um peixe!");
	}

	/// <summary>O laco da pesca (`while(usr.fishing)`, `Fishing.dm:30-52`), por tique do servidor.</summary>
	private void TickDaPesca(double dt)
	{
		if (_pescarias.Count == 0) return;
		long agora = NowMs();
		foreach ((int id, Pescaria p) in _pescarias.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Ficha.KO || pl.Ficha.dead)
			{
				_pescarias.Remove(id);
				continue;
			}
			// `Don't move or else you'll scare the fish!` -- e sem a vara na mochila nao ha linha na agua.
			if ((pl.Pos - p.Onde).LengthSquared > 16f || pl.Mochila.Quantos(TechDeBolso.Vara) <= 0)
			{
				_pescarias.Remove(id);
				Avisar(pl, "você se mexeu e espantou os peixes.");
				continue;
			}

			p.Relogio += dt;
			if (p.Relogio < Pesca.SegundosPorVolta) continue;
			p.Relogio -= Pesca.SegundosPorVolta;

			if (p.FisgouAte != 0)
			{
				if (agora <= p.FisgouAte) continue;   // a boia ainda esta de pe: ele pode puxar
				p.FisgouAte = 0;
				Avisar(pl, "o peixe escapou.");
				continue;
			}

			if (p.PegosHaPouco > 0) p.PegosHaPouco--;
			if (_rng.NextDouble() < Pesca.ChanceDeAndar) p.Next++;
			if (Pesca.NuncaFisgaDeTeste || !Pesca.Mordeu(p.Next, p.PegosHaPouco, p.Agua, pl.Ficha.FishingSkill)) continue;

			p.Next = 0;
			p.FisgouAte = agora + (long)(Pesca.SegundosPraPuxar * 1000);
			Avisar(pl, $"FISGOU! Puxe a linha (use a vara de novo) em {Pesca.SegundosPraPuxar:0} segundos.");
		}
	}

	// =====================================================================
	// O LIVRO E O PDA -- `obj/items/Book` (`Inventory.dm:7-19`) e as notas do `obj/items/PDA` (`Camera.dm:31-45`)
	// =====================================================================
	/// <summary>`book = input(usr,"Book","Book",book) as message`: o texto novo SUBSTITUI o que estava escrito.</summary>
	private void EscreverNoItem(ServerPlayer pl, ItemDef def, string cru)
	{
		string texto = TextoLivre(cru, TechDeBolso.MaxDeTexto);
		if (texto.Length == 0) { Avisar(pl, "escreva alguma coisa."); return; }

		int numero = NumeroDoCofre(def.Id);
		string id = def.Id;
		if (numero == 0)
		{
			// O NUMERO ENTRA NO PRIMEIRO CAMPO, e os outros ficam onde estavam (as frequencias e a tranca do PDA moram
			// nos seguintes -- ver `Vigilancia`).
			numero = _cofreDeTech.Proximo;
			string[] campos = TechDeBolso.Campos(def.Id);
			if (campos.Length == 0) campos = [""];
			campos[0] = numero.ToString();
			id = TechDeBolso.Com(TechDeBolso.Base(def.Id), campos);
			if (!TrocarItem(pl, def.Id, id)) { Avisar(pl, "sua mochila está cheia."); return; }
			_cofreDeTech.Proximo++;
		}
		_cofreDeTech.Textos[numero] = texto;
		GravarOCofreDeTech();
		Avisar(pl, $"você escreve em {CatalogoDeItens.Get(id)?.Nome ?? def.Nome}.");
	}

	/// <summary>`usr&lt;&lt;browse(book, ...)`: quem tem o livro nas maos le.</summary>
	private void LerOItem(ServerPlayer pl, ItemDef def)
	{
		if (!_cofreDeTech.Textos.TryGetValue(NumeroDoCofre(def.Id), out string? texto) || texto.Length == 0)
		{ Avisar(pl, $"{def.Nome} está em branco."); return; }
		Avisar(pl, $"-- {def.Nome} --");
		Avisar(pl, texto);
	}

	// =====================================================================
	// A CHAVE -- `obj/items/Key` (`Tier 1.dm:175-237`)
	// =====================================================================
	// No original a chave guarda senhas e o `Door/Enter` deixa passar quem carrega uma chave com a senha da porta. A
	// porta deste port ja LEMBRA de quem digitou a senha uma vez (a conta entra na lista dela); o que a chave
	// acrescenta e o que so um objeto faz: ela passa de mao em mao. Quem CARREGA a chave passa -- inclusive quem nunca
	// soube a senha -- e quem a perde deixa de passar. Ver `PortaAbrePara` (`GameServer.Blocos.cs`).

	/// <summary>
	/// GRAVAR. A chave comum guarda UMA senha (a nova substitui a antiga, `Tier 1.dm:188-193`); a reforcada guarda
	/// varias, e gravar de novo uma que ja esta nela a APAGA (o "Remove One" de `:196-199`).
	/// </summary>
	private void GravarSenhaNaChave(ServerPlayer pl, ItemDef def, string crua)
	{
		string senha = TechDeBolso.Limpo(crua, TechDeBolso.MaxDeSenhaNaChave);
		if (senha.Length == 0) { Avisar(pl, "digite a senha que a chave vai guardar."); return; }

		bool reforcada = TechDeBolso.ChaveReforcada(def.Id);
		List<string> senhas = [.. TechDeBolso.SenhasDaChave(def.Id)];
		bool apagou = false;
		if (!reforcada) senhas = [senha];
		else if (senhas.Remove(senha)) apagou = true;
		else senhas.Add(senha);

		if (!TrocarItem(pl, def.Id, TechDeBolso.ChaveCom(reforcada, senhas)))
		{ Avisar(pl, "a chave não comporta mais senhas."); return; }
		Avisar(pl, apagou ? "a senha foi apagada da chave."
						  : reforcada ? $"senha gravada. A chave guarda {senhas.Count} agora."
									  : "senha gravada na chave. Quem carregar esta chave passa pela porta que a usa.");
	}

	/// <summary>`Set_Password_On_Door` (`Tier 1.dm:233-237`): a porta de quem pede passa a usar a senha da chave.</summary>
	private void TrancarComAChave(ServerPlayer pl, ItemDef def)
	{
		string[] senhas = TechDeBolso.SenhasDaChave(def.Id);
		if (senhas.Length == 0) { Avisar(pl, "a chave não tem senha gravada."); return; }
		TrocarSenha(pl, senhas[0]);
	}

	/// <summary>
	/// REFORCAR (`verb/Upgrade`, `Tier 1.dm:219-232`): mil zeni, e a chave passa a guardar mais de uma senha. No
	/// original o preco e anunciado e nao e cobrado (falta o `usr.zenni -= 1000`); aqui ele e cobrado.
	/// </summary>
	private void ReforcarAChave(ServerPlayer pl, ItemDef def, string arg2)
	{
		if (TechDeBolso.ChaveReforcada(def.Id)) { Avisar(pl, "esta chave já foi reforçada."); return; }
		string nova = TechDeBolso.ChaveCom(true, TechDeBolso.SenhasDaChave(def.Id));
		if (nova.Length > TechDeBolso.MaxDoId) { Avisar(pl, "esta chave não comporta mais nada."); return; }
		// EM ZENI OU EM RECURSOS (a moeda vem colada no pedido, como em toda melhoria paga da mochila).
		(_, Moeda moeda) = Recurso.LerPedidoPago(arg2);
		if (!CobrarOuExplicar(pl, TechDeBolso.CustoDeReforcarAChave, moeda, "reforçar a chave")) return;
		TrocarItem(pl, def.Id, nova);
		Avisar(pl, "chave reforçada: ela guarda mais de uma senha agora.");
	}

	// =====================================================================
	// A AGULHA DE MASMORRA -- `obj/items/Dungeon_Needle` (`dungeon_obj.dm:210-264`)
	// =====================================================================
	/// <summary>
	/// APONTAR (`verb/Point`): varre a lista de masmorras e diz a direcao da mais proxima. As masmorras do original
	/// (`Code/Modules/Dungeons`) nao existem neste port: a lista esta vazia, e a agulha responde o que responde num
	/// mundo sem nenhuma.
	/// </summary>
	private void ApontarAAgulha(ServerPlayer pl) =>
		Avisar(pl, "a agulha gira e não se decide: não há nenhuma masmorra neste mundo.");

	// =====================================================================
	// O RECIPIENTE DE DNA -- `obj/items/DNA_Container` (`DNALabs.dm:280-326`)
	// =====================================================================
	/// <summary>
	/// EXTRAIR (`verb/Extract_DNA`): a amostra de quem esta nocauteado ao lado fica guardada DENTRO do recipiente
	/// (`S.loc = src`). O criterio de quem serve de doador e o do laboratorio deste port (`ColherDna`): gente.
	/// </summary>
	private void ExtrairDna(ServerPlayer pl, ItemDef def)
	{
		ServerPlayer? vitima = _players.Values
			.Where(o => o != pl && EhJogador(o) && o.Zone.Equals(pl.Zone) && (o.Ficha.KO || o.Ficha.dead))
			.OrderBy(o => Vec2.Distance(o.Pos, pl.Pos))
			.FirstOrDefault(o => Vec2.Distance(o.Pos, pl.Pos) <= AlcanceDeUso);
		if (vitima == null) { Avisar(pl, "não há ninguém caído ao seu alcance: o doador precisa estar nocauteado, ao seu lado."); return; }

		int numero = NumeroDoCofre(def.Id);
		List<Amostra> guardadas = numero != 0 && _cofreDeTech.Amostras.TryGetValue(numero, out List<Amostra>? ja) ? ja : [];
		if (guardadas.Count >= BioMaxDna) { Avisar(pl, $"o recipiente só comporta {BioMaxDna} amostras."); return; }
		if (guardadas.Any(a => a.Assinatura == vitima.Assinatura)) { Avisar(pl, $"o recipiente já tem uma amostra de {vitima.Name}."); return; }

		if (numero == 0)
		{
			numero = _cofreDeTech.Proximo;
			if (!TrocarItem(pl, def.Id, TechDeBolso.Com(TechDeBolso.ContainerDeDna, numero.ToString()))) { Avisar(pl, "sua mochila está cheia."); return; }
			_cofreDeTech.Proximo++;
		}
		guardadas.Add(new Amostra
		{
			Raca = vitima.Race,
			RacaDoPai = vitima.Ficha.ParentRace,
			Doador = vitima.Name,
			Assinatura = vitima.Assinatura,
			Bp = vitima.Ficha.BP,
			Skills = [.. TecnicasHerdaveis(vitima)],
		});
		_cofreDeTech.Amostras[numero] = guardadas;
		GravarOCofreDeTech();
		Avisar(pl, $"amostra coletada e guardada no recipiente: {Jandirus.Core.Races.NomeDaRaca.Bonito(vitima.Race)} de {vitima.Name} "
				   + $"({guardadas.Count}/{BioMaxDna}).");
		Avisar(vitima, "alguém enfia uma agulha em você enquanto você está caído.");
	}

	/// <summary>`verb/Ver_Amostras` (`DNALabs.dm:319-326`).</summary>
	private void VerAsAmostras(ServerPlayer pl, ItemDef def)
	{
		if (!_cofreDeTech.Amostras.TryGetValue(NumeroDoCofre(def.Id), out List<Amostra>? guardadas) || guardadas.Count == 0)
		{ Avisar(pl, "o recipiente está vazio."); return; }
		for (int i = 0; i < guardadas.Count; i++)
			Avisar(pl, $"  {i + 1}. {Jandirus.Core.Races.NomeDaRaca.Bonito(guardadas[i].Raca)} ({guardadas[i].Doador})");
	}

	/// <summary>
	/// NO LABORATORIO, SEM NINGUEM CAIDO POR PERTO: as amostras dos recipientes da mochila vao pro tanque (o
	/// `dnl_sample_owned` do original aceita a amostra que esta "dentro de um container carregado", `DNALabs.dm:329-334`).
	/// Devolve quantas entraram; o recipiente esvaziado volta a ser um recipiente novo.
	/// </summary>
	private int DespejarOsRecipientesNoLab(ServerPlayer pl, Obra lab)
	{
		int entraram = 0;
		foreach (Pilha p in pl.Mochila.Pilhas.ToList())
		{
			if (TechDeBolso.Base(p.Id) != TechDeBolso.ContainerDeDna) continue;
			int numero = NumeroDoCofre(p.Id);
			if (numero == 0 || !_cofreDeTech.Amostras.TryGetValue(numero, out List<Amostra>? guardadas)) continue;

			lab.Fornada ??= new Gestacao { DonoConta = pl.Conta };
			foreach (Amostra a in guardadas.ToList())
			{
				if (lab.Fornada.Amostras.Count >= BioMaxDna) break;
				if (lab.Fornada.Amostras.Any(x => x.Assinatura == a.Assinatura)) { guardadas.Remove(a); continue; }
				lab.Fornada.Amostras.Add(a);
				guardadas.Remove(a);
				entraram++;
			}
			if (guardadas.Count > 0) continue;
			_cofreDeTech.Amostras.Remove(numero);
			TrocarItem(pl, p.Id, TechDeBolso.ContainerDeDna);
		}
		if (entraram > 0) { GravarOCofreDeTech(); GravarMundo(); }
		return entraram;
	}

	// =====================================================================
	// OS DEPOSITOS -- o bau, a geladeira, a base e a fortaleza. A regra esta em `Deposito`.
	// =====================================================================
	/// <summary>Quem esta com a tela de que deposito aberta: jogador -> id da obra. So memoria -- a tela se reabre.</summary>
	private readonly Dictionary<int, int> _bauAberto = [];

	private readonly Dictionary<int, long> _descansoEm = [];

	/// <summary>Os verbos das maquinas portadas na v0.2.1. Falso quando o verbo nao e daqui.</summary>
	private bool ComandoDeTechPortada(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case Deposito.VerboAbrir: AbrirODeposito(pl); return true;
			case Deposito.VerboDescansar: DescansarNaBase(pl); return true;
			case Deposito.VerboDesmontar: DesmontarABase(pl); return true;
			case ScannerMedico.VerboEu: ExaminarComOScanner(pl, noAlvo: false); return true;
			case ScannerMedico.VerboAlvo: ExaminarComOScanner(pl, noAlvo: true); return true;
			// (lote T3) as maquinas de chao -- `GameServer.MaquinasDeChao.cs`.
			default: return ComandoDeMaquinaDeChao(pl, cmd, arg);
		}
	}

	private void AbrirODeposito(ServerPlayer pl)
	{
		Obra? o = ObraQueAceita(pl, Deposito.VerboAbrir);
		if (o == null) { Avisar(pl, "não há onde guardar nada por perto."); return; }
		if (Deposito.PedeParafuso(o.Tipo) && !o.Aparafusada) { Avisar(pl, $"aparafuse {NomeDaObra(o)} antes de usar."); return; }
		_bauAberto[pl.Id] = o.Id;
		MandarODeposito(pl, o);
	}

	/// <summary>O deposito que este jogador tem aberto E ainda alcanca, ou nulo (e ai a tela dele fecha).</summary>
	private Obra? DepositoAberto(ServerPlayer pl)
	{
		if (!_bauAberto.TryGetValue(pl.Id, out int id)) return null;
		Obra? o = _noChao.FirstOrDefault(x => x.Id == id && x.Zona.Equals(pl.Zone) && Deposito.Guarda(x.Tipo)
											  && Math.Abs(x.X - pl.Pos.X) <= AlcanceDeUso && Math.Abs(x.Y - pl.Pos.Y) <= AlcanceDeUso);
		if (o != null) return o;
		_bauAberto.Remove(pl.Id);
		MandarSaqueFechado(pl);
		return null;
	}

	/// <summary>
	/// A TELA DO DEPOSITO: o MESMO pacote da mochila de um nocauteado (`S2C.Saque`), com o id NEGATIVO -- e por ele
	/// que a tela do cliente sabe que e um bau (o titulo, e o botao de guardar). A carteira vai zerada: bau nao tem
	/// zeni.
	/// </summary>
	private void MandarODeposito(ServerPlayer pl, Obra o)
	{
		if (pl.Peer == null) return;
		List<Pilha> itens = o.Tech?.Itens ?? [];
		var w = Protocol.Begin(Protocol.S2C.Saque);
		w.Put(-o.Id);
		w.Put(NomeDaObra(o));
		w.Put((byte)Math.Min(itens.Count, Inventario.Slots));
		for (int i = 0; i < itens.Count && i < Inventario.Slots; i++)
		{
			w.Put(itens[i].Id);
			w.Put((ushort)Math.Clamp(itens[i].Quantidade, 0, ushort.MaxValue));
		}
		w.Put(0.0);
		w.Put(0.0);
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	private static bool EDonoDaObra(Obra o, ServerPlayer pl) =>
		o.DonoConta.Length > 0 && string.Equals(o.DonoConta, pl.Conta, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// TIRAR -- o `Loot()` do bau e o "Retrieve Item" da base. Chega pelo verbo `saquear` (o clique na tela e o mesmo
	/// de revistar um nocauteado); devolve falso quando o jogador nao tem deposito aberto, e ai o verbo segue pro saque.
	/// </summary>
	private bool TirarDoDepositoAberto(ServerPlayer pl, string item)
	{
		// A PERGUNTA do saque (sem item) e sempre sobre um CAIDO: ela fecha a sessao do deposito.
		if (item.Length == 0) { _bauAberto.Remove(pl.Id); return false; }
		if (!_bauAberto.ContainsKey(pl.Id)) return false;
		if (DepositoAberto(pl) is not { } o) { Avisar(pl, "você se afastou."); return true; }

		if (Deposito.SoODonoTira(o.Tipo) && !EDonoDaObra(o, pl) && !Deposito.QualquerUmTiraDeTeste)
		{ Avisar(pl, $"só o dono tira coisas de {NomeDaObra(o)}."); return true; }

		EstadoDeTech estado = o.Tech ??= new EstadoDeTech();
		int i = estado.Itens.FindIndex(p => p.Id == item);
		if (i < 0) { Avisar(pl, "isso não está mais aí."); MandarODeposito(pl, o); return true; }
		if (pl.Mochila.Guardar(item, 1) > 0) { Avisar(pl, $"sua mochila está cheia ({Inventario.Slots} espaços)."); return true; }

		if (estado.Itens[i].Quantidade > 1) estado.Itens[i] = estado.Itens[i] with { Quantidade = estado.Itens[i].Quantidade - 1 };
		else estado.Itens.RemoveAt(i);
		GravarMundo();
		MandarMochila(pl);
		MandarODeposito(pl, o);
		return true;
	}

	/// <summary>
	/// GUARDAR -- o `Put()` do bau e o "Store Item" da base: a pilha inteira sai da mochila e entra no deposito aberto.
	/// O que esta em uso nao se guarda (`if(!A.equipped)`, `Container.dm:44`).
	/// </summary>
	private void GuardarNoDeposito(ServerPlayer pl, ItemDef def)
	{
		if (DepositoAberto(pl) is not { } o) { Avisar(pl, "abra um baú antes de guardar."); return; }
		if (Deposito.SoComida(o.Tipo) && def.Nutricao <= 0) { Avisar(pl, $"{NomeDaObra(o)} só guarda comida."); return; }
		if (def.Id == CatalogoDeItens.Scouter && (pl.PoderesConcedidos & Protocol.Poder.Scouter) != 0)
		{ Avisar(pl, "desligue o scouter antes de guardar."); return; }
		if (def.Id == CatalogoDeItens.Pesos && pl.Ficha.Weighted > 0) { Avisar(pl, "tire os pesos antes de guardar."); return; }

		int quantos = pl.Mochila.Quantos(def.Id);
		if (quantos <= 0) return;
		EstadoDeTech estado = o.Tech ??= new EstadoDeTech();
		int i = def.Empilhavel ? estado.Itens.FindIndex(p => p.Id == def.Id) : -1;
		int vagas = Inventario.Slots - estado.Itens.Count;
		int cabem = i >= 0 || def.Empilhavel ? quantos : Math.Min(quantos, vagas);
		if (cabem <= 0 || (i < 0 && vagas <= 0)) { Avisar(pl, $"{NomeDaObra(o)} está cheio."); return; }

		pl.Mochila.Tirar(def.Id, cabem);
		if (i >= 0) estado.Itens[i] = estado.Itens[i] with { Quantidade = estado.Itens[i].Quantidade + cabem };
		else if (def.Empilhavel) estado.Itens.Add(new Pilha(def.Id, cabem));
		else for (int k = 0; k < cabem; k++) estado.Itens.Add(new Pilha(def.Id, 1));
		GravarMundo();
		MandarMochila(pl);
		MandarODeposito(pl, o);
		Avisar(pl, $"você guarda {def.Nome}{(cabem > 1 ? $" (x{cabem})" : "")} em {NomeDaObra(o)}.");
	}

	/// <summary>O "Rest" da base e da fortaleza (`PlanetTech.dm:465-469` e `:536-540`). Ver `Deposito.Descanso`.</summary>
	private void DescansarNaBase(ServerPlayer pl)
	{
		Obra? o = ObraQueAceita(pl, Deposito.VerboDescansar);
		if (o == null) { Avisar(pl, "não há base por perto."); return; }
		if (pl.Combate is not { } c || pl.Ficha.KO || EhCadaver(pl)) return;
		if (c.EmCombate > 0) { Avisar(pl, "não dá pra descansar no meio de uma briga."); return; }
		long agora = NowMs();
		if (agora < _descansoEm.GetValueOrDefault(pl.Id)) return;
		_descansoEm[pl.Id] = agora + (long)(Deposito.SegundosEntreDescansos * 1000);

		(double cura, double fracaoDoKi) = Deposito.Descanso(o.Tipo);
		c.Corpo.CurarVitais(cura);
		c.SincronizarVida();
		pl.Ficha.Ki = Math.Min(pl.Ficha.MaxKi, pl.Ficha.Ki + pl.Ficha.MaxKi * fracaoDoKi);
		MandarFicha(pl);
		Avisar(pl, $"você descansa em {NomeDaObra(o)} e recupera um pouco de energia.");
	}

	/// <summary>`verb/Dismantle` (`PlanetTech.dm:503-512`): so o dono, e o que estava guardado cai no chao.</summary>
	private void DesmontarABase(ServerPlayer pl)
	{
		Obra? o = ObraQueAceita(pl, Deposito.VerboDesmontar);
		if (o == null) { Avisar(pl, "não há base por perto."); return; }
		if (!EDonoDaObra(o, pl)) { Avisar(pl, $"só o dono desmonta {NomeDaObra(o)}."); return; }

		OQueEstavaGuardadoCai(o);
		_noChao.Remove(o);
		GravarMundo();
		AplicarColisaoDasObras(o.Zona);
		MandarObras(o.Zona);
		Avisar(pl, $"você desmonta {NomeDaObra(o)}. O que estava guardado caiu no chão.");
	}

	/// <summary>
	/// O QUE UM DEPOSITO GUARDAVA VAI PRO CHAO -- quando ele e desmontado, e quando ele e DESTRUIDO (`Estragar`): no
	/// original o que esta no `contents` de um objeto apagado some com ele; aqui cai, que e o que o `Dismantle` faz.
	/// </summary>
	private void OQueEstavaGuardadoCai(Obra o)
	{
		if (o.Tech is not { Itens.Count: > 0 } estado) return;
		foreach (Pilha p in estado.Itens)
			PorNoChao(o.Zona, new Vec2(o.X, o.Y), TipoDeLargado.Item, p.Id, p.Quantidade);
		estado.Itens.Clear();
		foreach ((int quem, int id) in _bauAberto.ToList())
			if (id == o.Id) _bauAberto.Remove(quem);
	}

	// =====================================================================
	// O SCANNER MEDICO -- `obj/items/Medical_Scanner` (`Tier 1.5.dm:422-441`)
	// =====================================================================
	/// <summary>
	/// O `scanperson()`: a idade, a idade em que o corpo comeca a declinar, a raca, o genero, o dano, a energia, a
	/// gravidade dominada e o estado de cada membro. NENHUM NUMERO DE PODER -- o original tambem nao mostra.
	/// </summary>
	private void ExaminarComOScanner(ServerPlayer pl, bool noAlvo)
	{
		string verbo = noAlvo ? ScannerMedico.VerboAlvo : ScannerMedico.VerboEu;
		Obra? o = ObraQueAceita(pl, verbo);
		if (o == null) { Avisar(pl, "não há scanner por perto."); return; }

		ServerPlayer paciente = pl;
		if (noAlvo)
		{
			float alcance = ScannerMedico.AlcanceEmTiles * ZoneCollision.TileSize;
			if (pl.AlvoId == 0 || !_players.TryGetValue(pl.AlvoId, out ServerPlayer? marcado) || !marcado.Zone.Equals(pl.Zone)
				|| Vec2.Distance(marcado.Pos, new Vec2(o.X, o.Y)) > alcance || marcado.Combate == null)
			{ Avisar(pl, $"marque alguém que esteja a até {ScannerMedico.AlcanceEmTiles} tiles do scanner."); return; }
			paciente = marcado;
		}
		if (paciente.Combate is not { } c) return;

		Jandirus.Core.Stats.Fighter f = paciente.Ficha;
		double auge = Jandirus.Core.Races.Envelhecimento.AugeDaRaca(paciente.Race);
		Avisar(pl, $"-- {NomeDaObra(o)}: {NomeVisivel(paciente)} --");
		Avisar(pl, $"  idade física: {f.Idade:0}" + (auge > 0 ? $" | o declínio começa aos {auge:0}" : " | este corpo não envelhece"));
		Avisar(pl, $"  raça: {Jandirus.Core.Races.NomeDaRaca.Bonito(paciente.Race)}" + (f.Class.Length > 0 ? $" ({f.Class})" : "")
				   + $" | gênero: {paciente.Genero}");
		Avisar(pl, $"  dano: {Math.Round(100 - f.HP):0}% | energia: {f.Ki:N0} | gravidade dominada: {Math.Round(f.GravMastered):0}x");
		foreach (Jandirus.Core.Combat.BodyPart parte in c.Corpo.Partes)
			Avisar(pl, parte.Decepado ? $"  {parte.Nome}: FALTANDO" : $"  {parte.Nome}: {parte.Vida:0} de {parte.VidaMax:0}");
		if (paciente != pl) AvisarSePessoa(paciente, $"{NomeVisivel(pl)} examina você com o scanner médico.");
	}

	/// <summary>O tique das maquinas e dos itens portados na v0.2.1. Chamado do `Tick()`.</summary>
	private void TickDaTechPortada(double dt)
	{
		TickDaPesca(dt);
		TickDasArmasEBombas(dt);
		TickDasMaquinasDeChao(dt);
		TickDasMaquinasGrandes(dt);
		TickDasComunicacoes();
		TickDosCompanheiros(dt);
		TickDoSintetizador(dt);
		TickDosSuperComputadores(dt);
		DescarregarOCofre(dt);
	}
}
