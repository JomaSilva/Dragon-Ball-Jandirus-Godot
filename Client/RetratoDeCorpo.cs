using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;

namespace Jandirus.Client;

/// <summary>
/// O RETRATO DE UM CORPO -- **todo campo visual dele, colhido por varredura e nao por lista.**
///
/// ============================ POR QUE ELE NAO E UMA LISTA DE CAMPOS ============================
/// A bancada de replicacao anterior comparava o que alguem tinha lembrado de escrever: cabelo, olho,
/// rabo, coladas, contorno. Ela achou o defeito do rabo -- e achou porque o rabo estava na lista. O
/// campo que nasce DEPOIS nasce fora da lista, e a bancada continua verde enquanto o jogo mostra
/// duas coisas diferentes pra duas pessoas. Este projeto ja pagou essa conta quatro vezes com a
/// LISTA DE DESLOCAMENTO POR ALTITUDE (aura, barra, balao, carga, e depois nebulosa e raios): cada
/// vez, uma lista escrita a mao com um item esquecido.
///
/// Entao aqui nao ha lista. Ha uma VARREDURA, e ela colhe tres coisas:
///
///   1. **os nodes** -- o caminho de cada filho do corpo, recursivo (um node que so nasce num dos
///      lados aparece como chave faltando, e nao como silencio);
///   2. **as propriedades** de cada node, pedidas ao proprio motor (`GetPropertyList`) -- posicao,
///      visibilidade, escala, modulate, folha do sprite, animacao, tudo;
///   3. **os uniformes de shader** de cada material, pedidos ao proprio shader
///      (`Shader.GetShaderUniformList`) -- que e onde MORA quase todo o estado visual deste jogo:
///      tinta do cabelo, tinta do olho, tinta do rabo, contorno, cor do contorno, ferida, achatar.
///
/// Um campo visual novo cai em uma das tres por construcao. Nao ha como acrescenta-lo sem ele
/// entrar aqui -- e e isso, e nao o numero de checagens, que era o pedido.
/// ==========================================================================================
///
/// ============================ POR QUE MIN E MAX, E NAO O VALOR ============================
/// Metade do estado visual deste jogo PULSA: o contorno respira entre `PisoDoPulsoDoContorno` e a
/// forca cheia num ciclo de 2,6 s, a cor alterna num ciclo de 4,0 s, a chama da carga oscila, a
/// aura anima 4 quadros. Comparar um instante do corpo A com um instante do corpo B compararia
/// FASES, e as fases sao diferentes por construcao -- os dois processos subiram em segundos
/// diferentes.
///
/// Colhendo por uma JANELA maior que o maior ciclo, o que se compara e a FAIXA que cada valor
/// percorre. Um valor parado tem min = max; um valor que pulsa tem a mesma faixa dos dois lados,
/// porque a faixa sai das constantes do Core e nao do relogio. E a unica forma de comparacao que
/// e ao mesmo tempo cega (nao sabe o que esta medindo) e estavel.
/// ======================================================================================
/// </summary>
public sealed class RetratoDeCorpo
{
	/// <summary>Quantas vezes <see cref="Amostrar"/> rodou. Zero = a bancada nao mediu nada.</summary>
	public int Amostras;

	/// <summary>Em que forma o corpo estava quando o retrato foi tirado -- o carimbo da comparacao.</summary>
	public string Forma = "";

	/// <summary>Um rotulo livre do estado (ex.: "ki normal", "ki acima de 100%").</summary>
	public string Estado = "";

	private readonly Dictionary<string, double[]> _min = [];
	private readonly Dictionary<string, double[]> _max = [];
	private readonly Dictionary<string, SortedSet<string>> _txt = [];

	/// <summary>Todas as chaves colhidas -- numericas e de texto.</summary>
	public IEnumerable<string> Chaves => _min.Keys.Concat(_txt.Keys);

	public int Campos => _min.Count + _txt.Count;

