using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-17) A LARVA DO BIO-ANDROIDE -- o adendo do dono, em resumo: uma transformacao que todo bio usa a qualquer hora, a larva de
/// quatro patas, com o poder selado, indetectavel, forma de supressao; ela se enterra pra se esconder e voa; pra sair, fica 5 minutos
/// enterrada, amadurece e volta na forma de antes. E, depois: *"A forma de barata do bio androide n consegue absorver, e como dito ela fica
/// sempre no modo 'relaxado' com 5 de bp e escondido do sense e rastreadores"*.
///
/// O QUE MORA ONDE:
///   * a pergunta "e larva?" -- `BioAndroids.EmLarva`: a recem-nascida (`bio_stage == Larva`) e a vestida (`Fighter.bio_larva`), que pra
///     todo o resto sao a mesma larva;
///   * o selo -- `PoderEmUso.Relaxado`: a larva e relaxada pela CARAPACA, entao o poder expresso trava em 5 e o Sense, o scouter e a
///     telepatia nao a acham pelos MESMOS caminhos do relaxado (`Fighter.Power.cs`, `TemEnergiaQueSeSente`); o botao de relaxar e a barra
///     recusam (<see cref="FraseDaLarvaSelada"/>);
///   * sem forma -- `EstadoDeForma.Avaliar` (`PerfilDeFormas.EmLarva`); sem absorver -- `AbsorverBio`; voa sem a skill -- `PodeVoar`;
///   * enterrada -- aqui: some da vista (`EstaOculto`), ninguem a alcanca (`CombatState.Enterrado`), nao anda (`PodeMexerOCorpo`) e sai da
///     grade de corpos (quem anda passa por cima). Cinco minutos SEM SAIR e ela amadurece: a recem-nascida vira o imperfeito pela porta de
///     sempre (`MudarDegrauDoBio`), a vestida volta ao degrau em que estava (<see cref="RomperALarva"/>). O BP base nao se mexe em nenhum dos
///     dois -- o degrau mora nele e nunca saiu.
/// </summary>
public partial class GameServer
{
	/// <summary>A recusa do botao de relaxar e da barra de poder pra larva: ela ja e relaxada, e quem a solta e amadurecer.</summary>
	private const string FraseDaLarvaSelada = "a carapaça de larva segura o seu poder em 5: só amadurecendo (enterrada) ele volta.";

	/// <summary>A larva esta enterrada? (O `Oculto` do snapshot le -- `EstaOculto`.)</summary>
	private bool Enterrado(int id) => _players.TryGetValue(id, out ServerPlayer? pl) && pl.EnterradoDesde >= 0;

	/// <summary>VESTIR A LARVA -- o verb `larva` (aba Skills). So o bio de laboratorio; a larva nao se veste duas vezes.</summary>
	private void VirarLarva(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		if (!f.bio_lab_born) { Avisar(pl, "seu corpo não tem forma de larva."); return; }
		if (BioAndroids.EmLarva(f))
		{
			Avisar(pl, $"você já é uma larva: pra amadurecer, enterre-se por {BioAndroids.SegundosEnterradoPraAmadurecer / 60:0} min.");
			return;
		}
		if (f.KO || f.dead) { Avisar(pl, "nao da, caido."); return; }
		if (EmCena(pl)) { Avisar(pl, "espere a cena acabar."); return; }

		// A FORMA CAI ANTES: a larva e uma forma de supressao, e nada entra por cima dela (`EstadoDeForma.Avaliar`).
		if (!pl.Forma.NaBase) RecuarPara(pl, Jandirus.Core.Forms.Catalogo.IdBase);

		f.bio_larva = true;
		pl.Visual.Corpo = BioAndroids.IndiceDoCorpo(BioAndroids.Larva);
		f.Statify();
		AplicarForma(pl);   // e o `RepercutirPoder` la dentro ja le o selo
		pl.SigAtributos = "";
		TrocarAparencias(pl);
		Persistir(pl);
		Avisar(pl, "você se recolhe na carapaça de LARVA: o poder fica selado (5), nem o sense nem o scouter te acham, e você voa sem esforço. "
				   + $"Pra amadurecer de volta, enterre-se ({BioAndroids.SegundosEnterradoPraAmadurecer / 60:0} min sem sair da terra).");
		AvisarPertoG3(pl, RaioDaVista, $"o corpo de {pl.Name} se contrai numa carapaça de larva!", excetoCentro: true);
		GD.Print($"[server] {pl.Name} vestiu a LARVA (degrau maduro {f.bio_stage}, marco {f.bio_marco})");
	}

