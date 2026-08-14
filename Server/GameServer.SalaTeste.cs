using Godot;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DA SALA DO TEMPO -- `--salateste`. **Esta rodada cobre A SALA INTEIRA do lado do
/// servidor**: a porta (13.1), os ganhos (13.4) e a SESSAO (13.5 + 13.6) -- cronometro em dias
/// in-game, envelhecimento, comida e prisao. O vazio procedural em volta tem bancada propria
/// (`--diagvazio`), porque ela e do CLIENTE: quem pinta o branco e quem faz o veu de visao moram
/// do outro lado do fio.
///
/// ============================ AS SECOES 12 A 14 SAO A CAMADA 3 ============================
/// Elas provam o que so existe com o tempo correndo: que a conta anda em DIAS IN-GAME e por tique
/// (com a trava de parede como rede), que os bonus acabam **com o corpo ainda dentro** (a sala nao
/// expulsa), que a comida repoe por CONSUMO e nao por relogio, e que a saida -- que e uma PASSAGEM,
/// e nao a porta -- recusa quem ficou preso.
/// ======================================================================================
///
/// ============================ AS SECOES 7 A 11 SAO A CAMADA 2 ============================
/// Elas provam o que estava DESLIGADO: o `zoneGainMult` que ninguem escrevia, a maestria 4x que nao
/// existia, o peso que nao custava nada (e nem rendia), o esmagamento que nao existia, e o numero na
/// tela sem o qual nada disso e visivel pro jogador.
///
/// Duas delas MEDEM em vez de ler campo -- o ganho de BP por minuto dentro contra fora, e a maestria
/// por minuto --, porque "o campo vale 280" e "o treino rende 280x" sao afirmacoes diferentes: entre
/// as duas ha quatro caminhos de ganho, um `CapCheck` e um teto pessoal.
/// ====================================================================================
///
/// ============================ O QUE SO DAQUI SE VE ============================
/// A porta atravessa SEIS lugares diferentes, e os seis podem estar certos com a corrente
/// arrebentada no meio -- que foi literalmente o estado do projeto ate agora:
///
///   `Turfs.dm` (o DM) -> `DmTechScanner.MobiliaDeMapa` -> `construcoes.json` ->
///   `.objetos` do z12 -> `Obra` de pe no mundo -> `Interacoes` (o botao) -> o gate do servidor
///
/// A prova mais importante desta bancada e a mais boba: **existe uma porta no Templo Sagrado**.
/// Ela nao existia. O `Passagens.cs` tirou o `tohbtc` das passagens automaticas com a
/// justificativa certa e o substituto nunca foi feito, e o unico sintoma disso era o mapa z13 --
/// 248 mil celulas convertidas -- ser inalcancavel por qualquer caminho.
/// ==============================================================================
///
/// ============================ E ELA MEDE O TETO, E NAO SO A REGRA ============================
/// A lotacao e 2. Um teto que nunca dispara e indistinguivel de teto nenhum (PARTE 0.7), entao a
/// secao 4 nao para em "dois entraram": ela poe o TERCEIRO na porta e exige a recusa, e depois
/// **derruba a conexao de um dos dois** pra provar que a vaga dele continua ocupada -- que e a
/// regra 13.6a, e a unica que nao se ve olhando quem esta desenhado na zona.
/// ==========================================================================================
///
/// OS CORPOS SAO FORJADOS, no molde do `--mestreteste`: sem `Peer`, entrando e saindo do
/// `_players`/`ZoneList` dentro do mesmo bloco sincrono. Ela **nao** mexe em arquivo do mundo --
/// o que a porta grava mora no save de cada personagem, e a secao 6 usa um save de mentira.
///
/// ============================ COMO CADA FAMILIA REPROVA (medido, injetando o defeito) ============================
/// Bancada verde nao e regra provada. Cada linha abaixo foi conferida pondo o defeito no codigo de
/// PRODUCAO, rodando, e tirando -- e tres delas ficaram VERDES na primeira tentativa, o que virou
/// as checagens novas desta rodada. Quem mexer aqui repete o gesto: injete e veja cair.
///
///   defeito injetado                                          | reprovam
///   ----------------------------------------------------------|----------
///   `zoneMasteryMult = TimeChamberMult` (o "conserto" do 4)    | 1
///   `AplicarRitmo` com `if (!dentro) return` (liga e nao desliga) | 7
///   `AplicarGravidade` so escreve quando a gravidade SOBE      | 2  (as DUAS sao novas)
///   `AplicarRitmo` ignorando `sessaoValendo`                   | 3
///   `TrainGain` sem o `zoneGainMult`                           | 2  (so as MEDIDAS; a secao 7 fica verde)
///   `LotacaoDaSala = 3`                                        | 5
///   quem cai libera a vaga (`_naSala.Remove` sem corpo)        | 3
///   `SalaDoTempoNoLogin` sem re-ocupar a vaga                  | 2  (as DUAS sao novas)
///   `AbastecerComidaDaSala()` dentro do tique (repor por RELOGIO) | 1  (NOVA -- a antiga ficava verde)
///   `PorcoesDaSala = 4`                                        | 5
///   uma porcao a tres tiles da chegada                         | 5
///   a comida nao e recolhida no fim da sessao                  | 2
///   `JanelaOuTranca` sem a janela (prende na hora)             | 4
///   `TickDasPassagens` sem o `APrisaoRecusaASaida`             | 2
///   `DeJogador` sem escrever `SalaPreso`                       | 3
///   `MsDeRecargaDaSala` = 24 MINUTOS                           | 3  (NOVAS -- as antigas ficavam verdes)
///   `MsDeRecargaDaSala` = 48 HORAS                             | 3  (NOVAS -- a outra beirada)
///   `case "spawn"` de volta no despacho do jogador             | 3  (secoes 16 e 17)
///   o verb do cliente de volta em `Verbos.Outros`              | 3  (aba, 'Other' limpa e A BUSCA)
///   `case "admin_spawn"` removido (tirar de TODO mundo)        | 2
///   `Renascer` sem o `AMorteSaiDaSala` (a morte nao limpa)     | 4
///   `Renascer` com `if (SalaPreso) return` (gate na morte)     | 6
///   `temclima: false` na ficha da Sala (`planetas.json`)       | 3
///   `Clima.DaZona` devolvendo `Nenhum` pra Sala                | 2
///   as regras da porta sem o preco da morte                    | 1
/// ==============================================================================================================
///
/// ============================ CUIDADO COM A COMPILACAO INCREMENTAL ============================
/// Metade destas linhas ficou VERDE com o defeito dentro na primeira tentativa, e nao por culpa da
/// bancada: o `dotnet build` incremental deu "compilacao com exito" **sem trocar a DLL**, e o Godot
/// subiu com o binario antigo. E o mesmo tombo que o `dbclimax-build-verify` ja registrou no BYOND,
/// com outra ferramenta. Quem for injetar defeito aqui usa `-t:Rebuild`, ou vai concluir que a
/// checagem nao pega o que ela pega.
/// ==============================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Faixa de ids de bancada -- longe do `_nextId` e das faixas das outras.</summary>
	private const int IdBaseDaSalaDeTeste = 90_700;

	private int _salaOk, _salaFalhou;

	private void AfirmarSala(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _salaOk++; GD.Print($"[sala]   OK    {oque}"); return; }
		_salaFalhou++;
		GD.PrintErr($"[sala]   FALHA {oque}   {detalhe}");
	}

	/// <summary>A ultima recusa que o corpo ouviu. E o que prova que a porta recusou pelo motivo CERTO.</summary>
	private static string UltimoAviso() =>
		EscutaDeAvisos is { Count: > 0 } l ? l[^1] : "";

	public void RodarBancadaDaSalaDoTempo()
	{
		_salaOk = _salaFalhou = 0;
		GD.Print("[sala] ================ SALA DO TEMPO: A PORTA E OS GANHOS ================");

		// A FOTO DO ESTADO DE VERDADE: a bancada poe e tira gente de `_naSala`, e o trono do
		// Guardiao e do MUNDO. Rodar a bancada tem que terminar como comecou.
		var salaReal = new Dictionary<string, long>(_naSala, StringComparer.Ordinal);
		bool tinhaGuardiao = _tronos.TryGetValue("guardian", out string? guardiaoReal);
		_naSala.Clear();

		ZoneKey lookout = ZoneKey.Premade("Lookout");
		ZoneKey sala = ZoneKey.Premade(ZonaDaSala);

		ServerPlayer Forjar(int i, string nome)
		{
			var novo = new ServerPlayer
			{
				Id = IdBaseDaSalaDeTeste + i,
				Peer = null,
				Name = nome,
				Race = "Human",
				Genero = "Male",
				Idade = 25,
				Zone = lookout,
				// EM FRENTE A PORTA: ela esta na celula (124,80) e a fila de baixo (81) e a unica
				// livre ao redor -- conferido no `.col` do z12. Sem isto o `ObraQueAceita` recusaria
				// por distancia e a bancada mediria a guarda errada.
				Pos = new Vec2(124 * ZoneCollision.TileSize + 16, 81 * ZoneCollision.TileSize + 16),
				Conta = $"bancada_sala_{i}",
				Slot = 0,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = 1000 },

				// O LIVRO DE SKILLS VAZIO -- e ele nao e enfeite desde que a chave da Sala passou a
				// ser a SKILL `/datum/skill/rank/Permission` (e nao o trono): sem livro a
				// `ReconciliarDadiva` sai pela porta dos fundos e o Guardiao forjado aqui nunca
				// receberia o kit que o jogo de verdade entrega.
				Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			novo.Ficha.Class = "Normal";
			PorNoMundo(novo);
			novo.Ficha.Ki = novo.Ficha.MaxKi;
			return novo;
		}

		ServerPlayer a = Forjar(1, "bancada: o primeiro");
		ServerPlayer b = Forjar(2, "bancada: o segundo");
		ServerPlayer c = Forjar(3, "bancada: o terceiro");
		ServerPlayer kami = Forjar(4, "bancada: o Guardiao");

		EscutaDeAvisos = [];
		try
		{
			APortaExiste();
			OBotaoEOServidorConcordam();
			OGateRecusaPeloMotivoCerto(a, kami);
			ALotacaoDispara(a, b, c, sala);
			OGuardiaoEAValvula(a, b, c, kami);
			OQueOSaveGuarda(a);
			OsDoisRitmosDaZona(a, sala, lookout);
			OQuantoRendeDeVerdade(a, sala, lookout);
			OPesoCustaOPasso(b);
			OEsmagamentoCobra(b);
			OMultiplicadorChegaNaTela(b);
			ASessaoAndaEmDiasInGame(a, b, lookout);
			AComidaReponPorConsumo(a, lookout);
			APrisaoFechaAPorta(a, kami, lookout);
			ARecargaDuraUmDiaReal(a, sala, lookout);
			OGotoSpawnSaiuDasMaosDoJogador(a, kami, lookout, sala);
			AMorteEASaidaCara(a, kami, lookout, sala);
			OClimaDaSalaFica();
		}
		catch (Exception e) { AfirmarSala($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? ""); }
		finally
		{
			EscutaDeAvisos = null;
			foreach (ServerPlayer p in new[] { a, b, c, kami })
			{
				_players.Remove(p.Id);
				ZoneList(lookout.Hash).Remove(p);
				ZoneList(sala.Hash).Remove(p);
			}
			_naSala.Clear();
			foreach (var kv in salaReal) _naSala[kv.Key] = kv.Value;
			_tronos.Remove("guardian");
			if (tinhaGuardiao && guardiaoReal != null) _tronos["guardian"] = guardiaoReal;
		}

		GD.Print($"[sala] ================ {_salaOk} passaram, {_salaFalhou} falharam ================");
	}

	// =====================================================================
	// 1) A PORTA EXISTE -- a corrente inteira, do DM ate o chao
	// =====================================================================
	private void APortaExiste()
	{
		Construcao? def = _obras?.Get(TipoDaPorta);
		AfirmarSala("a porta esta no catalogo de construcoes (`tech` do pipeline)", def != null);
		if (def == null) return;

		AfirmarSala("ela sai do typepath do DM (`/turf/Teleporters/tohbtc`)",
					def.Tipo == "/turf/Teleporters/tohbtc", def.Tipo);

		// A ARTE E O `Door6.dmi` DO DM, no estado "Closed" -- os dois vem da arvore de turfs, e
		// nenhum dos dois foi digitado. Sem arte a obra nasce como um retangulo cinza no meio do
		// Templo, e o jogador nao teria como saber que aquilo e a porta.
		AfirmarSala("ela tem arte (o `Door6.dmi` do DM) e o estado 'Closed'",
					def.Arte.Length > 0 && def.Estado == "Closed", $"arte='{def.Arte}' estado='{def.Estado}'");

		// MOBILIA NAO SE COMPRA. Custo negativo e a marca (ver `Construcao.Construivel`); sem ela a
		// porta da Sala do Tempo apareceria na loja de tecnologia por -1 zeni.
		AfirmarSala("ela NAO e construivel (mobilia de mapa, custo negativo)", !def.Construivel);

		// ============================ E ELA ESTA DE PE NO TEMPLO ============================
		// Esta e a checagem que a fase inteira existe pra fazer passar. Ela olha `_noChao`, que e o
		// mundo de verdade -- nao o arquivo --, entao ela quebra se o `.objetos` sumir, se o
		// `CarregarObjetosDoMapa` recusar a entrada por falta de catalogo, ou se o conversor
		// deixar de reconhecer o typepath.
		var portas = _noChao.FindAll(o => o.Tipo == TipoDaPorta);
		AfirmarSala("HA UMA PORTA DE PE NO TEMPLO SAGRADO (o defeito que esta fase conserta)",
					portas.Count > 0, $"{portas.Count} encontradas");
		foreach (Obra p in portas)
			AfirmarSala($"  ...e ela esta no Lookout, em ({(int)(p.X / 32)},{(int)((p.Y + MoveRules.FeetOffsetY) / 32)})",
						p.Zona.Equals(ZoneKey.Premade("Lookout")), p.Zona.ToString());

		// ============================ E ELA NAO CAI NO SOCO ============================
		// `turf/Teleporters` tem `destroyable = 0` no DM. Mobilia de mapa nasce com a armadura
		// padrao e cai no primeiro golpe de qualquer um -- inofensivo num banco, fatal aqui: a
		// porta e o UNICO caminho pra dentro do z13, e um soco a esmo trancaria a sala pro
		// servidor inteiro ate o proximo boot. O golpe e de BP absurdo de proposito: um teto que
		// so aguenta o fraco e indistinguivel de teto nenhum.
		if (portas.Count > 0)
		{
			Obra porta = portas[0];
			Estragar(porta, 1e12, null);
			AfirmarSala("...e ela NAO CAI nem com um golpe de 1e12 (`destroyable = 0`, Turfs.dm:102)",
						_noChao.Contains(porta));
		}
	}

	// =====================================================================
	// 2) O BOTAO E O SERVIDOR LEEM A MESMA TABELA
	// =====================================================================
	private void OBotaoEOServidorConcordam()
	{
		// O menu do cliente desenha o que `Interacoes.De` devolve, e o servidor recusa o que
		// `Interacoes.Aceita` nega. Sao a mesma tabela de proposito -- botao que existe com
		// comando que o servidor nao aceita e a falha classica deste canal.
		AfirmarSala("a porta e interativa (a dica de [E] acende)", Interacoes.Interativo(TipoDaPorta));
		AfirmarSala("o servidor aceita `sala_entrar` nesta porta", Interacoes.Aceita(TipoDaPorta, "sala_entrar"));
		AfirmarSala("o servidor aceita `sala_regras` nesta porta", Interacoes.Aceita(TipoDaPorta, "sala_regras"));

		// E O CONTRARIO TAMBEM: um cliente mexido nao usa a porta pra sacar do banco.
		AfirmarSala("...e NAO aceita um verbo de outra coisa (`banco_sacar`)",
					!Interacoes.Aceita(TipoDaPorta, "banco_sacar"));

		// A GRAVIDADE DA SALA. Ela nao e da porta, mas e o outro defeito que a fase 0 tinha que
		// confirmar: o nome da zona (`Hyperbolic_Time_Chamber`) e o da ficha ("Hyperbolic Time
		// Dimension") nao casavam, e a sala treinava com gravidade 1.
		double g = _planetas?.De(ZonaDaSala).Gravidade ?? 1;
		AfirmarSala("a Sala do Tempo puxa 10x (Gravity.dm:95, pelo apelido do catalogo)",
					Math.Abs(g - 10) < 1e-9, $"{g}");
	}

	// =====================================================================
	// 3) O GATE RECUSA, E PELO MOTIVO CERTO
	// =====================================================================
	/// <summary>
	/// A ORDEM DAS RECUSAS E A MENSAGEM QUE O JOGADOR LE, e por isso a bancada nao se contenta com
	/// "nao entrou": ela LE a frase. Um gate que recusasse tudo com "voce nao pode" passaria em
	/// qualquer teste booleano e ensinaria nada -- e a recarga de 24 h e exatamente o caso em que
	/// o jogador precisa saber que o que falta e a CHAVE, e nao o relogio.
	/// </summary>
	private void OGateRecusaPeloMotivoCerto(ServerPlayer a, ServerPlayer kami)
	{
		bool Tentou(ServerPlayer p, string trecho)
		{
			EscutaDeAvisos?.Clear();
			ComandoDaSalaDoTempo(p, "sala_entrar", "");
			bool ficou = string.Equals(p.Zone.Name, "Lookout", StringComparison.OrdinalIgnoreCase);
			return ficou && UltimoAviso().Contains(trecho, StringComparison.OrdinalIgnoreCase);
		}

		AfirmarSala("sem a chave do Guardiao, a porta fala em BARREIRA (e nao em relogio)",
					Tentou(a, "barreira"), UltimoAviso());

		// MORTO, NOCAUTEADO E VOANDO -- as tres do DM (`htc_try_enter` + o `if(!M.flight)` do turf).
		// A do VOO ganhou peso no port: no BYOND `flight` era um booleano e aqui o voo tem ALTITUDE,
		// entao entrar no ar significaria aparecer la dentro a dez tiles do chao.
		a.SalaAutorizada = true;
		a.Ficha.dead = true;
		AfirmarSala("morto nao entra", Tentou(a, "mortos"), UltimoAviso());
		a.Ficha.dead = false;

		a.Ficha.KO = true;
		AfirmarSala("nocauteado nao entra", Tentou(a, "pé"), UltimoAviso());
		a.Ficha.KO = false;

		a.Voando = true;
		AfirmarSala("quem esta voando nao entra (o `if(!M.flight)` do turf)", Tentou(a, "pouse"), UltimoAviso());
		a.Voando = false;

		// A CHAVE SOBREVIVEU AS TRES RECUSAS. Uma autorizacao consumida por uma tentativa que nem
		// chegou a mover o corpo seria a chave do Guardiao queimada por um acidente.
		AfirmarSala("...e nenhuma dessas recusas QUEIMOU a chave do Guardiao", a.SalaAutorizada);

		// A PRISAO: quem esta preso nao entra (nem sai -- o sair e da proxima fase). O bit ja e lido
		// aqui de proposito, pra a tranca e a chave nascerem juntas.
		a.SalaPreso = true;
		AfirmarSala("quem ficou PRESO nao e aceito pela porta", Tentou(a, "preso"), UltimoAviso());
		AfirmarSala("...e a recusa por prisao diz quem solta (o Guardiao ou um admin)",
					UltimoAviso().Contains("Guardião", StringComparison.OrdinalIgnoreCase)
					&& UltimoAviso().Contains("administrador", StringComparison.OrdinalIgnoreCase), UltimoAviso());
		a.SalaPreso = false;

		// A RECARGA DE 24 H REAIS, marcada NA ENTRADA (`htc_last_visit`).
		a.SalaUltimaEntrada = NowMs();
		AfirmarSala("com a recarga correndo, a porta fala em TEMPO REAL e em horas",
					Tentou(a, "tempo real") && UltimoAviso().Contains('h'), UltimoAviso());
		a.SalaUltimaEntrada = 0;

		// O AVISO DA PORTA -- a metade "avisar claro" da decisao sobre o risco da prisao. Ele tem
		// que dizer as tres coisas: que prende, quanto tempo, e quem solta.
		EscutaDeAvisos?.Clear();
		ComandoDaSalaDoTempo(a, "sala_regras", "");
		string tudo = string.Join(" | ", EscutaDeAvisos ?? []);
		AfirmarSala("as regras da porta AVISAM que a sala prende, com o prazo e com quem solta",
					tudo.Contains("PRESO", StringComparison.Ordinal)
					&& tudo.Contains("2 MINUTOS", StringComparison.Ordinal)
					&& tudo.Contains("administrador", StringComparison.OrdinalIgnoreCase), tudo);

		// ============================ E ELAS DIZEM O PRECO DA OUTRA SAIDA ============================
		// Depois que o `Goto Spawn` saiu das maos do jogador, a unica saida que nao depende de outra
		// pessoa e MORRER (decisao do dono). Um custo que so se descobre do lado de dentro nao e
		// custo, e pegadinha -- e esta e a mesma razao de o aviso ser uma acao do menu da porta.
		// ==========================================================================================
		AfirmarSala("...e AVISAM que morrer e a outra saida, com o preco dela",
					tudo.Contains("MORRER", StringComparison.OrdinalIgnoreCase)
					&& tudo.Contains("Enma", StringComparison.OrdinalIgnoreCase), tudo);

		_ = kami;
	}

	// =====================================================================
	// 4) A LOTACAO DE DUAS PESSOAS -- E O TETO DISPARA
	// =====================================================================
	private void ALotacaoDispara(ServerPlayer a, ServerPlayer b, ServerPlayer c, ZoneKey sala)
	{
		void Entrar(ServerPlayer p)
		{
			p.SalaAutorizada = true;
			p.SalaUltimaEntrada = 0;
			EscutaDeAvisos?.Clear();
			ComandoDaSalaDoTempo(p, "sala_entrar", "");
		}

		Entrar(a);
		AfirmarSala("o PRIMEIRO entra e vai parar no z13", a.Zone.Hash == sala.Hash, a.Zone.Name);
		AfirmarSala("...a chave foi CONSUMIDA na entrada (`permission = 0`)", !a.SalaAutorizada);
		AfirmarSala("...e a recarga foi armada NA ENTRADA, e nao na saida", a.SalaUltimaEntrada > 0);

		// O CORPO CAIU NUM LUGAR ONDE DA PRA FICAR. `HTC_ENTRY_LOC = locate(146,160,13)` vira a
		// celula (145,340) -- e uma entrada que largasse o corpo dentro de uma parede seria um
		// teleporte pra dentro da geometria.
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(sala);
		AfirmarSala("...e o ponto de entrada do DM cai numa celula LIVRE",
					mapa != null && !mapa.BlockedAt(new Vec2(a.Pos.X, a.Pos.Y + MoveRules.FeetOffsetY)),
					$"({a.Pos.X / 32:0},{(a.Pos.Y + MoveRules.FeetOffsetY) / 32:0})");

		Entrar(b);
		AfirmarSala("o SEGUNDO tambem entra (a lotacao e 2)", b.Zone.Hash == sala.Hash, b.Zone.Name);

		// ============================ O TETO DISPARA ============================
		Entrar(c);
		AfirmarSala("O TERCEIRO E RECUSADO NA PORTA (o teto de 2 dispara de verdade)",
					c.Zone.Hash != sala.Hash && UltimoAviso().Contains("máximo", StringComparison.OrdinalIgnoreCase),
					UltimoAviso());
		AfirmarSala("...e a recusa por lotacao NAO queima a chave dele", c.SalaAutorizada);

		// ============================ QUEM CAI CONTINUA OCUPANDO A VAGA ============================
		// Regra 13.6a. E a unica que nao se ve olhando quem esta desenhado na zona -- por isso a
		// conta e por ASSINATURA e nao por corpo vivo. Aqui o corpo de `b` some do mundo (que e o
		// que uma queda de conexao faz) e o terceiro continua sendo recusado.
		_players.Remove(b.Id);
		ZoneList(sala.Hash).Remove(b);
		TickDaSalaDoTempo();
		AfirmarSala("quem PERDEU A CONEXAO la dentro continua ocupando a vaga",
					QuantosNaSala() == 2, $"{QuantosNaSala()} dentro");

		Entrar(c);
		AfirmarSala("...e por isso o terceiro CONTINUA recusado com um dos dois deslogado",
					c.Zone.Hash != sala.Hash, c.Zone.Name);

		// E A VAGA VAGA QUANDO A PESSOA SAI DA ZONA -- que e o que a passagem `fromhbtc` faz.
		_players[b.Id] = b;
		ZoneList(sala.Hash).Add(b);
		b.Zone = ZoneKey.Premade("Lookout");
		ZoneList(sala.Hash).Remove(b);
		ZoneList(b.Zone.Hash).Add(b);
		TickDaSalaDoTempo();
		AfirmarSala("sair pela porta libera a vaga", QuantosNaSala() == 1, $"{QuantosNaSala()} dentro");

		Entrar(c);
		AfirmarSala("...e agora o terceiro entra", c.Zone.Hash == sala.Hash, c.Zone.Name);

		// ============================ E O REINICIO DE SERVIDOR NAO ABRE A SALA ============================
		// `_naSala` e MEMORIA de propósito (o `htc_inside` do DM tambem e `tmp`), entao um reinicio a
		// esvazia -- enquanto a sessao de cada um, que esta no disco, continua correndo. O que fecha
		// esse buraco e o `htc_login_check()`: quem acorda dentro da zona re-ocupa a vaga.
		//
		// SEM ELE, reiniciar (ou so relogar depois de um reinicio) seria o jeito de por quatro pessoas
		// numa sala de duas, com as quatro rendendo 280x -- e nada na tela diria de onde vieram. A
		// bancada cobra as duas metades: que a vaga volta pra quem esta DENTRO, e que ela **nao** e
		// dada a quem esta fora.
		// ==============================================================================================
		_naSala.Clear();
		AfirmarSala("um REINICIO de servidor esvazia a lista de quem esta dentro (ela e memoria)",
					QuantosNaSala() == 0, $"{QuantosNaSala()}");

		SalaDoTempoNoLogin(a);
		SalaDoTempoNoLogin(c);
		AfirmarSala("...mas o LOGIN de quem acorda DENTRO re-ocupa a vaga (`htc_login_check`)",
					QuantosNaSala() == 2, $"{QuantosNaSala()} dentro");

		SalaDoTempoNoLogin(b);   // `b` esta no Lookout desde a linha de cima
		AfirmarSala("...e quem acorda FORA nao ocupa vaga nenhuma", QuantosNaSala() == 2,
					$"{QuantosNaSala()} dentro");

		// o login com sessao viva repoe a mesa (e o que a linha do `SalaDoTempoNoLogin` faz); as
		// secoes seguintes contam com o chao limpo.
		RecolherComidaDaSala();
	}

	// =====================================================================
	// 5) O GUARDIAO, E A VALVULA DE ADMIN
	// =====================================================================
	/// <summary>
	/// A CHAVE E O RESGATE SAO DA MESMA MAO, e a valvula e a DECISAO sobre o risco da regra 13.6c:
	/// um jogador preso sem Guardiao online e um jogador que nao pode jogar. Aqui a bancada prova
	/// as duas metades -- que o Guardiao solta, e que o admin solta **com o trono vago**.
	/// </summary>
	private void OGuardiaoEAValvula(ServerPlayer a, ServerPlayer b, ServerPlayer c, ServerPlayer kami)
	{
		_tronos.Remove("guardian");

		// QUEM NAO E GUARDIAO NAO FAZ CHAVE. Sem esta guarda o verb viraria "qualquer um autoriza",
		// e o cargo inteiro deixaria de significar alguma coisa.
		b.SalaAutorizada = false;
		EscutaDeAvisos?.Clear();
		ComandoDaSalaDoTempo(a, "sala_autorizar", b.Id.ToString());
		AfirmarSala("quem NAO e Guardiao da Terra nao autoriza ninguem",
					!b.SalaAutorizada && UltimoAviso().Contains("Guardião", StringComparison.OrdinalIgnoreCase),
					UltimoAviso());

		// SENTAR NO TRONO NAO E RECEBER O KIT: quem entrega a skill `/datum/skill/rank/Permission` e a
		// `ReconciliarDadiva`, e ela roda em toda troca de trono de verdade (`Entronizar`) e no tique
		// de 1 Hz. Escrever so o `_tronos` a mao aqui e o atalho da bancada, e desde que o portao da
		// chave passou a ser a SKILL (e nao o trono), o atalho precisa passar pelo mesmo funil --
		// senao esta bancada estaria medindo um Guardiao que o jogo nunca produz.
		_tronos["guardian"] = kami.Conta;
		ReconciliarDadiva(kami);
		EscutaDeAvisos?.Clear();
		ComandoDaSalaDoTempo(kami, "sala_autorizar", b.Id.ToString());
		AfirmarSala("o Guardiao da Terra faz a chave (o `verb/Permission` do DM)", b.SalaAutorizada);

		// E O MESTRE KORIN TAMBEM, que e a metade que o port recusava: o `growbranches()` da Terra
		// liga a mesma skill pros dois cargos (`EarthRanks.dm:22` e `:33`) e o `Concede` do Korin
		// promete "Autorizar Sala do Tempo" desde que a tabela existe.
		b.SalaAutorizada = false;
		_tronos.Remove("guardian");
		_tronos["korin"] = kami.Conta;
		ReconciliarDadiva(kami);
		ComandoDaSalaDoTempo(kami, "sala_autorizar", b.Id.ToString());
		AfirmarSala("o Mestre Korin tambem faz a chave (o segundo ramo do `growbranches`)", b.SalaAutorizada);

		_tronos.Remove("korin");
		_tronos["guardian"] = kami.Conta;
		ReconciliarDadiva(kami);

		// SOLTAR: o Guardiao tira a tranca...
		c.SalaPreso = true;
		ComandoDaSalaDoTempo(kami, "sala_soltar", c.Id.ToString());
		AfirmarSala("o Guardiao solta quem ficou preso", !c.SalaPreso);

		// ...e um jogador comum NAO tira.
		c.SalaPreso = true;
		ComandoDaSalaDoTempo(a, "sala_soltar", c.Id.ToString());
		AfirmarSala("um jogador comum NAO solta ninguem", c.SalaPreso);

		// ============================ A VALVULA, COM O TRONO VAGO ============================
		// Este e o caso que a decisao existe pra cobrir. Sem Guardiao no mundo, o admin solta -- e
		// e por isso que o caminho de admin nao passa pela conferencia de cargo.
		_tronos.Remove("guardian");
		AdminSoltarDaSala(a, c.Id.ToString());
		AfirmarSala("O ADMIN SOLTA MESMO COM O TRONO DO GUARDIAO VAGO (a valvula do risco)",
					!c.SalaPreso);
	}

	// =====================================================================
	// 6) O QUE O DISCO GUARDA
	// =====================================================================
	/// <summary>
	/// OS TRES CAMPOS PRECISAM SOBREVIVER AO LOGOUT, e cada um por um motivo diferente: uma
	/// recarga que o logout zera nao e recarga; uma chave que morre no logout obriga o Guardiao a
	/// esperar o outro estar online; e uma prisao que o logout abre e a chave mestra da unica
	/// tranca do jogo.
	///
	/// A ARMADILHA E CONHECIDA E JA MORDEU: `DeJogador` monta o save do ZERO a cada `Persistir`, e
	/// **campo declarado e nao escrito e campo apagado** -- foi assim que os `Limiares` sumiram do
	/// disco por meses sem ninguem notar. Por isso o teste e uma IDA E VOLTA de verdade pelas duas
	/// funcoes, e nao uma leitura do objeto em memoria.
	/// </summary>
	private void OQueOSaveGuarda(ServerPlayer a)
	{
		long marca = NowMs() - 1234;
		a.SalaUltimaEntrada = marca;
		a.SalaAutorizada = true;
		a.SalaPreso = true;

		CharacterSave s = AccountStore.DeJogador(a, NowMs());
		AfirmarSala("`DeJogador` ESCREVE a recarga da sala", s.SalaUltimaEntrada == marca, $"{s.SalaUltimaEntrada}");
		AfirmarSala("`DeJogador` ESCREVE a chave do Guardiao", s.SalaAutorizada);
		AfirmarSala("`DeJogador` ESCREVE a prisao", s.SalaPreso);

		a.SalaUltimaEntrada = 0;
		a.SalaAutorizada = false;
		a.SalaPreso = false;
		AccountStore.ParaJogador(s, a);
		AfirmarSala("...e a volta do disco devolve os tres",
					a.SalaUltimaEntrada == marca && a.SalaAutorizada && a.SalaPreso,
					$"{a.SalaUltimaEntrada}/{a.SalaAutorizada}/{a.SalaPreso}");

		// ============================ O QUARTO CAMPO: A CONTA DE DIAS ============================
		// `htc_session_years` e var comum de mob no DM, e ele escreve o motivo do lado: *"persiste ->
		// relogar la dentro NAO zera a conta e re-ganha 2 anos"*. Sem esta ida e volta, deslogar e
		// voltar seria a fonte infinita de 280x -- a sessao recomecaria do zero de graca.
		// ======================================================================================
		a.SalaDiasDaSessao = 1.25;
		CharacterSave s2 = AccountStore.DeJogador(a, NowMs());
		a.SalaDiasDaSessao = 0;
		AccountStore.ParaJogador(s2, a);
		AfirmarSala("a CONTA DE DIAS da sessao sobrevive ao disco (relogar nao devolve os 2 dias)",
					Math.Abs(a.SalaDiasDaSessao - 1.25) < 1e-9, $"{a.SalaDiasDaSessao}");
		a.SalaDiasDaSessao = 0;

		// UM SAVE DE ANTES DISTO EXISTIR chega com os quatro no default, e os quatro defaults sao a
		// resposta certa: nunca visitou, sem chave, solto, sem sessao. Nao ha ramo de migracao porque
		// nao ha nada que distinguir -- e esta linha e o que prova que "sem campo" nao vira "preso".
		var antigo = new CharacterSave();
		AfirmarSala("save antigo (sem os campos) volta como 'nunca entrou, sem chave, SOLTO'",
					antigo.SalaUltimaEntrada == 0 && !antigo.SalaAutorizada && !antigo.SalaPreso
					&& antigo.SalaDiasDaSessao == 0);
	}

	// =====================================================================
	// 7) OS DOIS RITMOS DA ZONA -- e os dois voltam ao normal na saida
	// =====================================================================
	/// <summary>
	/// A CAMADA 2 (13.4) LIGOU TRES FIOS QUE ESTAVAM SOLTOS, e esta secao e a que prova que eles
	/// estao ligados **nas duas direcoes**.
	///
	/// ============================ O QUE ERA O DEFEITO ============================
	/// `GainKnobs.TimeChamberMult = 280` existia. `Fighter.zoneGainMult` existia e era lido por
	/// quatro caminhos de ganho. E o servidor **nunca escrevia** o campo -- so a `TrainBench` do
	/// pipeline escrevia. Ou seja: a constante, a multiplicacao e o mapa estavam prontos, e a Sala
	/// rendia exatamente o mesmo que um campo qualquer da Terra. Um sistema inteiro a uma atribuicao
	/// de distancia de existir.
	/// ==========================================================================
	///
	/// A SAIDA E TAO IMPORTANTE QUANTO A ENTRADA, e por isso ela e testada em cima de uma zona de
	/// gravidade IGUAL a da Sala (Vegeta tambem puxa 10x): o `AplicarGravidade` tem um atalho que
	/// desiste quando a gravidade nao mudou, e um ritmo escrito depois dele deixaria quem entrasse
	/// -- ou saisse -- pelo planeta certo com 280x no bolso, para sempre, em silencio.
	/// </summary>
	private void OsDoisRitmosDaZona(ServerPlayer a, ZoneKey sala, ZoneKey lookout)
	{
		Fighter f = a.Ficha;

		void Ir(ZoneKey z)
		{
			ZoneList(a.Zone.Hash).Remove(a);
			a.Zone = z;
			ZoneList(z.Hash).Add(a);
			AplicarGravidade(a);   // o funil de producao: e ele quem escreve os dois ritmos
		}

		// A GRAVIDADE DO TEMPLO, LIDA DA FICHA DELE e nao cravada em 1: o que se afirma e que o funil
		// escreve a gravidade DA ZONA ONDE O CORPO ESTA, e o segundo termo (`< 10`) e o que impede a
		// linha de virar tautologia -- ela tem que ser diferente da gravidade da Sala pra a checagem
		// de volta, la embaixo, significar alguma coisa.
		double gTemplo = _planetas?.De(lookout.Name).Gravidade ?? 1;
		Ir(lookout);
		AfirmarSala($"fora da Sala a gravidade e a do Templo ({gTemplo:0.##}x, e nao a dela)",
					Math.Abs(f.Planetgrav - gTemplo) < 1e-9 && gTemplo < 10, $"{f.Planetgrav}");
		AfirmarSala("fora da Sala o ganho de BP e 1x", Math.Abs(f.zoneGainMult - 1) < 1e-9, $"{f.zoneGainMult}");
		AfirmarSala("...e a maestria tambem", Math.Abs(f.zoneMasteryMult - 1) < 1e-9, $"{f.zoneMasteryMult}");

		Ir(sala);
		AfirmarSala("DENTRO a gravidade e 10x (Gravity.dm:95)", Math.Abs(f.Planetgrav - 10) < 1e-9, $"{f.Planetgrav}");
		AfirmarSala("DENTRO o ganho de BP e 280x (`GainKnobs.TimeChamberMult`, que nunca era escrito)",
					Math.Abs(f.zoneGainMult - GainKnobs.TimeChamberMult) < 1e-9, $"{f.zoneGainMult}");
		AfirmarSala("DENTRO a maestria de forma e 4x -- e NAO 280 (regra do dono 13.6d)",
					Math.Abs(f.zoneMasteryMult - 4) < 1e-9, $"{f.zoneMasteryMult}");

		// ============================ O 7000x QUE NAO PODE EXISTIR ============================
		// O BYOND roda os DOIS sistemas de Sala ao mesmo tempo: o novo (280x) e o legado
		// (`HBTCMod = 25` multiplicando `Egains`, Stats.dm:486-510). Nos caminhos de treino eles se
		// multiplicam e dao 7000x -- ninguem desenhou isso. O port porta so o novo, e esta linha e o
		// alarme: se alguem "completar" o porte escrevendo o 25, ela cai.
		// ==================================================================================
		AfirmarSala("o `HBTCMod` legado continua valendo 1 DENTRO da sala (o 25 foi descartado)",
					Math.Abs(f.HBTCMod - 1) < 1e-9, $"{f.HBTCMod}");
		AfirmarSala("...e por isso o ritmo da sala e 280x, e nao os 7000x que os dois sistemas somados dao",
					Math.Abs(f.zoneGainMult * f.HBTCMod - 280) < 1e-6, $"{f.zoneGainMult * f.HBTCMod}");

		// ============================ SAIR PARA UM CHAO DE MESMA GRAVIDADE ============================
		// Vegeta puxa 10x, igual a Sala. Se o ritmo fosse escrito depois do atalho de gravidade do
		// `AplicarGravidade`, este e o caminho por onde os 280x sairiam junto com o jogador.
		// =========================================================================================
		var vegeta = ZoneKey.Premade("Vegeta");
		double gVegeta = _planetas?.De(vegeta.Name).Gravidade ?? 0;
		Ir(vegeta);
		AfirmarSala($"saindo pra um chao de MESMA gravidade ({gVegeta:0}x), o ganho volta a 1x",
					Math.Abs(f.zoneGainMult - 1) < 1e-9, $"{f.zoneGainMult}");
		AfirmarSala("...e a maestria tambem volta a 1x", Math.Abs(f.zoneMasteryMult - 1) < 1e-9, $"{f.zoneMasteryMult}");

		// ============================ O TERCEIRO NUMERO TAMBEM VOLTA ============================
		// A 13.7 pede os TRES de volta ao normal, e ate aqui a bancada so cobrava dois: gravidade
		// entrava na conta na ida (10x, Gravity.dm:95) e ninguem perguntava o que acontecia com ela
		// na volta. Um corpo que saisse da Sala com `Planetgrav = 10` no bolso ganharia BP de graca
		// pelo `GravGain` -- e seria ESMAGADO no Templo assim que a maestria dele nao acompanhasse,
		// perdendo vida por segundo em pe no lugar mais seguro do jogo.
		// =====================================================================================
		Ir(lookout);
		AfirmarSala($"...E A GRAVIDADE TAMBEM ({gTemplo:0.##}x de volta) -- o terceiro numero da 13.7",
					Math.Abs(f.Planetgrav - gTemplo) < 1e-9, $"{f.Planetgrav}");
	}

	// =====================================================================
	// 8) QUANTO RENDE DE VERDADE -- medido, e nao lido do campo
	// =====================================================================
	/// <summary>
	/// AS DUAS RAZOES, MEDIDAS **SEPARADAS** -- porque juntas elas mentiriam.
	///
	/// ============================ POR QUE A GRAVIDADE FICA DE FORA DA MEDIDA DO 280 ============================
	/// Treinar na Sala rende MAIS que 280x contra a Terra, e nao por bug: a Sala tambem puxa 10x, e
	/// gravidade e um multiplicador proprio (`GravGain`). Medir "BP por minuto dentro contra fora" cru
	/// daria um numero maior que 280 e a bancada teria que escolher entre reprovar o certo ou aceitar
	/// qualquer coisa. Entao sao duas medidas:
	///
	///   * a do RITMO DA ZONA, com a gravidade CONGELADA nos dois lados -- e essa tem que dar 280
	///     cravado, porque e o unico fator que muda;
	///   * a do GANHO REAL (Terra 1x contra Sala 10x + 280x), que so precisa ser MAIOR que 280 --
	///     e o numero e impresso, porque e o que o jogador vai sentir.
	/// ======================================================================================================
	///
	/// A CONTA E FEITA PELO `Treinar` DE PRODUCAO, o mesmo que o `TickFichas` chama a 5 Hz. Uma
	/// bancada que somasse `BpGainBase * BPTick * ...` na mao mediria a formula que ela mesma
	/// escreveu, e nao o jogo.
	/// </summary>
	private void OQuantoRendeDeVerdade(ServerPlayer a, ZoneKey sala, ZoneKey lookout)
	{
		const int TiquesDeUmMinuto = 300;   // o `TickFichas` roda a 5 Hz

		// ============================ O QUE A MEDIDA PRECISA CONGELAR, E POR QUE ============================
		// Uma razao so vale se a UNICA coisa diferente entre as duas corridas for o que se esta
		// medindo. Cinco coisas aqui derivam sozinhas e estragariam a conta:
		//
		//   * `GravMastered` -- ele SOBE encarando gravidade acima dele (`GravGain`), entao a primeira
		//     corrida deixaria a segunda comecando aclimatada. Fica CRAVADO na propria gravidade: sem
		//     folga (nada de bonus de aclimatacao) e sem subida (nao ha o que masterizar);
		//   * `relBPmax` -- e o teto pessoal, e a corrida de 280x sozinha ganha ~1900 de BP. Com o teto
		//     perto, o `CapCheck` corta o ganho no meio da medida e a razao desaba. O `UPMod` alto poe
		//     o teto em 1e9 e o tira da conta;
		//   * `BPBuffer`/`Gaintimer` -- o acumulador de quem estava parado entra INTEIRO no primeiro
		//     ganho pago, e ele nao leva o ritmo da zona: entraria como um presente fixo na corrida
		//     mais barata, inflando o denominador;
		//   * `stamina` -- o `StamBPGainMod` sai do `Statify`, e com o tanque cheio ele fica preso no
		//     teto (1,25) nas duas corridas;
		//   * `bp_milestone_mult` -- o patamar da raca, que multiplica a base do ganho.
		// ================================================================================================
		double MedirBp(double gravidade, double ritmo)
		{
			Fighter f = a.Ficha;
			f.BP = 1000;
			f.UPMod = 1e6;                 // teto pessoal fora do caminho (ver acima)
			f.bp_milestone_mult = 1;
			f.BPBuffer = 0;
			f.Gaintimer = 0;
			f.hiddenpotential = 1;
			f.Planetgrav = gravidade;
			f.gravmult = 0;
			f.GravMastered = gravidade;    // sem folga e sem subida
			f.zoneGainMult = ritmo;
			f.Weighted = 0;
			f.Statify();
			f.WeightTick();
			f.PowerLevel();                // e ele quem escreve o `relBPmax` com o UPMod novo
			f.stamina = f.maxstamina;
			f.Statify();                   // ...e ele quem congela o `StamBPGainMod` no teto
			f.train = true;

			double antes = f.BP;
			for (int i = 0; i < TiquesDeUmMinuto; i++) Treinar(a);
			f.train = false;
			return f.BP - antes;
		}

		// --- 1) O RITMO DA ZONA, isolado: mesma gravidade dos dois lados ---
		double semRitmo = MedirBp(gravidade: 10, ritmo: 1);
		double comRitmo = MedirBp(gravidade: 10, ritmo: GainKnobs.TimeChamberMult);
		double razao = semRitmo > 0 ? comRitmo / semRitmo : 0;
		AfirmarSala($"o ritmo da Sala rende 280x com a gravidade congelada (medido: {razao:0.0}x)",
					Math.Abs(razao - 280) < 1, $"{semRitmo:0.####} -> {comRitmo:0.##} por minuto");

		// --- 2) O ganho REAL: Terra (1x, ritmo 1) contra Sala (10x, ritmo 280) ---
		double naTerra = MedirBp(gravidade: 1, ritmo: 1);
		double naSala = MedirBp(gravidade: 10, ritmo: GainKnobs.TimeChamberMult);
		double total = naTerra > 0 ? naSala / naTerra : 0;
		AfirmarSala($"e contra a TERRA a Sala rende {total:0}x (gravidade 10 POR CIMA do ritmo 280)",
					total > 280, $"{naTerra:0.####} -> {naSala:0.##} de BP por minuto");

		// --- 3) A MAESTRIA, e ela NAO segue o 280 ---
		// Pelo funil de producao (`SubirMaestriaDaZona`), com os mesmos argumentos que o
		// `TickDaForma` passa. O 4 e deliberado: 280 aqui entregaria a escada inteira dominada numa
		// sessao de 48 minutos, e maestria e o que DISPENSA a cinematica.
		double MedirMaestria(double ritmo)
		{
			a.Ficha.zoneMasteryMult = ritmo;
			a.Forma.Maestria.Por("ssj1", 0);   // do zero: a barra tem teto em 100 e satura
			for (int i = 0; i < 60; i++)
				SubirMaestriaDaZona(a, "ssj1", Jandirus.Core.Forms.Catalogo.MaestriaPorSegundo, 1, out _);
			return a.Forma.Maestria.De("ssj1");
		}

		double fora = MedirMaestria(1);
		double dentro = MedirMaestria(4);
		double razaoM = fora > 0 ? dentro / fora : 0;
		AfirmarSala($"a maestria de forma sobe 4x dentro -- e NAO 280 (medido: {razaoM:0.00}x)",
					Math.Abs(razaoM - 4) < 0.01, $"{fora:0.####}% -> {dentro:0.####}% por minuto");

		// O ESTADO VOLTA AO NORMAL: esta secao mexeu na gravidade e nos dois ritmos a mao.
		ZoneList(a.Zone.Hash).Remove(a);
		a.Zone = lookout;
		ZoneList(lookout.Hash).Add(a);
		AplicarGravidade(a);
		_ = sala;
	}

	// =====================================================================
	// 9) O PESO CUSTA O PASSO
	// =====================================================================
	/// <summary>
	/// ============================ O DEFEITO QUE ESTA SECAO FECHA ============================
	/// Peso rendia ate 8x de BP e **nao custava nada**: nao havia penalidade de movimento nenhuma no
	/// port. Com premio e sem preco, "quanto peso vestir?" tem uma resposta so -- o maximo -- e uma
	/// escolha com uma resposta so nao e escolha.
	///
	/// A bancada mede as DUAS pontas do mesmo gesto: vestir peso tem que SUBIR o multiplicador de
	/// ganho e DESCER a velocidade. Provar so uma das duas deixaria passar exatamente o estado
	/// anterior.
	/// ====================================================================================
	/// </summary>
	private void OPesoCustaOPasso(ServerPlayer b)
	{
		Fighter f = b.Ficha;

		// O RITMO DE ZONA DELE VOLTA AO NORMAL PELO CAMINHO DE PRODUCAO. `b` entrou na Sala na secao
		// 4 e foi tirado de la a mao (a bancada mexe na `ZoneList` direto pra simular a passagem), e
		// sem esta linha ele chegaria aqui com os 280x da Sala no bolso -- o que nao quebraria a
		// razao medida abaixo, mas faria os numeros impressos mentirem.
		AplicarGravidade(b);

		f.Planetgrav = 1;
		f.gravmult = 0;
		f.GravMastered = 1;
		f.Weighted = 0;
		f.Tick(agoraMs: NowMs());   // a ordem de producao: Statify -> PowerLevel -> WeightTick
		RecalcularVelocidade(b);

		float semPeso = b.SpeedStat;
		double ganhoSemPeso = f.MultiplicadorDeGanho();
		AfirmarSala("sem peso o passo e inteiro (o fator vale 1)",
					Math.Abs(Esmagamento.FatorDePasso(f) - 1) < 1e-9, $"{Esmagamento.FatorDePasso(f)}");
		AfirmarSala("...e o multiplicador de ganho do peso e 1x", Math.Abs(f.weight - 1) < 1e-9, $"{f.weight}");

		// ============================ 50% DO MAXIMO = O LIMITE DO CORPO ============================
		// Pelo VERBO de producao (`AjustarPeso`), e nao escrevendo `Weighted` a mao: o defeito que
		// esta secao fecha morava exatamente ali -- o verbo guardava uma FRACAO de 0 a 1 num campo que
		// esta na escala do `weight_cap_hw` (centenas ou bilhoes), e a razao dava zero. Uma bancada
		// que escrevesse o campo direto passaria com o verbo quebrado.
		//
		// 50% do teto (que e 2x o limite do corpo) da razao 1 na gravidade da Terra: o ponto em que o
		// corpo esta carregado ao maximo SEM ser esmagado, e o treino rende 2x.
		// ========================================================================================
		AjustarPeso(b, "50");

		AfirmarSala("50% do peso maximo poe o corpo EXATAMENTE no limite dele (razao 1)",
					Math.Abs(Esmagamento.Razao(f) - 1) < 0.01, $"{Esmagamento.Razao(f):0.###}");
		AfirmarSala("...e isso dobra o ganho de treino (o `min(weight_ratio*2, 8)` do DM)",
					Math.Abs(f.weight - 2) < 0.02, $"{f.weight:0.###}x");
		AfirmarSala("...ou seja: com peso, o multiplicador de GANHO sobe",
					f.MultiplicadorDeGanho() > ganhoSemPeso * 1.5,
					$"{ganhoSemPeso:0.##}x -> {f.MultiplicadorDeGanho():0.##}x");
		AfirmarSala("...E A VELOCIDADE DESCE (o `mobTime -= weight*(1/Espeed)` do DM)",
					b.SpeedStat < semPeso * 0.999, $"{semPeso:0.###} -> {b.SpeedStat:0.###}");

		// ============================ 100% JA ESMAGA, E ESSA E A DECISAO ============================
		// Vestir o maximo deixou de ser escolha obvia: ele rende 4x e cobra vida por segundo. E o
		// numero do DM (`WEIGHT_ITEM_CAP_MULT = 2` -> razao 2 no maximo, na Terra).
		// ========================================================================================
		AjustarPeso(b, "100");
		AfirmarSala("VESTIR O MAXIMO JA ESMAGA (razao 2) -- deixou de ser decisao obvia",
					Esmagamento.Esmaga(f) && Esmagamento.DanoPorSegundo(f) > 0,
					$"razao {Esmagamento.Razao(f):0.##}, {Esmagamento.DanoPorSegundo(f):0.###} de dano/s");
		AfirmarSala("...e rende 4x de treino em troca (razao 2 = 4x)",
					Math.Abs(f.weight - 4) < 0.05, $"{f.weight:0.###}x");

		// O PISO DO DM (`if(mobTime < 0.1) mobTime = 0.1`): peso sozinho ARRASTA, nao congela. Quem
		// para de vez e o esmagamento, que e outra regra e tem outro gatilho (secao 10).
		f.Weighted = f.weight_cap_hw * 100;
		f.WeightTick();
		f.Statify();
		AfirmarSala("mesmo com peso absurdo o passo nao chega a zero (o piso do `movement handler`)",
					Esmagamento.AtrasoDoPeso(f) > 0, $"{Esmagamento.AtrasoDoPeso(f):0.####}");

		AjustarPeso(b, "0");
		AfirmarSala("tirar os pesos devolve o passo inteiro",
					Math.Abs(Esmagamento.FatorDePasso(f) - 1) < 1e-9 && Math.Abs(f.weight - 1) < 1e-9,
					$"fator {Esmagamento.FatorDePasso(f):0.###}, peso {f.weight:0.##}x");
	}

	// =====================================================================
	// 10) O ESMAGAMENTO COBRA -- e o teto dele DISPARA
	// =====================================================================
	/// <summary>
	/// SEM ISTO, GRAVIDADE ALTA E GANHO DE GRACA. O port pagava o premio (`GravGain` escala com a
	/// gravidade absoluta) e cobrava so o `gravFelt`, que reduz o poder EXPRESSO -- visivel apenas no
	/// scouter. Um planeta de gravidade 80 era o melhor lugar do jogo sem nenhuma contrapartida.
	///
	/// A bancada percorre as tres faixas do `Grav_Handler` e exige que **o teto dispare** (PARTE 0.7:
	/// um teto que nunca e atingido e indistinguivel de teto nenhum) e que **a prisao chegue ao funil
	/// de movimento** -- que e a unica metade que se ve em jogo.
	/// </summary>
	private void OEsmagamentoCobra(ServerPlayer b)
	{
		Fighter f = b.Ficha;
		f.Weighted = 0;
		f.gravmult = 0;
		f.GravMastered = 10;
		f.Planetgrav = 10;
		f.Statify();
		f.WeightTick();

		AfirmarSala("na propria maestria nao ha esmagamento (razao 1)",
					!Esmagamento.Esmaga(f) && Esmagamento.DanoPorSegundo(f) == 0, $"{Esmagamento.Razao(f):0.##}");

		// --- o dobro da maestria: dano, freio pela metade, e AINDA anda ---
		f.Planetgrav = 20;
		AfirmarSala("no DOBRO da maestria o corpo perde vida por segundo",
					Esmagamento.DanoPorSegundo(f) > 0, $"{Esmagamento.DanoPorSegundo(f):0.###}/s");
		AfirmarSala("...e anda pela METADE (o `mobTime /= 1 + (r-1)*GRAVCRUSH_SLOW`)",
					Math.Abs(Esmagamento.FatorDePasso(f) - 0.5) < 1e-9, $"{Esmagamento.FatorDePasso(f):0.###}");
		AfirmarSala("...mas ainda ANDA (a prisao e so a partir de 4x)",
					!Esmagamento.Prende(f) && PodeMexerOCorpo(b));

		// --- o teto de dano DISPARA ---
		f.Planetgrav = 10_000;
		AfirmarSala($"o teto de dano dispara de verdade ({Esmagamento.DanoTeto}/s, `GRAVCRUSH_DMG_CAP`)",
					Math.Abs(Esmagamento.DanoPorSegundo(f) - Esmagamento.DanoTeto) < 1e-9,
					$"{Esmagamento.DanoPorSegundo(f):0.###}");

		// --- 4x: PRESO NO CHAO, e o funil de movimento obedece ---
		f.Planetgrav = 40;
		AfirmarSala("a 4x da maestria o corpo fica PRESO (`gravParalysis`, Gravity.dm:67)",
					Esmagamento.Prende(f));
		AfirmarSala("...e o funil de movimento do servidor RECUSA o passo (o mesmo que a IA obedece)",
					!PodeMexerOCorpo(b));

		// --- e o tique de producao cobra mesmo ---
		double vidaAntes = f.HP;
		double vigorAntes = f.stamina = f.maxstamina;
		TickDoEsmagamento();
		AfirmarSala("o tique de 1 Hz tira vida de verdade", b.Ficha.HP < vidaAntes,
					$"{vidaAntes:0.##}% -> {b.Ficha.HP:0.##}%");
		AfirmarSala("...e drena folego junto (`stamina -= maxstamina*0.002*r`)",
					b.Ficha.stamina < vigorAntes, $"{vigorAntes:0.#} -> {b.Ficha.stamina:0.#}");

		// --- SAIR SOLTA O CORPO, e sem ninguem apagar bit nenhum ---
		f.Planetgrav = 1;
		AfirmarSala("voltar pra gravidade normal SOLTA o corpo no mesmo instante",
					!Esmagamento.Prende(f) && PodeMexerOCorpo(b));

		f.GravMastered = 1;
		f.Statify();
		f.WeightTick();
		RecalcularVelocidade(b);

		NinguemNasceEsmagado();
	}

	/// <summary>
	/// ============================ A DIVIDA QUE O ESMAGAMENTO COBROU ============================
	/// `race.dm:130-131` (`GravMastered = max(GravMastered, PlanetGravity(spawnPlanet))`) era CITADO
	/// em quatro comentarios deste port -- inclusive pra justificar o teto de 15g dos bercos
	/// sorteados -- e nunca executado. Sem esmagamento no jogo, isso nao tinha sintoma nenhum.
	///
	/// Com esmagamento, e a vida do personagem: **Icer Planet puxa 15x** e a maestria de berco do
	/// Frost Demon valia 1. Razao 15, quase quatro vezes o que ja PRENDE o corpo -- ele nasceria
	/// imovel, perdendo 3 de vida por segundo, sem ter dado um passo. O mesmo pro Heran (Hera, 10x).
	///
	/// A bancada percorre TODOS os planetas do catalogo com gravidade acima de 1 e exige que um
	/// corpo recem-posto ali saia andando. E o teto dispara de verdade nesta lista: sem a
	/// aclimatacao, Icer (15x) e Hera/Vegeta/Inferno (10x) reprovariam.
	/// =========================================================================================
	/// </summary>
	private void NinguemNasceEsmagado()
	{
		if (_planetas == null) { AfirmarSala("(sem planetas.json: a checagem de berco nao roda)", false); return; }

		int pesados = 0, presos = 0;
		string piores = "";
		foreach (FichaDePlaneta ficha in _planetas.Todas)
		{
			double g = ficha.Gravidade;
			string nome = ficha.Nome;
			if (g <= 1) continue;
			pesados++;

			// O CORPO SAI DA MESMA FABRICA DE SEMPRE (`Birth.Nascer`) e e aclimatado pelo caminho de
			// producao (`Birth.AclimatarAoBerco`, o que o `AplicarGravidade` chama). Nada de cravar
			// `GravMastered` a mao: e justamente a atribuicao que faltava.
			var f = new Fighter { Race = "Human", BP = 100, GravMastered = Jandirus.Core.Races.Birth.GravidadeNatal("Human") };
			Jandirus.Core.Races.Birth.AclimatarAoBerco(f, g);
			f.Planetgrav = g;
			f.Tick();

			if (!Esmagamento.Prende(f)) continue;
			presos++;
			piores += $" {nome}({g:0}x)";
		}

		AfirmarSala($"NINGUEM NASCE PRESO NO CHAO em nenhum dos {pesados} planetas de gravidade alta "
					+ "(o `race.dm:131` que era so citado)", presos == 0, piores);
		AfirmarSala("...e a lista de gravidade alta nao esta vazia (o teto desta checagem dispara)",
					pesados >= 3, $"{pesados} planetas acima de 1x");
	}

	// =====================================================================
	// 11) O NUMERO CHEGA NA TELA
	// =====================================================================
	/// <summary>
	/// ============================ SEM ESTA SECAO O SISTEMA E INVISIVEL ============================
	/// Peso, gravidade e Sala mudam quanto BP entra por tique, e BP por tique nao se ve -- o numero da
	/// tela e o mesmo antes e depois, so muda mais rapido. A queixa que abriu a camada 2 e exatamente
	/// essa. O multiplicador na aba Stats E o sistema, do ponto de vista do jogador.
	///
	/// E o leitor mora na OUTRA maquina, entao nao basta a conta estar certa: o pacote tem que SAIR.
	/// A bancada le a ficha lenta que passou pelo fio (<see cref="EscutaDeAtributos"/>), como a do
	/// `--disciplinaformateste` faz -- e olha a assinatura de deduplicacao, que e onde um campo novo
	/// costuma ficar preso: se o `MandarAtributos` nao o incluir na assinatura, o multiplicador muda
	/// no servidor e nunca mais sai no fio, sem erro e sem log.
	/// ==========================================================================================
	/// </summary>
	private void OMultiplicadorChegaNaTela(ServerPlayer b)
	{
		Fighter f = b.Ficha;
		f.Weighted = 0;
		f.gravmult = 0;
		f.GravMastered = 1;
		f.Planetgrav = 1;
		f.zoneGainMult = 1;
		f.Statify();
		f.WeightTick();

		EscutaDeAtributos = [];
		try
		{
			b.SigAtributos = "";
			MandarAtributos(b);
			AfirmarSala("a ficha lenta sai no fio com o multiplicador de treino",
						EscutaDeAtributos.Count == 1 && EscutaDeAtributos[^1].GanhoDeTreino > 0,
						$"{EscutaDeAtributos.Count} pacote(s)");
			double naTerra = EscutaDeAtributos[^1].GanhoDeTreino;

			// NADA MUDOU: o pacote NAO pode sair de novo. E o outro lado da assinatura -- um campo
			// que muda por fracoes a cada tique transformaria esta ficha, que existe pra sair raro,
			// numa ficha de 30 Hz.
			MandarAtributos(b);
			AfirmarSala("...e nao sai de novo enquanto nada muda", EscutaDeAtributos.Count == 1,
						$"{EscutaDeAtributos.Count} pacote(s)");

			// ENTRAR NA SALA: gravidade 10 e ritmo 280 -> 2800x, e o numero TEM que sair.
			f.Planetgrav = 10;
			f.zoneGainMult = GainKnobs.TimeChamberMult;
			f.Statify();
			f.WeightTick();
			MandarAtributos(b);

			Jandirus.Net.Protocol.AtributosState ultimo = EscutaDeAtributos[^1];
			AfirmarSala("mudar de zona FAZ o pacote sair de novo (o campo esta na assinatura)",
						EscutaDeAtributos.Count == 2, $"{EscutaDeAtributos.Count} pacote(s)");
			AfirmarSala($"e o numero que chega na tela e ~2800x -- gravidade 10 x ritmo 280 "
						+ $"(medido: {ultimo.GanhoDeTreino:0}x, contra {naTerra:0.##}x na Terra)",
						ultimo.GanhoDeTreino > naTerra * 2000, $"{ultimo.GanhoDeTreino:0.##}");

			// AS PARTES VIAJAM JUNTO, e elas sao o que torna o numero acionavel: "2800x" sozinho e
			// magico, "2800x (10x grav, Sala 280x)" diz o que mudar.
			AfirmarSala("...e as PARTES viajam junto (gravidade e ritmo da zona, pra frase da tela)",
						Math.Abs(ultimo.Gravidade - 10) < 0.01 && Math.Abs(ultimo.ZonaMult - 280) < 0.01,
						$"grav {ultimo.Gravidade:0.#} / zona {ultimo.ZonaMult:0}");

			// A RAZAO DE ESMAGAMENTO TAMBEM VIAJA, e nao e enfeite: e por ELA que o cliente descobre
			// que esta preso no chao e para de tentar andar (`SheetState.Estado` nao tem bit livre).
			// Sem este campo no fio, o corpo do jogador tremeria -- cliente empurrando, servidor
			// corrigindo trinta vezes por segundo.
			f.GravMastered = 1;
			f.Planetgrav = 40;
			f.Statify();
			f.WeightTick();
			b.SigAtributos = "";
			MandarAtributos(b);
			AfirmarSala("a razao de esmagamento chega ao cliente (e o que trava o passo dele)",
						EscutaDeAtributos[^1].Esmagamento >= Esmagamento.RazaoQuePrende,
						$"{EscutaDeAtributos[^1].Esmagamento:0.##}x");
		}
		finally
		{
			EscutaDeAtributos = null;
			f.Planetgrav = 1;
			f.GravMastered = 1;
			f.zoneGainMult = 1;
			f.Statify();
			f.WeightTick();
		}
	}

	// =====================================================================
	// AS TRES SECOES DA CAMADA 3 (13.5 + 13.6) -- A SESSAO, A COMIDA E A PRISAO
	// =====================================================================
	/// <summary>
	/// COLOCA O CORPO EM FRENTE A PORTA E ENTRA PELO CAMINHO DE PRODUCAO.
	///
	/// Pelo `sala_entrar` e nao "movendo o corpo pro z13 na mao", porque e a ENTRADA que zera a
	/// conta de dias, arma a recarga, consome a chave e poe a comida no chao -- uma bancada que
	/// pulasse a porta testaria uma sessao que nenhum jogador consegue comecar.
	/// </summary>
	private void EntrarPelaPortaDeVerdade(ServerPlayer p, ZoneKey lookout)
	{
		ZoneList(p.Zone.Hash).Remove(p);
		p.Zone = lookout;
		ZoneList(lookout.Hash).Add(p);
		p.Pos = new Vec2(124 * ZoneCollision.TileSize + 16, 81 * ZoneCollision.TileSize + 16);
		p.SalaAutorizada = true;
		p.SalaUltimaEntrada = 0;
		p.SalaPreso = false;
		p.SalaDiasDaSessao = 0;
		p.SalaJanelaAte = 0;
		p.Ficha.dead = p.Ficha.KO = false;
		p.Voando = false;
		EscutaDeAvisos?.Clear();
		ComandoDaSalaDoTempo(p, "sala_entrar", "");
	}

	/// <summary>Roda o tique da sessao por N minutos REAIS, com o passo de um segundo.</summary>
	private void TiquesDeMinutos(double minutos)
	{
		for (int i = 0; i < (int)(minutos * 60); i++) TickDaSessaoDaSala(1.0);
	}

	// =====================================================================
	// 12) A SESSAO ANDA EM DIAS IN-GAME, POR TIQUE
	// =====================================================================
	/// <summary>
	/// ============================ A UNIDADE E A REGRA ============================
	/// "48 minutos" e o que o dono diz, mas nao e o que o codigo conta: o relogio deste mundo anda um
	/// dia a cada 24 min (`Ceu.SegundosPorDia`), entao a sessao sao **2 DIAS IN-GAME** -- os mesmos 2
	/// anos de treino do original. Esta secao mede nas duas unidades de proposito: ela conta minutos
	/// de tique e afirma DIAS, que e o unico jeito de o teste continuar valendo se o dia do mundo
	/// mudar de tamanho.
	///
	/// E ELA COBRA O TETO (PARTE 0.7). Sao tres tetos aqui, e nenhum deles disparava sozinho:
	///   * a sessao acaba mesmo (2 dias) e os bonus SOMEM COM O CORPO AINDA DENTRO -- que e a regra
	///     do dono: a sala nao expulsa;
	///   * a idade sobe 1 ano por dia fechado, e ela chega na FICHA (e nao so no save);
	///   * a TRAVA DE PAREDE fecha a sessao de quem congelou a contagem por tique (deslogado la
	///     dentro), e essa e a unica rede de seguranca do sistema.
	/// =============================================================================
	/// </summary>
	private void ASessaoAndaEmDiasInGame(ServerPlayer a, ServerPlayer b, ZoneKey lookout)
	{
		_naSala.Clear();
		EntrarPelaPortaDeVerdade(a, lookout);
		AfirmarSala("a sessao comeca ZERADA na entrada (`htc_session_years = 0`)",
					a.SalaDiasDaSessao == 0 && a.Zone.Name == ZonaDaSala, $"{a.SalaDiasDaSessao}");

		int idadeAntes = a.Idade;
		double mpd = Jandirus.Core.World.SalaDoTempo.MinutosReaisPorDia;

		// UM DIA IN-GAME **E UM PELO A MAIS**: o ano de idade entra quando o dia FECHA, e parar
		// cravado em 1,0 e parar em cima da fronteira -- o passo de ponto flutuante cai em
		// 0,999999... e a bancada reprovaria uma regra certa. Ver `CorrerASessao`.
		TiquesDeMinutos(mpd + 0.1);
		AfirmarSala($"depois de {mpd:0} min de tique a conta marca UM dia in-game "
					+ $"(medido: {a.SalaDiasDaSessao:0.###})",
					Math.Abs(a.SalaDiasDaSessao - 1) < 0.02, $"{a.SalaDiasDaSessao}");
		AfirmarSala("...e o dia fechado envelheceu UM ano (`HTC_AGE_PER_GAME_DAY`)",
					a.Idade == idadeAntes + 1, $"{idadeAntes} -> {a.Idade}");
		AfirmarSala("...e a idade nova chegou na FICHA, que e quem calcula poder (o `AgeDiv`)",
					Math.Abs(a.Ficha.Idade - a.Idade) < 1e-9, $"ficha {a.Ficha.Idade} x jogador {a.Idade}");
		AfirmarSala("...e a sessao AINDA rende no primeiro dia",
					Math.Abs(a.Ficha.zoneGainMult - GainKnobs.TimeChamberMult) < 1e-9, $"{a.Ficha.zoneGainMult}");

		TiquesDeMinutos(mpd + 0.2);   // o segundo dia, e um empurraozinho pra fechar
		AfirmarSala($"aos {Jandirus.Core.World.SalaDoTempo.SessaoEmMinutosReais:0} min "
					+ $"({Jandirus.Core.World.SalaDoTempo.SessaoEmDias:0} dias in-game) a sessao ACABA",
					!Jandirus.Core.World.SalaDoTempo.SessaoValendo(a.SalaDiasDaSessao),
					$"{a.SalaDiasDaSessao:0.###} dias");
		AfirmarSala("...e a sessao inteira envelheceu 2 anos", a.Idade == idadeAntes + 2,
					$"{idadeAntes} -> {a.Idade}");

		// ============================ OS BONUS ACABAM COM O CORPO AINDA DENTRO ============================
		// E a regra do dono inteira num par de linhas: o jogador continua no z13 (nao foi expulso) e
		// o ritmo caiu pra 1. Se o `AplicarRitmo` olhasse so a ZONA, este e o teste que nao passaria.
		// ==============================================================================================
		AfirmarSala("O CORPO CONTINUA DENTRO -- a sala nao expulsa (13.6c)",
					string.Equals(a.Zone.Name, ZonaDaSala, StringComparison.OrdinalIgnoreCase), a.Zone.Name);
		AfirmarSala("...mas o ganho de BP caiu pra 1x DENTRO DA SALA",
					Math.Abs(a.Ficha.zoneGainMult - 1) < 1e-9, $"{a.Ficha.zoneGainMult}");
		AfirmarSala("...e a maestria de forma tambem", Math.Abs(a.Ficha.zoneMasteryMult - 1) < 1e-9,
					$"{a.Ficha.zoneMasteryMult}");

		// ============================ CADA UM TEM O PROPRIO CRONOMETRO ============================
		// Regra 13.6a: a dupla pode entrar em momentos diferentes, e o segundo NAO herda o relogio do
		// primeiro. E a resposta coerente porque a comida e a prisao sao por pessoa -- um cronometro
		// compartilhado faria da segunda entrada uma armadilha (entrar no minuto 47 e ser preso no 50).
		// =======================================================================================
		double diasDoPrimeiro = a.SalaDiasDaSessao;
		ServerPlayer segundo = b;
		EntrarPelaPortaDeVerdade(segundo, lookout);
		AfirmarSala("o SEGUNDO a entrar comeca a sessao do zero (cada um tem o proprio cronometro)",
					segundo.SalaDiasDaSessao == 0 && diasDoPrimeiro > 0,
					$"primeiro {diasDoPrimeiro:0.##} dias, segundo {segundo.SalaDiasDaSessao:0.##}");
		TiquesDeMinutos(1);
		AfirmarSala("...e os dois relogios andam SEPARADOS",
					a.SalaDiasDaSessao > segundo.SalaDiasDaSessao,
					$"{a.SalaDiasDaSessao:0.###} x {segundo.SalaDiasDaSessao:0.###}");

		// o segundo sai de cena: as secoes seguintes contam com a sala vazia
		ZoneList(segundo.Zone.Hash).Remove(segundo);
		segundo.Zone = lookout;
		ZoneList(lookout.Hash).Add(segundo);
		segundo.SalaDiasDaSessao = 0;
		AplicarGravidade(segundo);
		TickDaSalaDoTempo();

		// ============================ A TRAVA DE PAREDE DISPARA ============================
		// A contagem por tique PARA quando o corpo nao esta em jogo, entao sem esta rede bastaria
		// deslogar pra congelar os 280x. Aqui a conta de dias e zerada a mao (que e o que "o tique
		// nunca correu" significa) e o relogio de parede e envelhecido -- um unico tique tem que
		// fechar a sessao assim mesmo.
		// ================================================================================
		a.SalaDiasDaSessao = 0;
		a.SalaJanelaAte = 0;
		a.SalaPreso = false;
		AplicarGravidade(a);
		a.SalaUltimaEntrada = NowMs() - (long)Jandirus.Core.World.SalaDoTempo.TravaDeParedeEmMs - 1;
		TickDaSessaoDaSala(1.0);
		AfirmarSala("A TRAVA DE PAREDE fecha a sessao de quem congelou a contagem por tique",
					!Jandirus.Core.World.SalaDoTempo.SessaoValendo(a.SalaDiasDaSessao),
					$"{a.SalaDiasDaSessao:0.###} dias");
		AfirmarSala("...e com ela o ritmo da zona cai junto",
					Math.Abs(a.Ficha.zoneGainMult - 1) < 1e-9, $"{a.Ficha.zoneGainMult}");
	}

	// =====================================================================
	// 13) A COMIDA: DUAS PORCOES, REPOSTAS PELO GESTO
	// =====================================================================
	/// <summary>
	/// ============================ O QUE ESTA SECAO TEM QUE SEPARAR ============================
	/// "A comida repoe" e uma frase que passa em teste de tres jeitos diferentes, e dois deles sao a
	/// regra errada: repor por RELOGIO (o que a macieira faz) e repor SEM TETO. A regra do dono
	/// (13.6b) e a terceira: teto de duas porcoes e uma nova **so quando alguem come uma**.
	///
	/// Entao a bancada roda o tique da sessao SEM ninguem comer e exige que nada nasca -- e essa e a
	/// checagem que distingue as tres. Depois come, e exige que nasca na hora.
	/// =======================================================================================
	/// </summary>
	private void AComidaReponPorConsumo(ServerPlayer a, ZoneKey lookout)
	{
		_naSala.Clear();
		RecolherComidaDaSala();
		EntrarPelaPortaDeVerdade(a, lookout);

		List<Obra> Porcoes() =>
			[.. _noChao.Where(o => o.Tipo == "Cooked_Meat" && o.Zona.Equals(ZoneKey.Premade(ZonaDaSala)))];

		AfirmarSala("entrar poe DUAS porcoes de comida no chao da sala", Porcoes().Count == 2,
					$"{Porcoes().Count} porcao(oes)");

		// PERTO DA PORTA, E EM CHAO LIVRE. Comida dentro da parede seria invisivel e inalcancavel, e
		// comida longe da porta nao empurra ninguem pra lugar nenhum.
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(ZoneKey.Premade(ZonaDaSala));
		bool livres = mapa != null && Porcoes().All(o => !mapa.BlockedAt(new Vec2(o.X, o.Y + MoveRules.FeetOffsetY)));
		AfirmarSala("...em celulas LIVRES", livres);
		bool perto = Porcoes().All(o => Math.Abs(o.X - a.Pos.X) <= AlcanceDeUso
									 && Math.Abs(o.Y - a.Pos.Y) <= AlcanceDeUso);
		AfirmarSala("...e ao ALCANCE de quem acabou de atravessar a porta (o `AlcanceDeUso`)", perto,
					string.Join(" ", Porcoes().Select(o => $"({(o.X - a.Pos.X):0},{(o.Y - a.Pos.Y):0})")));

		// ============================ NAO E TEMPORIZADOR -- E A MESA PRECISA TER VAGA ============================
		// ESTA CHECAGEM ERA VAZIA, e o jeito de descobrir isso foi injetar o defeito que ela existe
		// pra pegar: com um `AbastecerComidaDaSala()` dentro do tique -- reposicao por RELOGIO, que e
		// exatamente a regra errada -- ela continuava VERDE. O motivo e bobo e e o de sempre: a mesa
		// ja estava CHEIA (duas de duas), entao repor por relogio nao tinha o que repor, e "nada
		// nasceu" era verdade pelos dois sistemas.
		//
		// Um teto que ja esta cheio nao distingue nada (PARTE 0.7 outra vez). Entao a bancada ABRE uma
		// vaga na mesa antes de rodar o relogio -- e uma vaga que NAO foi aberta pelo gesto de comer,
		// que e a unica coisa que tem direito de repor. Cinco minutos depois ela tem que continuar
		// aberta.
		// =====================================================================================================
		_noChao.Remove(Porcoes()[0]);
		int comBuraco = Porcoes().Count;
		AfirmarSala("(a mesa ficou com UMA VAGA ABERTA -- sem ela a checagem seguinte nao mede nada)",
					comBuraco == PorcoesDaSala - 1, $"{comBuraco} de {PorcoesDaSala}");

		TiquesDeMinutos(5);
		AfirmarSala("cinco minutos de tique NAO enchem a vaga aberta (a reposicao nao e relogio)",
					Porcoes().Count == comBuraco, $"{comBuraco} -> {Porcoes().Count}");

		// ============================ E O CONSUMO REPOE, NA HORA ============================
		int[] idsAntes = [.. Porcoes().Select(o => o.Id)];
		a.Ficha.CurrentNutrition = 0;
		EscutaDeAvisos?.Clear();
		ComandoDaSalaDoTempo(a, "sala_comer", "");
		// O NUMERO E O DO DM, e nao "mais que zero": `Cooked_Meat` tem `nutrition = 30`
		// (`Modules/Stamina/Food.dm:105`) contra 6 da maca, e o tanque cheio e 50
		// (`Nutricao.TanqueBase`). Uma porcao que enchesse 1 passaria em "> 0" e faria a dupla comer
		// as duas porcoes de uma vez sem sair da fome -- a mesa da sala e uma REFEICAO.
		AfirmarSala($"comer uma porcao enche o estomago no valor do DM ({NutricaoDaPorcao:0} de nutricao)",
					Math.Abs(a.Ficha.CurrentNutrition - NutricaoDaPorcao) < 0.01,
					$"{a.Ficha.CurrentNutrition:0.#}");
		AfirmarSala("...e uma nova nasce no lugar NO MESMO GESTO (reposicao por consumo)",
					Porcoes().Count == 2, $"{Porcoes().Count}");
		AfirmarSala("...e ela e uma porcao NOVA, e nao a mesma de antes",
					Porcoes().Any(o => Array.IndexOf(idsAntes, o.Id) < 0),
					string.Join(",", Porcoes().Select(o => o.Id)));

		// O TETO NUNCA E ULTRAPASSADO -- comer dez vezes nao enche o chao de comida.
		int maximo = 2;
		for (int i = 0; i < 10; i++)
		{
			a.Ficha.CurrentNutrition = 0;
			ComandoDaSalaDoTempo(a, "sala_comer", "");
			maximo = Math.Max(maximo, Porcoes().Count);
		}
		AfirmarSala("dez refeicoes depois o teto de 2 porcoes simultaneas continua de pe", maximo == 2,
					$"pico de {maximo}");

		// ============================ A PORCAO DESTRUIDA NAO ENTOPE O TETO ============================
		// Porcao e OBRA, e obra cai no soco: o `GameServer.Estrago.cs` a tira do `_noChao` sem avisar
		// ninguem. A primeira versao guardava os ids numa lista paralela, e o id de uma porcao
		// destruida ficaria la pra sempre -- o teto de duas viveria cheio e a comida **nunca mais
		// nasceria**, calada. Aqui a destruicao e simulada do jeito que ela acontece de verdade.
		// ==========================================================================================
		_noChao.Remove(Porcoes()[0]);
		a.Ficha.CurrentNutrition = 0;
		ComandoDaSalaDoTempo(a, "sala_comer", "");
		AfirmarSala("uma porcao DESTRUIDA (e nao comida) nao entope o teto: a mesa se refaz",
					Porcoes().Count == 2, $"{Porcoes().Count} porcao(oes)");

		// ============================ AOS 48 MIN A COMIDA SOME ============================
		// E o que empurra a dupla pra porta no lugar da expulsao do original.
		a.SalaDiasDaSessao = Jandirus.Core.World.SalaDoTempo.SessaoEmDias;
		TickDaSessaoDaSala(1.0);
		AfirmarSala("com a sessao acabada a comida SOME do chao (13.6b)", Porcoes().Count == 0,
					$"{Porcoes().Count} porcao(oes)");
		AfirmarSala("...e comer deixa de existir como acao (nao ha o que comer perto)",
					ObraQueAceita(a, "sala_comer") == null);
	}

	// =====================================================================
	// 14) A PRISAO FECHA A PORTA -- e so o Guardiao (ou o admin) abre
	// =====================================================================
	/// <summary>
	/// ============================ A METADE QUE FALTAVA DA TRANCA ============================
	/// A porta de ENTRADA ja recusava quem esta preso desde a camada 1, e o Guardiao ja soltava. O
	/// que nunca existiu foi o lado de DENTRO: a saida da sala e uma PASSAGEM (chao que leva), e
	/// passagem nao pergunta nada a ninguem -- o preso sairia andando por cima dela.
	///
	/// Esta secao roda o `TickDasPassagens` de producao com o corpo em cima da celula de saida
	/// (144..147 x 338 do z13, o `fromhbtc`), tres vezes: rendendo, na janela, e preso.
	/// =====================================================================================
	/// </summary>
	private void APrisaoFechaAPorta(ServerPlayer a, ServerPlayer kami, ZoneKey lookout)
	{
		_naSala.Clear();
		RecolherComidaDaSala();

		// EM CIMA DA CELULA DE SAIDA: a passagem le a celula dos PES (`FeetOffsetY`), como a colisao.
		void NaSaida()
		{
			a.Pos = new Vec2(145 * ZoneCollision.TileSize + 16, 338 * ZoneCollision.TileSize + 8);
			_acabouDeAtravessar.Remove(a.Id);
			_avisoDePrisao.Remove(a.Id);
		}

		// --- 1) com a sessao rendendo, a saida funciona ---
		EntrarPelaPortaDeVerdade(a, lookout);
		NaSaida();
		TickDasPassagens();
		AfirmarSala("com a sessao rendendo, pisar na saida devolve pro Templo Sagrado",
					string.Equals(a.Zone.Name, "Lookout", StringComparison.OrdinalIgnoreCase), a.Zone.Name);

		// --- 2) na JANELA (sessao acabada, ainda solto) tambem funciona ---
		EntrarPelaPortaDeVerdade(a, lookout);
		a.SalaDiasDaSessao = Jandirus.Core.World.SalaDoTempo.SessaoEmDias;
		TickDaSessaoDaSala(1.0);        // este tique ABRE a janela
		AfirmarSala("acabada a sessao, a JANELA de saida e armada (e nao a tranca)",
					!a.SalaPreso && a.SalaJanelaAte > 0, $"preso={a.SalaPreso} janela={a.SalaJanelaAte}");
		NaSaida();
		TickDasPassagens();
		AfirmarSala($"...e dentro dela ({Jandirus.Core.World.SalaDoTempo.MinutosDaJanela:0} min) "
					+ "a saida ainda funciona",
					string.Equals(a.Zone.Name, "Lookout", StringComparison.OrdinalIgnoreCase), a.Zone.Name);

		// --- 3) passada a janela: PRESO, e a passagem recusa ---
		EntrarPelaPortaDeVerdade(a, lookout);
		a.SalaDiasDaSessao = Jandirus.Core.World.SalaDoTempo.SessaoEmDias;
		TickDaSessaoDaSala(1.0);                          // arma a janela
		a.SalaJanelaAte = NowMs() - 1;                    // ...e o relogio dela vence
		EscutaDeAvisos?.Clear();
		TickDaSessaoDaSala(1.0);
		AfirmarSala("PASSADA A JANELA, o tique PRENDE (o bit que ninguem escrevia)", a.SalaPreso);
		AfirmarSala("...e a mensagem diz quem solta",
					string.Join(" | ", EscutaDeAvisos ?? []).Contains("administrador", StringComparison.OrdinalIgnoreCase),
					UltimoAviso());

		NaSaida();
		EscutaDeAvisos?.Clear();
		TickDasPassagens();
		AfirmarSala("A SAIDA RECUSA QUEM ESTA PRESO -- o corpo nao sai do z13",
					string.Equals(a.Zone.Name, ZonaDaSala, StringComparison.OrdinalIgnoreCase), a.Zone.Name);
		AfirmarSala("...e a recusa explica por que, e quem abre",
					UltimoAviso().Contains("preso", StringComparison.OrdinalIgnoreCase), UltimoAviso());

		// ============================ RELOGAR NAO SOLTA ============================
		// A regra do dono diz isso com todas as letras. A prova e a ida e volta pelo disco, e nao a
		// leitura do objeto em memoria -- e a mesma armadilha dos `Limiares`.
		// ========================================================================
		CharacterSave s = AccountStore.DeJogador(a, NowMs());
		a.SalaPreso = false;
		AccountStore.ParaJogador(s, a);
		AfirmarSala("RELOGAR NAO SOLTA: a prisao volta do disco", a.SalaPreso);

		// ...E O GUARDIAO SOLTA. O verbo ja existia; o que faltava era alguem PRESO de verdade pra
		// ele soltar -- ate esta camada, o bit era sempre escrito a mao pela propria bancada.
		_tronos["guardian"] = kami.Conta;
		ComandoDaSalaDoTempo(kami, "sala_soltar", a.Id.ToString());
		AfirmarSala("o Guardiao da Terra solta quem a SESSAO prendeu", !a.SalaPreso);

		NaSaida();
		TickDasPassagens();
		AfirmarSala("...e ai a saida volta a funcionar (soltar nao teleporta: ele sai andando)",
					string.Equals(a.Zone.Name, "Lookout", StringComparison.OrdinalIgnoreCase), a.Zone.Name);

		// ============================ E O NUMERO CHEGA NA TELA ============================
		// Um prazo com castigo no fim cujo unico sinal e uma frase que ja rolou pra cima no chat e
		// uma armadilha. As tres fases tem que sair no fio -- e o campo tem que estar na ASSINATURA
		// de deduplicacao, senao ele muda no servidor e nunca mais sai.
		// ==============================================================================
		EscutaDeAtributos = [];
		try
		{
			EntrarPelaPortaDeVerdade(a, lookout);
			a.SigAtributos = "";
			MandarAtributos(a);
			AfirmarSala("a ficha lenta leva a fase da sessao pra tela (1 = rendendo)",
						EscutaDeAtributos.Count > 0 && EscutaDeAtributos[^1].SalaFase == 1,
						$"fase {(EscutaDeAtributos.Count > 0 ? EscutaDeAtributos[^1].SalaFase : -1)}");
			AfirmarSala($"...com os minutos que faltam (~{Jandirus.Core.World.SalaDoTempo.SessaoEmMinutosReais:0})",
						EscutaDeAtributos[^1].SalaMinutos > Jandirus.Core.World.SalaDoTempo.SessaoEmMinutosReais - 1,
						$"{EscutaDeAtributos[^1].SalaMinutos:0.#} min");

			int pacotes = EscutaDeAtributos.Count;
			a.SalaDiasDaSessao = Jandirus.Core.World.SalaDoTempo.SessaoEmDias;
			TickDaSessaoDaSala(1.0);
			AfirmarSala("virar a fase FAZ o pacote sair de novo (o campo esta na assinatura)",
						EscutaDeAtributos.Count > pacotes && EscutaDeAtributos[^1].SalaFase == 2,
						$"{pacotes} -> {EscutaDeAtributos.Count} pacote(s), fase {EscutaDeAtributos[^1].SalaFase}");

			a.SalaJanelaAte = NowMs() - 1;
			TickDaSessaoDaSala(1.0);
			AfirmarSala("...e a PRISAO tambem chega na tela (fase 3)",
						EscutaDeAtributos[^1].SalaFase == 3, $"fase {EscutaDeAtributos[^1].SalaFase}");
		}
		finally { EscutaDeAtributos = null; }

		// A BANCADA TERMINA COMO COMECOU: o corpo fora, solto, sem sessao e sem comida no chao.
		a.SalaPreso = false;
		a.SalaDiasDaSessao = 0;
		a.SalaJanelaAte = 0;
		RecolherComidaDaSala();
		ZoneList(a.Zone.Hash).Remove(a);
		a.Zone = lookout;
		ZoneList(lookout.Hash).Add(a);
		AplicarGravidade(a);
	}

	// =====================================================================
	// 15) A RECARGA DURA UM DIA REAL -- e o relogio dela e medido, nao lido
	// =====================================================================
	/// <summary>
	/// ============================ O BURACO QUE ESTA SECAO FECHA ============================
	/// A recarga ja era testada -- a secao 3 arma `SalaUltimaEntrada = agora` e cobra que a porta
	/// recuse falando em "tempo real" e em horas. So que essa checagem passa com QUALQUER duracao:
	/// com a recarga em 24 MINUTOS a frase continua saindo ("0h23m" tem 'h' e tem "tempo real") e a
	/// porta continua recusando naquele instante. Ou seja, o unico numero da regra -- 24 h
	/// (`HTC_COOLDOWN_HOURS`) -- nao tinha nenhuma linha que o defendesse.
	///
	/// Aqui a recarga e medida pelas duas beiradas, pelo caminho de producao e com o corpo de
	/// verdade: a UMA HORA do fim a porta recusa **e diz que falta uma hora** (o que so e verdade se
	/// o prazo inteiro for 24 h), e um minuto DEPOIS do fim ela aceita e o corpo se move.
	///
	/// A SEGUNDA METADE E A QUE FALTAVA MESMO. Uma bancada que so provasse a recusa ficaria verde com
	/// uma recarga eterna -- e "entrei uma vez e nunca mais consegui" e um defeito pior que nao ter
	/// recarga nenhuma, porque ele nao tem sintoma nenhum do lado de quem joga.
	/// =====================================================================================
	/// </summary>
	private void ARecargaDuraUmDiaReal(ServerPlayer a, ZoneKey sala, ZoneKey lookout)
	{
		_naSala.Clear();
		RecolherComidaDaSala();

		// EM FRENTE A PORTA de novo: o corpo veio andando da saida do z13 e a primeira guarda do
		// `EntrarNaSala` e a distancia ate a obra.
		void NaPorta()
		{
			ZoneList(a.Zone.Hash).Remove(a);
			a.Zone = lookout;
			ZoneList(lookout.Hash).Add(a);
			a.Pos = new Vec2(124 * ZoneCollision.TileSize + 16, 81 * ZoneCollision.TileSize + 16);
			a.SalaAutorizada = true;
			a.SalaPreso = false;
			a.SalaDiasDaSessao = 0;
			a.SalaJanelaAte = 0;
			a.Ficha.dead = a.Ficha.KO = false;
			a.Voando = false;
			EscutaDeAvisos?.Clear();
		}

		// ============================ AS HORAS SAO LITERAIS, E ISSO E O PONTO ============================
		// A primeira versao desta secao escreveu `MsDeRecargaDaSala - 3_600_000` -- ou seja, ela pedia
		// a duracao a propria constante que devia defender. Com a recarga injetada em 24 MINUTOS a
		// bancada ficou **133/133**: os dois lados da conta encolheram juntos e o teste concordou com o
		// defeito. E a armadilha 4 da PARTE 3 (dois lados calculando a mesma coisa) vestida de bancada.
		//
		// Aqui as duas beiradas sao horas de relogio escritas a mao: 23 h ainda recusa, 24 h e um
		// minuto ja aceita. So um prazo de 24 h passa nas duas.
		// ==============================================================================================
		const long UmaHora = 3_600_000;

		// --- 1) 23 h depois: recusa, e a frase diz que falta UMA HORA ---
		NaPorta();
		// OS 2 SEGUNDOS DE FOLGA NAO SAO ENFEITE: com a marca em exatamente 23 h, o `NowMs()` que a
		// porta le e alguns milissegundos DEPOIS do que a bancada escreveu, e o que falta vira
		// "0h59m". A checagem passava ou reprovava conforme o relogio da maquina -- e uma bancada
		// que pisca e pior que bancada nenhuma. Com a folga, a frase e "1h00m" nos dois casos.
		a.SalaUltimaEntrada = NowMs() - (23 * UmaHora - 2_000);
		ComandoDaSalaDoTempo(a, "sala_entrar", "");
		AfirmarSala("23 h depois da entrada a porta AINDA recusa (a recarga nao acabou)",
					a.Zone.Hash == lookout.Hash, a.Zone.Name);
		AfirmarSala("...e ela diz que falta UMA HORA, que e o que prova que o prazo e de 24 h",
					UltimoAviso().Contains("1h00m", StringComparison.Ordinal), UltimoAviso());
		AfirmarSala("...e a recusa por recarga NAO queima a chave do Guardiao", a.SalaAutorizada);

		// --- 2) 24 h e um minuto depois: a porta aceita, e o corpo se move ---
		NaPorta();
		a.SalaUltimaEntrada = NowMs() - (24 * UmaHora + 60_000);
		ComandoDaSalaDoTempo(a, "sala_entrar", "");
		AfirmarSala("PASSADAS AS 24 H REAIS A PORTA ACEITA DE NOVO (a recarga acaba mesmo)",
					a.Zone.Hash == sala.Hash, $"{a.Zone.Name} / {UltimoAviso()}");
		AfirmarSala("...e a nova entrada RE-ARMA a recarga a partir de agora",
					NowMs() - a.SalaUltimaEntrada < 5_000, $"{NowMs() - a.SalaUltimaEntrada} ms atras");

		// A BANCADA TERMINA COMO COMECOU.
		_naSala.Clear();
		RecolherComidaDaSala();
		a.SalaUltimaEntrada = 0;
		a.SalaDiasDaSessao = 0;
		a.SalaJanelaAte = 0;
		a.SalaAutorizada = false;
		ZoneList(a.Zone.Hash).Remove(a);
		a.Zone = lookout;
		ZoneList(lookout.Hash).Add(a);
		AplicarGravidade(a);
	}

	// =====================================================================
	// 16) O `Goto Spawn` SAIU DAS MAOS DO JOGADOR -- e continua na do admin
	// =====================================================================
	/// <summary>
	/// ============================ A PORTA DOS FUNDOS DA PRISAO ============================
	/// A tranca da Sala atravessa quatro guardas (a porta, a passagem, o relog e o disco) -- e
	/// nenhuma delas valia enquanto existisse `case "spawn"` no despacho comum: um botao na aba
	/// "Other" teleportava qualquer preso pro berco dele, sem gate, sem preco e sem rastro.
	///
	/// O dono resolveu isto: *"tire o verb gotospawn, deixe ele so pra adm"*. Esta secao cobra os
	/// DOIS SENTIDOS, porque so um deles nao e a regra:
	///   * o jogador NAO tem mais o caminho -- nem pelo verbo antigo, nem pelo novo sem ser admin;
	///   * o admin TEM -- tirar de todo mundo tambem "passaria" no primeiro teste, e teria apagado
	///     uma ferramenta em silencio.
	///
	/// E ELA MEDE OS DOIS LADOS DO FIO. O servidor e a autoridade (o `case` que sumiu e o funil de
	/// `admin_`), mas a lista de verbos do CLIENTE e o que o jogador enxerga -- e neste menu a busca
	/// varre TODAS as abas de proposito, entao deixar o verb em "Other" e "esconder" a aba Admin nao
	/// esconderia nada: bastava digitar "spawn". Por isso a checagem do cliente e por CATEGORIA e
	/// pela BUSCA, e nao so "ele existe".
	/// ====================================================================================
	/// </summary>
	private void OGotoSpawnSaiuDasMaosDoJogador(ServerPlayer a, ServerPlayer kami, ZoneKey lookout,
											   ZoneKey sala)
	{
		_naSala.Clear();
		RecolherComidaDaSala();

		// --- o lado do SERVIDOR, com o corpo DENTRO da sala (que e onde o verb doia) ---
		EntrarPelaPortaDeVerdade(a, lookout);
		a.Poderes &= ~Jandirus.Net.Protocol.Poder.Admin;

		EscutaDeAvisos?.Clear();
		Verbo(a, "spawn", "");
		AfirmarSala("O VERBO `spawn` NAO EXISTE MAIS: o jogador manda e nada acontece",
					a.Zone.Hash == sala.Hash, $"{a.Zone.Name} / {UltimoAviso()}");

		// ...E NEM PELO NOME NOVO. O funil de `admin_` e a unica conferencia de permissao do canal
		// de verbos; se ele deixasse passar, ter renomeado o verb so teria trocado a etiqueta.
		EscutaDeAvisos?.Clear();
		Verbo(a, "admin_spawn", "");
		AfirmarSala("...e `admin_spawn` na mao de quem NAO e admin tambem nao move ninguem",
					a.Zone.Hash == sala.Hash, a.Zone.Name);
		AfirmarSala("...e a recusa diz que aquilo e coisa de administrador",
					UltimoAviso().Contains("administrador", StringComparison.OrdinalIgnoreCase),
					UltimoAviso());

		// --- e o admin CONTINUA tendo o caminho ---
		// O `kami` esta no Templo; o berco de um corpo forjado e a zona de recuo viva (a Terra),
		// entao "saiu do Lookout" e a prova de que o comando rodou de verdade.
		bool eraAdmin = (kami.Poderes & Jandirus.Net.Protocol.Poder.Admin) != 0;
		kami.Poderes |= Jandirus.Net.Protocol.Poder.Admin;
		EscutaDeAvisos?.Clear();
		Verbo(kami, "admin_spawn", "");
		AfirmarSala("O ADMIN TEM O VERB: `admin_spawn` leva ele pro proprio berco",
					kami.Zone.Hash != lookout.Hash, $"{kami.Zone.Name} / {UltimoAviso()}");
		AfirmarSala("...e o despacho de admin CONHECE o comando (nao caiu no 'nao existe')",
					!UltimoAviso().Contains("nao existe", StringComparison.OrdinalIgnoreCase),
					UltimoAviso());

		if (!eraAdmin) kami.Poderes &= ~Jandirus.Net.Protocol.Poder.Admin;
		ZoneList(kami.Zone.Hash).Remove(kami);
		kami.Zone = lookout;
		ZoneList(lookout.Hash).Add(kami);
		kami.Pos = new Vec2(124 * ZoneCollision.TileSize + 16, 81 * ZoneCollision.TileSize + 16);

		// --- e o lado do CLIENTE: o menu e a busca ---
		OMenuDoClienteNaoOferece();

		// A BANCADA TERMINA COMO COMECOU.
		_naSala.Clear();
		RecolherComidaDaSala();
		a.SalaUltimaEntrada = a.SalaJanelaAte = 0;
		a.SalaDiasDaSessao = 0;
		a.SalaAutorizada = a.SalaPreso = false;
		ZoneList(a.Zone.Hash).Remove(a);
		a.Zone = lookout;
		ZoneList(lookout.Hash).Add(a);
		AplicarGravidade(a);
	}

	/// <summary>
	/// A LISTA DE VERBOS DO CLIENTE, lida do registro de verdade (`VerbosDoJogo.Registrar`).
	///
	/// ============================ POR QUE O SERVIDOR OLHA UMA COISA DO CLIENTE ============================
	/// Porque o defeito mora exatamente na costura: o servidor pode estar perfeito e o menu continuar
	/// oferecendo o botao -- e ai o jogador clica, o servidor recusa, e a unica coisa que ele aprende
	/// e que o jogo tem um botao quebrado. Pior: a barra de busca deste menu varre TODAS as
	/// categorias, entao um verb esquecido em "Other" continua achavel digitando "spawn" mesmo com a
	/// aba Admin escondida (ver `Verbos.Visivel`).
	///
	/// O REGISTRO E GLOBAL E ELE E DEVOLVIDO NO FIM. `Habilidades.Montar` refaz a lista inteira toda
	/// vez que a ficha lenta chega, entao um cliente de verdade se reconstroi sozinho -- mas deixar
	/// lixo de bancada num estado compartilhado e como a gente perde uma tarde.
	/// ==================================================================================================
	/// </summary>
	private void OMenuDoClienteNaoOferece()
	{
		bool eraAdminNaTela = Jandirus.Client.Verbos.SouAdmin;

		Jandirus.Client.Verbos.Limpar();
		Jandirus.Client.VerbosDoJogo.Limpar();
		Jandirus.Client.VerbosDoJogo.Registrar();

		Jandirus.Client.Verbo? gs =
			Jandirus.Client.Verbos.Todos.FirstOrDefault(v => v.Nome == "Goto Spawn");

		AfirmarSala("o `Goto Spawn` continua EXISTINDO no menu (nao foi apagado, foi mudado de dono)",
					gs != null);
		AfirmarSala("...e ele esta na aba ADMIN, e nao em 'Other'",
					gs?.Categoria == Jandirus.Client.Verbos.Admin, gs?.Categoria ?? "(sumiu)");
		AfirmarSala("...e nenhum verb de 'Other' se chama assim (a aba do jogador ficou limpa)",
					!Jandirus.Client.Verbos.Da(Jandirus.Client.Verbos.Outros)
						.Any(v => v.Nome == "Goto Spawn"));

		// A BUSCA E O BURACO QUE A CATEGORIA SOZINHA NAO TAPA.
		Jandirus.Client.Verbos.DefinirAdmin(false);
		AfirmarSala("um jogador comum digitando 'spawn' na busca NAO acha o verb",
					!Jandirus.Client.Verbos.Buscar("spawn").Any(v => v.Nome == "Goto Spawn"));

		Jandirus.Client.Verbos.DefinirAdmin(true);
		AfirmarSala("...e um admin digitando 'spawn' ACHA (os dois sentidos)",
					Jandirus.Client.Verbos.Buscar("spawn").Any(v => v.Nome == "Goto Spawn"));

		Jandirus.Client.Verbos.DefinirAdmin(eraAdminNaTela);
		Jandirus.Client.Verbos.Limpar();
		Jandirus.Client.VerbosDoJogo.Limpar();
	}

	// =====================================================================
	// 17) MORRER SAI DA SALA -- a saida CARA, e ela limpa o estado
	// =====================================================================
	/// <summary>
	/// ============================ ISTO NAO E UM BURACO: E A DECISAO DO DONO ============================
	/// *"sair da sala do tempo dps de preso so morrendo pra sair"*. A prisao deixou de ser absoluta e
	/// passou a ter um PRECO -- e o preco existe de verdade (karma, o Enma, o revive por Zeni, idade).
	///
	/// Esta secao existe porque a metade que ninguem ve e a que quebra: o corpo ja atravessava a
	/// tranca (o caminho da morte nunca perguntou nada a Sala), mas `SalaPreso` **vai pro disco**.
	/// Sem limpar, o morto renasce na Terra ainda marcado como preso -- e a porta o recusa pra
	/// sempre, num planeta onde ele nem esta. O sintoma so aparece dias depois.
	///
	/// A PRISAO AQUI E DE PRODUCAO, e nao um `SalaPreso = true` escrito a mao: o corpo entra pela
	/// porta, a sessao acaba, a janela vence e o `TickDaSessaoDaSala` e quem tranca. Uma bancada que
	/// escrevesse o bit sozinha nao provaria que o caminho inteiro se encontra.
	/// ==============================================================================================
	/// </summary>
	private void AMorteEASaidaCara(ServerPlayer a, ServerPlayer kami, ZoneKey lookout, ZoneKey sala)
	{
		_naSala.Clear();
		RecolherComidaDaSala();

		void NaPorta()
		{
			ZoneList(a.Zone.Hash).Remove(a);
			a.Zone = lookout;
			ZoneList(lookout.Hash).Add(a);
			a.Pos = new Vec2(124 * ZoneCollision.TileSize + 16, 81 * ZoneCollision.TileSize + 16);
			a.Ficha.dead = a.Ficha.KO = false;
			a.Voando = false;
			EscutaDeAvisos?.Clear();
		}

		// --- 1) ficar preso pelo caminho de producao ---
		EntrarPelaPortaDeVerdade(a, lookout);
		a.SalaDiasDaSessao = Jandirus.Core.World.SalaDoTempo.SessaoEmDias;
		TickDaSessaoDaSala(1.0);              // arma a janela
		a.SalaJanelaAte = NowMs() - 1;        // ...e o relogio dela vence
		TickDaSessaoDaSala(1.0);              // ...e o tique tranca
		AfirmarSala("o corpo ficou PRESO pelo caminho de producao (porta -> sessao -> janela -> tranca)",
					a.SalaPreso && a.Zone.Hash == sala.Hash, $"preso={a.SalaPreso} zona={a.Zone.Name}");

		// --- 2) preso, a PORTA continua recusando ---
		// De fora e de dentro sao guardas diferentes; esta e a de fora, e ela e a que responde
		// depois de um relog em frente ao Templo. A da passagem (a de dentro) e a secao 14.
		Vec2 dentro = a.Pos;
		NaPorta();
		a.SalaAutorizada = true;              // com chave e SEM recarga: so a prisao pode recusar
		a.SalaUltimaEntrada = 0;
		ComandoDaSalaDoTempo(a, "sala_entrar", "");
		AfirmarSala("PRESO, A PORTA CONTINUA RECUSANDO -- mesmo com chave e sem recarga",
					a.Zone.Hash == lookout.Hash, a.Zone.Name);
		AfirmarSala("...e ela recusa POR PRISAO (e nao por outro motivo)",
					UltimoAviso().Contains("preso", StringComparison.OrdinalIgnoreCase), UltimoAviso());

		// de volta pra dentro, do jeito que ele estava: preso no z13
		ZoneList(a.Zone.Hash).Remove(a);
		a.Zone = sala;
		ZoneList(sala.Hash).Add(a);
		a.Pos = dentro;
		_naSala[a.Assinatura] = NowMs();

		// --- 3) e o verb do jogador tambem nao salva ---
		EscutaDeAvisos?.Clear();
		Verbo(a, "spawn", "");
		AfirmarSala("PRESO + `Goto Spawn`: o verb do jogador nao existe mais, e ele continua dentro",
					a.Zone.Hash == sala.Hash && a.SalaPreso, $"{a.Zone.Name} preso={a.SalaPreso}");

		// --- 4) MORRER SAI ---
		EscutaDeAvisos?.Clear();
		a.Combate.Morrer(ignorarSeguro: true);
		Renascer(a);
		AfirmarSala("MORRER TIRA O PRESO DA SALA (a saida cara -- decisao do dono)",
					a.Zone.Hash != sala.Hash, a.Zone.Name);
		AfirmarSala("...e o ESTADO DE PRESO e limpo (ele nao renasce marcado)",
					!a.SalaPreso && a.SalaJanelaAte == 0,
					$"preso={a.SalaPreso} janela={a.SalaJanelaAte}");
		AfirmarSala("...e o jogador LE que foi a morte que o tirou de la",
					string.Join(" | ", EscutaDeAvisos ?? [])
						.Contains("morte te tira", StringComparison.OrdinalIgnoreCase), UltimoAviso());

		// O DISCO E QUEM GUARDA A PRISAO, e e por isso que a limpeza tem que chegar ate ele: em
		// memoria o bit some sozinho quando o objeto morre, e o teste ficaria verde com o save sujo.
		CharacterSave morto = AccountStore.DeJogador(a, NowMs());
		AfirmarSala("...e o SAVE tambem sai limpo (relogar depois de morrer nao devolve a prisao)",
					!morto.SalaPreso);

		// A VAGA VOLTA: o tique pergunta "este corpo ainda esta no z13?", e agora a resposta e nao.
		TickDaSalaDoTempo();
		AfirmarSala("...e a vaga que ele ocupava volta pra sala", QuantosNaSala() == 0,
					$"{QuantosNaSala()} dentro");

		// ============================ A PROVA QUE VALE POR TODAS ============================
		// "Nao renasce marcado" so significa alguma coisa se ele conseguir JOGAR depois. Aqui ele
		// volta pra porta com chave e sem recarga -- se a limpeza nao tivesse acontecido, a porta o
		// recusaria falando em prisao, e ele estaria banido da Sala pro resto da vida do personagem.
		// ================================================================================
		NaPorta();
		a.SalaAutorizada = true;
		a.SalaUltimaEntrada = 0;
		a.SalaDiasDaSessao = 0;
		ComandoDaSalaDoTempo(a, "sala_entrar", "");
		AfirmarSala("DEPOIS DE MORRER ELE ENTRA DE NOVO -- a prisao acabou junto com a vida",
					a.Zone.Hash == sala.Hash, $"{a.Zone.Name} / {UltimoAviso()}");

		// --- 5) e as duas maos que soltam continuam soltando ---
		a.SalaPreso = true;
		_tronos["guardian"] = kami.Conta;
		ComandoDaSalaDoTempo(kami, "sala_soltar", a.Id.ToString());
		AfirmarSala("o Guardiao da Terra CONTINUA soltando (nada disto mexeu no resgate normal)",
					!a.SalaPreso);

		a.SalaPreso = true;
		_tronos.Remove("guardian");
		AdminSoltarDaSala(kami, a.Id.ToString());
		AfirmarSala("...e a valvula do admin tambem, com o trono vago", !a.SalaPreso);

		// A BANCADA TERMINA COMO COMECOU.
		_naSala.Clear();
		RecolherComidaDaSala();
		a.SalaUltimaEntrada = a.SalaJanelaAte = 0;
		a.SalaDiasDaSessao = 0;
		a.SalaAutorizada = a.SalaPreso = false;
		a.Ficha.dead = a.Ficha.KO = false;
		ZoneList(a.Zone.Hash).Remove(a);
		a.Zone = lookout;
		ZoneList(lookout.Hash).Add(a);
		AplicarGravidade(a);
	}

	// =====================================================================
	// 18) O CLIMA DA SALA FICA -- a decisao do dono virando checagem
	// =====================================================================
	/// <summary>
	/// ============================ UMA DECISAO QUE PARECE UM BUG ============================
	/// *"mantenha o clima na sala do tempo"* -- ordem do dono, literal. A Sala e um quarto branco no
	/// meio de um vazio branco infinito, e ver nevasca caindo nesse vazio da vontade de consertar.
	///
	/// Os dois "consertos" obvios sao baratos e silenciosos: um `temclima: false` no `planetas.json`
	/// ou um ramo em `Clima.DaZona` ("interior e a Sala nao tem ceu"). Nenhum dos dois quebraria
	/// nada -- o ceu simplesmente ficaria limpo pra sempre, e ninguem descobriria por que.
	///
	/// Por isso sao TRES checagens e nao uma: a ficha (pega o JSON), a resolucao da zona (pega o
	/// ramo em codigo) e o SORTEIO ao longo do tempo (pega uma lista de climas permitidos que na
	/// pratica nunca sai). A terceira e a unica que mede o clima ACONTECENDO.
	/// ====================================================================================
	/// </summary>
	private void OClimaDaSalaFica()
	{
		FichaDePlaneta? ficha = _planetas?.De(ZonaDaSala);
		AfirmarSala("a Sala do Tempo tem ficha de planeta (o apelido casa com o nome da zona)",
					ficha != null);
		AfirmarSala("...e a ficha diz que ela TEM CLIMA (`temclima` -- decisao do dono, nao mexa)",
					ficha?.TemClima == true);

		ZoneKey sala = ZoneKey.Premade(ZonaDaSala);
		ClimaDoPlaneta ceu = ClimaDaZona(sala);
		AfirmarSala("...e a resolucao de zona CONCORDA (nenhum ramo escondido apaga o ceu de la)",
					ceu.Existe && ceu.Permitidos.Length > 0,
					$"existe={ceu.Existe} tipos={ceu.Permitidos.Length}");

		// ============================ E O CEU ACONTECE, e nao so 'esta permitido' ============================
		// Uma lista de climas que o sorteio nunca escolhe e indistinguivel de lista vazia. Aqui a
		// bancada anda pelo relogio do mundo e conta os blocos que sairam com clima -- e o sorteio e
		// funcao pura do tempo, entao da pra conferir um dia inteiro de ceu sem esperar um segundo.
		// =================================================================================================
		ulong sal = Clima.SalDaZona(sala);
		var vistos = new HashSet<TipoDeClima>();
		const int amostras = 240;
		for (int n = 0; n < amostras; n++)
		{
			EstadoDoClima e = Clima.Natural(ceu, TempoDoMundo + n * Clima.SegundosPorBloco, sal);
			if (e.Tipo != TipoDeClima.Limpo) vistos.Add(e.Tipo);
		}
		AfirmarSala($"O CEU DA SALA ACONTECE MESMO: em {amostras} blocos sairam varios climas",
					vistos.Count >= 2, $"{vistos.Count} tipo(s): {string.Join(", ", vistos)}");
	}
}
