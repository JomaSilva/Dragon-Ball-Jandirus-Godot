namespace Jandirus.Core.Tech;

/// <summary>O tipo de um minijogo de estudo. Zero no fio = "a janela fecha".</summary>
public enum TipoDeMinijogo : byte
{
	Nenhum = 0,
	Conta = 1,
	Elemento = 2,
	Molecula = 3,
}

/// <summary>
/// UM DESAFIO ABERTO: o tipo, o indice na tabela (o numero atomico do elemento, a posicao da molecula), a conta montada e a forma da frase.
/// A RESPOSTA fica no servidor e nao vai pro fio.
/// </summary>
public sealed class Minijogo
{
	public TipoDeMinijogo Tipo;
	/// <summary>Elemento: o numero atomico (1-118). Molecula: a posicao em <see cref="Minijogos.Moleculas"/>. Conta: zero.</summary>
	public int Indice;
	/// <summary>Conta: a expressao ("12 × 7 + 5"), igual em qualquer lingua.</summary>
	public string Expressao = "";
	/// <summary>Qual das frases do tipo (ver <see cref="Minijogos.Pergunta"/>): a variacao do enunciado.</summary>
	public byte Forma;
	/// <summary>Conta: o resultado. Elemento: o simbolo. Molecula: vazio (a regra das ligacoes decide).</summary>
	public string Resposta = "";
	/// <summary>Quando o prazo acaba, em ms do relogio do servidor.</summary>
	public long Prazo;
}

/// <summary>
/// ============================ OS MINIJOGOS DE ESTUDO (dono, 2026-10-27, o pedido 120) ============================
/// *"faça minigames de estudo q fazem vc ganhar tech mais rapido, ao ir na research bench e estudar, vao rolar minigames como vc ter q
/// montar uma molecula (de forma interativa juntando bolinhas etc), resolver uma conta de matemática (nada mt absurdo sempre ficando na
/// soma, multiplicação e divisao e ns divisao faça com multiplos pra n dar decimal), falar o simbolo de algum elemento da tabela
/// periodica, e cada acerto desse daria um bonus no ganho de tech/s, faca bastante variações de perguntas, coloque o todos os elementos da
/// tabela periodica na questão de falar o simbolo, enquanto quase nunca vai repetir questao, questão de matematica gerada
/// proceduralmente na hora"*. As escolhas dele: +25% por 10 min a cada acerto, ate +150%; um minijogo a cada minuto de estudo NA BANCADA,
/// 30 s pra responder; errar tira o bonus mais antigo; a molecula vale pelas LIGACOES certas (H 1, O 2, N 3, C 4; dupla = clicar de novo)
/// com tudo ligado num pedaco so.
///
/// TUDO AQUI E FUNCAO PURA E TABELA: quem abre, cronometra e paga e o servidor (`GameServer.Estudo.cs`); quem desenha e o cliente
/// (`TelaDoEstudo`). As DUAS pontas leem estas tabelas -- o fio leva so o indice, e cada cliente escreve o nome na lingua dele.
/// ==================================================================================================================
/// </summary>
public static class Minijogos
{
	/// <summary>A cada tantos segundos de estudo NA BANCADA abre um minijogo (o dono: um por minuto).</summary>
	public const int SegundosEntreDesafios = 60;

	/// <summary>O prazo pra responder (o dono: 30 s).</summary>
	public const int SegundosPraResponder = 30;

	/// <summary>Cada acerto: +25% no ganho de tech/s...</summary>
	public const double BonusPorAcerto = 0.25;

	/// <summary>...por 10 minutos...</summary>
	public const long MsDoBonus = 10 * 60 * 1000;

	/// <summary>...ate +150% (seis acertos valendo juntos).</summary>
	public const int TetoDeBonus = 6;

	/// <summary>O multiplicador do ganho de tech com tantos bonus valendo.</summary>
	public static double Multiplicador(int bonusAtivos) => 1 + BonusPorAcerto * Math.Clamp(bonusAtivos, 0, TetoDeBonus);

