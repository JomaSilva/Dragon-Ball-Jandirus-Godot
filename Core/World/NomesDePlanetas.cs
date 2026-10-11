namespace Jandirus.Core.World;

/// <summary>
/// O NOME QUE UM JOGADOR DEU A UM PLANETA -- e quem pisou nele primeiro.
///
/// O dono (2026-10-10): *"adicione tb a opçao de nomear o planeta (a primeira pessoa q entrou nele ou o
/// dono do planeta q dominou)"*.
///
/// ============================ O QUE O ORIGINAL TEM, E O QUE E NOVO ============================
/// METADE JA EXISTE NO DM: o **batismo** (`Tech/ProceduralSpace.dm:595-735`) -- o PRIMEIRO jogador a
/// pousar num mundo gerado ganha o direito de nomea-lo (`player_named`, `pspace_offer_name`: 3 a 24
/// caracteres, recusa nome de outro planeta e nome de mundo conhecido), e o servidor anuncia
/// *"X batizou um novo mundo: A agora se chama B!"*. A outra metade -- o DONO do dominio nomear -- e
/// pedido do dono e nao existe la (`PlanetConquest.dm` nao tem rename).
///
/// ============================ E UM NOME DE EXIBICAO, E NAO O NOME DA ZONA ============================
/// No DM o batismo e um rename de verdade: ele re-chaveia `pspace_planets`, migra seis prefixos de
/// cache e reescreve o `Planet` de cada mob. Aqui isso seria quebrar o jogo -- o NOME faz parte da
/// identidade da zona (`ZoneKey.Hash` e `Equals` leem `Name`): ele esta nos saves de personagem, nas
/// bases, nas obras, nas naves, nos dominios, na semente do ceu e do clima, e na assinatura do universo
/// que cliente e servidor comparam. Entao o planeta CONTINUA se chamando o que sempre se chamou por
/// dentro, e este registro guarda o nome que se MOSTRA, chaveado pela <see cref="ChaveDePlaneta"/>
/// (a mesma do livro dos mortos: o nome pro pre-feito, `#seed` pro gerado).
/// ==================================================================================================
/// </summary>
public sealed class NomeDePlaneta
{
	/// <summary>`ChaveDePlaneta.Texto`: o nome do pre-feito, ou `#seed` do gerado.</summary>
	public string Chave = "";

	/// <summary>O nome de catalogo (ou o gerado) -- o que o planeta se chama por dentro. So pra mostrar "antes: X".</summary>
	public string Original = "";

	/// <summary>O nome dado. Vazio = ninguem batizou ainda (o registro existe so pelo descobridor).</summary>
	public string Nome = "";

	/// <summary>O personagem que deu o nome que esta valendo.</summary>
	public string PorQuem = "";

	/// <summary>O primeiro jogador a pisar neste mundo (so mundo GERADO), e a assinatura dele.</summary>
	public string Descobridor = "";
	public string AssinaturaDoDescobridor = "";

	/// <summary>O descobridor ja gastou o batismo dele? (o `player_named = 1` do original: o direito e UM)</summary>
	public bool DireitoUsado;

	/// <summary>Quando foi o ultimo batismo, em segundos do relogio do mundo. Pro intervalo entre trocas do dono.</summary>
	public double Quando;
}

/// <summary>O LIVRO DOS NOMES: um registro por planeta que foi descoberto ou batizado.</summary>
public sealed class RegistroDeNomes
{
	private readonly Dictionary<string, NomeDePlaneta> _porChave = new(StringComparer.Ordinal);

	public int Quantos => _porChave.Count;
	public IEnumerable<NomeDePlaneta> Todos => _porChave.Values;

	/// <summary>Os que TEM nome dado -- e o que viaja pro cliente e o que conta como "batizado".</summary>
	public IEnumerable<NomeDePlaneta> Batizados => _porChave.Values.Where(n => n.Nome.Length > 0);

	public NomeDePlaneta? De(string chave) => _porChave.GetValueOrDefault(chave);

	/// <summary>O registro deste planeta, criado na hora se nao havia.</summary>
	public NomeDePlaneta Abrir(string chave, string original)
	{
		if (_porChave.TryGetValue(chave, out NomeDePlaneta? n)) return n;
		n = new NomeDePlaneta { Chave = chave, Original = original };
		_porChave[chave] = n;
		return n;
	}

	public void Por(NomeDePlaneta n) => _porChave[n.Chave] = n;
	public void Limpar() => _porChave.Clear();

	public void Substituir(IEnumerable<NomeDePlaneta> novos)
	{
		_porChave.Clear();
		foreach (NomeDePlaneta n in novos) _porChave[n.Chave] = n;
	}

	/// <summary>O NOME QUE SE MOSTRA: o dado, ou o original quando ninguem batizou.</summary>
	public string NomeVisto(string chave, string original) =>
		_porChave.TryGetValue(chave, out NomeDePlaneta? n) && n.Nome.Length > 0 ? n.Nome : original;

