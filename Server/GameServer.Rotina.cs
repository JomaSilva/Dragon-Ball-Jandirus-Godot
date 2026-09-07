using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A VIDA DIARIA DOS HABITANTES, NO SERVIDOR -- o atuador da <see cref="Rotina"/>, o dono do
/// <see cref="Habitat"/> de cada zona e o leitor do `rotina.json`.
///
/// ============================ O QUE MORA AQUI, E POR QUE SO ISTO ============================
/// A rotina (Core) decide; este arquivo executa pelos funis do jogador: a fala sai pelo `Falar`
/// (chat + balao, alcance de grito, limite de cadencia), o treino e a flag `train` da ficha (a
/// mesma da tecla do jogador, que da a pose de treino), o passo e o `PassoDaIa` (a mesma colisao
/// e velocidade do cerebro de combate). Conversa e convite sao pareados AQUI porque so o servidor
/// enxerga os dois corpos: a rotina de um pede "chame fulano", este arquivo chama a rotina do
/// outro e leva a resposta de volta, no mesmo tique.
///
/// ============================ O TREINO DO HABITANTE E COSMETICO -- decisao propria ============================
/// `TickFichas` chama `Treinar(pl)` pra TODO corpo com `train` ligado, e o `TrainGain` faria o
/// habitante subir de poder sem parar (uma populacao que nunca morre de velhice treinando por
/// horas). O molde diz quanto poder o habitante tem; a pose de treino e so a pose. Ver o corte
/// em `Treinar`.
///
/// ============================ O TIQUE BARATO ============================
/// Zona sem jogador ja dorme inteira (`MenteDormindo`). Zona com jogador: quem esta a mais de
/// `raioDeAtencaoTiles` de TODOS eles pensa um tique a cada `divisorDeLonge`, com o relogio da
/// mente andando os N tiques de uma vez -- as duracoes da rotina continuam em segundos reais, e
/// so o passo (que e por tique) fica mais lento, o que ninguem ve de tao longe.
/// =================================================================================================
/// </summary>
public sealed partial class GameServer
{
	private RotinaConfig _rotina = new();
	private readonly Dictionary<ulong, Habitat> _habitats = new();
	private ulong _tiquesDosCorpos;

	/// <summary>A bancada troca a config (durações curtas, chances cheias) antes de forjar os corpos.</summary>
	internal RotinaConfig RotinaDeTeste { get => _rotina; set => _rotina = value; }

	private void CarregarRotina()
	{
		const string caminho = "res://Assets/Data/rotina.json";
		if (!Godot.FileAccess.FileExists(caminho))
		{
			GD.PushWarning("[server] sem rotina.json -- a vida diaria dos habitantes usa os padroes do codigo");
			return;
		}
		try { _rotina = RotinaConfig.Ler(Godot.FileAccess.GetFileAsString(caminho)); }
		catch (Exception e)
		{
			GD.PushError($"[server] rotina.json ilegivel ({e.Message}) -- padroes do codigo");
			_rotina = new RotinaConfig();
			return;
		}
		int ruins = 0;
		foreach (string p in _rotina.Problemas()) { GD.PushError($"[server] rotina.json: {p}"); ruins++; }
		RotinaConfig r = _rotina;
		GD.Print($"[server] rotina dos habitantes: ocio {r.OciosoMin:0}-{r.OciosoMax:0} s, conversa "
			   + $"{r.ConversaMin:0}-{r.ConversaMax:0} s (raio {r.RaioDeConversaTiles} tiles), spar ate {r.SparMax:0} s, "
			   + $"{r.Frases.Conversa.Length} frases de conversa | espalhamento: regiao {r.Espalhamento.LadoDaRegiaoEmTiles} "
			   + $"tiles, distancia minima {r.Espalhamento.DistanciaMinimaEmTiles} | longe: 1 tique a cada "
			   + $"{r.DivisorDeLonge} alem de {r.RaioDeAtencaoTiles} tiles"
			   + (ruins > 0 ? $"  <<< {ruins} problema(s)" : ""));
	}

	/// <summary>
	/// O HABITAT DA ZONA, levantado na primeira vez que alguem nasce nela e guardado. Obras que
	/// subirem depois nao o mudam: a vaga e conferida de novo na hora (`PontoLivrePerto`).
	/// </summary>
	private Habitat? HabitatDaZona(ZoneKey zona)
	{
		if (_habitats.TryGetValue(zona.Hash, out Habitat? pronto)) return pronto;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		if (mapa == null) return null;
		ulong t0 = Time.GetTicksUsec();
		Vec2 berco = PontoDeNascimento(zona);
		int cx = (int)MathF.Floor(berco.X / ZoneCollision.TileSize);
		int cy = (int)MathF.Floor(berco.Y / ZoneCollision.TileSize);
		Habitat h = Habitat.Levantar(mapa, cx, cy, _rotina.Espalhamento,
									 Espaco.Misturar(SeedDoUniverso, Espaco.Hash64(zona.Name), 0));
		_habitats[zona.Hash] = h;
		GD.Print($"[server] habitat de {zona.Name}: {h.Celulas:N0} celulas navegaveis, {h.Vagas:N0} vagas em "
			   + $"{h.Regioes} regioes ({(Time.GetTicksUsec() - t0) / 1000.0:0.0} ms)");
		return h;
	}

