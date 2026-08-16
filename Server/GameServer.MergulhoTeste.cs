using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ A SUPERFICIE DE BANCADA DO MERGULHO (`--diagmergulho`) ============================
/// Este arquivo nao roda bancada nenhuma: ele e o que o robo do CLIENTE (`Client/RoboDoMergulho.cs`)
/// precisa alcancar do lado da AUTORIDADE, e nada mais. Cada metodo aqui e uma de duas coisas, e o
/// nome diz qual:
///
///   * `...NoTeste` -- uma PERGUNTA ao servidor (estou na mente? ha onda correndo? onde esta o
///     reflexo?) ou um ACONTECIMENTO que o jogador sozinho nao encomenda (o reflexo morre; alguem
///     soca o corpo largado). Todos entram pelo funil de producao;
///   * `Defeito...` -- um DEFEITO INJETADO, e cada um deles e uma linha que EXISTIU neste servidor
///     antes da rodada da gota. Eles estao aqui, e nao no cliente, porque o que eles desfazem e do
///     servidor: a fila da onda.
///
/// ============================ POR QUE ELES MORAM NUMA BANCADA, E NAO NO JOGO ============================
/// Porque a alternativa e um `if (modoDeTeste)` no caminho de producao, e este port ja registrou o
/// preco disso: a segunda copia do caminho e a que discorda um dia. Nada aqui afrouxa uma regra --
/// `EntrarNaMente`, `SairDaMente`, `ComecarAVoltaDaMente` e `MarcarAgressao` sao os metodos do jogo,
/// chamados como o jogo os chama. O que a bancada ganha e um GATILHO, nunca uma excecao.
///
/// ============================ E POR QUE O CLIENTE E QUEM PERGUNTA ============================
/// Porque as tres coisas que esta rodada precisa provar -- a tela ondula, ela ondula ANTES da viagem,
/// e ela NAO ondula quando arrancam voce do transe -- so existem inteiras quando ha um `Peer` de
/// verdade: `LargarOCorpo` recusa corpo forjado (*"corpo sem dono nao tem atencao pra mandar
/// embora"*), e a onda e um pacote (`MandarEfeito`), que num corpo sem `Peer` e um `?.` que nao faz
/// nada. O robo roda com `--host`: um processo so, um cliente de verdade, e o servidor aqui do lado.
/// ==========================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Faixa de ids desta bancada -- acima da `--menteviva` (91.500), que era a maior.</summary>
	private const int IdBaseDoMergulho = 92_500;

	// =====================================================================
	// AS PERGUNTAS
	// =====================================================================
	/// <summary>
	/// ESTE JOGADOR ESTA DENTRO DE UMA MENTE? -- pelo LUGAR, que e a unica verdade sobre isso
	/// (ver o cabecalho de `Core/World/DimensaoMental.cs`).
	///
	/// O cliente sabe a propria zona e poderia responder sozinho; ele pergunta ao servidor de
	/// proposito, porque a familia que usa isto afirma uma ORDEM entre o que a tela faz e o que a
	/// AUTORIDADE decidiu. Comparar a tela com ela mesma seria o cego registrado deste projeto --
	/// *"as duas telas concordam" fica verde com as duas erradas igual*.
	/// </summary>
	internal bool NaMenteNoTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) && NaMente(pl);

	/// <summary>Ha uma onda correndo pra esta pessoa? -- a fila `_ondaDaMente`, derivada e nao um bit.</summary>
	internal bool NaOndaNoTeste(int id) => NaOnda(id);

	/// <summary>Este corpo esta MEDITANDO na conta do servidor (`Ficha.med`)? -- a meditacao normal.</summary>
	internal bool MeditandoNoTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) && pl.Ficha.med;

	/// <summary>O corpo largado desta pessoa existe (ela esta fora do corpo)?</summary>
	internal bool ForaDoCorpoNoTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) && pl.BonecoLargado != null;

	/// <summary>
	/// ONDE ESTA O CORPO QUE A MENTE ERGUEU, em pixels -- nulo quando nao ha nenhum.
	///
	/// A familia da COLEIRA le daqui e nao do corpo desenhado no cliente, e a diferenca importa: o
	/// `RemotePlayer` interpola, e o que se quer medir e justamente o SALTO (o reaparecimento), que a
	/// interpolacao existe pra esconder.
	/// </summary>
	internal Vec2? PosDoReflexoNoTeste(int donoId)
	{
		if (!_players.TryGetValue(donoId, out ServerPlayer? dono) || dono.CloneId == 0) return null;
		return _players.TryGetValue(dono.CloneId, out ServerPlayer? c) ? c.Pos : null;
	}

	/// <summary>
	/// EMPURRA O PODER DESTE CORPO -- e com ele a VELOCIDADE, que e o que a bancada quer.
	///
	/// A familia do infinito anda QUINHENTOS tiles, e no BP de um personagem recem-criado isso e um
	/// passeio de varios minutos. Subir o BP e o mesmo recurso (e o mesmo metodo, `PorBp`) que a
	/// `--menteviva` ja usa, e ele passa pelo `RecalcularVelocidade` de producao -- que e o dono unico
	/// da velocidade neste port. Sem essa chamada o servidor continuaria conferindo o passo antigo e o
	/// corpo tremeria na costura, medindo o `ValidateStep` em vez do infinito.
	/// </summary>
	internal float PoderNoTesteDoMergulho(int id, double bp)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		PorBp(pl, bp);
		RecalcularVelocidade(pl);
		// DEVOLVE A VELOCIDADE RESULTANTE porque ela e o numero que a caminhada gasta: o `Espeed` satura
		// perto de 10 (`MoveRules.EspeedBase`), entao empurrar BP tem teto -- e a bancada precisa escrever
		// no log quanto conseguiu, senao "andou devagar" vira palpite.
		return pl.SpeedStat;
	}

	/// <summary>
	/// PoE O CORPO DE PE E INTEIRO -- membros devolvidos, nocaute limpo, Ki cheio.
	///
	/// ============================ ELE EXISTE PORQUE UMA CAMINHADA ANDOU DOIS TILES ============================
	/// A familia da borda anda 500 tiles depois de as familias 4 e 5 terem matado reflexo e apanhado no
	/// corpo largado. Na rodada inteira ela andou **2 tiles**: o corpo entrou na caminhada NOCAUTEADO, e
	/// `LocalPlayer` desliga o piloto automatico de quem esta caido (*"este corpo nao anda"*) -- a
	/// bancada mediu um nocaute e chamou de borda.
	///
	/// O `CurarDeTeste` que ela usava nao resolve: ele devolve VIDA e nao levanta ninguem (`Ficha.KO` e
	/// `Combate.NocauteRestante` continuam de pe). Quem levanta e o `CurarPorInteiro`, que ja existia
	/// na `--menteviva` -- e reusa-lo e o certo: uma segunda copia dele seria a duplicata que este port
	/// recusa por regra.
	/// =====================================================================================================
	/// </summary>
	internal bool DePeNoTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		CurarPorInteiro(pl);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Ficha.Tick(agoraMs: NowMs());
		MandarFicha(pl);
		return true;
	}

	// =====================================================================
	// OS ACONTECIMENTOS -- o que o jogador sozinho nao encomenda
	// =====================================================================
	/// <summary>
	/// O REFLEXO MORRE -- a unica saida por VITORIA, e a razao de existir da onda da volta.
	///
	/// ============================ A MORTE E ESCRITA, MAS QUEM REAGE E O JOGO ============================
	/// A bancada escreve `dead` na ficha do corpo que a mente ergueu: e exatamente o estado que um
	/// golpe LETAL produz (`MeleeResolver` -> `DeveMorrer`), e chegar la por soco exigiria ligar o
	/// letal, desnivelar o poder e esperar a briga -- tres coisas que nao sao o assunto desta rodada.
	///
	/// **O QUE ACONTECE DEPOIS NAO E DAQUI**: quem ve o reflexo morto e o `TicarUmCorpo` de producao,
	/// no proximo tique, e e ELE que chama o `ComecarAVoltaDaMente`. A bancada nao chama a volta --
	/// se aquele ramo for desligado um dia, esta familia fica vermelha, que e o ponto.
	/// ================================================================================================
	/// </summary>
	internal bool MatarOReflexoNoTeste(int donoId)
	{
		if (!_players.TryGetValue(donoId, out ServerPlayer? dono) || dono.CloneId == 0) return false;
		if (!_players.TryGetValue(dono.CloneId, out ServerPlayer? reflexo)) return false;

		reflexo.Ficha.dead = true;
		reflexo.Ficha.KO = false;   // morto nao e caido: o ramo da morte vem antes, e e o que se mede
		return true;
	}

	/// <summary>
	/// DESFAZ O REFLEXO SEM ENCERRAR O TRANSE -- o `DesfazerOOponente` de producao, que e o mesmo
	/// caminho de quando um VISITANTE chega (`if(clone) del(C)` do `add_member`, `MindMeditate.dm:201`).
	///
	/// ============================ ELE EXISTE POR CAUSA DE UMA MEDIDA QUE SAIU ERRADA ============================
	/// A familia da BORDA anda 500 tiles e conta os SALTOS PRA TRAS -- que e o *"ele TELEPORTA DE VOLTA"*
	/// do dono. Na primeira rodada ela contou **62 saltos** e chamou isso de teleporte; nao era: era o
	/// reflexo alcancando o fugitivo e batendo, e recuar levando um golpe e o `Empurrao` funcionando.
	/// Uma bancada que chama knockback de teleporte reprova o sistema certo.
	///
	/// Entao as duas coisas se separaram: a familia 6 anda com a mente VAZIA (a borda e do mapa, nao da
	/// briga) e a familia 7 anda com o reflexo vivo (a coleira so tem assunto com ele la). Cada uma mede
	/// o que o nome dela diz.
	/// ========================================================================================================
	/// </summary>
	internal bool DesfazerOReflexoNoTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.CloneId == 0) return false;
		DesfazerOOponente(pl, "o seu reflexo se desfaz -- a mente fica vazia.");
		return true;
	}

	/// <summary>
	/// EMPURRA O REFLEXO PRA TRAS DO DONO, <paramref name="tiles"/> tiles -- o gatilho da COLEIRA.
	///
	/// ============================ POR QUE A FUGA NAO SERVIA COMO GATILHO ============================
	/// A primeira versao da familia 7 fugia a pe e esperava o reflexo ficar pra tras. Ela e verdadeira e
	/// e FLAKY, e o motivo e a propria premissa do sistema: os dois correm na MESMA velocidade, entao a
	/// vantagem so nasce quando o reflexo para pra bater -- e quando ele bate, o knockback tira a
	/// vantagem de volta. Duas rodadas iguais deram 40 tiles de folga e 16 tiles de folga; a segunda
	/// nunca chegou perto do raio e a familia ficou vermelha sem defeito nenhum.
	///
	/// Empurrar o reflexo poe a bancada NO GATILHO em vez de torcer por ele: 60 tiles passam dos 40 da
	/// coleira, e o que se mede depois disso e produção pura -- `TicarUmCorpo` pergunta `FugiuDoDono` e
	/// chama `ReaparecerNaFrente`. **Nada aqui imita a regra**: a bancada so cria a distancia.
	/// ============================================================================================
	/// </summary>
	internal bool AfastarOReflexoNoTeste(int donoId, float tiles)
	{
		if (!_players.TryGetValue(donoId, out ServerPlayer? dono) || dono.CloneId == 0) return false;
		if (!_players.TryGetValue(dono.CloneId, out ServerPlayer? reflexo)) return false;

		reflexo.Pos = dono.Pos - new Vec2(tiles * ZoneCollision.TileSize, 0);
		return true;
	}

	/// <summary>
	/// ALGUEM SOCA O CORPO LARGADO -- *"(SO N VAI TER EFEITO SE ALGUEM BATER NO CORPO REAL enquanto
	/// ta meditando)"*, que e a linha do pedido que separa esta rodada de "ondula sempre".
	///
	/// ============================ O CARRASCO PRECISA EXISTIR, E SAI NA MESMA LINHA ============================
	/// `MarcarAgressao` recusa a si mesmo (*"ninguem guarda rancor de si mesmo"*), entao nao ha como o
	/// unico jogador do processo se acordar sozinho. O corpo forjado nasce, agride pelo funil de
	/// producao e e recolhido no mesmo metodo -- ele nao vive um tique sequer, e nao entra em conta
	/// nenhuma do mundo.
	///
	/// **O GATILHO E O `MarcarAgressao` e nao o `AcordarNoCorpo`**, de proposito: e o funil unico de
	/// agressao, e e nele que o pedido do dono foi encaixado. Chamar o de dentro pularia justamente a
	/// ligacao que pode estar faltando.
	/// ======================================================================================================
	/// </summary>
	internal bool SocarOCorpoLargadoNoTeste(int donoId)
	{
		if (!_players.TryGetValue(donoId, out ServerPlayer? dono)) return false;
		if (dono.BonecoLargado is not { } boneco) return false;

		var carrasco = new ServerPlayer
		{
			Id = IdBaseDoMergulho,
			Peer = null,
			Name = "bancada: quem bate no corpo",
			Race = "Human", Genero = "Male", Idade = 25,
			Zone = boneco.Zone, Pos = boneco.Pos + new Vec2(24, 0),
			Conta = "bancada_mergulho_carrasco", Slot = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = 1_000 },
		};
		carrasco.Ficha.Class = "Normal";
		PorNoMundo(carrasco);

		MarcarAgressao(boneco, carrasco);
		Recolher(carrasco);
		return true;
	}

	// =====================================================================
	// OS DEFEITOS -- cada um e uma linha que este servidor JA TEVE
	// =====================================================================
	/// <summary>
	/// DEFEITO: **A PORTA SEM A GOTA NA FRENTE** -- `EntrarNaMente(pl)` chamado direto, que e
	/// literalmente o que o `case "mente"` do canal de habilidade fazia antes desta rodada
	/// (`GameServer.Raciais.cs`, hoje `ComecarOMergulho`).
	///
	/// Com ele a viagem acontece no MESMO tique do pedido e nenhum pacote de ondulacao sai. As duas
	/// familias da ida ficam vermelhas por motivos diferentes e as duas importam: a da ORDEM porque
	/// nao ha onda pra vir antes, e a do PIXEL porque a foto do "meio da onda" sai identica a foto da
	/// tela parada. Sem ele, "a tela ondulou" passaria verde com o shader morto.
	/// </summary>
	internal bool DefeitoMergulhoSemOnda(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		EntrarNaMente(pl);
		return true;
	}

	/// <summary>
	/// DEFEITO: **A VOLTA SECA** -- `SairDaMente(pl, motivo)` chamado direto, que e a linha EXATA que
	/// estava em `GameServer.Clone.cs:490` antes da gota, e a queixa do dono palavra por palavra:
	/// *"atualmente a transicao ta MT RAPIDA E MT SECA sem efeito nenhum"*.
	///
	/// A familia da volta fica vermelha: o mundo real volta no mesmo tique, sem onda nenhuma.
	/// </summary>
	internal bool DefeitoSaidaSecaDaMente(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		SairDaMente(pl, "o seu reflexo se desfaz. Voce abre os olhos.");
		return true;
	}

	/// <summary>
	/// DEFEITO: **O SOCO QUE ONDULA** -- o corpo largado apanha e o dono volta pela FILA DA ONDA, em
	/// vez da volta seca de producao.
	///
	/// ============================ ESTE E O DEFEITO QUE A LINHA DO DONO EXISTE PRA IMPEDIR ============================
	/// *"(SO N VAI TER EFEITO SE ALGUEM BATER NO CORPO REAL enquanto ta meditando)"*. Sem esta
	/// injecao, uma bancada que so olhasse "ondulou na ida" e "ondulou na volta" ficaria verde num
	/// jogo que ondula SEMPRE -- e ondular ao levar um soco e o oposto do que a onda quer dizer: ser
	/// arrancado do transe tem que ser seco, que e a diferenca entre fechar os olhos e levar um soco
	/// na cara.
	///
	/// Repare que ele NAO enfileira `_acordar`: e uma implementacao inteira e plausivel do caminho
	/// errado (alguem ligando o `AcordarNoCorpo` na onda), e nao um remendo em cima do certo.
	/// ============================================================================================================
	/// </summary>
	internal bool DefeitoSocoQueOndula(int donoId)
	{
		if (!_players.TryGetValue(donoId, out ServerPlayer? dono)) return false;
		if (!NaMente(dono)) return false;
		ComecarAVoltaDaMente(dono, "algo atinge o seu corpo -- voce volta pra ele na hora.");
		return true;
	}

	/// <summary>
	/// LIMPEZA DE FIM DE RODADA -- tira o reflexo do bolso e devolve o corpo, se a bancada morreu no
	/// meio de alguma coisa. Chamada pelo robo no fim, e ela e o oposto de uma excecao: sem ela, uma
	/// rodada interrompida deixa um corpo dirigido parado numa mente pra sempre.
	/// </summary>
	internal void LimparOMergulhoNoTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		if (NaMente(pl)) SairDaMente(pl, "a bancada acabou.");
		DesfazerOOponente(pl, "");
		GD.Print($"[mergulho] limpeza: #{id} fora da mente e sem reflexo pendurado");
	}
}