	/// <summary>Outro planeta ja leva este nome dado? (sem ligar pra caixa)</summary>
	public bool EmUso(string nome, string menosAChave) =>
		_porChave.Values.Any(n => n.Chave != menosAChave && n.Nome.Length > 0
								  && string.Equals(n.Nome, nome, StringComparison.OrdinalIgnoreCase));
}

/// <summary>AS REGRAS DO BATISMO: quem pode, e que nome serve.</summary>
public static class Batismo
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--batismoteste`): qualquer um batiza qualquer planeta -- a regra de quem
	/// pode deixa de existir. Lido pela propria regra, em <see cref="DireitoDe"/>.
	/// </summary>
	public static bool QualquerUmBatizaDeTeste;

	/// <summary>`if(length(nm) &lt; 3 || length(nm) &gt; 24)` -- `ProceduralSpace.dm:706`.</summary>
	public const int MinLetras = 3, MaxLetras = 24;

	/// <summary>
	/// O intervalo entre duas trocas de nome do MESMO planeta pelo dono. Cada batismo e anunciado ao
	/// servidor inteiro, e sem intervalo um soberano entediado viraria um megafone. (O descobridor nao
	/// precisa: o direito dele e um so.)
	/// </summary>
	public const double SegundosEntreBatismos = 300;

	/// <summary>De onde vem o direito de quem pede.</summary>
	public enum Direito : byte { Nenhum = 0, Descobridor = 1, Dono = 2, Admin = 3 }

	/// <summary>
	/// ============================ QUEM PODE BATIZAR ============================
	///   * O DONO do dominio (quem dominou o planeta) -- sempre que quiser, respeitado o intervalo.
	///   * O DESCOBRIDOR (o primeiro a pisar num mundo gerado) -- UMA vez, e so enquanto o mundo nao tem
	///     dono: conquista fala mais alto que descoberta. E o `player_named` do original, que la se gasta
	///     ate quando o jogador cancela a caixa; aqui nao ha caixa que se cancele, entao o direito so se
	///     gasta quando o nome e dado.
	///   * O ADMIN -- e moderacao: nome dado por jogador e texto que o servidor inteiro le.
	/// ===========================================================================
	/// </summary>
	public static Direito DireitoDe(bool admin, bool donoAqui, bool temDono, bool descobridor, bool direitoUsado)
	{
		if (admin) return Direito.Admin;
		if (donoAqui || QualquerUmBatizaDeTeste) return Direito.Dono;
		return descobridor && !direitoUsado && !temDono ? Direito.Descobridor : Direito.Nenhum;
	}

	/// <summary>Por que o nome nao serve.</summary>
	public enum Recusa { Pode, Curto, Longo, Caractere, Conhecido, EmUso, Igual }

	/// <summary>
	/// O TEXTO DIGITADO, ARRUMADO: espaco das pontas fora e espaco repetido vira um (o `while(findtext(nm,"  "))`
	/// do original faz o mesmo). Nao corta no tamanho: nome comprido e RECUSADO com o motivo, e nao aparado
	/// em silencio pra virar outro nome.
	/// </summary>
	public static string Limpar(string? digitado) =>
		string.Join(' ', (digitado ?? "").Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

	/// <summary>
	/// O NOME SERVE? `limpo` ja passou pelo <see cref="Limpar"/>. `conhecidos` sao os nomes que pertencem ao
	/// jogo (os planetas do mapa e os lugares do alem) -- o original os recusa porque o codigo decide coisas
	/// pelo nome deles; aqui o motivo e mais simples: dois "Namek" no chat e mentira. `emUso` responde se
	/// OUTRO planeta ja foi batizado assim.
	/// </summary>
	public static Recusa Validar(string limpo, string atual, IEnumerable<string> conhecidos, Func<string, bool> emUso)
	{
		if (limpo.Length < MinLetras) return Recusa.Curto;
		if (limpo.Length > MaxLetras) return Recusa.Longo;
		foreach (char c in limpo)
			if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '\'') return Recusa.Caractere;
		if (string.Equals(limpo, atual, StringComparison.Ordinal)) return Recusa.Igual;
		foreach (string c in conhecidos)
			if (string.Equals(c, limpo, StringComparison.OrdinalIgnoreCase)) return Recusa.Conhecido;
		return emUso(limpo) ? Recusa.EmUso : Recusa.Pode;
	}

	public static string Motivo(Recusa r) => r switch
	{
		Recusa.Curto or Recusa.Longo => $"nome inválido: use de {MinLetras} a {MaxLetras} caracteres.",
		Recusa.Caractere => "nome inválido: só letras, números, espaço, hífen e apóstrofo.",
		Recusa.Igual => "o planeta já se chama assim.",
		Recusa.Conhecido => "esse nome pertence a um mundo conhecido. Escolha outro.",
		Recusa.EmUso => "já existe um planeta batizado com esse nome. Escolha outro.",
		_ => "",
	};
}
