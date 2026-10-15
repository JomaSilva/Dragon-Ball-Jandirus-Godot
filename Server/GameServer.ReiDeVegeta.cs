using Godot;
using Jandirus.Core.Ranks;

namespace Jandirus.Server;

/// <summary>
/// O REI VEGETA E O PRINCIPE (v0.3.0, item 17) -- a realeza de Vegeta em NPC, do `PlanetPopulation.dm` do DM
/// (`make_saiyan_elite`, `:372-385`; `populate_vegeta`, `:432-436`; o `King/grant_throne()`, `:185-211`).
///
/// O dono (2026-10-15): *"aparentemente o rei vegeta e o principe npcs n estao spawnando no planeta vegeta, eles deveriam
/// spawnar quando n tem nenhum player no cargo de rei vegeta, e ai sim o jogador pode matar o rei e pegar o trono"*.
///
/// ============================ QUEM POE OS DOIS NO MUNDO E O POVOAMENTO ============================
/// Eles sao duas linhas do plano (`Assets/Data/npcs.json`, `povoamento`) com `"tronoVago": "kov"`: a manutencao so as
/// completa enquanto nenhum JOGADOR carrega o trono de Vegeta (`Povoamento.LinhaVale`). E o `if(!King_of_Vegeta ...)` do
/// `populate_vegeta` (`:435`), com uma divergencia declarada a pedido do dono: no DM o Principe nasce SEMPRE (`:432-433`);
/// aqui ele segue a regra do Rei (*"eles deveriam spawnar quando n tem nenhum player no cargo"*). Quem ja esta de pe nao
/// e tirado quando um jogador sobe ao trono: a manutencao completa ate o alvo, e nunca mata pra chegar nele.
/// ==============================================================================================
///
/// O MOLDE E O KIT DE ELITE DO DM: Saiyajin "Elite", o cabelo do Vegeta, a armadura de elite com as luvas e as botas
/// Saiyajins, e a capa so no Rei; BP de 18 a 22 mil (o Rei) e de 14 a 18 mil (o Principe). Divergencias declaradas: os
/// NOMES sao "Rei Vegeta" e "Príncipe Vegeta" (os do dono; no DM, "King/Prince <nome sorteado>"); o DM sorteia a armadura
/// entre a de elite e a da saga Saiyajin (`:381`) e da ao Principe o cabelo "Vegeta Junior" -- aqui o Rei veste a de
/// elite, o Principe a da saga Saiyajin, e os dois o cabelo "Vegeta" (o "Vegeta Junior" nao esta no catalogo do port).
/// Como os cidadaos, eles nao atacam ninguem por conta propria e revidam quem os ataca ("PEACEFUL but TRAINABLE", `:8-9`).
///
/// MATAR O REI (o `grant_throne()`): o algoz SAIYAJIN (raca ou sangue da mae -- o mesmo `Sucessao.AlgozHerda` do Rei
/// jogador) toma o trono; qualquer outro so derruba o Rei, o trono continua vago, e o proximo Rei nasce na manutencao.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Os moldes da realeza de Vegeta no `npcs.json`.</summary>
	internal const string MoldeDoReiVegeta = "rei_vegeta", MoldeDoPrincipeVegeta = "principe_vegeta";

	/// <summary>DEFEITO INJETADO (`--v030teste`): o Rei NPC cai sem entregar o trono -- matar o Rei nao vale nada.</summary>
	internal static bool ReiNpcNaoEntregaOTronoDeTeste;

	/// <summary>O `view(8)` dos anuncios do DM (`:207-211`): oito tiles em volta do Rei.</summary>
	private const float AlcanceDoAnuncioDoRei = 8 * Jandirus.Core.World.ZoneCollision.TileSize;

	/// <summary>
	/// O REI NPC CAIU -- o `King/mobDeath() -> grant_throne()` do DM. Chamado do funil da derrota (`AoPerderALuta`, so na
	/// MORTE), ao lado da sucessao do Rei jogador: toda morte com algoz passa por la (o soco, o ki, as tecnicas).
	/// </summary>
	private void OReiNpcCaiu(ServerPlayer vitima, ServerPlayer algoz)
	{
		if (!EhNpcDoMundo(vitima) || vitima.Papel?.Molde.Id != MoldeDoReiVegeta) return;
		if (Cargos.Get(Sucessao.ReiDeVegeta) is not { } trono) return;

		// `if(killer && killer.client && !killer.dead && (Race=="Saiyan" || Parent_Race=="Saiyan"))` (`:202`) -- e so com o
		// trono VAGO: um Rei NPC num mundo que ja tem Rei jogador (um admin coroou alguem com ele de pe) cai sem coroar.
		if (!ReiNpcNaoEntregaOTronoDeTeste && TronoVago(trono.Chave) && EhPessoa(algoz) && !algoz.Ficha.dead
			&& Sucessao.AlgozHerda(trono.Chave, algoz.Race, algoz.Ficha.Genoma?.MajorityRace ?? algoz.Race, algoz.Class))
		{
			Avisar(algoz, "ao matar o Rei de Vegeta, você toma o trono! Você é o novo Rei de Vegeta!");
			AvisarPertoG3(vitima, AlcanceDoAnuncioDoRei, $"{algoz.Name} matou o Rei de Vegeta e tomou o trono!", excetoCentro: true);
			Entronizar(trono, algoz, $"{algoz.Name} matou o Rei Vegeta e tomou o trono de Vegeta.");
			return;
		}

		AvisarPertoG3(vitima, AlcanceDoAnuncioDoRei, "o Rei de Vegeta caiu!", excetoCentro: true);
		GD.Print($"[server] o Rei Vegeta (NPC) caiu pelas maos de {algoz.Name} -- o trono continua vago");
	}
}