	// =====================================================================
	// AS CONTAS -- geradas na hora, so soma, multiplicacao e divisao exata
	// =====================================================================
	/// <summary>
	/// UMA CONTA NOVA: oito formas, todas com numeros de cabeca ("nada mt absurdo") e resultado inteiro -- a divisao sai de um produto
	/// (o dividendo e o divisor vezes o quociente), entao nunca da decimal. A precedencia nunca e armadilha: a multiplicacao vem antes.
	/// </summary>
	public static (string Expressao, long Resposta) Conta(Random r)
	{
		switch (r.Next(8))
		{
			case 0: { int a = r.Next(12, 500), b = r.Next(12, 500); return ($"{a} + {b}", a + b); }
			case 1: { int a = r.Next(5, 100), b = r.Next(5, 100), c = r.Next(5, 100); return ($"{a} + {b} + {c}", a + b + c); }
			case 2: { int a = r.Next(3, 13), b = r.Next(3, 13); return ($"{a} × {b}", a * b); }
			case 3: { int a = r.Next(11, 26), b = r.Next(3, 10); return ($"{a} × {b}", a * b); }
			case 4: { int b = r.Next(2, 13), q = r.Next(3, 26); return ($"{b * q} ÷ {b}", q); }
			case 5: { int a = r.Next(3, 13), b = r.Next(3, 13), c = r.Next(5, 60); return ($"{a} × {b} + {c}", a * b + c); }
			case 6: { int b = r.Next(2, 10), q = r.Next(3, 21), c = r.Next(5, 60); return ($"{b * q} ÷ {b} + {c}", q + c); }
			default: { int a = r.Next(2, 10), b = r.Next(2, 10), c = r.Next(2, 6); return ($"{a} × {b} × {c}", a * b * c); }
		}
	}

