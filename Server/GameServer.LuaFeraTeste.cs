using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ A LUA CHEIA PEGA OS NPCs SAIYAJINS ============================
/// O pedido do dono, literal: *"npcs SAIYAJINS q estao LUTANDO e estao comecando a sofrer FERIMENTOS
/// GRAVES, se tiver LUA CHEIA eles vao OLHAR PRA LUA e se transformar em OOZARU assim como um
/// jogador. eles tem MAESTRIA ALEATORIA ao serem criados entao eles podem CONTROLAR OU NAO direito o
/// oozaru"*.
///
///     Godot --headless --path . --host --rede 7954 --luaferateste
///                      --conta bancada_luafera --nome QuemViraMacaco
///
/// ============================ POR QUE ESTA BANCADA PRECISA NASCER O SAIYAJIN A MAO ============================
/// **Do jeito que o mundo esta hoje, ligar este sistema muda o comportamento de ZERO corpos**, e isso
/// foi medido (`--povoteste`, 119 nascimentos): Saiyajin so tem berco em **Vegeta**, e Vegeta esta
/// destruida (*"'Vegeta' esta condenado -- ninguem nasce la"*); o `soldado_saiyajin` esta desligado
/// pelo interruptor do dono (*"inimigo comum DESLIGADO"*); e o unico Saiyajin do mundo e **um**
/// chefe (`guardiao_saiyajin`) que nasce em **Hell**, planeta sem lua.
///
/// Uma bancada que esperasse o povoamento produzir o corpo ficaria **verde por ausencia** -- ela nao
/// mediria nada e diria OK. Entao ela nasce os Saiyajins pelo caminho de PRODUCAO (`NascerNpc`), na
/// Terra, e adianta o ceu pela mesma manivela do `--luateste`.
/// ==========================================================================================================
///
/// ============================ AS NOVE FAMILIAS ============================
///   1. **O DEGRAU DE "GRAVE"** -> reprova se ele virar numero solto, ou se pender do HEMATOMA (que
///      enche em qualquer briga de rua) em vez do SANGUE.
///   2. **A MAESTRIA SORTEADA** -> reprova se todo NPC tiver a mesma, se ela nao for deterministica na
///      semente, ou se ela for escrita em quem nunca vai virar macaco (dado morto).
///   3. **OS QUATRO PORTOES DO DONO**, um por vez, com controle -> reprova se qualquer um deles for
///      decorativo.
///   4. **O RABO** -> o NPC cumpre os quatro requisitos, perdeu o rabo na briga, e **NAO vira**. Isso e
///      o CERTO, e sem caso proprio "nao virou" seria lido como defeito.
///   5. **A PLATEIA** (decisao 4) -> planeta vazio nao transforma; jogador pousa e o mesmo corpo vira.
///   6. **A MAESTRIA DECIDE O CONTROLE** -> maestria 0 perde as redeas, maestria 100 nunca perde.
///   7. **A VOLTA, E A ESTATUA** -> a familia mais importante do arquivo. `DevolverAsRedeas` fazia
///      `Cerebro = null` incondicionalmente: um NPC que saisse do Oozaru virava **estatua permanente**.
///   8. **NAO REGRIDE** -> o Oozaru do JOGADOR e a furia lendaria escrevem no mesmo lugar.
///   9. **O FIO** -> `TickDoCeu()` de verdade, e nao a funcao chamada a mao: reprova se o gatilho
///      existir e nao estiver ligado em lugar nenhum.
/// ==========================================================================
///
/// Ela nasce corpos, fere corpos, adianta o relogio do mundo e mexe na maestria de quem forjou. Tudo o
/// que toca e fotografado no comeco e devolvido no `finally`. So com a flag, e em conta e porta proprias.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// O TIPO DO `Checa` DESTA BANCADA -- um delegate proprio e nao um `Action&lt;string, bool, string&gt;`.
	///
	/// A razao e uma so e ela e do compilador: `Action` nao carrega valor PADRAO de parametro, entao com
	/// ele cada uma das ~50 checagens teria que escrever `, ""` no fim so pra existir. Um delegate
	/// declarado carrega o padrao, e as familias que dividem este `Checa` continuam lendo como as das
	/// outras bancadas do projeto (que o mantem como funcao local justamente por isso).
	/// </summary>
	private delegate void Verificacao(string nome, bool cond, string detalhe = "");

	private bool _luaFeraDeTeste;

	/// <summary>Faixa de lugares propria -- longe da dos habitantes (1..N), das sagas (5 M) e da de gente (8,1 M).</summary>
	private ulong _lugarDaBancadaDaLuaFera = 8_400_000;

	private void RodarBancadaDaLuaDaFera(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA: A LUA CHEIA PEGA OS NPCs SAIYAJINS =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		double ceuGuardado = _adiantoDoCeu;
		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		int agressorDoHostNoComeco = pl.UltimoAgressor;
		var forjados = new List<ServerPlayer>();

		try
		{
			var palco = ZoneKey.Premade("Earth");
			var longe = ZoneKey.Premade("Lookout");   // fora do plano de povoamento: la o host nao e plateia de ninguem
			ZoneCollision? mapa = MapaDaZonaOuCatalogo(palco);

			if (mapa == null || _moldes == null || _racas == null)
			{
				Checa("PRECONDICAO: a Earth tem mapa e os moldes/racas carregaram", false);
				return;
			}

			MedirODegrauGrave(Checa);

			// O CEU VAI PRA LUA CHEIA ANTES DE QUALQUER CORPO NASCER: as familias 3 a 9 medem o
			// GATILHO, e um gatilho medido debaixo de lua nova mede a ausencia dele.
			AjustarCeuDaTerra(hora: 0.90, fase: Ceu.Cheia);
			EstadoDoCeu ceuDaTerra = Ceu.De(RelogioDaZona(palco), TempoDoMundo);
			Checa("PRECONDICAO: a Terra esta com LUA CHEIA no ceu (a manivela do `--luateste`)",
				  ceuDaTerra.Cheia && ceuDaTerra.LuaNoCeu,
				  $"fase {ceuDaTerra.Fase}, altura {ceuDaTerra.Altura:0.00}, {(ceuDaTerra.Noite ? "noite" : "dia")}");

			MedirAMaestriaSorteada(Checa, palco, mapa, forjados);

			// O HOST E A PLATEIA das familias 3, 4, 6, 7, 8 e 9. Ele so sai da zona na familia 5, que e
			// justamente a que mede o contrario.
			MoveToZone(pl.Id, palco, PontoDeNascimento(palco));
			TickDosCorposSemDono(Protocol.TickSeconds);   // e quem escreve o `_zonasComGente`
			Checa("PRECONDICAO: com o host na Earth, a zona conta como habitada",
				  _zonasComGente.Contains(palco.Hash));

			MedirOsQuatroPortoes(Checa, palco, mapa, forjados);
			MedirORabo(Checa, palco, mapa, forjados);
			MedirAPlateia(Checa, pl, palco, longe, mapa, forjados);
			MedirOControle(Checa, palco, mapa, forjados);
			MedirAVolta(Checa, pl, palco, mapa, forjados);
			MedirQueNaoRegrediu(Checa, pl, palco, mapa, forjados);
			MedirOFio(Checa, palco, mapa, forjados);
			MedirOCusto(Checa, palco, mapa, forjados);
			MedirAFuriaNoNpc(Checa, palco, mapa, forjados);
			MedirORamoDaFera(Checa, pl, palco, mapa, forjados);
			MedirAVistaDaFera(Checa, palco, mapa, forjados);

			// ============================ A PLATEIA NAO PODE TER APANHADO ============================
			// O host e a plateia das familias 3 a 12, e NENHUMA delas bate nele: toda briga daqui e entre corpos da bancada.
			// Terminar com agressor quer dizer que um corpo sobreviveu a propria familia e o adotou como presa -- os cidadaos de
			// Namek das familias 3 e 10 fizeram isso ate 24/09 (ver o `RemoverNpc` no fim de cada uma), e a rodada seguia verde
			// com o host nocauteado e com Zenkai no BP: um mundo que familia nenhuma pediu. O `UltimoAgressor`
			// fica escrito depois que o nocaute passa (so outro golpe o troca), entao a pergunta vale pro FIM, e nao pra um
			// instante.
			// ================================================================================
			string agressorDoHost = pl.UltimoAgressor == agressorDoHostNoComeco ? "ninguem"
				: forjados.Find(f => f.Id == pl.UltimoAgressor)?.Name ?? $"id {pl.UltimoAgressor}";
			Checa("(higiene) o host, a plateia, chegou ao fim sem apanhar de NINGUEM da bancada",
				  pl.UltimoAgressor == agressorDoHostNoComeco && !pl.Ficha.KO,
				  $"agressor: {agressorDoHost}, KO agora: {(pl.Ficha.KO ? "sim" : "nao")}");

			Checa("a bancada chegou ao fim (ver o `catch`: sem ele, abortar no meio reportava '0 falhas')",
				  true);
		}
		catch (Exception ex) { Checa("a bancada rodou ate o fim sem excecao", false, ex.ToString()); }
		finally
		{
			foreach (ServerPlayer c in forjados)
				if (_players.ContainsKey(c.Id)) RemoverNpc(c);

			_adiantoDoCeu = ceuGuardado;
			if (pl.Zone.Hash != zonaGuardada.Hash) MoveToZone(pl.Id, zonaGuardada, posGuardada);
			pl.Pos = posGuardada;

			// O host pode ter sido usado como cobaia da familia 8: as redeas voltam pra mao dele.
			if (pl.CerebroDaPosse != null) DevolverAsRedeas(pl);
			if (pl.Oozaru != FormaOozaru.Nao) DesfazerOozaru(pl, "a bancada terminou.");
		}

		GD.Print($"===== FIM: {ok} OK, {falhou} FALHA(S) =====\n");
	}

	// =====================================================================
	// FAMILIA 1 -- O DEGRAU DE "GRAVE" (pura: nao precisa de corpo nenhum)
	// =====================================================================
	/// <summary>
	/// O LIMIAR DE "FERIMENTO GRAVE" NAO PODE SER UM NUMERO SOLTO -- foi o que o dono pediu.
	///
	/// O QUE ESTA SENDO MEDIDO, e cada linha reprova um jeito diferente de errar:
	///   * o degrau e a CONTA do limiar de quebra do jogo passado pela curva do sangue (nao um 11
	///     digitado);
	///   * ele pende do SANGUE e nao do HEMATOMA -- um corpo com hematoma CHEIO (50% de dano) nao e
	///     grave, e essa e a linha que reprova a versao facil da regra, porque metade de qualquer
	///     sessao de luta anda com o roxo cheio;
	///   * membro arrancado e grave mesmo com as camadas limpas (o caso do Namekuseijin que regenerou
	///     a vida do coto mas continua sem o braco).
	/// </summary>
	private static void MedirODegrauGrave(Verificacao checa)
	{
		GD.Print("--- 1. o degrau em que a ferida vira GRAVE ---");

		int esperado = (int)Math.Round(
			Math.Clamp((1.0 - Regras.LimiarQuebra - 0.55) / (0.90 - 0.55), 0, 1) * MascaraDeFeridas.Degraus);

		checa("o degrau de GRAVE e derivado do limiar de quebra do jogo (nao um numero digitado)",
			  Feridas.DegrauGrave == esperado, $"{Feridas.DegrauGrave} != {esperado}");
		checa("...e ele cai dentro da escala de 4 bits da mascara",
			  Feridas.DegrauGrave is > 0 and <= MascaraDeFeridas.Degraus, $"{Feridas.DegrauGrave}");

		checa("corpo limpo NAO e ferimento grave",
			  !Feridas.Grave(new MascaraDeFeridas(0, 0, 0, 0, 0)));

		// HEMATOMA CHEIO EM TODAS AS CINCO ZONAS: o nibble ALTO no maximo, o baixo zerado.
		checa("hematoma CHEIO nas cinco zonas NAO e grave (senao qualquer briga de rua dispararia)",
			  !Feridas.Grave(new MascaraDeFeridas(0xF0, 0xF0, 0xF0, 0xF0, 0xF0)));

		byte umAbaixo = (byte)(Feridas.DegrauGrave - 1);
		checa("sangue UM degrau abaixo do limiar ainda nao e grave",
			  !Feridas.Grave(new MascaraDeFeridas(0, 0, 0, 0, umAbaixo)),
			  $"degrau {umAbaixo}");

		byte noPonto = (byte)Feridas.DegrauGrave;
		checa("sangue NO degrau do limiar ja e grave (uma zona basta -- a media apagaria isto)",
			  Feridas.Grave(new MascaraDeFeridas(0, 0, 0, 0, noPonto)),
			  $"degrau {noPonto}");

		checa("membro ARRANCADO e grave mesmo com as camadas limpas (o coto que regenerou a vida)",
			  Feridas.Grave(new MascaraDeFeridas(0, 0, 0, 0, 0,
				  (byte)MascaraDeFeridas.Membro.BracoDir)));
	}

	// =====================================================================
	// FAMILIA 2 -- A MAESTRIA DA FERA E SORTEADA NO NASCIMENTO
	// =====================================================================
	/// <summary>
	/// *"eles tem MAESTRIA ALEATORIA ao serem criados"*.
	///
	/// O DEFEITO QUE ELA REPROVA E O ESTADO ANTERIOR: `Maestria.De("oozaru")` valia **0 pra todo NPC
	/// do jogo**, porque o Oozaru nao e degrau da escada e o `AbrirFormas` so escreve maestria em
	/// degrau que ele abre. Zero pra todo mundo passa em qualquer teste que so olhe "o campo existe" --
	/// o que separa e a VARIACAO entre corpos e o determinismo na semente.
	/// </summary>
	private void MedirAMaestriaSorteada(Verificacao checa, ZoneKey palco,
										ZoneCollision mapa, List<ServerPlayer> forjados)
	{
		GD.Print("--- 2. a maestria da fera, sorteada por semente ---");

		Vec2 berco = PontoDeHabitante(palco, ++_lugarDaBancadaDaLuaFera);
		var dominios = new List<double>();
		ulong primeiroLugar = 0;
		double primeiroDominio = -1;

		for (int i = 0; i < 12; i++)
		{
			ulong lugar = ++_lugarDaBancadaDaLuaFera;
			ServerPlayer? s = NascerNpc("guardiao_saiyajin", palco,
				mapa.PontoLivrePerto(berco + new Vec2(i * 48, 0)), lugar);
			if (s == null) continue;
			forjados.Add(s);
			dominios.Add(s.Forma.Maestria.De(Oozaru.IdRegular));
			if (primeiroDominio < 0) { primeiroDominio = dominios[0]; primeiroLugar = lugar; }
		}

		checa("PRECONDICAO: nasceram 12 Saiyajins pelo caminho de producao", dominios.Count == 12,
			  $"{dominios.Count}");
		if (dominios.Count < 2) return;

		checa("todo Saiyajin nasce com maestria de fera dentro de 0..100",
			  dominios.TrueForAll(d => d is >= 0 and <= 100),
			  string.Join(", ", dominios.Select(d => d.ToString("0"))));

		checa("...e ela VARIA entre corpos (o estado anterior era 0 pra todos)",
			  dominios.Distinct().Count() > 1,
			  string.Join(", ", dominios.Select(d => d.ToString("0"))));

		// DETERMINISMO: o mesmo (molde, zona, lugar) tem que devolver o mesmo dominio. E o que
		// separa "sorteado por semente" de "sorteado por `Random`" -- o segundo passa na linha de
		// cima e reprova aqui.
		ServerPlayer? gemeo = NascerNpc("guardiao_saiyajin", palco,
			mapa.PontoLivrePerto(berco + new Vec2(0, 96)), primeiroLugar);
		if (gemeo != null)
		{
			forjados.Add(gemeo);
			checa("o MESMO lugar renasce o MESMO dominio (semente, e nao `Random` global)",
				  Math.Abs(gemeo.Forma.Maestria.De(Oozaru.IdRegular) - primeiroDominio) < 1e-9,
				  $"{gemeo.Forma.Maestria.De(Oozaru.IdRegular)} != {primeiroDominio}");
		}

		// QUEM NUNCA VAI VIRAR MACACO NAO GANHA O DADO. Um cidadao da Terra nasce Humano/Namekuseijin
		// pelo berco do planeta -- e escrever maestria de fera nele seria dado morto num livro que a
		// aba Formas le.
		ServerPlayer? comum = NascerNpc("cidadao", palco,
			mapa.PontoLivrePerto(berco + new Vec2(0, 160)), ++_lugarDaBancadaDaLuaFera);
		if (comum != null)
		{
			forjados.Add(comum);
			bool ehSaiyajin = comum.Race is "Saiyan" or "Halfbreed";
			checa("quem NAO nasce com rabo nao ganha maestria de fera (nada de dado morto)",
				  ehSaiyajin || comum.Forma.Maestria.De(Oozaru.IdRegular) == 0,
				  $"{comum.Race} com {comum.Forma.Maestria.De(Oozaru.IdRegular)}");
		}

		checa("o OOZARU DOURADO fica em zero (ele tem portas proprias: Primal + SSJ1 dominado + BP)",
			  forjados.TrueForAll(f => f.Forma.Maestria.De(Oozaru.IdDourado) == 0));
	}

	// =====================================================================
	// FAMILIA 3 -- OS QUATRO PORTOES DO DONO
	// =====================================================================
	private void MedirOsQuatroPortoes(Verificacao checa, ZoneKey palco,
									  ZoneCollision mapa, List<ServerPlayer> forjados)
	{
		GD.Print("--- 3. os quatro portoes: saiyajin, lutando, ferido grave, lua cheia ---");

		ServerPlayer? a = ForjarSaiyajin(palco, mapa, forjados);
		ServerPlayer? b = ForjarSaiyajin(palco, mapa, forjados);
		if (a == null || b == null) { checa("PRECONDICAO: dois Saiyajins na Terra", false, ""); return; }

		// ---- O CONTROLE: tudo satisfeito -> ele vira ----
		PorEmBrigaFeia(a, b);
		checa("PRECONDICAO: o Saiyajin esta de pe (ferir o braco nao pode nocautear)",
			  !a.Ficha.KO && !a.Ficha.dead);
		checa("PRECONDICAO: a mascara dele diz FERIMENTO GRAVE", Feridas.Grave(a.EnvFeridas),
			  a.EnvFeridas.ToString());

		ALuaPegaOSaiyajin(a, TempoDoMundo);
		checa("CONTROLE: saiyajin + lutando + ferido grave + lua cheia -> ele VIRA OOZARU",
			  a.Oozaru != FormaOozaru.Nao, a.Oozaru.ToString());
		checa("...e o macaco que sai e o REGULAR (o Dourado exige Primal, e ele nao e)",
			  a.Oozaru == FormaOozaru.Regular, a.Oozaru.ToString());

		// ---- PORTAO "ESTA LUTANDO": rancor frio ----
		ServerPlayer? c = ForjarSaiyajin(palco, mapa, forjados);
		if (c != null)
		{
			PorEmBrigaFeia(c, b);
			c.RancorAte = 0;   // a tag de combate esfriou: a briga acabou ha mais de 90 s
			ALuaPegaOSaiyajin(c, TempoDoMundo);
			checa("PORTAO 'esta lutando': com a TAG DE COMBATE fria, ele nao vira",
				  c.Oozaru == FormaOozaru.Nao, c.Oozaru.ToString());

			// e ela volta a valer pelo funil de agressao de verdade
			MarcarAgressao(c, b);
			ALuaPegaOSaiyajin(c, TempoDoMundo);
			checa("...e volta a valer quando alguem bate nele de novo (`MarcarAgressao`, o funil unico)",
				  c.Oozaru != FormaOozaru.Nao, c.Oozaru.ToString());
		}

		// ---- PORTAO "FERIMENTO GRAVE": corpo inteiro ----
		ServerPlayer? d = ForjarSaiyajin(palco, mapa, forjados);
		if (d != null)
		{
			MarcarAgressao(d, b);          // lutando...
			TickDasFeridas();              // ...mas inteiro
			checa("PRECONDICAO: o corpo inteiro nao tem ferimento grave", !Feridas.Grave(d.EnvFeridas),
				  d.EnvFeridas.ToString());
			ALuaPegaOSaiyajin(d, TempoDoMundo);
			checa("PORTAO 'ferimento grave': lutando e INTEIRO, ele nao vira",
				  d.Oozaru == FormaOozaru.Nao, d.Oozaru.ToString());

			// e passa a valer quando o estrago chega -- pelo funil de dano de verdade
			FerirAteSangrar(d);
			TickDasFeridas();
			ALuaPegaOSaiyajin(d, TempoDoMundo);
			checa("...e passa a valer no golpe que abre o corpo dele (`Corpo.Ferir` -> mascara -> grave)",
				  d.Oozaru != FormaOozaru.Nao, d.Oozaru.ToString());
		}

		// ---- PORTAO "LUA CHEIA": a mesma pergunta que o jogador faz ----
		ServerPlayer? e = ForjarSaiyajin(palco, mapa, forjados);
		if (e != null)
		{
			PorEmBrigaFeia(e, b);
			double guardado = _adiantoDoCeu;
			AjustarCeuDaTerra(hora: 0.90, fase: (int)FaseDaLua.QuartoCrescente);
			EstadoDoCeu outra = Ceu.De(RelogioDaZona(palco), TempoDoMundo);
			checa("PRECONDICAO: o ceu saiu da cheia", !outra.Cheia, $"fase {outra.Fase}");

			ALuaPegaOSaiyajin(e, TempoDoMundo);
			checa("PORTAO 'lua cheia': com a lua em quarto crescente, ele nao vira",
				  e.Oozaru == FormaOozaru.Nao, e.Oozaru.ToString());

			_adiantoDoCeu = guardado;
			ALuaPegaOSaiyajin(e, TempoDoMundo);
			checa("...e vira no instante em que a cheia volta ao ceu (funcao pura do tempo)",
				  e.Oozaru != FormaOozaru.Nao, e.Oozaru.ToString());
		}

		// ---- PORTAO "E SAIYAJIN": um Namekuseijin com tudo o resto ----
		ServerPlayer? alien = NascerNpc("cidadao", ZoneKey.Premade("Namek"),
			PontoDeNascimento(ZoneKey.Premade("Namek")), ++_lugarDaBancadaDaLuaFera);
		if (alien != null)
		{
			forjados.Add(alien);
			MoveToZone(alien.Id, palco, PontoDeNascimento(palco));
			PorEmBrigaFeia(alien, b);
			ALuaPegaOSaiyajin(alien, TempoDoMundo);
			checa("PORTAO 'e saiyajin': quem nao e de sangue Saiyajin nao vira, com lua ou sem",
				  alien.Oozaru == FormaOozaru.Nao, $"{alien.Race}: {alien.Oozaru}");

			// ============================ E ELE SAI DO MUNDO AQUI MESMO ============================
			// Ele so existe pra esta pergunta de funcao pura -- nenhum tique roda com ele de pe --, e o ponto dele e o
			// `PontoDeNascimento` da Terra, que e onde o HOST esta (o `MoveToZone` do topo da bancada). Vivo, ele era um
			// cidadao com RANCOR e sem agressor que conte: a briga marcada e contra um NPC, e rancor contra NPC nao vale (o
			// `provoke()` do DM exige `atk.client`). Entao a busca alargada do `PresaDoNpc` (12 tiles) achava o host colado
			// nele, e uma das familias que rodam tique (a 7 numa rodada, a 12 em outra) o via NOCAUTEAR a plateia -- que ainda
			// ganhava Zenkai. Nenhuma linha reprovava; a guarda do fim da bancada (`(higiene)`) e quem passa a ver. A familia 10
			// tinha o mesmo corpo.
			// ================================================================================
			RemoverNpc(alien);
		}
	}

	// =====================================================================
	// FAMILIA 4 -- O RABO E PORTAO LEGITIMO
	// =====================================================================
	/// <summary>
	/// O NPC pode cumprir os QUATRO requisitos do dono e ainda assim nao virar, porque perdeu o rabo na
	/// briga (`ArrancarRabo`) -- e **isso e o certo**. Sem caso proprio, "nao virou" seria lido como
	/// defeito por quem estivesse depurando; com ele, o comportamento tem nome.
	/// </summary>
	private void MedirORabo(Verificacao checa, ZoneKey palco,
							ZoneCollision mapa, List<ServerPlayer> forjados)
	{
		GD.Print("--- 4. o rabo: o portao que nao esta na lista do dono ---");

		ServerPlayer? s = ForjarSaiyajin(palco, mapa, forjados);
		ServerPlayer? agressor = ForjarSaiyajin(palco, mapa, forjados);
		if (s == null || agressor == null) { checa("PRECONDICAO: dois Saiyajins", false, ""); return; }

		BodyPart? rabo = s.Combate!.Corpo.Achar("Rabo");
		checa("PRECONDICAO: o NPC Saiyajin NASCE COM RABO (`CombatState(ficha, TemRabo(raca), ...)`)",
			  rabo is { Decepado: false }, rabo == null ? "sem membro 'Rabo'" : "ja decepado");
		if (rabo == null) return;

		PorEmBrigaFeia(s, agressor);
		rabo.Decepado = true;
		rabo.Vida = 0;
		s.Combate.SincronizarVida();

		ALuaPegaOSaiyajin(s, TempoDoMundo);
		checa("com os QUATRO requisitos cumpridos e o rabo ARRANCADO, ele NAO vira -- e e o certo",
			  s.Oozaru == FormaOozaru.Nao, s.Oozaru.ToString());

		// E o rabo tambem DERRUBA no meio da forma -- e o `if(!container.Tail) DeBuff()` do DM.
		rabo.Decepado = false;
		rabo.Vida = rabo.VidaMax;
		s.Combate.SincronizarVida();
		ALuaPegaOSaiyajin(s, TempoDoMundo);
		checa("...e com o rabo de volta ele vira (a recusa era o rabo, e nao outro portao)",
			  s.Oozaru != FormaOozaru.Nao, s.Oozaru.ToString());

		rabo.Decepado = true;
		TickDoOozaru(s, Protocol.TickSeconds);
		checa("arrancar o rabo NO MEIO da forma derruba a fera (`Oozaru.dm:153`)",
			  s.Oozaru == FormaOozaru.Nao, s.Oozaru.ToString());
		checa("...e o NPC nao fica preso sem cerebro depois disso",
			  s.Cerebro != null && s.CerebroDaPosse == null);
	}

	// =====================================================================
	// FAMILIA 5 -- A PLATEIA (a decisao 4)
	// =====================================================================
	/// <summary>
	/// **DECISAO: planeta sem jogador NAO transforma.** Ver o porque escrito em
	/// <see cref="ALuaPegaOSaiyajin"/>, guarda 6 -- em resumo: sem plateia a mente esta congelada, entao
	/// a briga que as guardas 4 e 5 estao lendo ja parou; e o Oozaru custaria 30 Hz de relogio de forma
	/// pra ninguem ver.
	///
	/// A medida e um par: o MESMO corpo, nas MESMAS condicoes, com e sem o host na zona.
	/// </summary>
	private void MedirAPlateia(Verificacao checa, ServerPlayer pl, ZoneKey palco,
							   ZoneKey longe, ZoneCollision mapa, List<ServerPlayer> forjados)
	{
		GD.Print("--- 5. zona sem jogador: a decisao 4 ---");

		ServerPlayer? s = ForjarSaiyajin(palco, mapa, forjados);
		ServerPlayer? agressor = ForjarSaiyajin(palco, mapa, forjados);
		if (s == null || agressor == null) { checa("PRECONDICAO: dois Saiyajins", false, ""); return; }
		PorEmBrigaFeia(s, agressor);

		MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
		TickDosCorposSemDono(Protocol.TickSeconds);
		checa("PRECONDICAO: sem o host, a Earth nao conta como habitada",
			  !_zonasComGente.Contains(palco.Hash));

		ALuaPegaOSaiyajin(s, TempoDoMundo);
		checa("planeta SEM jogador: o Saiyajin ferido nao vira macaco (espetaculo que ninguem ve)",
			  s.Oozaru == FormaOozaru.Nao, s.Oozaru.ToString());

		MoveToZone(pl.Id, palco, PontoDeNascimento(palco));
		TickDosCorposSemDono(Protocol.TickSeconds);
		ALuaPegaOSaiyajin(s, TempoDoMundo);
		checa("CONTROLE: o host pousa e o MESMO corpo vira no segundo seguinte (a lua dura a noite)",
			  s.Oozaru != FormaOozaru.Nao, s.Oozaru.ToString());

		// E O QUE JA E FERA NAO CAI POR FALTA DE PLATEIA: o prazo continua correndo no relogio do
		// mundo, entao um NPC nao fica preso em macaco num planeta que esvaziou.
		MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
		TickDosCorposSemDono(Protocol.TickSeconds);
		s.OozaruAte = NowMs() - 1;
		TickDoOozaru(s, Protocol.TickSeconds);
		checa("...e o que JA e fera cai sozinho mesmo sem plateia (o prazo e relogio de MUNDO)",
			  s.Oozaru == FormaOozaru.Nao, s.Oozaru.ToString());

		MoveToZone(pl.Id, palco, PontoDeNascimento(palco));
		TickDosCorposSemDono(Protocol.TickSeconds);
	}

	// =====================================================================
	// FAMILIA 6 -- A MAESTRIA DECIDE O CONTROLE
	// =====================================================================
	private void MedirOControle(Verificacao checa, ZoneKey palco,
								ZoneCollision mapa, List<ServerPlayer> forjados)
	{
		GD.Print("--- 6. a maestria sorteada decide quem dirige a fera ---");

		ServerPlayer? cru = ForjarSaiyajin(palco, mapa, forjados);
		ServerPlayer? mestre = ForjarSaiyajin(palco, mapa, forjados);
		ServerPlayer? agressor = ForjarSaiyajin(palco, mapa, forjados);
		if (cru == null || mestre == null || agressor == null)
		{ checa("PRECONDICAO: tres Saiyajins", false, ""); return; }

		cru.Forma.Maestria.Por(Oozaru.IdRegular, 0);
		mestre.Forma.Maestria.Por(Oozaru.IdRegular, 100);

		PorEmBrigaFeia(cru, agressor);
		PorEmBrigaFeia(mestre, agressor);
		ALuaPegaOSaiyajin(cru, TempoDoMundo);
		ALuaPegaOSaiyajin(mestre, TempoDoMundo);
		if (cru.Oozaru == FormaOozaru.Nao || mestre.Oozaru == FormaOozaru.Nao)
		{ checa("PRECONDICAO: os dois viraram macaco", false, ""); return; }

		// O CEREBRO DE NASCIMENTO E A REFERENCIA: e ele que tem que continuar dirigindo o mestre, e e
		// ele que a fera tem que substituir no cru.
		object mentePropriaDoCru = cru.Cerebro!;
		object mentePropriaDoMestre = mestre.Cerebro!;

		checa("no primeiro quadro nenhum dos dois esta possuido (ha um piso de graca pra todo mundo)",
			  cru.CerebroDaPosse == null && mestre.CerebroDaPosse == null);

		// EMPURRA O RELOGIO DA FORMA: `SegundosNaForma` e derivado do prazo de fim, entao recuar o
		// `OozaruAte` e envelhecer a transformacao sem esperar de verdade.
		double passou = Oozaru.SegundosDeGraca + 1;
		cru.OozaruAte = NowMs() + (long)((Oozaru.Duracao(cru.Oozaru) - passou) * 1000);
		mestre.OozaruAte = NowMs() + (long)((Oozaru.Duracao(mestre.Oozaru) - passou) * 1000);
		TickDoOozaru(cru, Protocol.TickSeconds);
		TickDoOozaru(mestre, Protocol.TickSeconds);

		checa("MAESTRIA 0: passado o piso de graca, a FERA toma as redeas do NPC",
			  cru.CerebroDaPosse != null, "");
		checa("...e o cerebro que dirige passou a ser o DA FERA, e nao a mente dele",
			  !ReferenceEquals(cru.Cerebro, mentePropriaDoCru)
			  && cru.Cerebro is { Inteligencia: 0, Disciplina: 0, VidaCautelosa: 0 },
			  cru.Cerebro == null ? "sem cerebro" : $"int {cru.Cerebro.Inteligencia}");

		checa("MAESTRIA 100: a fera e DELE -- ele nunca perde as redeas",
			  mestre.CerebroDaPosse == null, "");
		checa("...e quem continua dirigindo e a mente com que ele nasceu",
			  ReferenceEquals(mestre.Cerebro, mentePropriaDoMestre));

		// E O PRAZO E QUADRATICO, nao um sim/nao: metade da maestria compra um quarto da forma.
		checa("a curva e a MESMA do jogador (`duracao * f²`): 50% de maestria = 25% da forma",
			  Math.Abs(Oozaru.SegundosDeControle(50, FormaOozaru.Regular)
					   - Oozaru.Duracao(FormaOozaru.Regular) * 0.25) < 0.01,
			  $"{Oozaru.SegundosDeControle(50, FormaOozaru.Regular):0.0}s");
	}

	// =====================================================================
	// FAMILIA 7 -- A VOLTA, E A ESTATUA PERMANENTE
	// =====================================================================
	/// <summary>
	/// ============================ A FAMILIA MAIS IMPORTANTE DESTE ARQUIVO ============================
	/// `DesfazerOozaru` chama `DevolverAsRedeas` incondicionalmente, e ele fazia **`pl.Cerebro = null`**
	/// pra qualquer corpo com `DonoDoClone == 0`. O guard de la protegia clone -- nao NPC de povoamento.
	///
	/// Consequencia: **todo NPC que saisse do Oozaru viraria estatua permanente**, e nada em jogo diria
	/// por que. O `TickDosCorposSemDono` filtra por `Cerebro != null`; sem cerebro o corpo nao decide,
	/// nao anda, nao ataca e nao morre de causa nenhuma -- ele so fica de pe no meio do planeta pra
	/// sempre.
	///
	/// Esta familia injeta o caminho inteiro (virar -> perder o controle -> a forma acabar) e afirma as
	/// tres coisas que o conserto tem que entregar: o corpo VOLTA a ter mente, a mente e a DELE (nao a
	/// da fera), e ele volta a decidir de verdade.
	/// ==========================================================================================================
	/// </summary>
	private void MedirAVolta(Verificacao checa, ServerPlayer pl, ZoneKey palco,
							 ZoneCollision mapa, List<ServerPlayer> forjados)
	{
		GD.Print("--- 7. a volta: o NPC nao pode ficar preso na fera (nem virar estatua) ---");

		ServerPlayer? s = ForjarSaiyajin(palco, mapa, forjados);
		ServerPlayer? agressor = ForjarSaiyajin(palco, mapa, forjados);
		if (s == null || agressor == null) { checa("PRECONDICAO: dois Saiyajins", false, ""); return; }

		s.Forma.Maestria.Por(Oozaru.IdRegular, 0);   // perder o controle e o caso que quebrava
		PorEmBrigaFeia(s, agressor);
		ALuaPegaOSaiyajin(s, TempoDoMundo);
		if (s.Oozaru == FormaOozaru.Nao) { checa("PRECONDICAO: ele virou macaco", false, ""); return; }

		double poderDeMacaco = s.Ficha.giantFormbuff;

		s.OozaruAte = NowMs() + (long)((Oozaru.Duracao(s.Oozaru) - Oozaru.SegundosDeGraca - 1) * 1000);
		TickDoOozaru(s, Protocol.TickSeconds);
		checa("PRECONDICAO: a fera tomou o corpo dele", s.CerebroDaPosse != null, "");

		// O PRAZO VENCE -- o caminho que passa pelo `DesfazerOozaru`.
		s.OozaruAte = NowMs() - 1;
		TickDoOozaru(s, Protocol.TickSeconds);

		checa("a forma acaba e o NPC deixa de ser fera", s.Oozaru == FormaOozaru.Nao, s.Oozaru.ToString());
		checa("**ELE NAO VIRA ESTATUA**: o corpo volta a ter cerebro", s.Cerebro != null, "");
		checa("...e a posse foi devolvida (a gaveta emprestada esta vazia)", s.CerebroDaPosse == null, "");
		checa("...e o cerebro que voltou e a MENTE DELE, e nao o tempero da fera",
			  s.Cerebro is { Inteligencia: > 0 },
			  s.Cerebro == null ? "sem cerebro" : $"int {s.Cerebro.Inteligencia}");
		checa("...e o poder de macaco saiu da ficha", Math.Abs(s.Ficha.giantFormbuff - 1) < 1e-9,
			  $"{s.Ficha.giantFormbuff} (era {poderDeMacaco})");

		// E ELE VOLTA A DECIDIR DE VERDADE -- um cerebro pendurado que nao e exercitado nao prova nada.
		//
		// ============================ E ESTA LINHA TEM UM BURACO CONHECIDO -- ELE ESTA MEDIDO ============================
		// `_decisoesDaMente` conta o MUNDO, e nao este corpo. Com os trinta Saiyajins que as familias
		// anteriores forjaram na zona, ele anda de qualquer jeito: injetando o `DevolverAsRedeas` antigo
		// (a estatua permanente), esta linha ficou **verde** com 2100 decisoes no mundo e zero neste
		// corpo. Quem reprova aquele defeito sao as tres linhas acima, que olham o ESTADO do corpo.
		//
		// A VERSAO ESPECIFICA FOI TENTADA E NAO SERVE, e ficou escrito pra ninguem tentar de novo: o
		// `Cerebro.Porque` (escrito dentro do `Pensar`) sai vazio mesmo num corpo saudavel e com
		// agressor de verdade, porque o ramo do NPC volta ANTES do `Pensar` quando nao ha presa -- e a
		// janela do `PasseioDeHabitante` sorteia pelo `_relogioDoMundo`, que uma bancada que chama o
		// laco a mao nao faz andar. Medir "ESTE corpo pensou" pede um contador POR CORPO que hoje nao
		// existe; virou buraco anotado em vez de uma linha que responde outra pergunta.
		//
		// O QUE ELA CONTINUA VALENDO: o laco da mente nao parou de rodar depois da devolucao.
		// ==========================================================================================================
		Restaurar(s);
		MarcarAgressao(s, pl);

		long antes = _decisoesDaMente;
		for (int t = 0; t < 60; t++) TickDosCorposSemDono(Protocol.TickSeconds);
		checa("...e o laco da mente continua rodando depois da devolucao (contador do MUNDO -- ver acima)",
			  _decisoesDaMente > antes, $"{_decisoesDaMente - antes} decisoes em 60 tiques");
	}

	// =====================================================================
	// FAMILIA 8 -- O QUE USA O MESMO MOTOR NAO PODE TER REGREDIDO
	// =====================================================================
	private void MedirQueNaoRegrediu(Verificacao checa, ServerPlayer pl, ZoneKey palco,
									 ZoneCollision mapa, List<ServerPlayer> forjados)
	{
		GD.Print("--- 8. o Oozaru do JOGADOR e a furia lendaria escrevem no mesmo lugar ---");

		string racaGuardada = pl.Race;
		FormaOozaru feraGuardada = pl.Oozaru;

		// O JOGADOR PERDENDO E RECUPERANDO AS REDEAS: e o caminho que a `--formasteste` ja mede, e o
		// que esta familia afirma e que ele continua terminando com `Cerebro == null` -- o corpo de um
		// jogador nao pensa sozinho, e a mudanca de hoje nao pode ter posto uma mente nele.
		TomarAsRedeas(pl);
		checa("JOGADOR: a fera assume e o input do dono passa a ser recusado",
			  pl.CerebroDaPosse != null && SemAsRedeas(pl));

		DevolverAsRedeas(pl);
		checa("JOGADOR: devolvidas as redeas, o corpo dele fica SEM cerebro (nao ganhou mente de NPC)",
			  pl.Cerebro == null && pl.CerebroDaPosse == null && !SemAsRedeas(pl));

		// A FERA TEM PRECEDENCIA SOBRE A FURIA, e agora isso vale pro NPC tambem.
		ServerPlayer? s = ForjarSaiyajin(palco, mapa, forjados);
		ServerPlayer? agressor = ForjarSaiyajin(palco, mapa, forjados);
		if (s != null && agressor != null)
		{
			PorEmBrigaFeia(s, agressor);
			ALuaPegaOSaiyajin(s, TempoDoMundo);
			if (s.Oozaru != FormaOozaru.Nao)
			{
				s.FuriaAte = NowMs() - 1;
				TickDaFuriaLendaria(s, Protocol.TickSeconds);
				checa("a FURIA LENDARIA se recusa a agir em corpo de macaco (a fera tem precedencia)",
					  s.FuriaAte == 0 && s.Oozaru != FormaOozaru.Nao,
					  $"furiaAte {s.FuriaAte}");
			}
			else checa("PRECONDICAO: o Saiyajin da familia 8 virou macaco", false, "");
		}

		pl.Race = racaGuardada;
		if (pl.Oozaru != feraGuardada && pl.Oozaru != FormaOozaru.Nao)
			DesfazerOozaru(pl, "a bancada terminou com voce.");
	}

	// =====================================================================
	// FAMILIA 9 -- O FIO: o gatilho esta LIGADO em algum lugar?
	// =====================================================================
	/// <summary>
	/// As familias 3 a 8 chamam o <see cref="ALuaPegaOSaiyajin"/> A MAO -- elas provam que a REGRA
	/// funciona. Nenhuma delas prova que alguem a CHAMA, e "a regra escrita nao e a regra ligada" e a
	/// armadilha que este port mais registra.
	///
	/// Aqui quem roda e o `TickDoCeu()` de verdade, o mesmo que o `Tick()` chama a 1 Hz.
	/// </summary>
	private void MedirOFio(Verificacao checa, ZoneKey palco,
						   ZoneCollision mapa, List<ServerPlayer> forjados)
	{
		GD.Print("--- 9. o fio: o TickDoCeu de verdade ---");

		ServerPlayer? s = ForjarSaiyajin(palco, mapa, forjados);
		ServerPlayer? agressor = ForjarSaiyajin(palco, mapa, forjados);
		if (s == null || agressor == null) { checa("PRECONDICAO: dois Saiyajins", false, ""); return; }

		PorEmBrigaFeia(s, agressor);
		checa("PRECONDICAO: ele ainda nao e fera", s.Oozaru == FormaOozaru.Nao, "");

		TickDoCeu();

		checa("**O GATILHO ESTA LIGADO**: um tique de ceu de verdade transforma o Saiyajin ferido",
			  s.Oozaru != FormaOozaru.Nao, s.Oozaru.ToString());
	}

	// =====================================================================
	// FAMILIA 10 -- QUANTO ISTO CUSTA POR CORPO POR TIQUE
	// =====================================================================
	/// <summary>
	/// ============================ A PERGUNTA RODA POR CORPO POR TIQUE, E O MUNDO TEM 148 ============================
	/// A fase 0 desta tarefa mediu o teto: perguntar o ceu, a vida e o rabo de todo corpo a 30 Hz custaria
	/// ~48 us/tique (0,14% do orcamento de 33.333 us) **e 568 KB/s de lixo pro coletor**, porque
	/// `Body.Achar` aloca 128 bytes por chamada. Aqui se mede o que foi de fato escrito, que e outra coisa:
	/// a pergunta mora no `TickDoCeu` (1 Hz) e as guardas estao em ordem crescente de preco.
	///
	/// SAO DOIS NUMEROS, e a diferenca entre eles E o desenho:
	///
	///   * **o mundo comum** -- corpo que nao e Saiyajin. Ele sai na terceira guarda (duas comparacoes de
	///     string) sem tocar no ceu, na ficha nem no corpo. E o que 99% dos 148 pagam;
	///   * **o pior caso** -- Saiyajin lutando, ferido grave, com plateia: ele paga TODAS as sete, ceu
	///     incluso. E o unico que chega la, e so numa noite de luta.
	///
	/// O NUMERO IMPRESSO E POR CORPO; o `Checa` afirma o TETO da conta a 148 corpos por segundo, que e o
	/// que o orcamento sente. Um teto e nao um valor exato porque a maquina que roda a bancada nao e a
	/// que roda o servidor -- o que a linha reprova e uma ordem de grandeza errada (um `Feridas.De` novo
	/// por corpo, uma varredura de zona, um `Achar` no caminho quente), nao um microssegundo a mais.
	/// ==========================================================================================================
	/// </summary>
	private void MedirOCusto(Verificacao checa, ZoneKey palco, ZoneCollision mapa, List<ServerPlayer> forjados)
	{
		GD.Print("--- 10. o custo por corpo por tique ---");

		ServerPlayer? saiyajin = ForjarSaiyajin(palco, mapa, forjados);
		ServerPlayer? agressor = ForjarSaiyajin(palco, mapa, forjados);
		ServerPlayer? comum = NascerNpc("cidadao", ZoneKey.Premade("Namek"),
			PontoDeNascimento(ZoneKey.Premade("Namek")), ++_lugarDaBancadaDaLuaFera);
		if (saiyajin == null || agressor == null || comum == null)
		{ checa("PRECONDICAO: corpos pra medir", false); return; }
		forjados.Add(comum);
		MoveToZone(comum.Id, palco, PontoDeNascimento(palco));

		PorEmBrigaFeia(saiyajin, agressor);
		PorEmBrigaFeia(comum, agressor);

		// O SAIYAJIN PRECISA CHEGAR NA SETIMA GUARDA E NAO PASSAR: com o ceu fora da cheia ele paga tudo,
		// ceu incluso, e volta sem transformar. Medir com a lua cheia mediria a TRANSFORMACAO, que
		// acontece uma vez por noite e nao por tique.
		double guardado = _adiantoDoCeu;
		AjustarCeuDaTerra(hora: 0.90, fase: (int)FaseDaLua.QuartoCrescente);

		const int Voltas = 200_000;
		double agora = TempoDoMundo;

		// aquecimento (JIT): sem ele a primeira volta mede a compilacao do metodo
		for (int i = 0; i < 20_000; i++) { ALuaPegaOSaiyajin(comum, agora); ALuaPegaOSaiyajin(saiyajin, agora); }

		var t = System.Diagnostics.Stopwatch.StartNew();
		for (int i = 0; i < Voltas; i++) ALuaPegaOSaiyajin(comum, agora);
		double usComum = t.Elapsed.TotalMilliseconds * 1000.0 / Voltas;

		t.Restart();
		for (int i = 0; i < Voltas; i++) ALuaPegaOSaiyajin(saiyajin, agora);
		double usSaiyajin = t.Elapsed.TotalMilliseconds * 1000.0 / Voltas;

		_adiantoDoCeu = guardado;

		// O CIDADAO SAI DO MUNDO ASSIM QUE A MEDIDA ACABA, pela razao escrita no `alien` da familia 3: posto no berco da
		// Terra -- em cima do host -- e com rancor contra um NPC, ele adotava o HOST pela busca alargada do `PresaDoNpc` e o
		// nocauteava no primeiro tique da familia 12 ("Nail NOCAUTEOU ..." no meio dela, e Zenkai pro host). A 12 so nao
		// reprovava porque o que ela olha e o vizinho da fera.
		RemoverNpc(comum);

		// O TETO REALISTA: 148 corpos, todos pagando o caminho do NAO-Saiyajin, mais os poucos que
		// chegam ao fim. O mundo de hoje tem UM Saiyajin; o teto abaixo supoe DEZ lutando ao mesmo tempo.
		double porSegundo = 138 * usComum + 10 * usSaiyajin;

		GD.Print($"  [custo] corte na 3a guarda (nao-Saiyajin): {usComum:0.000} us/corpo");
		GD.Print($"  [custo] as SETE guardas, ceu incluso:      {usSaiyajin:0.000} us/corpo");
		GD.Print($"  [custo] 148 corpos (138 comuns + 10 Saiyajins lutando), a 1 Hz: "
			   + $"{porSegundo:0.0} us/s = {porSegundo / 33_333.0 * 100:0.0000}% de UM tique");

		checa("o corte barato do nao-Saiyajin custa menos de 0,05 us (nao ha varredura escondida nele)",
			  usComum < 0.05, $"{usComum:0.000} us");
		checa("as sete guardas juntas custam menos de 1 us (o ceu e funcao pura, a ferida ja esta pronta)",
			  usSaiyajin < 1.0, $"{usSaiyajin:0.000} us");
		checa("o mundo inteiro por SEGUNDO cabe em 0,1% de UM tique de 33 ms",
			  porSegundo < 33.3, $"{porSegundo:0.0} us/s");
	}

	// =====================================================================
	// FAMILIA 11 -- A FURIA LENDARIA NUM CORPO DE NPC
	// =====================================================================
	/// <summary>
	/// ============================ A SEGUNDA POSSESSAO TAMBEM PASSOU A RECEBER NPC ============================
	/// A furia lendaria decidia posse lendo `SemAsRedeas`, que e `Cerebro != null` -- e isso era a MESMA
	/// pergunta que "alguem tomou este corpo" **enquanto so jogador podia ser possuido**. Um NPC nasce
	/// com o `Cerebro` cheio (`Temperamento.Montar`), entao pra ele a leitura antiga respondia *"a furia
	/// ja esta com este corpo"* desde o primeiro quadro: o passo 3 caia eternamente no ramo de DEVOLVER,
	/// e a posse **nunca acontecia num NPC**.
	///
	/// Ela reprova em tres pontos, e cada um e um jeito diferente de o conserto sumir:
	///   * a primeira linha e a raiz de tudo -- as duas perguntas dao respostas DIFERENTES num NPC. Uma
	///     edicao que voltasse `CerebroDaPosse` a ser sinonimo de `Cerebro` cairia aqui antes de
	///     qualquer sistema quebrar em jogo;
	///   * a posse propriamente dita: se voltar o `SemAsRedeas`, a furia nunca assume o NPC;
	///   * a VOLTA: a mesma estatua permanente da familia 7, pela outra porta. `DevolverAsRedeas` e
	///     compartilhado pelas duas possessoes, e uma bancada que so medisse a volta do Oozaru deixaria
	///     metade do caminho sem ninguem olhando.
	/// ==================================================================================================
	/// </summary>
	private void MedirAFuriaNoNpc(Verificacao checa, ZoneKey palco,
								  ZoneCollision mapa, List<ServerPlayer> forjados)
	{
		GD.Print("--- 11. a furia lendaria tambem possui NPC (a outra porta da mesma gaveta) ---");

		ServerPlayer? s = ForjarSaiyajin(palco, mapa, forjados);
		if (s == null) { checa("PRECONDICAO: um Saiyajin pra enfurecer", false); return; }

		// A RAIZ: as duas perguntas nao sao mais a mesma coisa. `SemAsRedeas` continua sendo "este
		// corpo esta sendo dirigido por alguem que nao o dono na tela" (e num NPC isso e sempre
		// verdade); `CerebroDaPosse` e "alguem TOMOU este corpo".
		checa("num corpo de NPC, `SemAsRedeas` e `CerebroDaPosse` dao respostas OPOSTAS",
			  SemAsRedeas(s) && s.CerebroDaPosse == null,
			  $"semRedeas={SemAsRedeas(s)} posse={(s.CerebroDaPosse == null ? "vazia" : "cheia")}");

		FormaDef? lendaria = Catalogo.Def("legendary");
		if (lendaria == null) { checa("PRECONDICAO: o catalogo tem a forma `legendary`", false); return; }

		object mentePropria = s.Cerebro!;
		s.Forma.Maestria.Por(lendaria.Id, 0);   // maestria cheia SEGURA a furia -- ver `FuriaLendaria.Dominou`
		EntrarNaForma(s, lendaria);

		// A CENA DA ESTREIA E DESLIGADA A MAO, e nao e trapaca: `EmCena` desarma a furia de proposito
		// (o prazo nao corre enquanto a cinematica prende o boneco), entao sem esta linha a bancada
		// mediria a cena e chamaria isso de "a furia nao pega NPC". Ver `TickDaFuriaLendaria`, saida 1.
		s.CenaSegundos = 0;

		TickDaFuriaLendaria(s, Protocol.TickSeconds);
		checa("o primeiro tique dentro da forma lendaria ARMA o relogio da furia no NPC",
			  s.FuriaAte != 0, $"furiaAte {s.FuriaAte}");
		checa("...e ate o prazo vencer o corpo ainda e dele", s.CerebroDaPosse == null);

		s.FuriaAte = NowMs() - 1;   // o prazo venceu
		TickDaFuriaLendaria(s, Protocol.TickSeconds);

		checa("**A FURIA TOMA O NPC**: com o prazo vencido, o servidor assume o corpo",
			  s.CerebroDaPosse != null, "a posse continuou vazia");
		checa("...e quem dirige e o tempero DA FURIA, e nao a mente sorteada dele",
			  !ReferenceEquals(s.Cerebro, mentePropria)
			  && s.Cerebro is { Inteligencia: 0, Disciplina: 0, VidaCautelosa: 0 },
			  s.Cerebro == null ? "sem cerebro" : $"int {s.Cerebro.Inteligencia}");

		// E A VOLTA, pela porta da furia: a forma DOMINADA solta o corpo na hora (o
		// `legendary_cur_mastery() >= 100` do `while` do DM).
		s.Forma.Maestria.Por(lendaria.Id, 100);
		TickDaFuriaLendaria(s, Protocol.TickSeconds);

		checa("a maestria cruza os 100% e a furia solta o corpo", s.CerebroDaPosse == null);
		checa("...e o NPC NAO vira estatua por esta porta tambem (a mente dele voltou)",
			  s.Cerebro is { Inteligencia: > 0 },
			  s.Cerebro == null ? "SEM CEREBRO -- estatua" : $"int {s.Cerebro.Inteligencia}");
		checa("...e o relogio da furia foi desarmado", s.FuriaAte == 0, $"{s.FuriaAte}");
	}

	// =====================================================================
	// FAMILIA 12 -- EM QUE RAMO O MACACO CAI
	// =====================================================================
	/// <summary>
	/// ============================ UM MACACO DE DEZ METROS QUE PASSEIA UM TILE A CADA 20 s ============================
	/// O `TicarUmCorpo` escolhe o comportamento por RAMO, e o do NPC de povoamento ganhou
	/// `&amp;&amp; npc.CerebroDaPosse == null` nesta rodada. Sem essa metade, o Saiyajin que a lua pegou
	/// continuaria caindo no ramo do HABITANTE -- e ali quem escolhe a vitima e o `PresaDoNpc`, que pra
	/// um hostil so enxerga JOGADOR e so dentro de 20 tiles. O resultado seria o oposto do pedido do
	/// dono (*"sai batendo em qualquer coisa"*): um Oozaru parado no meio do planeta, passeando.
	///
	/// **O QUE FAZ ESTA FAMILIA VALER** e que ela nao pergunta "qual ramo rodou" (nao ha como; o ramo
	/// nao deixa marca). Ela monta o unico quadro em que os dois ramos discordam e olha o RESULTADO:
	///
	///   * o vizinho e um cidadao -- **nao e jogador**, entao o ramo do habitante nunca o adota;
	///   * o host esta a 40 tiles -- **fora do raio de agressao** (20), entao o ramo do habitante nao
	///     tem jogador nenhum pra adotar e cai no passeio;
	///   * o vizinho esta COLADO, e no MESMO chao que a fera -- entao o ramo da fera o adota no primeiro
	///     tique e o soco chega sem ninguem precisar andar. Isso deixou de ser suposicao e virou a
	///     precondicao "chao pros dois" (o porque esta no corpo da familia: o berco da Terra mudou de
	///     lugar e o palco foi parar em cima de uma escarpa).
	///
	/// Com esse palco, "o vizinho apanhou" so pode ter vindo do ramo de baixo. E o CONTROLE e o mesmo
	/// corpo antes de virar macaco: mesma posicao, mesmo vizinho, MESMO tique de mundo, e ele nao encosta
	/// em ninguem.
	/// ==========================================================================================================
	/// </summary>
	private void MedirORamoDaFera(Verificacao checa, ServerPlayer pl, ZoneKey palco,
								  ZoneCollision mapa, List<ServerPlayer> forjados)
	{
		GD.Print("--- 12. o macaco cai no ramo da FERA, e nao no do habitante ---");

		// O PALCO FICA SEM MACACO ALHEIO ANTES DE COMECAR. As familias 3 a 9 deixaram Oozarus vivos na
		// Earth, e um deles chegando aqui morderia o vizinho -- a mordida que esta familia mede tem que
		// ter UM autor possivel. (`UltimoAgressor` guarda o ULTIMO, entao um segundo autor nao soma:
		// ele apaga.)
		foreach (ServerPlayer f in forjados)
			if (f.Oozaru != FormaOozaru.Nao) DesfazerOozaru(f, "a bancada limpou o palco.");

		// LONGE DO HOST DE PROPOSITO: 40 tiles e o dobro do `RaioDeAgressao` (20), ou seja o ramo do
		// habitante nao tem jogador pra adotar aqui. E o vizinho fica a UM tile, que e o alcance em que
		// o ramo da fera adota no primeiro tique.
		//
		// ============================ O VIZINHO NASCE COLADO NO CORPO QUE NASCEU, E NAO NO PONTO PEDIDO ============================
		// Ate aqui os dois pediam, cada um por conta propria, o chao livre mais perto de DOIS pontos
		// (`longeDoHost` e um tile a leste dele) -- e o `PontoLivrePerto` de cada um e livre pra desviar
		// pra lados OPOSTOS de uma parede. Enquanto o `PontoDeNascimento` da Terra era o (249,250) do
		// check-in da nave, 40 tiles ao sul era campo aberto e ninguem desviava. Desde que ele virou o
		// `/obj/SpawnPoint` do BYOND (a casa em (73,260), `SpawnPoints.dm:95`, commit 0b0c8b2), o ponto
		// pedido cai EM CIMA da escarpa diagonal que corta a Terra em (72..74, 300) (`z01_Earth.col`): a
		// fera desviava pra (72,301), ao pe dela, e o vizinho pra (73,299), em cima -- 72 px e uma
		// parede entre os dois. O macaco adotava o vizinho (o `PresaDaFera` nao olha parede), o soco
		// (40 px) nao chegava, e ele nao conseguia andar ate o vizinho nem investir (o `Aproximar` recusa
		// parede no caminho) -- e a familia ficava vermelha dizendo "a fera nao bate" quando o que
		// faltava era CHAO. Nao dependia da classe sorteada do host: o ponto pedido sai do
		// `PontoDeNascimento` do palco, que e o marco do MAPA e nao a posicao de ninguem.
		//
		// Ancorado no corpo que NASCEU, o vizinho so desvia pra perto dele. E a pergunta que a familia
		// inteira pressupoe virou PRECONDICAO escrita logo abaixo, com as duas chamadas de producao que
		// respondem "a fera chega nele?":
		//   * o ALCANCE DO SOCO (`CombatKnobs.Alcance`) -- o numero que o `AlvoNaFrente` compara pro alvo
		//     marcado. O soco em si NAO olha parede: quem decide se ele sai e so a distancia;
		//   * o CAMINHO (`MoveRules.PathOccupied` com o `ModoDeTravessiaDe` do corpo) -- a MESMA chamada
		//     com que o `Aproximar` recusa o passo curto e a investida. E ela, e nao o `PathBlocked`,
		//     porque ela amostra a CAIXA DOS PES (8 px pros lados, abaixo do centro) e nao a linha dos
		//     centros: os dois amostradores discordam rente a parede -- exatamente onde este palco mora --,
		//     e uma precondicao verde pelo amostrador errado devolveria o vermelho pra linha do macaco.
		// No dia em que o mapa mudar debaixo deste palco, quem fica vermelha e a linha que diz "nao ha
		// chao pros dois" -- e nao a do macaco, que mandaria alguem cacar defeito no cerebro da fera.
		// ================================================================================================================
		Vec2 longeDoHost = PontoDeNascimento(palco) + new Vec2(0, 40 * ZoneCollision.TileSize);
		ServerPlayer? fera = NascerNpc("guardiao_saiyajin", palco,
			mapa.PontoLivrePerto(longeDoHost), ++_lugarDaBancadaDaLuaFera);
		ServerPlayer? vizinho = fera == null ? null : NascerNpc("cidadao", palco,
			mapa.PontoLivrePerto(fera.Pos + new Vec2(ZoneCollision.TileSize, 0)),
			++_lugarDaBancadaDaLuaFera);
		if (fera == null || vizinho == null)
		{ checa("PRECONDICAO: um Saiyajin e um cidadao longe do host", false); return; }
		forjados.Add(fera);
		forjados.Add(vizinho);

		checa("PRECONDICAO: o host esta fora do raio de agressao dos dois (40 tiles)",
			  (pl.Pos - fera.Pos).Length > 20 * ZoneCollision.TileSize,
			  $"{(pl.Pos - fera.Pos).Length / ZoneCollision.TileSize:0} tiles");
		checa("PRECONDICAO: o vizinho e um corpo do mundo e NAO um jogador (o ramo do habitante o ignora)",
			  !EhJogador(vizinho), vizinho.Race);

		// CHAO PROS DOIS -- ver o bloco acima. O rodape diz as duas coordenadas em TILES, porque e isso
		// que se confere contra o `.col` quando esta linha ficar vermelha.
		float colado = (vizinho.Pos - fera.Pos).Length;
		bool caminhoFechado = MoveRules.PathOccupied(mapa, fera.Pos, vizinho.Pos, ModoDeTravessiaDe(fera));
		checa("PRECONDICAO: chao pros dois -- o vizinho nasceu ao alcance do soco da fera, e com caminho livre ate ele",
			  colado <= CombatKnobs.Alcance && !caminhoFechado,
			  $"{colado:0} px (o soco alcanca {CombatKnobs.Alcance:0}), caminho fechado: {(caminhoFechado ? "sim" : "nao")} | "
			  + $"fera ({(int)(fera.Pos.X / ZoneCollision.TileSize)},{(int)(fera.Pos.Y / ZoneCollision.TileSize)}) "
			  + $"vizinho ({(int)(vizinho.Pos.X / ZoneCollision.TileSize)},{(int)(vizinho.Pos.Y / ZoneCollision.TileSize)})");

		// ============================ O TIQUE DESTE PALCO E A CABECA DO `Tick()`, E NAO SO A MENTE ============================
		// As duas janelas abaixo chamavam SO o `TickDosCorposSemDono`, e isso media um mundo que o jogo
		// nao tem -- justamente nos dois relogios que decidem o que esta familia pergunta:
		//
		//   * a CENA DO MACACO. `AnunciarOozaru` -> `MarcarCena` prende o corpo por `SegundosPreso` (4 s na
		//     entrada do Oozaru, `Cinematicas.cs`), e quem abate esse prazo e SO o `TickDaForma`, dentro do
		//     `TickDosRelogiosDoCorpo`. Sem ele a fera ficava presa pela cena a janela INTEIRA: nao anda
		//     (`PodeMexerOCorpo`) e, desde que o `Aproximar` passou a perguntar o mesmo funil, tambem nao
		//     investe. Um vizinho que desse dois passos de passeio antes do primeiro soco ficava fora do
		//     alcance pra sempre -- o vermelho seria sorteio, e nao a regra;
		//   * a RECARGA DO SOCO. `CombatState.Recarga` so escorre no `CombatState.Tick`, que e o
		//     `TickCombate`. Sem ele a janela tinha UM golpe: o primeiro que saisse -- acertando ou no ar --
		//     era o ultimo.
		//
		// A ORDEM E A DO `Tick()`: grade, combate, relogios do corpo, mente. O POVOAMENTO FICA DE FORA de
		// proposito, pelo mesmo argumento do `TiqueDoMundo` da `--doiscorposteste`: ele nasce habitante NA
		// TERRA, e um terceiro corpo nascendo mais perto da fera que o vizinho trocaria a presa do
		// `PresaDaFera` e poria um segundo autor possivel na mordida. O ARREMESSO (`TickDoEmpurrao`)
		// tambem: ele e CONSEQUENCIA do golpe que ja escreveu o agressor e a vida, e aqui so jogaria o
		// vizinho contra a escarpa colada nele (corpo arremessado quebra cenario) sem mudar resposta
		// nenhuma.
		//
		// O CONTROLE RODA O MESMO TIQUE, e e isso que o mantem sendo controle: com o tique magro no
		// controle e o cheio na medida, "fora da forma ele nao bate" nao diria nada sobre o RAMO.
		// ======================================================================================================
		void TiqueDoPalco()
		{
			MontarAsGrades();
			TickCombate(Protocol.TickSeconds);
			TickDosRelogiosDoCorpo(Protocol.TickSeconds);
			TickDosCorposSemDono(Protocol.TickSeconds);
		}

		// ---- O CONTROLE: o mesmo corpo, ANTES da lua ----
		double vidaDeNascimento = vizinho.Ficha.HP;
		for (int t = 0; t < 120; t++) TiqueDoPalco();
		checa("CONTROLE: fora da forma, o Saiyajin nao encosta no vizinho (ramo do habitante)",
			  vizinho.UltimoAgressor != fera.Id
			  && Math.Abs(vizinho.Ficha.HP - vidaDeNascimento) < 1e-9,
			  $"agressor {vizinho.UltimoAgressor}, vida {vizinho.Ficha.HP:0.#} de {vidaDeNascimento:0.#}");

		// ---- A LUA PEGA ELE, E A FERA TOMA AS REDEAS ----
		fera.Forma.Maestria.Por(Oozaru.IdRegular, 0);
		PorEmBrigaFeia(fera, vizinho);
		ALuaPegaOSaiyajin(fera, TempoDoMundo);
		if (fera.Oozaru == FormaOozaru.Nao) { checa("PRECONDICAO: ele virou macaco", false); return; }

		fera.OozaruAte = NowMs()
					   + (long)((Oozaru.Duracao(fera.Oozaru) - Oozaru.SegundosDeGraca - 1) * 1000);
		TickDoOozaru(fera, Protocol.TickSeconds);
		if (fera.CerebroDaPosse == null) { checa("PRECONDICAO: a fera tomou o corpo", false); return; }

		// AS DUAS PERGUNTAS, LADO A LADO. E a prova de que o ramo IMPORTA: neste corpo, neste
		// instante, os dois ramos escolhem coisas diferentes -- um tem alvo, o outro nao tem nenhum.
		checa("o ramo da FERA enxerga o vizinho (`PresaDaFera(soJogadores: false)`)",
			  PresaDaFera(fera, soJogadores: false) == vizinho,
			  PresaDaFera(fera, soJogadores: false)?.Name ?? "ninguem");
		checa("...e o ramo do HABITANTE nao enxerga ninguem (`PresaDoNpc`: so jogador, so a 20 tiles)",
			  PresaDoNpc(fera) == null, PresaDoNpc(fera)?.Name ?? "ninguem");

		double vidaAntes = vizinho.Ficha.HP;
		// DUZENTOS TIQUES (6,7 s) NO MESMO TIQUE DO CONTROLE. O primeiro soco sai por volta de 1,5 s -- o
		// cerebro novo da fera nasce em `Plano.Nada` e so troca de plano depois do compromisso minimo
		// (`Cerebro.TempoMinimoNoPlano`, 1,2 s), na cadencia dela (`IntervaloDeDecisao`, 0,5 s) --, e a
		// cena de 4 s acaba no meio da janela: se o vizinho tiver saido do alcance, a fera ainda tem
		// tempo de ir atras dele pelas proprias pernas.
		for (int t = 0; t < 200; t++) TiqueDoPalco();

		checa("**O MACACO SAI BATENDO**: o vizinho apanhou, e o autor foi a fera",
			  vizinho.UltimoAgressor == fera.Id,
			  $"agressor {vizinho.UltimoAgressor} (a fera e {fera.Id})");
		checa("...e o estrago e de verdade (vida abaixo da que ele tinha)",
			  vizinho.Ficha.HP < vidaAntes || vizinho.Ficha.dead || vizinho.Ficha.KO,
			  $"{vizinho.Ficha.HP:0.#} de {vidaAntes:0.#}");
	}

	// =====================================================================
	// FAMILIA 13 -- A FERA SO CACA O QUE VE (o `oview` do DM)
	// =====================================================================
	/// <summary>
	/// ============================ O MACACO SABIA ONDE VOCE ESTAVA DESDE O POUSO ============================
	/// O `PresaDaFera` devolvia *o corpo em pe mais proximo da ZONA* -- do outro lado do mapa, atraves de muro.
	/// As duas possessoes do DM escolhem por `oview`, que e um quadrado de `get_dist` cortado por parede:
	///
	///   * o MACACO (`Oozaru.dm:190-201`): adota em `oview(container)` = `world.view` = 5 tiles, e LARGA quando o
	///     alvo sai da vista -- e so entao; enquanto ele esta a vista, o `target` fica, mesmo com outro mais perto;
	///   * a FURIA (`lssjbuff.dm:614-625`): adota em `oview(10)`, pula quem nao e `attackable`, e LARGA so pela
	///     DISTANCIA -- atras de um muro a presa continua sendo a presa.
	///
	/// O PALCO E ACHADO NO MAPA, e nao cravado (a licao das onze bancadas velhas): uma FAIXA de 13 celulas de
	/// chao com as fileiras vizinhas livres, e um MURO de uma celula entre dois pares de chao na mesma fileira --
	/// os dois longe (16 tiles) de todo corpo da zona, pra nenhum habitante entrar na conta de presa. As posicoes
	/// sao a VARIAVEL medida, e por isso a bancada as escreve; a fera vira macaco pelo funil da lua (`VirarFera`)
	/// e a furia toma o corpo pelo funil dela (`TomarAsRedeasDaFuria`).
	///
	/// COMO ELA REPROVA: volte o `PresaDaFera` antigo (o mais perto da zona) e as linhas do raio e da parede dizem
	/// "adotou"; tire a memoria (`PresaEngajada`) e a do "outro mais perto" diz que trocou e a do muro da furia diz
	/// "largou"; tire o crivo do `Intocavel` e a furia adota o corpo em cena.
	/// ============================================================================================================
	/// </summary>
	private void MedirAVistaDaFera(Verificacao checa, ZoneKey palco, ZoneCollision mapa, List<ServerPlayer> forjados)
	{
		GD.Print("--- 13. a fera so caca o que VE: o raio e a parede do `oview` ---");

		// O PALCO SEM MACACO ALHEIO: uma fera das familias anteriores nao pode ser a fera DESTA, nem presa dela.
		foreach (ServerPlayer f in forjados)
			if (f.Oozaru != FormaOozaru.Nao) DesfazerOozaru(f, "a bancada limpou o palco.");

		(int X, int Y)? faixa = AcharChaoDaVista(mapa, palco, comprimento: 13, colunaDoMuro: -1, longeDe: null);
		(int X, int Y)? muro = faixa is { } fx
			? AcharChaoDaVista(mapa, palco, comprimento: 5, colunaDoMuro: 2, longeDe: fx)
			: null;
		if (faixa is not { } f0 || muro is not { } m0)
		{
			checa("PRECONDICAO: a Terra tem uma faixa de 13 celulas de chao e um muro de uma celula longe de todo corpo",
				  false, $"faixa {faixa?.ToString() ?? "nenhuma"}, muro {muro?.ToString() ?? "nenhum"}");
			return;
		}
		Vec2 NaFaixa(int dx) => mapa.CentroDaCelula(f0.X + dx, f0.Y);
		Vec2 NoMuro(int dx) => mapa.CentroDaCelula(m0.X + dx, m0.Y);

		ServerPlayer? fera = NascerNpc("guardiao_saiyajin", palco, NaFaixa(0), ++_lugarDaBancadaDaLuaFera);
		ServerPlayer? a = NascerNpc("cidadao", palco, NaFaixa(4), ++_lugarDaBancadaDaLuaFera);
		ServerPlayer? b = NascerNpc("cidadao", palco, NaFaixa(12), ++_lugarDaBancadaDaLuaFera);
		if (fera == null || a == null || b == null) { checa("PRECONDICAO: a fera e as duas presas nasceram", false); return; }
		forjados.Add(fera);
		forjados.Add(a);
		forjados.Add(b);

		VirarFera(fera, FormaOozaru.Regular);
		checa("PRECONDICAO: o Saiyajin e macaco, e o muro corta a linha entre as duas pontas dele",
			  fera.Oozaru != FormaOozaru.Nao && mapa.PathBlocked(NoMuro(0), NoMuro(4)) && !mapa.PathBlocked(NaFaixa(0), NaFaixa(12)),
			  $"oozaru {fera.Oozaru}, faixa ({f0.X},{f0.Y}), muro ({m0.X},{m0.Y})");

		// ---- O MACACO: `oview(container)`, 5 tiles ----
		ServerPlayer? viu = PresaDaFera(fera, soJogadores: false);
		checa("o MACACO adota quem esta a 4 tiles, a vista (`oview(container)`, `Oozaru.dm:191`)",
			  viu == a, viu?.Name ?? "ninguem");

		a.Pos = NaFaixa(7);
		viu = PresaDaFera(fera, soJogadores: false);
		checa("...e a 7 tiles NAO ve ninguem: o `oview` sem numero e o `world.view` = 5 (a presa de antes saiu da conta)",
			  viu == null, viu?.Name ?? "ninguem");

		a.Pos = NaFaixa(4);
		PresaDaFera(fera, soJogadores: false);
		b.Pos = NaFaixa(2);
		viu = PresaDaFera(fera, soJogadores: false);
		checa("...e com a presa A VISTA ele NAO troca por outro que chegou mais perto (o `target` fica, `:195-196`)",
			  viu == a, $"{viu?.Name ?? "ninguem"} (a presa e {a.Name}, o de perto e {b.Name})");

		a.Pos = NaFaixa(7);
		viu = PresaDaFera(fera, soJogadores: false);
		checa("...e quando ela SAI da vista ele a larga e pega quem esta a vista (`container.target = null`, `:201`)",
			  viu == b, viu?.Name ?? "ninguem");

		fera.Pos = NoMuro(0);
		a.Pos = NoMuro(4);
		b.Pos = NaFaixa(12);
		viu = PresaDaFera(fera, soJogadores: false);
		checa("...e a 4 tiles, ATRAS DO MURO, ele nao a ve (a parede corta o `oview`)",
			  viu == null, viu?.Name ?? "ninguem");

		// ---- A FURIA: `oview(LEGB_RANGE)`, 10 tiles, e larga so pela distancia ----
		DesfazerOozaru(fera, "a bancada trocou a fera pela furia.");
		TomarAsRedeasDaFuria(fera, null, 0);
		fera.PresaEngajada = 0;
		fera.Pos = NaFaixa(0);
		a.Pos = NaFaixa(9);
		b.Pos = NaFaixa(12);
		checa("PRECONDICAO: a furia tomou o corpo, e ele nao e macaco",
			  fera.CerebroDaPosse != null && fera.Oozaru == FormaOozaru.Nao);
		viu = PresaDaFera(fera, soJogadores: false);
		checa("a FURIA ve mais longe: adota a 9 tiles (`oview(LEGB_RANGE)`, `LEGB_RANGE 10`)",
			  viu == a, viu?.Name ?? "ninguem");

		a.Pos = NaFaixa(11);
		viu = PresaDaFera(fera, soJogadores: false);
		checa("...e a 11 tiles larga e nao ve ninguem (`get_dist(src, prey) > LEGB_RANGE`, `:614`)",
			  viu == null, viu?.Name ?? "ninguem");

		fera.Pos = NoMuro(0);
		a.Pos = NoMuro(1);
		PresaDaFera(fera, soJogadores: false);
		a.Pos = NoMuro(4);
		viu = PresaDaFera(fera, soJogadores: false);
		checa("...e a presa que passou pra tras do muro CONTINUA presa (a furia larga so pela distancia, `:614`)",
			  viu == a, viu?.Name ?? "ninguem");

		fera.PresaEngajada = 0;
		fera.Pos = NaFaixa(0);
		a.Pos = NaFaixa(3);
		b.Pos = NaFaixa(6);
		AdminForcarForma(a, "ssj1");   // a cena cheia da estreia: `Intocavel`, o `attackable = 0` do DM
		viu = PresaDaFera(fera, soJogadores: false);
		checa("...e ela PULA quem esta em cena (`!M.attackable`, `:618`): o corpo intocavel a 3 tiles fica, o de 6 e a presa",
			  a.Combate.Intocavel && viu == b, $"a em cena={a.Combate.Intocavel}, presa {viu?.Name ?? "ninguem"}");

		DevolverAsRedeas(fera);
	}

	/// <summary>
	/// ACHA NO MAPA uma fileira de <paramref name="comprimento"/> celulas -- todas de chao A PE com as fileiras
	/// de cima e de baixo livres, menos a <paramref name="colunaDoMuro"/> (negativa = nenhuma), que tem que ser
	/// PAREDE --, com todo corpo da zona a mais de 16 tiles e, se <paramref name="longeDe"/> vier, a mais de 30
	/// tiles daquela celula. Devolve a celula da ponta esquerda.
	/// </summary>
	private (int X, int Y)? AcharChaoDaVista(ZoneCollision mapa, ZoneKey palco, int comprimento, int colunaDoMuro,
											 (int X, int Y)? longeDe)
	{
		const int Margem = 20;
		List<ServerPlayer> corpos = ZoneList(palco.Hash);
		for (int y = Margem; y < mapa.Height - Margem; y++)
			for (int x = Margem; x < mapa.Width - Margem - comprimento; x++)
			{
				bool serve = true;
				for (int i = 0; i < comprimento && serve; i++)
				{
					if (i == colunaDoMuro) { serve = mapa.BlockedCell(x + i, y); continue; }
					serve = mapa.ServeDeChao(x + i, y) && mapa.ServeDeChao(x + i, y - 1) && mapa.ServeDeChao(x + i, y + 1);
				}
				if (!serve) continue;
				if (longeDe is { } l && Math.Max(Math.Abs(l.X - x), Math.Abs(l.Y - y)) <= 30) continue;
				Vec2 centro = mapa.CentroDaCelula(x, y);
				bool alguemPerto = false;
				foreach (ServerPlayer c in corpos)
					if (Jandirus.Core.Social.Fusao.DistanciaEmTilesDoDm(c.Pos, centro, ZoneCollision.TileSize) <= 16 + comprimento)
					{ alguemPerto = true; break; }
				if (!alguemPerto) return (x, y);
			}
		return null;
	}

	// =====================================================================
	// O PALCO VIVO (`--macacovivo`) -- o lado do servidor da FOTO
	// =====================================================================
	/// <summary>
	/// ============================ NUMERO NAO RESPONDE "O SPRITE TROCOU?" ============================
	/// As doze familias acima medem o gatilho, a decisao e a volta -- e todas passariam verdes num
	/// servidor que transformasse o NPC sem que **um pixel mudasse na tela de quem esta olhando**. O
	/// caminho do desenho e outro (`S2C.Oozaru` -> `World.AoVirarOozaru` -> `CharacterVisual`), e ele so
	/// tem um juiz: a foto.
	///
	/// Este palco existe pra o robo `--diagmacaco` (`Client/RoboDeMacaco.cs`) ter o que fotografar, e
	/// ele nao encurta caminho nenhum: nasce um Saiyajin pelo `NascerNpc` de producao, marca a agressao
	/// pelo `MarcarAgressao`, poe a Terra em lua cheia pela manivela do `--luateste` e **quem transforma
	/// e o `TickDoCeu` de verdade**, pelas sete guardas.
	///
	/// ============================ POR QUE A FERIDA E ATRASADA ============================
	/// Se o corpo ja nascesse aberto, a transformacao aconteceria no primeiro tique de ceu depois do
	/// login -- ou seja, ANTES de o cliente ter desenhado o primeiro quadro. A foto "antes" sairia de um
	/// macaco, e a bancada visual inteira mediria o fim da historia. Os
	/// <see cref="SegundosAteAbrirOCorpo"/> sao a janela em que o robo fotografa o Saiyajin ainda gente.
	/// ==================================================================================
	/// </summary>
	private bool _macacoVivo;

	/// <summary>O corpo em cena. Zero = o palco ainda nao foi montado.</summary>
	private int _macacoVivoId;

	/// <summary>Quando abrir o corpo dele (ms do relogio do servidor). Zero = nada agendado.</summary>
	private long _macacoVivoFereEm;

	/// <summary>
	/// Quanto o Saiyajin fica INTEIRO depois do login, pra a foto "antes" existir.
	///
	/// VINTE SEGUNDOS, e o numero foi corrigido POR UMA RODADA VERMELHA: com dez, uma rodada em que o
	/// cliente demorou a montar a zona chegou ao primeiro quadro com o macaco JA transformado -- a foto
	/// "antes" teria saido do fim da historia, e foi a checagem `!_pacoteChegou` do robo que acusou.
	///
	/// A folga e de graca (o palco nao mede tempo) e a lua dura a noite inteira.
	/// </summary>
	private const double SegundosAteAbrirOCorpo = 20;

	/// <summary>
	/// MONTA O PALCO no primeiro login. O host e o AGRESSOR -- e a briga do pedido do dono, e ela nao
	/// e simulada: `MarcarAgressao` e o funil unico que escreve a tag de combate de 90 s.
	/// </summary>
	private void MontarOPalcoDoMacaco(ServerPlayer pl)
	{
		ZoneKey palco = pl.Zone;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(palco);
		if (mapa == null) { GD.PrintErr("[macacovivo] a zona do host nao tem mapa"); return; }

		// A LUA CHEIA NO ALTO, pela manivela do `--luateste`: sem ela o palco esperaria ate oito noites
		// de 24 minutos (mais de tres horas) pela proxima cheia.
		AjustarCeuDaTerra(hora: 0.90, fase: Ceu.Cheia);

		// SEIS TILES: perto o bastante pra caber no mesmo quadro que o host (a camera segue o host) e
		// longe o bastante pra o macaco de dez metros nao nascer por cima da lente.
		Vec2 onde = mapa.PontoLivrePerto(pl.Pos + new Vec2(6 * ZoneCollision.TileSize, 0));
		ServerPlayer? s = NascerNpc("guardiao_saiyajin", palco, onde, ++_lugarDaBancadaDaLuaFera);
		if (s == null) { GD.PrintErr("[macacovivo] o molde `guardiao_saiyajin` nao nasceu"); return; }

		MarcarAgressao(s, pl);
		_macacoVivoId = s.Id;
		_macacoVivoFereEm = NowMs() + (long)(SegundosAteAbrirOCorpo * 1000);

		EstadoDoCeu ceu = Ceu.De(RelogioDaZona(palco), TempoDoMundo);
		GD.Print($"[macacovivo] {s.Name} ({s.Race}) nasceu a 6 tiles do host, lutando | "
			   + $"maestria da fera {s.Forma.Maestria.De(Oozaru.IdRegular):0}% | "
			   + $"lua {Ceu.NomeDaFase(ceu.Fase)}, no ceu={ceu.LuaNoCeu} | "
			   + $"o corpo dele abre em {SegundosAteAbrirOCorpo:0} s");
	}

	/// <summary>
	/// ABRE O CORPO DELE -- e daqui em diante ninguem mais empurra nada: a proxima volta do
	/// <see cref="TickDoCeu"/> passa pelas sete guardas e chama o `Apeshit` se elas deixarem.
	/// </summary>
	private void FerirOPalcoDoMacaco()
	{
		if (NowMs() < _macacoVivoFereEm) return;
		_macacoVivoFereEm = 0;

		if (!_players.TryGetValue(_macacoVivoId, out ServerPlayer? s)) return;
		FerirAteSangrar(s);
		TickDasFeridas();
		GD.Print($"[macacovivo] o braco de {s.Name} se abriu -- ferida grave={Feridas.Grave(s.EnvFeridas)}, "
			   + $"tag de combate={(NowMs() < s.RancorAte ? "quente" : "fria")}. A lua faz o resto.");
	}

	// =====================================================================
	// AS FERRAMENTAS DA BANCADA
	// =====================================================================
	/// <summary>
	/// Nasce um Saiyajin na Terra pelo caminho de PRODUCAO, e ja no chao livre.
	///
	/// `guardiao_saiyajin` e o unico molde Saiyajin que o interruptor do dono deixa nascer hoje (o
	/// `soldado_saiyajin` esta desligado) -- ver o cabecalho deste arquivo. Ele comeca na forma BASE
	/// (`Catalogo.IdDoPiso`), o que importa: em SSJ o `QualSai` mandaria pro Dourado, que ele nao tem.
	/// </summary>
	private ServerPlayer? ForjarSaiyajin(ZoneKey palco, ZoneCollision mapa, List<ServerPlayer> forjados)
	{
		Vec2 berco = PontoDeHabitante(palco, ++_lugarDaBancadaDaLuaFera);
		ServerPlayer? s = NascerNpc("guardiao_saiyajin", palco,
			mapa.PontoLivrePerto(berco), ++_lugarDaBancadaDaLuaFera);
		if (s != null) forjados.Add(s);
		return s;
	}

	/// <summary>
	/// POE O CORPO NO ESTADO QUE O DONO DESCREVEU: **lutando** e **com ferimento grave**.
	///
	/// Os dois pelos funis de PRODUCAO e nao escrevendo campo: a agressao pelo `MarcarAgressao` (o funil
	/// unico, que e quem escreve o `RancorAte`), o estrago pelo `Corpo.Ferir` e a mascara pelo
	/// `TickDasFeridas`. Uma bancada que escrevesse `RancorAte` e `EnvFeridas` na mao diria "o gatilho
	/// funciona" sobre uma briga que nunca aconteceu -- e nao pegaria o dia em que o funil de agressao
	/// deixasse de escrever a tag.
	/// </summary>
	private void PorEmBrigaFeia(ServerPlayer vitima, ServerPlayer agressor)
	{
		MarcarAgressao(vitima, agressor);
		FerirAteSangrar(vitima);
		TickDasFeridas();
	}

	/// <summary>
	/// ABRE O BRACO DESTE CORPO ate a mascara chamar aquilo de grave.
	///
	/// O BRACO E NAO O TORSO de proposito: nucleo abaixo do limiar de quebra NOCAUTEIA
	/// (`Body.DeveNocautear`), e o `Apeshit` recusa corpo caido -- a bancada estaria medindo o portao
	/// errado. Braco quebrado di, sangra e deixa o Saiyajin de pe, que e exatamente o quadro do pedido
	/// do dono ("comecando a sofrer ferimentos graves").
	///
	/// `letal: true` porque o golpe nao-letal tem piso em 19,8% de vida (`Body.Ferir`), e 19,8% cai
	/// exatamente EM CIMA do degrau de grave -- uma bancada que medisse na borda passaria ou reprovaria
	/// por arredondamento.
	/// </summary>
	private static void FerirAteSangrar(ServerPlayer pl)
	{
		if (pl.Combate?.Corpo is not { } corpo) return;
		foreach (BodyPart p in corpo.Partes)
			if (p.Nome is "Braco direito" && !p.Decepado)
				corpo.Ferir(p, p.VidaMax * 0.92, letal: true);
		pl.Combate.SincronizarVida();
	}
}
