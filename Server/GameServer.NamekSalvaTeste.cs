using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A FAMILIA N DA `--chefesteste` (2026-10-19): NAMEK SALVA -- DERRUBADO ANTES DA DEATH BALL, O FREEZA NAO EXPLODE O PLANETA. O dono:
/// *"freeza quando é derrotado em namek ele explode o planeta de qualquer forma aparentemente, ele se for derrotado antes de usar a deathball
/// pra explodir o planeta, o planeta deveria ser salvo"*. Pela cadeia de producao (`SagaComOChefeNoMundo`, `TickDasSagas`, o ultimato):
///   1. o Freeza de Namek na forma final, com o prazo armado, NOCAUTEADO antes de ele vencer: vencido o prazo, ele NAO ergue a Death Ball
///      (continua no chao e tocavel -- a janela de finaliza-lo segue aberta) e o prazo anda 2 min; contra-exemplo `EsferaSobeNoChaoDeTeste`
///      (a esfera subia com ele caido, e o deixava intocavel ali);
///   2. morto nessa janela, a saga fecha VENCIDA, com o anuncio da vitoria, e Namek fica: nem condenada, nem em destruicao.
/// </summary>
public sealed partial class GameServer
{
	private void NamekSalvaDoFreezaCaido()
	{
		GD.Print("[chefes] -- N) Namek salva: derrubado antes da Death Ball, o Freeza nao explode o planeta (dono, 2026-10-19) --");
		SagasLimpas();
		var namek = ZoneKey.Premade("Namek");
		_sagas.Do("freeza_vegeta")!.Fase = FaseDaSaga.Vencida;
		(EloDaSaga elo, EstadoDoElo est, EstadoDoChefe ec) = SagaComOChefeNoMundo("freeza_namek", "freeza_namek");
		ServerPlayer? freeza = _players.TryGetValue(ec.Corpo, out ServerPlayer? f0) ? f0 : null;
		if (freeza?.Papel == null)
		{
			AfirmarCh("(montagem) o Freeza de Namek nasce pela saga, em Namek", false, $"chefe {ec.Fase}");
			SagasLimpas();
			return;
		}

		EscutaDasSagas = [];
		try
		{
			// a forma final pelo caminho do roteiro (o mesmo do `ChefeCaidoNoDegrau`); o tique da cadeia copia o degrau e segura o prazo
			freeza.Papel.Estagio = freeza.Papel.Molde.Estagios.Length - 1;
			EntrarNoDegrau(freeza, cura: 0, anunciar: false);
			TickDasSagas();
			AfirmarCh("(montagem) o Freeza de Namek esta no mundo, na forma final, tocavel, com o prazo da Death Ball armado -- e Namek inteira",
					  est.Fase == FaseDaSaga.EmCurso && ec.Fase == FaseDoChefe.NoMundo && freeza.Papel.Proximo == null && !freeza.Combate.Intocavel
					  && ec.UltimatoEm > _relogioDoMundo && MorteDaZona(namek) == null,
					  $"saga {est.Fase}; chefe {ec.Fase}; forma {freeza.Papel.Estagio + 1}; intocavel {freeza.Combate.Intocavel}; prazo "
					  + $"{ec.UltimatoEm - _relogioDoMundo:0} s; destruicao {MorteDaZona(namek)?.Fase}");

			// ---------------------------------------------------------------- 1. caido quando o prazo vence
			freeza.Combate.Nocautear(60);
			_relogioDoMundo = ec.UltimatoEm + 1;
			TickDasSagas();
			bool tocavel = !freeza.Combate.Intocavel;
			AfirmarCh("**CAIDO NO FIM DO PRAZO, ELE NAO ERGUE A DEATH BALL** (*\"se for derrotado antes de usar a deathball pra explodir o planeta, o "
					  + "planeta deveria ser salvo\"*): continua no chao e TOCAVEL -- a janela de finaliza-lo segue aberta -- e o prazo anda 2 min",
					  !ec.Carregando && freeza.SupernovaDesde == 0 && tocavel && freeza.Ficha.KO && ec.UltimatoEm > _relogioDoMundo + 100,
					  $"carregando {ec.Carregando}; esfera {freeza.SupernovaDesde != 0}; tocavel {tocavel}; caido {freeza.Ficha.KO}; "
					  + $"prazo {ec.UltimatoEm - _relogioDoMundo:0} s");

			// o contra-exemplo: o jogo de antes, do mesmo estado (caido, sem carga, o prazo vencendo agora) -- e depois o que ele fez e desfeito
			// (a esfera, a carga e o prazo). O estado e refeito aqui e nao herdado da checagem de cima: sem o conserto, ela deixaria a carga no ar.
			ec.Carregando = false;
			AcabarASupernova(freeza);
			ec.UltimatoEm = _relogioDoMundo;
			_relogioDoMundo += 1;
			EsferaSobeNoChaoDeTeste = true;
			try { TickDasSagas(); }
			finally { EsferaSobeNoChaoDeTeste = false; }
			bool subiu = ec.Carregando && freeza.SupernovaDesde != 0 && freeza.Combate.Intocavel;
			AfirmarCh("   CONTRA-EXEMPLO (o de antes): caido, ele ergue a Death Ball e fica INTOCAVEL no chao -- ninguem o finaliza, e se ele levanta "
					  + "antes da queda Namek explode; a regua de cima reprovaria", subiu,
					  $"carregando {ec.Carregando}; intocavel {freeza.Combate.Intocavel}");
			ec.Carregando = false;
			AcabarASupernova(freeza);
			ec.UltimatoEm = _relogioDoMundo + 120;

			// ---------------------------------------------------------------- 2. morto na janela
			bool morreu = freeza.Combate.Morrer();
			TickDasSagas();
			AfirmarCh("**MORTO ANTES DA ESFERA, NAMEK FICA**: a saga fecha VENCIDA, com o anuncio da vitoria, sem planeta condenado nem destruicao",
					  morreu && est.Fase == FaseDaSaga.Vencida && !est.Condenado && MorteDaZona(namek) == null
					  && EscutaDasSagas!.Any(t => t.Contains(elo.AnuncioDaVitoria, StringComparison.Ordinal)),
					  $"morreu {morreu}; saga {est.Fase}; condenado {est.Condenado}; destruicao {MorteDaZona(namek)?.Fase}; "
					  + string.Join(" | ", EscutaDasSagas!));
		}
		finally
		{
			EsferaSobeNoChaoDeTeste = false;
			EscutaDasSagas = null;
			TirarATropaDaBancada();
			SagasLimpas();
		}
	}
}