	// =====================================================================
	// OS 118 ELEMENTOS -- (numero atomico, simbolo, nome em portugues, nome em ingles)
	// =====================================================================
	public static readonly (int Z, string Simbolo, string Nome, string NomeEn)[] Elementos =
	[
		(1, "H", "Hidrogênio", "Hydrogen"), (2, "He", "Hélio", "Helium"), (3, "Li", "Lítio", "Lithium"), (4, "Be", "Berílio", "Beryllium"),
		(5, "B", "Boro", "Boron"), (6, "C", "Carbono", "Carbon"), (7, "N", "Nitrogênio", "Nitrogen"), (8, "O", "Oxigênio", "Oxygen"),
		(9, "F", "Flúor", "Fluorine"), (10, "Ne", "Neônio", "Neon"), (11, "Na", "Sódio", "Sodium"), (12, "Mg", "Magnésio", "Magnesium"),
		(13, "Al", "Alumínio", "Aluminium"), (14, "Si", "Silício", "Silicon"), (15, "P", "Fósforo", "Phosphorus"), (16, "S", "Enxofre", "Sulfur"),
		(17, "Cl", "Cloro", "Chlorine"), (18, "Ar", "Argônio", "Argon"), (19, "K", "Potássio", "Potassium"), (20, "Ca", "Cálcio", "Calcium"),
		(21, "Sc", "Escândio", "Scandium"), (22, "Ti", "Titânio", "Titanium"), (23, "V", "Vanádio", "Vanadium"), (24, "Cr", "Cromo", "Chromium"),
		(25, "Mn", "Manganês", "Manganese"), (26, "Fe", "Ferro", "Iron"), (27, "Co", "Cobalto", "Cobalt"), (28, "Ni", "Níquel", "Nickel"),
		(29, "Cu", "Cobre", "Copper"), (30, "Zn", "Zinco", "Zinc"), (31, "Ga", "Gálio", "Gallium"), (32, "Ge", "Germânio", "Germanium"),
		(33, "As", "Arsênio", "Arsenic"), (34, "Se", "Selênio", "Selenium"), (35, "Br", "Bromo", "Bromine"), (36, "Kr", "Criptônio", "Krypton"),
		(37, "Rb", "Rubídio", "Rubidium"), (38, "Sr", "Estrôncio", "Strontium"), (39, "Y", "Ítrio", "Yttrium"), (40, "Zr", "Zircônio", "Zirconium"),
		(41, "Nb", "Nióbio", "Niobium"), (42, "Mo", "Molibdênio", "Molybdenum"), (43, "Tc", "Tecnécio", "Technetium"),
		(44, "Ru", "Rutênio", "Ruthenium"), (45, "Rh", "Ródio", "Rhodium"), (46, "Pd", "Paládio", "Palladium"), (47, "Ag", "Prata", "Silver"),
		(48, "Cd", "Cádmio", "Cadmium"), (49, "In", "Índio", "Indium"), (50, "Sn", "Estanho", "Tin"), (51, "Sb", "Antimônio", "Antimony"),
		(52, "Te", "Telúrio", "Tellurium"), (53, "I", "Iodo", "Iodine"), (54, "Xe", "Xenônio", "Xenon"), (55, "Cs", "Césio", "Caesium"),
		(56, "Ba", "Bário", "Barium"), (57, "La", "Lantânio", "Lanthanum"), (58, "Ce", "Cério", "Cerium"), (59, "Pr", "Praseodímio", "Praseodymium"),
		(60, "Nd", "Neodímio", "Neodymium"), (61, "Pm", "Promécio", "Promethium"), (62, "Sm", "Samário", "Samarium"),
		(63, "Eu", "Európio", "Europium"), (64, "Gd", "Gadolínio", "Gadolinium"), (65, "Tb", "Térbio", "Terbium"),
		(66, "Dy", "Disprósio", "Dysprosium"), (67, "Ho", "Hólmio", "Holmium"), (68, "Er", "Érbio", "Erbium"), (69, "Tm", "Túlio", "Thulium"),
		(70, "Yb", "Itérbio", "Ytterbium"), (71, "Lu", "Lutécio", "Lutetium"), (72, "Hf", "Háfnio", "Hafnium"), (73, "Ta", "Tântalo", "Tantalum"),
		(74, "W", "Tungstênio", "Tungsten"), (75, "Re", "Rênio", "Rhenium"), (76, "Os", "Ósmio", "Osmium"), (77, "Ir", "Irídio", "Iridium"),
		(78, "Pt", "Platina", "Platinum"), (79, "Au", "Ouro", "Gold"), (80, "Hg", "Mercúrio", "Mercury"), (81, "Tl", "Tálio", "Thallium"),
		(82, "Pb", "Chumbo", "Lead"), (83, "Bi", "Bismuto", "Bismuth"), (84, "Po", "Polônio", "Polonium"), (85, "At", "Astato", "Astatine"),
		(86, "Rn", "Radônio", "Radon"), (87, "Fr", "Frâncio", "Francium"), (88, "Ra", "Rádio", "Radium"), (89, "Ac", "Actínio", "Actinium"),
		(90, "Th", "Tório", "Thorium"), (91, "Pa", "Protactínio", "Protactinium"), (92, "U", "Urânio", "Uranium"), (93, "Np", "Netúnio", "Neptunium"),
		(94, "Pu", "Plutônio", "Plutonium"), (95, "Am", "Amerício", "Americium"), (96, "Cm", "Cúrio", "Curium"), (97, "Bk", "Berquélio", "Berkelium"),
		(98, "Cf", "Califórnio", "Californium"), (99, "Es", "Einstênio", "Einsteinium"), (100, "Fm", "Férmio", "Fermium"),
		(101, "Md", "Mendelévio", "Mendelevium"), (102, "No", "Nobélio", "Nobelium"), (103, "Lr", "Laurêncio", "Lawrencium"),
		(104, "Rf", "Rutherfórdio", "Rutherfordium"), (105, "Db", "Dúbnio", "Dubnium"), (106, "Sg", "Seabórgio", "Seaborgium"),
		(107, "Bh", "Bóhrio", "Bohrium"), (108, "Hs", "Hássio", "Hassium"), (109, "Mt", "Meitnério", "Meitnerium"),
		(110, "Ds", "Darmstádio", "Darmstadtium"), (111, "Rg", "Roentgênio", "Roentgenium"), (112, "Cn", "Copernício", "Copernicium"),
		(113, "Nh", "Nihônio", "Nihonium"), (114, "Fl", "Fleróvio", "Flerovium"), (115, "Mc", "Moscóvio", "Moscovium"),
		(116, "Lv", "Livermório", "Livermorium"), (117, "Ts", "Tenesso", "Tennessine"), (118, "Og", "Oganessônio", "Oganesson"),
	];

