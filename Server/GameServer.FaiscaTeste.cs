using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// 14) A FAISCA DO TIRO ESTOURA NA CABECA, E NAO NO MEIO DO RAIO (`--projetilteste`, dono 2026-09-23).
///
/// ============================ O QUE SE MEDE, E POR QUE NOS BYTES ============================
/// A foto do dono: um raio de vinte tiles acertando alguem, e o clarao do acerto exatamente no MEIO do
/// feixe. O cliente estourava a faisca no meio de quem bateu e de quem apanhou -- certo pro soco, errado
/// pro tiro. O conserto e o servidor mandar o PONTO DO IMPACTO no relato do golpe (`HitEvent.Ponto`).
///
/// A bancada NAO le a variavel do servidor: ela escuta o FIO (`EscutaDeGolpes`, os bytes que sairiam
/// pros clientes), decodifica com o mesmo `HitEvent.Read` do `GameClient` e pergunta ao
/// `HitEvent.OndeEstoura` -- a MESMA funcao que o desenho do cliente usa -- onde a faisca vai estourar.
/// Um `Write` que esquecesse o ponto deixaria a variavel certa e o fio errado; e o fio que o dono ve.
/// ===========================================================================================
/// </summary>
public partial class GameServer
{
	private void AFaiscaEstouraNaCabeca()
	{
		GD.Print("[projetil] -- 14) A FAISCA DO TIRO ESTOURA NA CABECA DO RAIO, E NAO NO MEIO DELE");

		// ---------------------------------------------------------------- o fio: ida e volta
		var com = new Protocol.HitEvent { Atacante = 1, Alvo = 2, Nivel = 2, TemPonto = true, Ponto = new Vec2(123.5f, -45f),
										  TemDano = true, Dano = 7, Membro = "Torso", Decepou = true, Investiu = true };
		var sem = new Protocol.HitEvent { Atacante = 1, Alvo = 2, Nivel = 2, TemDano = true, Dano = 7, Membro = "Torso", ZanzoEsquiva = true };
		Protocol.HitEvent volta1 = IdaEVolta(com), volta2 = IdaEVolta(sem);
		AfirmarPj("o ponto do impacto atravessa o fio (bit 32 + Vec2 depois do byte de desfechos)",
				  volta1.TemPonto && volta1.Ponto.Equals(com.Ponto) && volta1.Decepou && volta1.Investiu && volta1.Membro == "Torso",
				  $"{volta1.TemPonto} {volta1.Ponto}");
		AfirmarPj("...e o golpe SEM ponto (o soco) continua saindo do mesmo tamanho, com os outros bits intactos",
				  !volta2.TemPonto && volta2.ZanzoEsquiva && !volta2.Investiu && Math.Abs(volta2.Dano - 7) < 1e-6);

		// ---------------------------------------------------------------- o raio
		(float naCabeca, float doMeio, Protocol.HitEvent? h) = FaiscaDeUmTiro(TipoDeProjetil.Beam, "Ki_Wave", 1, tiles: 10);
		AfirmarPj("RAIO: o relato do acerto leva o ponto do impacto", h is { TemPonto: true });
		AfirmarPj($"RAIO: a faisca estoura na FRENTE DA CABECA ({naCabeca:0.0} px dela)", naCabeca < 2f);
		AfirmarPj($"RAIO: ...e longe do meio do feixe ({doMeio / ZoneCollision.TileSize:0.0} tiles do meio)",
				  doMeio > 3 * ZoneCollision.TileSize);

		// ---------------------------------------------------------------- o Final Flash: a cabeca de 128 px
		(naCabeca, doMeio, h) = FaiscaDeUmTiro(TipoDeProjetil.Beam, "Final_Flash", 4, tiles: 14);
		AfirmarPj($"FINAL FLASH (folha de 64 px, escala 4): a faisca estoura na frente da cabeca, na beirada do corpo ({naCabeca:0.0} px)",
				  h is { TemPonto: true } && naCabeca < 2f);

		// ---------------------------------------------------------------- a bola
		(naCabeca, doMeio, h) = FaiscaDeUmTiro(TipoDeProjetil.Blast, "", 1, tiles: 8);
		AfirmarPj($"BOLA: a faisca estoura onde a bola encostou ({naCabeca:0.0} px dela), longe do meio do caminho",
				  h is { TemPonto: true } && naCabeca < 2f && doMeio > 2 * ZoneCollision.TileSize);

		// ---------------------------------------------------------------- o defeito injetado
		FaiscaNoMeioDeTeste = true;
		try
		{
			(naCabeca, doMeio, h) = FaiscaDeUmTiro(TipoDeProjetil.Beam, "Ki_Wave", 1, tiles: 10);
			AfirmarPj("(injetado) sem o ponto no relato a faisca volta pro MEIO do raio -- a regua de cima a pegaria",
					  h is { TemPonto: false } && naCabeca > 3 * ZoneCollision.TileSize && doMeio < 1f,
					  $"{naCabeca:0.0} px da cabeca, {doMeio:0.0} px do meio");
		}
		finally { FaiscaNoMeioDeTeste = false; }

		EscutaDeGolpes = null;
		LimparTudoDaBancada();
	}