	// =====================================================================
	// O QUE NAO SE COMPARA -- e cada linha tem um porque
	// =====================================================================
	/// <summary>
	/// PROPRIEDADES QUE NAO DIZEM NADA SOBRE O QUE APARECE NA TELA, ou que dizem uma coisa que os dois
	/// corpos NAO PODEM ter igual. A lista e curta de proposito: cada item aqui e um pedaco do corpo
	/// que a bancada deixa de olhar, e a razao tem que caber numa linha.
	/// </summary>
	private static readonly HashSet<string> Ignoradas =
	[
		// A POSICAO NO MUNDO E DIFERENTE POR CONSTRUCAO -- os dois corpos estao em lugares diferentes
		// da zona (e tem que estar; empilhados nao se leem). O que a bancada compara e a posicao
		// LOCAL de cada filho dentro do corpo, que e onde mora todo o deslocamento de altitude.
		"global_position", "global_transform", "global_rotation", "global_rotation_degrees",
		"global_scale", "global_skew", "transform",

		// O QUADRO DA ANIMACAO E FASE, nao estado: os dois processos subiram em segundos diferentes.
		// O que replica e a FOLHA e o NOME da animacao, e os dois continuam sendo comparados.
		"frame", "frame_progress",

		// IDENTIDADE E CICLO DE VIDA DO NODE, nao aparencia.
		"owner", "script", "name", "unique_name_in_owner", "scene_file_path", "editor_description",
		"process_mode", "process_priority", "process_physics_priority", "process_thread_group",
		"physics_interpolation_mode", "auto_translate_mode",
	];

	/// <summary>
	/// UNIFORMES QUE ANDAM COM UM RELOGIO PROPRIO E NAO VOLTAM -- `tempo` nos shaders de clima e
	/// embate cresce sem teto desde que o node nasceu, entao os dois lados divergem pelo tanto que
	/// um processo subiu antes do outro. Nao ha faixa a comparar: e um cronometro.
	///
	/// (O que PULSA -- contorno, cor alternada, chama -- **nao** entra aqui: pulso tem faixa, e a
	/// faixa e justamente o que este retrato compara.)
	/// </summary>
	private static readonly HashSet<string> UniformesDeRelogio = ["tempo", "idade", "t"];

	/// <summary>
	/// ============================ O QUE DIVERGE **DE PROPOSITO** ENTRE DUAS TELAS ============================
	/// Uma entrada so, e ela foi ACHADA por esta bancada na primeira rodada: a semente da nebulosa do
	/// Ultra Instinto sai de `GetInstanceId() % 997` (`NebulosaDaForma.cs:350`), e id de instancia e um
	/// contador do PROCESSO -- o mesmo personagem tem semente 82,49 na tela dele e 34,58 na do vizinho.
	///
	/// O comentario de la explica a intencao e ela e legitima: a semente existe pra dois lutadores em
	/// Ultra Instinto lado a lado nao piscarem em sincronia, e pra isso basta variar DENTRO de um
	/// cliente. Que o padrao do redemoinho de uma pessoa nao seja o mesmo nas duas telas e divergencia
	/// cosmetica assumida, nao defeito -- e fica anotada aqui em vez de ficar vermelha pra sempre,
	/// porque bancada que vive vermelha ensina a ignorar vermelho.
	///
	/// A LISTA E POR CHAVE EXATA de proposito: um campo NOVO com o mesmo vicio continua reprovando.
	/// ======================================================================================================
	/// </summary>
	private static readonly HashSet<string> DivergemDeProposito = ["Nebulosa/Quad~semente"];

	// =====================================================================
	// COLHER
	// =====================================================================
	/// <summary>
	/// UMA AMOSTRA. Chamar por quadro durante a janela de medicao; a faixa vai se abrindo sozinha.
	///
	/// A RAIZ FICA DE FORA de proposito: o corpo local e um <see cref="LocalPlayer"/> e o alheio um
	/// <see cref="RemotePlayer"/> -- classes diferentes, com campos diferentes (destino do clique,
	/// mapa de colisao, buffer de interpolacao). Comparar as duas raizes compararia a MAQUINARIA de
	/// mover o corpo, que nao e replicada e nao devia ser. O que se ve na tela sao os FILHOS.
	/// </summary>
	public void Amostrar(Node2D corpo)
	{
		Amostras++;
		Filhos(corpo, "");
	}

	/// <summary>
	/// ============================ O NOME AUTOMATICO DO MOTOR NAO SERVE DE CHAVE ============================
	/// As camadas do <see cref="CharacterVisual"/> e o quad da nebulosa nascem sem nome, e o Godot os
	/// batiza de `@AnimatedSprite2D@286` -- onde o numero e um CONTADOR DO PROCESSO. Nos dois clientes
	/// ele e diferente por construcao, entao a chave de cada camada saia diferente e a comparacao
	/// virava 478 "falta"/"sobra" que nao diziam nada.
	///
	/// Trocado pelo TIPO mais a POSICAO ENTRE OS IRMAOS, que e determinista e -- melhor -- e a propria
	/// ordem de desenho: com `YSortEnabled` e Y empatado, o indice do filho E o z-order (a regra esta
	/// escrita em `World.cs`, no bloco que poe a aura antes do corpo). Assim, duas camadas trocadas de
	/// lugar num dos clientes aparecem como diferenca em vez de sumirem na renomeacao.
	/// ====================================================================================================
	/// </summary>
	private void Filhos(Node no, string caminho)
	{
		Dictionary<string, int>? contagem = null;
		foreach (Node f in no.GetChildren())
		{
			string nome = f.Name.ToString();
			if (nome.StartsWith('@'))
			{
				contagem ??= [];
				string tipo = f.GetType().Name;
				int i = contagem.TryGetValue(tipo, out int c) ? c : 0;
				contagem[tipo] = i + 1;
				nome = $"{tipo}#{i}";
			}
			Andar(f, caminho.Length == 0 ? nome : caminho + "/" + nome);
		}
	}

