using System.Text.Json;
using Godot;
using Jandirus.Core.Social;

namespace Jandirus.Server;

/// <summary>
/// O LIVRO-CAIXA DA REPUTACAO DE PLANETA -- hoje ele so tem uma entrada: derrotar chefe de saga.
///
/// ============================ POR QUE ELE E DO MUNDO E NAO DA CONTA ============================
/// A tentacao e guardar os pontos dentro do `AccountSave`, junto do personagem. O original nao faz
/// isso -- ele tem um savefile proprio, `PlanetRep` (PlanetReputation.dm:238) -- e a razao aparece na
/// pergunta que o sistema precisa responder: *quem e o maior heroi da Terra?* (`planet_rep_top_name`,
/// :95). Isso e uma pergunta sobre o PLANETA, e responde-la com a reputacao espalhada por N arquivos
/// de conta obrigaria a abrir todos eles.
///
/// Entao ele mora ao lado do `mundo.json`, com o mesmo desenho e pelo mesmo motivo daquele: uma
/// coisa que EXISTE no mundo, e continua existindo com o dono desconectado.
/// ==========================================================================================
///
/// A CHAVE E A CONTA, e nao o personagem. E o `ckey(M.key)` do original (:64) -- a fama e da pessoa
/// e nao do corpo; trocar de slot nao apaga o que o povo viu voce fazer.
/// </summary>
public partial class GameServer
{
	/// <summary>planeta -> (conta -> pontos). Mesma forma da `planet_rep` do DM (lista de listas).</summary>
	private readonly Dictionary<string, Dictionary<string, double>> _reputacao =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>conta -> ultimo nome conhecido. E o `planet_rep_names` (:54): a fofoca cita o NOME.</summary>
	private readonly Dictionary<string, string> _nomesDaReputacao = new(StringComparer.OrdinalIgnoreCase);

	private string CaminhoDaReputacao =>
		System.IO.Path.Combine(_store?.Pasta ?? ".", "reputacao.json");

	/// <summary>
	/// A REPUTACAO DESTA CONTA COM CADA PLANETA -- o livro-caixa lido "pela conta" em vez de "pelo
	/// planeta". Quem consome e a <see cref="Jandirus.Core.Ranks.FichaDeRank"/>: quatro cargos do DM
	/// pedem reputacao com um planeta especifico, e eles precisam do mapa, nao de um numero.
	///
	/// So entra planeta em que a conta TEM registro -- quem nunca fez nada em Namek nao aparece, e a
	/// leitura devolve zero pela `FichaDeRank.ReputacaoCom`. O dicionario tem poucas entradas por
	/// construcao (um por planeta visitado com consequencia).
	/// </summary>
	public Dictionary<string, double> ReputacaoDe(string conta)
	{
		var mapa = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
		if (conta.Length == 0) return mapa;

		foreach ((string planeta, Dictionary<string, double> livro) in _reputacao)
			if (livro.TryGetValue(conta, out double v)) mapa[planeta] = v;
		return mapa;
	}

	/// <summary>Quanto o povo de um planeta gosta desta conta. Zero pra quem nunca fez nada la.</summary>
	public double ReputacaoDe(string planeta, string conta) =>
		_reputacao.TryGetValue(planeta, out Dictionary<string, double>? l)
		&& l.TryGetValue(conta, out double v) ? v : 0;

	/// <summary>
	/// SOMA (ou subtrai) reputacao e avisa quem cruzou limiar. Funil unico -- o `planet_rep_add`.
	///
	/// AVISAR FAZ PARTE DA OPERACAO e nao e cortesia: reputacao e um numero que ninguem ve, entao
	/// virar heroi sem uma linha na tela e uma recompensa que o jogador nao recebe. Por isso a
	/// travessia de limiar sai daqui e nao de quem chamou -- quem chama nao sabe onde a pessoa estava.
	/// </summary>
	public void SomarReputacao(string planeta, ServerPlayer quem, double delta, string motivo)
	{
		if (planeta.Length == 0 || quem.Conta.Length == 0 || delta == 0) return;

		if (!_reputacao.TryGetValue(planeta, out Dictionary<string, double>? livro))
			_reputacao[planeta] = livro = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

		double antes = livro.GetValueOrDefault(quem.Conta);
		double depois = Reputacao.Somar(antes, delta);
		livro[quem.Conta] = depois;
		_nomesDaReputacao[quem.Conta] = quem.Name;

		string travessia = Reputacao.Travessia(antes, depois, NomeDoPlaneta(planeta));
		if (travessia.Length > 0) Avisar(quem, travessia);

		GD.Print($"[server] reputacao: {quem.Name} ({quem.Conta}) {delta:+0;-0} em {planeta} "
			   + $"-> {depois:0} ({Reputacao.Faixa(depois)})  [{motivo}]");

		SalvarReputacao();
	}

