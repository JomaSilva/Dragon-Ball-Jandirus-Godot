using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DA NAVE (`--naveteste`).
///
/// ============================ O QUE SO DAQUI SE VE ============================
/// A bancada `nave` do AssetPipeline prova as CONTAS (preco, espera, viagem, teto). Ela nao pode
/// provar a CORRENTE, que e o que a spec pediu -- *"bancada que constroi, embarca, lanca, viaja e
/// pousa"* -- e que e onde este port ja quebrou muitas vezes:
///
///     fabricar -> assentar -> embarcar -> a velocidade MUDAR de verdade -> lancar -> subir pelo
///     `Decolar` de producao -> pousar pelo `TickDoEspaco` -> e a nave chegar junto.
///
/// Cada passo abaixo chama o MESMO metodo que o jogador chama (`ComandoDeTech`,
/// `ComandoDeInteracao`, `TickDasNaves`, `TickDoEspaco`). Uma bancada que montasse a `Nave` na mao
/// mediria o atalho, e nao o jogo -- e o `DecolarDeProcedural`, que existiu publico e sem chamador
/// nenhum, e o precedente de por que isso importa.
/// ==============================================================================
///
/// ============================ E ELA PROVA O QUE A `Obra` TAMBEM PRECISAVA ============================
/// O passo 3 poe tres naves em tres zonas que tem o MESMO NOME em duas delas (dois planetas gerados
/// homonimos, seeds diferentes -- o `% 1000` do `SistemaSolar.Planeta` faz isso acontecer de
/// verdade) e afirma que cada uma so aparece na sua. Este teste **reprovava a `Obra`**, que filtrava
/// por `o.Zona == zona.Name`; a `Obra` passou a guardar a `ZoneKey` inteira (ver `Obra.ZonaTipo`) e
/// hoje as duas usam a mesma chave.
/// ============================================================================================
///
///     Godot --headless -- --server --naveteste
/// </summary>
public partial class GameServer
{
	private bool _naveDeTeste;

