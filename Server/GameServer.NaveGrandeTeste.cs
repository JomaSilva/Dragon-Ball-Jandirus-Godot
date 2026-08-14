using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DA CAMADA 2 -- roda logo depois da camada 1, na mesma flag `--naveteste`.
///
/// ============================ O QUE SO DAQUI SE VE ============================
/// A bancada `nave` do AssetPipeline prova a PLANTA (que o casco fecha, que o console e parede, que
/// ha um vao pra ponte) e os NUMEROS (o casco, a espera, a janela do foguete). Ela nao pode provar a
/// CORRENTE, que e onde este port ja quebrou muitas vezes:
///
///     fabricar -> assentar -> EMBARCAR NUMA ZONA QUE NAO EXISTIA -> a colisao daquela zona valer ->
///     achar a ponte -> assumir o leme -> a nave seguir o corpo -> largar o leme e VOLTAR pra dentro ->
///     sair pela plataforma -> e, no fim, a nave ser DESTRUIDA com gente dentro.
///
/// O ultimo passo e o que a camada 2 mais precisa de prova: a ejecao e um caminho que so roda uma vez
/// na vida de cada nave, e um caminho que roda uma vez e o que fica quebrado calado por mais tempo.
///
/// Cada passo abaixo chama o MESMO metodo que o jogador chama (`ComandoDeTech`, `ComandoDeInteracao`,
/// `TickDasNaves`, `SocarCenario`). Uma bancada que montasse a `Nave` na mao mediria o atalho.
/// ==============================================================================
/// </summary>
public partial class GameServer
{
	private void RodarBancadaDaNaveGrande(ServerPlayer pl, Action<string, bool, string> checa)
	{
		void Checa(string nome, bool cond, string detalhe = "") => checa(nome, cond, detalhe);

		GD.Print("\n----- CAMADA 2: A CAPITAL SHIP -----");

		ZoneKey zonaAntes = pl.Zone;
		Vec2 posAntes = pl.Pos;
		var forjados = new List<ServerPlayer>();
		Nave? nave = null;

		try
		{
			pl.Ficha.techskill = 60;
			pl.Ficha.Zeni = 5_000_000;

			// ---------------------------------------------------------------- 1. FABRICAR E ASSENTAR
			int antes = _naves.Count;
			ComandoDeTech(pl, "construir", NaveGrande.Tipo);
			Checa("fabricar poe a Capital Ship na mochila (2.000.000z / tech 55)",
				  pl.Mochila.Quantos(NaveGrande.Tipo) > 0, $"mochila: {pl.Mochila.Quantos(NaveGrande.Tipo)}");

			ComandoDeTech(pl, "posicionar", $"{NaveGrande.Tipo}/{pl.Pos.X:0}/{pl.Pos.Y:0}");
			Checa("assentar cria uma NAVE (e nao uma Obra)", _naves.Count == antes + 1, $"naves: {_naves.Count}");

			nave = NavePerto(pl);
			if (nave == null) { GD.PrintErr("  bancada da camada 2 interrompida: sem nave."); return; }

			// O CASCO NASCE DO BP DE QUEM ERGUEU. `expressedBP` de um personagem novo e pequeno (a
			// armadilha 6 da PARTE 3), entao o que se espera aqui e o PISO -- e e exatamente por isso
			// que o piso existe.
			double esperado = NaveGrande.ArmaduraMaxima(pl.Ficha.expressedBP);
			Checa("o casco nasce de `max(expressedBP*5, 1000)`",
				  Math.Abs(nave.ArmaduraMax - esperado) < 1e-6 && Math.Abs(nave.Armadura - esperado) < 1e-6,
				  $"casco {nave.Armadura:N0}/{nave.ArmaduraMax:N0}, esperado {esperado:N0}");

			// ---------------------------------------------------------------- 2. EMBARCAR
			ZoneKey dentro = NaveGrande.ZonaDoInterior(nave.Id);
			ComandoDeInteracao(pl, "nave_embarcar", "");
			Checa("embarcar leva o corpo pra uma zona que nao existia um segundo atras",
				  pl.Zone.Equals(dentro), $"zona: {pl.Zone}");
			Checa("...e ela e o interior DESTA nave, e nao de outra",
				  NaveDesteInterior(pl)?.Id == nave.Id);

			// A COLISAO E O QUE FAZ O CASCO SEGURAR. Sem esta linha o `MapaDaZonaOuCatalogo` devolveria
			// nulo e o `ValidateStep` aprovaria qualquer passo -- da pra sair da nave andando pelo vazio.
			ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
			Checa("a zona do interior TEM colisao (senao o casco nao segura ninguem)",
				  mapa != null && mapa.Width == NaveGrande.Lado, $"mapa: {mapa?.Width ?? 0}");
			Checa("e o casco esta fechado no mapa que o SERVIDOR usa",
				  mapa != null && mapa.BlockedCell(0, 50) && mapa.BlockedCell(NaveGrande.Lado - 1, 50));
			Checa("o corpo chegou em chao livre, um tile ao sul da plataforma",
				  mapa != null && !MoveRules.Occupied(mapa, pl.Pos), $"pos {pl.Pos.X:0},{pl.Pos.Y:0}");

			// AS DUAS PECAS. Elas nao sao guardadas em lugar nenhum -- sao derivadas da planta na hora.
			var mobilia = MobiliaDoInterior(dentro).ToList();
			Checa("o pacote de construcoes do interior leva as DUAS pecas fixas",
				  mobilia.Count == 2, $"{mobilia.Count} peca(s)");
			Checa("...com ids fora do intervalo das naves (senao uma apagaria a outra na tela)",
				  mobilia.All(m => m.Id < -1000), $"ids: {string.Join(",", mobilia.Select(m => m.Id))}");
			Checa("e o menu da tecla E conhece as duas",
				  Interacoes.Interativo("Ship_Control") && Interacoes.Interativo("Ship_Pad"));

			// ...E ELAS TEM NOME EM PORTUGUES. O catalogo do cliente vem do `Ofertas`, que esconde
			// mobilia de mapa (custo negativo): sem a linha no `construcoes.json` o menu caia no
			// `tipo.Replace('_',' ')` e o jogador lia "Ship Control" e "Ship Pad", em ingles. O nome
			// viaja no pacote de construcoes desde entao -- ver `ObraInfo.Nome`.
			Checa("...e as duas tem NOME no catalogo (senao o menu as chama em ingles)",
				  _obras?.Get("Ship_Control")?.Nome is { Length: > 0 }
				  && _obras?.Get("Ship_Pad")?.Nome is { Length: > 0 },
				  $"console: \"{_obras?.Get("Ship_Control")?.Nome ?? "-"}\", "
				  + $"plataforma: \"{_obras?.Get("Ship_Pad")?.Nome ?? "-"}\"");

			// ---------------------------------------------------------------- 3. CONSTRUIR DENTRO E RECUSADO
			// ============================ A RECUSA FICA; O MOTIVO DELA MUDOU ============================
			// Aqui estava escrito "a `Obra` filtra a zona por NOME PURO, e o nome do interior de TODA nave
			// e 'Nave'". **Isso deixou de ser verdade**: a `Obra` guarda as tres partes da `ZoneKey` (ver
			// `Obra.ZonaTipo`) e a secao 5 da `--obrateste` prova que o interior da #7 e o da #8 sao zonas
			// distintas. Deixar a frase de pe faria a proxima pessoa "consertar" um defeito que ja morreu.
			//
			// O QUE SUSTENTA A RECUSA HOJE E O CICLO DE VIDA, e nao a chave: a nave se recolhe e se
			// destroi, e o `mundo.json` nao sabe disso -- a bancada erguida dentro dela ficaria de pe pra
			// sempre numa zona onde ninguem mais entra. Ver o comentario no `Posicionar`.
			// ========================================================================================
			int obrasAntes = _noChao.Count;
			ComandoDeTech(pl, "construir", "Research_Station");
			ComandoDeTech(pl, "posicionar", $"Research_Station/{pl.Pos.X:0}/{pl.Pos.Y:0}");
			Checa("construir DENTRO da nave e recusado (a nave morre, e a obra ficaria presa la)",
				  _noChao.Count == obrasAntes, $"obras: {obrasAntes} -> {_noChao.Count}");
			pl.Mochila.Tirar("Research_Station");

			// ---------------------------------------------------------------- 4. A PONTE
			Checa("longe do console, a ponte nao responde", NaveDaPonteDe(pl) == null);
			ComandoDeInteracao(pl, "nave_pilotar", "");
			Checa("...e o verbo de pilotar recusa com o motivo, em vez de calar",
				  nave.PilotoId == 0, $"piloto: {nave.PilotoId}");

			// ANDA ATE A PONTE -- um tile ao SUL do console, que e onde o corpo cabe (o console e parede).
			pl.Pos = NaveGrande.PixelDe((NaveGrande.CelDoConsole.X, NaveGrande.CelDoConsole.Y + 1));
			Checa("no console, a ponte responde", NaveDaPonteDe(pl)?.Id == nave.Id);

			// ============================ A BANDA MORTA DE 16 PX ============================
			// O console media 48 px (`AlcanceDaPeca`) e o menu do cliente abre a 64 (`Interacoes.
			// Alcance`). No meio havia uma faixa em que o menu ABRIA e o verbo era RECUSADO -- e duas
			// celulas ortogonais dao exatamente 64 px, entao dava pra cair nela andando reto.
			//
			// A PROVA E NA BORDA, e nao "perto": o corpo vai pro limite EXATO que o cliente usa pra
			// desenhar o botao. Se um dia os dois numeros se separarem de novo, esta linha cai.
			Vec2 console = NaveGrande.PixelDe(NaveGrande.CelDoConsole);
			pl.Pos = new Vec2(console.X + Interacoes.Alcance, console.Y);
			Checa("no limite EXATO do alcance do menu, o console ainda responde (sem banda morta)",
				  NaveDaPonteDe(pl)?.Id == nave.Id,
				  $"a {Interacoes.Alcance:0} px do console");
			pl.Pos = new Vec2(console.X + Interacoes.Alcance + 1, console.Y);
			Checa("...e um pixel alem dele, nao (o alcance ainda e um corte, e nao a sala inteira)",
				  NaveDaPonteDe(pl) == null);
			pl.Pos = NaveGrande.PixelDe((NaveGrande.CelDoConsole.X, NaveGrande.CelDoConsole.Y + 1));

			ComandoDeInteracao(pl, "nave_observar", "");
			Checa("observar nao explode fora do espaco (a nave esta pousada)", true);

			// ---------------------------------------------------------------- 5. O LEME
			float velANdando = pl.SpeedStat;
			ComandoDeInteracao(pl, "nave_pilotar", "");
			Checa("assumir o leme poe o corpo do lado de FORA, junto do casco",
				  pl.Zone.Equals(nave.Zona) && !NaveGrande.EhInterior(pl.Zone, out _), $"zona: {pl.Zone}");
			Checa("...e marca o piloto", nave.PilotoId == pl.Id, $"piloto: {nave.PilotoId}");
			Checa("...e o bit que escolhe o SPRITE grande viaja no snapshot", PilotaNaveGrande(pl.Id));
			Checa("pilotar conta como voo (`pilot.flight = 1`)", pl.Voando);
			Checa("e o leme nao deixa ninguem mais lento", pl.SpeedStat >= velANdando - 1e-4,
				  $"{velANdando:0.00} -> {pl.SpeedStat:0.00}");

			// ============================ O PEDIDO DO DONO: E DE NOVO VOLTA PRA DENTRO ============================
			// Ao leme o corpo esta noutra ZONA que o console -- nao ha objeto nenhum ao alcance da
			// tecla E. O alvo passa a ser o VEICULO, e e a tabela dele que oferece a volta.
			//
			// A BANCADA MEDE A TABELA, e nao o desenho: quem desenha e o cliente, mas o que ele desenha
			// sai daqui. Uma pagina vazia aqui seria um piloto sem botao nenhum, que e o buraco que a
			// aba Nav tapava.
			// ==================================================================================================
			Interacoes.Acao[] doLeme = Interacoes.DoVeiculo(NaveGrande.Tipo);
			Checa("ao leme, o menu do VEICULO existe (a tecla E tem alvo)", doLeme.Length > 0,
				  $"{doLeme.Length} acao(oes)");
			Checa("...e a primeira delas e voltar pra ponte (`nave_pilotar`)",
				  doLeme.Length > 0 && doLeme[0].Verbo == "nave_pilotar",
				  doLeme.Length > 0 ? doLeme[0].Rotulo : "-");
			Checa("...e ele NAO oferece Observar, que e do console e recusaria daqui",
				  !doLeme.Any(a => a.Verbo == "nave_observar"));

			// A NAVE SEGUE O CORPO -- o mesmo `TickDasNaves` do pod, sem um segundo motor de veiculo.
			pl.Pos += new Vec2(96, 0);
			TickDasNaves();
			Checa("a nave copia a posicao do piloto no tique de producao",
				  Math.Abs(nave.X - pl.Pos.X) < 1e-3 && Math.Abs(nave.Y - pl.Pos.Y) < 1e-3,
				  $"nave ({nave.X:0},{nave.Y:0}) x corpo ({pl.Pos.X:0},{pl.Pos.Y:0})");

			// LARGAR O LEME devolve o corpo pra DENTRO, ao lado do console -- e nao pra plataforma.
			ComandoDeInteracao(pl, "nave_pilotar", "");
			Checa("largar o leme devolve o corpo pra ponte", pl.Zone.Equals(dentro), $"zona: {pl.Zone}");
			Checa("...ao lado do console, e nao dentro dele",
				  MapaDaZonaOuCatalogo(pl.Zone) is { } m2 && !MoveRules.Occupied(m2, pl.Pos));
			Checa("...e o leme fica livre pro proximo", nave.PilotoId == 0);

			// ---------------------------------------------------------------- 6. LANCAR DA PONTE
			bool daquiSobe = Espaco.PreFeitos().Any(p =>
				string.Equals(p.Nome, nave.Zona.Name, StringComparison.OrdinalIgnoreCase))
				|| EhZonaProcedural(nave.Zona);

			if (!daquiSobe) GD.Print($"       (pulando o lancamento: nao da pra decolar de {nave.Zona.Name})");
			else
			{
				pl.Pos = NaveGrande.PixelDe((NaveGrande.CelDoConsole.X, NaveGrande.CelDoConsole.Y + 1));
				ComandoDeInteracao(pl, "nave_lancar", "");
				Checa("lancar da ponte arma o relogio de 10 s", nave.LancaEm > 0);

				// SEM NINGUEM AO LEME: e o caso do DM (`do_launch` nao olha `pilot_mob`), e o unico
				// caminho de subida que nao passa pelo `Decolar` -- ver `SubirNaveSozinha`.
				nave.LancaEm = NowMs() - 1;
				TickDasNaves();
				Checa("a nave sobe SEM piloto, e quem esta dentro NAO muda de zona",
					  Espaco.EhEspaco(nave.Zona) && pl.Zone.Equals(dentro),
					  $"nave em {nave.Zona}, eu em {pl.Zone}");

				// E SAIR AGORA CUSPE NO ESPACO, e nao no planeta de onde se embarcou. E a frase que
				// torna a nave um veiculo: "voce sai onde ela ESTIVER".
				pl.Pos = NaveGrande.PixelDe(NaveGrande.CelDaPlataforma);
				ComandoDeInteracao(pl, "nave_sair", "");
				Checa("sair pela plataforma cospe onde a nave ESTA agora (o espaco), e nao onde se embarcou",
					  Espaco.EhEspaco(pl.Zone), $"zona: {pl.Zone}");

				// E O SOL CONTINUA VALENDO PRA QUEM SAIU: o interior nao e espaco, entao o
				// `TickDoEspaco` (e com ele o `TickDoSol`) nem roda la dentro. Quem esta fora queima.
				Checa("o interior NAO e espaco -- ninguem queima dentro do casco",
					  !Espaco.EhEspaco(dentro));

				// volta pra dentro pro resto da bancada
				ComandoDeInteracao(pl, "nave_embarcar", "");
				if (!pl.Zone.Equals(dentro)) MoveToZone(pl.Id, dentro, NaveGrande.PixelDe(NaveGrande.CelDaChegada));
			}

			// ---------------------------------------------------------------- 7. PEGAR COM GENTE DENTRO
			Checa("com gente dentro, a nave conta a tripulacao", QuantosDentro(nave) >= 1,
				  $"{QuantosDentro(nave)} dentro");

			// ---------------------------------------------------------------- 8. A DESTRUICAO
			// O CASO QUE MAIS DA ERRADO CALADO. Dois corpos dentro (eu e um forjado), um deles no
			// leme, e o casco cede: ninguem pode ficar pra tras numa zona que deixou de existir.
			ServerPlayer? passageiro = ForjarPassageiro(dentro);
			if (passageiro != null) forjados.Add(passageiro);

			int aBordoAntes = QuantosDentro(nave);
			Checa("dois corpos a bordo pra o teste da ejecao", aBordoAntes >= (passageiro != null ? 2 : 1),
				  $"{aBordoAntes} dentro");

			// UM DELES ASSUME O LEME: e a combinacao que mais quebra -- um corpo FORA marcado como
			// piloto de uma nave que vai deixar de existir.
			pl.Pos = NaveGrande.PixelDe((NaveGrande.CelDoConsole.X, NaveGrande.CelDoConsole.Y + 1));
			ComandoDeInteracao(pl, "nave_pilotar", "");
			bool noLeme = nave.PilotoId == pl.Id;
			Checa("um piloto ao leme na hora da explosao", noLeme);

			ZoneKey ondeCaiu = nave.Zona;
			int idDaNave = nave.Id;

			// O DONO NAO DERRUBA A PROPRIA NAVE (`ShipVessel.dm:243-245`). Provar a RECUSA antes de
			// provar a queda: sem isso, "a nave caiu" nao distingue as duas regras.
			double cascoAntes = nave.Armadura;
			EstragarNave(nave, nave.ArmaduraMax * 10, pl);
			Checa("o dono nao derruba a propria nave", Math.Abs(nave.Armadura - cascoAntes) < 1e-9,
				  $"casco {nave.Armadura:N0}");

			// UM GOLPE FRACO NAO ARRANHA -- a superarmadura (`piso = 0,75*maxarmor`).
			EstragarNave(nave, nave.ArmaduraMax * 0.5, null);
			Checa("um golpe abaixo de 75% do casco nao arranha (superarmadura)",
				  Math.Abs(nave.Armadura - cascoAntes) < 1e-9, $"casco {nave.Armadura:N0}");

			// E O GOLPE QUE DERRUBA. `autor` nulo = "nao ha algoz": e o mesmo caminho do corpo
			// arremessado contra uma obra.
			EstragarNave(nave, nave.ArmaduraMax * 5, null);

			Checa("o casco cedeu e a nave saiu do mundo", _naves.All(x => x.Id != idDaNave));
			Checa("NINGUEM ficou dentro de uma zona que deixou de existir",
				  ZoneList(dentro.Hash).Count == 0, $"{ZoneList(dentro.Hash).Count} preso(s) la dentro");
			Checa("...todos foram parar na zona onde o casco estava",
				  pl.Zone.Equals(ondeCaiu) && (passageiro == null || passageiro.Zone.Equals(ondeCaiu)),
				  $"eu em {pl.Zone}, passageiro em {passageiro?.Zone.ToString() ?? "-"}");
			Checa("...e nao empilhados no MESMO pixel",
				  passageiro == null || (passageiro.Pos - pl.Pos).Length > 1f,
				  passageiro == null ? "" : $"distancia {(passageiro.Pos - pl.Pos).Length:0.0} px");
			if (noLeme)
				Checa("o piloto foi solto do leme antes de a nave sumir (senao o tique procuraria uma nave morta)",
					  !EstaPilotando(pl.Id));

			// ---------------------------------------------------------------- 9. O RESGATE DO RELOG
			// Quem deslogou dentro e voltou depois da explosao. O save guarda a `ZoneKey` inteira, e
			// sem esta guarda ele acordaria numa sala sem saida. A guarda de zona morta que ja existia
			// no login so olha `KindPremade`.
			ZoneKey guardada = pl.Zone;
			Vec2 pgc = pl.Pos;
			pl.Zone = dentro;
			ResgatarDeInteriorMorto(pl);
			Checa("quem volta pra dentro de uma nave destruida e devolvido ao berco",
				  !NaveGrande.EhInterior(pl.Zone, out _), $"zona: {pl.Zone}");
			MoveToZone(pl.Id, guardada, pgc);

			nave = null;   // ja saiu do mundo
		}
		finally
		{
			foreach (ServerPlayer f in forjados) RemoverPassageiro(f);
			if (nave != null) _naves.Remove(nave);
			pl.Mochila.Tirar(NaveGrande.Tipo);
			GravarNaves();
			if (!pl.Zone.Equals(zonaAntes)) MoveToZone(pl.Id, zonaAntes, posAntes);
			else pl.Pos = posAntes;
			RecalcularVelocidade(pl);
			MandarFicha(pl);
			MandarMochila(pl);
		}
	}

