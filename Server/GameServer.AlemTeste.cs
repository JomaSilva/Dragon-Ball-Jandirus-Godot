using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ A BANCADA DA MORTE E DO OUTRO MUNDO (`--alemteste`) ============================
/// *"falta fazer o personagem quando MORRER ir pro OUTRO MUNDO e a AUREOLA aparecer sobre a
/// cabeca"* -- o pedido do dono, e esta e a bancada que faltava pra ele.
///
/// ============================ POR QUE ELA PRECISOU EXISTIR ============================
/// A viagem e a auréola foram escritas, compiladas e commitadas **sem uma linha de bancada**, e o
/// levantamento que veio depois disse a frase que originou este arquivo: *"nao existe nenhum ponto
/// de entrada headless que mate um jogador com `Peer`; nao vi um `IrProAlem` disparar em execucao"*.
/// Codigo lido e codigo que compila nao sao codigo que RODA -- e a primeira coisa que esta bancada
/// achou foi um defeito que apagava o pedido inteiro:
///
///     **o `IrProAlem` nunca rearmava o `RelogioDaMorte`.** O campo continuava com o vencimento dos
///     15 s, ja passado; no tique seguinte (33 ms) a triagem chamava o `PassoDaMorte` de novo, o
///     corpo ja estava no alem, e o `Renascer` disparava. O jogador via o Outro Mundo por UM QUADRO,
///     enquanto o log anunciava "60s ate voltar a vida". `Alem.MsNoAlem` estava escrita, documentada
///     em vinte linhas, e o unico consumidor dela era essa mensagem.
///
/// Nada em jogo reclamaria: quem morre volta a vida no berco de qualquer jeito, que e o que o port
/// fazia antes da viagem. O sintoma era a AUSENCIA de um lugar.
/// ==================================================================
///
/// ============================ AS SETE FAMILIAS ============================
///   1. **O LUGAR** -- as tres zonas do alem existem no catalogo, `EhOAlem` responde as tres e nao
///      responde a Terra, e a mesa do Enma cai onde o `Death.dm:110` manda (com a inversao do Y do
///      BYOND conferida na conta, e o ponto conferido contra a COLISAO do mapa convertido).
///   2. **O FUNIL** -- `Morrer()` sozinho arma o relogio, porque o gancho `AoMorrer` esta instalado
///      pelo `PrepararCombate`. Reprova se alguem voltar a escrever o prazo a mao num chamador.
///   3. **A TRIAGEM** -- os corpos que NAO vao. Um por grupo do `Core/Npc/Gente.cs`, e cada um pelo
///      caminho de producao (`VenceuOPrazoDaMorte`), nao por um crivo copiado aqui.
///   4. **A VIAGEM** -- o host morre de verdade: cai **sem auréola** (ele e o cadaver, e o cadaver do
///      DM e fotografado antes de ela existir -- `Death.dm:64-67` x `:106-108`), espera, sobe, chega
///      inteiro, de pe, curado, COM auréola e AINDA MORTO. E o relogio da segunda etapa e conferido
///      -- a familia que nasceu do defeito acima.
///   5. **A AUREOLA NO FIO** -- ela e detectada por DIFERENCA e nao por chamada: uma morte escrita
///      DIRETO na ficha (como fazem o restauro da mente, o G4 e a gestacao) tem que acender do mesmo
///      jeito. E o pacote nao sai quando nada muda.
///   6. **O QUE SE PODE FAZER MORTO** -- o `move = 1` do `Death()` e as tres negativas do original:
///      sem Zenkai, sem digerir, estamina travada em 50%, Ki drenando em dobro.
///   7. **NAO SAIR DO ALEM** -- as passagens do alem so levam ao alem, decolar de la e recusado, e a
///      recusa nova (`OAlemNaoDeixaSair`) responde ao caso que os mapas de hoje nao produzem.
///   8. **A VOLTA** -- o `ReviveMe()` do DM tem que desfazer **tudo** o que a morte fez. Item a item,
///      contra a lista do `Renascer` -- e a auréola some junto, sem uma segunda linha.
/// ==========================================================
///
///     Godot --headless --path . --host --rede 7976 --alemteste --raca Saiyan
///                      --conta bancada_alem --nome Falecido
///
/// ============================ O QUE ELA MEXE, E COMO DEVOLVE ============================
/// Ela **mata o host de verdade** -- e o unico jeito de exercitar o `EhJogador`, porque um corpo
/// forjado nao tem `Peer` e a triagem o recusa por desenho (que e o item 3). Tudo o que ela toca do
/// host e fotografado antes e devolvido no `finally`: zona, posicao, `dead`, corpo, Ki, o relogio da
/// morte e a auréola enviada. Os corpos forjados saem do `_players` e das listas de zona.
///
/// **A AUREOLA DO CLIENTE NAO E MEDIDA AQUI** e nem poderia: pacote que saiu no fio nao volta. A
/// camada de desenho tem bancada propria, do outro lado -- `RoboDeForma.AAureolaSobreACabeca`.
/// ==================================================================================
/// </summary>
public partial class GameServer
{
	private bool _alemDeTeste;

	/// <summary>Faixa de ids propria -- longe do `_nextId` e das faixas das outras bancadas.</summary>
	private const int IdBaseDoAlemDeTeste = 91_300;

	private int _alemOk, _alemFalhou;

