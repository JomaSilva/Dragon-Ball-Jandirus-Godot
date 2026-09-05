using System.Collections.Generic;
using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// ============================ A ULTIMA APARENCIA DE CADA PESSOA QUE EU VI ============================
/// O dono (2026-09-04): *"na aba de People poderia aparecer uma 'foto' da aparencia dessa pessoa na
/// ultima vez que voce a viu, pra voce nao ter que lembrar so pelo nome"*.
///
/// A foto e a APARENCIA (`Appearance`: corpo, cabelo, roupa, cores), e nao um pixel: o `PeerLook` que
/// veste cada corpo que entra no meu campo de visao ja traz tudo o que a tela precisa pra desenhar a
/// pessoa de novo -- e e disso que o cartao da aba People se veste (`MenuJogo.Gente`), com o MESMO
/// `CharacterVisual` que desenha o jogador no mundo e o retrato na selecao de personagem.
///
/// E "DA ULTIMA VEZ QUE VOCE A VIU", literalmente: quem guarda e o CLIENTE, no `user://`, porque e a
/// memoria DESTE jogador -- o servidor nao sabe (nem deve saber) o que cada um lembra de quem. Roupa
/// trocada, cabelo novo, forma: o cartao mostra o que os meus olhos viram por ultimo, e nao a ficha
/// atual do outro. Por NOME, que e a chave que a aba People tem (`ConhecidoInfo.Nome`).
///
/// NO DISCO PELO MESMO FORMATO DO FIO (`PutAppearance`/`GetAppearance`): um segundo serializador da
/// aparencia divergiria do primeiro na primeira camada nova -- e ja aconteceu com o `Rgb` e o STJ.
/// ================================================================================================
/// </summary>
public static class VistosDeGente
{
	public readonly record struct Visto(string Raca, string Genero, Appearance Aparencia);

	private const string Arquivo = "user://vistos.dat";
	private const int Versao = 1;

	private static readonly Dictionary<string, Visto> _vistos = [];
	private static bool _carregado;

	private static VisualCatalog? _catalogo;
	private static bool _catalogoLido;

	/// <summary>
	/// O CATALOGO VISUAL que veste um retrato -- o mesmo `visual.json` da selecao de personagem
	/// (`CharacterSelect`) e do mundo (`World`), lido uma vez. Nulo quando o arquivo nao existe (o
	/// AssetPipeline nao rodou): ai a aba mostra o cartao sem foto, e nao um retangulo cinza.
	/// </summary>
	public static VisualCatalog? Catalogo
	{
		get
		{
			if (_catalogoLido) return _catalogo;
			_catalogoLido = true;
			const string dados = "res://Assets/Data/visual.json";
			if (Godot.FileAccess.FileExists(dados))
				_catalogo = VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));
			return _catalogo;
		}
	}

	/// <summary>A aparencia de `nome` como eu a vi por ultimo, ou nulo se nunca a vi com estes olhos.</summary>
	public static Visto? De(string nome)
	{
		Carregar();
		return _vistos.TryGetValue(nome, out Visto v) ? v : null;
	}

	/// <summary>Quantas pessoas eu ja vi -- so pra bancada e log.</summary>
	public static int Quantos { get { Carregar(); return _vistos.Count; } }

	/// <summary>
	/// ANOTA o que o `PeerLook` acabou de vestir. Grava no disco na hora: e um evento raro (uma vez por
	/// pessoa que entra na tela) e uma memoria que nao pode depender de o jogo fechar bem.
	/// </summary>
	public static void Anotar(string nome, string raca, string genero, Appearance ap)
	{
		if (nome.Length == 0 || ap == null) return;
		Carregar();
		_vistos[nome] = new Visto(raca, genero, ap);
		Gravar();
	}

	/// <summary>Só bancada: uma memoria plantada, sem passar pelo fio nem pelo disco.</summary>
	public static void AnotarDeTeste(string nome, string raca, string genero, Appearance ap)
	{
		_carregado = true;
		_vistos[nome] = new Visto(raca, genero, ap);
	}

	/// <summary>Só bancada: esquece tudo (em memoria).</summary>
	public static void EsquecerDeTeste() { _carregado = true; _vistos.Clear(); }

	private static void Carregar()
	{
		if (_carregado) return;
		_carregado = true;
		if (!Godot.FileAccess.FileExists(Arquivo)) return;
		try
		{
			using Godot.FileAccess f = Godot.FileAccess.Open(Arquivo, Godot.FileAccess.ModeFlags.Read);
			if (f == null) return;
			var r = new NetDataReader(f.GetBuffer((long)f.GetLength()));
			if (r.GetInt() != Versao) return;
			int n = r.GetInt();
			for (int i = 0; i < n; i++)
			{
				string nome = r.GetString(64);
				string raca = r.GetString(24);
				string genero = r.GetString(8);
				Appearance ap = r.GetAppearance();
				_vistos[nome] = new Visto(raca, genero, ap);
			}
		}
		catch (System.Exception e)
		{
			// UM ARQUIVO VELHO OU CORROMPIDO NAO PODE DERRUBAR A ABA: a memoria comeca vazia e e refeita
			// conforme as pessoas passam pela tela.
			GD.PushWarning($"[vistos] nao li {Arquivo}: {e.Message} -- comecando do zero");
			_vistos.Clear();
		}
	}

	private static void Gravar()
	{
		var w = new NetDataWriter();
		w.Put(Versao);
		w.Put(_vistos.Count);
		foreach ((string nome, Visto v) in _vistos)
		{
			w.Put(nome); w.Put(v.Raca); w.Put(v.Genero);
			w.PutAppearance(v.Aparencia);
		}
		using Godot.FileAccess f = Godot.FileAccess.Open(Arquivo, Godot.FileAccess.ModeFlags.Write);
		f?.StoreBuffer(w.CopyData());
	}
}
