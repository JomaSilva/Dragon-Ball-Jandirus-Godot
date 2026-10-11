using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Appearance;
using Jandirus.Core.Items;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DOS ANIMAIS (`--animaisteste`) -- o pedido do dono em 2026-10-10: *"adicione animais npcs (como
/// lobos etc) ... a forca deles e fixa (normalmente em torno de 10 de bp a 100 de bp), eles n teleportam nem
/// nada, sao animais comuns q as vezes batem um pouco forte (100 de bp). ao matar eles vc pode lootear eles e
/// pegar carne ... ao qual vc pode comer e recuperar bastante nutricao quando n tem frutas etc."*
///
/// No PRIMEIRO LOGIN, e hospedando, pelo motivo da bancada dos viloes: bicho so existe em planeta COM JOGADOR,
/// e jogador e corpo com `Peer` -- o do host. Os outros "jogadores" (o cacador, o novato, quem esta no Mirante)
/// sao corpos forjados com o `Peer` do host emprestado, devolvido antes de sairem.
///
/// TUDO PELO CODIGO DE PRODUCAO: a fauna nasce pelo `TickDosAnimais`, os bichos pensam pelo
/// `TickDosCorposSemDono`, apanham pelo `Atacar`, morrem pelo `TickCombate`, e a carne sai pelo verbo
/// `saquear` e entra pelo `item_comer` -- as mesmas portas do `C2S.Verbo`.
///
///   1. O DADO          os moldes, a forca fixa, a arte, a carne no catalogo
///   2. ONDE HA FAUNA   planeta sim; Mirante, alem, interior e espaco nao; proporcional ao tamanho
///   3. NASCER          em volta de quem esta la, fora da tela, em chao de verdade, longe de predio
///   4. SUMIR           planeta sem ninguem, e o bicho que ficou longe -- sem mexer no estoque
///   5. PLANETA GERADO  a fauna de um mundo sorteado
///   5b. O CATALOGO     Namek, Icer e Arlia (onde a unica comida e a caca) tem; Makyo Star nao tem nada
///   6. A FORCA FIXA    o BP volta ao do molde
///   7. O MANSO         nao ataca; apanhou, foge
///   8. O BRAVO         ataca quem chega perto, pelas pernas (corre, nao investe); nao voa, nao usa ki;
///                      larga na borda do territorio e da tregua a quem derrubou
///   9. A CACA          cai e morre, a carcaca, a carne, o saque; sem karma nem reputacao; a pata nao cai
///  10. A CARNE         alimenta mais que a maca, pela mesma porta
///  11. SEM ABUSO       nada de Zenkai, de treino apanhando nem de bonus de luta
///
/// ONZE DEFEITOS INJETADOS (`Animais.*DeTeste`, cada um lido pela propria linha da regra) e os contra-exemplos
/// de dado e de gente (o molde invalido, o cidadao morto pelo mesmo caminho, o corpo de gente destrocado, o
/// braco de gente arrancado).
///
///     Godot --headless --path . --host --rede 8160 --animaisteste --raca Human --conta bancada_animais --nome Cacador
/// </summary>
public sealed partial class GameServer
{
	private bool _animaisDeTeste;