	private void AfirmarAlem(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _alemOk++; GD.Print($"[alem]   OK    {oque}"); return; }
		_alemFalhou++;
		GD.PrintErr($"[alem]   FALHA {oque}   {detalhe}");
	}

	private void RodarBancadaDoAlem(ServerPlayer pl)
	{
		_alemOk = _alemFalhou = 0;
		GD.Print("[alem] ================ A MORTE, O OUTRO MUNDO E A AUREOLA ================");

		// A FOTO DO HOST. Ele vai MORRER aqui dentro -- ver o cabecalho.
		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		bool mortoGuardado = pl.Ficha.dead, koGuardado = pl.Ficha.KO;
		long relogioGuardado = pl.RelogioDaMorte;
		bool viajouGuardado = pl.MorteJaViajou;
		bool aureolaGuardada = pl.EnvAureola;
		double kiGuardado = pl.Ficha.Ki, vigorGuardado = pl.Ficha.stamina;
		double nutricaoGuardada = pl.Ficha.CurrentNutrition;
		bool voandoGuardado = pl.Voando;

		var forjados = new List<ServerPlayer>();
		List<string>? escutaAntes = EscutaDeAvisos;
		EscutaDeAvisos = [];

		try
		{
			AfirmarAlem("PRECONDICAO: o host tem dono na tela (sem `Peer` a triagem o recusaria)",
						EhJogador(pl));

			OLugarEACoordenada();
			OFunilArmaORelogio(pl);
			ATriagemDosCorpos(pl, forjados);
			AViagemAoOutroMundo(pl);
			AAureolaSaiPorDiferenca(pl);
			OQueSePodeFazerMorto(pl);
			DoAlemNaoSeSaiAndando(pl);
			AVoltaDesfazAMorte(pl);

			AfirmarAlem("a bancada chegou ao fim (sem esta linha, abortar no meio reportaria '0 falhas')",
						true);
		}
		catch (Exception e)
		{
			AfirmarAlem($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			EscutaDeAvisos = escutaAntes;

			foreach (ServerPlayer f in forjados)
			{
				_npcsPraTirar.Remove(f);
				_players.Remove(f.Id);
				ZoneList(f.Zone.Hash).Remove(f);
				f.Peer = null;   // o `Peer` emprestado nao pode sair daqui em corpo nenhum
			}

			// O HOST VOLTA VIVO E NO LUGAR. `Reviver` antes de mover: um corpo morto largado na
			// zona de origem seria a bancada deixando o jogo pior do que encontrou.
			pl.Combate.Reviver();
			pl.Ficha.dead = mortoGuardado;
			pl.Ficha.KO = koGuardado;
			pl.Ficha.Ki = kiGuardado;
			pl.Ficha.stamina = vigorGuardado;
			pl.Ficha.CurrentNutrition = nutricaoGuardada;
			pl.RelogioDaMorte = relogioGuardado;
			pl.MorteJaViajou = viajouGuardado;
			pl.EnvAureola = aureolaGuardada;
			pl.Voando = voandoGuardado;
			if (!pl.Zone.Equals(zonaGuardada)) MoveToZone(pl.Id, zonaGuardada, posGuardada);
			else pl.Pos = posGuardada;
			pl.Combate.SincronizarVida();
			MandarAureola(pl);
		}

		GD.Print($"[alem] ================ {_alemOk} passaram, {_alemFalhou} falharam ================");
	}

	// =====================================================================
	// 1) O LUGAR E A COORDENADA
	// =====================================================================
	/// <summary>
	/// AS TRES ZONAS E A MESA DO ENMA. A conta do Y e o unico lugar deste caminho que erra CALADO: uma
	/// inversao errada nao quebra nada, so cospe o morto no lugar errado do Outro Mundo -- e ninguem
	/// tem como saber qual era o lugar certo sem abrir o `.dmm`.
	/// </summary>
	private void OLugarEACoordenada()
	{
		GD.Print("[alem] -- 1) o lugar e a coordenada --");

		foreach (string nome in new[] { Alem.ZonaDoOutroMundo, Alem.ZonaDoCeu, Alem.ZonaDoInferno })
		{
			AfirmarAlem($"a zona '{nome}' existe no catalogo (mapa convertido do .dmm)",
						_catalogo?.Get(nome) != null);
			AfirmarAlem($"...e o `EhOAlem` a reconhece", Alem.EhOAlem(nome));
		}

		// A OUTRA METADE DA PERGUNTA. Sem ela, um `EhOAlem` que devolvesse `true` pra tudo passaria em
		// todas as linhas acima -- e o morto ficaria de pe em qualquer planeta.
		AfirmarAlem("e a Terra NAO e o alem (o crivo nao e um `return true`)",
					!Alem.EhOAlem("Earth") && !Alem.EhOAlem(SpawnZone));

		// ============================ A INVERSAO DO Y, CONFERIDA NA CONTA ============================
		// `loc = locate(187, 104, 6)` -- `Death.dm:110`. No BYOND o eixo cresce pra CIMA; aqui, pra
		// baixo. Com uma altura de 500 tiles a linha 104 do original vira a 396 daqui, e a bancada
		// escreve os dois numeros pra a divergencia saltar aos olhos.
		// ==========================================================================================
		const int alturaFicticia = 500;
		Vec2 mesa = Alem.MesaDoEnma(alturaFicticia);
		int cx = (int)(mesa.X / ZoneCollision.TileSize);
		int cy = (int)(mesa.Y / ZoneCollision.TileSize);
		AfirmarAlem("a coluna sai direto do DM (x 187 -> celula 186, base 0)", cx == Alem.EnmaX - 1, $"{cx}");
		AfirmarAlem("e a LINHA e invertida (y 104 do BYOND, altura 500 -> celula 396)",
					cy == alturaFicticia - Alem.EnmaY, $"{cy}");
		AfirmarAlem("...e o ponto cai no MEIO do tile, e nao na quina",
					Mathf.IsEqualApprox(mesa.X % ZoneCollision.TileSize, ZoneCollision.TileSize / 2f));

		// ============================ E ELE E LIVRE NO MAPA DE VERDADE ============================
		// A altura ficticia acima prova a FORMULA; esta metade prova o LUGAR. Cair dentro de uma
		// parede no Outro Mundo seria a segunda maneira de prender alguem -- a primeira era nao ter
		// como voltar.
		// ====================================================================================
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(alem);
		AfirmarAlem("o mapa do Outro Mundo carrega (sem ele a chegada seria um palpite)", mapa != null);
		if (mapa == null) return;

		Vec2 real = MesaDoEnma(alem);
		AfirmarAlem("a chegada e uma celula LIVRE do z6 convertido (nao e parede)",
					!mapa.BlockedCell((int)(real.X / ZoneCollision.TileSize),
									  (int)(real.Y / ZoneCollision.TileSize)),
					$"({(int)(real.X / ZoneCollision.TileSize)},{(int)(real.Y / ZoneCollision.TileSize)})");
	}

	// =====================================================================
	// 2) O FUNIL -- um lugar so decide "morreu"
	// =====================================================================
	/// <summary>
	/// `Morrer()` SOZINHO TEM QUE ARMAR O RELOGIO. E a prova de que o gancho `CombatState.AoMorrer`
	/// esta instalado -- e de que ninguem precisa (nem deve) escrever o prazo a mao.
	///
	/// COMO REPROVA: tire a linha do `AoMorrer` do `PrepararCombate` e todo caminho de morte do jogo
	/// (soco, Kamehameha, explosao de planeta, calor da estrela, fome, verb de admin) para de contar
	/// o tempo -- o corpo fica morto no chao pra sempre, e nenhum deles tem uma linha de codigo em
	/// comum onde isso apareceria.
	/// </summary>
	private void OFunilArmaORelogio(ServerPlayer pl)
	{
		GD.Print("[alem] -- 2) o funil da morte --");

		pl.RelogioDaMorte = 0;
		long antes = NowMs();
		bool morreu = pl.Combate.Morrer(ignorarSeguro: true);

		AfirmarAlem("`Morrer()` responde que a morte aconteceu", morreu);
		AfirmarAlem("...e ela SOZINHA arma o relogio (o gancho `AoMorrer` esta instalado)",
					pl.RelogioDaMorte >= antes + Alem.MsNoChao, $"{pl.RelogioDaMorte - antes} ms");
		AfirmarAlem("...com o prazo do CADAVER, e nao o do alem (o lugar e o mundo dos vivos)",
					pl.RelogioDaMorte - antes < Alem.MsNoAlem, $"{pl.RelogioDaMorte - antes} ms");

		// E MORRER DE NOVO NAO REARMA. `Morrer()` sai na primeira linha quando `dead` ja e verdadeiro
		// -- sem isso, um segundo golpe num cadaver empurraria a viagem pra frente pra sempre.
		long armado = pl.RelogioDaMorte;
		AfirmarAlem("morrer DE NOVO nao acontece (e nao empurra o prazo pra frente)",
					!pl.Combate.Morrer(ignorarSeguro: true) && pl.RelogioDaMorte == armado);
	}

	// =====================================================================
	// 3) A TRIAGEM -- os corpos que NAO vao
	// =====================================================================
	/// <summary>
	/// ============================ TRES GRUPOS, TRES DESTINOS, PELO CAMINHO DE PRODUCAO ============================
	/// A pergunta e a do <see cref="Gente"/>, e esta familia chama o <see cref="VenceuOPrazoDaMorte"/>
	/// de verdade -- **nao um crivo copiado pra ca**. Copiar seria medir a copia: no dia em que a
	/// triagem mudasse, a bancada continuaria verde falando da regra de ontem.
	///
	/// O QUE ESTA EM JOGO, se ela reprovar: um reflexo da mente apareceria **de pe na mesa do Enma**,
	/// e um cidadao de Namek renasceria na Terra 15 s depois de morrer, pra sempre.
	/// ========================================================================================================
	/// </summary>
	private void ATriagemDosCorpos(ServerPlayer pl, List<ServerPlayer> forjados)
	{
		GD.Print("[alem] -- 3) os corpos que nao vao --");

		if (_moldes?.Get("cidadao") is not { } molde)
		{
			AfirmarAlem("o molde 'cidadao' carregou do npcs.json (sem ele nao ha NPC a triar)", false);
			return;
		}

		ServerPlayer Forjar(int i, string nome)
		{
			var novo = new ServerPlayer
			{
				Id = IdBaseDoAlemDeTeste + i,
				Peer = null,
				Name = nome,
				Race = "Human",
				Genero = "Male",
				Idade = 25,
				Zone = pl.Zone,
				Pos = pl.Pos,
				Conta = $"bancada_alem_{i}",
				Slot = 0,
				Ficha = new Fighter { Race = "Human", BP = 1000 },
				Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			novo.Ficha.Class = "Normal";
			PorNoMundo(novo);
			forjados.Add(novo);
			return novo;
		}

		// --- o NPC DO MUNDO: sai do mundo, nao viaja e nao renasce ---
		ServerPlayer npc = Forjar(1, "bancada: cidadao");
		npc.Papel = new PapelDeNpc(molde, 1);
		npc.Combate.Morrer(ignorarSeguro: true);
		VenceuOPrazoDaMorte(npc);
		AfirmarAlem("o NPC DO MUNDO e enfileirado pra SAIR do mundo", _npcsPraTirar.Contains(npc));
		AfirmarAlem("...e nao foi pro Outro Mundo (nem renasceu)",
					!Alem.EhOAlem(npc.Zone) && npc.Ficha.dead, npc.Zone.Name);
		_npcsPraTirar.Remove(npc);

		// --- o TERCEIRO GRUPO: clone/reflexo/boneco/corpo forjado ---
		ServerPlayer reflexo = Forjar(2, "bancada: reflexo da mente");
		reflexo.Combate.Morrer(ignorarSeguro: true);
		VenceuOPrazoDaMorte(reflexo);
		AfirmarAlem("o REFLEXO (sem dono e sem papel) nao vai pro alem",
					!Alem.EhOAlem(reflexo.Zone), reflexo.Zone.Name);
		AfirmarAlem("...nem renasce vivo na Terra (era o `else` largo do tique antigo)",
					reflexo.Ficha.dead);
		AfirmarAlem("...nem sai do mundo como NPC (quem o ergueu e que cuida dele)",
					!_npcsPraTirar.Contains(reflexo));
		AfirmarAlem("...e o relogio dele nao volta a vencer (o funil nao o reexamina todo tique)",
					reflexo.RelogioDaMorte == long.MaxValue, $"{reflexo.RelogioDaMorte}");

		// ============================ O BONECO DO CORPO LARGADO -- E ELE TEM `Peer` ============================
		// O unico corpo que passa pelas DUAS primeiras pernas do crivo: ele carrega o `Peer` e a Ficha
		// do dono de proposito (pro Sense enxergar quem esta em transe). So a terceira o recusa.
		//
		// EMPRESTAR O `Peer` AQUI E SEGURO porque o destino deste grupo e escrever um numero e mais
		// nada -- nenhum pacote sai. Emprestar num corpo que VIAJASSE mandaria um `ZoneChanged` com o
		// id errado pro cliente do host, e a tela dele trocaria de planeta sozinha.
		// ================================================================================================
		ServerPlayer boneco = Forjar(3, "bancada: boneco largado");
		boneco.DonoDoCorpoLargado = pl.Id;
		boneco.Peer = pl.Peer;
		boneco.Combate.Morrer(ignorarSeguro: true);
		VenceuOPrazoDaMorte(boneco);
		boneco.Peer = null;
		AfirmarAlem("o BONECO DO CORPO LARGADO nao vai pro alem, mesmo com `Peer` na mao",
					!Alem.EhOAlem(boneco.Zone) && boneco.RelogioDaMorte == long.MaxValue,
					$"{boneco.Zone.Name} / {boneco.RelogioDaMorte}");
		AfirmarAlem("...e a razao e a 3a perna do crivo, e nao o `Peer`",
					!Gente.EhJogador(true, null, boneco.DonoDoCorpoLargado));
	}

	// =====================================================================
	// 4) A VIAGEM
	// =====================================================================
	/// <summary>
	/// ============================ O HOST MORRE DE VERDADE E SOBE ============================
	/// A familia central: o percurso inteiro do `Death.dm`, com o corpo de alguem que tem dono na
	/// tela. Ela entra com um BRACO DECEPADO de proposito -- `RegrowLimb` roda ANTES do `loc =`
	/// (`:86-89`), e sem ele a primeira coisa que o jogador veria do alem seria um defeito.
	/// ====================================================================================
	/// </summary>
	private void AViagemAoOutroMundo(ServerPlayer pl)
	{
		GD.Print("[alem] -- 4) a viagem --");

		// (ele ja esta morto: a familia 2 o matou, e o cadaver dela e o ponto de partida desta)
		if (pl.Combate.Corpo.Achar("Braco direito") is { } braco) { braco.Decepado = true; braco.Vida = 0; }
		pl.Combate.SincronizarVida();

		AfirmarAlem("CAIDO no mundo dos vivos: o morto ainda nao esta de pe",
					!pl.MortoDePe && pl.Deitado, $"dePe={pl.MortoDePe} deitado={pl.Deitado}");
		AfirmarAlem("...e a pose que os outros veem e a de nocaute (o cadaver que o port nao tem)",
					pl.Pose(NowMs(), CanalDeKiDe(pl.Id).canal) == Protocol.Pose.Nocauteado);

		// ============================ O CADAVER NAO TEM AUREOLA -- O BUG QUE O DONO FOTOGRAFOU ============================
		// *"o personagem fica com AUREOLA NO CORPO MORTO dele, oq n deveria acontecer. o corpo q fica
		// no MAPA DOS VIVOS deveria ser o EXATO CORPO DELE QUANDO MORRE, sem a aureola"*.
		//
		// A bancada media isto ao contrario: ela AFIRMAVA a aureola no cadaver, porque o crivo antigo
		// era `TemAureola(morto) => morto` e a bancada foi escrita pra descrever o crivo em vez de
		// descrever o DM. Aqui esta a ordem do original, medida: `GenerateCorpse()` no passo 5
		// (`Death.dm:64-67`) copia os overlays de ENTAO; o `overlayList += 'Halo.dmi'` so vem no passo
		// 10 (`:106-108`), na linha de cima do `loc =` (`:110`).
		// ============================================================================================================
		AfirmarAlem("O CADAVER NAO TEM AUREOLA -- ele e o corpo exato de quem caiu (`Death.dm:64-67`)",
					!Alem.TemAureola(pl.Ficha.dead, pl.MorteJaViajou),
					$"morto={pl.Ficha.dead} viajou={pl.MorteJaViajou}");
		pl.EnvAureola = true;               // finge que o fio ja anunciou uma: o tique tem que APAGAR
		TickDasAureolas();
		AfirmarAlem("...e o tique APAGA a auréola de um cadaver que a tivesse (o corpo do dono, na foto)",
					!pl.EnvAureola);

		// E O CRIVO NAO E "sem aureola nunca": a metade que da sentido a de cima. Sem ela, um
		// `TemAureola => false` passaria em todas as linhas desta familia.
		AfirmarAlem("...mas o crivo NAO e um `return false`: o mesmo morto, ja viajado, tem auréola",
					Alem.TemAureola(true, true) && !Alem.TemAureola(false, true));

		ZoneKey antes = pl.Zone;
		long marcado = NowMs();
		PassoDaMorte(pl);   // vence o prazo dos 15 s

		AfirmarAlem("A MORTE LEVA PRO OUTRO MUNDO -- o pedido do dono",
					pl.Zone.Name == Alem.ZonaDoOutroMundo, $"{antes.Name} -> {pl.Zone.Name}");
		AfirmarAlem("...na MESA DO ENMA (`loc = locate(187,104,6)`)",
					Vec2.Distance(pl.Pos, MesaDoEnma(ZoneKey.Premade(Alem.ZonaDoOutroMundo))) < 1f,
					$"{pl.Pos}");
		AfirmarAlem("...e ele continua MORTO (a viagem nao e um revive)", pl.Ficha.dead);
		AfirmarAlem("...DE PE, como o `Un_KO()` do DM (`Death.dm:89`)",
					pl.MortoDePe && !pl.Deitado);
		AfirmarAlem("...INTEIRO: o braco decepado voltou (`RegrowLimb`)",
					pl.Combate.Corpo.Achar("Braco direito") is { Decepado: false });
		AfirmarAlem("...e CURADO (`SpreadHeal(100,1,1)`)", pl.Ficha.HP >= 99.99, $"{pl.Ficha.HP:0.##}");
		AfirmarAlem("...e o voo caiu na travessia (ninguem chega ao alem pairando)",
					!pl.Voando && Mathf.IsZeroApprox(pl.Altitude));

		// ============================ E A AUREOLA ACENDE **NA VIAGEM** -- `Death.dm:106-108` ============================
		// O passo 10 do original, na linha de cima do `loc =`. Repare que **nao houve tique nenhum**
		// entre o `PassoDaMorte` e esta linha: o `EnvAureola` ja saiu verdadeiro porque o bit e escrito
		// ANTES do `MoveToZone`, e quem apresenta a auréola a zona de destino e o `TrocarAureolas` de
		// dentro dele. Se alguem mover a escrita pra depois do `MoveToZone`, esta linha reprova -- e o
		// sintoma em jogo seria a auréola piscando ausente na chegada ate o tique de 5 Hz alcancar.
		// ========================================================================================================
		AfirmarAlem("A AUREOLA ACENDE NA VIAGEM, e nao na morte (`overlayList += 'Halo.dmi'`, `:106-108`)",
					pl.MorteJaViajou && Alem.TemAureola(pl.Ficha.dead, pl.MorteJaViajou));
		AfirmarAlem("...e ela ja foi apresentada a zona de destino SEM esperar o tique (`TrocarAureolas`)",
					pl.EnvAureola);

		// ============================ O RELOGIO DA SEGUNDA ETAPA -- A FAMILIA QUE ACHOU O DEFEITO ============================
		// Sem esta linha o campo ficava com o vencimento dos 15 s, JA PASSADO: o tique seguinte
		// chamava o `PassoDaMorte` de novo, o corpo ja estava no alem, e o `Renascer` disparava 33 ms
		// depois de chegar. O Outro Mundo durava um quadro -- e o log dizia "60s ate voltar a vida".
		// ========================================================================================================
		AfirmarAlem("O PRAZO DO ALEM FOI ARMADO -- ele nao renasce no quadro seguinte",
					pl.RelogioDaMorte >= marcado + Alem.MsNoAlem,
					$"faltam {pl.RelogioDaMorte - NowMs()} ms (esperado ~{Alem.MsNoAlem})");
		AfirmarAlem("...e o funil NAO o tira de la antes da hora",
					NowMs() < pl.RelogioDaMorte);

		// A PROVA DO PRAZO E ELE NAO VENCER: chamar a triagem agora nao pode mover ninguem.
		ZoneKey ondeEstava = pl.Zone;
		if (pl.Ficha.dead && NowMs() >= pl.RelogioDaMorte) VenceuOPrazoDaMorte(pl);
		AfirmarAlem("...conferido pelo caminho do tique: ele continua no Outro Mundo",
					pl.Zone.Hash == ondeEstava.Hash && pl.Ficha.dead, pl.Zone.Name);
	}

	// =====================================================================
	// 5) A AUREOLA NO FIO
	// =====================================================================
	/// <summary>
	/// ============================ POR DIFERENCA, E NAO POR CHAMADA ============================
	/// A tentacao era mandar o pacote de dentro do `AMorteAconteceu` e do `Renascer`. Seriam dois
	/// lugares -- e `Ficha.dead` e escrito DIRETO em pelo menos quatro outros que nao passam por
	/// nenhum dos dois (o restauro da mente, a volta no tempo do G4, o reflexo que nasce vivo, a
	/// gestacao do bio-androide). Esta familia encena justamente esse quinto caminho: escreve `dead`
	/// na unha e cobra o tique.
	/// ======================================================================================
	/// </summary>
	private void AAureolaSaiPorDiferenca(ServerPlayer pl)
	{
		GD.Print("[alem] -- 5) a auréola --");

		// ============================ A AUREOLA E `dead && ja viajou`, E A CONJUNCAO E O DESENHO ============================
		// Ela **nao e mais** `Ficha.dead` sozinho (era, e era o bug do dono -- ver a familia 4). Mas
		// continua sendo uma CONJUNCAO com `dead`, e e dai que vem a propriedade que este port nao quis
		// perder: apagar a morte apaga a auréola sem que caminho nenhum de revive saiba que ela existe.
		// As quatro linhas abaixo sao a tabela verdade inteira do crivo.
		// ============================================================================================================
		AfirmarAlem("morto e ja viajado: TEM auréola", Alem.TemAureola(true, true));
		AfirmarAlem("morto e AINDA CADAVER: nao tem -- o corpo no mapa dos vivos e o exato de quem caiu",
					!Alem.TemAureola(true, false));
		AfirmarAlem("vivo: nao tem, tenha viajado ou nao (a conjuncao e com `dead`, e por isso o "
				  + "revive apaga a auréola sem uma linha propria)",
					!Alem.TemAureola(false, true) && !Alem.TemAureola(false, false));

		pl.EnvAureola = false;
		TickDasAureolas();
		AfirmarAlem("o tique acende a auréola do morto", pl.EnvAureola);

		// E NAO REMANDA. O canal e o do estado LENTO (reliable, so quando muda): um pacote por tique
		// pra carregar um bit que muda duas vezes por vida seria o custo que o snapshot recusou.
		pl.EnvAureola = true;
		TickDasAureolas();
		AfirmarAlem("...e nao remanda nada quando nada mudou", pl.EnvAureola);

		// ============================ O QUINTO CAMINHO: `dead` ESCRITO NA UNHA ============================
		// Nenhum dos caminhos abaixo passa por `Morrer()` nem por `Renascer()`. Se a auréola fosse
		// mandada por chamada, todos eles deixariam o desenho errado -- e o mais visivel deles e o
		// restauro da mente, que acontece toda vez que alguem sai de um transe.
		// ============================================================================================
		pl.Ficha.dead = false;
		TickDasAureolas();
		AfirmarAlem("um revive escrito DIRETO na ficha apaga a auréola (o restauro da mente, o G4)",
					!pl.EnvAureola);

		pl.Ficha.dead = true;
		TickDasAureolas();
		AfirmarAlem("...e uma morte escrita direto a acende (a gestacao do bio-androide)",
					pl.EnvAureola);

		// ============================ E O TIQUE VE A ETAPA, NAO SO A MORTE ============================
		// O mesmo corpo, morto do mesmo jeito, com a viagem desfeita na mao: a auréola tem que APAGAR.
		// E a metade que separa este crivo do antigo -- com `TemAureola(morto) => morto` esta linha era
		// impossivel de escrever, e o cadaver do dono ficava aceso.
		// ==========================================================================================
		pl.MorteJaViajou = false;
		TickDasAureolas();
		AfirmarAlem("...mas um morto que AINDA NAO VIAJOU nao acende (a janela do cadaver)",
					!pl.EnvAureola);
		pl.MorteJaViajou = true;   // devolve a etapa: as linhas abaixo medem o corpo que ja subiu
		TickDasAureolas();

		// QUEM CHEGA NUMA ZONA VE AS AUREOLAS DE LA. Mesma familia de defeito das construcoes, das
		// portas, das feridas e das formas: o pacote existe, sai UMA vez, e quem nao estava presente
		// naquele instante nunca soube.
		pl.EnvAureola = false;
		TrocarAureolas(pl);
		AfirmarAlem("e quem CHEGA na zona e apresentado as auréolas de la", pl.EnvAureola);
	}

	// =====================================================================
	// 6) O QUE SE PODE FAZER MORTO
	// =====================================================================
	/// <summary>
	/// ============================ O MORTO ANDA, VOA, TREINA E LUTA ============================
	/// `move = 1` e escrito QUATRO vezes dentro do proprio `Death()` (`:93,104,114,127`), e o autor
	/// comenta o unico corte de combate: *"so impede Zenkai de luta no Outro Mundo (ja morto, ex:
	/// torneio do ceu)"* (`combatgains.dm:28-33`). O alem nao e uma tela de espera -- e um LUGAR, e
	/// e por isso que o Sr. Kaioh ensina la dentro.
	///
	/// AS TRES NEGATIVAS que o original escreve, e que sao o que faz o lugar custar alguma coisa.
	/// ==================================================================================
	/// </summary>
	private void OQueSePodeFazerMorto(ServerPlayer pl)
	{
		GD.Print("[alem] -- 6) o que se pode fazer morto --");

		AfirmarAlem("no alem o morto esta DE PE -- ele anda (o `move = 1` do `Death()`)",
					Alem.MortoDePe(true, Alem.ZonaDoOutroMundo)
					&& Alem.MortoDePe(true, Alem.ZonaDoCeu)
					&& Alem.MortoDePe(true, Alem.ZonaDoInferno));
		AfirmarAlem("...e FORA dele nao (o cadaver dos 15 s fica no chao)",
					!Alem.MortoDePe(true, "Earth") && !Alem.MortoDePe(false, Alem.ZonaDoOutroMundo));

		// --- SEM ZENKAI: `if(dead) return` (`combatgains.dm:33`) ---
		var cobaia = new Fighter { Race = "Saiyan", BP = 1000, dead = true };
		cobaia.Statify();
		AfirmarAlem("MORTO nao ganha Zenkai (o torneio do ceu nao vira fazenda de BP)",
					cobaia.GainZenkai(1e9, NowMs()) == Fighter.ZenkaiResult.Morto);
		cobaia.dead = false;
		AfirmarAlem("...e vivo ele ganha (senao a linha acima estaria medindo outra coisa)",
					cobaia.GainZenkai(1e9, NowMs()) == Fighter.ZenkaiResult.Concedido);

		// --- SEM DIGERIR, E A ESTAMINA TRAVADA EM 50% (`StaminaDrain.dm:35-36`) ---
		var estomago = new Fighter { Race = "Human", BP = 1000, dead = true };
		estomago.Statify();
		estomago.CurrentNutrition = Jandirus.Core.Stats.Nutricao.Tanque(estomago.Metabolism);
		estomago.stamina = 0;
		(double vigor, double ki) = Jandirus.Core.Stats.Nutricao.Digerir(estomago);
		AfirmarAlem("MORTO nao digere -- a comida nao vira vigor nem Ki",
					Mathf.IsZeroApprox((float)vigor) && Mathf.IsZeroApprox((float)ki));

		Jandirus.Core.Stats.Nutricao.QueimarVigor(estomago);
		AfirmarAlem("...e a estamina trava em METADE do maximo, nem sobe nem desce",
					Math.Abs(estomago.stamina - 0.5 * estomago.maxstamina) < 1e-6,
					$"{estomago.stamina:0.##}/{estomago.maxstamina:0.##}");

		// --- O KI DRENA EM DOBRO (`CargaDeKi`, o ramo de dentro do teto) ---
		// Dois corpos identicos, um vivo e um morto, com o MESMO dt: a diferenca so pode ser o `dead`.
		Fighter vivoKi = FichaDeDrenoDeKi(false), mortoKi = FichaDeDrenoDeKi(true);
		double kiAntes = vivoKi.Ki;
		Jandirus.Core.Combat.CargaDeKi.PrecoDoExcesso(vivoKi, 1.0);
		Jandirus.Core.Combat.CargaDeKi.PrecoDoExcesso(mortoKi, 1.0);
		double drenoVivo = kiAntes - vivoKi.Ki, drenoMorto = kiAntes - mortoKi.Ki;
		AfirmarAlem("o Ki do morto vaza EM DOBRO (o segundo desconto do `if (f.dead)`)",
					drenoVivo > 0 && Math.Abs(drenoMorto - 2 * drenoVivo) < drenoVivo * 0.01,
					$"vivo {drenoVivo:0.####} / morto {drenoMorto:0.####}");
	}

	/// <summary>Duas fichas iguais menos pelo `dead` -- o par de controle do dreno de Ki.</summary>
	private static Fighter FichaDeDrenoDeKi(bool morto)
	{
		var f = new Fighter { Race = "Human", BP = 10_000 };
		f.Statify();

		// ============================ O RAMO TEM QUE SER O DO VAZAMENTO ============================
		// `PrecoDoExcesso` sai na primeira linha com `Ki <= MaxKi` (nao ha excesso a cobrar), e acima
		// do TETO SEGURO (`kicapacity`, que e ABSOLUTA) ele entra nos ramos que MACHUCAM -- e la o
		// dreno e outro, sem a linha do `dead`. O ponto que interessa e o do meio: acima de 100% e
		// dentro do seguro, onde o Ki so vaza devagar. O teto e cravado aqui pra a bancada nao
		// depender do `Statify` de uma raca (`1,3 * MaxKi * log(...)` pode cair de qualquer lado).
		// ======================================================================================
		f.kicapacity = f.MaxKi * 2;
		f.Ki = f.MaxKi * 1.05;
		f.overcharge = false;
		f.dead = morto;
		return f;
	}

	// =====================================================================
	// 7) DO ALEM NAO SE SAI ANDANDO
	// =====================================================================
	/// <summary>
	/// ============================ O `Aging.dm:123`, ESCRITO COMO RECUSA ============================
	/// O original acha o morto fora de lugar e o REBOCA de volta pro checkpoint. Aqui a regra e uma
	/// recusa na porta, porque rebocar depois e o pior dos dois: o jogador chega em Namek, ve o
	/// cenario carregar, e e puxado de volta -- que le como bug, e nao como regra.
	///
	/// E A OUTRA METADE DA MESMA FAMILIA: o morto PODE atravessar DENTRO do alem. A guarda antiga
	/// (`if (pl.Ficha.dead || pl.Ficha.KO) continue`) apagava o Ceu e o Inferno do jogo sem que nada
	/// reclamasse -- e o proprio `Alem.MortoDePe` ja descrevia, por escrito, um caso que ela tornava
	/// impossivel.
	/// ==========================================================================================
	/// </summary>
	private void DoAlemNaoSeSaiAndando(ServerPlayer pl)
	{
		GD.Print("[alem] -- 7) nao sair do alem --");

		// --- as passagens do alem, como estao no disco ---
		foreach (string nome in new[] { Alem.ZonaDoOutroMundo, Alem.ZonaDoCeu, Alem.ZonaDoInferno })
		{
			if (!_passagens.TryGetValue(nome, out List<Passagem>? lista) || lista.Count == 0)
			{
				AfirmarAlem($"'{nome}' tem passagem (senao o lugar e um beco sem saida)", false);
				continue;
			}
			AfirmarAlem($"todas as passagens de '{nome}' levam a outra zona do ALEM",
						lista.TrueForAll(p => Alem.EhOAlem(p.Zona)),
						string.Join(", ", lista.ConvertAll(p => p.Zona)));
		}

		// O CEU E O INFERNO SAO ALCANCAVEIS a pe a partir do Outro Mundo -- e e por isso que os cacos
		// de revivencia, o bonus do Kai no Ceu e o do Demonio no Inferno tem onde acontecer.
		if (_passagens.TryGetValue(Alem.ZonaDoOutroMundo, out List<Passagem>? doZ6))
		{
			AfirmarAlem("do Outro Mundo se alcanca o Ceu E o Inferno a pe",
						doZ6.Exists(p => p.Zona == Alem.ZonaDoCeu)
						&& doZ6.Exists(p => p.Zona == Alem.ZonaDoInferno));
		}

		// --- a recusa em si, com uma passagem que os mapas de hoje nao produzem ---
		// ELA E DEFENSIVA: nenhuma passagem existente sai do alem. E exatamente por isso que ela
		// precisa de bancada -- uma regra que nunca dispara e uma regra que ninguem ve apodrecer.
		var fuga = new Passagem { X = 0, Y = 0, Zona = SpawnZone.Name, Dx = 0, Dy = 0, Nome = "a Terra" };
		EscutaDeAvisos?.Clear();
		AfirmarAlem("o MORTO e recusado numa passagem que sairia do alem",
					OAlemNaoDeixaSair(pl, fuga));
		AfirmarAlem("...e ele LE por que (a recusa fala em estar morto)",
					string.Join(" | ", EscutaDeAvisos ?? []).Contains("morto"),
					string.Join(" | ", EscutaDeAvisos ?? []));

		var interna = new Passagem { X = 0, Y = 0, Zona = Alem.ZonaDoInferno, Dx = 0, Dy = 0, Nome = "Inferno" };
		AfirmarAlem("...mas NAO e recusado indo pro Inferno (a travessia de dentro continua livre)",
					!OAlemNaoDeixaSair(pl, interna));

		bool eraMorto = pl.Ficha.dead;
		pl.Ficha.dead = false;
		AfirmarAlem("...e o VIVO passa pelas duas (a regra e sobre estar morto, nao sobre o lugar)",
					!OAlemNaoDeixaSair(pl, fuga));
		pl.Ficha.dead = eraMorto;

		// --- e nao se decola de la ---
		EscutaDeAvisos?.Clear();
		Decolar(pl);
		AfirmarAlem("nao se DECOLA do Outro Mundo (ele nao fica no mapa do universo)",
					pl.Zone.Name == Alem.ZonaDoOutroMundo, pl.Zone.Name);
	}

	// =====================================================================
	// 8) A VOLTA -- o `ReviveMe()` do DM
	// =====================================================================
	/// <summary>
	/// ============================ A VOLTA DESFAZ TUDO O QUE A MORTE FEZ ============================
	/// `mob/proc/ReviveMe()` (`Death.dm:143-161`). A conferencia e item a item contra a lista do
	/// <see cref="Renascer"/>: o que a morte escreveu tem que sair, e o que ela levou tem que voltar.
	///
	/// **A AUREOLA NAO TEM LINHA PROPRIA AQUI, E ISSO E O TESTE.** Ela e `dead && ja viajou` -- uma
	/// CONJUNCAO com a morte, e nao um bit ao lado dela --, entao apagar a morte apaga o desenho e
	/// nenhum caminho de revive precisa lembrar da auréola. Se um dia alguem trocar a conjuncao por
	/// um segundo bit paralelo (que o `Renascer` teria que apagar a mao), esta linha e a que reprova.
	///
	/// O QUE O DM FAZ E ESTE PORT AINDA NAO: `kaiTrainingAllowed = 0`, `hakai_mark = 0`,
	/// `pk_karma_taken = 0`, `pk_rep_taken = 0`. Os quatro campos **nao existem aqui** -- nascem com
	/// o Enma e com o karma por PK. Ficam citados pra a ausencia ser pendencia, e nao esquecimento.
	/// ==========================================================================================
	/// </summary>
	private void AVoltaDesfazAMorte(ServerPlayer pl)
	{
		GD.Print("[alem] -- 8) a volta --");

		// O PRAZO VENCE AGORA. E o unico atalho da bancada: o percurso e o de producao, o que ela
		// pula sao os 60 s de relogio de parede.
		pl.RelogioDaMorte = 0;
		if (pl.Combate.Corpo.Achar("Perna esquerda") is { } perna) { perna.Decepado = true; perna.Vida = 0; }
		pl.Combate.SincronizarVida();

		VenceuOPrazoDaMorte(pl);

		AfirmarAlem("VENCIDO O PRAZO, ele volta a vida", !pl.Ficha.dead && !pl.Ficha.KO);
		AfirmarAlem("...e sai do Outro Mundo (o berco, ou o dominio escolhido)",
					!Alem.EhOAlem(pl.Zone), pl.Zone.Name);
		AfirmarAlem("...inteiro: a perna decepada voltou (`Corpo.Restaurar` dentro do `Reviver`)",
					pl.Combate.Corpo.Achar("Perna esquerda") is { Decepado: false });
		AfirmarAlem("...com METADE da vida -- a divergencia declarada do port (o DM cura 100%)",
					pl.Ficha.HP > 40 && pl.Ficha.HP < 60, $"{pl.Ficha.HP:0.##}");
		AfirmarAlem("...e METADE do Ki", pl.Ficha.Ki <= pl.Ficha.MaxKi * 0.5 + 1e-6);
		AfirmarAlem("...e o relogio da morte zerado (senao o funil reexaminaria um corpo vivo)",
					pl.RelogioDaMorte == 0);

		TickDasAureolas();
		AfirmarAlem("E A AUREOLA SOME SOZINHA -- ela nao tem linha propria no revive",
					!pl.EnvAureola);
	}
}