	/// <summary>A bancada troca a config e precisa levantar o habitat de novo com ela.</summary>
	internal void EsquecerHabitatsDeTeste() => _habitats.Clear();

	/// <summary>
	/// ESTE HABITANTE ESTA LONGE DE TODO JOGADOR DA ZONA? So habitante conta: chefe, clone e corpo
	/// possuido pensam todo tique, porque o que eles fazem e visto (ou medido) de longe.
	/// </summary>
	private bool LongeDeTodos(ServerPlayer npc)
	{
		if (!EhNpcDoMundo(npc) || npc.Papel is not { Pacifico: true } || npc.DonoDoClone != 0) return false;
		float raio = _rotina.RaioDeAtencaoTiles * ZoneCollision.TileSize;
		float raio2 = raio * raio;
		foreach (ServerPlayer o in ZoneList(npc.Zone.Hash))
			if (EhJogador(o) && (o.Pos - npc.Pos).LengthSquared <= raio2) return false;
		return true;
	}

	/// <summary>
	/// UM TIQUE DA VIDA DIARIA. Devolve TRUE quando o cerebro de combate deve assumir (spar): a presa
	/// e o parceiro. FALSE = a rotina ja moveu ou parou o corpo neste tique.
	/// </summary>
	private bool TicarARotina(ServerPlayer npc, double dtDaMente, double dtDoPasso,
							  out ServerPlayer? presa, out Vec2 destino)
	{
		presa = null;
		destino = npc.Pos;
		// QUEM NAO E HABITANTE so passeia (ver `Rotina.SoPasseia`): o chefe sem presa anda por ai,
		// como o corpo antigo andava, e nao entra na vida social do povo.
		Rotina r = npc.Rotina ??= new Rotina(_rotina, npc.Papel?.Semente ?? (ulong)npc.Id, npc.Id,
											 soPasseia: npc.Papel is not { Pacifico: true });
		Arredores a = LerArredores(npc, r);
		Ordem o = r.Tique(dtDaMente, a);

		// PAREAMENTO -- as duas chamadas que so o servidor pode fazer, porque so ele ve os dois.
		if (o.Chamar != 0)
		{
			ServerPlayer? outro = CorpoNaZona(npc, o.Chamar);
			if (outro?.Rotina == null || !outro.Rotina.Chamado(npc.Id, r.PrazoDaConversa))
				r.Interromper("conversa: ele nao quis");
		}
		if (o.Convidar != 0)
		{
			ServerPlayer? outro = CorpoNaZona(npc, o.Convidar);
			bool aceitou = outro?.Rotina != null && outro.Rotina.Convite(npc.Id, RazaoDePoder(npc, outro));
			r.Resposta(aceitou);
		}

		if (o.Falar is { Length: > 0 } frase) Falar(npc, Protocol.Fala.Diz, frase);
		if (npc.Ficha.train != o.Treinar) npc.Ficha.train = o.Treinar;
		if (_diagIa && !string.Equals(r.Porque, npc.PorqueDaRotina, StringComparison.Ordinal))
		{
			npc.PorqueDaRotina = r.Porque;
			GD.Print($"[rotina] {npc.Name}: {r.Afazer} ({r.Porque})" + (r.Parceiro != 0 ? $" com #{r.Parceiro}" : ""));
		}

		if (o.Lutar != 0 && CorpoNaZona(npc, o.Lutar) is { } parceiro && !parceiro.Ficha.dead)
		{
			presa = parceiro;
			destino = parceiro.Pos;
			return true;
		}

		// O CORPO E DA ROTINA neste tique: um passo (ou a parada) pelo mesmo funil do cerebro -- o
		// atuador inteiro, e nao so o passo, porque a agua pode pedir "decolar" ou "nadar" (ver abaixo).
		var c = new Comando { Rumo = o.Rumo, Olhar = o.Olhar };
		c = AtravessarAgua(npc, c, dtDoPasso);
		AplicarComando(npc, c, dtDoPasso);
		npc.QuisAndar = o.Rumo.LengthSquared > 1e-6f;
		return false;
	}

