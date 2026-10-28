using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O SR. KAIOH E O KORIN (v0.3.0) -- os dois NPCs de conversa do DM que faltavam, semeados no boot como o Enma e com a
/// conversa do original pelo menu E. A regra de onde e quem esta em `Core/World/NpcsDeConversa.cs`.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// SEMEIA os dois, no boot, pelo molde do <see cref="SemearOEnma"/>: obra FIXA do mapa (`DoMapa`), densa como o original
	/// (`density = 1`, `SkyNPCs.dm:14`) e com a armadura no infinito (`attackable = 0`, `:12`). A entrada do catalogo entra
	/// junto e nao vem do disco: o `tech.json` e extraido das construcoes do DM, e nenhum dos dois e uma.
	/// </summary>
	private void SemearOsNpcsDeConversa()
	{
		if (_obras == null) return;
		SemearUmNpcDeConversa(NpcsDeConversa.TipoDoSrKaioh, "Sr. Kaioh",
			"O Kaioh do Norte. Ensina a quem tem coração limpo -- e ri das próprias piadas.",
			"res://Assets/Sprites/NPCs/KingKai.tres", ZoneKey.Premade(Alem.ZonaDoOutroMundo),
			_ => (NpcsDeConversa.SrKaiohX, NpcsDeConversa.SrKaiohY));
		SemearUmNpcDeConversa(NpcsDeConversa.TipoDoKorin, "Korin",
			"O guardião da Torre. Cultiva as sementes Senzu.",
			"res://Assets/Sprites/NPCs/Korin.tres", ZoneKey.Premade(NpcsDeConversa.ZonaDoKorin),
			NpcsDeConversa.CelulaDoKorin);
		// (2026-10-19) os tres doadores dos servicos do Outro Mundo, pelo mesmo molde -- ver `GameServer.ServicosDoAlem.cs`
		SemearOsDoadoresDoAlem();
	}

	private void SemearUmNpcDeConversa(string tipo, string nome, string desc, string arte, ZoneKey zona,
															   Func<int, (int Cx, int Cy)> celula)
	{
		if (_obras!.Get(tipo) == null)
			_obras.Acrescentar(new Construcao
			{
				Id = tipo, Nome = nome, Desc = desc, Custo = -1, Arte = arte, Estado = "default", Densa = true,
			});

		if (_noChao.Any(o => o.Tipo == tipo && o.Zona.Equals(zona))) return;
		if (MapaDaZonaOuCatalogo(zona) is not { } mapa)
		{
			GD.PushWarning($"[server] {nome}: a zona {zona.Name} nao tem mapa -- ele nao foi posto");
			return;
		}
		(int cx, int cy) = celula(mapa.Height);
		const float t = ZoneCollision.TileSize;
		var obra = new Obra
		{
			Id = _proximaObraId++,
			Tipo = tipo,
			X = cx * t + t / 2f,
			Y = cy * t + t / 2f,
			DonoNome = "",
			Aparafusada = true,
			DoMapa = true,
			ErguidaEm = 0,
			Armadura = double.PositiveInfinity,
			ArmaduraMax = double.PositiveInfinity,
		};
		obra.PorZona(zona);
		_noChao.Add(obra);
		AplicarColisaoDasObras(zona);
		GD.Print($"[server] {nome} esta em {zona.Name} ({cx},{cy})");
	}

	/// <summary>
	/// O `kingkai_interact()` do DM (`SkyNPCs.dm:207-226`), na ordem dele: as duas licoes por karma (que valem pro vivo
	/// tambem -- o DM pergunta a licao ANTES do `if(!dead)`), o vivo enxotado com uma piada, e a acolhida do morto.
	///
	/// DIVERGENCIA DECLARADA: a dica do Kaio-ken do DM mostra o BP do jogador (`BP [FullNum(round(BP))]/8000`). Aqui nao
	/// -- a regra principal do dono: *"sem scouter vc n tem como saber seu bp de nenhuma forma"*. O karma aparece.
	/// </summary>
	private void SrKaiohFalar(ServerPlayer pl)
	{
		if (ObraQueAceita(pl, "kaioh_falar") == null) { Avisar(pl, "o Sr. Kaioh não está ao alcance -- chegue mais perto dele."); return; }

		// O KAIO-KEN: coracao limpo E forca de verdade (`:208-211`) -- e, desde 2026-10-27 (o pedido 124d), so pra quem CORREU A SNAKE WAY
		// (`Fighter.SnakeWayFeita`, o mesmo pra Genkidama). Ver `GameServer.SnakeWay.cs`.
		if (pl.Ficha.SnakeWayFeita && pl.Karma >= NpcsDeConversa.KarmaDoKaioken && pl.Ficha.BP >= NpcsDeConversa.BpDoKaioken
			&& LicaoDeNpc(pl, NpcsDeConversa.SkillDoKaioken))
		{
			Avisar(pl, "Sr. Kaioh ajeita os óculos: \"Um coração puro E força de verdade... muito bem! Vou te ensinar a minha "
						   + "técnica suprema: o KAIO-KEN! Use com sabedoria -- é o seu próprio corpo que paga o preço!\"");
			return;
		}
		// A GENKIDAMA: o coracao no maximo (`:212-215`)
		if (pl.Ficha.SnakeWayFeita && pl.Karma >= NpcsDeConversa.KarmaDaGenkidama && LicaoDeNpc(pl, NpcsDeConversa.SkillDaGenkidama))
		{
			Avisar(pl, "Sr. Kaioh sorri, solene: \"O seu coração brilha sem uma única sombra... Você é digno da GENKIDAMA! "
						   + "Erga as mãos e peça emprestada a energia de todos os seres vivos!\"");
			return;
		}
		if (!pl.Ficha.dead)
		{
			Avisar(pl, "Sr. Kaioh ri: \"Você ainda está vivo! Não dá pra treinar no meu planeta até bater as botas. "
						   + "...Quer ouvir uma piada enquanto está aqui?\"");
			return;
		}
		Avisar(pl, "Sr. Kaioh: \"Bem-vindo ao meu pequeno planeta! A gravidade aqui é dez vezes a da Terra -- treine duro e "
					  + "você volta à vida mais forte do que nunca. Cuidado pra não pisar no Bubbles!\"");
		if (!pl.Ficha.SnakeWayFeita)
			Avisar(pl, "Sr. Kaioh: \"Quer aprender comigo? Então corra a SNAKE WAY -- a pé, desde a mesa do Enma! É só falar com ele.\"");
		else if (!pl.Livro.Sabe(NpcsDeConversa.SkillDoKaioken))
			Avisar(pl, $"Sr. Kaioh: \"Mantenha esse coração LIMPO (karma {pl.Karma}/{NpcsDeConversa.KarmaDoKaioken}) e fique forte, "
						   + "e talvez eu te ensine alguma coisinha...\"");
		else if (!pl.Livro.Sabe(NpcsDeConversa.SkillDaGenkidama))
			Avisar(pl, $"Sr. Kaioh: \"Um coração SEM DEFEITO (karma {pl.Karma}/{NpcsDeConversa.KarmaDaGenkidama}) ainda pode "
						   + "aprender o meu maior segredo...\"");
	}

	/// <summary>O `Korin/Click()` do DM (`SkyNPCs.dm:258-264`): uma fala so.</summary>
	private void KorinFalar(ServerPlayer pl)
	{
		if (ObraQueAceita(pl, "korin_falar") == null) { Avisar(pl, "o Korin não está ao alcance -- chegue mais perto dele."); return; }
		Avisar(pl, "Korin gira o cajado: \"Chegou até o topo da Torre, é? Treine duro, e talvez -- TALVEZ -- eu solte "
					  + "uma semente Senzu.\"");
	}

	/// <summary>
	/// A LICAO DE UM NPC: a skill entra como ENSINADA (nao se repassa -- `EnsinoDeSkill.Ensinar`) e o resto do aprender vem
	/// junto: o botao, os poderes e os efeitos (o mesmo trio da licao entre jogadores, `GameServer.Ensino.cs`). Falso se ja
	/// sabia.
	/// </summary>
	private bool LicaoDeNpc(ServerPlayer pl, string path)
	{
		if (_skills?.Get(path) is not { } s || !EnsinoDeSkill.Ensinar(pl.Livro, s)) return false;
		MandarSkills(pl, forcar: true);
		AplicarPoderes(pl);
		AplicarEfeitos(pl);
		GD.Print($"[server] {pl.Name} aprendeu '{s.Nome}' com um NPC de conversa");
		return true;
	}
}
