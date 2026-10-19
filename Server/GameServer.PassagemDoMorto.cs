using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A PASSAGEM DO MORTO PELA TRANSMISSAO INSTANTANEA (v0.3.2) -- o dono, 2026-10-16: *"caso um player morto use instante transmission
/// pra alguem q ta vivo, ele aparece meio transparente e fica 5 segundos no mundo dos vivos dps voltando pro outro mundo"*.
///
/// ============================ O MORTO CONTINUA MORTO, COMO NO PASSE ============================
/// `dead` nao muda e a aureola fica. O que poe o morto DE PE entre os vivos e o prazo no corpo (`ServerPlayer.PassagemAte`, lido pelo
/// `MortoDePe` -- a terceira metade, ao lado do Outro Mundo e do passe do Enma): ele anda, luta e e alvo como o fantasma de la. O que
/// o diferencia do passe e o DESENHO (meio transparente: o bit da passagem no `S2C.Aureola`) e o prazo de 5 s -- de sessao, e nao
/// da ficha: a passagem nao vai pro disco, e quem cai no meio e gravado no lugar da volta (`GravarAVoltaDaPassagem`).
/// ==============================================================================================
///
/// QUEM CHEGA DE PASSAGEM: quem sai MORTO do Outro Mundo pela Transmissao rumo aos vivos -- o dono dela e cada carona morta
/// (`ChegarPelaTransmissaoG11`). O morto com o passe do Enma, que ja esta entre os vivos, transmite como um vivo: nao sai do Outro
/// Mundo. A VOLTA e pro lugar exato de onde a alma saiu (o Inferno, se ela estava no Inferno); se ele deixou de ser do Outro Mundo,
/// a mesa do Enma. Cair entre os vivos (o "morrer de novo") acaba a passagem na mesa do Enma (`MorrerDeNovoNoAlem`), e quem volta a
/// viver no meio fica entre os vivos, vivo.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>DEFEITO INJETADO (`--v030teste` IT): a Transmissao volta a recusar todo morto -- o jogo de 2026-10-15. Falso em jogo, sempre.</summary>
	internal static bool MortoNaoTransmiteDeTeste;

	/// <summary>DEFEITO INJETADO (`--v030teste` IT): a passagem nunca vence -- o morto fica entre os vivos pra sempre. Falso em jogo, sempre.</summary>
	internal static bool PassagemSemVoltaDeTeste;

	/// <summary>O aviso de quem chega de passagem. Um so, pra a bancada cobrar pelo texto.</summary>
	private const string FraseDaPassagem =
		"você atravessa pro mundo dos vivos, meio transparente: a sua alma só aguenta 5 segundos aqui antes de voltar pro Outro Mundo.";

	/// <summary>A passagem comeca: o prazo e o lugar da volta (o de AGORA, no Outro Mundo), antes do salto.</summary>
	private void ArmarAPassagem(ServerPlayer pl)
	{
		pl.PassagemAte = NowMs() + Alem.MsDaPassagem;
		pl.PassagemZona = pl.Zone;
		pl.PassagemPos = pl.Pos;
		GD.Print($"[server] {pl.Name} sai do Outro Mundo de passagem pela Transmissao ({Alem.SegundosDaPassagem:0} s)");
	}

	/// <summary>
	/// O RELOGIO DA PASSAGEM, no laco de fichas (ao lado do passe do Enma): vencido, a alma volta. Quem voltou a viver no meio perde a
	/// passagem calado -- ela nao serve pra nada vivo.
	/// </summary>
	private void CumprirAPassagem(ServerPlayer pl)
	{
		if (pl.PassagemAte == 0) return;
		if (!pl.Ficha.dead)
		{
			pl.PassagemAte = 0;
			MandarFicha(pl);
			return;
		}
		if (NowMs() < pl.PassagemAte || PassagemSemVoltaDeTeste) return;
		AcabarAPassagem(pl);
	}

	/// <summary>A alma volta pro lugar de onde saiu no Outro Mundo -- ou, se aquele lugar nao e mais o Outro Mundo, a mesa do Enma.</summary>
	private void AcabarAPassagem(ServerPlayer pl)
	{
		ZoneKey zona = pl.PassagemZona;
		Vec2 pos = pl.PassagemPos;
		pl.PassagemAte = 0;   // ANTES de mover: a zona do Outro Mundo o recebe sem a passagem (`TrocarAureolas`)
		if (!Alem.EhOAlem(pl.Zone))
		{
			// QUEM ESTA FORA DO CORPO VOLTA PRO CORPO ANTES DE VIAJAR (a mente, o leme) -- a mesma linha do `AcabarOPasse`.
			if (pl.BonecoLargado != null) VoltarDeOndeEstiver(pl, "");
			// (2026-10-18) E QUEM ESTA NUMA NAVE DESEMBARCA ANTES: a nave segue o piloto pra qualquer zona (`TickDasNaves`), e o morto pilota
			// agora (`NaoPilota`) -- sem esta linha o pod iria junto pro Outro Mundo. Ele fica entre os vivos, onde estava.
			if (NaveDoPiloto(pl) is { } nave) Desembarcar(pl, nave);
			if (!Alem.EhOAlem(zona))
			{
				zona = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
				pos = MesaDoEnma(zona);
			}
			MoveToZone(pl.Id, zona, PontoLivre(zona, pos) ?? pos);
			Avisar(pl, $"os {Alem.SegundosDaPassagem:0} segundos acabaram: a sua alma é puxada de volta pro Outro Mundo.");
		}
		MandarFicha(pl);
		Persistir(pl);
		GD.Print($"[server] {pl.Name}: a passagem acabou -- de volta ao Outro Mundo ({pl.Zone.Name})");
	}

	/// <summary>O morto de passagem vai pro disco no lugar da volta -- quem cai no meio dos 5 s acorda no Outro Mundo, e nao como o cadaver dos vivos.</summary>
	private static void GravarAVoltaDaPassagem(CharacterSave salvo, ServerPlayer pl)
	{
		salvo.Zona = pl.PassagemZona.Name;
		salvo.ZonaTipo = pl.PassagemZona.Kind;
		salvo.ZonaSeed = pl.PassagemZona.Seed;
		salvo.X = pl.PassagemPos.X;
		salvo.Y = pl.PassagemPos.Y;
	}
}