	private void Andar(Node no, string caminho)
	{
		// O NODE EXISTIR JA E UM CAMPO. Sem esta linha, um node que so nasce num dos lados nao
		// deixaria chave nenhuma quando ele nao tivesse nenhuma propriedade colhivel -- e sumiria
		// da comparacao em vez de reprovar.
		Texto(caminho + " (existe)", no.GetType().Name);

		foreach (string prop in PropriedadesDe(no))
		{
			Variant v;
			try { v = no.Get(prop); }
			catch (Exception) { continue; }   // propriedade que so o editor le
			Guardar(caminho + "." + prop, v);
		}

		if (no is CanvasItem ci && ci.Material is ShaderMaterial mat && mat.Shader is { } sh)
			Uniformes(caminho + "~", mat, sh);

		Filhos(no, caminho);
	}

	/// <summary>
	/// OS UNIFORMES COMO O SHADER OS DECLARA -- e nao como alguem lembrou de escreve-los.
	///
	/// O VALOR NAO ESCRITO CAI NO PADRAO DO SHADER (`RenderingServer.ShaderGetParameterDefault`), e
	/// isso e obrigatorio: um lado que escreve `contorno = 0` e outro que nunca escreveu nada
	/// desenham a MESMA coisa, e sem o padrao a bancada acusaria uma diferenca que nao existe --
	/// ruido que faz desligarem a checagem inteira.
	/// </summary>
	private void Uniformes(string prefixo, ShaderMaterial mat, Shader sh)
	{
		foreach (Godot.Collections.Dictionary u in sh.GetShaderUniformList())
		{
			if (!u.TryGetValue("name", out Variant nv)) continue;
			string nome = nv.AsString();
			if (UniformesDeRelogio.Contains(nome)) continue;

			Variant v = mat.GetShaderParameter(nome);
			if (v.VariantType == Variant.Type.Nil)
				v = RenderingServer.ShaderGetParameterDefault(sh.GetRid(), nome);
			Guardar(prefixo + nome, v);
		}
	}

	private static readonly Dictionary<string, string[]> CachePorTipo = [];

	/// <summary>
	/// AS PROPRIEDADES DE UM TIPO, perguntadas ao motor UMA VEZ. A varredura roda por quadro em ate
	/// trinta nodes; refazer a lista toda vez custaria mais que o resto do retrato inteiro.
	/// </summary>
	private static string[] PropriedadesDe(Node no)
	{
		string tipo = no.GetType().FullName ?? no.GetType().Name;
		if (CachePorTipo.TryGetValue(tipo, out string[]? pronto)) return pronto;

		var achadas = new List<string>();
		foreach (Godot.Collections.Dictionary d in no.GetPropertyList())
		{
			if (!d.TryGetValue("name", out Variant nv)) continue;
			string nome = nv.AsString();
			if (Ignoradas.Contains(nome) || nome.StartsWith('_')) continue;

			// SO O QUE O MOTOR GUARDARIA NUMA CENA. As outras entradas da lista sao categorias,
			// grupos e ajudas de editor -- ler uma delas devolve nulo e infla o retrato com vazio.
			long uso = d.TryGetValue("usage", out Variant uv) ? uv.AsInt64() : 0;
			if ((uso & (long)PropertyUsageFlags.Storage) == 0) continue;

			achadas.Add(nome);
		}

		string[] arr = [.. achadas];
		CachePorTipo[tipo] = arr;
		return arr;
	}

