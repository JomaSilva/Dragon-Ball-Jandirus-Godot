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
	///   * o vizinho esta COLADO -- entao o ramo da fera o adota no primeiro tique.
	///
	/// Com esse palco, "o vizinho apanhou" so pode ter vindo do ramo de baixo. E o CONTROLE e o mesmo
	/// corpo antes de virar macaco: mesma posicao, mesmo vizinho, e ele nao encosta em ninguem.
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
		Vec2 longeDoHost = PontoDeNascimento(palco) + new Vec2(0, 40 * ZoneCollision.TileSize);
		ServerPlayer? fera = NascerNpc("guardiao_saiyajin", palco,
			mapa.PontoLivrePerto(longeDoHost), ++_lugarDaBancadaDaLuaFera);
		ServerPlayer? vizinho = NascerNpc("cidadao", palco,
			mapa.PontoLivrePerto(longeDoHost + new Vec2(ZoneCollision.TileSize, 0)),
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

		// ---- O CONTROLE: o mesmo corpo, ANTES da lua ----
		double vidaDeNascimento = vizinho.Ficha.HP;
		for (int t = 0; t < 120; t++) TickDosCorposSemDono(Protocol.TickSeconds);
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
		for (int t = 0; t < 200; t++) TickDosCorposSemDono(Protocol.TickSeconds);

		checa("**O MACACO SAI BATENDO**: o vizinho apanhou, e o autor foi a fera",
			  vizinho.UltimoAgressor == fera.Id,
			  $"agressor {vizinho.UltimoAgressor} (a fera e {fera.Id})");
		checa("...e o estrago e de verdade (vida abaixo da que ele tinha)",
			  vizinho.Ficha.HP < vidaAntes || vizinho.Ficha.dead || vizinho.Ficha.KO,
			  $"{vizinho.Ficha.HP:0.#} de {vidaAntes:0.#}");
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