	private void RodarBancadaDosAnimais(ServerPlayer pl)
	{
		GD.Print("\n[animais] ================ OS ANIMAIS (dono, 2026-10-10) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[animais]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
			else { falhou++; GD.PrintErr($"[animais]   FALHA {nome}   [{detalhe}]"); }
		}

		const float T = ZoneCollision.TileSize;
		ZoneKey terra = pl.Zone;
		long inicio = NowMs();

		// O HOST AINDA NAO ESTA NA LISTA DA ZONA (a bancada roda dentro do `Entrar`): a fauna aparece EM VOLTA
		// de quem esta no planeta, e "quem esta" se le da lista. Poe, mede, e tira no fim.
		List<ServerPlayer> daZona = ZoneList(terra.Hash);
		bool entrouPelaBancada = !daZona.Contains(pl);
		if (entrouPelaBancada) daZona.Add(pl);

		var forjados = new List<ServerPlayer>();
		bool ligadoDeAntes = Animais.Ligado;
		double carenciaDoHost = pl.Combate.Carencia;
		double? sementeDeAntes = SorteioDeSementeDeTeste;
		long relogioBase = AdiantoDoRelogioDeTeste;
		double andado = 0;
		int tiquesDados = 0;

		// ---------------------------------------------------------------- as ferramentas
		// O RELOGIO DE PAREDE ANDA COM A BANCADA (rancor, fuga, prazo da carcaca e reposicao sao em ms).
		void AndarORelogio(double ms)
		{
			andado += ms;
			AdiantoDoRelogioDeTeste = relogioBase + (long)andado;
		}
		// UM SEGUNDO DE FAUNA, e so de fauna: ninguem pensa nem anda (as familias de nascer e sumir).
		void Segundos(int n)
		{
			for (int i = 0; i < n; i++) { AndarORelogio(1000); TickDosAnimais(); }
		}
		// O TIQUE DO SERVIDOR, na ordem dele: grades, combate, arremesso, mentes -- e a fauna a 1 Hz.
		void Tiques(int n, Func<bool>? ateQue = null)
		{
			for (int i = 0; i < n; i++)
			{
				AndarORelogio(Protocol.TickSeconds * 1000.0);
				MontarAsGrades();
				TickCombate(Protocol.TickSeconds);
				TickDoEmpurrao();
				TickDosCorposSemDono(Protocol.TickSeconds);
				if (++tiquesDados % 30 == 0) TickDosAnimais();
				if (ateQue != null && ateQue()) return;
			}
		}
		ServerPlayer Gente(string nome, string raca, ZoneKey zona, Vec2 onde, double bp)
		{
			var c = new ServerPlayer
			{
				Id = _nextId++, Peer = pl.Peer, Name = nome, Race = raca, Genero = "Male", Idade = 25,
				Zone = zona, Pos = onde, LastInputMs = NowMs(), Conta = "bancada_animais_" + nome.ToLowerInvariant(), Slot = 0,
				Ficha = new Fighter { Name = nome, Race = raca, BP = bp, Idade = 25, maxstamina = 100, stamina = 100 },
				Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			c.Ficha.Class = "Normal";
			PorNoMundo(c);
			c.Ficha.Ki = c.Ficha.MaxKi;
			c.Ficha.Tick(agoraMs: NowMs());
			forjados.Add(c);
			return c;
		}
		void Descartar(params ServerPlayer?[] corpos)
		{
			foreach (ServerPlayer? c in corpos)
			{
				if (c == null) continue;
				// O `Peer` EMPRESTADO VOLTA ANTES DE O CORPO SAIR: `RemoverNpc` recusa corpo com dono.
				if (forjados.Remove(c)) c.Peer = null;
				_animais.Remove(c.Id);
				if (_players.ContainsKey(c.Id)) RemoverNpc(c);
			}
		}
		void LimparFauna()
		{
			foreach (int id in _animais.Keys.ToList())
				if (_players.TryGetValue(id, out ServerPlayer? n)) RemoverNpc(n);
			_animais.Clear();
			foreach ((ServerPlayer corpo, long _) in _carcacas.Values.ToList()) DesfazerOCadaver(corpo);
		}
		List<ServerPlayer> BichosDe(ZoneKey z) =>
			[.. _animais.Values.Where(a => a.Zona == z.Hash).Select(a => _players.GetValueOrDefault(a.Id)).Where(n => n != null).Select(n => n!)];
		(int X, int Y) Celula(Vec2 p) => ((int)MathF.Floor(p.X / T), (int)MathF.Floor(p.Y / T));
		float Tiles(Vec2 a, Vec2 b) => (a - b).Length / T;
		void Refazer(ServerPlayer c, double bp)
		{
			c.Ficha.BP = bp;
			c.Combate.Curar();
			c.Ficha.KO = false;
			c.Ficha.Ki = c.Ficha.MaxKi;
			c.Ficha.stamina = c.Ficha.maxstamina;
			c.Ficha.Tick(agoraMs: NowMs());
			c.RancorAte = 0;
			c.UltimoAgressor = 0;
			c.TiquesDeVoo = 0;
		}
		// O GOLPE DO JOGADOR, pela funcao do `C2S.Action`: colado no alvo, marcado, ate ele cair (ou o teto).
		int BaterAteCair(ServerPlayer quem, ServerPlayer alvo, int teto = 300)
		{
			int golpes = 0;
			while (!alvo.Ficha.KO && !alvo.Ficha.dead && golpes < teto)
			{
				quem.Pos = alvo.Pos + new Vec2(-30, 0);
				quem.Facing = Facing.East;
				quem.AlvoId = alvo.Id;
				quem.Combate.Recarga = 0;
				quem.Combate.Stun = 0;
				quem.Ficha.stamina = quem.Ficha.maxstamina;
				Atacar(quem, Protocol.Golpe.Leve);
				golpes++;
			}
			return golpes;
		}
		// A BANCADA MUDA UM BICHO DE LUGAR: a toca vai junto (o pasto e o territorio se medem dela), e o que ele
		// estava fazendo acaba -- como se tivesse nascido ali.
		void Mudar(ServerPlayer bicho, Vec2 onde)
		{
			bicho.Pos = onde;
			bicho.PresaEngajada = 0;
			bicho.RancorAte = 0;
			bicho.UltimoAgressor = 0;
			if (!_animais.TryGetValue(bicho.Id, out AnimalVivo? a)) return;
			a.Casa = onde;
			a.Andando = false;
			a.ProximoPassoMs = 0;
			a.DeVoltaAte = 0;
		}
		ServerPlayer? CarcacaDe(ServerPlayer morto) =>
			ZoneList(morto.Zone.Hash).FirstOrDefault(c => c.ECadaver && c.NomeDeQuemMorreu == morto.Name
														  && (c.Pos - morto.Pos).Length < 4f && c.CaiuEm >= inicio);

		// A ULTIMA LISTA DE SAQUE QUE O SERVIDOR MONTOU -- o conteudo do `S2C.Saque`.
		(int Ladrao, int Caido, List<Pilha> Leva)? lista = null;
		Action<int, int, List<Pilha>>? escutaDeAntes = EscutaDeSaque;
		EscutaDeSaque = (ladrao, caido, leva) => lista = (ladrao, caido, [.. leva]);

		try
		{
			// O HOST E SO PLATEIA: intocavel ate o fim (um lobo de passagem nao pode derruba-lo no meio da medida).
			pl.Combate.Carencia = 1e6;
			Animais.Ligado = true;
			SorteioDeAnimalDeTeste = null;
			LimparFauna();
			_fauna.Clear();
			_especies = null;

			// ================================================================ 1. o dado
			GD.Print("[animais] -- 1) o dado: os moldes, a forca fixa, a arte e a carne --");
			List<MoldeDeNpc> especies = Especies();
			Checa("(montagem) o `npcs.json` traz moldes do tipo proprio (`animal`)", especies.Count >= 4,
				  string.Join(", ", especies.Select(m => $"{m.Nome} {m.BpMax:0}")));
			if (especies.Count == 0) return;

			List<string> problemas = [.. especies.SelectMany(m => m.Problemas())];
			Checa("nenhum molde de bicho tem problema de dado (folha, forca, carne, peso)", problemas.Count == 0, string.Join(" | ", problemas));
			Checa("**A FORCA E FIXA POR ESPECIE**: `bpMin` = `bpMax`, e nenhum acompanha os jogadores (`bpRelativo` zero)",
				  especies.All(m => m.BpMin == m.BpMax && m.BpRelativo <= 0));
			Checa("...na faixa do pedido: de 10 a 100 de BP",
				  especies.All(m => m.BpMax is >= 10 and <= 100), $"{especies.Min(m => m.BpMax):0} a {especies.Max(m => m.BpMax):0}");
			Checa("ha MANSOS (nao atacam) e ha BRAVOS (atacam quem chega perto)",
				  especies.Any(m => m.Animal!.Bravo) && especies.Any(m => !m.Animal!.Bravo),
				  $"{especies.Count(m => !m.Animal!.Bravo)} mansos, {especies.Count(m => m.Animal!.Bravo)} bravos");

			List<string> semArte = [.. especies.Select(m => Animais.CaminhoDaFolha(Animais.IndiceDaFolha(m.Animal!.Folha)))
											   .Where(c => !ResourceLoader.Exists(c))];
			ItemDef? carneDef = CatalogoDeItens.Get(CatalogoDeItens.Carne);
			Checa("A ARTE JA EXISTE (importada): a folha de cada especie", semArte.Count == 0, string.Join(", ", semArte));
			Checa("A CARNE esta no catalogo de itens: empilha, se come, e a arte dela existe",
				  carneDef is { Empilhavel: true } && Array.IndexOf(carneDef.AcoesDoItem, "comer") >= 0 && ResourceLoader.Exists(carneDef.Arte),
				  carneDef?.Arte ?? "sem ficha");

			var comRelativo = new MoldeDeNpc { Id = "x", Tipo = TipoDeNpc.Animal, TipoDeclarado = true, BpMin = 10, BpMax = 10, BpRelativo = 1, Animal = new FichaDeAnimal { Folha = "Wolf" } };
			var comFaixa = new MoldeDeNpc { Id = "x", Tipo = TipoDeNpc.Animal, TipoDeclarado = true, BpMin = 10, BpMax = 90, Animal = new FichaDeAnimal { Folha = "Wolf" } };
			var semFolha = new MoldeDeNpc { Id = "x", Tipo = TipoDeNpc.Animal, TipoDeclarado = true, BpMin = 10, BpMax = 10, Animal = new FichaDeAnimal { Folha = "Unicornio" } };
			var semBloco = new MoldeDeNpc { Id = "x", Tipo = TipoDeNpc.Animal, TipoDeclarado = true, BpMin = 10, BpMax = 10 };
			Checa("(contra-exemplo) o boot RECUSA bicho que acompanha os jogadores, bicho com faixa de BP, folha que nao existe e molde sem o bloco `animal`",
				  comRelativo.Problemas().Count > 0 && comFaixa.Problemas().Count > 0 && semFolha.Problemas().Count > 0 && semBloco.Problemas().Count > 0,
				  $"{comRelativo.Problemas().Count}/{comFaixa.Problemas().Count}/{semFolha.Problemas().Count}/{semBloco.Problemas().Count} problema(s)");

			Checa("O INIMIGO COMUM CONTINUA DESLIGADO, e o bicho nasce: sao tipos diferentes",
				  !Povoamento.InimigoComumLigado && !Povoamento.PodeNascer(TipoDeNpc.Inimigo) && Povoamento.PodeNascer(TipoDeNpc.Animal));
			Checa("...e bicho nao entra no plano de povoamento (ele nasce pela fauna)",
				  Povoamento.Problemas([new LinhaDePovoamento { Planeta = "Earth", Molde = especies[0].Id, Quantos = 1 }], _moldes!).Count > 0);

			int[] vezes = new int[especies.Count];
			for (int i = 0; i < 10_000; i++) vezes[especies.IndexOf(Animais.Escolher(especies, (i + 0.5) / 10_000)!)]++;
			int maisPesada = Enumerable.Range(0, especies.Count).MaxBy(i => especies[i].Animal!.Peso);
			int maisLeve = Enumerable.Range(0, especies.Count).MinBy(i => especies[i].Animal!.Peso);
			Checa("a especie sai PELO PESO: toda especie aparece, e a mais pesada aparece mais que a mais leve",
				  vezes.All(v => v > 0) && vezes[maisPesada] > vezes[maisLeve],
				  string.Join(", ", especies.Select((m, i) => $"{m.Nome} {vezes[i] / 100.0:0.#}%")));

			// ================================================================ 2. onde ha fauna
			GD.Print("[animais] -- 2) que chao tem fauna --");
			ZoneCollision? mapa = MapaDaZonaOuCatalogo(terra);
			Checa("(montagem) o host esta num planeta com mapa", RecebeFauna(terra) && mapa != null, terra.Name);
			if (mapa == null) return;

			ZoneKey mirante = ZoneKey.Premade("Lookout"), alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
			Checa("**NUNCA no Mirante, no alem, na Sala do Tempo, em interior nem no espaco**",
				  !RecebeFauna(mirante) && !RecebeFauna(alem) && !RecebeFauna(ZoneKey.Premade(Alem.ZonaDoCeu))
				  && !RecebeFauna(ZoneKey.Premade(Alem.ZonaDoInferno)) && !RecebeFauna(ZoneKey.Premade("Hyperbolic_Time_Chamber"))
				  && !RecebeFauna(ZoneKey.Interior("Interdimension", (ulong)pl.Id)) && !RecebeFauna(Espaco.Zona(SeedDoUniverso)));
			// dono, 2026-10-10: "esses planetas so vao ter animais, makyo star e o unico q n tem nada"
			string[] soTemCaca = ["Namek", "Icer", "Arlia"];
			Checa("**MAKYO STAR E O UNICO PLANETA DA CARTA SEM FAUNA** (a lista `Animais.PlanetasSemFauna`); Namek, Icer e Arlia -- onde a unica comida e a caca -- nao estao nela",
				  Espaco.EhPlaneta(ZoneKey.Premade("Makyo_Star")) && !RecebeFauna(ZoneKey.Premade("Makyo_Star"))
				  && Espaco.PreFeitos().Count(p => !RecebeFauna(Espaco.ZonaDe(p))) == 1
				  && soTemCaca.All(n => RecebeFauna(ZoneKey.Premade(n)) && Animais.PlanetaTemFauna(n)),
				  "sem fauna: " + string.Join(", ", Espaco.PreFeitos().Where(p => !RecebeFauna(Espaco.ZonaDe(p))).Select(p => p.Nome)));
			Checa("planeta do catalogo e planeta GERADO tem",
				  RecebeFauna(ZoneKey.Premade("Namek")) && RecebeFauna(ZoneKey.Premade("Vegeta")) && RecebeFauna(ZoneKey.Procedural("Verdejante-1", 0xABCDEF)));
			int pequeno = Animais.AlvoDoPlaneta(192, 192), medio = Animais.AlvoDoPlaneta(320, 320), grande = Animais.AlvoDoPlaneta(1000, 1000);
			Checa("**PROPORCIONAL AO TAMANHO**, com piso e teto: 192 tiles < 320 tiles < 1000 tiles",
				  pequeno == Animais.MinPorPlaneta && grande == Animais.MaxPorPlaneta && pequeno < medio && medio < grande,
				  $"{pequeno} / {medio} / {grande}");

			// ================================================================ 3. nascer
			GD.Print("[animais] -- 3) a fauna aparece em volta de quem esta no planeta --");
			int alvo = Animais.AlvoDoPlaneta(mapa.Width, mapa.Height);
			Vec2 berco = PontoDeNascimento(terra);

			Segundos(1);
			Checa("UM POR SEGUNDO: a fauna nao nasce inteira num tique", AnimaisDeTeste(terra).NaZona <= 1, $"{AnimaisDeTeste(terra).NaZona}");
			int levou = 1;
			while (AnimaisDeTeste(terra).NaZona < alvo && levou < 180) { Segundos(1); levou++; }
			List<ServerPlayer> bichos = BichosDe(terra);
			Checa("**COM GENTE NO PLANETA A FAUNA APARECE**, ate o tamanho dele", bichos.Count == alvo, $"{bichos.Count} de {alvo} em {levou} s");
			if (bichos.Count == 0) return;

			Checa("...cada um e um corpo SEM DONO, do tipo proprio, naquele planeta",
				  bichos.All(b => b.Papel is { Tipo: TipoDeNpc.Animal } && b.Peer == null && b.Zone.Equals(terra) && EhNpcDoMundo(b)));
			Checa("**EM CHAO DE VERDADE**: fora de parede, de agua e da beirada do mapa",
				  bichos.All(b => mapa.ServeDeChao(Celula(b.Pos).X, Celula(b.Pos).Y)));
			Checa("**LONGE DE PREDIO E DE BASE**: nada sob teto nem parede erguida no raio",
				  bichos.All(b => !Animais.PertoDeConstrucao(mapa, Celula(b.Pos).X, Celula(b.Pos).Y)), $"raio {Animais.TilesLongeDeConstrucao} tiles");
			Checa("...e com chao pra andar (nao nasce em patio murado nem em ilhota)",
				  bichos.All(b => Animais.TemChaoLivre(mapa, Celula(b.Pos).X, Celula(b.Pos).Y)));
			Checa("**FORA DA TELA de quem esta la, e perto o bastante pra ser achado**",
				  bichos.All(b => Tiles(b.Pos, pl.Pos) >= Animais.DistMinDeGenteTiles - 1 && Tiles(b.Pos, pl.Pos) <= Animais.DistMaxDeGenteTiles + 1),
				  $"de {bichos.Min(b => Tiles(b.Pos, pl.Pos)):0} a {bichos.Max(b => Tiles(b.Pos, pl.Pos)):0} tiles do host");
			Checa("...e longe do ponto de chegada do planeta",
				  bichos.All(b => Tiles(b.Pos, berco) >= Animais.TilesLongeDoBerco - 1), $"o mais perto a {bichos.Min(b => Tiles(b.Pos, berco)):0} tiles");
			Checa("**A FORCA DE CADA UM E A DO MOLDE**, e o poder expresso bate com ela",
				  bichos.All(b => b.Ficha.BP == Animais.BpDe(b.Papel!.Molde) && b.Ficha.expressedBP / b.Ficha.BP is > 0.9 and < 1.3),
				  string.Join(", ", bichos.Select(b => $"{b.Name} {b.Ficha.BP:0}/{b.Ficha.expressedBP:0}")));
			Checa("nenhum sabe skill, voa ou usa ki; nenhum fala e nenhum investe",
				  bichos.All(b => b.Livro.Aprendidas.Count == 0 && b.Perfil == PerfilDeCombate.SoCorpo && !PodeVoar(b)
								  && b.Cerebro is { ChanceDeInvestir: 0, AlcanceDaInvestida: 0 } c && !c.Boca.Ligado));
			Checa("o passo de todos e o de bicho (pouco mais da metade do passo de gente), e nao o que a ficha daria",
				  bichos.All(b => b.SpeedStat == Animais.VelocidadeDoPasso), $"x{Animais.VelocidadeDoPasso}");
			Checa("**BICHO SO DERRUBA**: o golpe de todos e o nao-letal, e a mordida nao arremessa ninguem (o voo contra a parede mataria por fora do golpe)",
				  bichos.All(b => !b.Combate.Letal && !b.Knockback));
			Checa("o corpo e a FOLHA DA ESPECIE, sem cabelo, olhos nem roupa por cima",
				  _visual != null && bichos.All(b => _visual.CorpoSprite(b.Visual, b.Race, b.Genero)
														 == Animais.CaminhoDaFolha(Animais.IndiceDaFolha(b.Papel!.Molde.Animal!.Folha))
													 && !VisualCatalog.TemCabelo(b.Race) && !VisualCatalog.CorpoTemRosto(b.Visual, b.Race)
													 && b.Visual.Roupa.Count == 0));

			Segundos(10);
			Checa("FAUNA CHEIA: mais dez segundos e nao nasce mais nenhum (o teto do planeta)",
				  AnimaisDeTeste(terra).NaZona == alvo && Math.Abs(AnimaisDeTeste(terra).Estoque - alvo) < 1e-6, $"{AnimaisDeTeste(terra)}");

			Animais.QualquerLugarDeTeste = true;
			try
			{
				int ruins = 0, sorteados = 0;
				for (int i = 0; i < 80; i++)
				{
					if (LugarDeAnimal(terra, mapa) is not { } p) continue;
					sorteados++;
					Animais.QualquerLugarDeTeste = false;
					bool serve = Animais.LugarServe(mapa, Celula(p).X, Celula(p).Y) && Tiles(p, berco) >= Animais.TilesLongeDoBerco;
					Animais.QualquerLugarDeTeste = true;
					if (!serve) ruins++;
				}
				Checa("(defeito injetado: o lugar nao e conferido) em 80 sorteios sai bicho em parede, agua, colado em predio ou no ponto de chegada -- a regua sabe reprovar",
					  ruins > 0, $"{ruins} de {sorteados} lugares ruins");
			}
			finally { Animais.QualquerLugarDeTeste = false; }

			// ================================================================ 4. sumir
			GD.Print("[animais] -- 4) planeta sem ninguem, o Mirante, e o bicho que ficou longe --");
			ServerPlayer noMirante = Gente("Mirante", "Human", mirante, PontoDeNascimento(mirante), 100);
			ServerPlayer noAlem = Gente("Alem", "Human", alem, PontoDeNascimento(alem), 100);
			Segundos(15);
			Checa("**COM GENTE NO MIRANTE E NO ALEM, NADA NASCE LA**",
				  AnimaisDeTeste(mirante).NaZona == 0 && AnimaisDeTeste(alem).NaZona == 0
				  && !ZoneList(mirante.Hash).Any(EhAnimal) && !ZoneList(alem.Hash).Any(EhAnimal));
			Descartar(noMirante, noAlem);
			Checa("planeta SEM NINGUEM nao tem bicho", AnimaisDeTeste(ZoneKey.Premade("Vegeta")).NaZona == 0 && AnimaisDeTeste(ZoneKey.Premade("Namek")).NaZona == 0);

			// um bicho que ficou longe de todo mundo. A CHAVE GERAL fica desligada enquanto isso (`Animais.Ligado`, a
			// que a linha de comando `--semfauna` desliga): com ela ligada a vaga que ele deixa seria preenchida no
			// mesmo segundo, e nao daria pra ver nem a vaga nem a chave.
			ServerPlayer longe = bichos[0];
			longe.Pos = pl.Pos + new Vec2((Animais.DistQueSomeTiles + 20) * T, 0);
			Animais.Ligado = false;
			try
			{
				Segundos(2);
				Checa("longe de todo mundo ele ainda espera um pouco...", _players.ContainsKey(longe.Id));
				Segundos((int)Animais.SegundosAteSumir + 1);
				Checa("...e depois VOLTA PRO ESTOQUE: some sem morrer (sem carcaca, sem perder do estoque)",
					  !_players.ContainsKey(longe.Id) && _carcacas.Count == 0 && Math.Abs(AnimaisDeTeste(terra).Estoque - alvo) < 1e-6,
					  $"{AnimaisDeTeste(terra)}");
				Segundos(10);
				Checa("COM A FAUNA DESLIGADA (`--semfauna`) nenhum bicho nasce: dez segundos com a vaga aberta, e os outros continuam la",
					  AnimaisDeTeste(terra).NaZona == alvo - 1, $"{AnimaisDeTeste(terra).NaZona} de {alvo}");
			}
			finally { Animais.Ligado = true; }
			levou = 0;
			while (AnimaisDeTeste(terra).NaZona < alvo && levou < 60) { Segundos(1); levou++; }
			Checa("...religada, a vaga e preenchida no anel de alguem", AnimaisDeTeste(terra).NaZona == alvo && levou >= 1, $"em {levou} s");

			// o ultimo jogador sai do planeta (o host "vai pro Mirante"): a lista da zona e a zona dele, como no `MoveToZone`
			daZona.Remove(pl);
			pl.Zone = mirante;
			Segundos(1);
			bool saiuTudo = AnimaisDeTeste(terra).NaZona == 0 && !ZoneList(terra.Hash).Any(EhAnimal);
			double estoqueVazio = AnimaisDeTeste(terra).Estoque;
			pl.Zone = terra;
			daZona.Add(pl);
			Checa("**O ULTIMO JOGADOR SAIU: os bichos saem do planeta**, e o estoque dele fica como estava",
				  saiuTudo && Math.Abs(estoqueVazio - alvo) < 1e-6, $"estoque {estoqueVazio:0.##}");
			levou = 0;
			while (AnimaisDeTeste(terra).NaZona < alvo && levou < 180) { Segundos(1); levou++; }
			Checa("...voltou alguem: eles reaparecem, ate o MESMO estoque", AnimaisDeTeste(terra).NaZona == alvo, $"em {levou} s");

			// ================================================================ 5. planeta gerado
			GD.Print("[animais] -- 5) a fauna de um planeta gerado --");
			PlanetaNoEspaco? sorteado = null;
			for (int sy = -12; sy <= 12 && sorteado == null; sy++)
				for (int sx = -12; sx <= 12 && sorteado == null; sx++)
				{
					if (Sistemas.Do(SeedDoUniverso, sx, sy) is not { } s) continue;
					foreach (PlanetaNoEspaco p in s.Planetas())
						if (!p.Premade && !PlanetaMorto(p) && MundoProcedural.DaSeed(p.Seed, p.Nome).Lado <= 300) { sorteado = p; break; }
				}
			Checa("(montagem) ha um planeta gerado pequeno por perto", sorteado != null);
			if (sorteado is { } gerado)
			{
				ZoneKey zg = Espaco.ZonaDe(gerado);
				MundoDaZona(zg, gerado);
				if (_gerando.TryGetValue(zg.Hash, out GeracaoEmVoo? voo)) voo.Tarefa.Wait();
				TickDasGeracoes();
				ZonaGerada? viva = MundoDaZona(zg);
				Checa("(montagem) o mundo gerado esta de pe", viva != null, viva?.Ficha.Descricao() ?? "nao nasceu");
				if (viva != null)
				{
					int alvoG = Animais.AlvoDoPlaneta(viva.Colisao.Width, viva.Colisao.Height);
					ServerPlayer astronauta = Gente("Astronauta", "Human", zg, viva.Chegada, 100);
					levou = 0;
					while (AnimaisDeTeste(zg).NaZona < alvoG && levou < 240) { Segundos(1); levou++; }
					List<ServerPlayer> deLa = BichosDe(zg);
					Checa("**PLANETA GERADO TEM FAUNA** -- e nele que a carne mais importa", deLa.Count == alvoG && alvoG >= Animais.MinPorPlaneta,
						  $"{deLa.Count} de {alvoG} em {levou} s ({viva.Ficha.Descricao()})");
					Checa("...em chao de verdade do mundo gerado, fora da tela de quem pousou",
						  deLa.Count > 0 && deLa.All(b => viva.Colisao.ServeDeChao(Celula(b.Pos).X, Celula(b.Pos).Y)
														 && Tiles(b.Pos, astronauta.Pos) >= Animais.DistMinDeGenteTiles - 1));
					Checa("...e com a forca do molde, seja qual for a gravidade do mundo: o poder expresso bate com o BP",
						  deLa.Count > 0 && deLa.All(b => b.Ficha.BP == Animais.BpDe(b.Papel!.Molde) && b.Ficha.expressedBP / b.Ficha.BP is > 0.9 and < 1.3),
						  $"gravidade {viva.Ficha.Gravidade:0.##}x: " + string.Join(", ", deLa.Take(4).Select(b => $"{b.Name} {b.Ficha.BP:0}/{b.Ficha.expressedBP:0}")));
					Descartar(astronauta);
					Segundos(1);
					Checa("...e quando o planeta esvazia nao sobra corpo nenhum la (o mundo gerado pode ser descarregado)",
						  ZoneList(zg.Hash).Count == 0 && AnimaisDeTeste(zg).NaZona == 0, $"{ZoneList(zg.Hash).Count} corpo(s)");
				}
			}

			// ================================================================ 5b. os planetas do catalogo
			// A fauna e a mesma regra em todo planeta, mas os mapas nao sao: Namek e Icer sao tres quartos de agua,
			// e a regra de lugar (chao de verdade, longe de predio, com espaco) podia deixar um deles sem bicho. E
			// justo nesses nao ha fruta. Um visitante no ponto de chegada de cada um, e a fauna tem de encher.
			GD.Print("[animais] -- 5b) Namek, Icer e Arlia vivem de caca; Makyo Star nao tem nada --");
			foreach (string nome in (string[])[.. soTemCaca, "Vegeta"])
			{
				ZoneKey z = ZoneKey.Premade(nome);
				ZoneCollision? mapaDele = MapaDaZonaOuCatalogo(z);
				if (mapaDele == null) { Checa($"(montagem) {nome} tem mapa", false); continue; }
				int alvoDele = Animais.AlvoDoPlaneta(mapaDele.Width, mapaDele.Height);
				ServerPlayer visitante = Gente("Em" + nome, "Human", z, PontoDeNascimento(z), 100);
				int seg = 0;
				while (AnimaisDeTeste(z).NaZona < alvoDele && seg < 150) { Segundos(1); seg++; }
				List<ServerPlayer> la = BichosDe(z);
				int carneLa = la.Sum(b => b.Papel!.Molde.Animal!.Carne);
				Checa($"**{nome.ToUpperInvariant()} TEM FAUNA**: com alguem no ponto de chegada ela enche, em chao de verdade e fora da tela",
					  la.Count == alvoDele && la.All(b => mapaDele.ServeDeChao(Celula(b.Pos).X, Celula(b.Pos).Y)
														 && Tiles(b.Pos, visitante.Pos) >= Animais.DistMinDeGenteTiles - 1),
					  $"{la.Count} de {alvoDele} em {seg} s | {la.Select(b => b.Papel!.Molde.Id).Distinct().Count()} especies, "
					  + $"{la.Count(b => !b.Papel!.Molde.Animal!.Bravo)} mansos e {la.Count(b => b.Papel!.Molde.Animal!.Bravo)} bravos, {carneLa} pedacos de carne de pe");
				Descartar(visitante);
				Segundos(1);
			}

			ZoneKey makyo = ZoneKey.Premade("Makyo_Star");
			Checa("(montagem) Makyo Star tem mapa", MapaDaZonaOuCatalogo(makyo) != null);
			ServerPlayer emMakyo = Gente("EmMakyo", "Human", makyo, PontoDeNascimento(makyo), 100);
			Segundos(30);
			Checa("**EM MAKYO STAR NAO NASCE NENHUM**: trinta segundos com gente la, e nada",
				  AnimaisDeTeste(makyo).NaZona == 0 && !ZoneList(makyo.Hash).Any(EhAnimal));
			Animais.FaunaEmTodoPlanetaDeTeste = true;
			try
			{
				int seg = 0;
				while (AnimaisDeTeste(makyo).NaZona == 0 && seg < 90) { Segundos(1); seg++; }
				Checa("(defeito injetado: a lista de planetas sem fauna nao vale) em Makyo Star aparece bicho -- a regua sabe reprovar",
					  AnimaisDeTeste(makyo).NaZona > 0, $"{AnimaisDeTeste(makyo).NaZona} em {seg} s");
			}
			finally { Animais.FaunaEmTodoPlanetaDeTeste = false; }
			Segundos(1);
			Checa("...e com a lista valendo de novo, o que nasceu la sai no segundo seguinte",
				  AnimaisDeTeste(makyo).NaZona == 0 && !ZoneList(makyo.Hash).Any(EhAnimal));
			Descartar(emMakyo);

			// ================================================================ daqui em diante, cenas montadas
			// A FAUNA NATURAL SAI e a chave desliga: as cenas usam bichos postos a mao (pelo `NascerNpc`, a porta
			// de todo nascimento) numa pista de terra firme, e um lobo de passagem estragaria a medida.
			LimparFauna();
			Animais.Ligado = false;
			_pjProximoCorredor = 8;
			_pjMapa = mapa;
			Vec2 pista = CorredorDeTerraFirme(46);
			ulong lugar = 0x7A0F_0000UL;
			ServerPlayer? Bicho(string molde, Vec2 onde) => NascerNpc(molde, terra, onde, ++lugar);
			MoldeDeNpc? moldeDoLobo = _moldes!.Get("animal_lobo"), moldeDaVaca = _moldes.Get("animal_vaca"), moldeDoLeao = _moldes.Get("animal_leao");
			Checa("(montagem) os moldes do lobo, da vaca e do leao existem", moldeDoLobo != null && moldeDaVaca != null && moldeDoLeao != null);
			if (moldeDoLobo == null || moldeDaVaca == null || moldeDoLeao == null) return;

			ServerPlayer cacador = Gente("Cacador", "Human", terra, pista, 40);

			// ================================================================ 6. a forca fixa
			GD.Print("[animais] -- 6) a forca nao anda --");
			ServerPlayer? fixo = Bicho("animal_lobo", pista + new Vec2(20 * T, 0));
			Checa("(montagem) o `NascerNpc` com um molde de bicho devolve um BICHO (e nao um humano com nome de lobo)",
				  fixo != null && EhAnimal(fixo) && fixo.Race == Animais.Raca && _animais.ContainsKey(fixo.Id));
			if (fixo == null) return;
			double bpDoLobo = Animais.BpDe(moldeDoLobo);
			fixo.Ficha.BP = bpDoLobo * 3;
			TickDosAnimais();
			Checa("**A FORCA NAO ANDA**: um BP inflado (como se a luta tivesse rendido) volta ao do molde no segundo seguinte",
				  fixo.Ficha.BP == bpDoLobo, $"BP {fixo.Ficha.BP:0}");
			Animais.BpSoltoDeTeste = true;
			try
			{
				fixo.Ficha.BP = bpDoLobo * 3;
				TickDosAnimais();
				Checa("(defeito injetado: sem o pino) o BP inflado fica -- a regua sabe reprovar", fixo.Ficha.BP == bpDoLobo * 3, $"BP {fixo.Ficha.BP:0}");
			}
			finally { Animais.BpSoltoDeTeste = false; }
			Descartar(fixo);

			// ================================================================ 7. o manso
			GD.Print("[animais] -- 7) o manso nao ataca; apanhou, foge --");
			ServerPlayer? vaca = Bicho("animal_vaca", pista + new Vec2(2 * T, 0));
			if (vaca == null) { Checa("(montagem) a vaca nasceu", false); return; }
			Vec2 tocaDaVaca = vaca.Pos;
			float maisLongeDaToca = 0, andouNoPasto = 0;
			Vec2 passoAnterior = vaca.Pos;
			Tiques(900, () =>
			{
				maisLongeDaToca = Math.Max(maisLongeDaToca, Tiles(vaca.Pos, tocaDaVaca));
				andouNoPasto += (vaca.Pos - passoAnterior).Length;
				passoAnterior = vaca.Pos;
				return false;
			});
			Checa("**O MANSO NAO ATACA**: trinta segundos pastando a dois tiles de um jogador e ele nem encosta",
				  cacador.RancorAte == 0 && cacador.Ficha.HP >= 99.9 && vaca.PresaEngajada == 0, $"vida do cacador {cacador.Ficha.HP:0.#}");
			Checa("**ELE PASTA EM VOLTA DA TOCA**: anda (nao fica plantado) e nunca passa do pasto -- e o que mantem a fauna fora da cidade",
				  andouNoPasto > 2 * T && maisLongeDaToca <= Animais.PastoTiles + 0.5f,
				  $"andou {andouNoPasto / T:0.#} tiles em 30 s, o mais longe a {maisLongeDaToca:0.#} da toca (pasto {Animais.PastoTiles})");

			// um tapa (o cacador tem BP parecido com o dela: ela nao cai). NA PISTA, com o cacador a oeste: a fuga e
			// pro leste, pela fileira que o `CorredorDeTerraFirme` conferiu (pastando ela pode ter saido da fileira,
			// e uma arvore no caminho da fuga mediria a arvore).
			Mudar(vaca, pista + new Vec2(6 * T, 0));
			cacador.Pos = vaca.Pos + new Vec2(-30, 0);
			cacador.Facing = Facing.East;
			cacador.AlvoId = vaca.Id;
			for (int i = 0; i < 40 && vaca.RancorAte == 0; i++) { cacador.Combate.Recarga = 0; Atacar(cacador, Protocol.Golpe.Leve); }
			float antesDaFuga = Tiles(vaca.Pos, cacador.Pos);
			bool correu = false;
			Tiques(90, () => { correu |= vaca.Correndo; return false; });
			Checa("(montagem) o tapa do cacador chegou na vaca, e ela continua de pe", vaca.RancorAte != 0 && !vaca.Ficha.KO && !vaca.Ficha.dead);
			Checa("**APANHOU, FOGE**: o manso corre de quem bateu",
				  Tiles(vaca.Pos, cacador.Pos) - antesDaFuga >= 3 && correu, $"de {antesDaFuga:0.#} pra {Tiles(vaca.Pos, cacador.Pos):0.#} tiles em 3 s");
			Checa("...e NAO revida", cacador.RancorAte == 0 && vaca.PresaEngajada == 0);
			Tiques((int)(Animais.SegundosDeFuga * 30) + 30);
			Checa("...e depois de uns segundos sossega", !vaca.Correndo && DeQuemOBichoFoge(vaca, NowMs()) == null);
			float depoisDaFuga = Tiles(vaca.Pos, pista + new Vec2(6 * T, 0));
			Tiques(900);
			Checa("...e VOLTA PRO PASTO: meio minuto depois ela esta mais perto da toca do que quando parou de correr",
				  Tiles(vaca.Pos, pista + new Vec2(6 * T, 0)) < depoisDaFuga - 2,
				  $"parou a {depoisDaFuga:0.#} tiles da toca, esta a {Tiles(vaca.Pos, pista + new Vec2(6 * T, 0)):0.#}");

			// o defeito: um jogador FRACO (a vaca "brava" nao teria por que ter medo dele), os dois na pista
			Refazer(cacador, 5);
			Refazer(vaca, Animais.BpDe(moldeDaVaca));
			Mudar(vaca, pista + new Vec2(6 * T, 0));
			cacador.Pos = pista + new Vec2(4 * T, 0);
			Animais.MansoAtacaDeTeste = true;
			try
			{
				Tiques(450, () => cacador.RancorAte != 0);
				Checa("(defeito injetado: o manso caca) a vaca parte pra cima do jogador -- a regua sabe reprovar", cacador.RancorAte != 0);
			}
			finally { Animais.MansoAtacaDeTeste = false; }
			Descartar(vaca);

			// ================================================================ 8. o bravo
			GD.Print("[animais] -- 8) o bravo ataca quem chega perto -- andando --");
			Refazer(cacador, 5000);   // forte: o lobo nao o derruba, e a medida e o comportamento do lobo
			cacador.Pos = pista;
			ServerPlayer? lobo = Bicho("animal_lobo", pista + new Vec2(24 * T, 0));
			if (lobo == null) { Checa("(montagem) o lobo nasceu", false); return; }
			float maisPertoDoCacador = float.MaxValue;
			Tiques(450, () => { maisPertoDoCacador = Math.Min(maisPertoDoCacador, Tiles(lobo.Pos, cacador.Pos)); return false; });
			Checa("FORA DO FARO o bravo nao nota ninguem: quinze segundos pastando com um jogador a 24 tiles da toca",
				  lobo.PresaEngajada == 0 && cacador.RancorAte == 0 && maisPertoDoCacador > Animais.FaroTiles,
				  $"o mais perto que chegou: {maisPertoDoCacador:0.#} tiles (faro {Animais.FaroTiles})");

			// os dois na pista (pastando o lobo pode ter saido da fileira conferida)
			Mudar(lobo, pista + new Vec2(24 * T, 0));
			cacador.Pos = lobo.Pos + new Vec2(-4 * T, 0);
			int projeteisAntes = ProjeteisDaZona(terra.Hash).Count;
			float passoMaior = 0;
			bool voou = false, guardou = false, carregou = false, correuAtras = false;
			Vec2 ultima = lobo.Pos;
			float passoDeAndar = MoveRules.SpeedPx(lobo.SpeedStat) * Protocol.TickSeconds;
			float passoDeCorrer = MoveRules.SpeedPx(lobo.SpeedStat, true) * Protocol.TickSeconds;
			Tiques(360, () =>
			{
				passoMaior = Math.Max(passoMaior, (lobo.Pos - ultima).Length);
				ultima = lobo.Pos;
				voou |= lobo.Voando || lobo.Altitude > 0;
				guardou |= lobo.Combate.Bloqueando;
				carregou |= lobo.Carregando;
				correuAtras |= lobo.Correndo;
				return cacador.RancorAte != 0;
			});
			Checa("**NO FARO (4 tiles) O BRAVO ATACA**: vai ate o jogador e morde", cacador.RancorAte != 0 && cacador.UltimoAgressor == lobo.Id && lobo.PresaEngajada == cacador.Id);
			Checa("**NAO TELEPORTA**: do faro ate a mordida, nenhum passo maior que o de CORRER -- ele chega pelas pernas",
				  passoMaior <= passoDeCorrer * 1.05f, $"maior passo {passoMaior:0.0} px por tique (correr = {passoDeCorrer:0.0}, andar = {passoDeAndar:0.0})");
			float lobocorrendo = MoveRules.SpeedPx(lobo.SpeedStat, true), genteAndando = MoveRules.SpeedPx(1f), genteCorrendo = MoveRules.SpeedPx(1f, true);
			Checa("**ELE CORRE ATRAS DA PRESA**: mais rapido que um jogador ANDANDO, mais devagar que um CORRENDO -- de lobo se foge correndo",
				  correuAtras && passoMaior > passoDeAndar * 1.5f && lobocorrendo > genteAndando && lobocorrendo < genteCorrendo,
				  $"lobo correndo {lobocorrendo:0} px/s; jogador andando {genteAndando:0}, correndo {genteCorrendo:0}");
			Checa("...e so corpo a corpo: nao voa, nao solta ki, nao carrega energia nem ergue guarda",
				  !voou && !guardou && !carregou && ProjeteisDaZona(terra.Hash).Count == projeteisAntes);

			// a investida, pedida na mao: o golpe pesado de longe com o alvo marcado
			cacador.Pos = pista;
			Mudar(lobo, pista + new Vec2(8 * T, 0));
			void PedirInvestida()
			{
				lobo.Facing = Facing.West;
				lobo.AlvoId = cacador.Id;
				lobo.Combate.Recarga = 0;
				lobo.Combate.Stun = 0;
				lobo.DashLivreEm = 0;
				lobo.TiquesDeVoo = 0;
				lobo.Ficha.Ki = lobo.Ficha.MaxKi;
				Atacar(lobo, Protocol.Golpe.Pesado);
			}
			Vec2 antesDoBote = lobo.Pos;
			PedirInvestida();
			Checa("**BICHO NAO INVESTE**: o golpe pesado pedido a 8 tiles do alvo MARCADO nao tira o corpo do lugar",
				  (lobo.Pos - antesDoBote).Length < 1f, $"{Tiles(lobo.Pos, antesDoBote):0.#} tiles");
			Animais.InvesteDeTeste = true;
			try
			{
				lobo.Pos = antesDoBote;
				PedirInvestida();
				Checa("(defeito injetado: o bicho investe) o mesmo golpe faz o lobo SALTAR ate o jogador num tique -- a regua sabe reprovar",
					  Tiles(lobo.Pos, antesDoBote) > 5, $"saltou {Tiles(lobo.Pos, antesDoBote):0.#} tiles");
			}
			finally { Animais.InvesteDeTeste = false; }

			// quem some da perseguicao e largado
			Refazer(cacador, 5000);
			Mudar(lobo, pista + new Vec2(4 * T, 0));
			cacador.Pos = pista;
			Tiques(60, () => lobo.PresaEngajada == cacador.Id);
			bool engajou = lobo.PresaEngajada == cacador.Id;
			cacador.Pos = lobo.Pos + new Vec2((Animais.PerseguicaoTiles + 3) * T, 0);
			Tiques(3);
			Checa("QUEM SAI DA PERSEGUICAO E LARGADO: o jogador que some pra longe deixa de ser a presa",
				  engajou && lobo.PresaEngajada == 0, $"a {Tiles(lobo.Pos, cacador.Pos):0} tiles");

			// quem voa esta fora do alcance da mordida: nao e presa -- e o bravo que apanha de quem nao alcanca, corre
			Refazer(cacador, 5000);
			Mudar(lobo, pista + new Vec2(10 * T, 0));
			cacador.Pos = lobo.Pos + new Vec2(-3 * T, 0);
			cacador.Voando = true;
			cacador.Altitude = Voo.AlturaMaxima;
			Tiques(120);
			Checa("QUEM VOA NAO E PRESA: tres tiles ao lado e la em cima, o bravo continua pastando",
				  lobo.PresaEngajada == 0 && cacador.RancorAte == 0);
			// (os dois de volta na pista: em quatro segundos de pasto o lobo pode ter saido da fileira conferida, e a
			// fuga pro leste daria numa arvore -- esse caso tem a familia dele, logo abaixo)
			Mudar(lobo, pista + new Vec2(10 * T, 0));
			cacador.Pos = lobo.Pos + new Vec2(-3 * T, 0);
			MarcarAgressao(lobo, cacador);   // um tiro la de cima
			float antesDeCorrer = Tiles(lobo.Pos, cacador.Pos);
			bool correuDoVoador = false;
			Tiques(90, () => { correuDoVoador |= lobo.Correndo; return false; });
			Checa("...e o bravo que APANHA de quem ele nao alcanca CORRE, em vez de pastar levando tiro",
				  correuDoVoador && Tiles(lobo.Pos, cacador.Pos) > antesDeCorrer + 3 && cacador.RancorAte == 0,
				  $"de {antesDeCorrer:0.#} pra {Tiles(lobo.Pos, cacador.Pos):0.#} tiles em 3 s");
			// BARRADO NA FUGA. Um obstaculo ISOLADO do mapa (uma celula que barra no meio de um descampado: chao
			// livre por oito tiles a oeste, catorze a leste e doze pra cima e pra baixo), o lobo colado a oeste dele
			// e quem atira mais a oeste, la em cima: a fuga e pro leste, de cara no obstaculo.
			(int X, int Y)? ilhado = null;
			for (int oy = 14; oy < Math.Min(mapa.Height - 14, 300) && ilhado == null; oy++)
				for (int ox = 12; ox < Math.Min(mapa.Width - 16, 300) && ilhado == null; ox++)
				{
					if (!mapa.BlockedCell(ox, oy)) continue;
					bool limpo = true;
					for (int dy = -12; dy <= 12 && limpo; dy++)
						for (int dx = -8; dx <= 14 && limpo; dx++)
							if ((dx != 0 || dy != 0) && !mapa.ServeDeChao(ox + dx, oy + dy)) limpo = false;
					if (limpo) ilhado = (ox, oy);
				}
			Checa("(montagem) o mapa tem um obstaculo isolado, com chao livre em volta", ilhado != null);
			if (ilhado is { } obst)
			{
				float FugaContraOObstaculo()
				{
					Refazer(cacador, 5000);
					Mudar(lobo, mapa.CentroDaCelula(obst.X - 1, obst.Y));
					cacador.Pos = mapa.CentroDaCelula(obst.X - 5, obst.Y);
					cacador.Voando = true;
					cacador.Altitude = Voo.AlturaMaxima;
					MarcarAgressao(lobo, cacador);
					float antes = Tiles(lobo.Pos, cacador.Pos);
					Tiques(90);
					return Tiles(lobo.Pos, cacador.Pos) - antes;
				}
				float ganhou = FugaContraOObstaculo();
				Checa("**BARRADO NA FUGA, DESVIA**: com um obstaculo colado no caminho, o bicho contorna e continua se afastando",
					  ganhou >= 3, $"abriu {ganhou:0.#} tiles em 3 s, com o obstaculo em ({obst.X}, {obst.Y})");
				Animais.SemDesvioDeTeste = true;
				try
				{
					float semDesvio = FugaContraOObstaculo();
					Checa("(defeito injetado: sem desvio) o bicho fica empurrando o obstaculo, parado, de costas pra quem bate -- a regua sabe reprovar",
						  semDesvio < 1, $"abriu {semDesvio:0.#} tiles em 3 s");
				}
				finally { Animais.SemDesvioDeTeste = false; }
			}
			cacador.Voando = false;
			cacador.Altitude = 0;

			// o territorio: o jogador recua devagar, sempre tres tiles a frente do lobo
			Refazer(cacador, 5000);
			Mudar(lobo, pista + new Vec2(2 * T, 0));
			cacador.Pos = lobo.Pos + new Vec2(3 * T, 0);
			Vec2 casa = lobo.Pos;
			float maisLonge = 0;
			bool largou = false;
			Tiques(600, () =>
			{
				maisLonge = Math.Max(maisLonge, Tiles(lobo.Pos, casa));
				if (_animais.TryGetValue(lobo.Id, out AnimalVivo? a) && a.DeVoltaAte > NowMs()) largou = true;
				if (!largou) cacador.Pos = lobo.Pos + new Vec2(3 * T, 0);
				return largou && Tiles(lobo.Pos, casa) < maisLonge - 3;
			});
			Checa("**O TERRITORIO**: puxado andando, o bravo larga a presa na borda (medida da toca) e VOLTA -- ninguem leva um lobo ate a cidade",
				  largou && maisLonge <= Animais.TerritorioTiles + 2 && Tiles(lobo.Pos, casa) < maisLonge - 3,
				  $"foi ate {maisLonge:0.#} tiles (territorio {Animais.TerritorioTiles}), voltou pra {Tiles(lobo.Pos, casa):0.#}");

			// a tregua: derrubou a presa, da a briga por ganha. A CENA E MONTADA, e nao sorteada: a presa e um jogador
			// forte (a mordida nao o fere) e o nocaute entra pela porta do corpo (`CombatState.Nocautear`). O que se
			// mede e o que o LOBO faz quando a presa dele cai e levanta ao lado -- nao a sorte de uma surra.
			bool MordidoDeNovoAoLevantar(out bool mordeuAntes, out bool emTregua)
			{
				Refazer(cacador, 5000);
				Mudar(lobo, pista + new Vec2(14 * T, 0));
				cacador.Pos = lobo.Pos + new Vec2(-2 * T, 0);
				Tiques(300, () => cacador.RancorAte != 0);
				mordeuAntes = cacador.RancorAte != 0 && lobo.PresaEngajada == cacador.Id;

				cacador.Combate.Nocautear(3);                      // a presa cai...
				Tiques(5);
				emTregua = _animais.TryGetValue(lobo.Id, out AnimalVivo? doLobo) && doLobo.DeVoltaAte > NowMs() && lobo.PresaEngajada == 0;
				cacador.Combate.Curar();                           // ...e levanta, a dois tiles do lobo
				cacador.RancorAte = 0;
				cacador.UltimoAgressor = 0;
				cacador.Pos = lobo.Pos + new Vec2(-2 * T, 0);
				Tiques(300, () => cacador.RancorAte != 0);
				return cacador.RancorAte != 0;
			}

			bool mordidoDeNovo = MordidoDeNovoAoLevantar(out bool mordeuAPresa, out bool lobEmTregua);
			Checa("(montagem) o lobo adotou o jogador a dois tiles e mordeu", mordeuAPresa);
			Checa("**A TREGUA**: a presa caiu, o lobo da a briga por ganha -- quem levanta ao lado dele NAO e mordido de novo, e tem tempo de ir embora",
				  lobEmTregua && !mordidoDeNovo, $"tregua de {Animais.SegundosDeTregua:0} s");
			AndarORelogio(Animais.SegundosDeTregua * 1000);
			// (de volta ao ponto da cena, na pista -- pastando na tregua ele pode ter ido pra tras de uma arvore. So
			// a posicao: a tregua tem de ter vencido pelo RELOGIO, e nao por a bancada te-la zerado.)
			lobo.Pos = pista + new Vec2(14 * T, 0);
			if (_animais.TryGetValue(lobo.Id, out AnimalVivo? deVolta)) deVolta.Andando = false;
			cacador.Pos = lobo.Pos + new Vec2(-2 * T, 0);
			Tiques(450, () => cacador.RancorAte != 0);
			Checa("...e passada a tregua o faro volta a valer", cacador.RancorAte != 0 && cacador.UltimoAgressor == lobo.Id);

			Animais.SemTreguaDeTeste = true;
			try
			{
				bool semTregua = MordidoDeNovoAoLevantar(out bool mordeuDeNovoAntes, out bool _);
				Checa("(defeito injetado: sem tregua) a MESMA cena: quem levanta ao lado do lobo e mordido de novo na hora -- a regua sabe reprovar",
					  mordeuDeNovoAntes && semTregua);
			}
			finally { Animais.SemTreguaDeTeste = false; }
			Refazer(cacador, 5000);
			Descartar(lobo);

			// ================================================================ 9. a caca
			GD.Print("[animais] -- 9) cai e morre; a carcaca, a carne e o saque --");
			Refazer(cacador, 5000);
			cacador.Karma = 0;
			double repAntes = ReputacaoDe(terra.Name, cacador.Conta);
			// o estoque da Terra, pra ver o abate descontar
			FaunaDoPlaneta fauna = FaunaDe(terra, mapa, NowMs());
			double estoqueAntes = fauna.Estoque;

			ServerPlayer? presa = Bicho("animal_lobo", pista + new Vec2(1 * T, 0));
			if (presa == null) { Checa("(montagem) o lobo da caca nasceu", false); return; }
			int golpes = BaterAteCair(cacador, presa);
			Checa("**BICHO QUE CAI, MORRE** -- e na hora: o golpe NAO-LETAL de todo jogador (o que so nocauteia gente) ja o deixa morto, sem um instante caido e vivo",
				  presa.Ficha.dead && !presa.Ficha.KO && !cacador.Combate.Letal, $"{golpes} golpe(s)");
			List<string> doAbate = Ouvir(() => Tiques(2));
			ServerPlayer? carcaca = CarcacaDe(presa);
			Checa("...e o corpo sai do mundo no tique seguinte, pelo caminho de todo NPC morto",
				  !_players.ContainsKey(presa.Id) && !_animais.ContainsKey(presa.Id) && !ZoneList(terra.Hash).Contains(presa));
			// (`presa.Pos` e onde o corpo estava quando saiu: o golpe que derruba tambem arremessa.)
			Checa("**FICA A CARCACA**, no lugar e com a cara dele: a mesma folha, deitada",
				  carcaca != null && EhCarcaca(carcaca) && carcaca.Race == Animais.Raca && carcaca.Visual.Corpo == presa.Visual.Corpo
				  && (carcaca.Pos - presa.Pos).Length < 2f && carcaca.Ficha.dead, carcaca?.Name ?? "sem carcaca");
			if (carcaca == null) return;
			Checa("quem abateu e avisado de que a carne esta na carcaca", Disse(doAbate, "abateu") && Disse(doAbate, "carne"), Ultimos(doAbate));
			int carneDoLobo = moldeDoLobo.Animal!.Carne;
			Checa("**COM A CARNE**: tantos pedacos quanto a especie da", carcaca.Mochila.Quantos(CatalogoDeItens.Carne) == carneDoLobo,
				  $"{carcaca.Mochila.Quantos(CatalogoDeItens.Carne)} de {carneDoLobo}");
			Checa("abater tira UM do estoque do planeta (que so volta com o tempo)",
				  Math.Abs(AnimaisDeTeste(terra).Estoque - (estoqueAntes - 1)) < 0.05, $"de {estoqueAntes:0.##} pra {AnimaisDeTeste(terra).Estoque:0.##}");
			Checa("**SEM KARMA E SEM REPUTACAO**: abater um bicho nao mexe em nenhum dos dois",
				  cacador.Karma == 0 && ReputacaoDe(terra.Name, cacador.Conta) == repAntes, $"karma {cacador.Karma}, reputacao {ReputacaoDe(terra.Name, cacador.Conta):0.#}");

			// o saque
			cacador.Pos = carcaca.Pos + new Vec2(20, 0);
			Checa("a carcaca NAO e corpo de enterrar: o E nela nao abre o menu de cadaver", CadaverPerto(cacador) == null);
			lista = null;
			List<string> falas = Ouvir(() => Verbo(cacador, Saque.Verbo, ""));
			Checa("**REVISTAR A CARCACA (o `saquear` da tecla E) MOSTRA A CARNE**",
				  lista is { } l1 && l1.Ladrao == cacador.Id && l1.Caido == carcaca.Id && l1.Leva.Count == 1
				  && l1.Leva[0].Id == CatalogoDeItens.Carne && l1.Leva[0].Quantidade == carneDoLobo,
				  lista is { } l1b ? string.Join(",", l1b.Leva.Select(p => $"{p.Id}x{p.Quantidade}")) : "sem lista: " + Ultimos(falas));
			Ouvir(() => Verbo(cacador, Saque.Verbo, CatalogoDeItens.Carne));
			Checa("**UM PEDACO POR VEZ VAI PRA MOCHILA DO CACADOR**",
				  cacador.Mochila.Quantos(CatalogoDeItens.Carne) == 1 && carcaca.Mochila.Quantos(CatalogoDeItens.Carne) == carneDoLobo - 1);
			for (int i = 1; i < carneDoLobo; i++) { cacador.ProximoSaqueMs = 0; Ouvir(() => Verbo(cacador, Saque.Verbo, CatalogoDeItens.Carne)); }
			Checa("**A CARCACA VAZIA SOME**, e a carne toda esta com quem cacou",
				  cacador.Mochila.Quantos(CatalogoDeItens.Carne) == carneDoLobo && !ZoneList(terra.Hash).Contains(carcaca) && !_carcacas.ContainsKey(carcaca.Id));

			// a mesma medida com um CIDADAO: o caminho da morte e o mesmo, e o que muda e o tipo
			ServerPlayer? cidadao = NascerNpc("cidadao", terra, pista + new Vec2(6 * T, 0), ++lugar);
			if (cidadao != null)
			{
				Checa("o NPC humanoide continua SEM inventario (nenhum molde de gente carrega item)", cidadao.Mochila.Ocupados == 0);
				MarcarAgressao(cidadao, cacador);
				cidadao.Combate.Morrer(ignorarSeguro: true);
				cidadao.RelogioDaMorte = NowMs();
				Tiques(2);
				ServerPlayer? corpoDoCidadao = CarcacaDe(cidadao);
				Checa("(contra-exemplo: o MESMO caminho com um cidadao) matar gente custa reputacao -- a regua sabe reprovar",
					  ReputacaoDe(terra.Name, cacador.Conta) < repAntes, $"reputacao {ReputacaoDe(terra.Name, cacador.Conta):0.#}");
				Checa("...e o corpo de gente NAO e carcaca: nao se revista, se enterra",
					  corpoDoCidadao != null && !EhCarcaca(corpoDoCidadao) && corpoDoCidadao.Mochila.Ocupados == 0);
				if (corpoDoCidadao != null)
				{
					// o corpo de gente destrocado se desfaz (a regra de todo cadaver) -- o contra-exemplo da carcaca, abaixo
					foreach (var p in corpoDoCidadao.Combate.Corpo.Partes) p.Vida = 0;
					TickDosCadaveres();
					Checa("(contra-exemplo: um corpo de GENTE com todos os membros em zero) ele se desfaz na hora -- a regua sabe reprovar",
						  !ZoneList(terra.Hash).Contains(corpoDoCidadao));
					if (ZoneList(terra.Hash).Contains(corpoDoCidadao)) DesfazerOCadaver(corpoDoCidadao);
				}
			}

			// a carcaca que ninguem pegou: nao se desfaz por dano, e tem prazo
			ServerPlayer? esquecida = Bicho("animal_vaca", pista + new Vec2(10 * T, 0));
			if (esquecida != null)
			{
				BaterAteCair(cacador, esquecida);
				Tiques(3);
				ServerPlayer? resto = CarcacaDe(esquecida);
				cacador.Pos = pista;
				if (resto != null)
				{
					foreach (var p in resto.Combate.Corpo.Partes) p.Vida = 0;
					TickDosCadaveres();
					Checa("**A CARCACA NAO SE DESFAZ POR DANO**: com o corpo inteiro em zero (uma rajada de ki de quem tem milhares de BP) a carne continua la",
						  ZoneList(terra.Hash).Contains(resto) && resto.Mochila.Quantos(CatalogoDeItens.Carne) == moldeDaVaca.Animal!.Carne);
				}
				AndarORelogio((Animais.SegundosDaCarcaca - 5) * 1000);
				TickDosAnimais();
				bool aindaLa = resto != null && ZoneList(terra.Hash).Contains(resto);
				AndarORelogio(6_000);
				TickDosAnimais();
				Checa("A CARCACA QUE NINGUEM PEGOU fica o prazo dela e depois some, com a carne",
					  aindaLa && resto != null && !ZoneList(terra.Hash).Contains(resto) && !_carcacas.ContainsKey(resto.Id),
					  $"prazo {Animais.SegundosDaCarcaca:0} s");
			}

			// a caca LETAL (o modo de quem quer matar): morre direto, e a carcaca e a mesma -- inteira
			int pecasAntes = PecasDaZona(terra.Hash).Count;
			ServerPlayer? abatidoALetal = Bicho("animal_lobo", pista + new Vec2(1 * T, 0));
			if (abatidoALetal != null)
			{
				Refazer(cacador, 5000);
				cacador.Combate.Letal = true;
				try { BaterAteCair(cacador, abatidoALetal); }
				finally { cacador.Combate.Letal = false; }
				Tiques(4);
				ServerPlayer? carcacaLetal = CarcacaDe(abatidoALetal);
				Checa("COM O GOLPE LETAL e igual: morre, e deixa a carcaca com a carne",
					  abatidoALetal.Ficha.dead && carcacaLetal != null && EhCarcaca(carcacaLetal)
					  && carcacaLetal.Mochila.Quantos(CatalogoDeItens.Carne) == carneDoLobo);
				Checa("...INTEIRA: nenhuma pata arrancada, nenhum pedaco de corpo no chao",
					  carcacaLetal != null && !carcacaLetal.Combate.Corpo.Partes.Any(p => p.Decepado) && PecasDaZona(terra.Hash).Count == pecasAntes,
					  $"{PecasDaZona(terra.Hash).Count - pecasAntes} peca(s) no chao");
				if (carcacaLetal != null) DesfazerOCadaver(carcacaLetal);
			}

			// a regra por tras disso, medida direto: o MESMO golpe letal que zera um membro, num bicho e numa pessoa
			ServerPlayer? deQuatroPatas = Bicho("animal_lobo", pista + new Vec2(30 * T, 0));
			ServerPlayer manequim = Gente("Manequim", "Human", terra, pista + new Vec2(34 * T, 0), 50);
			if (deQuatroPatas != null)
			{
				var pata = deQuatroPatas.Combate.Corpo.Achar("Braco direito");
				var braco = manequim.Combate.Corpo.Achar("Braco direito");
				if (pata != null) deQuatroPatas.Combate.Ferir(pata, 1e9, letal: true);
				bool pataFicou = pata is { Decepado: false, Vida: <= 0 } && PecasDaZona(terra.Hash).Count == pecasAntes;
				if (braco != null) manequim.Combate.Ferir(braco, 1e9, letal: true);
				bool bracoCaiu = braco is { Decepado: true } && PecasDaZona(terra.Hash).Count > pecasAntes;
				Checa("**PATA DE BICHO NAO CAI**: o golpe letal que zera o membro nao o arranca (a peca no chao seria um braco de gente)", pataFicou);
				Checa("(contra-exemplo: o mesmo golpe num corpo de gente) o braco e arrancado e vai pro chao -- a regua sabe reprovar", bracoCaiu);
				// as pecas da bancada saem do chao
				var noChao = PecasDaZona(terra.Hash);
				if (noChao.Count > pecasAntes) noChao.RemoveRange(pecasAntes, noChao.Count - pecasAntes);
			}
			Descartar(deQuatroPatas, manequim);

			Animais.NocauteNaoMataDeTeste = true;
			try
			{
				ServerPlayer? duro = Bicho("animal_lobo", pista + new Vec2(1 * T, 0));
				if (duro != null)
				{
					Refazer(cacador, 5000);
					BaterAteCair(cacador, duro);
					Tiques(5);
					TickDosAnimais();
					Checa("(defeito injetado: nocaute nao mata) o lobo fica no chao VIVO, sem carcaca, e levantaria como gente -- a regua sabe reprovar",
						  duro.Ficha.KO && !duro.Ficha.dead && _players.ContainsKey(duro.Id) && CarcacaDe(duro) == null);
					Descartar(duro);
				}
			}
			finally { Animais.NocauteNaoMataDeTeste = false; }

			// ================================================================ 10. a carne
			GD.Print("[animais] -- 10) comer carne --");
			cacador.Mochila.Guardar(CatalogoDeItens.Maca, 1);
			SorteioDeSementeDeTeste = 0.99;   // a maca desta medida nao deixa semente
			double tanque = Nutricao.Tanque(cacador.Ficha.Metabolism);
			cacador.Ficha.CurrentNutrition = 0;
			Ouvir(() => Verbo(cacador, "item_comer", CatalogoDeItens.Carne));
			double comCarne = cacador.Ficha.CurrentNutrition;
			cacador.Ficha.CurrentNutrition = 0;
			Ouvir(() => Verbo(cacador, "item_comer", CatalogoDeItens.Maca));
			double comMaca = cacador.Ficha.CurrentNutrition;
			Checa("**COMER CARNE ALIMENTA BASTANTE -- bem mais que a maca**",
				  comCarne == Animais.NutricaoDaCarne && comMaca > 0 && comCarne >= comMaca * 3, $"carne {comCarne:0.#}, maca {comMaca:0.#}");
			Checa("...mais de meio estomago num pedaco so", comCarne >= tanque * 0.5, $"{comCarne:0.#} de {tanque:0.#}");
			Checa("...pela mesma porta da maca: o pedaco comido sai da mochila", cacador.Mochila.Quantos(CatalogoDeItens.Carne) == carneDoLobo - 1);
			cacador.Ficha.CurrentNutrition = tanque;
			falas = Ouvir(() => Verbo(cacador, "item_comer", CatalogoDeItens.Carne));
			Checa("...e cheio nao come: a carne fica guardada pra depois",
				  cacador.Mochila.Quantos(CatalogoDeItens.Carne) == carneDoLobo - 1 && Disse(falas, "cheio"), Ultimos(falas));

			// ================================================================ 11. sem abuso
			GD.Print("[animais] -- 11) bicho nao e fonte de BP, de Zenkai nem de treino --");
			ServerPlayer novato = Gente("Novato", "Saiyan", terra, pista + new Vec2(20 * T, 0), 5);
			ServerPlayer? leao = Bicho("animal_leao", novato.Pos + new Vec2(1 * T, 0));
			if (leao == null) { Checa("(montagem) o leao nasceu", false); return; }
			Checa("(montagem) o novato e um Saiyajin (tem Zenkai) vinte vezes mais fraco que o leao",
				  novato.Ficha.HasZenkai() && leao.Ficha.expressedBP > novato.Ficha.expressedBP * 10,
				  $"{novato.Ficha.expressedBP:0.#} contra {leao.Ficha.expressedBP:0.#}");

			Checa("**SOCAR BICHO RENDE O QUE SOCAR O AR RENDE**: o gap de poder contra um leao nao multiplica o ganho",
				  novato.Ficha.FightGainMult(leao.Ficha) == 1, $"x{novato.Ficha.FightGainMult(leao.Ficha):0.##}");
			Animais.BonusDeLutaDeTeste = true;
			try
			{
				Checa("(defeito injetado: o gap vale) o mesmo novato ganharia o teto do bonus de lutar com quem e mais forte -- a regua sabe reprovar",
					  novato.Ficha.FightGainMult(leao.Ficha) == GainKnobs.FightGainCap, $"x{novato.Ficha.FightGainMult(leao.Ficha):0.##}");
			}
			finally { Animais.BonusDeLutaDeTeste = false; }

			double bpDoNovato = novato.Ficha.BP;
			AoPerderALuta(novato, leao, morreu: false);
			Checa("**SER DERRUBADO POR UM BICHO NAO DA ZENKAI**", novato.Ficha.BP == bpDoNovato, $"BP {novato.Ficha.BP:0.##}");
			Animais.DerrotaDeGenteDeTeste = true;
			try
			{
				AoPerderALuta(novato, leao, morreu: false);
				Checa("(defeito injetado: derrota de gente) o Saiyajin derrubado pelo leao sai mais forte -- a regua sabe reprovar",
					  novato.Ficha.BP > bpDoNovato, $"BP {bpDoNovato:0.##} -> {novato.Ficha.BP:0.##}");
			}
			finally { Animais.DerrotaDeGenteDeTeste = false; }
			Descartar(novato);

			// apanhar parado: um corpo do tamanho do leao, que nao revida
			ServerPlayer saco = Gente("Saco", "Human", terra, leao.Pos + new Vec2(-2 * T, 0), 100);
			double bpDoSaco = saco.Ficha.BP;
			Tiques(450, () => saco.Ficha.HP < 97);
			Checa("(montagem) o leao acertou quem estava parado ao lado dele", saco.Ficha.HP < 99.9 && saco.UltimoAgressor == leao.Id, $"vida {saco.Ficha.HP:0.#}");
			Checa("**APANHAR DE BICHO NAO TREINA**: o BP de quem apanhou nao andou", saco.Ficha.BP == bpDoSaco, $"BP {saco.Ficha.BP:0.######}");
			Tiques(1);   // o `IsInFight` da ficha e escrito pelo tique do combate, a partir do relogio
			Checa("...nem liga a \"luta de verdade\" de quem so apanha (o relogio que paga treino de gravidade, skill de luta e estilo); a tag de combate fica",
				  saco.Combate.LutandoDeVerdade <= 0 && !saco.Ficha.IsInFight && saco.Combate.EmCombate > 0,
				  $"luta de verdade {saco.Combate.LutandoDeVerdade:0.#} s, tag {saco.Combate.EmCombate:0} s");
			Refazer(saco, bpDoSaco);
			saco.Pos = leao.Pos + new Vec2(-2 * T, 0);
			Animais.ApanharTreinaDeTeste = true;
			try
			{
				Tiques(450, () => saco.Ficha.HP < 97);
				Checa("(defeito injetado: apanhar treina) cada mordida que encosta rende ganho de luta -- a regua sabe reprovar",
					  saco.Ficha.BP > bpDoSaco, $"BP {bpDoSaco:0.######} -> {saco.Ficha.BP:0.######}");
				Tiques(1);
				Checa("(o mesmo defeito) ...e quem so apanha passa a estar em \"luta de verdade\"",
					  saco.Combate.LutandoDeVerdade > 0 && saco.Ficha.IsInFight, $"{saco.Combate.LutandoDeVerdade:0.#} s");
			}
			finally { Animais.ApanharTreinaDeTeste = false; }

			// quem REVIDA esta lutando: o golpe do proprio jogador no bicho liga o relogio dele
			Refazer(saco, bpDoSaco);
			saco.Combate.LutandoDeVerdade = 0;
			saco.Pos = leao.Pos + new Vec2(-30, 0);
			saco.Facing = Facing.East;
			saco.AlvoId = leao.Id;
			saco.Combate.Recarga = 0;
			saco.Combate.Stun = 0;
			Atacar(saco, Protocol.Golpe.Leve);
			Checa("quem REVIDA esta lutando: o golpe do proprio jogador no bicho liga a luta de verdade dele",
				  saco.Combate.LutandoDeVerdade > 0, $"{saco.Combate.LutandoDeVerdade:0.#} s");
			Checa("...e o leao, de tanto bater, continua com a forca do molde", leao.Ficha.BP == Animais.BpDe(moldeDoLeao) || TickESeMantem(leao, moldeDoLeao),
				  $"BP {leao.Ficha.BP:0.######}");

			Checa("o inimigo comum continua sem nascer (o `NascerNpc` o recusa)", NascerNpc("saibaman", terra, pista, ++lugar) == null);
			Descartar(saco, leao);

			// ================================================================ 12. o quadro inteiro
			// As familias de cima rodam o combate e as mentes; o resto do servidor (fome, ficha, feridas, treino,
			// snapshot, o bloco de 1 Hz) so passa por um bicho no `Tick()` de verdade. Um de cada especie de pe, uma
			// galinha caindo no meio, e cinco segundos de servidor inteiro.
			GD.Print("[animais] -- 12) o tique INTEIRO do servidor com a fauna de pe --");
			Refazer(cacador, 5000);
			cacador.Pos = pista;
			var zoo = new List<ServerPlayer>();
			for (int i = 0; i < especies.Count; i++)
				if (Bicho(especies[i].Id, pista + new Vec2((10 + i * 4) * T, 0)) is { } b) zoo.Add(b);
			ServerPlayer? galinha = Bicho("animal_galinha", pista + new Vec2(2 * T, 0));
			if (galinha != null) BaterAteCair(cacador, galinha);
			cacador.Pos = pista;

			long quadrosAntes = _quadrosInteiros;
			int cadenciaDeAntes = _tickCount;
			// (A cadencia comeca do zero pra os 150 quadros nao cruzarem o salvamento em disco, a cada 3600.)
			_tickCount = 0;
			string estouro = "";
			const int Quadros = 150;
			try { for (int i = 0; i < Quadros; i++) Tick(); }
			catch (Exception e) { estouro = e.ToString(); }
			finally { _tickCount = cadenciaDeAntes; }
			Checa("**O TIQUE INTEIRO DO SERVIDOR RODA COM BICHO NO MUNDO**: cinco segundos de quadro cheio, nenhum estouro",
				  estouro.Length == 0 && _quadrosInteiros == quadrosAntes + Quadros,
				  estouro.Length > 0 ? estouro : $"{_quadrosInteiros - quadrosAntes} de {Quadros} quadros");
			Checa("...todas as especies continuam de pe, vivas e com a forca do molde",
				  zoo.Count == especies.Count && zoo.All(b => _players.ContainsKey(b.Id) && !b.Ficha.dead && !b.Ficha.KO
															  && Math.Abs(b.Ficha.BP / Animais.BpDe(b.Papel!.Molde) - 1) < 0.01),
				  string.Join(", ", zoo.Select(b => $"{b.Name} {b.Ficha.BP:0.##}")));
			Checa("...e a galinha derrubada virou carcaca pelo caminho de producao (o `Tick()`, e nao a bancada)",
				  galinha != null && galinha.Ficha.dead && !_players.ContainsKey(galinha.Id) && CarcacaDe(galinha) is { } cg && EhCarcaca(cg));
		}
		catch (Exception e)
		{
			Checa("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			Animais.QualquerLugarDeTeste = Animais.BpSoltoDeTeste = Animais.MansoAtacaDeTeste = Animais.InvesteDeTeste = false;
			Animais.NocauteNaoMataDeTeste = Animais.DerrotaDeGenteDeTeste = Animais.ApanharTreinaDeTeste = Animais.BonusDeLutaDeTeste = false;
			Animais.SemTreguaDeTeste = false;
			Animais.FaunaEmTodoPlanetaDeTeste = false;
			Animais.SemDesvioDeTeste = false;
			EscutaDeSaque = escutaDeAntes;
			SorteioDeAnimalDeTeste = null;
			SorteioDeSementeDeTeste = sementeDeAntes;

			LimparFauna();
			_fauna.Clear();
			foreach (ServerPlayer c in forjados.ToList()) Descartar(c);
			// os cadaveres que a bancada deixou (o cidadao do contra-exemplo, se sobrou)
			foreach (ServerPlayer c in ZoneList(terra.Hash).Where(c => c.ECadaver && c.CaiuEm >= inicio).ToList()) DesfazerOCadaver(c);

			Animais.Ligado = ligadoDeAntes;
			pl.Zone = terra;
			pl.Combate.Carencia = carenciaDoHost;
			if (entrouPelaBancada) daZona.Remove(pl);   // o `Entrar` o poe de volta, na linha dele

			// O PLACAR SAI DAQUI: a bancada tem saidas no meio, e sem placar o lancador espera o prazo inteiro.
			GD.Print($"[animais] ================ {ok} OK, {falhou} FALHA(S) ================");
		}

		// O PINO E DE 1 Hz: depois de uma surra o BP do bicho pode estar um fio acima ate o segundo virar.
		bool TickESeMantem(ServerPlayer bicho, MoldeDeNpc molde)
		{
			TickDosAnimais();
			return bicho.Ficha.BP == Animais.BpDe(molde);
		}
	}

	// =====================================================================
	// O PALCO DA `--diaganimais` (o robo de janela, `Client/RoboDeAnimais.cs`)
	// =====================================================================

	/// <summary>Os bichos que o palco pos, por id. SO PRA BANCADA.</summary>
	private readonly List<int> _zooDeTeste = [];

	/// <summary>
	/// UM BICHO DE CADA ESPECIE EM VOLTA DE UM JOGADOR: os mansos numa fileira dois tiles ao norte, os bravos
	/// numa dois tiles ao sul (deslocada pro leste: o painel do chat cobre o canto de baixo da tela). As duas
	/// cabem na janela da bancada. Nascem pela porta de todo NPC (`NascerNpc`), com a toca ali; os bravos ficam
	/// quietos pelos primeiros segundos (a foto do zoo) e depois e o jogo. SO PRA BANCADA.
	/// </summary>
	public List<(int Id, string Nome, string Folha, bool Bravo)> ZooDeTeste(int idDoJogador)
	{
		var saida = new List<(int, string, string, bool)>();
		if (!_players.TryGetValue(idDoJogador, out ServerPlayer? quem)) return saida;

		const float T = ZoneCollision.TileSize;
		int mansos = 0, bravos = 0;
		foreach (MoldeDeNpc m in Especies())
		{
			bool bravo = m.Animal!.Bravo;
			Vec2 onde = bravo
				? quem.Pos + new Vec2(bravos++ * 1.5f * T, 2 * T)
				: quem.Pos + new Vec2((mansos++ * 1.5f - 3) * T, -2 * T);
			if (NascerNpc(m.Id, quem.Zone, onde, ++_lugarDosAnimais) is not { } npc) continue;
			if (_animais.TryGetValue(npc.Id, out AnimalVivo? a)) a.DeVoltaAte = NowMs() + 4_000;
			_zooDeTeste.Add(npc.Id);
			saida.Add((npc.Id, npc.Name, Animais.CaminhoDaFolha(npc.Visual.Corpo), bravo));
		}
		return saida;
	}

	/// <summary>Tira do mundo os bichos do palco, menos um (0 = todos). SO PRA BANCADA.</summary>
	public void LimparZooDeTeste(int menosEste)
	{
		foreach (int id in _zooDeTeste.ToList())
		{
			if (id == menosEste) continue;
			_zooDeTeste.Remove(id);
			_animais.Remove(id);
			if (_players.TryGetValue(id, out ServerPlayer? npc) && EhAnimal(npc)) RemoverNpc(npc);
		}
	}

	/// <summary>
	/// O ABATE DE UM BICHO DO PALCO, ao lado de um jogador e em nome dele. O golpe em si e da bancada de servidor
	/// (`--animaisteste`); daqui em diante e producao: a morte, a saida do mundo, a carcaca com a carne e o
	/// aviso a quem abateu. SO PRA BANCADA.
	/// </summary>
	public bool AbaterDeTeste(int idDoBicho, int idDoJogador)
	{
		if (!_players.TryGetValue(idDoBicho, out ServerPlayer? bicho) || !EhAnimal(bicho)) return false;
		if (!_players.TryGetValue(idDoJogador, out ServerPlayer? quem)) return false;
		bicho.Pos = quem.Pos + new Vec2(28, 0);
		MarcarAgressao(bicho, quem);
		AbaterAnimal(bicho);
		return bicho.Ficha.dead;
	}

	/// <summary>A carcaca de um bicho com este nome: o id dela, quanta carne tem e onde esta. SO PRA BANCADA.</summary>
	public (int Id, int Carne, Vec2 Onde)? CarcacaDeTeste(string nomeDoBicho)
	{
		foreach ((ServerPlayer corpo, long _) in _carcacas.Values)
			if (corpo.NomeDeQuemMorreu == nomeDoBicho)
				return (corpo.Id, corpo.Mochila.Quantos(CatalogoDeItens.Carne), corpo.Pos);
		return null;
	}

	/// <summary>O nome do ultimo BICHO que agrediu este jogador, ou nulo. SO PRA BANCADA.</summary>
	public string? QuemMordeuDeTeste(int idDoJogador) =>
		_players.TryGetValue(idDoJogador, out ServerPlayer? pl) && pl.RancorAte != 0
		&& _players.TryGetValue(pl.UltimoAgressor, out ServerPlayer? q) && EhAnimal(q) ? q.Name : null;

	/// <summary>Esvazia o estomago de um jogador (pra medir a refeicao). SO PRA BANCADA.</summary>
	public void EsvaziarEstomagoDeTeste(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Ficha.CurrentNutrition = 0;
	}

	/// <summary>Quanto ha no estomago de um jogador (-1 = nao existe). SO PRA BANCADA.</summary>
	public double NutricaoDeTeste(int id) => _players.TryGetValue(id, out ServerPlayer? pl) ? pl.Ficha.CurrentNutrition : -1;
}