	// =====================================================================
	// GUARDAR (a faixa)
	// =====================================================================
	private void Guardar(string chave, Variant v)
	{
		switch (v.VariantType)
		{
			case Variant.Type.Bool: Numero(chave, v.AsBool() ? 1 : 0); break;
			case Variant.Type.Int: Numero(chave, v.AsInt64()); break;
			case Variant.Type.Float: Numero(chave, v.AsDouble()); break;
			case Variant.Type.Vector2: Numero(chave, v.AsVector2().X, v.AsVector2().Y); break;
			case Variant.Type.Vector2I: Numero(chave, v.AsVector2I().X, v.AsVector2I().Y); break;
			case Variant.Type.Vector3:
				Numero(chave, v.AsVector3().X, v.AsVector3().Y, v.AsVector3().Z); break;
			case Variant.Type.Vector4:
				Numero(chave, v.AsVector4().X, v.AsVector4().Y, v.AsVector4().Z, v.AsVector4().W); break;
			case Variant.Type.Color:
				Numero(chave, v.AsColor().R, v.AsColor().G, v.AsColor().B, v.AsColor().A); break;
			case Variant.Type.Rect2:
				Numero(chave, v.AsRect2().Position.X, v.AsRect2().Position.Y,
					   v.AsRect2().Size.X, v.AsRect2().Size.Y); break;

			case Variant.Type.String:
			case Variant.Type.StringName:
			case Variant.Type.NodePath:
				Texto(chave, v.AsString()); break;

			// UM RECURSO E O CAMINHO DELE. E o que separa "o cabelo do Goku" de "o cabelo do Ultra
			// Instinto" -- e o que separa a `AuraSSjBig` da `colorablebigaura`. Recurso montado em
			// memoria (sem caminho) vira o nome do tipo, que ainda distingue "tem folha" de "nao tem".
			case Variant.Type.Object:
				if (v.As<Resource>() is { } r)
					Texto(chave, r.ResourcePath.Length > 0 ? r.ResourcePath : "<" + r.GetType().Name + ">");
				break;

			// Vetores de dado, dicionarios e RIDs ficam de fora: nao ha faixa util a extrair, e o
			// RID muda de valor a cada processo por ser um ponteiro.
			default: break;
		}
	}

	private void Numero(string chave, params double[] valores)
	{
		if (!_min.TryGetValue(chave, out double[]? mn))
		{
			_min[chave] = [.. valores];
			_max[chave] = [.. valores];
			return;
		}
		double[] mx = _max[chave];
		if (mn.Length != valores.Length) return;
		for (int i = 0; i < valores.Length; i++)
		{
			if (valores[i] < mn[i]) mn[i] = valores[i];
			if (valores[i] > mx[i]) mx[i] = valores[i];
		}
	}

	private void Texto(string chave, string valor)
	{
		if (!_txt.TryGetValue(chave, out SortedSet<string>? s))
			_txt[chave] = s = [];
		s.Add(valor);
	}

	// =====================================================================
	// DISCO -- os dois retratos nascem em PROCESSOS diferentes
	// =====================================================================
	/// <summary>
	/// ESCREVE O RETRATO EM TEXTO. O `user://` e o mesmo pros dois processos desta maquina, e e por
	/// ele que o corpo LOCAL do processo A chega ao juiz, que roda no processo B. Nao ha outro
	/// caminho: a regua de "o corpo alheio esta certo" e o que o DONO daquele corpo ve na tela dele,
	/// e isso mora do outro lado do soquete.
	/// </summary>
	public string Serializar()
	{
		var sb = new StringBuilder();
		sb.Append("forma\t").Append(Forma).Append('\n');
		sb.Append("estado\t").Append(Estado).Append('\n');
		sb.Append("amostras\t").Append(Amostras).Append('\n');
		sb.Append("relogio\t").Append(DateTime.Now.Ticks).Append('\n');

		foreach ((string k, double[] mn) in _min.OrderBy(p => p.Key, StringComparer.Ordinal))
		{
			double[] mx = _max[k];
			sb.Append("N\t").Append(k).Append('\t');
			for (int i = 0; i < mn.Length; i++)
			{
				if (i > 0) sb.Append('|');
				sb.Append(mn[i].ToString("0.#####", CultureInfo.InvariantCulture)).Append(';')
				  .Append(mx[i].ToString("0.#####", CultureInfo.InvariantCulture));
			}
			sb.Append('\n');
		}

		foreach ((string k, SortedSet<string> vs) in _txt.OrderBy(p => p.Key, StringComparer.Ordinal))
			sb.Append("T\t").Append(k).Append('\t').Append(string.Join("|", vs)).Append('\n');

		return sb.ToString();
	}