	/// <summary>
	/// A AGUA PRA QUEM E DA ROTINA -- o MESMO filtro do cerebro de luta (`Travessia`), com os mesmos
	/// numeros que os verbs cobram. O habitante jogado num lago por um jogador nada ate a margem (ou
	/// decola, se souber voar, e pousa no seco), e volta ao que estava fazendo.
	///
	/// So monta a percepcao (e le as capacidades) quando ha agua na historia: no seco, sem passo
	/// barrado e no chao, e um `if` e nada mais -- a rotina roda pra todo habitante, todo tique.
	/// </summary>
	private Comando AtravessarAgua(ServerPlayer npc, Comando c, double dt)
	{
		if (npc.CarenciaDeAgua > 0) npc.CarenciaDeAgua -= dt;
		bool naAgua = PesNaAgua(npc);
		if (!naAgua && !npc.BarradoPelaAgua && !npc.Voando) return c;

		var p = new Percepcao
		{
			Minha = npc.Pos, EstouVoando = npc.Voando, Ki = npc.Ficha.Ki,
			Caido = npc.Ficha.KO || npc.Ficha.dead, Arremessado = npc.TiquesDeVoo > 0,
			NaAgua = naAgua, EstouNadando = npc.Nadando, BarradoPelaAgua = npc.BarradoPelaAgua,
			RumoDaMargem = RumoDaMargem(npc),
		};
		Capacidades cap = npc.Perfil.Filtrar(new Capacidades
		{
			PodeVoar = PodeVoar(npc),
			CustoDeDecolar = Voo.CustoParaLigar(HabilidadeDeVoo(npc), npc.VooRapido),
			KiParaNadar = Nado.KiParaComecar(npc.Ficha.MaxKi, npc.Ficha.KiMod, npc.Ficha.swimmastery),
		});
		Comando pronto = Travessia.Atravessar(p, cap, c, ref npc.CarenciaDeAgua, pousarNoSeco: true, out string? porque);
		if (porque != null && _diagIa && !string.Equals(porque, npc.PorqueDaRotina, StringComparison.Ordinal))
		{
			npc.PorqueDaRotina = porque;
			GD.Print($"[rotina] {npc.Name}: {porque}");
		}
		return pronto;
	}

	private Arredores LerArredores(ServerPlayer npc, Rotina r)
	{
		Fighter f = npc.Ficha;
		int vizinho = 0;
		Vec2 doVizinho = npc.Pos;
		if (r.PrecisaDeVizinho && HabitanteLivrePerto(npc) is { } v) { vizinho = v.Id; doVizinho = v.Pos; }
		ServerPlayer? parceiro = r.Parceiro != 0 ? CorpoNaZona(npc, r.Parceiro) : null;
		return new Arredores
		{
			Minha = npc.Pos,
			VidaFrac = f.HP / 100.0,
			MeuPoder = Finito(f.expressedBP),
			Caido = f.KO || f.dead,
			Bloqueado = npc.QuisAndar && !npc.Moving,
			VizinhoLivre = vizinho,
			DoVizinho = doVizinho,
			ParceiroPresente = parceiro != null && !parceiro.Ficha.dead,
			DoParceiro = parceiro?.Pos ?? npc.Pos,
			VidaDoParceiro = (parceiro?.Ficha.HP ?? 100) / 100.0,
			ParceiroCaido = parceiro is { } pc && (pc.Ficha.KO || pc.Ficha.dead),
			AfazerDoParceiro = parceiro?.Rotina?.Afazer ?? Afazer.Ocioso,
			ParceiroDoParceiro = parceiro?.Rotina?.Parceiro ?? 0,
		};
	}

	/// <summary>
	/// O HABITANTE LIVRE MAIS PERTO ao alcance de conversa -- so ocioso ou passeando, vivo e de pe.
	/// E a unica varredura da rotina, e ela so acontece no tique da decisao.
	/// </summary>
	private ServerPlayer? HabitanteLivrePerto(ServerPlayer npc)
	{
		float raio = _rotina.RaioDeConversaTiles * ZoneCollision.TileSize;
		float perto = raio * raio;
		ServerPlayer? melhor = null;
		foreach (ServerPlayer o in ZoneList(npc.Zone.Hash))
		{
			if (o.Id == npc.Id || !EhNpcDoMundo(o) || o.Papel is not { Pacifico: true }) continue;
			if (o.Ficha.dead || o.Ficha.KO || o.DonoDoClone != 0) continue;
			if (o.Rotina is not { Afazer: Afazer.Ocioso or Afazer.Passeando }) continue;
			// QUEM TEM PRESA NAO ESTA LIVRE: o habitante provocado por um jogador esta "ocioso" so na
			// rotina (ela foi interrompida); o corpo esta lutando. Chama-lo pra conversa o faria aceitar
			// e ser interrompido de novo no tique seguinte, num vaivem que a bancada viu.
			if (PresaDoNpc(o) != null) continue;
			float d2 = (o.Pos - npc.Pos).LengthSquared;
			if (d2 >= perto) continue;
			perto = d2;
			melhor = o;
		}
		return melhor;
	}

	private ServerPlayer? CorpoNaZona(ServerPlayer npc, int id) =>
		_players.TryGetValue(id, out ServerPlayer? o) && o.Zone.Hash == npc.Zone.Hash ? o : null;

	/// <summary>Poder de quem convida sobre o do convidado -- a "inteligencia minima" da recusa.</summary>
	private static double RazaoDePoder(ServerPlayer convida, ServerPlayer convidado) =>
		Finito(convida.Ficha.expressedBP) / Math.Max(1, Finito(convidado.Ficha.expressedBP));
}
