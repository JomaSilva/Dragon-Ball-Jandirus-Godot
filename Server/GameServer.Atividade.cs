using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// **TREINAR E MEDITAR, CONFERIDOS NO SERVIDOR** -- as duas primeiras atividades do `C2S.Activity`.
///
/// A regra pura (a lista de "nao pode", com as linhas do DM) mora em <see cref="TreinoEMeditacao"/>. Aqui mora o
/// que e ESTADO: onde o corpo comecou, e as duas portas --
///
///   * o PEDIDO (<see cref="AceitarTreinoOuMeditacao"/>, chamado pelo `DefinirAtividade`): comeca, ou e
///     recusado com a frase;
///   * o GANHO (<see cref="AtividadeValendo"/>, chamado pelo `Treinar` antes de pagar, 5 Hz): a mesma pergunta, e
///     quem deixou de poder PARA -- o `Ficha.train`/`Ficha.med` caem, o dono le por que, e o cliente larga a
///     pose (<see cref="TreinoEMeditacao.EfeitoDeQueCaiu"/>).
///
/// E o desenho da mineracao (`PorQueNaoMinera` / `TickDaMineracao`, `GameServer.Recursos.cs`), que ja nasceu
/// conferida. A diferenca e ONDE a conferencia de quem ja comecou roda: a mineracao paga uma vez por segundo, no
/// tique dela; treino e meditacao pagam na volta de ganho, e e la que a pergunta e feita -- ganho nenhum corre
/// com a condicao quebrada.
///
/// SO O CORPO QUE UM CLIENTE DIRIGE (`Peer` nao nulo). O habitante que treina pela rotina (`GameServer.Rotina.cs`),
/// o corpo largado de quem mergulhou na mente e o reflexo sao dirigidos pelo servidor, que e quem escreve o
/// `train`/`med` e o `Moving` deles: nao ha pedido a conferir.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): treinar e meditar voltam a ser so declarados -- o servidor
	/// anota o que o cliente disse e paga o ganho andando, voando, brigando ou nocauteado. Lido pelas duas portas
	/// (<see cref="AceitarTreinoOuMeditacao"/> e <see cref="AtividadeValendo"/>). Falso em jogo, sempre.
	/// </summary>
	internal bool AtividadeSemConferenciaDeTeste;

	/// <summary>
	/// Quanto tempo depois do pedido o "parei de andar" do cliente ainda pode estar chegando (ms). O pedido viaja
	/// no canal confiavel e o input no outro, sem ordem entre os dois: quem para e aperta T no mesmo quadro pode
	/// ter o pedido lido antes do ultimo passo. Nessa janela o servidor NAO PAGA e nao derruba -- so espera.
	/// </summary>
	private const long CarenciaDeChegadaMs = 500;

	/// <summary>Onde (zona e ponto) e quando a atividade de um corpo comecou.</summary>
	private sealed class AncoraDeAtividade
	{
		public ulong Zona;
		public Vec2 Onde;
		public long Desde;
	}

	/// <summary>
	/// A ANCORA DE QUEM ESTA TREINANDO OU MEDITANDO. Existir e o que diz "o cliente deste corpo acha que ele esta
	/// numa atividade": e por ela que o servidor sabe que precisa AVISAR quando a atividade acaba por conta dele.
	///
	/// POR CORPO (o objeto), e nao por id -- a mesma tabela fraca do `_carteirasVistas` (`GameServer.Recursos.cs`):
	/// o registro morre com o corpo, sem faxina no logout, e um id reusado nao herda a ancora de ninguem.
	/// </summary>
	private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ServerPlayer, AncoraDeAtividade> _ancorasDeAtividade = new();

	/// <summary>
	/// POR QUE ESTE CORPO NAO PODE TREINAR (ou meditar) AGORA -- vazio = pode. Junta o estado do servidor e
	/// pergunta a <see cref="TreinoEMeditacao.PorQueNao"/>.
	///
	/// O `Oozaru.FeraMeditaDeTeste` (o defeito injetado da bancada `--formasteste`: a fera volta a meditar) abre
	/// aqui as duas linhas que falam da fera, como abre as outras duas portas dela.
	/// </summary>
	private string PorQueNaoTreinaNemMedita(ServerPlayer pl, bool meditar, bool emCurso, bool saiuDoLugar) =>
		TreinoEMeditacao.PorQueNao(meditar, emCurso,
			ko: pl.Ficha.KO,
			cadaver: pl.Ficha.dead && !pl.MortoDePe,
			possuido: SemAsRedeas(pl) && !Oozaru.FeraMeditaDeTeste,
			fera: pl.Oozaru != FormaOozaru.Nao && !Oozaru.FeraMeditaDeTeste,
			agarrado: Agarrado(pl),
			voando: pl.Voando || pl.Altitude > 0f,
			nadando: pl.Nadando,
			atirando: BlastingG12(pl.Id),
			emBriga: pl.Combate is { LutandoDeVerdade: > 0, EmBrigaComAlguem: true },
			saiuDoLugar: saiuDoLugar,
			pesoDemais: pl.Ficha.PesoDemaisPraTreinar(),    // (2026-10-23) acima de 8x o limite do corpo nao se treina (dono)
			gravidadeDemais: pl.Ficha.GravidadeDemaisPraTreinar());   // (2026-10-27) nem acima de 4x a maestria de gravidade (o pedido 117)

	/// <summary>
	/// O PEDIDO DE TREINAR OU MEDITAR PASSA? Recusado, o dono le por que e o cliente larga a pose. Andar NAO e
	/// perguntado aqui (o "parei" pode estar chegando -- ver <see cref="CarenciaDeChegadaMs"/>): quem pede andando
	/// e derrubado na primeira volta de ganho depois da carencia, sem ter ganhado nada.
	/// </summary>
	private bool AceitarTreinoOuMeditacao(ServerPlayer pl, bool meditar)
	{
		if (pl.Peer == null || AtividadeSemConferenciaDeTeste) return true;

		string porque = PorQueNaoTreinaNemMedita(pl, meditar, emCurso: false, saiuDoLugar: false);
		if (porque.Length > 0)
		{
			GD.Print($"[server] {pl.Name}: o pedido de {(meditar ? "meditar" : "treinar")} foi recusado ({porque})");
			Avisar(pl, porque);
			MandarEfeito(pl, TreinoEMeditacao.EfeitoDeQueCaiu, 0);
			return false;
		}

		// (2026-10-24) O T EXAUSTO E RECUSADO JA NO PEDIDO, dizendo quanto falta, e o estomago vazio avisado a cada tentativa (dono) --
		// ver `GameServer.VigorDoTreino.cs`. Meditar nao custa vigor: so o treino pergunta.
		if (!meditar && !PedidoDeTreinoAguenta(pl)) return false;

		// A ANCORA NASCE COM A ATIVIDADE. Repetir o pedido do que ja esta ligado nao a renova: a carencia e de
		// quem acabou de parar, e nao um prazo que se estica apertando a tecla.
		bool jaFazia = meditar ? pl.Ficha.med : pl.Ficha.train;
		if (!jaFazia || !_ancorasDeAtividade.TryGetValue(pl, out _))
			_ancorasDeAtividade.AddOrUpdate(pl, new AncoraDeAtividade { Zona = pl.Zone.Hash, Onde = pl.Pos, Desde = NowMs() });
		return true;
	}

	/// <summary>
	/// **O TREINO (OU A MEDITACAO) DESTE CORPO ESTA VALENDO AGORA?** Chamado pelo `Treinar` antes de pagar.
	///
	///   * nao esta fazendo nada, ou e corpo do servidor: verdadeiro (nao ha o que conferir);
	///   * esta, e pode: verdadeiro -- paga;
	///   * esta, e nao pode mais: a atividade CAI aqui (e o `Treinar` segue como quem esta parado);
	///   * esta se mexendo dentro da carencia, ou com os alvos de Ki: falso -- nao paga e nao derruba.
	/// </summary>
	private bool AtividadeValendo(ServerPlayer pl)
	{
		if (pl.Peer == null || AtividadeSemConferenciaDeTeste) return true;

		Fighter f = pl.Ficha;
		bool temAncora = _ancorasDeAtividade.TryGetValue(pl, out AncoraDeAtividade? a);
		if (!f.train && !f.med)
		{
			// ACABOU POR CONTA DO SERVIDOR (o nocaute, a morte, a posse, a volta do transe): o cliente continua
			// achando que treina, e e a ancora que diz isso. Quem para pela propria tecla nao passa por aqui com
			// ancora -- o `DefinirAtividade` a solta na hora.
			if (temAncora) SoltarAAncoraDaAtividade(pl, avisarOCliente: true);
			return true;
		}

		// QUEM LIGOU POR OUTRA PORTA (o `Ki_Targets` escreve `med`) ou foi levado pra outra zona pelo servidor (o
		// mergulho na mente, a porta da Sala do Tempo) ganha uma ancora nova, sem carencia.
		long agora = NowMs();
		if (a == null || a.Zona != pl.Zone.Hash)
		{
			a = new AncoraDeAtividade { Zona = pl.Zone.Hash, Onde = pl.Pos, Desde = agora - CarenciaDeChegadaMs };
			_ancorasDeAtividade.AddOrUpdate(pl, a);
		}

		float folga = TreinoEMeditacao.FolgaDePosicao;
		bool saiu = pl.Moving || (pl.Pos - a.Onde).LengthSquared > folga * folga;

		// ============================ OS DOIS QUE SE MEXEM SEM CAIR ============================
		//   * A CARENCIA de quem acabou de pedir (ver a constante).
		//   * OS ALVOS DE KI (`Ki_Targets`, `GameServer.Tecnicas.G12.cs`): o verbo poe o corpo em meditacao e
		//     manda SOCAR esferas que nascem a ate quatro tiles. No DM elas se acertam de longe, pelo CLIQUE
		//     (`Meditate.dm:280-289`), que este port nao tem -- aqui so se chega nelas andando. O `med` fica (e
		//     ele que mantem os alvos), e o que este corpo nao leva e o ganho de MEDITACAO enquanto anda.
		// Em nenhum dos dois o ganho corre, e o lugar passa a ser onde o corpo PARAR.
		// =========================================================================================
		if (saiu && (agora - a.Desde < CarenciaDeChegadaMs || _alvosDeKiG12.ContainsKey(pl.Id))
			&& PorQueNaoTreinaNemMedita(pl, f.med, emCurso: true, saiuDoLugar: false).Length == 0)
		{
			a.Onde = pl.Pos;
			return false;
		}

		string porque = PorQueNaoTreinaNemMedita(pl, f.med, emCurso: true, saiu);
		if (porque.Length == 0) return true;

		GD.Print($"[server] {pl.Name}: {(f.med ? "a meditacao" : "o treino")} caiu em {pl.Zone.Name} ({porque})");
		f.train = f.med = false;
		Avisar(pl, porque);
		SoltarAAncoraDaAtividade(pl, avisarOCliente: true);
		return true;
	}

	/// <summary>
	/// A ATIVIDADE DESTE CORPO ACABOU: a ancora sai, e (se nao foi o proprio cliente que parou) ele fica sabendo --
	/// a pose do corpo dele e o "TREINANDO" do HUD so conhecem o que ele mesmo declarou.
	/// </summary>
	private void SoltarAAncoraDaAtividade(ServerPlayer pl, bool avisarOCliente)
	{
		_ancorasDeAtividade.Remove(pl);
		if (avisarOCliente) MandarEfeito(pl, TreinoEMeditacao.EfeitoDeQueCaiu, 0);
	}

	/// <summary>
	/// A PORTA DA BANCADA DO CLIENTE (`--diagatividade`, `Client/RoboDeAtividade.cs`): o que o SERVIDOR diz que
	/// este corpo esta fazendo. So leitura -- a bancada compara a tela com a autoridade, e nao com ela mesma.
	/// </summary>
	internal (bool Treina, bool Medita, bool NoAr) AtividadeDeTeste(int idDoJogador) =>
		_players.TryGetValue(idDoJogador, out ServerPlayer? p)
			? (p.Ficha.train, p.Ficha.med, p.Voando || p.Altitude > 0f)
			: default;
}