	/// <summary>
	/// UM CORPO A MAIS DENTRO DA NAVE, so pra a ejecao ter mais de uma pessoa pra tirar.
	///
	/// Nasce pela FABRICA DE NPC de producao (`NascerNpc`) e nao a mao, pelo mesmo motivo de sempre:
	/// um `new ServerPlayer` aqui testaria o atalho. Devolve nulo quando nao ha molde que possa
	/// nascer -- e ai a bancada mede a ejecao com uma pessoa so e diz isso em voz alta.
	/// </summary>
	private ServerPlayer? ForjarPassageiro(ZoneKey dentro)
	{
		if (_moldes == null) return null;

		// ============================ ELE NASCE NA TERRA E DEPOIS ENTRA ============================
		// `NascerNpc` sorteia a RACA pelo berco do planeta em que o corpo nasce (ver `SorteioDeNpc`), e
		// o interior de uma nave nao e berco de raca nenhuma -- nascer direto la dentro sai com um
		// aviso amarelo e nulo. Nascer num planeta de verdade e ANDAR pra dentro e o que um passageiro
		// faz mesmo, e usa o `MoveToZone` de producao pra isso.
		foreach (MoldeDeNpc m in _moldes.Todos)
		{
			ServerPlayer? npc = NascerNpc(m.Id, ZoneKey.Premade("Earth"), new Vec2(8000, 8000), 4242);
			if (npc == null) continue;
			MoveToZone(npc.Id, dentro, NaveGrande.PixelDe((49, 52)));
			return npc;
		}
		GD.Print("       (sem molde de NPC disponivel: a ejecao sera medida com uma pessoa so)");
		return null;
	}

