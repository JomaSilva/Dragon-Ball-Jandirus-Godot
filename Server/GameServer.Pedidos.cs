using Jandirus.Core.Forms;
using Jandirus.Core.Social;
using Jandirus.Core.Verbos;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>Um pedido pendente como a tela o mostra: de que tipo, o que dizer, e quantos segundos faltam (0 = sem prazo).</summary>
public readonly record struct PedidoNaTela(Fatos Fato, string Texto, int Segundos);

/// <summary>
/// OS PEDIDOS PENDENTES DE CADA JOGADOR, NO FIO -- o encanamento do `PedidosPendentes` (a regra, e o porque, estao la).
///
/// ============================ LEITURA, E NAO OUVINTE -- E A MESMA LEITURA DOS FATOS ============================
/// A lista e LIDA do estado, no mesmo tique dos fatos (`TickDosFatos`, cinco vezes por segundo), e sai so quando
/// muda -- pelo argumento ja escrito no `GameServer.Fatos.cs`: um aviso em cada porta que cria, responde ou deixa
/// vencer um pedido sao dezenas de portas em oito arquivos, e a proxima que nascer nao ia lembrar de avisar.
///
/// E OS FATOS DE PEDIDO PASSARAM A SAIR DAQUI (<see cref="FatosDe"/> soma os da lista): as condicoes -- o convite que
/// ainda esta no prazo, a bomba que ainda cresce -- estao escritas UMA vez, e a tela que mostra o cartao e a aba que
/// mostra o botao nao tem como discordar.
/// ================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O que cada TELA recebeu por ultimo (a tela vai junto: id se reusa -- ver `_fatosMandados`).</summary>
	private readonly Dictionary<int, (NetPeer Tela, string Assinatura)> _pedidosMandados = [];

	private static int SegundosAte(long ate, long agora) => (int)Math.Clamp(Math.Ceiling((ate - agora) / 1000.0), 1, ushort.MaxValue);

	/// <summary>
	/// **O QUE ESTA NA MESA DESTE JOGADOR ESPERANDO RESPOSTA, AGORA.** Cada ramo e a mesma pergunta que o verb de
	/// responder faz ao recusar (o arquivo esta ao lado), e O PRAZO ENTRA: pedido vencido nao e pedido.
	///
	/// A lista e de quem chama (recheada, e nao devolvida): isto roda pra cada jogador cinco vezes por segundo, e quase
	/// sempre conclui "nada".
	/// </summary>
	internal void PedidosDe(ServerPlayer pl, List<PedidoNaTela> saida)
	{
		saida.Clear();
		long agora = NowMs();

		// O CONVITE DE MESTRE E O DESPERTAR ASSISTIDO dividem o campo (sao mutuamente exclusivos) -- Mestre.cs:352
		if (pl.PedidoDoMestre is { } doMestre && agora <= doMestre.Ate)
			saida.Add(new(Fatos.ConviteDeMestre,
						  doMestre.Despertar
							  ? $"{doMestre.NomeDoMestre} quer despertar em você: {Catalogo.Def(doMestre.FormaId)?.Nome ?? "uma forma"}."
							  : $"{doMestre.NomeDoMestre} quer ser o seu MESTRE.",
						  SegundosAte(doMestre.Ate, agora)));

		// A LICAO DE SKILL -- Ensino.cs:175
		if (pl.PedidoDeLicao is { } licao && agora <= licao.Ate)
			saida.Add(new(Fatos.LicaoOferecida, TextoDaLicao(pl, licao), SegundosAte(licao.Ate, agora)));

		// A FUSAO -- Fusao.cs:619, :756
		if (_pedidosDeFusao.TryGetValue(pl.Id, out PedidoDeFusao? fusao) && agora <= fusao.Ate)
			saida.Add(fusao.Tipo == TipoDeFusao.Namek
				? new(Fatos.ConviteDeAbsorcao, $"{fusao.Nome} quer te ABSORVER (fusão Namekuseijin): você se entrega a ele.", SegundosAte(fusao.Ate, agora))
				: new(Fatos.ConviteDeFusao,
					  $"{fusao.Nome} quer se FUNDIR com você ({(fusao.Tipo == TipoDeFusao.Potara ? "os brincos Potara" : "a dança da fusão")}).",
					  SegundosAte(fusao.Ate, agora)));

		// A GENKIDAMA: o pedido so vale enquanto a bomba de quem pediu CRESCE -- Tecnicas.G12.cs:981
		if (_ofertasDeGenkidamaG12.TryGetValue(pl.Id, out int daBomba) && _players.TryGetValue(daBomba, out ServerPlayer? dono)
			&& _genkidamaG12.TryGetValue(daBomba, out EstadoDaGenkidamaG12? bomba) && bomba.Fase == 1)
			saida.Add(new(Fatos.PedidoDeGenkidama, $"{NomeVisivel(dono)} pede a sua energia pra Genkidama.", 0));

		// (as de baixo sao guardadas por CONTA, e um corpo sem conta nao recebe oferta nenhuma)
		if (pl.Conta.Length > 0)
		{
			if (_ofertasDeJuventudeG8.TryGetValue(pl.Conta, out (string Quem, int Idade, long Ate) juventude) && agora <= juventude.Ate)
				saida.Add(new(Fatos.OfertaDeJuventude,
							  $"{OnlinePorConta(juventude.Quem)?.Name ?? "Alguém"} oferece devolver o seu corpo aos {juventude.Idade} anos.",
							  SegundosAte(juventude.Ate, agora)));                                           // Tecnicas.G8.cs:360

			if (_ofertasDePotencialG11.TryGetValue(pl.Conta, out (string Quem, long Ate) potencial) && agora <= potencial.Ate)
				saida.Add(new(Fatos.OfertaDePotencial,
							  $"{OnlinePorConta(potencial.Quem)?.Name ?? "Alguém"} oferece despertar o seu potencial.",
							  SegundosAte(potencial.Ate, agora)));                                           // Tecnicas.G11.cs:1083

			if (_convitesDeAnciao.TryGetValue(pl.Conta, out (string Assento, string Nomeador) assento))
				saida.Add(new(Fatos.OfertaDeAssento,
							  $"{OnlinePorConta(assento.Nomeador)?.Name ?? "Alguém"} te oferece um assento de Ancião.", 0));   // CargoPortas.cs:413

			if (_desafianteEsperando.Length > 0 && string.Equals(ContaDoDeus, pl.Conta, StringComparison.OrdinalIgnoreCase))
				saida.Add(new(Fatos.DesafioAoTitulo,
							  $"{OnlinePorConta(_desafianteEsperando)?.Name ?? "Alguém"} DESAFIA o seu título.",
							  _prazoDaResposta > agora ? SegundosAte(_prazoDaResposta, agora) : 0));            // CargoDuelo.cs:241
		}
	}

	/// <summary>
	/// O que o cartao de uma licao diz: a skill (e, na licao de MESTRE, que ela custa marcos -- o "sim" gasta), ou a
	/// tecnica de ki que o mestre inventou.
	/// </summary>
	private string TextoDaLicao(ServerPlayer aluno, PedidoDeLicao licao)
	{
		if (licao.Tecnica > 0)
		{
			string tecnica = CorpoDaAssinatura(licao.SigMestre)?.Customizadas.Find(t => t.Id == licao.Tecnica)?.Nome ?? "uma técnica";
			return $"{licao.NomeDoMestre} quer te ensinar a técnica de ki {tecnica}.";
		}

		Jandirus.Core.Skills.Skill? s = _skills?.Get(licao.Path);
		bool doMestre = s != null && Jandirus.Core.Skills.EnsinoDeSkill.LicaoDeMestre(s, MestreDe(aluno.Assinatura) == licao.SigMestre);
		return doMestre
			? $"O seu mestre {licao.NomeDoMestre} quer te ensinar {s!.Nome}: custa {Jandirus.Core.Skills.EnsinoDeSkill.Custo(s)} marco(s) (você tem {aluno.Livro.MarcosLivres})."
			: $"{licao.NomeDoMestre} quer te ensinar {s?.Nome ?? "uma técnica"} (não gasta marco nenhum).";
	}

	/// <summary>
	/// O QUE VAI PRA TELA deste jogador: os pedidos dele. Separada do envio pra bancada medir a lista sem precisar de
	/// uma tela ligada (corpo forjado nao tem `Peer`), e e aqui que o defeito injetado mora: ele cala o CARTAO e deixa
	/// os fatos como estao -- a v0.2.0, em que o pedido so existia como botao no menu P.
	/// </summary>
	internal void PedidosPraTela(ServerPlayer pl, List<PedidoNaTela> saida)
	{
		PedidosDe(pl, saida);
		if (PedidosPendentes.SemNotificacaoDeTeste) saida.Clear();
	}

	/// <summary>A lista reusada do tique -- ver <see cref="PedidosDe"/>.</summary>
	private readonly List<PedidoNaTela> _pedidosDoTique = [];

	/// <summary>
	/// Manda os pedidos deste jogador se a lista mudou -- ou se esta TELA ainda nao recebeu nenhuma. O que decide
	/// "mudou" e o tipo e o texto de cada um, e nao os segundos: o relogio do cartao anda no cliente.
	/// </summary>
	private void MandarPedidos(ServerPlayer pl)
	{
		if (pl.Peer is not { } tela) return;

		PedidosPraTela(pl, _pedidosDoTique);

		string assinatura = _pedidosDoTique.Count == 0 ? "" : string.Join('|', _pedidosDoTique.Select(p => $"{(uint)p.Fato}:{p.Texto}"));
		bool jaTinha = _pedidosMandados.TryGetValue(pl.Id, out (NetPeer Tela, string Assinatura) antes) && ReferenceEquals(antes.Tela, tela);
		if (jaTinha ? antes.Assinatura == assinatura : assinatura.Length == 0) return;   // (tela nova sem pedido nenhum: nada a dizer)
		_pedidosMandados[pl.Id] = (tela, assinatura);

		var w = Protocol.Begin(Protocol.S2C.Pedidos);
		w.Put((byte)Math.Min(_pedidosDoTique.Count, byte.MaxValue));
		foreach (PedidoNaTela p in _pedidosDoTique)
		{
			w.Put((uint)p.Fato);
			w.Put(p.Texto);
			w.Put((ushort)p.Segundos);
		}
		tela.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
