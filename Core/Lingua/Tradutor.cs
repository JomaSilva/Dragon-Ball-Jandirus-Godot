using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jandirus.Core.Lingua;

/// <summary>
/// ============================ O TRADUTOR DO JOGO (2026-10-22) ============================
/// O dono: *"adicionar uma opçao de mudar a lingua do jogo pra ingles, mudando toda a interface do jogo pra ingles,
/// mensagens q o jogador fala automaticamente tb seriam traduzidas pro ingles ... faça com q o jogo saiba quando é
/// automatizada pra traduzir pro ingles na hora"*.
///
/// O JOGO CONTINUA ESCRITO EM PORTUGUES, e o ingles e uma LEITURA dele. Sao ~11 mil textos espalhados por ~900
/// arquivos (botao, aviso de chat, fala de NPC, motivo de recusa que o Core devolve, descricao de item no JSON), e
/// trocar cada um por uma chave seria reescrever os arquivos mais quentes do projeto inteiro -- com dez sessoes
/// mexendo neles ao mesmo tempo. Entao a traducao acontece NO FIM DO CAMINHO: o texto portugues que ia aparecer e a
/// propria chave, e este tradutor acha o ingles dele num dicionario (`Assets/Data/lingua-en.json`).
///
/// TRES JEITOS DE ACHAR, na ordem:
///   1. A FRASE INTEIRA: o botao, o titulo, o aviso fixo. Tambem sem caixa (`Tema.Rotulo` grava o rotulo em CAIXA
///      ALTA no proprio texto) e sem a pontuacao das bordas ("Treinar:" acha "Treinar").
///   2. O MOLDE: a frase com pedaco calculado -- `$"Voce ganhou {valor} zeni."` virou a entrada
///      "Voce ganhou {0} zeni." no dicionario. O pedaco volta no lugar que o ingles der a ele, e se ele mesmo for um
///      texto do jogo (o nome de um item, um motivo, a parte do corpo) e traduzido por sua vez. Ver <see cref="Molde"/>.
///   3. A FRASE COMPOSTA: quem escreveu colou varias (linhas, " · ", " -- ", ponto final, "rotulo: valor") -- cada
///      pedaco pelos dois jeitos de cima. E o ultimo recurso: pode sair meio a meio, e meio a meio e melhor que nada.
/// O que nao se acha volta como veio e, se parece portugues, entra nas <see cref="Faltas"/> -- e o que a bancada
/// le pra dizer o que sobrou sem traducao numa tela.
///
/// O QUE NAO PASSA POR AQUI: o que uma PESSOA escreveu (a fala digitada, o nome do personagem). Quem decide isso e
/// quem chama: o chat so traduz a linha que o servidor marcou como automatica (ver `Client/Idioma.cs`).
/// =================================================================================================
/// </summary>
public sealed class Tradutor
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `lingua`, Tools/AssetPipeline): o MOLDE nao e procurado -- so a frase inteira acha
	/// traducao. E o tradutor "de dicionario simples" que a primeira ideia teria dado: todo aviso com nome ou numero
	/// sairia em portugues. Lido pelo <see cref="PeloMolde"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool SemMoldesDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `lingua`): o pedaco de um molde volta CRU -- "You drop {Semente dos deuses}", com o
	/// nome do item em portugues no meio da frase inglesa. Lido pelo <see cref="PeloMolde"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool PedacoCruDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `lingua`): a frase composta nao faz os dois cortes que a ficha de skill pede -- o NUMERO COM
	/// SINAL do fim ("ataque fisico +0,4", "tecnica x1,2") e a <see cref="Seta"/> da casa escolhida --, e essas linhas voltam
	/// em portugues (o jogo de antes de 2026-10-23). Lido pelo corte de <see cref="NumeroNoFim"/> e pelo da seta, os dois em
	/// <see cref="Cortes"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool SemOsCortesDaFichaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `lingua`): a borda come a pontuacao que e da FRASE -- "* o chao comeca a tremer." procura so
	/// "o chao comeca a tremer", sem o ponto que a chave tem, e a narracao de toda cinematica volta em portugues (o jogo de
	/// antes de 2026-10-23). Lido pelo <see cref="PelasBordas"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool BordaComeAPontuacaoDeTeste;

	/// <summary>
	/// QUANTO UM TEXTO PODE SE DESDOBRAR: o pedaco de um molde que e outro molde, cujo pedaco e uma frase composta...
	/// Era 5, pelo requisito de arvore dentro do cartao da aba Learning ("abre com: dominio corporal ≥ 2 e prontidao
	/// corporal ≥ 2   ·   regra de ..."): molde, conta, "e", nome -- quatro andares, e um de folga. (2026-10-23) O mais fundo
	/// passou a ser o RESUMO da ficha de skill dentro da dica do cartao: a linha da dica, a lista do resumo, o "; " dos
	/// niveis, "ate o nivel N:", "nivel N:", "habilidade nova:" e o detalhe entre parenteses -- SETE andares (medido: com 5 tres
	/// resumos e seis dicas ficavam meio em portugues; com 6 so as dicas), e um de folga. O teto existe pra que um texto
	/// patologico nao vire recursao sem fim (cada andar corta o texto, entao ele nunca cresce).
	/// </summary>
	public const int AndaresMaximos = 8;

	/// <summary>
	/// O MOLDE FRACO NAO TRADUZ SOZINHO: um molde com menos letras fixas que isto ("{0} com {1}") casaria com
	/// qualquer frase longa que tivesse a palavra, e traduziria a frase errada pela metade. Ele so vale quando o
	/// resultado sai inteiro em ingles (os pedacos tambem acharam traducao); o forte vale mesmo com um pedaco em
	/// portugues no meio. AJUSTE: mais alto = menos meio-a-meio, mais frase em portugues inteira.
	/// </summary>
	public const int PesoDeMoldeForte = 10;

	/// <summary>O molde com menos letras fixas que isto nao entra no indice: "{0}!" casaria com todo grito.</summary>
	public const int PesoMinimo = 3;

	/// <summary>Quantos textos a memoria guarda antes de se esvaziar (o HUD muda numero todo quadro).</summary>
	public const int TetoDaMemoria = 50_000;

	/// <summary>Quantas faltas diferentes se guardam: e diagnostico, nao arquivo.</summary>
	public const int TetoDasFaltas = 5_000;

	private readonly Dictionary<string, string> _exatas = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _semCaixa = new(StringComparer.Ordinal);
	private readonly List<Molde> _moldes = [];
	private readonly Dictionary<string, List<int>> _porPalavra = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _memoria = new(StringComparer.Ordinal);
	private readonly HashSet<string> _faltas = new(StringComparer.Ordinal);

	/// <summary>
	/// AS PALAVRAS QUE SO O LADO PORTUGUES DO DICIONARIO TEM ("madrugada", "neblina", "alto", "arma"): o vocabulario
	/// que a regua de <see cref="ParecePortugues"/> soma ao acento e as palavras de sempre. Sai do proprio dicionario --
	/// palavra que aparece tambem em algum ingles (nome proprio, "Ki", "Saiyan") fica de fora.
	/// </summary>
	private readonly HashSet<string> _soDoPortugues = new(StringComparer.Ordinal);

	/// <summary>As palavras de todos os ingleses do dicionario: o que esta aqui nunca e "portugues" pra regua.</summary>
	private readonly HashSet<string> _doIngles = new(StringComparer.Ordinal);
	private readonly object _trava = new();

	/// <summary>Quantas frases inteiras o dicionario tem.</summary>
	public int Exatas => _exatas.Count;

	/// <summary>Quantos moldes entraram no indice.</summary>
	public int Moldes => _moldes.Count;

	/// <summary>Os moldes que ficaram FORA do indice (fracos demais ou sem palavra de ancora), pela chave portuguesa.</summary>
	public IReadOnlyList<string> MoldesSemAncora => _semAncora;
	private readonly List<string> _semAncora = [];

	/// <summary>
	/// O QUE SE PEDIU E NAO SE ACHOU, e parece portugues (<see cref="PareceTextoPortugues"/>). Copia: quem le e a
	/// bancada, no fim da rodada.
	/// </summary>
	public IReadOnlyCollection<string> Faltas { get { lock (_trava) return [.. _faltas]; } }

	/// <summary>Esquece as faltas -- a bancada mede uma tela de cada vez.</summary>
	public void EsquecerFaltas() { lock (_trava) _faltas.Clear(); }

	/// <summary>
	/// O DICIONARIO VAZIO: tudo volta como veio. E o que o jogo usa enquanto o arquivo nao carregou (ou se ele sumiu
	/// do pacote) -- melhor o jogo inteiro em portugues que uma excecao no meio da tela de login.
	/// </summary>
	public static Tradutor Vazio { get; } = new(new Dictionary<string, string>());

	/// <summary>
	/// Le o `lingua-en.json`: `{ "textos": { "portugues": "ingles", ... } }`. Chave e valor sao MOLDES: `{0}`, `{1}`
	/// sao os pedacos calculados, `{{`/`}}` sao chaves de verdade, e `{0!}` no ingles quer dizer "o pedaco entra cru"
	/// -- e o que uma PESSOA escreveu, dentro de uma linha do servidor (o "(Relogio) Fulano diz" com a frase dele).
	/// </summary>
	public static Tradutor DoJson(string json)
	{
		using JsonDocument doc = JsonDocument.Parse(json);
		var textos = new Dictionary<string, string>(StringComparer.Ordinal);
		if (doc.RootElement.TryGetProperty("textos", out JsonElement t) && t.ValueKind == JsonValueKind.Object)
			foreach (JsonProperty p in t.EnumerateObject())
				if (p.Value.ValueKind == JsonValueKind.String) textos[p.Name] = p.Value.GetString() ?? "";
		var deFabrica = new List<string>();
		if (doc.RootElement.TryGetProperty("ingles", out JsonElement i) && i.ValueKind == JsonValueKind.Array)
			foreach (JsonElement w in i.EnumerateArray())
				if (w.ValueKind == JsonValueKind.String) deFabrica.Add(w.GetString() ?? "");
		return new Tradutor(textos, deFabrica);
	}

	/// <param name="inglesDeFabrica">
	/// PALAVRAS INGLESAS QUE O JOGO MOSTRA SEM TRADUCAO -- os nomes e as descricoes que ja vem em ingles do DM ("Geti Star
	/// King/Queen", "Upon manual activation"). Nao ha linha deles no dicionario (nao precisam), entao sem esta lista a regua de
	/// <see cref="ParecePortugues"/> os acharia portugueses so por a palavra aparecer num texto portugues.
	/// </param>
	public Tradutor(IReadOnlyDictionary<string, string> textos, IEnumerable<string>? inglesDeFabrica = null)
	{
		var crus = new List<(string Pt, List<Peca> PecasPt, List<Peca> En)>();
		foreach ((string pt0, string en0) in textos)
		{
			if (string.IsNullOrEmpty(pt0) || string.IsNullOrEmpty(en0) || en0 == pt0) continue;
			List<Peca> pecasPt = Pecas(pt0);
			if (pecasPt.TrueForAll(p => p.Buraco < 0))
			{
				string chave = Junto(pecasPt);
				string ingles = Junto(Pecas(en0));
				_exatas[chave] = ingles;
				// ...e sem os espacos das bordas: o pedido chega aparado quando vem colado em outro texto
				if (chave.Trim() is { Length: > 0 } aparada && aparada != chave) _exatas.TryAdd(aparada, ingles.Trim());
				_semCaixa.TryAdd(chave.Trim().ToLowerInvariant(), chave.Trim() == chave ? chave : chave.Trim());
				continue;
			}
			// O MOLDE GUARDA-SE SEM OS ESPACOS (e quebras de linha) DAS BORDAS: o `Dentro` apara o pedido antes de procurar e
			// devolve as bordas dele -- um molde que comecasse com tres espacos nunca casaria com o pedido aparado.
			string pt = pt0.Trim(), en = en0.Trim();
			crus.Add((pt, Pecas(pt), Pecas(en)));
		}

		foreach (string en in textos.Values) _doIngles.UnionWith(Palavras(en));
		if (inglesDeFabrica != null) _doIngles.UnionWith(inglesDeFabrica.Select(w => w.ToLowerInvariant()));
		foreach ((string pt, string en) in textos)
			if (en != pt)
				foreach (string w in Palavras(pt))
					if (!_doIngles.Contains(w)) _soDoPortugues.Add(w);

		// A PALAVRA MAIS RARA DE CADA MOLDE E A ANCORA dele no indice: quem tem essa palavra inteira no texto e
		// candidato, quem nao tem nao pode casar. Rara pra que a lista de candidatos fique curta.
		//
		// O MOLDE CUJAS PALAVRAS TODAS ENCOSTAM NUM PEDACO ("{0} vago{1}", o plural colado) ancora pelo COMECO de palavra: no
		// texto de verdade "vago" e o comeco de "vagos". A chave leva o prefixo `Prefixo`, e a busca procura os comecos de cada
		// palavra do pedido (ver <see cref="PeloMolde"/>).
		var frequencia = new Dictionary<string, int>(StringComparer.Ordinal);
		var palavrasDe = new List<List<string>>();
		foreach ((_, List<Peca> pecasPt, _) in crus)
		{
			List<string> ps = PalavrasInteiras(pecasPt);
			if (ps.Count == 0) ps = [.. ComecosQueEncostam(pecasPt).Select(w => Prefixo + w)];
			palavrasDe.Add(ps);
			foreach (string w in ps.Distinct()) frequencia[w] = frequencia.GetValueOrDefault(w) + 1;
		}

		var aceitos = new List<(Molde M, string Ancora)>();
		for (int i = 0; i < crus.Count; i++)
		{
			(string pt, List<Peca> pecasPt, List<Peca> en) = crus[i];
			int peso = 0;
			foreach (Peca p in pecasPt)
				if (p.Buraco < 0)
					foreach (char c in p.Texto) if (char.IsLetter(c)) peso++;
			string? ancora = palavrasDe[i].OrderBy(w => frequencia[w]).ThenByDescending(w => w.Length).FirstOrDefault();
			if (peso < PesoMinimo || ancora == null) { _semAncora.Add(pt); continue; }
			aceitos.Add((new Molde(pt, pecasPt, en, peso), ancora));
			if (ancora.StartsWith(Prefixo, StringComparison.Ordinal)) _temComecos = true;
		}

		// DO MAIS PESADO PRO MAIS LEVE: o indice devolve posicoes, e a primeira que casa e a mais especifica. No empate de
		// letras, o de mais texto fixo ("proficiencia em {0} {1}%" antes de "proficiencia em {0}"), e depois o de mais pedacos.
		aceitos.Sort((a, b) =>
			b.M.Peso != a.M.Peso ? b.M.Peso.CompareTo(a.M.Peso)
			: b.M.Fixos != a.M.Fixos ? b.M.Fixos.CompareTo(a.M.Fixos)
			: string.CompareOrdinal(a.M.Pt, b.M.Pt));
		foreach ((Molde m, string ancora) in aceitos)
		{
			if (!_porPalavra.TryGetValue(ancora, out List<int>? l)) _porPalavra[ancora] = l = [];
			l.Add(_moldes.Count);
			_moldes.Add(m);
		}
	}

	/// <summary>
	/// O TEXTO EM INGLES, ou o proprio texto quando nao ha traducao. Barato pra quem chama toda hora: o que ja foi
	/// pedido volta da memoria, e texto sem letra (numero, "--", "100%") nem e procurado.
	/// </summary>
	public string Traduzir(string pt)
	{
		if (string.IsNullOrEmpty(pt) || !TemLetra(pt)) return pt;
		lock (_trava)
		{
			if (_memoria.TryGetValue(pt, out string? ja)) return ja;
			string r = Dentro(pt, 0);
			if (_memoria.Count >= TetoDaMemoria) _memoria.Clear();
			_memoria[pt] = r;
			if (r == pt && _faltas.Count < TetoDasFaltas && ParecePortugues(pt)) _faltas.Add(pt);
			return r;
		}
	}

	/// <summary>Os tres jeitos, num andar. Devolve o PROPRIO objeto de entrada quando nao achou nada.</summary>
	private string Dentro(string s, int andar)
	{
		if (_exatas.TryGetValue(s, out string? exata)) return exata;
		if (!TemLetra(s)) return s;

		// os espacos das bordas ficam onde estavam (o rotulo " · sai sozinho" e colado em outro texto)
		int a = 0, b = s.Length;
		while (a < b && char.IsWhiteSpace(s[a])) a++;
		while (b > a && char.IsWhiteSpace(s[b - 1])) b--;
		if (a > 0 || b < s.Length)
		{
			string miolo = s[a..b];
			string t = Dentro(miolo, andar);
			return ReferenceEquals(t, miolo) ? s : s[..a] + t + s[b..];
		}

		if (SemCaixa(s) is { } sc) return sc;
		if (PeloMolde(s, andar) is { } pm) return pm;
		if (PelasBordas(s, andar) is { } pb) return pb;
		if (Composta(s, andar) is { } cp) return cp;
		return s;
	}

	/// <summary>A frase inteira, sem olhar a caixa: "TREINAR" e "treinar" acham "Treinar" e saem "TRAIN" e "train".</summary>
	private string? SemCaixa(string s)
	{
		if (!_semCaixa.TryGetValue(s.ToLowerInvariant(), out string? chave)) return null;
		return NaCaixaDe(s, chave, _exatas[chave]);
	}

	/// <summary>
	/// "TREINAR:" acha "Treinar" e devolve "TRAIN:" -- a pontuacao e o enfeite das bordas ("» ", "- ", "...", o
	/// parentese do "(medio)" colado num numero, as aspas) voltam pro lugar. So a frase inteira e o molde; a frase
	/// composta e outro passo.
	///
	/// (2026-10-23) A PONTUACAO DO FIM PODE SER DA FRASE, e nao enfeite: a chave do dicionario e "o chao comeca a tremer."
	/// COM o ponto, e a narracao da cinematica chega ao chat como "* o chao comeca a tremer." -- sem o "* " E sem o ponto, o
	/// miolo nao achava nada, e as narracoes de todas as cenas ficavam em portugues. O mesmo com o marcador da ficha de skill
	/// ("escolha unica entre:", "trilha Arlian escolhida (1)" atras do marcador). Quando o miolo sozinho nao acha, ele e
	/// procurado de novo COM a pontuacao do fim -- so quando ha enfeite no comeco: sem ele, o miolo com a pontuacao e o
	/// proprio pedido, que ja foi procurado.
	/// </summary>
	private string? PelasBordas(string s, int andar)
	{
		Match m = Bordas.Match(s);
		if (!m.Success) return null;
		string antes = m.Groups[1].Value, miolo = m.Groups[2].Value, depois = m.Groups[3].Value;
		if (miolo.Length == 0 || (antes.Length == 0 && depois.Length == 0)) return null;
		string? t = _exatas.TryGetValue(miolo, out string? e) ? e : SemCaixa(miolo) ?? PeloMolde(miolo, andar);
		if (t != null) return antes + t + depois;
		string fim = depois.TrimEnd();
		if (antes.Length == 0 || fim.Length == 0 || BordaComeAPontuacaoDeTeste) return null;
		string comFim = miolo + fim;
		t = _exatas.TryGetValue(comFim, out e) ? e : SemCaixa(comFim) ?? PeloMolde(comFim, andar);
		return t == null ? null : antes + t + depois[fim.Length..];
	}

	private static readonly Regex Bordas = new(@"^([\s»•·\-\*>–—\(\[""“'«]*)(.*?)([\s:;,\.…!?\)\]""”'»]*)$", RegexOptions.Singleline);

	/// <summary>
	/// O MOLDE QUE CASA COM O TEXTO INTEIRO, do mais pesado pro mais leve. Os candidatos saem do indice: so quem tem a
	/// palavra de ancora no texto. O molde fraco que deixaria portugues no meio e pulado (ver <see cref="PesoDeMoldeForte"/>):
	/// a frase inteira em portugues entra nas faltas e se conserta; meio a meio, ninguem acharia.
	/// </summary>
	private string? PeloMolde(string s, int andar)
	{
		if (SemMoldesDeTeste || _moldes.Count == 0) return null;
		var candidatos = new List<int>();
		foreach (string w in Palavras(s))
		{
			if (_porPalavra.TryGetValue(w, out List<int>? l)) candidatos.AddRange(l);
			if (!_temComecos) continue;
			for (int k = 3; k <= w.Length; k++)
				if (_porPalavra.TryGetValue(Prefixo + w[..k], out List<int>? lc)) candidatos.AddRange(lc);
		}
		if (candidatos.Count == 0) return null;
		candidatos.Sort();

		// O EMPATE SE DECIDE PELA CAIXA: dois moldes de mesmo peso que casam com o pedido ("{0} nao existe mais" e "{0} NAO
		// EXISTE MAIS") -- ganha o escrito na caixa em que o texto veio. O primeiro aceito fica de reserva ate o peso cair.
		string? reserva = null;
		int pesoDaReserva = -1;
		int anterior = -1;
		foreach (int i in candidatos)
		{
			if (i == anterior) continue;
			anterior = i;
			Molde m = _moldes[i];
			if (reserva != null && m.Peso < pesoDaReserva) break;
			Match mt;
			try { mt = m.Regex.Match(s); }
			catch (RegexMatchTimeoutException) { continue; }
			if (!mt.Success) continue;

			var sb = new StringBuilder();
			// O QUE O MOLDE FRACO PRECISA PROVAR: que o resultado saiu inteiro em ingles -- sem contar o pedaco CRU, que e
			// de uma pessoa e pode estar na lingua que ela quiser. E o pedaco de varias palavras que voltou sem traducao e
			// sinal de que o molde casou com uma frase que nao e a dele ("o guerreiro luta" + " com " + "a espada do avo").
			var semOCru = new StringBuilder();
			bool sobrouFrase = false;
			foreach (Peca p in m.En)
			{
				if (p.Buraco < 0) { sb.Append(p.Texto); semOCru.Append(p.Texto); continue; }
				Group g = mt.Groups["b" + p.Buraco];
				string v = g.Success ? g.Value : "";
				string t = p.Cru || PedacoCruDeTeste || andar >= AndaresMaximos || !TemLetra(v) ? v : Dentro(v, andar + 1);
				sb.Append(t);
				if (p.Cru) continue;
				semOCru.Append(t);
				if (ReferenceEquals(t, v) && v.Contains(' ') && TemLetra(v)) sobrouFrase = true;
			}
			string r = NaCaixaDoMolde(s, m, sb.ToString());
			if (m.Peso < PesoDeMoldeForte && (sobrouFrase || ParecePortugues(semOCru.ToString()))) continue;
			if (NaMesmaCaixa(s, m)) return r;
			if (reserva == null) { reserva = r; pesoDaReserva = m.Peso; }
		}
		return reserva;
	}

	/// <summary>Todo o texto fixo do molde esta no pedido com as MESMAS letras, maiusculas e minusculas?</summary>
	private static bool NaMesmaCaixa(string s, Molde m)
	{
		foreach (Peca p in m.PecasPt)
			if (p.Buraco < 0 && !s.Contains(p.Texto, StringComparison.Ordinal)) return false;
		return true;
	}

	private static readonly string[] Juntas = ["\n", " · ", " | ", " — ", " -- ", "  ", "; ", Seta];

	/// <summary>
	/// (2026-10-23) A SETA, a ultima junta: a ficha de skill cola a casa escolhida e a tecnica que ela da ("Van-sama", seta,
	/// "Provocacao"). Cortada como as outras, cada lado se traduz sozinho.
	/// </summary>
	private const string Seta = " → ";

	/// <summary>
	/// AS JUNTAS DA CONTA ("dominio corporal ≥ 2"): o requisito de arvore e montado colando o nome do campo, o comparador
	/// e o numero -- o nome tem traducao, a conta inteira nao.
	/// </summary>
	private static readonly string[] JuntasDeConta = [" ≥ ", " ≤ ", " > ", " < ", " = "];

	/// <summary>
	/// AS JUNTAS QUE SE TRADUZEM: o "e" e o "ou" que colam dois requisitos. So valem quando TODOS os pedacos acharam
	/// ingles -- senao a frase portuguesa qualquer que tivesse um " e " sairia com um "and" no meio.
	/// </summary>
	private static readonly (string Pt, string En)[] JuntasDeLigar = [(" e ", " and "), (" ou ", " or ")];

	private static readonly Regex FimDeFrase = new(@"(?<=[.!?…])(\s+)(?=\S)");

	/// <summary>"Rotulo (detalhe)": o detalhe entre parenteses no FIM, colado num nome ("crescente gibosa (no ceu)").</summary>
	private static readonly Regex ComParentese = new(@"^(?<a>.*\S)(?<b>\s+\([^()]*\))$", RegexOptions.Singleline);

	/// <summary>
	/// "Rotulo +N": o NUMERO COM SINAL no fim, colado num nome ("ataque fisico +0,4", "percepcao de Ki -1", "tecnica" e o
	/// fator "vezes 1,2"). E como a ficha de skill (`FichaDeSkill`) escreve o que cada compra e cada nivel dao: o nome do campo
	/// tem traducao (`NomesLegiveis`), o par nome + numero nao -- e um molde "{0} +{1}" seria leve demais pra entrar no indice
	/// (<see cref="PesoMinimo"/>). So com SINAL (mais, menos, o menos tipografico e o vezes): o numero solto no fim ("Nivel 5")
	/// fica com quem ja o traduzia. O numero volta como veio, com a virgula decimal do portugues, como todo numero do jogo em
	/// ingles ("1.500 zeni").
	/// </summary>
	private static readonly Regex NumeroNoFim = new(@"^(?<a>.*\S)(?<b>\s+[+\-−×]\d[\d.,]*)$", RegexOptions.Singleline);

	/// <summary>
	/// A FRASE COMPOSTA: corta nas juntas que o jogo usa pra colar texto, traduz cada pedaco e cola de volta com a
	/// MESMA junta. Os cortes, na ordem: as juntas (linha, " · ", " -- ", "; "...); as FRASES, em grupos (ver
	/// <see cref="PorFrases"/>); o "rotulo: valor"; o detalhe entre parenteses no fim; o numero com sinal no fim ("X +0,4");
	/// a conta ("X ≥ 2"); o "e"/"ou".
	///
	/// FICA O PRIMEIRO QUE SAI INTEIRO EM INGLES, e nao o primeiro que mudou alguma coisa: o "; " no meio de duas frases
	/// coladas cortaria o grupo que o dicionario conhece e devolveria meio a meio. So se nenhum sai inteiro, o primeiro
	/// meio a meio.
	/// </summary>
	private string? Composta(string s, int andar)
	{
		if (andar >= AndaresMaximos) return null;
		string? meio = null;
		foreach (string? r in Cortes(s, andar))
		{
			if (r == null) continue;
			if (!ParecePortugues(r)) return r;
			meio ??= r;
		}
		return meio;
	}

	/// <summary>Cada jeito de cortar <paramref name="s"/>, traduzido (nulo = esse corte nao mudou nada), na ordem da <see cref="Composta"/>.</summary>
	private IEnumerable<string?> Cortes(string s, int andar)
	{
		foreach (string j in Juntas)
		{
			if (!s.Contains(j) || (j == Seta && SemOsCortesDaFichaDeTeste)) continue;
			string[] partes = s.Split(j);
			yield return TraduzirPartes(partes, andar) ? string.Join(j, partes) : null;
		}

		yield return PorFrases(s, andar);

		int dp = s.IndexOf(": ", StringComparison.Ordinal);
		if (dp > 0 && dp < s.Length - 2)
		{
			string[] lados = [s[..dp], s[(dp + 2)..]];
			yield return TraduzirPartes(lados, andar) ? lados[0] + ": " + lados[1] : null;
		}

		Match mp = ComParentese.Match(s);
		if (mp.Success)
		{
			string[] lados = [mp.Groups["a"].Value, mp.Groups["b"].Value];
			yield return TraduzirPartes(lados, andar) ? lados[0] + lados[1] : null;
		}

		// o nome com o numero com sinal no fim: so o nome se traduz, o numero fica onde estava
		Match mn = NumeroNoFim.Match(s);
		if (mn.Success && !SemOsCortesDaFichaDeTeste)
		{
			string[] nome = [mn.Groups["a"].Value];
			yield return TraduzirPartes(nome, andar) ? nome[0] + mn.Groups["b"].Value : null;
		}

		foreach (string j in JuntasDeConta)
		{
			if (!s.Contains(j)) continue;
			string[] partes = s.Split(j);
			yield return TraduzirPartes(partes, andar) ? string.Join(j, partes) : null;
		}

		foreach ((string pt, string en) in JuntasDeLigar)
		{
			if (!s.Contains(pt)) continue;
			string[] partes = s.Split(pt);
			yield return TraduzirPartes(partes, andar, todas: true) ? string.Join(en, partes) : null;
		}

		// A LISTA, por ultimo ("chuva, neve, neblina"): a virgula e a junta mais fraca -- frase de verdade tambem tem
		if (s.Contains(", "))
		{
			string[] partes = s.Split(", ");
			yield return TraduzirPartes(partes, andar) ? string.Join(", ", partes) : null;
		}
	}

	/// <summary>
	/// AS FRASES EM GRUPOS. Quem monta um texto longo costuma colar duas ou tres frases do dicionario uma atras da outra
	/// (`.Text += "..."`): a frase de verdade e o GRUPO, e nao cada uma. De cada ponto, o grupo mais longo que o dicionario
	/// conhece inteiro (frase ou molde); a frase que nao entra em grupo nenhum vai sozinha.
	/// </summary>
	private string? PorFrases(string s, int andar)
	{
		string[] p = FimDeFrase.Split(s);   // o grupo capturado devolve os espacos como pedacos proprios: frase, espaco, frase...
		if (p.Length < 3) return null;
		var sb = new StringBuilder();
		bool mudou = false;
		int i = 0;
		while (i < p.Length)
		{
			string? achado = null;
			int fim = i;
			for (int j = p.Length - 1; j > i && achado == null; j -= 2)
			{
				string grupo = string.Concat(p[i..(j + 1)]);
				achado = _exatas.TryGetValue(grupo, out string? e) ? e : SemCaixa(grupo) ?? PeloMolde(grupo, andar + 1);
				if (achado != null) fim = j;
			}
			if (achado == null)
			{
				achado = TemLetra(p[i]) ? Dentro(p[i], andar + 1) : p[i];
				fim = i;
			}
			if (!ReferenceEquals(achado, p[i]) && achado != string.Concat(p[i..(fim + 1)])) mudou = true;
			sb.Append(achado);
			if (fim + 1 < p.Length) sb.Append(p[fim + 1]);   // o espaco depois do grupo
			i = fim + 2;
		}
		return mudou ? sb.ToString() : null;
	}

	/// <param name="todas">So vale se TODO pedaco com letra achou traducao (a junta que se traduz, o "e" -> "and").</param>
	private bool TraduzirPartes(string[] partes, int andar, bool todas = false)
	{
		bool mudou = false, faltou = false;
		for (int i = 0; i < partes.Length; i++)
		{
			if (partes[i].Length == 0 || !TemLetra(partes[i])) continue;
			string t = Dentro(partes[i], andar + 1);
			if (!ReferenceEquals(t, partes[i]) && t != partes[i]) { partes[i] = t; mudou = true; }
			else faltou = true;
		}
		return mudou && (!todas || !faltou);
	}

	// =====================================================================
	// A CAIXA DAS LETRAS
	// =====================================================================

	/// <summary>
	/// O ingles na caixa em que o texto PEDIDO veio: todo em maiusculas sai todo em maiusculas; a primeira letra
	/// trocada de caixa (o aviso de chat que comeca minusculo, o rotulo que comeca maiusculo) troca no ingles tambem.
	/// </summary>
	private static string NaCaixaDe(string pedido, string chave, string ingles)
	{
		if (pedido == chave || ingles.Length == 0) return ingles;
		if (SoMaiusculas(pedido)) return ingles.ToUpperInvariant();
		// a chave em CAIXA ALTA pedida em caixa normal ("COM VOCE" pedido como "com voce"): o ingles todo minusculo, e a
		// primeira letra na caixa do pedido -- trocar so a primeira daria "wITH YOU"
		if (SoMaiusculas(chave) && SoMaiusculas(ingles)) ingles = ingles.ToLowerInvariant();
		char p = PrimeiraLetra(pedido), c = PrimeiraLetra(chave);
		if (SoMaiusculas(chave)) return ComAPrimeira(ingles, char.IsUpper(p));
		if (p == '\0' || c == '\0' || char.IsUpper(p) == char.IsUpper(c)) return ingles;
		return ComAPrimeira(ingles, char.IsUpper(p));
	}

	private static string NaCaixaDoMolde(string pedido, Molde m, string ingles)
	{
		if (SoMaiusculas(pedido) && !SoMaiusculas(ingles)) return ingles.ToUpperInvariant();
		// a primeira letra so se ajusta quando o molde COMECA com texto fixo (o pedaco do comeco e de quem o deu)
		if (m.PecasPt.Count == 0 || m.PecasPt[0].Buraco >= 0) return ingles;
		char p = PrimeiraLetra(pedido), c = PrimeiraLetra(m.PecasPt[0].Texto);
		if (p == '\0' || c == '\0' || char.IsUpper(p) == char.IsUpper(c)) return ingles;
		return ComAPrimeira(ingles, char.IsUpper(p));
	}

	private static bool SoMaiusculas(string s)
	{
		int letras = 0;
		foreach (char c in s)
		{
			if (!char.IsLetter(c)) continue;
			if (char.IsLower(c)) return false;
			letras++;
		}
		return letras >= 2;
	}

	private static char PrimeiraLetra(string s)
	{
		foreach (char c in s) if (char.IsLetter(c)) return c;
		return '\0';
	}

	private static string ComAPrimeira(string s, bool maiuscula)
	{
		for (int i = 0; i < s.Length; i++)
		{
			if (!char.IsLetter(s[i])) continue;
			char c = maiuscula ? char.ToUpperInvariant(s[i]) : char.ToLowerInvariant(s[i]);
			return s[..i] + c + s[(i + 1)..];
		}
		return s;
	}

	// =====================================================================
	// O MOLDE
	// =====================================================================

	/// <summary>Um pedaco de molde: texto fixo (<c>Buraco</c> &lt; 0), ou o pedaco calculado N (<c>Cru</c> = entra como veio).</summary>
	private readonly record struct Peca(string Texto, int Buraco, bool Cru);

	/// <summary>
	/// UMA FRASE COM PEDACOS CALCULADOS. O portugues vira uma expressao regular ancorada nas duas pontas, com cada
	/// pedaco no lugar de um grupo preguicoso -- e o pedaco que se repete ("{0}?! Fala comigo, {0}!") tem de ser o
	/// MESMO texto nas duas vezes. A expressao so e montada na primeira vez que o molde vira candidato: a maioria
	/// nunca vira, numa sessao.
	/// </summary>
	private sealed class Molde(string pt, List<Peca> pecasPt, List<Peca> en, int peso)
	{
		public readonly string Pt = pt;
		public readonly List<Peca> PecasPt = pecasPt;
		public readonly List<Peca> En = en;
		public readonly int Peso = peso;

		/// <summary>Quanto texto fixo o molde tem (letra, pontuacao e espaco): o desempate de dois moldes de mesmo peso.</summary>
		public readonly int Fixos = pecasPt.Sum(p => p.Buraco < 0 ? p.Texto.Length : 0);

		private Regex? _regex;

		public Regex Regex => _regex ??= Montar();

		private Regex Montar()
		{
			var sb = new StringBuilder("^");
			var vistos = new HashSet<int>();
			foreach (Peca p in PecasPt)
			{
				if (p.Buraco < 0) sb.Append(Regex.Escape(p.Texto));
				else if (vistos.Add(p.Buraco)) sb.Append("(?<b").Append(p.Buraco).Append(">.*?)");
				else sb.Append(@"\k<b").Append(p.Buraco).Append('>');
			}
			sb.Append('$');
			return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline,
							 TimeSpan.FromMilliseconds(50));
		}
	}

	/// <summary>Corta um molde do dicionario em texto fixo e pedacos: `{n}`, `{n!}`, e `{{`/`}}` como chave de verdade.</summary>
	private static List<Peca> Pecas(string molde)
	{
		var r = new List<Peca>();
		var sb = new StringBuilder();
		for (int i = 0; i < molde.Length; i++)
		{
			char c = molde[i];
			if (c == '{' && i + 1 < molde.Length && molde[i + 1] == '{') { sb.Append('{'); i++; continue; }
			if (c == '}' && i + 1 < molde.Length && molde[i + 1] == '}') { sb.Append('}'); i++; continue; }
			if (c == '{')
			{
				int j = i + 1;
				while (j < molde.Length && char.IsAsciiDigit(molde[j])) j++;
				bool cru = j < molde.Length && molde[j] == '!';
				int fim = cru ? j + 1 : j;
				if (j > i + 1 && fim < molde.Length && molde[fim] == '}')
				{
					if (sb.Length > 0) { r.Add(new Peca(sb.ToString(), -1, false)); sb.Clear(); }
					r.Add(new Peca("", int.Parse(molde.AsSpan(i + 1, j - i - 1)), cru));
					i = fim;
					continue;
				}
			}
			sb.Append(c);
		}
		if (sb.Length > 0) r.Add(new Peca(sb.ToString(), -1, false));
		return r;
	}

	private static string Junto(List<Peca> pecas)
	{
		var sb = new StringBuilder();
		foreach (Peca p in pecas) sb.Append(p.Buraco < 0 ? p.Texto : "{" + p.Buraco + "}");
		return sb.ToString();
	}

	/// <summary>
	/// AS PALAVRAS QUE SERVEM DE ANCORA: as que estao INTEIRAS dentro do texto fixo. A que encosta num pedaco
	/// ("possivel{1}" vira "possiveis" no jogo) nao serve -- o texto de verdade nao a tem inteira.
	/// </summary>
	private static List<string> PalavrasInteiras(List<Peca> pecas)
	{
		var r = new List<string>();
		for (int k = 0; k < pecas.Count; k++)
		{
			Peca p = pecas[k];
			if (p.Buraco >= 0) continue;
			bool colaAntes = k > 0, colaDepois = k < pecas.Count - 1;
			string t = p.Texto;
			int i = 0;
			while (i < t.Length)
			{
				if (!char.IsLetter(t[i])) { i++; continue; }
				int ini = i;
				while (i < t.Length && char.IsLetter(t[i])) i++;
				if ((ini == 0 && colaAntes) || (i == t.Length && colaDepois)) continue;
				if (i - ini >= 3) r.Add(t[ini..i].ToLowerInvariant());
			}
		}
		return r;
	}

	/// <summary>A marca da chave de ancora que e COMECO de palavra (ver o indice no construtor).</summary>
	private const string Prefixo = "\u0001";

	/// <summary>Ha molde ancorado por comeco de palavra? Sem nenhum, a busca nem olha os comecos.</summary>
	private bool _temComecos;

	/// <summary>
	/// AS PALAVRAS QUE ENCOSTAM NUM PEDACO SO PELO FIM ("vago{1}", "habilidade{3}"): no texto de verdade elas sao o
	/// COMECO de uma palavra maior ("vagos"). Servem de ancora quando o molde nao tem palavra inteira nenhuma.
	/// </summary>
	private static List<string> ComecosQueEncostam(List<Peca> pecas)
	{
		var r = new List<string>();
		for (int k = 0; k + 1 < pecas.Count; k++)
		{
			Peca p = pecas[k];
			if (p.Buraco >= 0 || pecas[k + 1].Buraco < 0) continue;
			string t = p.Texto;
			int fim = t.Length, ini = fim;
			while (ini > 0 && char.IsLetter(t[ini - 1])) ini--;
			if (fim - ini < 3 || (ini == 0 && k > 0)) continue;   // a que encosta dos DOIS lados nao tem comeco certo
			r.Add(t[ini..fim].ToLowerInvariant());
		}
		return r;
	}

	/// <summary>As palavras de um texto, em minusculas, cada uma uma vez.</summary>
	private static HashSet<string> Palavras(string s)
	{
		var r = new HashSet<string>(StringComparer.Ordinal);
		int i = 0;
		while (i < s.Length)
		{
			if (!char.IsLetter(s[i])) { i++; continue; }
			int ini = i;
			while (i < s.Length && char.IsLetter(s[i])) i++;
			if (i - ini >= 3) r.Add(s[ini..i].ToLowerInvariant());
		}
		return r;
	}

	private static bool TemLetra(string s)
	{
		foreach (char c in s) if (char.IsLetter(c)) return true;
		return false;
	}

	// =====================================================================
	// "ISTO E PORTUGUES?"
	// =====================================================================

	private static readonly HashSet<string> PalavrasSoDoPortugues = new(StringComparer.Ordinal)
	{
		"você", "voce", "vocês", "voces", "não", "nao", "que", "para", "pra", "com", "uma", "umas", "uns", "isso", "isto",
		"esse", "essa", "este", "esta", "está", "estão", "estao", "aqui", "ainda", "quando", "pelo", "pela", "pelos",
		"pelas", "ninguém", "ninguem", "nenhum", "nenhuma", "agora", "depois", "também", "tambem", "muito", "muita",
		"dele", "dela", "deles", "delas", "então", "entao", "porque", "onde", "quem", "tudo", "nada", "sem", "já",
		"seu", "sua", "seus", "suas", "dos", "das", "nos", "nas", "de", "da", "em", "ao", "aos", "é", "são", "foi",
		"vai", "tem", "ser", "pode", "fica", "até", "cada", "outro", "outra", "todos", "todas", "mais", "menos",
		"mesmo", "mesma", "enquanto", "nunca", "sempre", "vez", "vezes", "entre", "num", "numa", "pro", "pros",
	};

	private static readonly Regex AcentoDoPortugues = new("[ãõçáéíóúâêôàÃÕÇÁÉÍÓÚÂÊÔÀ]");

	/// <summary>
	/// O TEXTO PARECE PORTUGUES? Acento do portugues, ou uma palavra que so o portugues usa ("voce", "nao", "que",
	/// "com"...). E a regua das faltas e da bancada: um texto que passou por aqui e ainda responde sim nao foi
	/// traduzido. Erra pro lado do "sim" com nome proprio acentuado -- aceitavel, porque e so diagnostico.
	/// </summary>
	public static bool PareceTextoPortugues(string s)
	{
		if (string.IsNullOrEmpty(s)) return false;
		if (AcentoDoPortugues.IsMatch(s)) return true;
		int i = 0;
		while (i < s.Length)
		{
			if (!char.IsLetter(s[i])) { i++; continue; }
			int ini = i;
			while (i < s.Length && char.IsLetter(s[i])) i++;
			if (PalavrasSoDoPortugues.Contains(s[ini..i].ToLowerInvariant())) return true;
		}
		return false;
	}

	/// <summary>
	/// ...E COM O VOCABULARIO DESTE DICIONARIO: a regua de cima mais toda palavra que so aparece do lado portugues
	/// ("madrugada (05,6h)" nao tem acento nem palavra de ligacao, e e portugues). E a regua das faltas, do molde fraco e
	/// do robo da lingua.
	/// </summary>
	public bool ParecePortugues(string s)
	{
		if (PareceTextoPortugues(s)) return true;
		// (e o plural de uma delas, "vagos" de "vago" -- menos o plural que e ingles: "passes" nao e "passe" + s)
		foreach (string w in Palavras(s))
		{
			if (_soDoPortugues.Contains(w)) return true;
			if (w.Length > 4 && w[^1] == 's' && !_doIngles.Contains(w) && _soDoPortugues.Contains(w[..^1])) return true;
		}
		return false;
	}
}