	private void RodarBancadaDaNave(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA AO VIVO DA NAVE (SPACEPOD) =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		// O ESTADO DE ANTES, pra devolver no fim. Esta bancada MEXE no personagem vivo do dono
		// (zeni, tecnologia, zona, posicao) -- e por isso ela so roda com a flag.
		ZoneKey zonaAntes = pl.Zone;
		Vec2 posAntes = pl.Pos;
		double zeniAntes = pl.Ficha.Zeni, techAntes = pl.Ficha.techskill;
		bool voavaAntes = pl.Voando;

		try
		{
			// ---------------------------------------------------------------- 1. FABRICAR
			pl.Ficha.techskill = 60;
			pl.Ficha.Zeni = 3_000_000;
			int naquela = _naves.Count;

			ComandoDeTech(pl, "construir", "Spacepod");
			Checa("fabricar poe a Spacepod na mochila (aba Tech, 100.000z / tech 40)",
				  pl.Mochila.Quantos("Spacepod") > 0, $"mochila: {pl.Mochila.Quantos("Spacepod")}");
			Checa("e cobrou o preco do catalogo",
				  pl.Ficha.Zeni <= 3_000_000 - 100_000, $"zeni {pl.Ficha.Zeni:N0}");

			// ---------------------------------------------------------------- 2. ASSENTAR
			// PELO VERBO, com a posicao do proprio corpo: e o mesmo caminho do fantasma no mouse.
			ComandoDeTech(pl, "posicionar", $"Spacepod/{pl.Pos.X:0}/{pl.Pos.Y:0}");
			Checa("assentar cria uma NAVE (e nao uma Obra)", _naves.Count == naquela + 1,
				  $"naves: {_naves.Count}, obras na zona: {_noChao.Count(o => o.Zona.Equals(pl.Zone))}");

			Nave? n = NavePerto(pl);
			Checa("e ela esta ao alcance da mao, na zona certa", n != null && n.Zona.Equals(pl.Zone),
				  n == null ? "nao achei nave por perto" : $"zona da nave: {n.Zona}");
			if (n == null) { GD.PrintErr("  bancada interrompida: sem nave."); return; }

			// ---------------------------------------------------------------- 3. A CHAVE DE ZONA
			// DOIS MUNDOS GERADOS HOMONIMOS -- o caso que a `Obra` nao sabe distinguir. Eles nao
			// precisam existir de verdade: o que esta sob teste e o CRIVO (`NavesParadasEm`), que
			// compara a `ZoneKey` inteira.
			var geradoA = ZoneKey.Procedural("Verdejante-1042", 111);
			var geradoB = ZoneKey.Procedural("Verdejante-1042", 222);
			var forjadas = new List<Nave>();
			foreach (ZoneKey z in new[] { geradoA, geradoB })
			{
				var f = new Nave { Id = _proximaNaveId++, Tipo = "Spacepod", X = 100, Y = 100 };
				f.PorZona(z);
				_naves.Add(f);
				forjadas.Add(f);
			}

			Checa("duas naves em mundos gerados HOMONIMOS nao se misturam",
				  NavesParadasEm(geradoA).Count == 1 && NavesParadasEm(geradoB).Count == 1,
				  $"A: {NavesParadasEm(geradoA).Count}, B: {NavesParadasEm(geradoB).Count}");
			Checa("...e nenhuma delas vaza pra zona onde eu estou",
				  NavesParadasEm(pl.Zone).Count == 1, $"aqui: {NavesParadasEm(pl.Zone).Count}");

			// A PROVA POR CONTRASTE: e o filtro da `Obra` (nome puro) aplicado aos MESMOS dados.
			int comoAObraFiltraria = _naves.Count(x => x.ZonaNome == geradoA.Name);
			Checa("e o filtro por NOME (o da `Obra`) juntaria as duas -- por isso ela nao serve",
				  comoAObraFiltraria == 2, $"por nome: {comoAObraFiltraria}");

			foreach (Nave f in forjadas) _naves.Remove(f);

			// ---------------------------------------------------------------- 4. EMBARCAR
			float velANdando = pl.SpeedStat;
			ComandoDeInteracao(pl, "nave_usar", "");
			Checa("embarcar poe o piloto na nave", n.PilotoId == pl.Id, $"piloto: {n.PilotoId}");
			Checa("pilotar conta como voo (`pilot.flight = 1`, PlanetTech.dm:135)", pl.Voando);
			Checa("a nave parada some da lista da zona (`density = 0`, :137)",
				  NavesParadasEm(pl.Zone).Count == 0, $"{NavesParadasEm(pl.Zone).Count} ainda paradas");
			Checa("e o bit de pilotagem viaja no snapshot", EstaPilotando(pl.Id));

			// ============================ E A TECLA E ALCANCA O PROPRIO VEICULO ============================
			// O pod some da lista de construcoes ao ser pilotado (a linha logo acima prova isso), e era
			// exatamente por isso que os comandos dele viviam na aba Nav: sem alvo no chao, a tecla E
			// nao tinha o que oferecer -- nem "sair". Agora o alvo e o veiculo, e ele vem do servidor.
			//
			// SAO DUAS TABELAS DIFERENTES de proposito: a de fora oferece Entrar e Pegar, a de dentro
			// oferece Sair e Lancar. `nave_lancar` no menu do pod PARADO nunca funcionou -- quem esta
			// de pe ao lado nao e `NaveDoPiloto` e o verbo responde "ninguém está pilotando". Era um
			// botao inalcancavel em estado util, e as duas linhas abaixo o mantem no lugar certo.
			// ===========================================================================================
			Interacoes.Acao[] montado = Interacoes.DoVeiculo("Spacepod");
			Checa("pilotando, o menu do VEICULO existe (a tecla E tem alvo)", montado.Length > 0,
				  $"{montado.Length} acao(oes)");
			Checa("...e ele tem como DESCER (`nave_usar`)", montado.Any(a => a.Verbo == "nave_usar"));
			Checa("...e e nele que mora o `nave_lancar`", montado.Any(a => a.Verbo == "nave_lancar"));
			Checa("e o menu do pod PARADO nao oferece mais lancar (nunca funcionou de fora)",
				  !Interacoes.De("Spacepod").Any(a => a.Verbo == "nave_lancar"));
			Checa("...mas continua oferecendo ENTRAR e PEGAR, que so fazem sentido de fora",
				  Interacoes.De("Spacepod").Any(a => a.Verbo == "nave_usar")
				  && Interacoes.De("Spacepod").Any(a => a.Verbo == "nave_pegar"));

			// O ALCANCE E UM NUMERO SO -- ver `Interacoes.Alcance`. Ele era copiado em tres casas com
			// dois valores, e a que divergia (o console da ponte, 48) abria uma banda morta de 16 px.
			Checa("o alcance do menu e o mesmo que o servidor aceita (um numero so)",
				  Math.Abs(Interacoes.Alcance - AlcanceDeUso) < 1e-6,
				  $"menu {Interacoes.Alcance:0}, servidor {AlcanceDeUso:0}");

			// ---------------------------------------------------------------- 5. VELOCIDADE
			// O DEGRAU 1 NAO ACELERA (e o pod do DM). O que prova o sistema e o upgrade.
			Checa("no degrau 1 a nave nao deixa ninguem mais lento",
				  pl.SpeedStat >= velANdando - 1e-4, $"{velANdando:0.00} -> {pl.SpeedStat:0.00}");

			int degraus = 0;
			while (n.Velocidade < Naves.VelocidadeMaxima && degraus < Naves.VelocidadeMaxima + 5)
			{ ComandoDeInteracao(pl, "nave_melhorar", ""); degraus++; }

			Checa($"o upgrade sobe ate o teto do DM ({Naves.VelocidadeMaxima}x)",
				  n.Velocidade == Naves.VelocidadeMaxima, $"velocidade {n.Velocidade}");
			// A RECUSA NO TETO (com o zeni intacto e a frase dita) e medida na CAMADA 4 -- havia aqui
			// uma segunda linha afirmando `n.Velocidade == VelocidadeMaxima` de novo e se chamando "o
			// teto DISPARA". Ela nao tentava melhorar, nao olhava o dinheiro e nao olhava o aviso: era
			// a mesma afirmacao duas vezes. Ver `OTetoQueDispara`.
			Checa("e a velocidade do CORPO passou a ser a da nave",
				  Math.Abs(pl.SpeedStat - Naves.VelocidadeMaxima) < 1e-3, $"SpeedStat {pl.SpeedStat:0.00}");

			double px = MoveRules.SpeedPx(pl.SpeedStat);
			GD.Print($"       velocidade medida: {px:N0} px/s "
					 + $"({Naves.PassoPorTique(n.Velocidade):0} px por tique a 30 Hz)");
			Checa("e ela cabe no teto da amostragem -- o pouso nao fura",
				  Naves.PassoPorTique(n.Velocidade) < 300, $"{Naves.PassoPorTique(n.Velocidade):0} px/tique");

			// ---------------------------------------------------------------- 6. LANCAR
			// Do chao de um planeta que existe no mapa do universo. Se a bancada rodar em outro
			// lugar (Outro Mundo, Sala do Tempo), o proprio `Decolar` recusa -- e e o certo.
			bool daquiSobe = Espaco.PreFeitos().Any(p =>
				string.Equals(p.Nome, pl.Zone.Name, StringComparison.OrdinalIgnoreCase))
				|| EhZonaProcedural(pl.Zone);

			if (!daquiSobe) GD.Print($"       (pulando o lancamento: nao da pra decolar de {pl.Zone.Name})");
			else
			{
				ComandoDeInteracao(pl, "nave_lancar", "");
				Checa("lancar arma o relogio do `sleep(400/Speed)`", n.LancaEm > 0);

				// O RELOGIO E ADIANTADO, e nao esperado: a bancada roda dentro de um tique. O que
				// esta sob teste e o que acontece QUANDO ele vence, e nao a passagem do tempo.
				n.LancaEm = NowMs() - 1;
				TickDasNaves();

				Checa("o lancamento sobe pelo `Decolar` de producao", Espaco.EhEspaco(pl.Zone),
					  $"zona: {pl.Zone}");
				Checa("e a nave subiu junto", n.Zona.Equals(pl.Zone), $"nave em {n.Zona}");
			}

			// ---------------------------------------------------------------- 7. VIAJAR E POUSAR
			if (Espaco.EhEspaco(pl.Zone))
			{
				PlanetaNoEspaco namek = Espaco.PreFeitos().First(p => p.Nome == "Namek");
				PlanetaNoEspaco terra = Espaco.PreFeitos().First(p => p.Nome == "Earth");
				double d = (namek.Pos - terra.Pos).Length;

				GD.Print($"       Terra->Namek: {d:N0} px  |  a pe {Espaco.SegundosDeViagem(d) / 60:0} min  |  "
						 + $"nesta nave {Naves.SegundosDeViagem(d, n.Velocidade) / 60:0.0} min");
				Checa("a nave corta a viagem em mais de dez vezes",
					  Espaco.SegundosDeViagem(d) / Naves.SegundosDeViagem(d, n.Velocidade) > 10);

				// O CORPO E POSTO SOBRE O DISCO e o tique de producao faz o resto. Andar o milhao e
				// meio de pixels a 30 Hz levaria os quatro minutos de verdade -- o que esta sob
				// teste e o POUSO, e ele e uma pergunta por posicao.
				pl.Pos = namek.Pos;
				TickDoEspaco(pl);
				TickDasNaves();

				Checa("encostar em Namek pousa (o `TickDoEspaco` de producao)",
					  pl.Zone.Kind == ZoneKey.KindPremade && pl.Zone.Name == "Namek", $"zona: {pl.Zone}");
				Checa("e a nave desceu junto -- sem um segundo caminho de pouso",
					  n.Zona.Equals(pl.Zone), $"nave em {n.Zona}");
			}

			// ---------------------------------------------------------------- 8. DESEMBARCAR
			ComandoDeInteracao(pl, "nave_usar", "");
			Checa("desembarcar solta o piloto", n.PilotoId == 0);
			Checa("e devolve o voo ao que era antes (`pod_had_flight`, :53)", pl.Voando == voavaAntes,
				  $"voando: {pl.Voando}, era: {voavaAntes}");
			Checa("a nave volta a ser um objeto no chao da zona onde se desceu",
				  NavesParadasEm(pl.Zone).Count == 1, $"{NavesParadasEm(pl.Zone).Count} paradas aqui");
			Checa("...e a velocidade do corpo voltou a ser a do corpo",
				  pl.SpeedStat < Naves.VelocidadeMaxima, $"SpeedStat {pl.SpeedStat:0.00}");

			// ---------------------------------------------------------------- 9. LIMPEZA
			_naves.Remove(n);
			GravarNaves();
		}
		finally
		{
			// DEVOLVE O PERSONAGEM. Uma bancada que deixa o corpo do dono em Namek com tres milhoes
			// de zeni nao e uma bancada, e um estrago.
			pl.Ficha.Zeni = zeniAntes;
			pl.Ficha.techskill = techAntes;
			pl.Voando = voavaAntes;
			pl.Mochila.Tirar("Spacepod");
			if (!pl.Zone.Equals(zonaAntes)) MoveToZone(pl.Id, zonaAntes, posAntes);
			else pl.Pos = posAntes;
			RecalcularVelocidade(pl);
			MandarFicha(pl);
			MandarMochila(pl);
		}

		// ============================ AS CAMADAS 2 E 3, NO MESMO CONTADOR ============================
		// DEPOIS do `finally` acima, e nao dentro do `try`: a camada 1 tem que ter devolvido o corpo do
		// dono ao lugar antes de a proxima comecar a mexer nele. Uma bancada que rodasse aninhada
		// deixaria o personagem em Namek se a de cima estourasse no meio.
		//
		// O CONTADOR E O MESMO de proposito: "26 OK, 0 FALHA" e uma frase sobre A NAVE, e nao sobre um
		// arquivo. Tres somatorios separados fariam alguem ler dois e achar que leu tudo.
		RodarBancadaDaNaveGrande(pl, Checa);
		RodarBancadaDoFoguete(pl, Checa);

		// A CAMADA 4 nao empresta o corpo do dono -- ela forja os seus. Ver o cabecalho de
		// `GameServer.NaveViagemTeste.cs`: ela VIAJA, e levar o personagem de alguem numa travessia de
		// estrela e um jeito de a bancada matar quem a rodou.
		RodarBancadaDaViagem(pl, Checa);

		GD.Print($"===== BANCADA DA NAVE: {ok} OK, {falhou} FALHA(S) =====\n");
	}
}