	/// <summary>O elemento deste numero atomico.</summary>
	public static (int Z, string Simbolo, string Nome, string NomeEn) Elemento(int z) => Elementos[Math.Clamp(z, 1, Elementos.Length) - 1];

	// =====================================================================
	// AS MOLECULAS -- so H, C, N, O, F e Cl: as que fecham pela regra das ligacoes, sem carga
	// =====================================================================
	/// <summary>A VALENCIA de cada atomo: quantas ligacoes ele fecha (a dupla conta duas, a tripla tres).</summary>
	public static readonly Dictionary<string, int> Valencia = new() { ["H"] = 1, ["F"] = 1, ["Cl"] = 1, ["O"] = 2, ["N"] = 3, ["C"] = 4 };

	/// <summary>(nome em portugues, nome em ingles, formula, os atomos na mesa)</summary>
	public static readonly (string Nome, string NomeEn, string Formula, string[] Atomos)[] Moleculas =
	[
		("água", "water", "H2O", ["O", "H", "H"]),
		("gás hidrogênio", "hydrogen gas", "H2", ["H", "H"]),
		("gás oxigênio", "oxygen gas", "O2", ["O", "O"]),
		("gás nitrogênio", "nitrogen gas", "N2", ["N", "N"]),
		("gás carbônico", "carbon dioxide", "CO2", ["C", "O", "O"]),
		("metano", "methane", "CH4", ["C", "H", "H", "H", "H"]),
		("amônia", "ammonia", "NH3", ["N", "H", "H", "H"]),
		("ácido clorídrico", "hydrogen chloride", "HCl", ["H", "Cl"]),
		("fluoreto de hidrogênio", "hydrogen fluoride", "HF", ["H", "F"]),
		("gás cloro", "chlorine gas", "Cl2", ["Cl", "Cl"]),
		("etano", "ethane", "C2H6", ["C", "C", "H", "H", "H", "H", "H", "H"]),
		("eteno", "ethene", "C2H4", ["C", "C", "H", "H", "H", "H"]),
		("etino", "ethyne", "C2H2", ["C", "C", "H", "H"]),
		("metanol", "methanol", "CH4O", ["C", "O", "H", "H", "H", "H"]),
		("etanol", "ethanol", "C2H6O", ["C", "C", "O", "H", "H", "H", "H", "H", "H"]),
		("formaldeído", "formaldehyde", "CH2O", ["C", "O", "H", "H"]),
		("ácido cianídrico", "hydrogen cyanide", "HCN", ["H", "C", "N"]),
		("peróxido de hidrogênio", "hydrogen peroxide", "H2O2", ["O", "O", "H", "H"]),
		("hidrazina", "hydrazine", "N2H4", ["N", "N", "H", "H", "H", "H"]),
		("ácido fórmico", "formic acid", "CH2O2", ["C", "O", "O", "H", "H"]),
		("ureia", "urea", "CH4N2O", ["C", "O", "N", "N", "H", "H", "H", "H"]),
		("propano", "propane", "C3H8", ["C", "C", "C", "H", "H", "H", "H", "H", "H", "H", "H"]),
		("clorometano", "chloromethane", "CH3Cl", ["C", "Cl", "H", "H", "H"]),
		("metilamina", "methylamine", "CH5N", ["C", "N", "H", "H", "H", "H", "H"]),
		("acetaldeído", "acetaldehyde", "C2H4O", ["C", "C", "O", "H", "H", "H", "H"]),
		("ácido acético", "acetic acid", "C2H4O2", ["C", "C", "O", "O", "H", "H", "H", "H"]),
		("propeno", "propene", "C3H6", ["C", "C", "C", "H", "H", "H", "H", "H", "H"]),
		("hidroxilamina", "hydroxylamine", "NH3O", ["N", "O", "H", "H", "H"]),
		("acetona", "acetone", "C3H6O", ["C", "C", "C", "O", "H", "H", "H", "H", "H", "H"]),
		("tetracloreto de carbono", "carbon tetrachloride", "CCl4", ["C", "Cl", "Cl", "Cl", "Cl"]),
		("diclorometano", "dichloromethane", "CH2Cl2", ["C", "Cl", "Cl", "H", "H"]),
		("cianogênio", "cyanogen", "C2N2", ["C", "C", "N", "N"]),
		("glicina", "glycine", "C2H5NO2", ["C", "C", "N", "O", "O", "H", "H", "H", "H", "H"]),
		("benzeno", "benzene", "C6H6", ["C", "C", "C", "C", "C", "C", "H", "H", "H", "H", "H", "H"]),
		("butano", "butane", "C4H10", ["C", "C", "C", "C", "H", "H", "H", "H", "H", "H", "H", "H", "H", "H"]),
		("trifluoreto de nitrogênio", "nitrogen trifluoride", "NF3", ["N", "F", "F", "F"]),
		("difluoreto de oxigênio", "oxygen difluoride", "OF2", ["O", "F", "F"]),
		("fosgênio", "phosgene", "COCl2", ["C", "O", "Cl", "Cl"]),
		("ácido hipocloroso", "hypochlorous acid", "HClO", ["O", "H", "Cl"]),
		("cloramina", "chloramine", "NH2Cl", ["N", "Cl", "H", "H"]),
		("acetonitrila", "acetonitrile", "C2H3N", ["C", "C", "N", "H", "H", "H"]),
		("ácido ciânico", "cyanic acid", "CHNO", ["C", "N", "O", "H"]),
		("dimetilamina", "dimethylamine", "C2H7N", ["C", "C", "N", "H", "H", "H", "H", "H", "H", "H"]),
		("clorofórmio", "chloroform", "CHCl3", ["C", "Cl", "Cl", "Cl", "H"]),
	];

