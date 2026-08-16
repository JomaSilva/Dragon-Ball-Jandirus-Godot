using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DO AVESSO (`--avessoteste`) -- **a corrente inteira, e o procurador sob ataque**.
///
/// ============================ POR QUE ELA EXISTE, SE JA HA DUAS ============================
/// A `--esferateste` (Fase 1) mede o CORPO do sistema e a `--desejoteste` (Fase 2) mede o que ele FAZ.
/// As duas sao honestas e as duas tem o MESMO cego, e ele esta escrito na regra 2 da tarefa:
/// **elas nascem dentro do estado**.
///
///   * a Fase 2 forja um jogador ja com as sete Super Esferas na mao (`DarSupers`) -- entao ela nunca
///     testou o CLAIM;
///   * ela poe o dragao de pe com um ajudante que teleporta as sete pro colo de quem vai pedir
///     (`PorODragaoDePe`) -- entao ela nunca testou ACHAR nem PEGAR;
///   * e as duas injetam defeito no ESTADO (um campo escrito na mao), nunca no CODIGO. Um `if` que
///     alguem apagar do arquivo continua deixando as duas verdes.
///
/// Esta bancada atravessa a corrente inteira, do nada ate o efeito no corpo, **so por verbo de
/// producao**: erguer -> o radar achar -> viajar -> pegar -> reunir -> invocar -> escolher -> o zeni
/// entrar no bolso -> e as sete SUMIREM. E depois faz a mesma coisa no espaco: achar -> chegar perto ->
/// reivindicar -> os dez segundos correrem no tique -> disputar a de outro -> as sete -> a lingua
/// recusar -> emprestar a voz -> e o desejo cair em quem PEDIU.
///
/// E ela e o alvo das CINCO INJECOES DE CODIGO-FONTE da Fase 3 (feitas a mao, no arquivo de producao,
/// uma por vez, e desfeitas com Edit reverso). Cada uma tem aqui a checagem que ela derruba:
///
///   (a) o portao da lingua apagado ................ secao 9  ("sem a lingua, NADA e consumido")
///   (b) o procurador ficando com o desejo ......... secao 12 ("o zeni entra em quem PEDIU")
///   (c) a disputa de 5 min terminando cedo ........ secao 8  ("aos 10 s a esfera de OUTRO nao muda")
///   (d) a esfera nao sumindo depois do desejo ..... secao 5  ("reunidas as sete, o dragao RECUSA")
///   (e) o determinismo quebrado ................... secao 6  ("onde o servidor pos = onde a conta manda")
/// ======================================================================================
///
/// ============================ AS DUAS METADES, SEMPRE, E NOS MESMOS CORPOS ============================
/// *"afirmacao de UM LADO SO fica verde num sistema morto"*. Aqui elas andam em par:
///
///     de longe o verbo RECUSA          x  chegando perto, ele ACEITA
///     com seis o dragao nao sobe       x  com sete ele SOBE
///     gastas, as sete nao invocam      x  antes do pedido elas invocavam
///     a de outro nao cai aos 10 s      x  cai aos 5 min
///     quem nao fala e recusado         x  quem fala invoca -- **as mesmas sete, no mesmo minuto**
///     o desejo cai no pedinte          x  e NAO cai no porta-voz
/// ==================================================================================================
///
///     Godot --headless --path . --host --rede 7979 --conta bancada_avesso --senha teste
///            --nome AvessoBanca --raca Namekian --avessoteste
/// </summary>
public partial class GameServer
{
	private bool _avessoDeTeste;

