using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// AS LINGUAS NO SERVIDOR (2026-10-27) -- os pedidos 118 e 121 do dono; a regra pura e `Core/Races/Linguas.cs`.
///   * A LINGUA DO SAY: cada corpo fala na que escolheu no botao do chat (`lingua_falar:&lt;id&gt;`), e so numa que ele sabe inteira; quem
///     nunca escolheu fala a Galactica. A fala sai com a etiqueta e cada ouvinte a le pela maestria DELE (`Mandar`, `GameServer.Chat.cs`) --
///     e aprende um pouco ouvindo.
///   * O ENSINO: um gesto pra quem esta colado (`lingua_ensinar`), a oferta espera o "sim" (`lingua_aceitar` / `lingua_recusar`) -- a
///     lingua NATIVA de quem ensina, +10% na hora e 2x pra sempre, uma vez por lingua.
///   * O PACOTE (`S2C.Linguas`): a lingua do say e a maestria de cada lingua que o corpo conhece, pro botao e pro cartao da aba Stats.
///   * AS ESFERAS DE NAMEK (o pedido 121): o Porunga so atende quem sabe 75% da lingua namekuseijin (`PorQueNaoFalaComOPorunga`).
/// </summary>
public sealed partial class GameServer
{
	/// <summary>(`--linguasteste`) A LINGUA DE NAMEK VALE NAS ESFERAS NESTE PROCESSO DE BANCADA -- a familia dela a pede. O jogo nao le isto.</summary>
	internal static bool LinguaDeNamekNaBancadaDeTeste;

	/// <summary>A lingua do say de cada corpo (id -> lingua); quem nao esta aqui fala a Galactica.</summary>
	private readonly Dictionary<int, string> _linguaDaFala = [];

	/// <summary>A oferta de ensino de lingua esperando o "sim" do aluno: quem ensina, a lingua e o prazo (ms).</summary>
	private readonly Dictionary<int, (int Mestre, string Lingua, long Ate)> _licaoDeLingua = [];

	internal const string VerboDaLinguaFalar = "lingua_falar:", VerboDaLinguaEnsinar = "lingua_ensinar",
		VerboDaLinguaAceitar = "lingua_aceitar", VerboDaLinguaRecusar = "lingua_recusar";

	/// <summary>Quanto a oferta de ensino espera o "sim" (ms).</summary>
	private const long MsDaOfertaDeLingua = 60_000;

	/// <summary>Este id saiu do mundo: a lingua do say escolhida e a licao pendente somem com ele (id se reusa -- ver `_aoEsquecer`).</summary>
	private void EsquecerAsLinguas(int id)
	{
		_linguaDaFala.Remove(id);
		_licaoDeLingua.Remove(id);
	}

	/// <summary>A LINGUA DO SAY deste corpo agora: a escolhida, se ele ainda a sabe inteira; senao, a Galactica.</summary>
	private string LinguaDaFala(ServerPlayer pl) =>
		_linguaDaFala.TryGetValue(pl.Id, out string? l) && Linguas.PodeFalar(pl.Ficha, l) ? l : Linguas.Galactica;

	/// <summary>As linguas que o botao do chat oferece, na ordem em que ele as alterna: a Galactica, as nativas, a dos Deuses e as aprendidas inteiras.</summary>
	private static List<string> LinguasQueFala(Fighter f)
	{
		List<string> l = [Linguas.Galactica, .. Linguas.Nativas(f)];
		if (Linguas.PodeFalar(f, Linguas.DosDeuses) && !l.Contains(Linguas.DosDeuses)) l.Add(Linguas.DosDeuses);
		foreach ((string lingua, double m) in f.Linguas)
			if (m >= 1 && !l.Contains(lingua)) l.Add(lingua);
		return l;
	}

	/// <summary>O canal de verbos das linguas (despachado em `GameServer.Raciais.cs`). Falso = o id nao e daqui.</summary>
	private bool UsarVerboDeLingua(ServerPlayer pl, string id)
	{
		if (id.StartsWith(VerboDaLinguaFalar, StringComparison.Ordinal)) { EscolherLinguaDaFala(pl, id[VerboDaLinguaFalar.Length..]); return true; }
		switch (id)
		{
			case VerboDaLinguaEnsinar: OferecerLingua(pl); return true;
			case VerboDaLinguaAceitar: ResponderALingua(pl, aceitou: true); return true;
			case VerboDaLinguaRecusar: ResponderALingua(pl, aceitou: false); return true;
			default: return false;
		}
	}

	/// <summary>O BOTAO DO CHAT: troca a lingua do say. Recusa a que o corpo nao sabe inteira.</summary>
	private void EscolherLinguaDaFala(ServerPlayer pl, string lingua)
	{
		if (!Linguas.PodeFalar(pl.Ficha, lingua))
		{
			Avisar(pl, $"você ainda não fala a língua {Linguas.NomeDe(lingua)} ({Linguas.Maestria(pl.Ficha, lingua) * 100:0.#}%).");
			MandarLinguas(pl);
			return;
		}
		if (lingua == Linguas.Galactica) _linguaDaFala.Remove(pl.Id);
		else _linguaDaFala[pl.Id] = lingua;
		Avisar(pl, $"você passa a falar na língua {Linguas.NomeDe(lingua)}.");
		MandarLinguas(pl);
	}

