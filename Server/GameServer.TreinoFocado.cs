using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// O TREINO FOCADO (T) E A MEDITACAO FOCADA (M). O cliente pede pelo verbo <c>focado</c> e manda a tecla
/// crua (<see cref="Protocol.C2S.TreinoTecla"/>, <see cref="Protocol.C2S.RespiracaoTecla"/>). Quem sorteia
/// a letra, quem conhece a fase do marcador e quem soma o BP e o servidor. A conta esta em
/// <see cref="TreinoFocado"/>.
///
/// O anel NAO entra no silencio dos atalhos do embate (<c>Foco.AtalhosMudos</c> / <c>QteMudo</c>). Aquele
/// portao cala o T e o M, e as duas oscilacoes da <c>--diagmudez</c> nascem dele. Aqui o WASD so deixa de
/// ANDAR (<see cref="TreinoFocado.WasdNaoAnda"/>); o resto do teclado continua do jogador. Um embate de
/// verdade, quando comeca, PAUSA o focado: a tecla dele e do embate, nao deste anel.
/// </summary>
public partial class GameServer
{
	private sealed class SessaoFocada
	{
		public bool Meditando;
		public double Bonus = TreinoFocado.PisoDoBonus;
		public int Acertos;
		public char Letra;
		public long Prazo;
		public long InicioResp;
		public long TravaAte;
		public bool Pausado;

		/// <summary>(2026-10-21, revisao) O ultimo julgamento da respiracao (ou o comeco). Ver <see cref="TreinoFocado.RespiracaoSemApertoMs"/>.</summary>
		public long UltimoAperto;
	}

	private readonly Dictionary<int, SessaoFocada> _foco = new();

	/// <summary>A BANCADA INJETA O ATRASO DA REDE de um corpo sem Peer. Nulo em jogo: vale o do LiteNetLib.</summary>
	internal static int? IdaEVoltaDeTeste;

	/// <summary>O tempo de ida e volta da rede deste jogador, em ms. Ver <see cref="TreinoFocado.InstanteVisto"/>.</summary>
	private static long IdaEVoltaDe(ServerPlayer pl) => IdaEVoltaDeTeste ?? pl.Peer?.RoundTripTime ?? 0;

	/// <summary>
	/// O verbo <c>focado</c>. <c>treino</c> ou <c>meditar</c> ligam; qualquer outra palavra desliga.
	/// A atividade (o <c>C2S.Activity</c>) e quem senta o corpo; este verbo so pendura o minigame em
	/// quem ja esta treinando ou meditando de verdade.
	/// </summary>
	private bool ComandoDeTreinoFocado(ServerPlayer pl, string cmd, string arg)
	{
		if (!string.Equals(cmd, "focado", StringComparison.Ordinal)) return false;

		bool meditar = string.Equals(arg, "meditar", StringComparison.Ordinal);
		bool treinar = string.Equals(arg, "treino", StringComparison.Ordinal);
		if (!meditar && !treinar)
		{
			SoltarFocado(pl, avisar: true);
			return true;
		}

		if (meditar && !pl.Ficha.med) DefinirAtividade(pl, Protocol.Activity.Meditando);
		if (treinar && !pl.Ficha.train) DefinirAtividade(pl, Protocol.Activity.Treinando);
		bool sentou = meditar ? pl.Ficha.med : pl.Ficha.train;
		if (!sentou)
		{
			Avisar(pl, meditar ? "não deu pra focar a meditação." : "não deu pra focar o treino.");
			SoltarFocado(pl, avisar: true);
			return true;
		}

		var s = new SessaoFocada { Meditando = meditar, InicioResp = NowMs(), UltimoAperto = NowMs() };
		_foco[pl.Id] = s;
		if (meditar) MandarRespiracao(pl, s, veredito: 0);
		else NovaLetra(pl, s, veredito: 0);
		MandarFoco(pl, s);
		return true;
	}

	/// <summary>
	/// Quanto o passe do <c>Treinar</c> multiplica. 1 fora do focado (o AFK), e 1 tambem enquanto um
	/// embate pausa o anel: naquele intervalo ninguem esta acertando letra.
	/// </summary>
	internal double MultiplicadorDoTreinoFocado(ServerPlayer pl)
	{
		if (!_foco.TryGetValue(pl.Id, out SessaoFocada? s) || s.Pausado || !SessaoCombina(pl, s))
			return TreinoFocado.AfkLevaOBonusDeTeste ? TreinoFocado.Teto : TreinoFocado.PisoDoBonus;
		return s.Bonus;
	}

	/// <summary>A tecla crua do anel. Nao e movimento e nao diz "acertei".</summary>
	private void TeclaDoTreinoFocado(ServerPlayer pl, char c)
	{
		if (!_foco.TryGetValue(pl.Id, out SessaoFocada? s) || s.Meditando || s.Pausado || !SessaoCombina(pl, s))
			return;
		if (!LetrasDoEmbate.EhLetra(c) || s.Letra == '\0') return;

		bool acertou = char.ToUpperInvariant(c) == s.Letra && NowMs() <= s.Prazo;
		Julgar(pl, s, acertou);
		NovaLetra(pl, s, acertou ? (byte)1 : (byte)2);
		MandarFoco(pl, s);
	}