	/// <summary>
	/// UM TIRO DE VERDADE (`Disparar`, o tique de producao) ate o primeiro acerto -- e o relato DELE, lido do
	/// fio. Devolve a distancia entre onde a faisca estoura (`HitEvent.OndeEstoura`, com os dois corpos onde
	/// estavam) e o ponto certo -- a frente da cabeca, que e a beirada do corpo -- e a distancia dela ao meio.
	/// </summary>
	private (float NaCabeca, float DoMeio, Protocol.HitEvent? Relato) FaiscaDeUmTiro(TipoDeProjetil tipo, string verbo,
																					double escala, int tiles)
	{
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;
		Vec2 raia = CorredorSeco(tiles + 8);
		ServerPlayer atira = Forjar("Faisca", raia, bp: 200_000);
		atira.Facing = Facing.East;
		ServerPlayer alvo = Forjar("Leva", raia + new Vec2(tiles * T, 0), bp: 200_000);

		Projetil p = Disparar(atira, new ReceitaDeProjetil
		{
			Tipo = tipo, BaseDano = 0.002, Velocidade = 1, AlcanceTiles = 40, Deflectivel = false,
			EscalaVisual = escala, Nome = "Faisca",
		}, verbo: verbo);
		if (tipo == TipoDeProjetil.Beam) p.Canalizando = true;

		EscutaDeGolpes = [];
		for (int t = 0; t < 30 * 6; t++)
		{
			// OS DOIS CORPOS ANTES DO TIQUE: o relato sai no meio dele, com o alvo onde estava ao ser
			// acertado -- depois do tique o arrasto ja o levou.
			Vec2 deAtira = atira.Pos, deAlvo = alvo.Pos;
			UmTiqueDeArrasto();

			foreach ((bool cheio, byte[] fio) in EscutaDeGolpes)
			{
				if (!cheio) continue;
				Protocol.HitEvent h = LerGolpe(fio);
				if (h.Alvo != alvo.Id) continue;

				Vec2 estoura = h.OndeEstoura(deAtira, deAlvo);
				Vec2 certo = tipo == TipoDeProjetil.Beam ? deAlvo - p.Rumo * Feixe.MeioCorpo : deAlvo;
				// A BOLA ENCOSTA COM O CENTRO a ate um raio de impacto do corpo: o certo pra ela e ONDE ELA ESTAVA,
				// e a regua aceita o raio inteiro. O raio encosta com a frente, na beirada -- dois pixels.
				float naCabeca = tipo == TipoDeProjetil.Beam
					? (estoura - certo).Length
					: MathF.Max(0f, (estoura - certo).Length - Projetil.RaioDeImpacto);
				float doMeio = (estoura - (deAtira + deAlvo) * 0.5f).Length;
				EscutaDeGolpes = [];
				return (naCabeca, doMeio, h);
			}
			EscutaDeGolpes.Clear();
			if (!p.Vivo) break;
		}
		return (float.MaxValue, 0f, null);
	}

	private static Protocol.HitEvent IdaEVolta(Protocol.HitEvent h)
	{
		var w = new NetDataWriter();
		h.Write(w);
		return Protocol.HitEvent.Read(new NetDataReader(w.CopyData()));
	}
}