	private void RemoverPassageiro(ServerPlayer f)
	{
		ZoneList(f.Zone.Hash).Remove(f);
		_players.Remove(f.Id);
	}

	// =====================================================================
	// A CAMADA 3 -- O FOGUETE DE USO UNICO
	// =====================================================================
	/// <summary>
	/// O FOGUETE, DA FABRICA A REENTRADA -- `obj/Rocketship` (`PlanetTech.dm:237-334`).
	///
	/// ============================ O QUE SO DAQUI SE VE ============================
	/// O foguete e a unica nave deste port que faz alguma coisa SOZINHA: ele sobe, espera e desce sem
	/// ninguem apertar nada. Um comportamento com relogio proprio e o tipo de coisa que fica quebrada
	/// calada -- se o prazo nunca vencer, o foguete simplesmente fica em orbita pra sempre e ninguem
	/// tem como notar a diferenca entre "ainda esta subindo" e "o codigo nao roda".
	///
	/// Por isso os prazos sao ADIANTADOS aqui (`n.VoltaEm = NowMs() - 1`) em vez de esperados: o que
	/// esta sob teste e o que acontece QUANDO eles vencem, e nao a passagem do tempo.
	/// ==============================================================================
	/// </summary>
	private void RodarBancadaDoFoguete(ServerPlayer pl, Action<string, bool, string> checa)
	{
		void Checa(string nome, bool cond, string detalhe = "") => checa(nome, cond, detalhe);

		GD.Print("\n----- CAMADA 3: O FOGUETE DE USO UNICO -----");

		ZoneKey zonaAntes = pl.Zone;
		Vec2 posAntes = pl.Pos;
		Nave? n = null;

		try
		{
			pl.Ficha.techskill = 60;
			pl.Ficha.Zeni = 1_000_000;

			ComandoDeTech(pl, "construir", "Rocket_Ship");
			ComandoDeTech(pl, "posicionar", $"Rocket_Ship/{pl.Pos.X:0}/{pl.Pos.Y:0}");
			n = NavePerto(pl);
			Checa("o foguete assenta como NAVE (200.000z / tech 30)", n != null && Naves.EhFoguete(n.Tipo));
			if (n == null) return;

			Checa("ele nasce inteiro (`didland = 0`)", !n.Usada);
			Checa("e sem casco: ele nao participa do sistema de armadura", n.ArmaduraMax <= 0);

			ComandoDeInteracao(pl, "nave_usar", "");
			Checa("entrar no foguete marca o piloto", n.PilotoId == pl.Id);

			bool daquiSobe = Espaco.PreFeitos().Any(p =>
				string.Equals(p.Nome, pl.Zone.Name, StringComparison.OrdinalIgnoreCase))
				|| EhZonaProcedural(pl.Zone);

			if (!daquiSobe)
			{
				GD.Print($"       (pulando a viagem: nao da pra decolar de {pl.Zone.Name})");
				ComandoDeInteracao(pl, "nave_usar", "");
				return;
			}

			ZoneKey origem = pl.Zone;
			Vec2 posDeOrigem = pl.Pos;

			ComandoDeInteracao(pl, "nave_lancar", "");
			Checa("lancar arma o relogio da subida", n.LancaEm > 0);
			Checa("...e guarda DE ONDE ele subiu, com a ZoneKey inteira",
				  n.ZonaDaVolta.Equals(origem), $"volta pra {n.ZonaDaVolta}");

			n.LancaEm = NowMs() - 1;
			TickDasNaves();
			Checa("o foguete sobe pelo `Decolar` de producao", Espaco.EhEspaco(pl.Zone), $"zona: {pl.Zone}");
			Checa("...e ja sobe com a hora da volta marcada (`sleep(150)`)", n.VoltaEm > 0);
			Checa("...e o casco ja conta como GASTO na subida (`didland=1`)", n.Usada);

			// O AVISO DE REENTRADA. Ele sai ANTES da descida, e sem esta prova ele podia nunca sair --
			// um aviso que nao dispara e indistinguivel de aviso nenhum.
			n.VoltaEm = NowMs() + (long)((Naves.SegundosEmOrbita - Naves.SegundosAteAvisarAReentrada) * 1000) - 1;
			TickDasNaves();
			Checa("o aviso de reentrada dispara antes da descida", n.AvisouDaReentrada);

			n.VoltaEm = NowMs() - 1;
			TickDasNaves();
			Checa("o foguete desce sozinho, sem ninguem apertar nada", !Espaco.EhEspaco(pl.Zone),
				  $"zona: {pl.Zone}");
			Checa("...e desce EXATAMENTE na zona de onde subiu", pl.Zone.Equals(origem), $"zona: {pl.Zone}");
			Checa("...em chao livre (a colisao e conferida na DESCIDA, e nao na subida)",
				  MapaDaZonaOuCatalogo(pl.Zone) is not { } m || !MoveRules.Occupied(m, pl.Pos),
				  $"pos {pl.Pos.X:0},{pl.Pos.Y:0}");

			// O USO UNICO. Sem esta recusa, "uso unico" seria so um adjetivo no `desc`.
			ComandoDeInteracao(pl, "nave_lancar", "");
			Checa("um foguete gasto NAO lanca de novo", n.LancaEm == 0);

			// E O RECONDICIONAMENTO O DESTRAVA -- `verb/Fit` (:320-334).
			double zeniAntes = pl.Ficha.Zeni;
			pl.Ficha.Zeni = Naves.CustoDeRecondicionar - 1;
			ComandoDeInteracao(pl, "nave_recondicionar", "");
			Checa("sem zeni, recondicionar e recusado", n.Usada);

			pl.Ficha.Zeni = zeniAntes;
			ComandoDeInteracao(pl, "nave_recondicionar", "");
			Checa("com zeni, ele volta a voar", !n.Usada);
			Checa("...e cobrou os 100.000 do DM",
				  Math.Abs(zeniAntes - pl.Ficha.Zeni - Naves.CustoDeRecondicionar) < 1e-6,
				  $"{zeniAntes:N0} -> {pl.Ficha.Zeni:N0}");

			// ---------------------------------------------------------------- O SALTO EM ORBITA
			// `else ... "Re-entry failure" ... del(src)` (:290-292): quem pula fora perde o foguete.
			// E o unico caminho deste sistema que roda com o veiculo SEM piloto, e ele vinha depois de
			// uma guarda que devolvia cedo -- sem esta prova, um foguete abandonado ficaria em orbita
			// pra sempre com um prazo que ninguem olharia.
			pl.Pos = posDeOrigem;
			ComandoDeInteracao(pl, "nave_lancar", "");
			n.LancaEm = NowMs() - 1;
			TickDasNaves();
			Checa("subiu de novo depois do recondicionamento", n.VoltaEm > 0);

			int idDoFoguete = n.Id;
			ComandoDeInteracao(pl, "nave_usar", "");   // salta fora, em orbita
			Checa("saltar em orbita solta o piloto", n.PilotoId == 0);

			n.VoltaEm = NowMs() - 1;
			TickDasNaves();
			Checa("o foguete abandonado se PERDE na reentrada, em vez de voar pra sempre",
				  _naves.All(x => x.Id != idDoFoguete), $"naves: {_naves.Count}");
			n = null;
		}
		finally
		{
			if (n != null) _naves.Remove(n);
			pl.Mochila.Tirar("Rocket_Ship");
			GravarNaves();
			if (!pl.Zone.Equals(zonaAntes)) MoveToZone(pl.Id, zonaAntes, posAntes);
			else pl.Pos = posAntes;
			RecalcularVelocidade(pl);
			MandarFicha(pl);
			MandarMochila(pl);
		}
	}
}