	/// <summary>
	/// A MOLECULA FECHOU? As ligacoes (atomo A, atomo B, ordem 1-3) sobre os atomos da mesa: cada atomo com exatamente a valencia dele, e
	/// tudo ligado num pedaco so -- qualquer isomero que feche vale (o dono: "pelas ligacoes certas"). Par repetido soma as ordens.
	/// </summary>
	public static bool MoleculaFechou(string[] atomos, IEnumerable<(int A, int B, int Ordem)> ligacoes)
	{
		int n = atomos.Length;
		if (n == 0) return false;
		var ordem = new Dictionary<(int, int), int>();
		foreach ((int a, int b, int o) in ligacoes)
		{
			if (a < 0 || b < 0 || a >= n || b >= n || a == b || o < 1 || o > 3) return false;
			var par = (Math.Min(a, b), Math.Max(a, b));
			ordem[par] = ordem.GetValueOrDefault(par) + o;
			if (ordem[par] > 3) return false;
		}

		var soma = new int[n];
		foreach (((int a, int b), int o) in ordem) { soma[a] += o; soma[b] += o; }
		for (int i = 0; i < n; i++)
			if (!Valencia.TryGetValue(atomos[i], out int v) || soma[i] != v) return false;

		// tudo num pedaco so
		var visto = new bool[n];
		var fila = new Queue<int>();
		fila.Enqueue(0);
		visto[0] = true;
		while (fila.Count > 0)
		{
			int i = fila.Dequeue();
			foreach ((int a, int b) in ordem.Keys)
			{
				int outro = a == i ? b : b == i ? a : -1;
				if (outro >= 0 && !visto[outro]) { visto[outro] = true; fila.Enqueue(outro); }
			}
		}
		return visto.All(v => v);
	}