	/// <summary>
	/// O GESTO DE ENSINAR: a lingua NATIVA de quem ensina (a primeira que o aluno ainda nao sabe inteira e nao foi ensinado), pra quem esta
	/// colado na frente. A oferta espera o "sim" do aluno.
	/// </summary>
	private void OferecerLingua(ServerPlayer mestre)
	{
		ServerPlayer? aluno = QuemEstaNaFrente(mestre, _ => true);
		if (aluno == null || !EhPessoa(aluno)) { Avisar(mestre, "não há ninguém na sua frente pra aprender a sua língua."); return; }
		// (2026-10-27) nem lingua se ensina entre personagens do mesmo IP (o pedido 110) -- ver `GameServer.MesmoIp.cs`
		if (MesmoIp(mestre, aluno)) { Avisar(mestre, FraseDoMesmoIp); return; }
		string? lingua = Linguas.Nativas(mestre.Ficha).FirstOrDefault(
			l => Linguas.Maestria(aluno.Ficha, l) < 1 && !aluno.Ficha.LinguasEnsinadas.Contains(l));
		if (lingua == null)
		{
			Avisar(mestre, $"{aluno.Name} já sabe a sua língua, ou já foi ensinado nela.");
			return;
		}
		_licaoDeLingua[aluno.Id] = (mestre.Id, lingua, NowMs() + MsDaOfertaDeLingua);
		Avisar(mestre, $"você se oferece pra ensinar a língua {Linguas.NomeDe(lingua)} a {aluno.Name}.");
		Avisar(aluno, $"{mestre.Name} se oferece pra te ensinar a língua {Linguas.NomeDe(lingua)}: +10% na hora e você passa a aprendê-la "
					  + "2x mais rápido, pra sempre. Use \"Aceitar a língua ensinada\" (aba Other) em 60 s.");
	}

	/// <summary>A RESPOSTA DO ALUNO. A oferta e conferida de novo: quem ensina ainda tem de estar por perto.</summary>
	private void ResponderALingua(ServerPlayer aluno, bool aceitou)
	{
		if (!_licaoDeLingua.Remove(aluno.Id, out var oferta) || NowMs() > oferta.Ate)
		{
			Avisar(aluno, "ninguém está te ensinando uma língua agora.");
			return;
		}
		_players.TryGetValue(oferta.Mestre, out ServerPlayer? mestre);
		if (!aceitou)
		{
			Avisar(aluno, $"você decide não aprender a língua {Linguas.NomeDe(oferta.Lingua)} agora.");
			if (mestre != null) Avisar(mestre, $"{aluno.Name} não quis aprender a língua {Linguas.NomeDe(oferta.Lingua)}.");
			return;
		}
		if (mestre == null || mestre.Zone.Hash != aluno.Zone.Hash
			|| Vec2.Distance(mestre.Pos, aluno.Pos) > 2 * ZoneCollision.TileSize)
		{
			Avisar(aluno, "quem ia te ensinar não está mais por perto.");
			return;
		}
		if (!Linguas.Ensinar(aluno.Ficha, oferta.Lingua))
		{
			Avisar(aluno, $"você já sabe a língua {Linguas.NomeDe(oferta.Lingua)}, ou já foi ensinado nela.");
			return;
		}
		Avisar(aluno, $"{mestre.Name} te ensina a língua {Linguas.NomeDe(oferta.Lingua)}: agora você a entende "
					  + $"{Linguas.Maestria(aluno.Ficha, oferta.Lingua) * 100:0.#}%, e a aprende 2x mais rápido.");
		Avisar(mestre, $"você ensinou a língua {Linguas.NomeDe(oferta.Lingua)} a {aluno.Name}.");
		MandarLinguas(aluno);
	}

	/// <summary>
	/// O PACOTE DAS LINGUAS: a do say, e cada lingua que o corpo conhece (as que ele fala inteiras e as que esta aprendendo) com a maestria e
	/// se foi ensinado nela. Sai quando muda (a escolha, o ensino) e, pra quem esta aprendendo ouvindo, junto da ficha lenta.
	/// </summary>
	private void MandarLinguas(ServerPlayer pl)
	{
		if (pl.Peer == null) return;
		List<string> fala = LinguasQueFala(pl.Ficha);
		List<string> todas = [.. fala, .. pl.Ficha.Linguas.Keys.Where(l => !fala.Contains(l)).OrderBy(l => l, StringComparer.Ordinal)];
		var w = Protocol.Begin(Protocol.S2C.Linguas);
		w.Put(LinguaDaFala(pl));
		w.Put((byte)Math.Min(todas.Count, 255));
		foreach (string l in todas.Take(255))
		{
			w.Put(l);
			w.Put((float)Linguas.Maestria(pl.Ficha, l));
			w.Put(pl.Ficha.LinguasEnsinadas.Contains(l));
		}
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// (o pedido 121) O PORUNGA SO ATENDE QUEM FALA A LINGUA DOS NAMEKUSEIJINS: 75% dela. Vazio = atende. Quem chama e o funil das esferas de
	/// Namek (o chamado e o desejo).
	/// </summary>
	private static string PorQueNaoFalaComOPorunga(ServerPlayer pl)
	{
		double m = Linguas.Maestria(pl.Ficha, Linguas.Namekuseijin);
		return m >= Linguas.MaestriaDasEsferasDeNamek
			? ""
			: $"as esferas de Namek só respondem a quem fala a língua dos Namekuseijins: é preciso entender "
			  + $"{Linguas.MaestriaDasEsferasDeNamek * 100:0}% dela, e você entende {m * 100:0.#}%.";
	}
}
