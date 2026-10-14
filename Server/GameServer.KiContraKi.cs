using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// KI CONTRA KI NO SERVIDOR -- a costura do <see cref="KiContraKi"/> (dono, 2026-10-13: *"permita a colisao de
/// quaisquer ataques de ki, beam vs esferas gigantes, beams vs esferas de ki etc"*).
///
/// A REGRA mora no Core; aqui estao as tres pontas dela: a pergunta feita a cada sub-passo do tiro
/// (<see cref="ChocarComOutroAtaque"/>), o fim de quem perde (<see cref="AcabarNoChoque"/>) e a porta da disputa
/// entre um raio alimentado e uma esfera grande (<see cref="TentarEmbateComEsfera"/>). Raio contra raio continua
/// onde sempre esteve (`GameServer.EmbateDeKi.cs`, `GameServer.Feixe.cs`).
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Quantos ataques acabaram num choque de ki contra ki neste processo. So pra bancada (`--v021teste`).</summary>
	internal int ChoquesDeKiDeTeste;

	/// <summary>
	/// O ATAQUE `p`, INDO PRA <paramref name="nova"/>, ENCOSTA EM OUTRO ATAQUE DE KI? Devolve verdadeiro quando
	/// alguma coisa aconteceu -- e quem chama olha o que sobrou de `p` (morto, esvaziando ou numa disputa: o
	/// passo acaba ali; inteiro: ele venceu e segue).
	///
	/// QUEM NAO ENTRA: os meus proprios ataques (uma rajada nao se desfaz sozinha), o que ainda esta sendo
	/// formado, a cabeca presa numa disputa (quem manda nela e o embate), o raio que ja acabou e so engole o
	/// rastro, o que esta noutro andar -- e RAIO CONTRA RAIO, que tem as regras dele.
	///
	/// DOIS JEITOS DE ENCOSTAR: cabeca com cabeca (os dois raios de toque somados, no plano desenhado), e a BOLA
	/// no TRONCO de um raio (a meia espessura desenhada dele).
	/// </summary>
	private bool ChocarComOutroAtaque(Projetil p, Vec2 nova, List<Projetil> lista, List<ServerPlayer> corpos,
									  ZoneKey zona, bool temChao)
	{
		if (p.Inerte) return false;

		bool raioP = p.Tipo == TipoDeProjetil.Beam;
		Vec2 euNaTela = nova + Feixe.Subida(p.Altitude);
		float meuRaio = KiContraKi.RaioDeToque(p);
		bool houve = false;

		for (int i = lista.Count - 1; i >= 0; i--)
		{
			Projetil q = lista[i];
			if (ReferenceEquals(q, p) || !q.Vivo || q.Dono == p.Dono || q.Inerte || q.EmEmbate || q.Esvaziando) continue;
			bool raioQ = q.Tipo == TipoDeProjetil.Beam;
			if (raioP && raioQ) continue;
			if (!Feixe.PodemSeTocar(p.Altitude, q.Altitude)) continue;

			float toque = meuRaio + KiContraKi.RaioDeToque(q);
			bool cabecas = (Feixe.NaTela(q) - euNaTela).LengthSquared <= toque * toque;
			bool noTronco = false;
			if (!cabecas && raioQ)
			{
				Vec2 subida = Feixe.Subida(q.Altitude);
				noTronco = Feixe.EncostaNoTronco(q.Cauda + subida, q.Pos + subida, q.Rumo, Feixe.AlcanceDaCabeca(q), euNaTela,
												 meuRaio + Feixe.MeiaEspessuraDoTronco(q), out _);
			}
			if (!cabecas && !noTronco) continue;

			// A DISPUTA: o raio alimentado (ou outra esfera grande) contra uma esfera grande, de frente.
			if (cabecas && TentarEmbateComEsfera(p, nova, q, zona)) return true;

			switch (KiContraKi.Decidir(p.PoderDeEmbate(), q.PoderDeEmbate(), raioP, raioQ))
			{
				case FimDoChoqueDeKi.EuVenco:
					// (o TRONCO nao acaba por causa de uma bola: a mais forte so atravessa)
					if (noTronco) break;
					AcabarNoChoque(q, corpos, zona, temChao);
					houve = true;
					break;

				case FimDoChoqueDeKi.EleVence:
					p.Pos = nova;
					AcabarNoChoque(p, corpos, zona, temChao);
					return true;

				default:
					p.Pos = nova;
					AcabarNoChoque(p, corpos, zona, temChao);
					if (!noTronco) AcabarNoChoque(q, corpos, zona, temChao);
					return true;
			}
		}
		return houve;
	}

	/// <summary>
	/// ESTE ATAQUE PERDEU UM CHOQUE DE KI, e acaba onde esta:
	///   * a CABECA DE UM RAIO para e o rastro e engolido (o mesmo fim de quem perde o empurrao: `Defletido`);
	///   * a ESFERA GRANDE faz o estouro grande dela -- cratera, dano em area e o cenario em volta;
	///   * a BOLA COMUM estoura pequeno (a que ninguem ve so se apaga).
	/// </summary>
	private void AcabarNoChoque(Projetil x, List<ServerPlayer> corpos, ZoneKey zona, bool temChao)
	{
		if (!x.Vivo) return;
		ChoquesDeKiDeTeste++;

		if (x.Tipo == TipoDeProjetil.Beam)
		{
			x.Arrastando = 0;
			x.Encostado = false;
			Matar(x, FimDeProjetil.Defletido);
			return;
		}
		if (x.EstouraSempreQueAcaba) { ExplodirOndeAcabou(x, corpos, zona, temChao); return; }
		Matar(x, x.Invisivel ? FimDeProjetil.Apagou : FimDeProjetil.Estourou);
	}

	/// <summary>
	/// **RAIO ALIMENTADO CONTRA ESFERA GRANDE (ou esfera grande contra esfera grande), DE FRENTE: COMECA A
	/// DISPUTA.** E o mesmo cabo de guerra de dois feixes -- as letras, o medidor, o encontro que caminha
	/// (`Comecar`, `TickDosEmbatesDeKi`) --, com a esfera no lugar de um deles. NOVO: no DM a disputa exige
	/// `WaveAttack` dos dois lados (`BeamClash.dm:70`).
	///
	/// O RAIO E SEMPRE O LADO A: e ele quem tem mao e cauda, e o `Comecar` mede o lado A pela boca do cano. Entre
	/// duas esferas, o lado A e a que chegou.
	/// </summary>
	private bool TentarEmbateComEsfera(Projetil p, Vec2 nova, Projetil q, ZoneKey zona)
	{
		if (KiContraKi.EsferaNaoDisputaDeTeste) return false;
		if (!p.EsferaGrande && !q.EsferaGrande) return false;
		if (!Feixe.VemContra(p.Rumo, q.Rumo)) return false;
		if (!PodeDisputarComEsfera(p, zona, out ServerPlayer? donoP) || donoP == null) return false;
		if (!PodeDisputarComEsfera(q, zona, out ServerPlayer? donoQ) || donoQ == null) return false;
		if (donoP.Id == donoQ.Id) return false;

		p.Pos = nova;
		bool qEOLadoA = q.Tipo == TipoDeProjetil.Beam && p.Tipo != TipoDeProjetil.Beam;
		Comecar(new LadoDeKi { Quem = qEOLadoA ? donoQ : donoP, Feixe = qEOLadoA ? q : p },
				new LadoDeKi { Quem = qEOLadoA ? donoP : donoQ, Feixe = qEOLadoA ? p : q },
				Protocol.TipoDeEmbate.FeixeContraFeixe);
		return true;
	}

	/// <summary>
	/// ESTE ATAQUE PODE ENTRAR NUMA DISPUTA COM UMA ESFERA? O raio, pelas condicoes de sempre (<see cref="PodeDisputar"/>:
	/// vivo e alimentado pelo dono). A esfera grande, se esta viva e andando, e quem a jogou esta na zona, de pe,
	/// com Ki e livre de outro embate -- e ele quem vai empurra-la.
	/// </summary>
	private bool PodeDisputarComEsfera(Projetil x, ZoneKey zona, out ServerPlayer? dono)
	{
		if (x.Tipo == TipoDeProjetil.Beam) return PodeDisputar(x, out dono);

		dono = null;
		if (!x.Vivo || !x.EsferaGrande || x.EmEmbate || x.Inerte || x.Rumo.LengthSquared < 1e-6f) return false;
		if (!_players.TryGetValue(x.Dono, out ServerPlayer? d) || !d.Zone.Equals(zona)) return false;
		if (d.Ficha.dead || d.Ficha.KO || d.Ficha.Ki <= 1) return false;
		if (_emEmbateDeKi.ContainsKey(d.Id) || _emEmbate.ContainsKey(d.Id) || EstaDancando(d.Id)) return false;

		dono = d;
		return true;
	}
}