	public static RetratoDeCorpo? Ler(string caminho)
	{
		if (!Godot.FileAccess.FileExists(caminho)) return null;
		string texto = Godot.FileAccess.GetFileAsString(caminho);
		if (texto.Length == 0) return null;

		var r = new RetratoDeCorpo();
		foreach (string linha in texto.Split('\n'))
		{
			string[] p = linha.Split('\t');
			if (p.Length < 2) continue;
			switch (p[0])
			{
				case "forma": r.Forma = p[1]; break;
				case "estado": r.Estado = p[1]; break;
				case "amostras": r.Amostras = int.TryParse(p[1], out int a) ? a : 0; break;
				case "relogio":
					r.Idade = long.TryParse(p[1], out long t)
						? TimeSpan.FromTicks(Math.Max(0, DateTime.Now.Ticks - t)) : TimeSpan.MaxValue;
					break;
				case "N" when p.Length >= 3:
					{
						string[] comps = p[2].Split('|');
						var mn = new double[comps.Length];
						var mx = new double[comps.Length];
						for (int i = 0; i < comps.Length; i++)
						{
							string[] par = comps[i].Split(';');
							if (par.Length != 2) continue;
							mn[i] = double.Parse(par[0], CultureInfo.InvariantCulture);
							mx[i] = double.Parse(par[1], CultureInfo.InvariantCulture);
						}
						r._min[p[1]] = mn;
						r._max[p[1]] = mx;
						break;
					}
				case "T" when p.Length >= 3:
					r._txt[p[1]] = [.. p[2].Split('|')];
					break;
			}
		}
		return r;
	}

	/// <summary>Ha quanto tempo o retrato lido foi escrito. Retrato velho e retrato de outro estado.</summary>
	public TimeSpan Idade { get; private set; }

	// =====================================================================
	// COMPARAR
	// =====================================================================
	/// <summary>
	/// O QUE DIVERGE ENTRE OS DOIS RETRATOS, uma linha por chave.
	///
	/// <paramref name="soNoDono"/> sao os nodes que existem no corpo do DONO e nao no corpo alheio
	/// **por decisao de projeto** -- cada um com o motivo escrito em quem chama. Faltar node e o
	/// defeito mais grave que esta bancada pode achar, entao a lista de perdao e explicita e curta.
	/// </summary>
	public static List<string> Comparar(RetratoDeCorpo dono, RetratoDeCorpo alheio,
										IReadOnlyCollection<string> soNoDono)
	{
		var fora = new List<string>();
		bool Perdoado(string chave) =>
			DivergemDeProposito.Contains(chave)
			|| soNoDono.Any(n => chave.StartsWith(n, StringComparison.Ordinal));

		foreach (string k in dono.Chaves)
		{
			if (Perdoado(k)) continue;
			if (!alheio._min.ContainsKey(k) && !alheio._txt.ContainsKey(k))
			{ fora.Add($"FALTA no corpo alheio: {k}"); continue; }
		}

		foreach (string k in alheio.Chaves)
			if (!Perdoado(k) && !dono._min.ContainsKey(k) && !dono._txt.ContainsKey(k))
				fora.Add($"SOBRA no corpo alheio: {k}");

		foreach ((string k, SortedSet<string> a) in dono._txt)
		{
			if (Perdoado(k) || !alheio._txt.TryGetValue(k, out SortedSet<string>? b)) continue;
			if (!a.SetEquals(b))
				fora.Add($"{k}: dono [{string.Join(",", a)}] x alheio [{string.Join(",", b)}]");
		}

		foreach ((string k, double[] amn) in dono._min)
		{
			if (Perdoado(k) || !alheio._min.TryGetValue(k, out double[]? bmn)) continue;
			double[] amx = dono._max[k], bmx = alheio._max[k];
			if (amn.Length != bmn.Length)
			{ fora.Add($"{k}: numero de componentes diferente"); continue; }

			for (int i = 0; i < amn.Length; i++)
				if (!Perto(amn[i], bmn[i]) || !Perto(amx[i], bmx[i]))
				{
					fora.Add($"{k}[{i}]: dono {amn[i]:0.###}..{amx[i]:0.###} x "
						   + $"alheio {bmn[i]:0.###}..{bmx[i]:0.###}");
					break;
				}
		}

		return fora;
	}

	/// <summary>
	/// A TOLERANCIA E RELATIVA E ABSOLUTA AO MESMO TEMPO. Absoluta porque cor e forca vivem entre 0
	/// e 1 e um erro de 0,01 ali nao se ve; relativa porque posicao chega a centenas de pixels e
	/// exigir 0,01 px de duas interpolacoes independentes seria exigir o impossivel.
	/// </summary>
	private static bool Perto(double a, double b) =>
		Math.Abs(a - b) <= 0.01 + 0.01 * Math.Max(Math.Abs(a), Math.Abs(b));
}