	/// <summary>As ligacoes no fio: "a-b-ordem;a-b-ordem". Nulo se o texto nao for isso.</summary>
	public static List<(int A, int B, int Ordem)>? LerLigacoes(string texto)
	{
		var l = new List<(int, int, int)>();
		foreach (string parte in texto.Split(';', StringSplitOptions.RemoveEmptyEntries))
		{
			string[] p = parte.Split('-');
			if (p.Length != 3 || !int.TryParse(p[0], out int a) || !int.TryParse(p[1], out int b) || !int.TryParse(p[2], out int o)) return null;
			l.Add((a, b, o));
			if (l.Count > 64) return null;
		}
		return l;
	}

	/// <summary>As ligacoes no fio, do outro lado.</summary>
	public static string EscreverLigacoes(IEnumerable<(int A, int B, int Ordem)> ligacoes) =>
		string.Join(";", ligacoes.Select(l => $"{l.A}-{l.B}-{l.Ordem}"));

	// =====================================================================
	// AS PERGUNTAS -- tres frases por tipo (a variacao do enunciado), escritas em portugues; o cliente as traduz pelo dicionario
	// =====================================================================
	/// <summary>Quantas frases cada tipo tem.</summary>
	public const int FormasPorTipo = 3;

	/// <summary>
	/// O ENUNCIADO, na lingua da tela: o nome do elemento e da molecula sai das tabelas (o ingles quando <paramref name="emIngles"/>), e a
	/// frase em portugues passa pelo dicionario da tela como qualquer texto (o molde com o nome no buraco).
	/// </summary>
	public static string Pergunta(TipoDeMinijogo tipo, int indice, string expressao, byte forma, bool emIngles)
	{
		switch (tipo)
		{
			case TipoDeMinijogo.Conta:
				return (forma % FormasPorTipo) switch { 0 => $"Quanto é {expressao}?", 1 => $"Resolva: {expressao}", _ => $"Calcule {expressao}" };
			case TipoDeMinijogo.Elemento:
			{
				var e = Elemento(indice);
				string nome = emIngles ? e.NomeEn : e.Nome;
				return (forma % FormasPorTipo) switch
				{
					0 => $"Qual é o símbolo do elemento {nome}?",
					1 => $"Escreva o símbolo químico de: {nome}",
					_ => $"Elemento {nome}: qual é o símbolo?",
				};
			}
			case TipoDeMinijogo.Molecula:
			{
				var m = Moleculas[Math.Clamp(indice, 0, Moleculas.Length - 1)];
				string nome = emIngles ? m.NomeEn : m.Nome;
				return (forma % FormasPorTipo) switch
				{
					0 => $"Monte a molécula: {nome} ({m.Formula})",
					1 => $"Ligue os átomos e forme: {nome} ({m.Formula})",
					_ => $"Junte as bolinhas: {nome} ({m.Formula})",
				};
			}
			default: return "";
		}
	}

	/// <summary>A resposta de conta ou de simbolo esta certa? A conta aceita espacos e pontos de milhar; o simbolo, qualquer caixa.</summary>
	public static bool RespostaCerta(Minijogo m, string resposta)
	{
		string r = (resposta ?? "").Trim();
		return m.Tipo switch
		{
			TipoDeMinijogo.Conta => long.TryParse(r.Replace(" ", "").Replace(".", ""), out long n) && n.ToString() == m.Resposta,
			TipoDeMinijogo.Elemento => string.Equals(r, m.Resposta, StringComparison.OrdinalIgnoreCase),
			TipoDeMinijogo.Molecula => LerLigacoes(r) is { } l && MoleculaFechou(Moleculas[Math.Clamp(m.Indice, 0, Moleculas.Length - 1)].Atomos, l),
			_ => false,
		};
	}
}