	/// <summary>
	/// A BANCADA DO AVESSO. Roda uma vez, no primeiro login -- e ela precisa MESMO de alguem com
	/// `Peer`: o canal de claim da Super Esfera cai na primeira condicao de aborto quando o disputante
	/// "saiu do mundo", e *"saiu do mundo" e literalmente `Peer == null`*. Um corpo forjado nunca
	/// fecha um claim, e por isso quem atravessa a corrente aqui e o testador de verdade.
	/// </summary>
	private void RodarBancadaDoAvesso(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA DO AVESSO: A CORRENTE INTEIRA E O PROCURADOR SOB ATAQUE =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		// ============================ O MUNDO VOLTA COMO ESTAVA ============================
		// Ela ergue estatua, espalha esferas de verdade, poe e tira o trono de Guardiao, viaja pro
		// espaco, reivindica as sete Super, empresta a guarda, gasta desejo e escreve nos dois
		// arquivos. Tudo fotografado aqui e devolvido no `finally`.
		// ==============================================================================
		var setsGuardados = new List<SetDeEsferas>(_sets);
		var esferasGuardadas = new List<Esfera>(_esferas);
		var supersGuardadas = _supers.Select(s => (s.Numero, s.Dono, s.DonoNome)).ToList();
		int cicloGuardado = _cicloDasSupers;
		(string bs, string bn, string bp, string ba) benefGuardado =
			(_benefSig, _benefNome, _pedidoDoBenef, _alvoDoPedido);
		double ceuGuardado = _adiantoDoCeu;
		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		string racaGuardada = pl.Race, classeGuardada = pl.Class;
		double bpGuardado = pl.Ficha.BP, zeniGuardado = pl.Ficha.Zeni;
		bool linguaGuardada = pl.Ficha.godtongue;
		int radarGuardado = pl.Mochila.Quantos(Jandirus.Core.Items.CatalogoDeItens.Radar);
		var tronosGuardados = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		var forjados = new List<ServerPlayer>();

		EscutaDasEsferas = [];
		EscutaDasSupers = [];
		EscutaDosDesejos = [];
		List<string>? escutaDeAvisosAntes = EscutaDeAvisos;

		try
		{
			// =====================================================================
			// 1. O COMECO: O MUNDO SEM NADA NAS MAOS DELE
			// =====================================================================
			// ============================ ELA NAO PODE NASCER DENTRO DO ESTADO ============================
			// A primeira coisa que esta bancada faz e AFIRMAR O ZERO. Sem isto, "ele reuniu as sete" seria
			// indistinguivel de "ele ja tinha as sete quando a bancada comecou" -- que e exatamente o
			// defeito que a tarefa nomeou e que as duas bancadas anteriores tem.
			// =========================================================================================
			_sets.RemoveAll(s => !s.Eterno);
			_esferas.RemoveAll(e => !_sets.Any(s => s.Id == e.Set));
			foreach (SuperEsfera s in _supers) { s.Dono = ""; s.DonoNome = ""; }
			_disputasDeSuper.Clear();
			_invocacoes.Clear();
			_benefSig = ""; _benefNome = ""; _pedidoDoBenef = ""; _alvoDoPedido = "";

			var terra = ZoneKey.Premade("Earth");
			MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
			pl.Race = "Namekian";
			pl.Class = "Dragon clan";
			pl.Ficha.BP = 5_000_000;
			pl.Ficha.Statify();
			_tronos["guardian"] = pl.Conta;

			Checa("o testador comeca SEM esfera nenhuma na mao e SEM Super Esfera reivindicada",
				  QuantasCarrega(pl.Id) == 0 && QuantasSupersTem(pl.Assinatura) == 0,
				  $"carrega {QuantasCarrega(pl.Id)}, super {QuantasSupersTem(pl.Assinatura)}");

			// =====================================================================
			// 2. ERGUER -- e as sete nascem ESPALHADAS, e nao no colo dele
			// =====================================================================
			ErguerEstatua(pl, "");
			SetDeEsferas? set = _sets.Find(s => !s.Eterno && s.Zona.Hash == terra.Hash);
			Checa("o verbo de producao ergue a estatua e as sete NASCEM",
				  set != null && _esferas.Count(e => e.Set == set.Id) == Esferas.Total,
				  $"{_esferas.Count(e => set != null && e.Set == set.Id)} esferas");

			if (set == null) throw new InvalidOperationException("sem set: o resto da corrente nao existe");

			List<Esfera> asSete = [.. _esferas.Where(e => e.Set == set.Id)];

			// ELAS NASCEM LONGE UMAS DAS OUTRAS. Sem isto, "ele viajou ate cada uma" poderia ser
			// "todas cairam no mesmo tile" -- e a corrente inteira seria um passo so.
			double maiorDistancia = 0;
			foreach (Esfera a in asSete)
				foreach (Esfera b in asSete)
					maiorDistancia = Math.Max(maiorDistancia,
						new Vec2(a.X - b.X, a.Y - b.Y).Length);
			Checa("...e caem ESPALHADAS pelo mundo (a busca e uma viagem, e nao um passo)",
				  maiorDistancia > 20 * ZoneCollision.TileSize,
				  $"a maior distancia entre duas e {maiorDistancia / ZoneCollision.TileSize:0} tiles");

			// Elas nascem APAGADAS (`ActiveYear = Year + 0.4`, Dragonballs.dm:108). O relogio anda pra
			// que a corrente possa continuar -- e o proprio prazo ja e medido pela `--esferateste`.
			_adiantoDoCeu += (set.AtivoEm - TempoDoMundo) + 5;
			Checa("...e passada a espera de nascimento elas ACORDAM", SetAtivo(set));

			// =====================================================================
			// 3. ACHAR -- o radar, e o lugar de onde ele NAO alcanca
			// =====================================================================
			ZoneCollision? mapa = MapaDaZonaOuCatalogo(terra);
			Vec2 longeDeTudo = PontoLongeDasEsferas(mapa, asSete);
			pl.Pos = longeDeTudo;

			EscutaDeAvisos = [];
			pl.Mochila.Tirar(Jandirus.Core.Items.CatalogoDeItens.Radar, 9);
			UsarORadar(pl);
			Checa("sem o Dragon Radar na mochila o radar RECUSA (o item e a porta de entrada)",
				  EscutaDeAvisos.Any(t => t.Contains("não tem um Dragon Radar")),
				  string.Join(" | ", EscutaDeAvisos));

			pl.Mochila.Guardar(Jandirus.Core.Items.CatalogoDeItens.Radar);
			EscutaDeAvisos.Clear();
			UsarORadar(pl);
			int sinais = EscutaDeAvisos.Count(t => t.Contains("estrela"));
			Checa($"...e COM o radar ele ve as {Esferas.Total} deste mundo",
				  sinais == Esferas.Total, $"achou {sinais}");
			EscutaDeAvisos = null;

			// ============================ A ENTRADA NO ESTADO, QUE E O QUE FALTAVA ============================
			// **De longe, o verbo de pegar RECUSA.** Esta e a linha que separa esta bancada das outras
			// duas: elas punham a esfera na mao e mediam o resto. Sem esta recusa, "ele pegou as sete"
			// nao quer dizer que exista alcance nenhum.
			// ============================================================================================
			EscutaDeAvisos = [];
			PegarEsfera(pl);
			Checa("de LONGE o verbo de pegar recusa -- ha alcance de verdade (`oview(1)`, :344)",
				  QuantasCarrega(pl.Id) == 0
				  && EscutaDeAvisos.Any(t => t.Contains("ao seu alcance")),
				  string.Join(" | ", EscutaDeAvisos));
			EscutaDeAvisos = null;

			// =====================================================================
			// 4. VIAJAR E PEGAR -- uma por uma, pelo verbo de producao
			// =====================================================================
			// A VIAGEM E O UNICO PASSO QUE A BANCADA FAZ NO LUGAR DO JOGADOR, e ela e so isso: pousar o
			// corpo no lugar. Quem decide se da pra pegar continua sendo o `PegarEsfera`, que mede a
			// distancia sozinho -- e acabou de provar, na linha de cima, que ele mede.
			for (int i = 0; i < Esferas.Total - 1; i++)
			{
				pl.Pos = new Vec2(asSete[i].X, asSete[i].Y);
				PegarEsfera(pl);
			}

			Checa($"viajando ate cada uma, ele junta {Esferas.Total - 1} das {Esferas.Total}",
				  QuantasCarrega(pl.Id) == Esferas.Total - 1, $"carrega {QuantasCarrega(pl.Id)}");

			// COM SEIS O DRAGAO NAO SOBE -- e ele esta LONGE da setima, senao ela contaria do chao
			// (`SeteJuntas` conta as do chao ao redor, que e o `view(1,usr)` do DM).
			pl.Pos = longeDeTudo;
			EscutaDeAvisos = [];
			InvocarODragao(pl);
			Checa($"com {Esferas.Total - 1} reunidas o dragao NAO sobe",
				  !_invocacoes.ContainsKey(set.Id)
				  && EscutaDeAvisos.Any(t => t.Contains($"das {Esferas.Total} esferas")),
				  string.Join(" | ", EscutaDeAvisos));
			EscutaDeAvisos = null;

			// A SETIMA.
			pl.Pos = new Vec2(asSete[Esferas.Total - 1].X, asSete[Esferas.Total - 1].Y);
			PegarEsfera(pl);
			Checa("...e pegando a setima ele tem as SETE",
				  QuantasCarrega(pl.Id) == Esferas.Total, $"carrega {QuantasCarrega(pl.Id)}");

			EscutaDasEsferas.Clear();
			InvocarODragao(pl);
			Checa("...e ENTAO o dragao sobe (as duas metades da mesma pergunta)",
				  _invocacoes.ContainsKey(set.Id));
			Checa("...e o mundo ouve a fala dele ('EU SOU ... DIGA O SEU DESEJO!')",
				  EscutaDasEsferas.Any(t => t.Contains("DIGA O SEU DESEJO")),
				  string.Join(" | ", EscutaDasEsferas));

			// =====================================================================
			// 5. O DESEJO NO CORPO -- e as sete SUMIREM
			// =====================================================================
			double zeniAntes = pl.Ficha.Zeni;
			var ondeEstavam = asSete.Select(e => new Vec2(e.X, e.Y)).ToList();
			int cicloDoSetAntes = set.Ciclo;

			EscutaDosDesejos.Clear();
			PedirDesejo(pl, "dinheiro");

			// RESULTADO NO CORPO, e nao "o campo Desejo foi escrito" (regra 3 da tarefa).
			Checa($"o desejo entra no CORPO: +{Desejos.ZeniDoDesejo:N0} zeni no bolso de quem pediu",
				  Math.Abs(pl.Ficha.Zeni - (zeniAntes + Desejos.ZeniDoDesejo)) < 1,
				  $"{zeniAntes:N0} -> {pl.Ficha.Zeni:N0}");

			Checa("...e o dragao vai embora (a invocacao fecha)", !_invocacoes.ContainsKey(set.Id));

			// E O MUNDO OUVIU. Um desejo que acontece em silencio nao da a ninguem a chance de reagir a
			// ele -- e o canal existir sem leitor e o pecado classico deste repo.
			Checa("...e o MUNDO ouve o pedido (o anuncio de cada desejo, e nao so o efeito)",
				  EscutaDosDesejos.Any(t => t.Contains(pl.Name, StringComparison.Ordinal)),
				  string.Join(" | ", EscutaDosDesejos));

			// ============================ E AS SETE SOMEM -- A INJECAO (d) MORA AQUI ============================
			// `D_Wish` (:233-239) faz tres coisas no fim: apaga (`IsInactive = 1`), remarca (`ActiveYear`)
			// e ESPALHA. Se qualquer uma delas cair, o set vira uma maquina de desejo infinito -- e o jogo
			// inteiro sai do prumo em silencio, porque nada estoura.
			// ================================================================================================
			Checa("as sete se APAGAM depois do pedido (`ActiveYear = Year + OffTime...`, :235)",
				  !SetAtivo(set), $"AtivoEm - agora = {set.AtivoEm - TempoDoMundo:0} s");

			Checa("...e saem da mao de quem pediu e se ESPALHAM de novo",
				  QuantasCarrega(pl.Id) == 0 && set.Ciclo == cicloDoSetAntes + 1,
				  $"carrega {QuantasCarrega(pl.Id)}, ciclo {cicloDoSetAntes} -> {set.Ciclo}");

			var ondeEstaoAgora = _esferas.Where(e => e.Set == set.Id)
										 .Select(e => new Vec2(e.X, e.Y)).ToList();
			int mudaramDeLugar = ondeEstaoAgora.Where((p, i) => (p - ondeEstavam[i]).Length > 1).Count();
			Checa("...e em LUGARES NOVOS (senao 'se espalham' seria uma frase)",
				  mudaramDeLugar >= Esferas.Total - 1, $"{mudaramDeLugar} das {Esferas.Total} mudaram");

			EscutaDeAvisos = [];
			UsarORadar(pl);
			Checa("...e o radar NAO acha mais nenhuma (esfera apagada nao aparece, Tier 1.5.dm:241)",
				  !EscutaDeAvisos.Any(t => t.Contains("estrela")),
				  string.Join(" | ", EscutaDeAvisos));
			EscutaDeAvisos = null;

			// ============================ E A PROVA QUE SO ESTA BANCADA FAZ ============================
			// Reunir as sete OUTRA VEZ -- viajando e pegando, como da primeira -- e provar que **com as
			// sete na mao** o dragao continua recusando. Sem este segundo laco, "elas somem" seria
			// indistinguivel de "elas estao longe", e apagar o prazo do codigo de producao deixaria a
			// bancada verde.
			// ======================================================================================
			List<Esfera> deNovo = [.. _esferas.Where(e => e.Set == set.Id)];
			foreach (Esfera e in deNovo)
			{
				pl.Pos = new Vec2(e.X, e.Y);
				PegarEsfera(pl);
			}

			EscutaDeAvisos = [];
			InvocarODragao(pl);
			Checa("REUNIDAS AS SETE DE NOVO, o dragao RECUSA: elas ja deram o que tinham",
				  !_invocacoes.ContainsKey(set.Id)
				  && EscutaDeAvisos.Any(t => t.Contains("se refazem")),
				  $"carrega {QuantasCarrega(pl.Id)} | " + string.Join(" | ", EscutaDeAvisos));
			EscutaDeAvisos = null;

			// A OUTRA METADE: adiantado o prazo, as MESMAS sete voltam a servir. Sem ela, "recusa" seria
			// indistinguivel de um set que morreu pra sempre.
			_adiantoDoCeu += (set.AtivoEm - TempoDoMundo) + 5;
			TickDasEsferas();
			InvocarODragao(pl);
			Checa("...e passada a espera as MESMAS sete invocam de novo (o prazo e prazo, e nao morte)",
				  _invocacoes.ContainsKey(set.Id));
			FecharAInvocacao(set.Id, "");

			// =====================================================================
			// 6. DETERMINISMO MEDIDO NO RESULTADO -- a injecao (e) mora aqui
			// =====================================================================
			// ============================ NAO E "A FUNCAO DEVOLVE O MESMO" ============================
			// Isso a `--esferateste` ja afirma, e e uma afirmacao sobre a funcao. Aqui a pergunta e a que
			// o jogo faz: **onde o SERVIDOR pos cada esfera e onde a conta pura manda?** E a mesma conta
			// que o cliente faria, refeita fora do servidor -- e e a unica forma de o "os dois lados
			// chegam nela sozinhos" do cabecalho do `SuperEsferas.PosicaoDa` valer alguma coisa.
			// ====================================================================================
			int bateram = 0;
			foreach (Esfera e in _esferas.Where(x => x.Set == set.Id))
			{
				(int cx, int cy) = Esferas.CelulaDoEspalhamento(
					set.Zona.Seed ^ Espaco.Hash64(set.ZonaNome), set.Id, e.Numero, set.Ciclo,
					mapa?.Width ?? 256, mapa?.Height ?? 256);

				const int t = ZoneCollision.TileSize;
				var cru = new Vec2(cx * t + t / 2f, cy * t + t / 2f);
				Vec2 esperado = mapa != null ? mapa.PontoLivrePerto(cru) : cru;

				if (e.X.Equals(esperado.X) && e.Y.Equals(esperado.Y)) bateram++;
			}
			Checa("onde o SERVIDOR pos cada esfera e exatamente onde a conta PURA manda "
				+ "(bit a bit, e nao 'quase')",
				  bateram == Esferas.Total, $"bateram {bateram} de {Esferas.Total}");

			// ESPALHAR O MESMO CICLO DUAS VEZES DA O MESMO LUGAR. E o que um reinicio faz.
			var antesDoReespalho = _esferas.Where(e => e.Set == set.Id)
										   .Select(e => new Vec2(e.X, e.Y)).ToList();
			set.Ciclo--;                 // o `EspalharOSet` incrementa: assim ele reconstroi o MESMO ciclo
			EspalharOSet(set);
			var depoisDoReespalho = _esferas.Where(e => e.Set == set.Id)
											.Select(e => new Vec2(e.X, e.Y)).ToList();
			Checa("...e espalhar o MESMO ciclo de novo cai no MESMO lugar (sobrevive a um reinicio)",
				  antesDoReespalho.Zip(depoisDoReespalho).All(p => p.First.X.Equals(p.Second.X)
															   && p.First.Y.Equals(p.Second.Y)));

			// A METADE QUE PEGA A FUNCAO CONSTANTE: um ciclo NOVO tem que mudar tudo de lugar.
			EspalharOSet(set);
			var noCicloNovo = _esferas.Where(e => e.Set == set.Id)
									  .Select(e => new Vec2(e.X, e.Y)).ToList();
			int mudou = depoisDoReespalho.Zip(noCicloNovo).Count(p => (p.First - p.Second).Length > 1);
			Checa("...e um ciclo NOVO muda tudo de lugar (senao 'deterministico' seria uma constante)",
				  mudou >= Esferas.Total - 1, $"{mudou} das {Esferas.Total} mudaram");

			// =====================================================================
			// 7. O ESPACO: ACHAR E REIVINDICAR -- a corrente das Super, do zero
			// =====================================================================
			GD.Print("  -- as Super Esferas: a corrente do claim --");

			// ============================ O TRONO SAI ANTES DE ENTRAR NO ESPACO ============================
			// O Guardiao da Terra e um dos onze cargos divinos (`WishTable.dm:30`), e a lingua **nunca se
			// esquece**. Se o testador entrasse na secao da lingua ainda com o trono, o
			// `ConferirALingua` do proprio `ChamarOSuperShenron` o ensinaria, e a recusa da secao 9
			// ficaria verde por vacuidade -- ou melhor: ficaria VERMELHA, e por um motivo que nao tem
			// nada a ver com o portao.
			// =========================================================================================
			_tronos.Remove("guardian");

			MoveToZone(pl.Id, ZonaDoEspaco, OndeEstaASuper(1));
			pl.Ficha.KO = false;
			pl.Ficha.dead = false;

			// O SINAL DOURADO -- a mesma funcao que o `MandarSupers` chama pro painel Nav.
			Checa("o radar dourado NAO sente uma Super Esfera longe demais "
				+ $"(alem de {SuperEsferas.CelulasDeSinal} celulas)",
				  SuperEsferas.SinalDoRadar(SuperEsferas.CelulasDeSinal + 1, OndeEstaASuper(1), 1).Length == 0);

			string frase = SuperEsferas.SinalDoRadar(0, OndeEstaASuper(1), 1);
			Checa("...e na celula certa ele entrega a POSICAO literal (o *\"NESTE setor\"* do DM)",
				  frase.Contains("NESTA célula"), frase);

			// A ENTRADA: de longe o verbo recusa.
			pl.Pos = OndeEstaASuper(1) + new Vec2(SuperEsferas.AlcanceDeReivindicar * 8, 0);
			EscutaDeAvisos = [];
			ReivindicarSuper(pl);
			Checa("de LONGE o verbo de reivindicar recusa (`get_dist(usr,src) <= 2`, :1510)",
				  !_disputasDeSuper.ContainsKey(pl.Id)
				  && EscutaDeAvisos.Any(t => t.Contains("Chegue perto")),
				  string.Join(" | ", EscutaDeAvisos));
			EscutaDeAvisos = null;

			// E CHEGANDO PERTO, ELE ABRE -- e os dez segundos correm no TIQUE DE PRODUCAO.
			pl.Pos = OndeEstaASuper(1);
			ReivindicarSuper(pl);
			Checa("...e encostado nela o canal ABRE", _disputasDeSuper.ContainsKey(pl.Id));

			for (int i = 0; i < (int)SuperEsferas.SegundosDeClaim + 2; i++) TickDasSuperEsferas();
			Checa($"{SuperEsferas.SegundosDeClaim:0} s parado FECHAM o claim -- a primeira e dele",
				  QuantasSupersTem(pl.Assinatura) == 1, $"tem {QuantasSupersTem(pl.Assinatura)}");

			// =====================================================================
			// 8. A DISPUTA CONTRA UM DONO -- a injecao (c) mora aqui
			// =====================================================================
			// O RIVAL. Ele e o mesmo corpo que vai virar o PORTA-VOZ na secao 11, e isso e de proposito:
			// *"o falante sendo inimigo do pedinte"* e a borda que a tarefa pediu, e ela fica muito mais
			// afiada quando o inimigo e alguem de quem o pedinte JA TOMOU uma esfera a forca.
			ServerPlayer rival = Forjar("bancada: o rival que fala", pl.Pos, ZonaDoEspaco, "bancada_avesso_r");
			forjados.Add(rival);
			rival.Race = "Kai";
			rival.Ficha.Race = "Kai";
			rival.Ficha.ParentRace = "";

			// A ESFERA #2 JA E DELE. Este e o unico estado que a bancada herda em vez de construir, e o
			// motivo esta escrito: **um corpo forjado nao tem `Peer`**, e a primeira condicao de aborto do
			// canal e exatamente essa. Nenhum claim de NPC fecharia. O que esta sob medicao aqui e a
			// DISPUTA DELE CONTRA O RIVAL, e essa passa inteira pelo verbo de producao.
			_supers[1].Dono = rival.Assinatura;
			_supers[1].DonoNome = rival.Name;
			_recadosDeConquista.Remove(rival.Assinatura);

			pl.Pos = OndeEstaASuper(2);
			ReivindicarSuper(pl);
			Checa("tomar a esfera DE OUTRO abre o canal LONGO, e nao o curto",
				  _disputasDeSuper.TryGetValue(pl.Id, out DisputaDeSuper? disp)
				  && disp.Tomada && disp.Faltam > SuperEsferas.SegundosDeClaim,
				  $"faltam {_disputasDeSuper.GetValueOrDefault(pl.Id)?.Faltam:0} s");

			Checa("...e o dono e avisado NA FILA DA CONQUISTA (o `conq_notify_owner` que o DM reusa, :1547)",
				  _recadosDeConquista.TryGetValue(rival.Assinatura, out List<string>? fila)
				  && fila.Any(t => t.Contains("Super Esfera")),
				  "a fila da conquista nao recebeu nada -- o reuso nao aconteceu");

			// ============================ A INJECAO (c) MORA AQUI ============================
			// Aos dez segundos -- o prazo da esfera LIVRE -- a esfera de OUTRO **nao pode** mudar de dono.
			// A razao 30:1 entre os dois prazos e o desenho inteiro: achar uma esfera livre e exploracao,
			// tomar a de alguem e um EVENTO com cinco minutos pro dono chegar.
			// ============================================================================
			for (int i = 0; i < (int)SuperEsferas.SegundosDeClaim + 2; i++) TickDasSuperEsferas();
			Checa($"aos {SuperEsferas.SegundosDeClaim:0} s a esfera de OUTRO ainda NAO mudou de dono "
				+ "(a disputa e 30x mais longa)",
				  string.Equals(_supers[1].Dono, rival.Assinatura, StringComparison.Ordinal)
				  && _disputasDeSuper.ContainsKey(pl.Id),
				  $"dono '{_supers[1].DonoNome}', canal aberto: {_disputasDeSuper.ContainsKey(pl.Id)}");

			// A OUTRA METADE: aos cinco minutos ela troca de mao. Sem ela, "nao muda aos 10 s" ficaria
			// verde num canal que nunca fecha.
			for (int i = 0; i < (int)SuperEsferas.SegundosDeDisputa + 2; i++) TickDasSuperEsferas();
			Checa($"...e aos {SuperEsferas.SegundosDeDisputa / 60:0} min ela TROCA de mao",
				  string.Equals(_supers[1].Dono, pl.Assinatura, StringComparison.Ordinal)
				  && QuantasSupersTem(pl.Assinatura) == 2,
				  $"dono '{_supers[1].DonoNome}', ele tem {QuantasSupersTem(pl.Assinatura)}");

			Checa("...e o ex-dono recebe o recado da PERDA",
				  _recadosDeConquista.TryGetValue(rival.Assinatura, out List<string>? f2)
				  && f2.Any(t => t.Contains("PERDEU")),
				  string.Join(" | ", _recadosDeConquista.GetValueOrDefault(rival.Assinatura) ?? []));

			// AS CINCO QUE FALTAM, pelo mesmo caminho.
			for (int n = 3; n <= SuperEsferas.Total; n++)
			{
				pl.Pos = OndeEstaASuper(n);
				ReivindicarSuper(pl);
				for (int i = 0; i < (int)SuperEsferas.SegundosDeClaim + 2; i++) TickDasSuperEsferas();
			}

			Checa($"reivindicando uma a uma, ele chega as {SuperEsferas.Total} Super Esferas",
				  QuantasSupersTem(pl.Assinatura) == SuperEsferas.Total,
				  $"tem {QuantasSupersTem(pl.Assinatura)}");

			// =====================================================================
			// 9. A LINGUA -- a injecao (a) mora aqui
			// =====================================================================
			GD.Print("  -- a lingua dos deuses --");

			ConferirALingua(pl);
			Checa("o Namekuseijin sem cargo divino NAO fala a lingua dos deuses "
				+ "(nem cargo, nem sangue Kai/Demigod)",
				  !pl.Ficha.godtongue);

			int cicloAntesDaRecusa = _cicloDasSupers;
			EscutaDeAvisos = [];
			ChamarOSuperShenron(pl, "sdb_riqueza");
			bool recusouPelaLingua = EscutaDeAvisos.Any(t => t.Contains("LÍNGUA DOS DEUSES"));
			bool ensinouOProcurador = EscutaDeAvisos.Any(t => t.Contains("TRANSFIRA"));
			EscutaDeAvisos = null;

			// ============================ A INJECAO (a) MORA AQUI ============================
			// COM AS SETE E SEM A LINGUA: recusa, e **nada e consumido**. As duas metades da mesma linha
			// -- porque uma recusa que consumisse o conjunto seria pior que nao recusar.
			// ============================================================================
			Checa("com as SETE e sem a lingua, o Super Shenron RECUSA",
				  recusouPelaLingua, "a recusa nao falou da lingua");

			Checa("...e NADA e consumido: o ciclo nao anda e as sete continuam dele",
				  _cicloDasSupers == cicloAntesDaRecusa
				  && QuantasSupersTem(pl.Assinatura) == SuperEsferas.Total,
				  $"ciclo {cicloAntesDaRecusa} -> {_cicloDasSupers}, "
				  + $"tem {QuantasSupersTem(pl.Assinatura)}");

			// A RECUSA E A UNICA LINHA DO JOGO QUE ENSINA QUE O PROCURADOR EXISTE. Sem ela, o sistema
			// inteiro da secao 11 fica inalcancavel pra quem nao leu o codigo.
			Checa("...e a recusa ENSINA a saida (o verbo de transferir, :1588)", ensinouOProcurador);

			// =====================================================================
			// 10. EMPRESTAR A VOZ -- a oferta, a recusa, e o aceite
			// =====================================================================
			GD.Print("  -- o procurador --");

			ConferirALingua(rival);
			Checa("o RIVAL fala a lingua pelo SANGUE (raca Kai, sem cargo nenhum -- WishTable.dm:39)",
				  rival.Ficha.godtongue);

			// A BORDA "O FALANTE RECUSA".
			rival.Pos = pl.Pos;
			rival.Zone = pl.Zone;
			TransferirAGuarda(pl, rival.Name);
			Checa("a oferta de guarda fica de pe esperando a resposta dele",
				  _ofertasDeGuarda.ContainsKey(rival.Conta));

			ResponderAGuarda(rival, aceitou: false);
			Checa("...e RECUSADA, as sete ficam com quem as tinha e nao nasce procuracao nenhuma",
				  QuantasSupersTem(pl.Assinatura) == SuperEsferas.Total && !HaProcuracao,
				  $"tem {QuantasSupersTem(pl.Assinatura)}, procuracao: {HaProcuracao}");

			// AGORA ACEITANDO.
			TransferirAGuarda(pl, rival.Name);
			ResponderAGuarda(rival, aceitou: true);
			Checa("aceita, as SETE passam ao porta-voz",
				  QuantasSupersTem(rival.Assinatura) == SuperEsferas.Total
				  && QuantasSupersTem(pl.Assinatura) == 0);
			Checa("...e o BENEFICIARIO gravado e o PEDINTE, e nao quem carrega (:1676-1677)",
				  string.Equals(_benefSig, pl.Assinatura, StringComparison.Ordinal));

			// =====================================================================
			// 11. O INIMIGO TENTA -- as CINCO rotas de roubo, todas fechadas
			// =====================================================================
			// ============================ POR QUE ELE E INIMIGO, E POR QUE ISSO IMPORTA ============================
			// A tarefa pediu a borda *"o falante sendo inimigo do pedinte"*. Ela nao e um humor: e a
			// situacao NORMAL deste sistema. So existem sete Super Esferas no universo e so os cargos
			// divinos e o sangue Kai/Demigod falam a lingua -- entao quem junta as sete quase nunca vai
			// poder escolher um amigo pra falar. Este rival acabou de PERDER uma esfera pro pedinte numa
			// disputa de cinco minutos, e agora carrega as sete dele. Todas as portas que ele tem:
			// ==================================================================================================
			string pedidoRegistrado = "sdb_riqueza";
			RegistrarPedido(pl, pedidoRegistrado);
			Checa("so o PEDINTE registra o desejo -- e ele registrou",
				  _pedidoDoBenef == pedidoRegistrado, $"ficou '{_pedidoDoBenef}'");

			// ROTA 1: reescrever o pedido.
			EscutaDeAvisos = [];
			RegistrarPedido(rival, "sdb_supremo");
			EscutaDeAvisos = null;
			Checa("ROTA 1 fechada: o porta-voz NAO consegue reescrever o pedido",
				  _pedidoDoBenef == pedidoRegistrado, $"virou '{_pedidoDoBenef}'");

			// ROTA 2: repassar as sete a um comparsa (o buraco (A) do DM).
			ServerPlayer comparsa = Forjar("bancada: o comparsa", pl.Pos, ZonaDoEspaco, "bancada_avesso_c");
			forjados.Add(comparsa);

			EscutaDeAvisos = [];
			TransferirAGuarda(rival, comparsa.Name);
			bool disseProcuracao = EscutaDeAvisos.Any(t => t.Contains("procuração não se repassa"));
			EscutaDeAvisos = null;
			Checa("ROTA 2 fechada: procuracao NAO se repassa (buraco (A) do original)",
				  !_ofertasDeGuarda.ContainsKey(comparsa.Conta) && disseProcuracao,
				  $"oferta aberta: {_ofertasDeGuarda.ContainsKey(comparsa.Conta)}");

			// ROTA 3: esperar o pedinte sair do mundo e invocar sozinho (o buraco (B) do DM).
			ZoneList(pl.Zone.Hash).Remove(pl);
			_players.Remove(pl.Id);

			int cicloComPedinteFora = _cicloDasSupers;
			EscutaDeAvisos = [];
			ChamarOSuperShenron(rival, "");
			EscutaDeAvisos = null;

			Checa("ROTA 3 fechada: com o PEDINTE fora do mundo o desejo e recusado (buraco (B))",
				  _cicloDasSupers == cicloComPedinteFora
				  && QuantasSupersTem(rival.Assinatura) == SuperEsferas.Total,
				  $"ciclo {cicloComPedinteFora} -> {_cicloDasSupers}");

			_players[pl.Id] = pl;
			ZoneList(pl.Zone.Hash).Add(pl);

			// ROTA 4: sumir com as sete pra sempre -- e a metade que so existe neste port.
			// O rival "desliga" com as sete na mao. No DM isto e o fim: nao ha como tomar de volta o que
			// se emprestou, e o unico conjunto de Super Esferas do universo fica preso num personagem que
			// nunca mais loga. Ver o cabecalho do `RevogarAGuarda`.
			ZoneList(rival.Zone.Hash).Remove(rival);
			_players.Remove(rival.Id);

			RevogarAGuarda(pl);
			Checa("ROTA 4 fechada: o falante SOME com as sete e o pedinte as RETOMA",
				  QuantasSupersTem(pl.Assinatura) == SuperEsferas.Total && !HaProcuracao,
				  $"tem {QuantasSupersTem(pl.Assinatura)}, procuracao: {HaProcuracao}");

			_players[rival.Id] = rival;
			ZoneList(rival.Zone.Hash).Add(rival);

			// ROTA 5: lavar a procuracao numa disputa combinada com o comparsa.
			// ============================ E O ROUBO CUSTA A ELE, E NAO AO PEDINTE ============================
			// `:1571-1573` -- uma esfera tomada A FORCA quebra a procuracao. O rival podia achar que isso o
			// favorece: sem procuracao, as sete que ele carrega sao dele e o desejo tambem. So que o preco
			// da quebra e uma esfera: ele fica com SEIS, e seis nao invocam nada.
			//
			// O `FecharADisputa` e chamado direto (e nao pelo canal) porque **o comparsa e um corpo forjado
			// e nao tem `Peer`** -- o canal dele cairia na primeira condicao de aborto. O que esta sob
			// medicao aqui e o EFEITO da tomada, e ele passa inteiro pelo funil de producao.
			// ============================================================================================
			TransferirAGuarda(pl, rival.Name);
			ResponderAGuarda(rival, aceitou: true);
			RegistrarPedido(pl, pedidoRegistrado);

			SuperEsfera roubada = _supers[0];
			FecharADisputa(comparsa, roubada, new DisputaDeSuper
			{
				Numero = roubada.Numero, Quem = comparsa.Id, Tomada = true, DonoNoInicio = roubada.Dono,
			});

			Checa("ROTA 5 fechada: tomar a forca QUEBRA a procuracao (:1571-1573)", !HaProcuracao);

			EscutaDeAvisos = [];
			ChamarOSuperShenron(rival, "sdb_riqueza");
			bool faltaramEsferas = EscutaDeAvisos.Any(t => t.Contains($"de {SuperEsferas.Total}"));
			EscutaDeAvisos = null;
			Checa("...e o roubo custa a ELE: ficou com seis, e seis nao invocam nada",
				  QuantasSupersTem(rival.Assinatura) == SuperEsferas.Total - 1 && faltaramEsferas,
				  $"tem {QuantasSupersTem(rival.Assinatura)}");

			// A esfera volta pro pedinte e a procuracao e refeita, pra o tiro central.
			roubada.Dono = pl.Assinatura;
			roubada.DonoNome = pl.Name;
			foreach (SuperEsfera s in _supers) { s.Dono = pl.Assinatura; s.DonoNome = pl.Name; }
			SalvarSupers();

			TransferirAGuarda(pl, rival.Name);
			ResponderAGuarda(rival, aceitou: true);
			RegistrarPedido(pl, pedidoRegistrado);

			// =====================================================================
			// 12. O TIRO CENTRAL -- a injecao (b) mora aqui
			// =====================================================================
			// ============================ A PERGUNTA DO DONO, MEDIDA NO BOLSO ============================
			// *"as pessoas q n sabem a lingua dos deuses tem q emprestar/pedir pra outra pessoa q saiba a
			// lingua dos deuses falar com o super shenlong"*. O falante manda `sdb_supremo` -- OUTRO desejo,
			// o mais caro do jogo, o que troca a vida por poder. O registrado e `sdb_riqueza`.
			//
			// Tres medicoes no mesmo instante, e as tres precisam ser verdade ao mesmo tempo:
			//   1. o zeni entra em quem PEDIU;
			//   2. NAO entra em quem falou;
			//   3. o desejo que ACONTECEU e o registrado -- ninguem ficou com a divida do supremo.
			// ========================================================================================
			double zeniPedinteAntes = pl.Ficha.Zeni;
			double zeniVozAntes = rival.Ficha.Zeni;
			double dividaPedinteAntes = pl.Ficha.sw_doom_year;
			double dividaVozAntes = rival.Ficha.sw_doom_year;
			int cicloAntesDoTiro = _cicloDasSupers;

			EscutaDasSupers.Clear();
			ChamarOSuperShenron(rival, "sdb_supremo");   // <- ele TENTA trocar o desejo

			Checa("O DESEJO CAI EM QUEM PEDIU: o zeni entra no bolso do PEDINTE",
				  Math.Abs(pl.Ficha.Zeni - (zeniPedinteAntes + Desejos.ZeniDoSuperDesejo)) < 1,
				  $"{zeniPedinteAntes:N0} -> {pl.Ficha.Zeni:N0}");

			Checa("...e NAO entra no porta-voz (a metade que prova que nao foi so 'alguem recebeu')",
				  Math.Abs(rival.Ficha.Zeni - zeniVozAntes) < 1,
				  $"{zeniVozAntes:N0} -> {rival.Ficha.Zeni:N0}");

			Checa("O FALANTE NAO TROCOU O DESEJO: ele pediu o supremo e o supremo NAO aconteceu "
				+ "(ninguem ficou com a divida)",
				  Math.Abs(pl.Ficha.sw_doom_year - dividaPedinteAntes) < 1
				  && Math.Abs(rival.Ficha.sw_doom_year - dividaVozAntes) < 1,
				  $"pedinte {dividaPedinteAntes:0} -> {pl.Ficha.sw_doom_year:0}, "
				  + $"falante {dividaVozAntes:0} -> {rival.Ficha.sw_doom_year:0}");

			Checa("...e o mundo ouve o nome de QUEM RECEBEU, e nao o de quem falou",
				  EscutaDasSupers.Any(t => t.Contains("riqueza") && t.Contains(pl.Name)),
				  string.Join(" | ", EscutaDasSupers));

			Checa("...e as sete se consomem e o ciclo ANDA (elas voam pros confins de novo)",
				  _cicloDasSupers == cicloAntesDoTiro + 1
				  && QuantasSupersTem(pl.Assinatura) == 0
				  && QuantasSupersTem(rival.Assinatura) == 0,
				  $"ciclo {cicloAntesDoTiro} -> {_cicloDasSupers}");

			Checa("...e a PROCURACAO cai junto (o `pspace_sdb_scatter` zera o beneficiario)",
				  !HaProcuracao && _pedidoDoBenef.Length == 0);
		}
		catch (Exception ex)
		{
			// UMA EXCECAO E UMA FALHA, E NAO UM FIM. Esta bancada roda dentro do tratador de pacote da
			// rede, e ele ENGOLE excecao num aviso -- sem este bloco ela pararia no meio e imprimiria
			// "N OK, 0 FALHA", que e o pior jeito de um teste falhar. Ja aconteceu nesta fase.
			falhou++;
			GD.PrintErr($"  FALHA (EXCECAO -- a bancada parou aqui): {ex}");
		}
		finally
		{
			// ============================ O TESTADOR VOLTA PRO MUNDO ANTES DE TUDO ============================
			// A secao 11 TIRA o testador de `_players` de proposito (a rota 3 do inimigo e "espere ele
			// deslogar"). Se uma excecao acontecesse entre a saida e a volta, o `finally` restauraria o
			// mundo inteiro **em volta de um jogador que nao esta mais nele** -- e o `MoveToZone` da linha
			// de baixo nao acharia o id. O dono da tela ficaria fantasma ate reiniciar o servidor.
			// ============================================================================================
			if (!_players.ContainsKey(pl.Id))
			{
				_players[pl.Id] = pl;
				if (!ZoneList(pl.Zone.Hash).Contains(pl)) ZoneList(pl.Zone.Hash).Add(pl);
			}

			foreach (ServerPlayer f in forjados)
			{
				_players.Remove(f.Id);
				ZoneList(f.Zone.Hash).Remove(f);
			}

			_sets.Clear();
			_sets.AddRange(setsGuardados);
			_esferas.Clear();
			_esferas.AddRange(esferasGuardadas);
			_invocacoes.Clear();

			foreach ((int n, string dono, string nome) in supersGuardadas)
				if (_supers.Find(s => s.Numero == n) is { } s) { s.Dono = dono; s.DonoNome = nome; }
			_cicloDasSupers = cicloGuardado;
			(_benefSig, _benefNome, _pedidoDoBenef, _alvoDoPedido) = benefGuardado;
			_disputasDeSuper.Clear();
			_ofertasDeGuarda.Clear();
			_precoPendente = default;

			_adiantoDoCeu = ceuGuardado;

			pl.Race = racaGuardada;
			pl.Class = classeGuardada;
			pl.Ficha.BP = bpGuardado;
			pl.Ficha.Zeni = zeniGuardado;
			pl.Ficha.godtongue = linguaGuardada;
			pl.Ficha.Statify();

			int radarAgora = pl.Mochila.Quantos(Jandirus.Core.Items.CatalogoDeItens.Radar);
			if (radarAgora > radarGuardado)
				pl.Mochila.Tirar(Jandirus.Core.Items.CatalogoDeItens.Radar, radarAgora - radarGuardado);
			else if (radarAgora < radarGuardado)
				pl.Mochila.Guardar(Jandirus.Core.Items.CatalogoDeItens.Radar, radarGuardado - radarAgora);

			MoveToZone(pl.Id, zonaGuardada, posGuardada);

			_tronos.Clear();
			foreach ((string k, string v) in tronosGuardados) _tronos[k] = v;

			SalvarEsferas();
			SalvarSupers();

			// A BANCADA CHAMOU `Persistir` (a lingua, o zeni): o disco levou numeros de mentira.
			// Devolver so o campo em memoria deixaria o save com eles.
			Persistir(pl);

			EscutaDasEsferas = null;
			EscutaDasSupers = null;
			EscutaDosDesejos = null;
			EscutaDeAvisos = escutaDeAvisosAntes;
		}

		GD.Print($"===== BANCADA DO AVESSO: {ok} OK, {falhou} FALHA =====\n");
	}