	/// <summary>"Earth" e o nome do MAPA; "Terra" e o nome que o jogador ouve. E o `(planet == "Earth") ? "Terra"` do DM.</summary>
	private static string NomeDoPlaneta(string zona) =>
		string.Equals(zona, "Earth", StringComparison.OrdinalIgnoreCase) ? "Terra" : zona;

	// =====================================================================
	// DISCO
	// =====================================================================
	/// <summary>O que vai pro `reputacao.json`. Tipo proprio pra o formato do arquivo nao ser um detalhe dos dois dicionarios.</summary>
	private sealed class LivroDeReputacao
	{
		public Dictionary<string, Dictionary<string, double>> Planetas = new();
		public Dictionary<string, string> Nomes = new();
	}

	private void CarregarReputacao()
	{
		try
		{
			if (!System.IO.File.Exists(CaminhoDaReputacao)) return;
			LivroDeReputacao? l = JsonSerializer.Deserialize<LivroDeReputacao>(
				System.IO.File.ReadAllText(CaminhoDaReputacao), new JsonSerializerOptions { IncludeFields = true });
			if (l == null) return;

			foreach ((string p, Dictionary<string, double> livro) in l.Planetas)
				_reputacao[p] = new Dictionary<string, double>(livro, StringComparer.OrdinalIgnoreCase);
			foreach ((string k, string v) in l.Nomes) _nomesDaReputacao[k] = v;

			GD.Print($"[server] reputacao: {_reputacao.Sum(p => p.Value.Count)} registro(s) em "
				   + $"{_reputacao.Count} planeta(s)");
		}
		catch (Exception e) { GD.PushWarning($"[server] reputacao.json ilegivel: {e.Message}"); }
		finally { AnunciarADividaDaReputacao(); }
	}

	/// <summary>
	/// ============================ A DIVIDA SAI NO BOOT, E NAO SO NUM COMENTARIO ============================
	/// Deste sistema entrou **so o livro-caixa** -- o que a recompensa da cadeia de sagas precisava. O
	/// `PlanetReputation.dm` inteiro tem cinco coisas a mais, e todas mudam o jogo (cidadao que caca,
	/// decaimento, penalidade por matar habitante).
	///
	/// Um sistema pela metade que nao diz onde acaba e um sistema que ninguem termina, e a versao cara
	/// disso e alguem daqui a tres meses achando que a reputacao ja esta pronta porque ela tem arquivo,
	/// funil e save. Mesmo motivo pelo qual o recorte do inimigo comum sai no boot com o motivo escrito.
	/// ==================================================================================================
	/// </summary>
	private static void AnunciarADividaDaReputacao() =>
		GD.Print($"[server] reputacao: so o livro-caixa esta portado -- faltam {Reputacao.OqueFalta.Length}: "
			   + string.Join("; ", Reputacao.OqueFalta));

	private void SalvarReputacao()
	{
		if (_store == null) return;
		try
		{
			var l = new LivroDeReputacao { Nomes = _nomesDaReputacao };
			foreach ((string p, Dictionary<string, double> livro) in _reputacao) l.Planetas[p] = livro;

			// TEMPORARIO E RENOMEIA, como o `AccountStore.Gravar`: um processo derrubado no meio da
			// escrita deixaria o livro-caixa de todo mundo pela metade.
			string tmp = CaminhoDaReputacao + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(l,
				new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDaReputacao, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao deu pra salvar reputacao.json: {e.Message}"); }
	}
}
