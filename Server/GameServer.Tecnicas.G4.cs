using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// LOTE G4 -- UTILITARIAS, ESTADO E MUNDO.
///
/// As sete daqui nao tem nada em comum no EFEITO (uma manda recado, outra ressuscita, outra
/// desfaz o tempo), e tem tudo em comum na FORMA: nenhuma causa dano. Sao as tecnicas que mexem
/// no ESTADO do personagem ou no LUGAR onde ele esta, e por isso nenhuma delas usa o alicerce de
/// buff -- nao ha stat somado pra desfazer depois. O que elas usam do alicerce e a disciplina
/// dele: guardar o que foi aplicado (o ponto do Chrono Trigger guarda o corpo INTEIRO de antes,
/// nao uma diferenca a recalcular) e nunca deixar um efeito sobreviver ao corpo.
///
/// TRES DELAS NAO SAO TECNICA, SAO MENU (Telepathy, RiftTeleport, DirectSSJ, Magic_Words). No DM
/// isso e resolvido com `input()` -- uma caixa modal que trava o jogo do jogador ate ele
/// responder. Aqui nao ha modal no servidor, entao o padrao e o mesmo dos cargos e da
/// tecnologia: o id VAZIO devolve a lista, e o id com `:argumento` executa a escolha. Duas
/// viagens em vez de uma, e nenhum jogador parado esperando caixa.
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// O QUE E "DO LADO"
	// =====================================================================
	/// <summary>
	/// `view(1)` / `oview(1)` do BYOND -- a caixa 3x3 em volta de quem usa.
	///
	/// A DIAGONAL E O MOTIVO DO 1,5. No BYOND a caixa pega os oito vizinhos, e o vizinho
	/// diagonal fica a raiz de 2 (~1,41) tiles de distancia. Cortar em 1 tile redondo faria a
	/// tecnica recusar justamente quem esta encostado na diagonal -- o jogador veria o corpo
	/// colado no dele e ouviria "ninguem por perto".
	/// </summary>
	private const float RaioDeUmTileG4 = 1.5f * ZoneCollision.TileSize;

	private readonly Dictionary<int, long> _prontoTelepatiaG4 = [];
	private readonly Dictionary<int, long> _prontoRiftG4 = [];
	private readonly Dictionary<int, long> _prontoSuccaoG4 = [];

	/// <summary>O ponto cravado no tempo. Nulo = nao ha ponto (ver Chrono Trigger).</summary>
	private readonly Dictionary<int, PontoCronoG4> _pontoCronoG4 = [];

	/// <summary>As dez frases gravadas de cada um (Magic Words).</summary>
	private readonly Dictionary<int, string[]> _frasesG4 = [];

	/// <summary>
	/// O ESTADO CONGELADO de um corpo, do jeito que o `obj/Chronoic_Point` do DM guarda.
	///
	/// Guardar o ESTADO e nao a DIFERENCA e a mesma regra do <see cref="BuffAtivo"/>: se o ponto
	/// guardasse "voce estava com 30 de vida a mais", reverter depois de ficar mais forte (mais
	/// vida maxima, mais Ki maximo) devolveria um corpo que nunca existiu.
	/// </summary>
	private sealed class PontoCronoG4
	{
		public double Hp, Ki, Stamina;
		public bool Morto;
		public int Idade;
		public ZoneKey Zona;
		public Vec2 Pos;
		public long CravadoEm;
	}

	// =====================================================================
	// REGISTRO
	// =====================================================================
	public static void RegistrarTecnicasG4()
	{
		Tecnicas.Registrar("Telepathy", "Telepatia", Modo.Instantanea,
			"Fala direto na mente de qualquer pessoa do mundo, sem limite de distância. Não alcanca "
			+ "quem está escondido, quem e Android, nem quem tem energia fraca demais pra ser achado. "
			+ "Mande sem alvo pra ver quem da pra alcancar.", aba: "Outros");

		Tecnicas.Registrar("Revive", "Ressuscitar", Modo.Instantanea,
			"Traz de volta a vida um morto que esteja do seu lado, e o puxa pra onde você esta. O "
			+ "corpo volta inteiro: membros decepados crescem de novo, Ki e fôlego cheios. Você "
			+ "precisa estar vivo pra usar.");

		Tecnicas.Registrar("RiftTeleport", "Rasgo Dimensional", Modo.Instantanea,
			"Rasga a realidade e atravessa ate outro mundo. Exige Ki CHEIO e leva TODO ele: você "
			+ "chega do outro lado sem energia nenhuma. Mande sem destino pra ver aonde o rasgo "
			+ "alcanca.", aba: "Outros");

		Tecnicas.Registrar("Chrono_Trigger", "Gatilho Cronico", Modo.Instantanea,
			"Crava um ponto no tempo guardando seu corpo como ele está agora: vida, energia, fôlego, "
			+ "idade, lugar -- e ate se você estava vivo. Usar de novo devolve você aquele instante e "
			+ "apaga o ponto. Isso desfaz ate a própria morte.");

		Tecnicas.Registrar("Life_Suck", "Sugar a Vida", Modo.Instantanea,
			"Se alimenta da alma de alguém NOCAUTEADO e vivo ao seu lado. A energia maxima da vitima "
			+ "vira energia sua na hora, e uma fatia do poder dela vira poder seu. Não mata -- mas o "
			+ "corpo leva cinco minutos pra conseguir de novo.");

		Tecnicas.Registrar("DirectSSJ", "Transformacao Direta", Modo.Instantanea,
			"Pula direto pra qualquer forma que você JA despertou, sem subir a escada degrau por "
			+ "degrau. Não afrouxa nenhum requisito: a forma continua pedindo o poder e a maestria "
			+ "de sempre. Mande sem numero pra ver o que está aberto.", aba: "Formas");

		Tecnicas.Registrar("Magic_Words", "Palavras Magicas", Modo.Instantanea,
			"Grava ate dez frases que viram atalhos de fala. Mande sem argumento pra ver as suas, e "
			+ "com numero e texto pra gravar. Depois de gravada, a frase sai com um comando so.",
			aba: "Outros");

		// AS DEZ FRASES NAO SAO REGISTRADAS DE PROPOSITO. `Tecnicas.Portadas` contra
		// `Tecnicas.Total` e a medida honesta de quanto do jogo antigo ja foi trazido -- somar dez
		// atalhos de fala ali inflaria o numero em dez sem portar tecnica nenhuma. Elas sao
		// tratadas no despacho e pertencem a quem sabe Magic_Words.
	}

	// =====================================================================
	// DESPACHO
	// =====================================================================
	/// <summary>
	/// O despacho do lote. Devolve true quando o id E deste lote (mesmo que a tecnica tenha sido
	/// RECUSADA -- recusa tambem e resposta), e false quando nao e pra ca.
	///
	/// O ID VEM COM ARGUMENTO, separado por dois-pontos: "Telepathy:Goku:oi", "DirectSSJ:2". Por
	/// isso a checagem de "voce sabe essa tecnica?" e feita AQUI e sobre o id BASE -- o despacho
	/// generico de <c>UsarTecnica</c> procuraria o verb "DirectSSJ:2" no livro de skills e nao
	/// acharia nunca, entao toda tecnica com argumento seria recusada como desconhecida.
	/// </summary>
	public bool UsarTecnicasG4(ServerPlayer pl, string id)
	{
		if (id.Length == 0) return false;

		int corte = id.IndexOf(':');
		string baseId = corte < 0 ? id : id[..corte];
		string arg = corte < 0 ? "" : id[(corte + 1)..];

		// AS FRASES PERTENCEM AO MENU QUE AS GRAVOU. No DM elas sao verbs criados na hora pelo
		// `reinitialize_words()`; aqui nao ha verb pra criar, entao quem autoriza a frase e a
		// propria Magic_Words -- e por isso o gate delas e o dela.
		bool frase = baseId.StartsWith("Phrase_", StringComparison.OrdinalIgnoreCase);
		string gate = frase ? "Magic_Words" : baseId;

		switch (baseId)
		{
			case "Telepathy":
			case "Revive":
			case "RiftTeleport":
			case "Chrono_Trigger":
			case "Life_Suck":
			case "DirectSSJ":
			case "Magic_Words":
				break;
			default:
				if (!frase) return false;
				break;
		}

		if (!SabeTecnica(pl, gate))
		{
			Avisar(pl, frase
				? "você não sabe gravar palavras magicas."
				: $"você não sabe {Tecnicas.Get(baseId)?.Nome ?? baseId}.");
			return true;
		}

		if (frase) { DizerFraseG4(pl, baseId); return true; }

		switch (baseId)
		{
			case "Telepathy": TelepatiaG4(pl, arg); break;
			case "Revive": RessuscitarG4(pl); break;
			case "RiftTeleport": RasgoDimensionalG4(pl, arg); break;
			case "Chrono_Trigger": GatilhoCronicoG4(pl); break;
			case "Life_Suck": SugarVidaG4(pl); break;
			case "DirectSSJ": FormaDiretaG4(pl, arg); break;
			case "Magic_Words": PalavrasMagicasG4(pl, arg); break;
		}
		return true;
	}

	/// <summary>
	/// Apelido pro nome no singular. Existe so porque a fiacao das quatro levas pode chamar por
	/// qualquer um dos dois nomes -- um alias de uma linha custa menos que um dispatch mudo.
	/// </summary>
	public bool UsarTecnicaG4(ServerPlayer pl, string id) => UsarTecnicasG4(pl, id);

	// =====================================================================
	// TELEPATHY -- Communication.dm:49
	// =====================================================================
	/// <summary>
	/// FALA NA MENTE, SEM DISTANCIA. E a unica coisa no jogo que atravessa planeta sem gastar
	/// nada: no DM o verb e `mob/keyable/verb/Telepathy(mob/M in player_list)` -- a lista de
	/// TODOS os jogadores, sem `view()` nenhum em volta.
	///
	/// OS TRES QUE NAO DA PRA ALCANCAR sao literais do DM: quem esta escondido (`isconcealed`),
	/// quem e Android e quem tem `expressedBP <= 5`. Os tres tem a mesma justificativa dentro da
	/// ficcao -- a telepatia procura ENERGIA, e nesses tres casos nao ha energia pra achar. Por
	/// isso a recusa e uma so ("nao acho a energia") e nao tres mensagens diferentes: dizer
	/// "aquele ali e Android" seria a tecnica entregando informacao que ela nao deveria ter.
	///
	/// FICOU DE FORA: o `seetelepathy`, que no DM e uma chave nas Configuracoes (Settings.dm:212)
	/// pra recusar telepatia. O padrao dele e LIGADO (Communication.dm:17), entao tratar todo
	/// mundo como receptivo e o comportamento de todo jogador que nunca mexeu na opcao. Quando o
	/// port tiver tela de configuracoes, o teste entra aqui.
	/// </summary>
	private void TelepatiaG4(ServerPlayer pl, string arg)
	{
		if (pl.Ficha.dead) { Avisar(pl, "sua mente está longe demais do mundo dos vivos."); return; }

		if (arg.Length == 0) { ListarMentesG4(pl); return; }

		// "<nome>" ou "<nome>:<mensagem>" -- a mensagem fica INTEIRA, dois-pontos inclusive
		int corte = arg.IndexOf(':');
		string nome = (corte < 0 ? arg : arg[..corte]).Trim();
		string texto = corte < 0 ? "" : arg[(corte + 1)..].Trim();

		ServerPlayer? alvo = null;
		foreach (ServerPlayer o in _players.Values)
			if (string.Equals(o.Name, nome, StringComparison.OrdinalIgnoreCase)) { alvo = o; break; }

		if (alvo == null) { Avisar(pl, $"não ha ninguém chamado '{nome}' no mundo agora."); return; }
		if (alvo == pl) { Avisar(pl, "você ja sabe o que está pensando."); return; }
		if (!AchoAEnergiaG4(alvo)) { Avisar(pl, $"não consigo achar a energia de {alvo.Name}!"); return; }

		if (texto.Length == 0)
		{
			Avisar(pl, $"e o que você quer dizer a {alvo.Name}? Use Telepathy:{alvo.Name}:sua mensagem");
			return;
		}

		// A FREADA E A MESMA DA FALA, e de proposito: `MsEntreFalas` ja e a decisao deste port
		// sobre quanto texto uma pessoa pode empurrar por segundo. O DM nao tem freada nenhuma
		// aqui -- e sem ela a telepatia seria o canal mais barato do jogo pra inundar alguem,
		// porque alcanca o mundo inteiro e nao custa Ki.
		long agora = NowMs();
		if (_prontoTelepatiaG4.TryGetValue(pl.Id, out long pronto) && agora < pronto)
		{
			Avisar(pl, "sua mente ainda está formando o pensamento anterior.");
			return;
		}
		_prontoTelepatiaG4[pl.Id] = agora + MsEntreFalas;

		if (texto.Length > Protocol.MaxFala) texto = texto[..Protocol.MaxFala];
		texto = texto.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
		if (texto.Length == 0) { Avisar(pl, "não havia pensamento nenhum ali."); return; }

		Avisar(alvo, $"{pl.Name} diz na sua mente, '{texto}'");
		Avisar(pl, $"você diz na mente de {alvo.Name}, '{texto}'");
		GD.Print($"[server] telepatia: {pl.Name} -> {alvo.Name}");
	}

	/// <summary>Os tres testes de `Communication.dm:53`, num lugar so.</summary>
	private static bool AchoAEnergiaG4(ServerPlayer m) =>
		!m.Ficha.isconcealed && m.Race != "Android" && m.Ficha.expressedBP > 5;

	private void ListarMentesG4(ServerPlayer pl)
	{
		var achados = new List<string>();
		foreach (ServerPlayer o in _players.Values)
		{
			// NPC nao tem mente pra alcancar: `Peer` nulo e o que define corpo sem dono no port
			if (o == pl || o.Peer == null) continue;
			if (!AchoAEnergiaG4(o)) continue;
			achados.Add(o.Name);
		}

		if (achados.Count == 0) { Avisar(pl, "você estende a mente e não acha ninguém."); return; }

		achados.Sort(StringComparer.OrdinalIgnoreCase);
		Avisar(pl, $"você alcanca {achados.Count} {(achados.Count == 1 ? "mente" : "mentes")}: {string.Join(", ", achados)}");
		Avisar(pl, "para falar: Telepathy:<nome>:<mensagem>");
	}

	// =====================================================================
	// REVIVE -- kai.dm:65 (versao de SKILL)
	// =====================================================================
	/// <summary>
	/// TRAZ UM MORTO DE VOLTA e o puxa pra onde voce esta.
	///
	/// PORTADA A VERSAO DE SKILL (kai.dm:65). A versao de CARGO
	/// (`OtherworldRankSkills.dm:217`) e outra coisa e faz quatro coisas a mais que esta nao faz:
	/// (1) cura o corpo inteiro e enche o folego explicitamente, (2) tira o nocaute, (3) CONTA
	/// quantas vezes cada alma ja voltou, e (4) a partir da SEGUNDA volta da mesma alma, QUEM
	/// REVIVE MORRE NO LUGAR ("trades his life for the resurrection"). Esse preco progressivo e o
	/// que faz a versao de cargo ser um poder de mundo e nao um botao -- e ele depende de um
	/// contador de ressurreicoes por personagem que o port ainda nao guarda. Quando guardar, e
	/// aqui que a versao de cargo entra, ao lado desta e nao no lugar dela.
	///
	/// O `ReviveMe()` do DM (Death.dm:143) ja cura tudo, enche Ki e folego e faz membro decepado
	/// CRESCER DE NOVO -- entao a versao de skill parecer "mais pobre" e ilusao de leitura: os
	/// dois caminhos devolvem o corpo inteiro, a de cargo so escreve de novo o que o proc ja fez.
	///
	/// FICOU DE FORA: o `aged_out` (Death.dm:144) -- quem morreu de VELHICE nao volta por meio
	/// nenhum, so pela reencarnacao do Enma. O port ainda nao tem morte por idade; quando tiver,
	/// o teste e a primeira linha daqui.
	/// </summary>
	private void RessuscitarG4(ServerPlayer pl)
	{
		// `if(!usr.dead)` -- o morto nao ressuscita ninguem, nem a si mesmo
		if (pl.Ficha.dead) { Avisar(pl, "você precisa estar vivo pra trazer alguém de volta."); return; }

		ServerPlayer? alvo = AlvoPertoG4(pl, RaioDeUmTileG4, o => o.Ficha.dead);
		if (alvo == null)
		{
			// as duas recusas do DM em uma: ou nao ha ninguem do lado, ou quem esta nao morreu
			ServerPlayer? vivo = AlvoPertoG4(pl, RaioDeUmTileG4, _ => true);
			Avisar(pl, vivo != null ? $"{vivo.Name} não está morto." : "não ha nenhum morto ao seu lado.");
			return;
		}

		// --- o `ReviveMe()` do DM, membro a membro -------------------------
		alvo.Combate.Reviver(1, SegundosDeCarencia);   // corpo INTEIRO, decepado incluso
		alvo.Ficha.Ki = alvo.Ficha.MaxKi;
		alvo.Ficha.stamina = alvo.Ficha.maxstamina;
		alvo.RenasceEm = 0;                            // o renascimento automatico perde a corrida
		AjustarGanhoDoRabo(alvo);
		alvo.Ficha.Statify();

		// --- `M.loc = locate(usr.x, usr.y, usr.z)` -------------------------
		PuxarParaG4(alvo, pl.Zone, pl.Pos);

		Persistir(alvo);

		Avisar(pl, $"você traz {alvo.Name} de volta, e o corpo se refaz aos seus pes.");
		Avisar(alvo, $"{pl.Name} trouxe você de volta ao mundo dos vivos!");
		GD.Print($"[server] {pl.Name} RESSUSCITOU {alvo.Name}");
	}

	// =====================================================================
	// RIFT TELEPORT -- demigod.dm:43
	// =====================================================================
	/// <summary>
	/// OS ONZE DESTINOS do `input(...) in list(...)` de demigod.dm:52, na ordem em que aparecem
	/// la, mapeados pro nome de zona deste port.
	///
	/// DOIS NAO EXISTEM AQUI e por isso nao estao na lista: "Large Space Station" (o port so
	/// converteu a pequena, z27) e nao ha nada pra onde apontar; e o nome "Icer Planet" do menu,
	/// que aqui e a zona "Icer" (z04) -- mesmo lugar, nome curto.
	///
	/// A LISTA E FIXA E ISSO E A TECNICA. Nao e um teleporte pra qualquer lugar: o poder alcanca
	/// ESTES mundos e mais nenhum, e e isso que impede o rasgo de aposentar a viagem espacial.
	/// </summary>
	private static readonly string[] DestinosDoRasgoG4 =
	[
		"Earth", "Namek", "Vegeta", "Icer", "Arconia", "Desert", "Arlia",
		"Small_Space_Station", "Afterlife", "Heaven",
	];

	/// <summary>
	/// `sleep(10)` -- os dez decimos de segundo que o DM segura entre o rasgo abrir e o corpo
	/// aparecer do outro lado, com o `inteleport` travado pra nao rasgar duas vezes.
	///
	/// Aqui vira RECARGA em vez de espera: o despacho de tecnica e sincrono, entao segurar um
	/// segundo dentro dele seria segurar o servidor inteiro. O efeito pro jogador e o mesmo (nao
	/// da pra encadear rasgos), e sem a espera nao ha a janela em que o DM deixava o corpo
	/// existindo em dois lugares.
	/// </summary>
	private const long RecargaDoRasgoMsG4 = 1000;

	private void RasgoDimensionalG4(ServerPlayer pl, string destino)
	{
		// `if(!usr.canmove || usr.KO || ...) return` -- o DM sai CALADO aqui, e isso e um defeito
		// pequeno e chato: o jogador aperta, nao acontece nada, e nada explica. Aqui fala.
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "não da, caido."); return; }
		if (pl.Combate is { Stun: > 0 }) { Avisar(pl, "você ainda está atordoado."); return; }

		// `canfight && !usr.med && !usr.train` -- rasgar a realidade e concentracao total
		if (pl.Ficha.med || pl.Ficha.train)
		{
			Avisar(pl, "você precisa parar de treinar pra se concentrar no rasgo.");
			return;
		}
		if (pl.CloneId != 0) { Avisar(pl, "primeiro saia da sua mente."); return; }

		long agora = NowMs();
		if (_prontoRiftG4.TryGetValue(pl.Id, out long pronto) && agora < pronto)
		{
			Avisar(pl, $"o rasgo anterior ainda está se fechando ({(pronto - agora) / 1000.0:0.#}s).");
			return;
		}

		// `usr.Ki >= usr.MaxKi` -- KI CHEIO, nao "quase cheio". A tolerancia existe porque o Ki
		// do port e ponto flutuante recalculado por tick: exigir igualdade exata deixaria a
		// tecnica recusando um jogador que esta com 99,9999% por arredondamento.
		if (pl.Ficha.Ki < pl.Ficha.MaxKi - 0.5)
		{
			Avisar(pl, $"isso pede energia CHEIA: {pl.Ficha.Ki:0} de {pl.Ficha.MaxKi:0}.");
			return;
		}

		if (destino.Length == 0)
		{
			Avisar(pl, $"seu rasgo alcanca: {string.Join(", ", DestinosDoRasgoG4)}");
			Avisar(pl, "para atravessar: RiftTeleport:<destino>");
			return;
		}

		string? escolhido = null;
		foreach (string d in DestinosDoRasgoG4)
			if (string.Equals(d, destino, StringComparison.OrdinalIgnoreCase)) { escolhido = d; break; }

		if (escolhido == null)
		{
			Avisar(pl, $"seu rasgo não alcanca '{destino}'. Alcanca: {string.Join(", ", DestinosDoRasgoG4)}");
			return;
		}
		if (_catalogo?.Get(escolhido) == null)
		{
			Avisar(pl, $"{escolhido} não tem mapa carregado neste servidor.");
			return;
		}
		if (string.Equals(escolhido, pl.Zone.Name, StringComparison.Ordinal))
		{
			Avisar(pl, $"você ja está em {escolhido}.");
			return;
		}

		// `usr.Ki=0` -- ZERA, nao desconta. Atravessar custa TUDO, e e esse "tudo" que faz o
		// rasgo ser uma decisao: chega-se do outro lado indefeso.
		pl.Ficha.Ki = 0;
		_prontoRiftG4[pl.Id] = agora + RecargaDoRasgoMsG4;

		// FICOU DE FORA: o `input("X Location?")` do DM, que deixa escolher a coordenada exata de
		// chegada. Isso e teleporte cirurgico pra dentro de qualquer lugar do mapa de destino --
		// inclusive pra dentro de parede, que o DM nem confere. Aqui a chegada e o ponto de
		// spawn da zona, que e o unico lugar de que se sabe que existe chao.
		Falar(pl, Protocol.Fala.Emote, $"{pl.Name} rasga o ar e desaparece dentro dele.");
		MoveToZone(pl.Id, ZoneKey.Premade(escolhido), SpawnPos);
		MandarEfeito(pl, "rasgo", RecargaDoRasgoMsG4);

		Avisar(pl, $"você atravessa o rasgo e chega em {escolhido}, sem uma gota de energia.");
		GD.Print($"[server] {pl.Name} rasgou ate {escolhido}");
	}

	// =====================================================================
	// CHRONO TRIGGER -- kanassa-jin.dm:108
	// =====================================================================
	/// <summary>
	/// CRAVA UM PONTO NO TEMPO; usar de novo volta pra ele e apaga o ponto.
	///
	/// O CUSTO FICOU DE FORA, e isso e uma falta e nao uma escolha de balanceamento: o DM cobra
	/// `stored_time -= 100` (cem de tempo guardado, "dez anos"), e `stored_time` e uma moeda de
	/// um sistema inteiro -- os Kanassa-jin a acumulam vivendo e a gastam em varias tecnicas de
	/// tempo. O port nao tem essa moeda. INVENTAR um custo aqui (tanto de Ki, tanta recarga)
	/// seria escolher sozinho o balanceamento de um sistema que ainda vai ser portado, e o custo
	/// errado num poder que DESFAZ A MORTE e pior que custo nenhum: uma vez publicado, mexer nele
	/// e nerf. Entao fica de graca e anotado, pra entrar junto com o `stored_time`.
	///
	/// O QUE O PONTO GUARDA e o estado inteiro, nao uma diferenca -- ver <see cref="PontoCronoG4"/>.
	/// </summary>
	private void GatilhoCronicoG4(ServerPlayer pl)
	{
		// --- JA HA PONTO: volta pra ele e o apaga ---------------------------
		if (_pontoCronoG4.Remove(pl.Id, out PontoCronoG4? cp))
		{
			Falar(pl, Protocol.Fala.Emote, $"{pl.Name} volta no tempo!");

			pl.Ficha.Ki = cp.Ki;
			pl.Ficha.stamina = cp.Stamina;
			pl.Idade = cp.Idade;

			// `dead = cp.dead` -- E ISTO QUE DESFAZ A MORTE. Nos dois sentidos: um ponto cravado
			// vivo ressuscita, e um ponto cravado MORTO mata de volta quem foi revivido depois.
			if (pl.Ficha.dead && !cp.Morto)
			{
				pl.Combate.Reviver(1, SegundosDeCarencia);
				pl.RenasceEm = 0;
				AjustarGanhoDoRabo(pl);
			}
			else if (!pl.Ficha.dead && cp.Morto)
			{
				pl.Combate.Morrer();
				pl.RenasceEm = NowMs() + MsAteRenascer;
			}

			// A VIDA VEM DEPOIS DO REVIVER, e a ordem e o conserto de um defeito de verdade: a
			// primeira versao escrevia a vida de cada membro ANTES deste bloco, e o `Reviver()`
			// logo abaixo restaura o corpo INTEIRO -- entao voltar pra um ponto cravado VIVO
			// estando MORTO devolvia o corpo cheio, e nao a vida que estava no ponto. A tecnica
			// promete devolver AQUELE instante; devolver mais do que ele e um presente silencioso.
			double dif = cp.Hp - pl.Ficha.HP;
			if (dif > 0) pl.Combate.Corpo.Curar(dif);
			else if (dif < 0)
			{
				// DESVIO DELIBERADO: o DM usa `SpreadDamage`, que passa pelo funil de dano e pode
				// QUEBRAR membro, nocautear e ate decepar. Reverter pra um estado PIOR e virar
				// aleijado no processo transformaria a tecnica em roleta -- o ponto promete
				// devolver o corpo daquele instante, e um membro a menos nao estava no instante.
				// Aqui a vida sai uniforme de cada membro que ainda existe, com piso de 1.
				foreach (Jandirus.Core.Combat.BodyPart p in pl.Combate.Corpo.Partes)
				{
					if (p.Decepado) continue;
					p.Vida = Math.Max(1, p.Vida + dif);
				}
			}
			pl.Combate.SincronizarVida();

			PuxarParaG4(pl, cp.Zona, cp.Pos);
			pl.Ficha.Statify();
			Persistir(pl);

			MandarEfeito(pl, "crono", 0);
			Avisar(pl, $"o tempo se dobra e devolve você ao instante que cravou "
					 + $"({(NowMs() - cp.CravadoEm) / 1000.0:0}s atras).");
			GD.Print($"[server] {pl.Name} voltou ao ponto cronico");
			return;
		}

		// --- NAO HA PONTO: crava -------------------------------------------
		Falar(pl, Protocol.Fala.Emote, $"{pl.Name} se concentra, e solta um pouco de tempo.");

		_pontoCronoG4[pl.Id] = new PontoCronoG4
		{
			Hp = pl.Ficha.HP,
			Ki = pl.Ficha.Ki,
			Stamina = pl.Ficha.stamina,
			Morto = pl.Ficha.dead,
			Idade = pl.Idade,
			Zona = pl.Zone,
			Pos = pl.Pos,
			CravadoEm = NowMs(),
		};

		MandarEfeito(pl, "crono", -1);
		Avisar(pl, $"você crava um ponto no tempo aqui: {pl.Ficha.HP:0} de vida, {pl.Ficha.Ki:0} de energia. "
				 + "Usar de novo devolve você a este instante.");
		GD.Print($"[server] {pl.Name} cravou ponto cronico em {pl.Zone}");
	}

	// =====================================================================
	// LIFE SUCK -- demon.dm:75
	// =====================================================================
	/// <summary>`sleep(3000)` -- os 300 segundos que o DM segura o `absorbing` ligado.</summary>
	private const long RecargaDaSuccaoMsG4 = 300_000;

	/// <summary>
	/// SE ALIMENTA DE UM NOCAUTEADO VIVO ao lado. E a unica absorcao do jogo que NAO mata.
	///
	/// AS DUAS FORMULAS DE BP, literais de demon.dm:100-103:
	///   mais fraco que o alvo:  BP += capcheck( ((M.BP / M.BPMod) * usr.BPMod) / 4 )
	///   mais forte que o alvo:  BP += capcheck(  (M.BP / M.BPMod)              / 8 )
	///
	/// A DIVISAO PELO `BPMod` DO ALVO e o coracao das duas: `BPMod` e o multiplicador racial, e
	/// dividir por ele converte o poder do outro pra uma moeda neutra -- sem isso, absorver um
	/// membro de raca multiplicadora daria poder que aquela raca so tem por SER daquela raca. O
	/// ramo de baixo remultiplica pelo SEU `BPMod` (voce recebe na sua propria moeda); o ramo de
	/// cima nao remultiplica, e essa assimetria e do DM -- quem ja e mais forte leva menos e leva
	/// cru, que e coerente com o /8 contra o /4.
	///
	/// UM DEFEITO DO DM CONSERTADO: la os dois `if` sao INDEPENDENTES, e o primeiro ja somou no
	/// `usr.BP` antes de o segundo ser testado. Com o alvo pouco acima de voce (voce 100, ele
	/// 110), o primeiro ramo paga, seu BP passa de 110, e ai o segundo ramo tambem passa a valer
	/// e paga DE NOVO na mesma absorcao. Aqui e um `else`: os dois ramos sao o mesmo caso visto
	/// de dois lados, e sempre foi um so que deveria pagar.
	///
	/// FICOU DE FORA: (1) o `Lifespan` -- o DM transfere tempo de vida do alvo pro absorvedor
	/// (`declinetaken = (M.DeclineAge - M.Age)/10`, ele ganha /100 e o alvo perde /200), e o port
	/// nao tem tempo de vida nem idade de declinio; e essa e a metade TEMATICA da tecnica, o "you
	/// take their decline" da descricao. (2) `HasSoul` e `absorbable`, as duas chaves que marcam
	/// quem ja foi absorvido e quem tem alma pra absorver. (3) `Planet != "Sealed"`, que o port
	/// nao tem. Sem (2) o unico freio e a recarga de cinco minutos -- que e o freio principal
	/// tambem no DM.
	/// </summary>
	private void SugarVidaG4(ServerPlayer pl)
	{
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "não da, caido."); return; }

		long agora = NowMs();
		if (_prontoSuccaoG4.TryGetValue(pl.Id, out long pronto) && agora < pronto)
		{
			Avisar(pl, $"você ainda está digerindo a ultima alma ({(pronto - agora) / 1000.0:0}s).");
			return;
		}

		ServerPlayer? alvo = AlvoPertoG4(pl, RaioDeUmTileG4, o => o.Ficha.KO && !o.Ficha.dead);
		if (alvo == null)
		{
			Avisar(pl, "o alvo precisa estar NOCAUTEADO, vivo, e ao seu lado.");
			return;
		}

		_prontoSuccaoG4[pl.Id] = agora + RecargaDaSuccaoMsG4;

		// --- tecnologia: `if(usr.techmod < M.techmod)` / `if(usr.techskill < M.techskill)` ---
		// Comer um cerebro melhor que o seu ensina. O +1 no `techmod` e literal do DM e e MUITO
		// (o padrao e 1, entao dobra o ritmo de estudo) -- fica como esta, com o gate que o DM
		// poe: so vale enquanto o outro for mais esperto que voce.
		if (pl.Ficha.techmod < alvo.Ficha.techmod) pl.Ficha.techmod += 1;
		if (pl.Ficha.techskill < alvo.Ficha.techskill) pl.Ficha.techskill += alvo.Ficha.techskill / 10;

		// --- `usr.Ki += M.MaxKi` -------------------------------------------
		// O TETO E DAQUI, NAO DO DM. La o Ki passa do maximo sem reclamar; aqui o `kiratio` do
		// `powerlevel()` e `Ki/MaxKi` SEM teto por cima (Fighter.Power.cs:40), entao Ki acima do
		// maximo viraria BP expresso de graca -- absorver duas vezes daria mais poder pela conta
		// de energia do que pela conta de absorcao.
		pl.Ficha.Ki = Math.Min(pl.Ficha.Ki + alvo.Ficha.MaxKi, pl.Ficha.MaxKi);

		// --- as duas formulas de BP ----------------------------------------
		double bpAlvo = alvo.Ficha.BP;
		double modAlvo = Math.Max(alvo.Ficha.BPMod, 0.1);   // o DM divide sem se proteger do zero
		double ganho;
		if (pl.Ficha.BP < bpAlvo) ganho = pl.Ficha.CapCheck(bpAlvo / modAlvo * pl.Ficha.BPMod / 4);
		else ganho = pl.Ficha.CapCheck(bpAlvo / modAlvo / 8);
		pl.Ficha.BP += ganho;

		// --- `absorbadd` -> o `AbsorbBP` do port ---------------------------
		// `absorbmod` (a fracao do BP absorvido que conta) e 1 no proto do DM -- Absorption.dm:5,
		// "1=100%" -- e o port nao tem o campo por personagem, entao entra como 1 literal.
		// `PowerPcnt/100` vira o `powerMod` do port (Controle de Poder), que ja e razao.
		double add = (bpAlvo / 2 + alvo.Ficha.AbsorbBP) * alvo.Ficha.powerMod * (alvo.Ficha.Anger / 100);
		if (pl.Ficha.AbsorbBP < add) pl.Ficha.AbsorbBP += pl.Ficha.CapCheck(add);

		pl.Ficha.Statify();
		Persistir(pl);

		Avisar(pl, $"você sente o mal dentro da alma de {alvo.Name} e a puxa pra dentro de si "
				 + $"(+{ganho:N0} de poder, +{alvo.Ficha.MaxKi:0} de energia).");
		Avisar(alvo, "você se sente drenado, como se a vida tivesse sido sugada do seu corpo.");
		Falar(pl, Protocol.Fala.Emote, $"{pl.Name} parece sugar a vida direto de {alvo.Name}!");
		GD.Print($"[server] {pl.Name} sugou {alvo.Name}: +{ganho:0} BP");
	}

	// =====================================================================
	// DIRECT SSJ -- supersaiyan.dm:236
	// =====================================================================
	/// <summary>
	/// O SELETOR DE FORMA. Nao e tecnica: e o atalho que troca a escada por um numero.
	///
	/// O QUE ELE PULA E A ORDEM, NAO O REQUISITO. Este e o ponto inteiro e e facil de errar: o
	/// <see cref="EstadoDeForma.Avaliar"/> normal recusa com `ForaDeOrdem` quem tenta SSJ3 sem
	/// estar em SSJ2 -- e usar `Avaliar` inteiro aqui deixaria o seletor com zero funcao, porque
	/// so aceitaria exatamente o degrau seguinte, que e o que a tecla de transformar ja faz. Por
	/// isso os testes sao refeitos aqui SEM o de ordem: porta de BP base, maestria do degrau
	/// anterior, Ki e estado do corpo continuam valendo, um por um.
	///
	/// UMA TRAVA A MAIS QUE O DM NAO TEM: so pula pra forma que voce JA DESPERTOU. O DM deixa
	/// despertar uma forma nova direto pelo seletor, e isso atropela o unico momento em que a
	/// forma e um ACONTECIMENTO -- a primeira transformacao e cinematica, roda uma vez na vida do
	/// personagem, e chegar nela por uma caixa de numero e trocar a cena pela planilha. Despertar
	/// continua sendo subir a escada; o seletor e pra depois.
	///
	/// FICOU DE FORA: o ramo `Class == "Legendary"` (supersaiyan.dm:240), onde os numeros querem
	/// dizer outra coisa (1 = Restrained, 2 = SSJ, 3 = LSSJ) e a escada e a `lssj`. O port ainda
	/// nao tem a escada Legendary; quando tiver, e um segundo mapa de numeros aqui dentro.
	/// Tambem fica de fora o `Apeshit` (Oozaru), que no DM bloqueia o seletor inteiro.
	/// </summary>
	private void FormaDiretaG4(ServerPlayer pl, string arg)
	{
		if (!UsaEscadaSaiyajin(pl)) { Avisar(pl, "sua raca não tem essa escada de transformacao."); return; }

		EstadoDeForma est = pl.Forma;
		bool diluido = SangueDiluido(pl);

		if (arg.Length == 0) { ListarFormasDiretasG4(pl, est, diluido); return; }

		if (!double.TryParse(arg, System.Globalization.NumberStyles.Float,
							 System.Globalization.CultureInfo.InvariantCulture, out double n))
		{
			Avisar(pl, "diga o numero da forma: 0 (base), 1, 1.5, 2, 3 ou 4.");
			return;
		}

		// `round(input, 0.5)` -- o DM arredonda pro meio mais proximo antes de decidir
		n = Math.Round(n * 2, MidpointRounding.AwayFromZero) / 2;

		// --- 0 volta pra base ---------------------------------------------
		// O DM nao tem o 0 no ramo comum (a lista dele e 1-4) -- so o ramo Legendary chama
		// `Revert()`. Aceitar 0 aqui e coerencia: um seletor que leva a qualquer degrau e nao
		// consegue descer obrigaria o jogador a usar DOIS controles diferentes pra mesma coisa.
		if (n <= 0)
		{
			if (est.Atual == Forma.Base) { Avisar(pl, "você ja está na forma base."); return; }
			Forma deBase = est.Atual;
			est.Entrar(Forma.Base);
			AplicarForma(pl);
			Avisar(pl, "você volta ao normal.");
			AnunciarForma(pl, deBase, Forma.Base, primeira: false);
			return;
		}

		Forma? escolhida = FormaDoNumeroG4(n, est);
		if (escolhida == null)
		{
			Avisar(pl, $"'{arg}' não e uma forma. Use 0 (base), 1, 1.5 (grade), 2, 3 ou 4.");
			return;
		}

		Forma alvo = escolhida.Value;
		if (alvo == est.Atual) { Avisar(pl, "você ja está nessa forma."); return; }   // `if(usr.ssj==SSJchoice) return`

		FormaDef? d = EscadaSaiyajin.Def(alvo);
		if (d == null) { Avisar(pl, "essa forma não existe nestá escada."); return; }

		// --- os testes, um a um, SEM o de ordem ----------------------------
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "não da, caido."); return; }

		if (!est.JaDespertou.Contains((int)alvo))
		{
			Avisar(pl, $"você nunca despertou {d.Nome} -- o atalho so leva aonde você ja chegou pela escada.");
			return;
		}
		if (d.PedeMaestria > 0 && est.Maestria.De(d.Vem) < d.PedeMaestria)
		{
			Avisar(pl, $"{d.Nome} pede {d.PedeMaestria:0}% de maestria em "
					 + $"{EscadaSaiyajin.Def(d.Vem)?.Nome ?? "na forma anterior"} "
					 + $"(você tem {est.Maestria.De(d.Vem):0.#}%).");
			return;
		}
		if (pl.Ficha.BP < d.PortaBp)
		{
			// SEM NUMERO -- mesmo motivo de `GameServer.Formas.cs`: o limiar e sorteado por
			// personagem, e a mensagem de erro estava devolvendo de graca o que a aba Forms
			// acabou de tirar da tela.
			Avisar(pl, $"{d.Nome} ainda está além do seu alcance.");
			return;
		}

		double fracaoKi = pl.Ficha.MaxKi > 0 ? pl.Ficha.Ki / pl.Ficha.MaxKi : 1;
		if (fracaoKi < 0.1) { Avisar(pl, "Ki baixo demais pra sustentar a forma."); return; }

		Forma anterior = est.Atual;
		est.Entrar(alvo);   // ja despertou antes: nao e primeira vez, entao nao ha cinematica
		AplicarForma(pl);

		Avisar(pl, $"{d.Nome} (x{est.Multiplicador(diluido):0.##}).");
		AnunciarForma(pl, anterior, alvo, primeira: false);
		GD.Print($"[server] {pl.Name}: DIRETO {anterior} -> {alvo}");
	}

	/// <summary>
	/// O numero do DM -> o degrau do port.
	///
	/// O 1.5 E UM PRA DOIS. No DM ha UM ultra ("1.5 is USSJ"); este port partiu aquele degrau em
	/// dois grades (2 e 3), que abrem com maestrias diferentes no SSJ1. Entao o 1.5 leva ao
	/// MELHOR dos dois que ja estiver despertado -- que e o que a pessoa quis dizer ao pedir "o
	/// ultra": o mais forte a que ela tem direito.
	/// </summary>
	private static Forma? FormaDoNumeroG4(double n, EstadoDeForma est)
	{
		if (n == 1) return Forma.Ssj1;
		if (n == 1.5) return est.JaDespertou.Contains((int)Forma.Grade3) ? Forma.Grade3 : Forma.Grade2;
		if (n == 2) return Forma.Ssj2;
		if (n == 3) return Forma.Ssj3;
		if (n == 4) return Forma.Ssj4;
		return null;
	}

	private void ListarFormasDiretasG4(ServerPlayer pl, EstadoDeForma est, bool diluido)
	{
		var abertas = new List<string>();
		foreach (FormaDef d in EscadaSaiyajin.Degraus)
		{
			if (!est.JaDespertou.Contains((int)d.Id)) continue;
			bool ok = pl.Ficha.BP >= d.PortaBp
					  && (d.PedeMaestria <= 0 || est.Maestria.De(d.Vem) >= d.PedeMaestria);
			string num = d.Id switch
			{
				Forma.Ssj1 => "1",
				Forma.Grade2 or Forma.Grade3 => "1.5",
				Forma.Ssj2 => "2",
				Forma.Ssj3 => "3",
				_ => "4",
			};
			abertas.Add($"{num}={d.Nome}{(ok ? "" : " (requisito nao cumprido)")}");
		}

		if (abertas.Count == 0)
		{
			Avisar(pl, "você ainda não despertou nenhuma forma -- suba a escada uma vez e o atalho abre.");
			return;
		}
		Avisar(pl, $"formas ao seu alcance: 0=base, {string.Join(", ", abertas)}");
		Avisar(pl, $"para ir direto: DirectSSJ:<numero> (agora: {EscadaSaiyajin.Def(est.Atual)?.Nome ?? "base"}, "
				 + $"x{est.Multiplicador(diluido):0.##})");
	}

	// =====================================================================
	// MAGIC WORDS -- Magic.dm:107
	// =====================================================================
	/// <summary>Os dez slots do `preset_words` do DM. O numero e literal: `if("Ten")` e o ultimo.</summary>
	private const int FrasesG4 = 10;

	/// <summary>
	/// O MENU DAS FRASES. Nao e combate nem magia: e conforto de quem interpreta.
	///
	/// A IDEIA DO DM E BOA E ESTA INTEIRA AQUI: guardar ate dez falas usadas o tempo todo (o grito
	/// da tecnica, a saudacao do clã, o encerramento do ritual) e disparar cada uma com um comando
	/// so, em vez de redigitar. O `reinitialize_words()` de la cria um verb `Phrase_N` pra cada
	/// slot preenchido; aqui os dez atalhos ja existem e simplesmente nao respondem quando o slot
	/// esta vazio -- mesmo efeito, sem criar e destruir verb a cada edicao.
	///
	/// FICOU DE FORA o resto do menu do DM, que e de um sistema que o port nao tem: "Toggle
	/// Ritual Words" e o `word_power` (a voz carregando poder magico), "View Known Words" e "View
	/// Known Ritual Words" (o indice de palavras de poder aprendidas, `known_words` /
	/// `p_spell_index`). Nada disso existe sem o sistema de magia; o que da pra portar hoje sao as
	/// frases, e sao elas que estao aqui.
	///
	/// AS FRASES NAO ATRAVESSAM O LOGOUT, e isso e limite de escopo e nao decisao: guardar exige
	/// um campo no `CharacterSave`, que e arquivo compartilhado com o resto do port.
	/// </summary>
	private void PalavrasMagicasG4(ServerPlayer pl, string arg)
	{
		string[] minhas = FrasesDeG4(pl.Id);

		if (arg.Length == 0)
		{
			for (int i = 0; i < FrasesG4; i++)
				Avisar(pl, $"  {i + 1}: {(minhas[i].Length == 0 ? "(vazia)" : minhas[i])}");
			Avisar(pl, "para gravar: Magic_Words:<1-10>:<frase>   |   para dizer: Phrase_<1-10>");
			return;
		}

		int corte = arg.IndexOf(':');
		string numero = corte < 0 ? arg : arg[..corte];
		string frase = corte < 0 ? "" : arg[(corte + 1)..];

		if (!int.TryParse(numero.Trim(), out int slot) || slot < 1 || slot > FrasesG4)
		{
			Avisar(pl, $"o numero da frase vai de 1 a {FrasesG4}.");
			return;
		}

		// APAGAR E GRAVAR VAZIO, como no DM: la o `reinitialize_words()` so cria o verb pros
		// slots com texto, entao apagar o texto e o jeito de tirar o atalho.
		if (corte < 0 || frase.Trim().Length == 0)
		{
			minhas[slot - 1] = "";
			Avisar(pl, $"frase {slot} apagada.");
			return;
		}

		if (frase.Length > Protocol.MaxFala) frase = frase[..Protocol.MaxFala];
		frase = frase.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();

		minhas[slot - 1] = frase;
		Avisar(pl, $"frase {slot} gravada: '{frase}' (diga com Phrase_{slot}).");
	}

	/// <summary>Dispara uma das dez. E fala de verdade: passa pelo mesmo alcance e pela mesma freada.</summary>
	private void DizerFraseG4(ServerPlayer pl, string verbo)
	{
		if (!int.TryParse(verbo["Phrase_".Length..], out int slot) || slot < 1 || slot > FrasesG4)
		{
			Avisar(pl, $"frase invalida -- vao de Phrase_1 a Phrase_{FrasesG4}.");
			return;
		}

		string[] minhas = FrasesDeG4(pl.Id);
		string frase = minhas[slot - 1];
		if (frase.Length == 0)
		{
			Avisar(pl, $"a frase {slot} está vazia. Grave com Magic_Words:{slot}:<texto>");
			return;
		}

		// PELO CANAL DE FALA NORMAL, nao por um canal proprio: uma frase gravada e uma frase dita,
		// e quem ouve tem que ouvir do mesmo jeito. Isso tambem herda de graca o "!" que vira
		// grito e a freada de 400 ms -- que e justamente o freio que um atalho de fala precisa.
		Falar(pl, Protocol.Fala.Diz, frase);
	}

	private string[] FrasesDeG4(int id)
	{
		if (!_frasesG4.TryGetValue(id, out string[]? v))
		{
			v = new string[FrasesG4];
			for (int i = 0; i < FrasesG4; i++) v[i] = "";
			_frasesG4[id] = v;
		}
		return v;
	}

	// =====================================================================
	// AJUDANTES
	// =====================================================================
	/// <summary>
	/// QUEM ESTA DO MEU LADO e serve pra isto.
	///
	/// O ALVO MARCADO VEM PRIMEIRO, e nao e enfeite: o DM usa `if(!target) target = input(...)`,
	/// ou seja, quem ja mirou em alguem nao ve caixa nenhuma. No port o duplo clique marca o
	/// `AlvoId`, e respeita-lo aqui e o que impede a tecnica de ressuscitar o cadaver errado numa
	/// pilha de corpos -- que e exatamente a situacao em que se ressuscita alguem.
	/// </summary>
	private ServerPlayer? AlvoPertoG4(ServerPlayer pl, float raio, Func<ServerPlayer, bool> serve)
	{
		if (pl.AlvoId != 0 && _players.TryGetValue(pl.AlvoId, out ServerPlayer? marcado)
			&& marcado != pl && marcado.Zone.Equals(pl.Zone)
			&& Vec2.Distance(marcado.Pos, pl.Pos) <= raio && serve(marcado))
			return marcado;

		ServerPlayer? melhor = null;
		float perto = float.MaxValue;
		foreach (ServerPlayer o in _players.Values)
		{
			if (o == pl || !o.Zone.Equals(pl.Zone)) continue;
			float d = Vec2.Distance(o.Pos, pl.Pos);
			if (d > raio || d >= perto || !serve(o)) continue;
			perto = d;
			melhor = o;
		}
		return melhor;
	}

	/// <summary>
	/// PUXA UM CORPO pra uma zona e uma posicao -- o `loc = locate(x,y,z)` do DM.
	///
	/// Sao dois caminhos porque sao dois problemas: mudar de zona reescreve a lista de interesse
	/// e manda o mundo novo (e disso o <see cref="MoveToZone"/> ja cuida); mexer dentro da MESMA
	/// zona precisa so avisar o dono, senao o cliente dele continua desenhando o personagem onde
	/// estava e a proxima validacao de passo o puxa de volta.
	/// </summary>
	private void PuxarParaG4(ServerPlayer quem, ZoneKey zona, Vec2 pos)
	{
		if (!quem.Zone.Equals(zona)) { MoveToZone(quem.Id, zona, pos); return; }

		quem.Pos = pos;
		quem.LastInputMs = NowMs();   // sem isto o proximo pacote em voo vira "correcao" e conta cheat
		quem.CorrecaoEsperadaAte = NowMs() + 1000;   // + a SEQUENCIA: input montado antes deste instante nao opina sobre onde o corpo esta
		quem.SeqDoTeleporte = quem.SeqInput;

		var w = Protocol.Begin(Protocol.S2C.Correction);
		w.Put(quem.SeqInput);   // sequencia + posicao -- ver o comentario nos outros pontos
		w.PutVec(pos);
		quem.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