	/// <summary>
	/// UM PONTO DO MAPA LONGE DE TODAS AS SETE -- o lugar de onde o verbo de pegar TEM que recusar.
	///
	/// Ele e procurado e nao inventado: as esferas caem onde a semente manda, e um ponto fixo podia
	/// calhar de nascer ao lado de uma delas. Uma bancada que so passa "quando da sorte" e pior que
	/// bancada nenhuma, porque ela vira ruido e alguem a desliga.
	/// </summary>
	private static Vec2 PontoLongeDasEsferas(ZoneCollision? mapa, List<Esfera> sete)
	{
		const int t = ZoneCollision.TileSize;
		int w = mapa?.Width ?? 256, h = mapa?.Height ?? 256;
		float minimo = 6 * t;

		Vec2 melhor = new(w * t / 2f, h * t / 2f);
		double melhorDistancia = -1;

		for (int cy = 8; cy < h - 8; cy += 6)
			for (int cx = 8; cx < w - 8; cx += 6)
			{
				var p = new Vec2(cx * t + t / 2f, cy * t + t / 2f);
				if (mapa != null && (mapa.PontoLivrePerto(p) - p).Length > t) continue;

				double perto = sete.Min(e => new Vec2(e.X - p.X, e.Y - p.Y).Length);
				if (perto <= melhorDistancia) continue;

				melhor = p;
				melhorDistancia = perto;
				if (perto > minimo * 4) return melhor;   // longe o bastante: nao ha porque varrer o resto
			}

		return melhor;
	}
}
