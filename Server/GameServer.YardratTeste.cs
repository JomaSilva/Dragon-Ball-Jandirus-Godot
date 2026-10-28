using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--yardratteste` (2026-10-27) -- o pedido 109 do dono: *"escolhe um planeta em um sistema proximo de namek pra ser yadrat, vai ter
/// npcs dessa raça andando por la so q é um planeta "secreto" n aparece marcando no nav system"*, com o NPC que ensina o Instant
/// Transmission. Roda no BOOT, pela semente do universo de verdade:
///   Y1  o Yardrat e um planeta do SISTEMA DE NAMEK (o ancorado), na orbita vizinha, gerado (nao pre-feito) -- e os outros planetas do
///       sistema continuam com os nomes deles;
///   Y2  a CARTA nao o mostra (`NaCarta` falso so nele) -- e o espaco o mostra: a vizinhanca do servidor (`Espaco.PorPerto`) tem um planeta
///       chamado Yardrat, e e nele que se pousa;
///   Y3  o chao NASCE POVOADO: o mestre (Pybara) e os dez habitantes, todos Yardrat -- e o chao que ja tem povo nao ganha outro;
///   Y4  o MESTRE pela tecla E: ao alcance ele e o corpo apontado, com o menu dele; "aprender" ensina o Instant Transmission; a segunda vez,
///       "ja sabe";
///   Y5  o mestre CAIDO nao ensina.
///
///     Godot --headless --path . --server --port 11791 --yardratteste
/// </summary>
public sealed partial class GameServer
{
	private int _yrOk, _yrFalhou;

	private void AfirmarYr(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _yrOk++; GD.Print($"[yardrat]   OK    {oque}   [{detalhe}]"); return; }
		_yrFalhou++;
		GD.PrintErr($"[yardrat]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDoYardrat()
	{
		_yrOk = _yrFalhou = 0;
		GD.Print("[yardrat] ================ O PLANETA YARDRAT, O SECRETO (dono, 2026-10-27) ================");

		// ---- Y1) o planeta no sistema de Namek
		PlanetaNoEspaco namek = Espaco.PreFeitos().First(p => p.Nome == Yardrat.Vizinho);
		SistemaSolar? sis = Sistemas.Em(SeedDoUniverso, namek.Pos);
		int k = sis?.OrbitaDoYardrat ?? -1;
		PlanetaNoEspaco? yardrat = sis is { } s0 && k >= 0 ? s0.Planeta(k) : null;
		var outros = new List<string>();
		if (sis is { } s1)
			for (int i = 0; i < s1.Orbitas; i++)
				if (i != k && i != s1.OrbitaPreFeita) outros.Add(s1.Planeta(i).Nome);
		double dias = yardrat is { } y0 ? Espaco.DiasInGame((y0.Pos - namek.Pos).Length) : double.MaxValue;
		AfirmarYr("Y1. o YARDRAT e um planeta gerado do sistema de Namek, na orbita vizinha -- e os outros do sistema seguem com os nomes deles",
				  sis is { Ancorado: true } && yardrat is { Nome: Yardrat.Nome, Premade: false } && Math.Abs(k - sis.Value.OrbitaPreFeita) == 1
				  && outros.Count > 0 && outros.All(n => n != Yardrat.Nome) && dias < 3,
				  $"orbita {k} (Namek na {sis?.OrbitaPreFeita}); {dias:0.##} dias de Namek; outros: {string.Join(", ", outros)}");

		// ---- Y2) fora da carta, dentro do espaco
		bool naCarta = sis is { } s2 && k >= 0 && s2.NaCarta(k);
		bool outrosNaCarta = sis is { } s3 && Enumerable.Range(0, s3.Orbitas).Where(i => i != k).All(i => s3.NaCarta(i));
		bool noEspaco = yardrat is { } y1 && Espaco.PorPerto(SeedDoUniverso, y1.Chunk).Any(p => p.Nome == Yardrat.Nome && p.Pos.Equals(y1.Pos));
		AfirmarYr("Y2. a CARTA nao mostra o Yardrat (so ele fica fora) -- e o ESPACO mostra: a vizinhanca do servidor tem o planeta Yardrat",
				  !naCarta && outrosNaCarta && noEspaco, $"na carta {naCarta}; os outros na carta {outrosNaCarta}; no espaco {noEspaco}");

		if (yardrat is not { } planeta)
		{
			GD.Print($"[yardrat] ================ {_yrOk} passaram, {_yrFalhou} falharam ================");
			return;
		}

		// ---- Y3) o povo
		ZoneKey zona = ZoneKey.Procedural(planeta.Nome, planeta.Seed);
		var forjados = new List<ServerPlayer>();
		try
		{
			PovoarOYardrat(zona);
			List<ServerPlayer> povo = [.. ZoneList(zona.Hash).Where(p => p.Peer == null && p.Papel != null)];
			ServerPlayer? mestre = povo.FirstOrDefault(EhOMestreYardrat);
			int habitantes = povo.Count(p => p.Papel?.Molde.Id == Yardrat.MoldeDoPovo);
			PovoarOYardrat(zona);
			int depoisDeNovo = ZoneList(zona.Hash).Count(p => p.Peer == null && p.Papel != null);
			// (2026-10-27, o pedido 131) o mestre tem o nome so dele ("Mestre Pybara"): nenhum habitante se chama como ele
			AfirmarYr("Y3. o chao NASCE POVOADO: o mestre (Mestre Pybara, o unico com esse nome) e dez habitantes, todos Yardrat -- e o chao que ja tem povo nao ganha outro",
					  mestre is { Name: "Mestre Pybara", Race: "Yardrat" } && povo.Count(p => p.Name.Contains("Pybara", StringComparison.Ordinal)) == 1
					  && habitantes == Yardrat.Habitantes && povo.All(p => p.Race == "Yardrat") && depoisDeNovo == povo.Count,
					  $"mestre {mestre?.Name ?? "nenhum"} ({mestre?.Race}); {habitantes} habitante(s); racas {string.Join(",", povo.Select(p => p.Race).Distinct())}; "
					  + $"nomes {string.Join(",", povo.Select(p => p.Name))}; de novo {depoisDeNovo}");

			// ---- Y3b) (2026-10-27, o pedido 130) o CORPO de Yardrat: a lista da raca tem o Yardrat e mais seis corpos humanos, e o sorteio do NPC
			// caia quase sempre num humano -- o dono: "n esta spawnando npcs com icone de yardrat". Cada corpo, a folha que o cliente desenha.
			string Folha(ServerPlayer p)
			{
				List<string> lista = _visual?.CorposDe(p.Race).Para(p.Genero) ?? [];
				return p.Visual.Corpo >= 0 && p.Visual.Corpo < lista.Count ? System.IO.Path.GetFileNameWithoutExtension(lista[p.Visual.Corpo]) : "?";
			}
			List<string> folhas = [.. povo.Select(Folha)];
			AfirmarYr("Y3b. (o pedido 130) o mestre e os habitantes nascem com o CORPO de Yardrat (a folha 'Yardrat'), e nao com um corpo humano da lista da raca",
					  povo.Count > 0 && folhas.All(f => f == "Yardrat"),
					  string.Join(", ", povo.Select(p => $"{p.Name}: {Folha(p)}")));

			// ---- Y4) a tecla E do mestre
			if (mestre != null)
			{
				var aluno = new ServerPlayer
				{
					Id = 94_400, Peer = null, Name = "bancada: o visitante", Race = "Human", Genero = "Male", Idade = 25, Zone = zona,
					Pos = mestre.Pos + new Vec2(ZoneCollision.TileSize, 0), Conta = "bancada_yardrat", Slot = 0,
					Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = 10_000 }, Livro = new Jandirus.Core.Skills.SkillBook(),
				};
				aluno.Ficha.Class = "Normal";
				PorNoMundo(aluno);
				forjados.Add(aluno);
				var (corpo, tipo, _) = MestreYardratAoAlcance(aluno);
				bool sabiaAntes = aluno.Livro.Sabe(Yardrat.SkillDoMestre);
				List<string> primeira = Ouvir(() => AprenderComOMestreYardrat(aluno));
				bool sabeDepois = aluno.Livro.Sabe(Yardrat.SkillDoMestre);
				List<string> segunda = Ouvir(() => AprenderComOMestreYardrat(aluno));
				bool temOMenu = Jandirus.Core.Tech.Interacoes.De(Yardrat.Tipo).Any(a => a.Verbo == Yardrat.VerboAprender);
				AfirmarYr("Y4. a tecla E aponta o MESTRE (com o menu dele); \"aprender\" ensina o Instant Transmission; a segunda vez, \"ja sabe\"",
						  corpo == mestre.Id && tipo == Yardrat.Tipo && temOMenu && !sabiaAntes && sabeDepois
						  && primeira.Any(s => s.Contains("te ensina o Instant Transmission")) && segunda.Any(s => s.Contains("já sabe")),
						  $"apontado {corpo} ({tipo}); menu {temOMenu}; sabia {sabiaAntes} -> {sabeDepois}; {string.Join(" | ", primeira)} || {string.Join(" | ", segunda)}");

				// ---- Y5) o mestre caido
				aluno.Livro.Esquecer(Yardrat.SkillDoMestre);
				mestre.Combate.Nocautear(30);
				List<string> caido = Ouvir(() => AprenderComOMestreYardrat(aluno));
				mestre.Combate.Levantar();
				AfirmarYr("Y5. o mestre CAIDO nao ensina", !aluno.Livro.Sabe(Yardrat.SkillDoMestre) && caido.Any(s => s.Contains("de pé")),
						  string.Join(" | ", caido));
			}
		}
		finally
		{
			foreach (ServerPlayer p in ZoneList(zona.Hash).ToList())
			{
				_players.Remove(p.Id);
				ZoneList(zona.Hash).Remove(p);
			}
		}
		GD.Print($"[yardrat] ================ {_yrOk} passaram, {_yrFalhou} falharam ================");
	}
}