	/// <summary>O espaco da respiracao. A hora e a de chegada do pacote, com a folga da regra.</summary>
	private void EspacoDaRespiracao(ServerPlayer pl)
	{
		if (!_foco.TryGetValue(pl.Id, out SessaoFocada? s) || !s.Meditando || s.Pausado || !SessaoCombina(pl, s))
			return;
		long agora = NowMs();
		if (agora < s.TravaAte) return;

		bool acertou = TreinoFocado.NaFaixa(TreinoFocado.InstanteVisto(agora, IdaEVoltaDe(pl)), s.InicioResp, TreinoFocado.PeriodoDaRespiracaoMs);
		s.TravaAte = agora + TreinoFocado.TravaAposJulgarMs;
		s.UltimoAperto = agora;
		Julgar(pl, s, acertou);
		MandarRespiracao(pl, s, acertou ? (byte)1 : (byte)2);
		MandarFoco(pl, s);
	}

	/// <summary>
	/// O tique cheio (30 Hz), ao lado das letras do embate: um prazo de 900 ms julgado a 5 Hz erraria
	/// por ate 200 ms. Tambem e aqui que o embate pausa o focado e que a sessao cai quando a atividade cai.
	/// </summary>
	private void TicarTreinoFocado()
	{
		if (_foco.Count == 0) return;
		long agora = NowMs();
		foreach (int id in _foco.Keys.ToArray())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) { _foco.Remove(id); continue; }
			SessaoFocada s = _foco[id];
			if (!SessaoCombina(pl, s)) { SoltarFocado(pl, avisar: true); continue; }

			bool embate = _emEmbate.ContainsKey(pl.Id) || _emEmbateDeKi.ContainsKey(pl.Id) || _dancando.ContainsKey(pl.Id);
			if (embate)
			{
				if (!s.Pausado) { s.Pausado = true; MandarFoco(pl, s); }
				continue;
			}
			if (s.Pausado)
			{
				s.Pausado = false;
				s.UltimoAperto = agora;
				if (!s.Meditando) NovaLetra(pl, s, veredito: 0);
				else MandarRespiracao(pl, s, veredito: 0);
				MandarFoco(pl, s);
				continue;
			}

			if (!s.Meditando && s.Letra != '\0' && agora > s.Prazo)
			{
				Julgar(pl, s, acertou: false);
				NovaLetra(pl, s, veredito: 2);
				MandarFoco(pl, s);
			}

			// A RESPIRACAO SEM APERTO E ERRO, como a letra que vence: sem isto o bonus ficava no teto pra quem largou o teclado.
			if (s.Meditando && !TreinoFocado.RespiracaoSemPrazoDeTeste && agora - s.UltimoAperto > TreinoFocado.RespiracaoSemApertoMs)
			{
				Julgar(pl, s, acertou: false);
				s.UltimoAperto = agora;
				MandarRespiracao(pl, s, veredito: 2);
				MandarFoco(pl, s);
			}
		}
	}

	private static bool SessaoCombina(ServerPlayer pl, SessaoFocada s) =>
		s.Meditando ? pl.Ficha.med : pl.Ficha.train;

	private void Julgar(ServerPlayer pl, SessaoFocada s, bool acertou)
	{
		if (acertou)
		{
			s.Acertos++;
			s.Bonus = TreinoFocado.Subir(s.Bonus);
			return;
		}
		s.Acertos = 0;
		s.Bonus = TreinoFocado.Cair(s.Bonus);
		pl.Ficha.stamina = Math.Max(0, pl.Ficha.stamina - TreinoFocado.VigorDoErro(pl.Ficha.maxstamina));
	}

	private void NovaLetra(ServerPlayer pl, SessaoFocada s, byte veredito)
	{
		string letras = LetrasDoEmbate.Letras;
		char letra = TreinoFocado.LetraFixaDeTeste || letras.Length == 0 ? 'W' : letras[_rng.Next(letras.Length)];
		s.Letra = letra;
		s.Prazo = NowMs() + TreinoFocado.PrazoDaLetra(s.Acertos);
		MandarLetra(pl, s, veredito);
	}

	private void SoltarFocado(ServerPlayer pl, bool avisar)
	{
		if (!_foco.Remove(pl.Id) || !avisar || pl.Peer == null) return;
		var w = Protocol.Begin(Protocol.S2C.Foco);
		w.Put(TreinoFocado.ModoNenhum);
		w.Put((byte)0);
		w.Put(TreinoFocado.PisoDoBonus);
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	private void MandarFoco(ServerPlayer pl, SessaoFocada s)
	{
		if (pl.Peer == null) return;
		var w = Protocol.Begin(Protocol.S2C.Foco);
		w.Put(s.Meditando ? TreinoFocado.ModoMeditacao : TreinoFocado.ModoTreino);
		w.Put(s.Pausado ? (byte)1 : (byte)0);
		w.Put(s.Bonus);
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	private void MandarLetra(ServerPlayer pl, SessaoFocada s, byte veredito)
	{
		if (pl.Peer == null) return;
		var w = Protocol.Begin(Protocol.S2C.TreinoFocado);
		w.Put((byte)s.Letra);
		w.Put((int)Math.Max(0, s.Prazo - NowMs()));
		w.Put(veredito);
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	private void MandarRespiracao(ServerPlayer pl, SessaoFocada s, byte veredito)
	{
		if (pl.Peer == null) return;
		var w = Protocol.Begin(Protocol.S2C.Respiracao);
		w.Put(s.InicioResp);
		w.Put((int)TreinoFocado.PeriodoDaRespiracaoMs);
		w.Put(NowMs());
		w.Put(veredito);
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
