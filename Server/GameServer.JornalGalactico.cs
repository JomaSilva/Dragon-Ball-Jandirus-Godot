using System.Text.Json;
using Godot;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ============================ O JORNAL GALACTICO (dono, 2026-10-31, o pedido 198) ============================
/// O dono pediu um jornal diario -- a cada dia do jogo, que dura 24 minutos --, com estilo holografico e um aviso no canto inferior direito,
/// contando quem derrotou os viloes de saga, quem e procurado e por que, as prisoes, os viloes causando problemas e onde, e os planetas
/// destruidos e por quem (o texto inteiro do pedido esta no PEDIDO-2026-10-23.md, item 198).
///   * AS NOTICIAS DO DIA se juntam em <see cref="Noticiar"/>, chamado de quem as tem: a Patrulha (o procurado, a prisao, o Deus chamado e a
///     justica dele), a destruicao do planeta (`ConsumarDestruicao`), o chefe de saga que chega e o que cai (`GameServer.Sagas.cs`), e o
///     vilao avulso que aparece e o que cai (`GameServer.Viloes.cs`).
///   * NA VIRADA DO DIA do calendario (`TickDoCalendario`), a EDICAO sai: a data e as noticias do dia que acabou, por ordem de peso (o planeta
///     destruido primeiro, o vilao que apareceu por ultimo), ate <see cref="NoticiasPorEdicao"/>. O dia sem nada vira o "dia calmo". Ela vai
///     pro `jornal.json` (as ultimas <see cref="EdicoesGuardadas"/>) e pra tela de quem esta no jogo (`S2C.Jornal`, com o aviso no canto).
///   * `jornal_ler` (o verbo, e o botao "Ler" do aviso) pede a ultima edicao de novo, aberta.
/// O TEXTO VIAJA EM PORTUGUES, como todo recado do servidor: cada tela o le na lingua dela (`lingua-en.json`, pelos moldes das frases).
/// ======================================================================================================
/// Bancada: `--patrulhateste` (a familia do jornal); fotos: `--diagpatrulha`.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O tipo da noticia -- e a ORDEM dela na edicao (o menor primeiro). O cliente desenha o selo de cada um.</summary>
	public enum TipoDeNoticia : byte
	{
		Geral = 0,
		PlanetaDestruido = 1,
		Desequilibrio = 2,
		Preso = 3,
		Procurado = 4,
		VilaoDerrotado = 5,
		VilaoAtacando = 6,
	}

	public sealed class Noticia
	{
		public TipoDeNoticia Tipo;
		public string Texto = "";
		public long Quando;

		/// <summary>(2026-10-31) A MESMA HISTORIA NO MESMO DIA: a noticia nova com a mesma chave toma o lugar da velha (o procurado que sobe de nivel).</summary>
		public string Chave = "";
	}

	public sealed class EdicaoDoJornal
	{
		public long Dia;
		public string Data = "";
		public List<Noticia> Noticias = [];
	}

	private sealed class LivroDoJornal
	{
		public List<Noticia> DoDia = [];
		public List<EdicaoDoJornal> Edicoes = [];
	}

	/// <summary>Quantas edicoes o disco guarda, quantas noticias cabem numa, e quantas o dia junta no maximo (as mais velhas saem).</summary>
	public const int EdicoesGuardadas = 7, NoticiasPorEdicao = 12, NoticiasDoDiaNoMaximo = 200;

	/// <summary>A edicao do dia em que nada aconteceu.</summary>
	public const string FraseDoDiaCalmo = "Um dia calmo na galáxia: nenhum crime, nenhuma prisão, nenhum planeta perdido.";

	private LivroDoJornal _jornal = new();
	private bool _jornalCarregado;

	private string CaminhoDoJornal => System.IO.Path.Combine(_store?.Pasta ?? ".", "jornal.json");

	/// <summary>(bancada) O fio de cada `S2C.Jornal` que saiu, por destinatario. Nulo em jogo.</summary>
	internal static List<(int Para, byte[] Fio)>? EscutaDoJornal;

	/// <summary>DEFEITO INJETADO (`--patrulhateste`): a virada do dia nao publica a edicao. Lido pelo <see cref="PublicarOJornal"/>. Falso em jogo, sempre.</summary>
	internal static bool JornalNaoSaiDeTeste;

	private void GarantirOJornal()
	{
		if (_jornalCarregado) return;
		_jornalCarregado = true;
		try
		{
			if (System.IO.File.Exists(CaminhoDoJornal)
				&& JsonSerializer.Deserialize<LivroDoJornal>(System.IO.File.ReadAllText(CaminhoDoJornal),
					new JsonSerializerOptions { IncludeFields = true }) is { } lido)
				_jornal = lido;
		}
		catch (Exception e) { GD.PushWarning($"[server] jornal.json ilegivel: {e.Message}"); }
	}

	private void SalvarOJornal()
	{
		if (_store == null || _limpezaEmCurso) return;
		try
		{
			string tmp = CaminhoDoJornal + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(_jornal, new JsonSerializerOptions { IncludeFields = true }));
			System.IO.File.Move(tmp, CaminhoDoJornal, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao deu pra salvar jornal.json: {e.Message}"); }
	}

	/// <summary>A limpeza total (pela da Patrulha, que divide a inscricao com ele).</summary>
	private void ZerarOJornal()
	{
		_jornal = new LivroDoJornal();
		_jornalCarregado = true;
	}

	/// <summary>Uma noticia do dia. O texto vai como esta (em portugues -- cada tela o traduz).</summary>
	internal void Noticiar(TipoDeNoticia tipo, string texto, string chave = "")
	{
		GarantirOJornal();
		// A MESMA HISTORIA NO MESMO DIA E UMA NOTICIA SO: o procurado que sobe tres niveis vira a linha do nivel de agora (a primeira rodada do
		// robo da Patrulha mostrou oito "PROCURADO" do mesmo personagem num dia).
		if (chave.Length > 0) _jornal.DoDia.RemoveAll(n => n.Chave == chave);
		_jornal.DoDia.Add(new Noticia { Tipo = tipo, Texto = texto, Quando = NowMs(), Chave = chave });
		if (_jornal.DoDia.Count > NoticiasDoDiaNoMaximo) _jornal.DoDia.RemoveRange(0, _jornal.DoDia.Count - NoticiasDoDiaNoMaximo);
		SalvarOJornal();
	}

	/// <summary>
	/// A EDICAO DO DIA -- chamada do `TickDoCalendario` na virada (<paramref name="dia"/> e o dia que comeca; as noticias sao as do que acabou),
	/// e pela bancada. Vai pra todo mundo que esta no jogo, com o aviso no canto.
	/// </summary>
	internal void PublicarOJornal(long dia, string data)
	{
		if (JornalNaoSaiDeTeste) return;
		GarantirOJornal();
		var edicao = new EdicaoDoJornal
		{
			Dia = dia,
			Data = data,
			Noticias = [.. _jornal.DoDia.OrderBy(n => (byte)n.Tipo).ThenBy(n => n.Quando).Take(NoticiasPorEdicao)],
		};
		if (edicao.Noticias.Count == 0) edicao.Noticias.Add(new Noticia { Tipo = TipoDeNoticia.Geral, Texto = FraseDoDiaCalmo, Quando = NowMs() });
		_jornal.DoDia.Clear();
		_jornal.Edicoes.Add(edicao);
		if (_jornal.Edicoes.Count > EdicoesGuardadas) _jornal.Edicoes.RemoveRange(0, _jornal.Edicoes.Count - EdicoesGuardadas);
		SalvarOJornal();
		foreach (ServerPlayer pl in Jogadores.ToList()) MandarOJornal(pl, aviso: true);
		GD.Print($"[jornal] a edicao de {data} saiu: {edicao.Noticias.Count} noticia(s)");
	}

	/// <summary>A ultima edicao publicada (nula antes da primeira).</summary>
	internal EdicaoDoJornal? UltimaEdicao
	{
		get
		{
			GarantirOJornal();
			return _jornal.Edicoes.Count > 0 ? _jornal.Edicoes[^1] : null;
		}
	}

	/// <summary>As noticias que ja esperam a proxima edicao (a bancada le).</summary>
	internal IReadOnlyList<Noticia> NoticiasDoDiaDeTeste
	{
		get
		{
			GarantirOJornal();
			return _jornal.DoDia;
		}
	}

	/// <summary>
	/// O PACOTE `S2C.Jornal`: a ultima edicao, pra um jogador -- com o aviso no canto (<paramref name="aviso"/>: a edicao nova) ou aberta na
	/// hora (o `jornal_ler`). Sem edicao nenhuma ainda, quem pediu le quando sai a primeira.
	/// </summary>
	private void MandarOJornal(ServerPlayer pl, bool aviso)
	{
		if (UltimaEdicao is not { } e)
		{
			if (!aviso) Avisar(pl, "o Jornal Galáctico ainda não publicou nenhuma edição: a primeira sai na virada do dia.");
			return;
		}
		var w = Protocol.Begin(Protocol.S2C.Jornal);
		w.Put(aviso);
		w.Put(e.Dia);
		w.Put(e.Data);
		w.Put((byte)Math.Min(e.Noticias.Count, byte.MaxValue));
		foreach (Noticia n in e.Noticias.Take(byte.MaxValue))
		{
			w.Put((byte)n.Tipo);
			w.Put(n.Texto);
		}
		EscutaDoJornal?.Add((pl.Id, w.CopyData()));
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
