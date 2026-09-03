using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// LOTE G8 -- OS VERBOS MUDOS DOS CARGOS.
///
/// ============================ O PEDIDO, E O QUE SOBROU DELE ============================
/// *"acho q chegou a hora de colocar TODOS OS VERBS MUDOS DOS RANKS q ainda n estao ativados, vendo
/// como funcionavam no byond"*. O levantamento anterior contou 50 verbos de cargo, 19 vivos e 30
/// mudos. Este lote fecha **seis** deles, e os seis tem a mesma propriedade: o corpo do verb no DM
/// nao depende de sistema nenhum que o port nao tenha.
///
/// A regra que decidiu quem entra e a 6 do pedido do dono -- *"se um verbo depender de sistema que
/// nao existe, NAO o entregue pela metade"*. Os 24 que ficaram de fora estao nomeados no relatorio
/// desta sessao com o sistema que falta em cada um; nao ha nenhum aqui "quase pronto".
/// ====================================================================================
///
/// ============================ AS SEIS, E DE ONDE CADA UMA SAIU ============================
///     verbo                    cargo(s)                                   fonte no DM
///     -----------------------  -----------------------------------------  ----------------------------------------
///     Dead                     10 cargos (todo kit do Outro Mundo, o       `OtherworldRankSkills.dm:212-215`
///                              Guardiao da Terra e os Anciaos de Namek)
///     Go_To_Heaven_Or_Hell     King Yemma (skill `Judge`)                 `OtherworldRankSkills.dm:188-193`
///     Holy_Shortcut            Arconian Guardian                          `ordered/SpaceRanks.dm:59-84`
///     Detect_Shard             Arconian Guardian                          `ordered/SpaceRanks.dm:110-112`
///     Keep_Body                9 cargos                                   `OtherworldRankSkills.dm:195-202`
///     Restore_Youth            Grand Kai, Demon Lord                      `OtherworldRankSkills.dm:163-174`
/// ====================================================================================
///
/// ============================ TRES DELES ESTAVAM MAL ETIQUETADOS, E ISSO ADIOU O PORTE ============================
/// O censo (`CensoDeSkills.SistemaQueFalta`) respondia, sobre estes tres, um sistema que eles nao
/// precisam -- e enquanto a etiqueta dizia "falta o sistema X", ninguem ia olhar o corpo do verb:
///
///   * **`Detect_Shard`** vinha etiquetado como *"esferas do dragao: fragmento, radar e estatua"*. O
///     corpo inteiro do verb no DM e **uma linha de texto**: *"The Master Emerald is no more; there
///     is nothing to detect."* (`SpaceRanks.dm:110-112`). E uma piada do autor, nao um radar;
///   * **`Go_To_Heaven_Or_Hell`** vinha como *"burocracia do alem"*. E um `switch` de dois destinos,
///     e as duas zonas ja existem neste port (`Alem.ZonaDoCeu`, `Alem.ZonaDoInferno`). **A descricao
///     da skill `Judge` mente** (*"Choose whether or not someone is sent to Heaven or Hell"*): o verb
///     nao toca em ninguem, ele TELEPORTA O PROPRIO YEMMA;
///   * **`Holy_Shortcut`** idem. E um teleporte de ida e volta que cobra metade do Ki.
///
/// Fica escrito porque o defeito nao foi o porte estar dificil -- foi a etiqueta estar errada, e uma
/// etiqueta errada e mais cara que uma pendencia, porque ela para de ser revisitada.
/// ==============================================================================================================
///
/// ============================ E O `Keep_Body` ACENDE UM CAMPO QUE JA TINHA CONSUMIDORES ESPERANDO ============================
/// Tres lugares deste port ja citavam `KeepsBody` em comentario, com a regra do DM copiada e a nota
/// "no dia em que for portado" (`GameServer.Esmagamento.cs:57` e `:77`, `GameServer.Combat.cs:283`),
/// e `Alem.TemAureola` escolheu um crivo TEMPORAL em vez de um por lugar **precisamente** pra nao
/// plantar um defeito pra este dia. O campo entrou (`Fighter.KeepsBody`) e os tres passaram a le-lo.
/// ==========================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// 0. O REGISTRO E O DESPACHO
	// =====================================================================
	/// <summary>
	/// AS SEIS TECNICAS DESTE LOTE. Chamado do `RegistrarTecnicas` junto dos outros lotes.
	///
	/// AS LINHAS-ESPELHO ESTAO EM `Core/Skills/Tecnicas.Portadas.cs`, e nao e opcional: a
	/// `--catalogoteste` compara as duas bocas nas duas direcoes toda rodada, e foi ela que pegou o
	/// lote G7 inteiro faltando la. Ver o cabecalho de `Tecnicas.DesencontroDoEspelho`.
	/// </summary>
	private void RegistrarTecnicasG8()
	{
		IniciarLote("G8");
		Vivo("Dead", VerOsMortosG8);
		Vivo("Go_To_Heaven_Or_Hell", JulgarG8);
		Vivo("Holy_Shortcut", AtalhoSagradoG8);
		Vivo("Detect_Shard", DetectarEsmeraldaG8);
		Vivo("Keep_Body", ManterOCorpoG8);
		Vivo("Restore_Youth", OferecerJuventudeG8);
	}

	// =====================================================================
	// 1. DEAD -- `OtherworldRankSkills.dm:212-215`
	// =====================================================================
	/// <summary>
	/// QUEM ESTA MORTO AGORA.
	///
	///     mob/Admin1/verb/Dead()
	///         for(var/mob/M) if(M.dead) to_chat(usr, "&lt;font color=green&gt;[M] is dead.")
	///
	/// O laco do DM e `for(var/mob/M)` -- **todo mob do mundo**, jogador ou nao, em qualquer z. Aqui
	/// o crivo e o <see cref="EhJogador"/> e nao "todo corpo", e a diferenca merece estar escrita:
	/// este port povoa os planetas com NPCs clientless (`GameServer.Populacao.cs`) e cria clones,
	/// reflexos e cadaveres que sao `ServerPlayer` de verdade. Listar tudo devolveria centenas de
	/// linhas de gente que nao existe pro jogador -- e o verb serve pra achar ALGUEM.
	///
	/// A ORDEM E ALFABETICA e nao a da lista interna: a do DM e a ordem de criacao dos mobs, que e
	/// aleatoria pro leitor. Uma lista que muda de ordem a cada uso e uma lista que nao da pra ler.
	/// </summary>
	private void VerOsMortosG8(ServerPlayer pl)
	{
		var mortos = new List<string>();
		foreach (ServerPlayer o in _players.Values)
			if (EhJogador(o) && o.Ficha.dead) mortos.Add($"{o.Name} ({o.Zone.Name})");

		if (mortos.Count == 0) { Avisar(pl, "ninguem esta morto no mundo agora."); return; }

		mortos.Sort(StringComparer.OrdinalIgnoreCase);
		Avisar(pl, $"-- os mortos ({mortos.Count}) --");
		foreach (string m in mortos) Avisar(pl, $"  {m}");
	}

	// =====================================================================
	// 2. JUDGE / GO_TO_HEAVEN_OR_HELL -- `OtherworldRankSkills.dm:188-193`
	// =====================================================================
	/// <summary>
	/// ============================ A DESCRICAO DA SKILL MENTE, E O VERB E OUTRA COISA ============================
	/// A `desc` de `/datum/skill/rank/Judge` promete *"Choose whether or not someone is sent to Heaven
	/// or Hell"* (`OtherworldRankSkills.dm:106`). O verb que ela concede nao toca em ninguem:
	///
	///     mob/Rank/verb/Go_To_Heaven_Or_Hell()
	///         switch(input("Go to Planet", "", text) in list ("Hell","Heaven","None",))
	///             if("Hell")   loc=locate(64,290,9)
	///             if("Heaven") loc=locate(175,140,10)
	///
	/// Ele TELEPORTA O PROPRIO YEMMA, e mais nada. Portado o que o codigo faz, e nao o que a descricao
	/// promete -- e o texto da tecnica neste port descreve o codigo. **Nao "conserte" isto pra bater
	/// com a `desc`**: mandar alguem pro Inferno e o `enma_judge_to_hell()` do NPC (`SkyNPCs.dm:176`),
	/// que e outro sistema e ja tem dono.
	/// ==========================================================================================================
	///
	/// O `input()` bloqueante virou argumento, como no `RiftTeleport` do lote G4 e pelo mesmo motivo:
	/// num servidor autoritativo nao existe "travar o jogador esperando uma caixa". Sem argumento, o
	/// verb LISTA -- que e o `None` do menu do DM, so que informativo.
	/// </summary>
	private void JulgarG8(ServerPlayer pl, string destino)
	{
		if (pl.Ficha.KO) { Avisar(pl, "nao da, caido."); return; }

		string alvo = destino.Trim() switch
		{
			var s when s.Equals("ceu", StringComparison.OrdinalIgnoreCase)
					   || s.Equals("céu", StringComparison.OrdinalIgnoreCase)
					   || s.Equals("heaven", StringComparison.OrdinalIgnoreCase) => Alem.ZonaDoCeu,
			var s when s.Equals("inferno", StringComparison.OrdinalIgnoreCase)
					   || s.Equals("hell", StringComparison.OrdinalIgnoreCase) => Alem.ZonaDoInferno,
			_ => "",
		};

		if (alvo.Length == 0)
		{
			Avisar(pl, "o juiz vai onde quiser: Go_To_Heaven_Or_Hell:ceu ou Go_To_Heaven_Or_Hell:inferno.");
			return;
		}
		if (string.Equals(alvo, pl.Zone.Name, StringComparison.Ordinal))
		{
			Avisar(pl, $"voce ja esta em {alvo}.");
			return;
		}
		if (_catalogo?.Get(alvo) == null) { Avisar(pl, $"{alvo} nao tem mapa carregado neste servidor."); return; }

		// AS COORDENADAS SAO AS DO DM (`locate(64,290,9)` e `locate(175,140,10)`), convertidas na hora
		// pela altura REAL do mapa -- ver `CoordenadaDoDmG8`, e o mesmo argumento da `MesaDoEnma`.
		ZoneKey z = ZoneKey.Premade(alvo);
		Vec2 ponto = string.Equals(alvo, Alem.ZonaDoInferno, StringComparison.Ordinal)
			? CoordenadaDoDmG8(z, 64, 290)
			: CoordenadaDoDmG8(z, 175, 140);

		Falar(pl, Protocol.Fala.Emote, "bate o carimbo e some do balcao.");
		MoveToZone(pl.Id, z, ponto);
		Avisar(pl, $"voce atravessa pro {alvo}.");
		GD.Print($"[server] {pl.Name} (juiz) foi pra {alvo}");
	}

	// =====================================================================
	// 3. HOLY SHORTCUT -- `ordered/SpaceRanks.dm:59-84`
	// =====================================================================
	/// <summary>
	/// O REINO DIVINO DO PORT E O `z31` DO DM. `loc=locate(44,210,31)` e o Holy Summit do original, e
	/// o z31 convertido deste port se chama `God_Realm` (`Assets/Maps/manifest.json`, z31, 255x255).
	/// Nome diferente, mesmo mapa -- e por isso a constante existe em vez de a string estar solta em
	/// duas linhas: o dia em que a zona for renomeada, e um lugar so.
	///
	/// **COM UNDERLINE, e nao "God Realm"**: quem responde ao `_catalogo.Get` e ao `MapaDaZonaOuCatalogo`
	/// e o nome do MANIFESTO DOS MAPAS, nao o do `planetas.json` (que escreve "God Realm" com espaco,
	/// como escreve "Small Space Station" pro `Small_Space_Station` que o `RiftTeleport` ja usa). A
	/// primeira versao desta linha usou o nome com espaco e a `--catalogoteste` reprovou na hora, com a
	/// frase exata: *"God Realm nao tem mapa carregado neste servidor"*.
	/// </summary>
	private const string ZonaDoCumeSagradoG8 = "God_Realm";

	/// <summary>O outro lado do atalho -- `loc=locate(340,270,5)`, o z5 do DM.</summary>
	private const string ZonaDeArconiaG8 = "Arconia";

	/// <summary>
	/// HOLY SHORTCUT -- o unico salto que NAO exige Ki cheio: `usr.Ki >= (usr.MaxKi/2)` pra sair e
	/// `Ki /= 2` de preco, e o unico que cai em coordenadas proprias do DM em vez do spawn. A recusa
	/// do DM era uma frase so pros quatro casos (*"You lack the sufficient Ki required to chuckle.
	/// Either that or you refused to stand still..."*); aqui cada porta fala pela `PodeSaltarDePlaneta`.
	/// </summary>
	private void AtalhoSagradoG8(ServerPlayer pl, string destino)
	{
		// OS DOIS LADOS DO ATALHO, com os apelidos que a mao digita: "cume"/"summit" e "arconia".
		string canonico = destino.Trim() switch
		{
			var s when s.Equals("cume", StringComparison.OrdinalIgnoreCase)
					   || s.Equals("summit", StringComparison.OrdinalIgnoreCase) => ZonaDoCumeSagradoG8,
			var s when s.Equals("arconia", StringComparison.OrdinalIgnoreCase) => ZonaDeArconiaG8,
			var s => s,
		};

		if (!SaltarDePlaneta(pl, canonico, [ZonaDoCumeSagradoG8, ZonaDeArconiaG8], "o atalho", "Holy_Shortcut",
							 kiCheio: false, comCarona: true, emote: "cruza os bracos e da um sorrisinho...",
							 fraseDaCarona: "te leva junto usando... risadinha?",
							 pontoDeChegada: z => string.Equals(z.Name, ZonaDoCumeSagradoG8, StringComparison.Ordinal)
								 ? CoordenadaDoDmG8(z, 44, 210)
								 : CoordenadaDoDmG8(z, 340, 270),
							 out string alvo, out int levou))
			return;

		Avisar(pl, levou == 0
			? $"voce da uma risadinha e reaparece em {alvo}."
			: $"voce da uma risadinha e reaparece em {alvo}, com {levou} pessoa{(levou == 1 ? "" : "s")} a tiracolo.");
		GD.Print($"[server] {pl.Name} usou o Atalho Sagrado pra {alvo} (+{levou} de carona)");
	}

	// =====================================================================
	// 4. DETECT SHARD -- `ordered/SpaceRanks.dm:110-112`
	// =====================================================================
	/// <summary>
	/// A ESMERALDA NAO EXISTE MAIS, E ESSE E O VERB INTEIRO.
	///
	///     mob/Rank/verb/Detect_Shard()
	///         set category="Skills"
	///         to_chat(usr, "The Master Emerald is no more; there is nothing to detect.")
	///
	/// Nao ha segunda linha. A `desc` da skill descreve um radar de coordenadas
	/// (`SpaceRanks.dm:87-88`) que o autor nunca escreveu -- e a piada e justamente essa. Este port
	/// entrega o verb como ele e: **e um verbo VIVO**, ele responde, e a resposta e a do original.
	///
	/// Ele estava catalogado como "falta o sistema das esferas do dragao", que e a etiqueta mais cara
	/// possivel pra uma linha de texto: enquanto ela estivesse la, ninguem abriria o arquivo.
	/// </summary>
	private void DetectarEsmeraldaG8(ServerPlayer pl) =>
		Avisar(pl, "a Esmeralda Mestra nao existe mais; nao ha nada a detectar.");

	// =====================================================================
	// 5. KEEP BODY -- `OtherworldRankSkills.dm:195-202`
	// =====================================================================
	/// <summary>
	/// LIGA E DESLIGA, EM OUTRA PESSOA, O DIREITO DE FICAR COM O CORPO.
	///
	///     mob/Rank/verb/Keep_Body(mob/M in view(src))
	///         if(!M.KeepsBody) M.KeepsBody=1 ... else M.KeepsBody=0
	///
	/// E um toggle puro, sem custo, sem alcance util (`view(src)` e a tela inteira) e sem pedir
	/// licenca a quem recebe. Aqui o alvo e o MARCADO (duplo clique), que e a convencao deste port
	/// pra todo verb com alvo -- ver `Client/VerbosDoJogo.NoAlvo`.
	///
	/// O QUE O BIT FAZ esta documentado em <see cref="Jandirus.Core.Stats.Fighter.KeepsBody"/> e
	/// aplicado em dois lugares que ja o esperavam por escrito: o esmagamento por gravidade
	/// (`GameServer.Esmagamento.cs`) e a viagem pro Outro Mundo (`GameServer.Alem.PassoDaMorte`).
	///
	/// ELE VAI PRO DISCO NA HORA (`Persistir`): e um estado que o outro jogador nao pediu e nao pode
	/// desfazer, entao perde-lo num reinicio do servidor seria pior do que nao te-lo -- o Kaio teria
	/// que refazer o favor a cada queda, sem saber que precisava.
	/// </summary>
	private void ManterOCorpoG8(ServerPlayer pl)
	{
		ServerPlayer? alvo = PorNome(pl.AlvoId.ToString());
		if (alvo == null || !EhPessoa(alvo))
		{
			Avisar(pl, "marque alguem antes (duplo clique nele).");
			return;
		}

		bool ligado = !alvo.Ficha.KeepsBody;
		alvo.Ficha.KeepsBody = ligado;
		Persistir(alvo);

		Avisar(pl, ligado
			? $"voce concede a {alvo.Name} o direito de ficar com o proprio corpo depois de morto."
			: $"voce revoga o direito de {alvo.Name} de ficar com o corpo depois de morto.");
		Avisar(alvo, ligado
			? $"{pl.Name} te concede o direito de manter o seu corpo depois da morte -- voce nao sera "
			  + "mais arrancado do mundo dos vivos, so chamado de volta quando a energia acabar."
			: $"{pl.Name} revoga o seu direito de manter o corpo depois da morte.");
		GD.Print($"[server] {pl.Name} pos KeepsBody={ligado} em {alvo.Name}");
	}

	// =====================================================================
	// 6. RESTORE YOUTH -- `OtherworldRankSkills.dm:163-174`
	// =====================================================================
	/// <summary>A oferta de juventude em aberto: conta do alvo -> (quem ofereceu, idade, ate quando).</summary>
	private readonly Dictionary<string, (string Quem, int Idade, long Ate)> _ofertasDeJuventudeG8 =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// QUANTO TEMPO UMA OFERTA FICA DE PE. O DM nao tem prazo -- o `input()` dele PRENDE o alvo ate
	/// ele responder. Aqui a oferta e assincrona (como o convite de Anciao), e uma oferta sem prazo
	/// vira uma promessa que ninguem lembra de ter feito.
	/// </summary>
	private const long PrazoDaOfertaDeJuventudeMsG8 = 60_000;

	/// <summary>
	/// A IDADE MAXIMA QUE O DOM ALCANCA. `if(age>25) age=25` (`OtherworldRankSkills.dm:169`) -- e o
	/// piso e 0 pelo `if(age<0) age=0` da linha anterior.
	/// </summary>
	private const int IdadeMaximaDoDomG8 = 25;

	/// <summary>
	/// OFERECE A JUVENTUDE A QUEM ESTA MARCADO.
	///
	/// ============================ O `input()` NO ALVO VIRA OFERTA, E ISSO E O PORTE ============================
	/// O DM abre uma caixa **no alvo** (`switch(input(M, "(Offerer=[usr]) Do you want your age to be
	/// restored to [age] years?", ...)`) e so escreve a idade se a resposta for "Yes". Ou seja: **o
	/// consentimento faz parte da tecnica**, e nao e enfeite -- idade mexe em BP neste port
	/// (`Fighter.AgeDiv`, `Envelhecimento.DivisorDeIdade`), entao um Grand Kai poderia rejuvenescer um
	/// desafeto pra derrubar o poder dele.
	///
	/// Num servidor autoritativo nao ha caixa bloqueante, entao a oferta vira estado com prazo -- o
	/// mesmo desenho (e pelo mesmo argumento) do convite de Anciao em `GameServer.CargoPortas.cs`.
	/// A resposta chega pelos verbs `juventude_aceitar` / `juventude_recusar`.
	///
	/// A OFERTA E POR CONTA e nao por id de sessao: quem esta escolhendo pode deslogar e voltar dentro
	/// do minuto, e o id de sessao e reciclado (a `--arsenalteste` ja afirma isso sobre a paralisia).
	/// ========================================================================================================
	/// </summary>
	private void OferecerJuventudeG8(ServerPlayer pl, string arg)
	{
		ServerPlayer? alvo = PorNome(pl.AlvoId.ToString());
		if (alvo == null || !EhPessoa(alvo) || alvo == pl)
		{
			Avisar(pl, "marque quem voce quer rejuvenescer (duplo clique nele).");
			return;
		}

		if (!int.TryParse(arg.Trim(), out int idade))
		{
			Avisar(pl, $"a que idade? Use Restore_Youth:<idade> (0 a {IdadeMaximaDoDomG8}).");
			return;
		}
		idade = Math.Clamp(idade, 0, IdadeMaximaDoDomG8);   // `if(age<0) age=0` / `if(age>25) age=25`

		_ofertasDeJuventudeG8[alvo.Conta] = (pl.Conta, idade, NowMs() + PrazoDaOfertaDeJuventudeMsG8);
		Avisar(pl, $"voce oferece a {alvo.Name} voltar aos {idade} anos. Agora e com {alvo.Name}.");
		Avisar(alvo, $"{pl.Name} oferece devolver voce aos {idade} anos de idade. "
				   + $"(aceite ou recuse na aba Other, em {PrazoDaOfertaDeJuventudeMsG8 / 1000}s)");
	}

	/// <summary>
	/// A RESPOSTA DO ALVO -- o `switch(input(M, ...))` do DM, do lado de quem recebe.
	///
	/// `M.Age=age` E `M.Body=age`, as duas linhas do original (`:172-173`). Neste port a idade e um
	/// campo so em dois lugares (`ServerPlayer.Idade`, que e o que vai pro save, e `Fighter.Idade`,
	/// que e o que a conta de BP le) -- e os DOIS tem que ser escritos, senao o poder e a ficha
	/// contam idades diferentes ate o proximo login. E o `Body` do DM e o mesmo par.
	/// </summary>
	private void ResponderJuventudeG8(ServerPlayer pl, bool aceitou)
	{
		if (!_ofertasDeJuventudeG8.TryGetValue(pl.Conta, out (string Quem, int Idade, long Ate) o)
			|| NowMs() > o.Ate)
		{
			_ofertasDeJuventudeG8.Remove(pl.Conta);
			Avisar(pl, "ninguem te ofereceu juventude nenhuma (ou a oferta ja venceu).");
			return;
		}
		_ofertasDeJuventudeG8.Remove(pl.Conta);

		ServerPlayer? ofertante = OnlinePorConta(o.Quem);
		if (!aceitou)
		{
			Avisar(pl, "voce recusa a oferta.");
			if (ofertante != null) Avisar(ofertante, $"{pl.Name} recusou a sua oferta.");
			return;
		}

		pl.Idade = o.Idade;
		pl.Ficha.Idade = o.Idade;
		pl.Ficha.Statify();
		AplicarPoderes(pl);
		Persistir(pl);

		foreach (ServerPlayer v in ZoneList(pl.Zone.Hash))
			Avisar(v, $"os anos escorrem de {pl.Name}: o corpo dele volta a ter {o.Idade}.");
		if (ofertante != null && ofertante.Zone.Hash != pl.Zone.Hash)
			Avisar(ofertante, $"{pl.Name} aceitou, e voltou aos {o.Idade} anos.");
		GD.Print($"[server] {pl.Name} rejuvenesceu pra {o.Idade} anos (oferta de {o.Quem})");
	}

	// =====================================================================
	// A COORDENADA DO DM, CONVERTIDA
	// =====================================================================
	/// <summary>
	/// UM `locate(x, y, z)` DO BYOND VIRA UM PONTO DESTE PORT.
	///
	/// Mesma conta (e mesmo argumento) da <see cref="Alem.MesaDoEnma"/>: o BYOND conta o Y de baixo
	/// pra cima e comeca em 1, este port conta de cima pra baixo e comeca em 0. **A altura sai do
	/// MAPA carregado e nao de um numero cravado**, senao a conversao mentiria calada no dia em que a
	/// zona fosse reconvertida com outro tamanho.
	///
	/// E o resultado passa pelo `PontoLivrePerto` pela mesma razao da mesa do Enma: um mapa
	/// convertido tem parede onde o `.dmm` tinha, e uma construcao levantada em cima do ponto o
	/// bloqueia em runtime. Chegar dentro de uma parede e a maneira mais boba de prender alguem.
	/// </summary>
	private Vec2 CoordenadaDoDmG8(ZoneKey zona, int bx, int by)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		int alturaEmTiles = mapa?.Height ?? 500;
		const int t = ZoneCollision.TileSize;
		int cx = Math.Max(bx - 1, 0);
		int cy = Math.Clamp(alturaEmTiles - by, 0, Math.Max(0, alturaEmTiles - 1));
		var p = new Vec2(cx * t + t / 2f, cy * t + t / 2f);
		return mapa?.PontoLivrePerto(p) ?? p;
	}
}