	/// <summary>
	/// ENTERRAR-SE / SAIR DA TERRA -- o verb `enterrar` (aba Skills). So a larva, de pe e no chao: nem voando, nem nadando, nem no espaco.
	/// Sair antes dos 5 min ZERA a conta.
	/// </summary>
	private void AlternarEnterrar(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		if (!BioAndroids.EmLarva(f)) { Avisar(pl, "só a larva se enterra."); return; }
		if (pl.EnterradoDesde >= 0)
		{
			Desenterrar(pl);
			Avisar(pl, "você sai da terra antes de amadurecer -- a conta recomeça do zero.");
			AvisarPertoG3(pl, RaioDaVista, $"a terra se mexe e {pl.Name} sai dela.", excetoCentro: true);
			return;
		}
		if (f.KO || f.dead) { Avisar(pl, "nao da, caido."); return; }
		if (pl.Voando) { Avisar(pl, "pouse antes de se enterrar."); return; }
		if (pl.Nadando || Jandirus.Core.World.Espaco.EhEspaco(pl.Zone)) { Avisar(pl, "aqui não há terra pra cavar."); return; }

		pl.EnterradoDesde = NowMs();
		pl.MinutosEnterradoAvisados = 0;
		pl.Moving = false;
		if (pl.Combate != null) pl.Combate.Enterrado = true;
		Avisar(pl, "você cava e some na terra: ninguém te vê nem te alcança aqui embaixo. "
				   + $"{BioAndroids.SegundosEnterradoPraAmadurecer / 60:0} min sem sair e a carapaça amadurece.");
		AvisarPertoG3(pl, RaioDaVista, $"{pl.Name} cava o chão e some!", excetoCentro: true);
	}

	/// <summary>A larva sai da terra (pelo verb, pelo amadurecer ou porque saiu da condicao): vista, alcancavel e andando de novo.</summary>
	private void Desenterrar(ServerPlayer pl)
	{
		pl.EnterradoDesde = -1;
		if (pl.Combate != null) pl.Combate.Enterrado = false;
	}

	/// <summary>
	/// A LARVA ENTERRADA, a cada segundo (`TickDaLarva`): a que saiu da condicao (caiu, morreu, deixou de ser larva) sai da terra; a que fechou
	/// os 5 min AMADURECE; a cada minuto inteiro ela ouve quanto falta.
	/// </summary>
	private void AndarALarva(ServerPlayer pl, long agora)
	{
		if (pl.EnterradoDesde < 0) return;
		Fighter f = pl.Ficha;
		if (!BioAndroids.EmLarva(f) || f.KO || f.dead)
		{
			Desenterrar(pl);
			return;
		}
		double s = (agora - pl.EnterradoDesde) / 1000.0;
		if (s >= BioAndroids.SegundosEnterradoPraAmadurecer)
		{
			Desenterrar(pl);
			AmadurecerALarva(pl);
			return;
		}
		int minutos = (int)(s / 60);
		if (minutos > pl.MinutosEnterradoAvisados)
		{
			pl.MinutosEnterradoAvisados = minutos;
			Avisar(pl, $"a carapaça amadurece debaixo da terra... faltam {Math.Ceiling((BioAndroids.SegundosEnterradoPraAmadurecer - s) / 60):0} min.");
		}
	}

	/// <summary>
	/// A LARVA AMADURECE: a recem-nascida vira o IMPERFEITO pela porta de sempre (`SubirDegrauDoBio` -> `MudarDegrauDoBio`, que ja e o
	/// `dnl_larva_mature` do DM: o rompimento, o marco `bio1`, o corpo, o Ki cheio), e a vestida volta ao degrau em que estava.
	/// </summary>
	private void AmadurecerALarva(ServerPlayer pl)
	{
		if (pl.Ficha.bio_stage <= BioAndroids.Larva) SubirDegrauDoBio(pl, BioAndroids.Imperfeito);
		else RomperALarva(pl);
	}

	/// <summary>
	/// A LARVA VESTIDA SE ROMPE: o corpo volta ao degrau maduro (`bio_stage`, que nunca saiu do lugar) -- o mesmo clarao da carapaca
	/// (`CenaBio.Rompimento`, o `flick` de 0,6 s do DM), o Ki cheio e o corpo inteiro, como no rompimento da recem-nascida. O BP base nao se
	/// mexe: o degrau mora nele e nunca saiu.
	/// </summary>
	private void RomperALarva(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		f.bio_larva = false;
		pl.Visual.Corpo = BioAndroids.IndiceDoCorpo(f.bio_stage);
		f.Statify();
		AplicarForma(pl);
		f.Ki = f.MaxKi;
		pl.Combate?.Corpo.Restaurar();
		pl.Combate?.SincronizarVida();
		pl.SigAtributos = "";
		CenaDoBio(pl, Jandirus.Core.Forms.Cinematicas.CenaBio.Rompimento);
		TrocarAparencias(pl);
		Persistir(pl);
		Avisar(pl, $"a carapaça se rompe e você sai da terra INTEIRO, de volta a {BioAndroids.NomeDoDegrau(f.bio_stage)}: o seu poder volta todo.");
		AvisarPertoG3(pl, RaioDaVista, $"a terra se abre e {pl.Name} emerge da carapaça!", excetoCentro: true);
		GD.Print($"[server] {pl.Name} amadureceu da LARVA de volta ao degrau {f.bio_stage} (marco {f.bio_marco})");
	}

	/// <summary>A larva pediu uma forma: a frase certa (quem barra e o `EstadoDeForma.Avaliar`). Devolve se recusou.</summary>
	private bool LarvaRecusaAForma(ServerPlayer pl)
	{
		if (!BioAndroids.EmLarva(pl.Ficha)) return false;
		Avisar(pl, "a carapaça de larva não se transforma: amadureça primeiro (enterre-se).");
		return true;
	}
}
