using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DAS FORMAS (`--formasteste`).
///
/// ============================ POR QUE O CORE NAO BASTA ============================
/// A bancada `formas` do AssetPipeline prova as REGRAS: multiplicador, dreno, gate, save. Ela nao
/// pode provar a CADEIA -- que pedir "subir" chega no `Transformar`, que o `AplicarForma` escreve
/// no `ssjBuff`, que o `powerlevel()` le esse campo, e que o BP na ficha realmente muda. Sao dois
/// trabalhos diferentes, e este projeto ja gastou sessoes inteiras achando regra escrita e nunca
/// ligada.
///
/// Entao aqui o teste chama `Transformar(pl, true)` -- a MESMA funcao que a tecla C chama -- e le o
/// `expressedBP` DEPOIS. No servidor o BP e real (no cliente ele chega NaN por causa do sigilo),
/// entao este e o unico lugar do jogo onde da pra conferir a conta.
/// ==============================================================================
///
///     Godot --headless -- --server --formasteste
/// </summary>
public partial class GameServer
{
	private bool _formasDeTeste;

	// =====================================================================
	// AS TRES ESCUTAS -- o que o servidor DIZ, guardado pra ser conferido
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ELAS EXISTEM, E POR QUE MORAM AQUI ============================
	/// Tudo o que o servidor conta pro jogador termina num `NetPeer.Send`, e pacote que saiu no fio nao
	/// volta: uma bancada de servidor nao tem como perguntar "o aviso saiu?" nem "com que degrau de cena
	/// aquele anuncio foi?". E sao justamente essas duas coisas que quebram CALADAS -- a fase anterior
	/// entregou uma punicao de Oozaru que dizia o que aconteceu e nao dizia a saida, e nada percebeu,
	/// porque a falta de uma frase nao derruba nada.
	///
	/// A alternativa era testar copias das regras (montar o pacote de novo na bancada e conferir a conta
	/// da bancada) -- que e exatamente o "node isolado" que ja deu verde tres vezes com o jogo quebrado.
	///
	/// TRES LINHAS DE PRODUCAO, TODAS `?.Add`: <see cref="Avisar"/>, <see cref="AnunciarForma"/>,
	/// <see cref="AnunciarOozaru"/> e <see cref="MandarEstadoDeForma"/>. Nulas em jogo -- uma comparacao
	/// contra null e nada mais. E moram NESTE arquivo de proposito: elas nascem e morrem com a bancada,
	/// e quem apagar `--formasteste` um dia leva as quatro linhas junto sem caca.
	/// ================================================================================================
	/// </summary>
	internal static List<string>? EscutaDeAvisos;

	/// <summary>O que o <see cref="AnunciarForma"/> mandou pra zona: quem, de onde, pra onde, e com quanta cena.</summary>
	internal static List<(int Quem, string De, string Para, DegrauDeCena Degrau)>? EscutaDeAnuncios;

	/// <summary>O mesmo, pro <see cref="AnunciarOozaru"/>.</summary>
	internal static List<(int Quem, FormaOozaru Forma, bool Primeira, DegrauDeCena Degrau)>? EscutaDeFeras;

	/// <summary>
	/// O que a sincronia de entrada de zona mandou, e PRA QUEM. Capturado no
	/// <see cref="MandarEstadoDeForma"/>, com o `NetDataWriter` inteiro -- a bancada le os BYTES que
	/// sairiam no fio, e nao um espelho deles.
	/// </summary>
	internal static List<(int Quem, int Para, NetDataWriter Pacote)>? EscutaDeSincronia;

	/// <summary>
	/// PRA QUEM O ANUNCIO DE FORMA FOI -- um par por destinatario, anotado DENTRO do `foreach` do
	/// <see cref="AnunciarForma"/>.
	///
	/// ============================ ELA MEDE O ALCANCE DA CENA, QUE E COISA DE SERVIDOR ============================
	/// A musica de transformacao toca pro planeta inteiro e o tremor sacode o planeta inteiro -- e nem o
	/// cliente nem o Core decidem isso: quem decide e o `ZoneList(pl.Zone.Hash)` daquele laco. O cliente
	/// nao tem como recusar o que nao chega, e e por isso que "nao alcanca quem esta em outro planeta"
	/// nunca teve bancada: do lado de la nao acontece NADA, e nada e o que uma bancada de cliente ve o
	/// tempo todo.
	///
	/// COMO ELA REPROVA SE A REGRA SUMIR: troque o `ZoneList(pl.Zone.Hash)` do `AnunciarForma` por
	/// `_players.Values` (a "simplificacao" plausivel -- e o anuncio continuaria funcionando pra quem
	/// esta perto, entao ninguem perceberia jogando) e o estranho de outro planeta aparece nos destinos.
	/// ======================================================================================================
	/// </summary>
	internal static List<(int Quem, int Para)>? EscutaDeDestinos;

	/// <summary>
	/// QUEM TEVE A CINEMATICA DE FURIA DISPARADA -- anotada dentro do <see cref="TalvezACenaDaFuria"/>,
	/// depois das quatro condicoes e da recarga.
	///
	/// ============================ ELA MEDE UMA DECISAO QUE NAO DEIXA RASTRO ============================
	/// A cena da furia nao muda estado nenhum: nao ha forma nova, nao ha buff novo (o `angerBuff` ja
	/// vinha da janela), nao ha campo pra perguntar depois. As quatro condicoes do `Murder.dm:119` --
	/// grau extremo, nao estava em furia, tem dono, e a raiva nao vai virar transformacao -- decidem
	/// entre "o pacote sai" e "o pacote nao sai", e mais nada muda no servidor.
	///
	/// Sem esta lista, a unica forma de conferi-las seria reimplementa-las na bancada -- que e o
	/// "node isolado" que este arquivo inteiro existe pra recusar.
	///
	/// O `FuriaCenaAte` E A OUTRA METADE e ele deixa rastro (e por isso a recarga se confere lendo o
	/// campo). Esta lista responde a pergunta que ele nao responde: se o pacote SAIU.
	/// ============================================================================================
	/// </summary>
	internal static List<int>? EscutaDeFurias;

	/// <summary>
	/// O ESTADO DE FORMA DE UM JOGADOR, PRA A BANCADA LER -- e so ler.
	///
	/// ============================ POR QUE UMA BANCADA DO CLIENTE PRECISA DISTO ============================
	/// A cinematica do SSJ3 VESTE o SSJ1 e o SSJ2 no caminho (`Efeito.VesteDegrau`), e a pergunta que
	/// o dono fez sobre ela nao e visual: *"e NAO desperta nem concede SSJ1/SSJ2 no servidor"*. Vestir
	/// e conceder se parecem em tudo na tela -- o boneco fica igualzinho --, e a UNICA diferenca mora
	/// aqui dentro, no `Liberadas`/`EstreiaVista` deste objeto. Sem esta janela a bancada do
	/// `--diagforma` so poderia afirmar a metade que se ve.
	///
	/// SO LEITURA, e de proposito: quem escreve continua sendo o `Transformar`/`Entrar`. Uma versao
	/// que devolvesse algo mutavel viraria a porta dos fundos que este metodo existe pra vigiar.
	/// (O `EstadoDeForma` e uma classe, entao o objeto devolvido E o de producao -- a bancada que o
	/// recebe copia o que precisa comparar e nao guarda a referencia.)
	///
	/// Vive NESTE arquivo pelo mesmo motivo das quatro escutas acima: nasce e morre com a bancada, e
	/// quem apagar o `--formasteste` um dia leva esta linha junto sem caca.
	/// ================================================================================================
	/// </summary>
	internal EstadoDeForma? FormaDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? pl.Forma : null;

	/// <summary>
	/// ============================ A COR DE AURA QUE O **SERVIDOR** GUARDA PRA ESTE CORPO ============================
	/// Irma da de cima, e ela existe por um motivo medido, nao por simetria: metade dos sorteios de
	/// aura da BRANCO PURO (~49%, ver `Core.Appearance.CorDeAura`). Ou seja toda checagem do cliente
	/// que compare a chama do corpo com um branco -- e o sujeito de bancada sai branco em uma rodada a
	/// cada duas -- fica verde com um defeito que pinte branco, que e justamente o defeito historico
	/// deste port ("branco multiplicando a folha colorivel APAGA a arte").
	///
	/// Com esta janela a afirmacao deixa de ser sobre uma cor e passa a ser sobre um CAMINHO: a cor
	/// que o servidor sorteou (ou derivou do save) e a mesma que chegou no node `Aura` do corpo, seja
	/// ela qual for. Isso nao tem como passar por coincidencia.
	///
	/// SO LEITURA, como a irma: devolve a `Rgb` da ficha, que e struct e portanto copia.
	/// ============================================================================================
	/// </summary>
	internal Jandirus.Core.Appearance.Rgb? CorDeAuraDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? pl.Visual.CorAura : null;

	/// <summary>O que um pacote de estado DIZ, lido dos bytes. Ver <see cref="LerPacote"/>.</summary>
	private readonly record struct PacoteLido(Protocol.S2C Tipo, int Quem, string De, string Para,
											  DegrauDeCena Degrau, FormaOozaru Fera, bool Primeira);

	/// <summary>
	/// DESMONTA O PACOTE PELO MESMO CAMINHO QUE O CLIENTE USA -- byte a byte, na ordem em que o
	/// `PacoteDeForma`/`PacoteDeOozaru` os escreveu.
	///
	/// Ler os bytes e nao os argumentos e o que faz esta bancada perceber um campo trocado de lugar:
	/// um `Put` fora de ordem nao muda nenhuma chamada e destroi o pacote inteiro do outro lado.
	/// </summary>
	private static PacoteLido LerPacote(NetDataWriter w)
	{
		var r = new NetDataReader(w.Data, 0, w.Length);
		var tipo = (Protocol.S2C)r.GetByte();
		int quem = r.GetInt();

		if (tipo == Protocol.S2C.Forma)
		{
			string de = Catalogo.PorRede(r.GetUShort())?.Id ?? "?";
			string para = Catalogo.PorRede(r.GetUShort())?.Id ?? "?";
			return new PacoteLido(tipo, quem, de, para, (DegrauDeCena)r.GetByte(), FormaOozaru.Nao, false);
		}

		var fera = (FormaOozaru)r.GetByte();
		bool primeira = r.GetBool();
		return new PacoteLido(tipo, quem, "", "", (DegrauDeCena)r.GetByte(), fera, primeira);
	}

	/// <summary>
	/// ============================ A CINEMATICA VEM ANTES DE TODA MEDIDA DE TIQUE ============================
	/// Enquanto a cena prende o corpo, o tique NAO cobra Ki, NAO sobe maestria e **NAO chama o
	/// `AplicarForma`** (ver `GameServer.Formas.EmCena`). Quem for medir dreno, maestria ou
	/// multiplicador sem queimar a cena primeiro mede o CONGELAMENTO -- e conclui que nenhum dos tres
	/// existe. A estreia de um degrau prende de 4 a 140 s.
	///
	/// TETO DE VOLTAS e nao `while`: se um dia o prazo deixar de andar, isto vira laco infinito e a
	/// bancada TRAVA em vez de reprovar -- que e o unico jeito de um teste ser pior que nenhum.
	///
	/// Era funcao local do <see cref="RodarBancadaDeFormas"/> e subiu pra ca quando o segundo bloco
	/// precisou dela: uma copia de tres linhas e o comeco de duas bancadas medindo coisas diferentes.
	/// ================================================================================================
	/// </summary>
	private void PassarACena(ServerPlayer p)
	{
		for (int t = 0; t < 3000 && p.CenaSegundos > 0; t++) TickDaForma(p, 0.1);
	}

	/// <summary>
	/// MATA O CORPO PELO FUNIL DE VERDADE -- `Corpo.Ferir` (letal) -> `DeveMorrer` -> `Morrer()`.
	///
	/// Nao e `pl.Ficha.dead = true`: quem escreve o campo na mao pula exatamente os passos onde uma
	/// regra de morte se pendura (o seguro da Aura of Destruction se pendura ali, e o gancho da
	/// amizade vai se pendurar). Uma bancada que escrevesse o booleano diria "ninguem entrou em
	/// furia" sobre uma morte que nunca aconteceu.
	///
	/// Era funcao local do <see cref="RodarBancadaDeFormas"/> e subiu quando o segundo bloco precisou
	/// dela -- mesmo motivo do <see cref="PassarACena"/>.
	/// </summary>
	private static void MatarDeVerdade(ServerPlayer pl)
	{
		foreach (Jandirus.Core.Combat.BodyPart bp in pl.Combate!.Corpo.Partes.ToList())
			if (!bp.Decepado) pl.Combate.Corpo.Ferir(bp, bp.VidaMax * 10, letal: true);
		pl.Combate.SincronizarVida();
		if (pl.Combate.Corpo.DeveMorrer()) pl.Combate.Morrer();
	}

	/// <summary>
	/// SOBE A ESCADA INTEIRA E CONFERE O BP A CADA DEGRAU.
	///
	/// Roda uma vez, quando o primeiro jogador entra. Ela MEXE no personagem de proposito (BP,
	/// maestria, formas despertadas) -- e por isso so acontece com a flag, nunca em servidor de
	/// verdade.
	/// </summary>
	private void RodarBancadaDeFormas(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA AO VIVO DAS FORMAS =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		// FECHA A ESCUTA E DEVOLVE O QUE ELA OUVIU. Desarmar no mesmo gesto e o que impede uma escuta
		// esquecida ligada de acumular o resto da bancada e fazer uma checagem passar por barulho de
		// outro bloco -- que e o modo de falha classico de teste com estado global.
		List<string> Ouvido()
		{
			List<string> ditos = EscutaDeAvisos ?? [];
			EscutaDeAvisos = null;
			return ditos;
		}

		// ============================ ANTES DE TUDO: O CORPO PRECISA SER DE UM **SAIYAJIN** ============================
		// Esta bancada mede a escada Saiyajin do comeco ao fim (SSJ1 ate o SSJ4 vindo da lua, os degraus
		// de raiva, a maestria, a razao de Ki) num corpo VIVO -- e o corpo vivo e o do primeiro jogador
		// que logar, com a raca que a criacao sorteou pra ele.
		//
		// **ELA NUNCA TINHA PERGUNTADO A RACA, E PASSAVA.** Passava por um defeito, nao por sorte: o
		// `Catalogo.LinhasAbertas` entregava a escada Saiyajin a QUALQUER raca que nao fosse Primal,
		// Legendary, Futuro ou Frost Demon -- entao um Humano subia ate o Super Saiyajin 4 e a bancada
		// dizia OK. Na primeira execucao depois de a porta ser fechada por raca (ver `LinhasAbertas`),
		// esta bancada reprovou 24 vezes com a conta `Guerreiro`, que e um **Humano/Peak Human** -- e
		// nenhuma das 24 falhas era sobre o que a checagem dizia estar medindo.
		//
		// O CONSERTO E O MESMO PADRAO DO `--frostteste`, que ja vestia o corpo antes de medir
		// (`VestirDeFrost`): quem mede uma escada de SANGUE tem que trazer o sangue. A `Class` vai junto
		// e e "Normal" de proposito -- ela nao pode ser Legendary (linha propria), Kaio (Rose no lugar
		// do Blue), Elite (Blue Evolution) nem Prodigial (linha do Mistico), e cada um desses trocaria
		// silenciosamente a escada que os blocos abaixo esperam.
		//
		// **NAO HA `finally` PRA DESFAZER**, e isso e proposital e nao esquecimento: os blocos desta
		// bancada ja escrevem BP, maestria, formas liberadas, ki divino e classe no personagem de
		// verdade -- ela existe atras de uma flag de linha de comando justamente porque MEXE no corpo.
		// Devolver a raca no fim daria a impressao de que o resto foi devolvido tambem.
		// =========================================================================================================
		pl.Race = "Saiyan";
		pl.Ficha.Race = pl.Race;
		pl.Ficha.Class = "Normal";
		if (pl.Ficha.Genoma != null) pl.Ficha.Genoma.Class = pl.Ficha.Class;

		// --- o personagem de teste: BP de sobra e Ki cheio -----------------
		pl.Ficha.BP = 1e13;
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Ficha.KO = false;
		pl.Ficha.dead = false;
		AplicarForma(pl);
		Medir(pl);

		double bpBase = pl.Ficha.expressedBP;
		Checa("BP base positivo antes de qualquer forma", bpBase > 0, $"{bpBase}");
		Checa("multiplicador da base = 1", Math.Abs(pl.Ficha.ssjBuff - 1) < 1e-9, $"{pl.Ficha.ssjBuff}");

		// ============================ O DOM DO MISTICO -- O GANCHO DO RITUAL ============================
		// O ritual do Kaioshin nao existe ainda; `ConcederMistico` existe, e e por ele que o ritual
		// vai falar quando existir. Esta bancada e quem prova que o gancho FUNCIONA -- sem ela, o
		// unico caminho pra o Mistico seria o verb de admin, e o dia em que o ritual chamasse isto
		// seria o dia de descobrir se a concessao pega.
		//
		// O QUE ESTA SENDO MEDIDO, na ordem: a forma e INVISIVEL sem concessao (nem com BP de sobra),
		// a concessao a torna alcancavel na hora, e conceder duas vezes nao e conceder duas vezes.
		Checa("o Mistico e recusado por NAO CONCEDIDO (nem com BP de 1e13)",
			  pl.Forma.Avaliar(Catalogo.IdMistico, pl.Ficha.BP, 1, false, Perfil(pl))
				  == RecusaForma.NaoConcedida,
			  pl.Forma.Avaliar(Catalogo.IdMistico, pl.Ficha.BP, 1, false, Perfil(pl)).ToString());

		Checa("conceder o Mistico devolve TRUE na primeira vez", ConcederMistico(pl, "bancada"));
		Checa("e FALSE na segunda (o ritual nao anuncia duas vezes)", !ConcederMistico(pl, "bancada"));
		Checa("concedido, o Mistico passa a ser alcancavel",
			  pl.Forma.Avaliar(Catalogo.IdMistico, pl.Ficha.BP, 1, false, Perfil(pl)) == RecusaForma.Pode);

		// E A BANCADA DEVOLVE O DOM. Nao e limpeza de cortesia: `Proxima` escolhe o degrau MAIS
		// FORTE alcancavel, e o Mistico (16x) atropelaria a subida da escada Saiyajin logo abaixo --
		// o teste de "sobe degrau a degrau" viraria um salto so, e passaria.
		pl.Forma.Liberadas.Remove(Catalogo.Rede(Catalogo.IdMistico));

		// ============================ A FURIA EXTREMA -- O GANCHO DA AMIZADE ============================
		// Mesmo papel do bloco acima: aqui o gancho e chamado A MAO, pra medir a peca sozinha. Quem
		// prova que o JOGO o chama (ver um amigo morrer, na sua frente, pelas maos de um inimigo) e
		// o `OConvivioAoVivo`, mais abaixo -- e a bancada `raiva` [8], que varre os fontes.
		//
		// E ha uma segunda coisa medida aqui que a bancada do Core NAO consegue medir: que o
		// `Perfil()` -- o funil unico dos gates -- realmente LE o prazo. O Core testa o `Avaliar`
		// com um `RaivaExtrema` escrito a mao; aqui o booleano tem que nascer do relogio.
		// ================================================================================================
		{
			string classeAntes = pl.Ficha.Class;
			Jandirus.Core.Stats.GodKiState? godkiAntes = pl.Ficha.godki;

			pl.Ficha.Class = Catalogo.ClasseProdigial;
			pl.Ficha.godki = new Jandirus.Core.Stats.GodKiState
			{ awakened = true, usage = true, mastery = 100 };
			pl.Forma.Liberar(Catalogo.IdMistico);
			pl.Forma.Entrar(Catalogo.IdMistico);

			Checa("sem o gancho, ninguem esta em raiva (o padrao e o lado seguro)",
				  Perfil(pl).Raiva == NivelDeRaiva.Nenhuma);
			Checa("e o Beast e recusado por SEM FURIA, com Mistico e 100% de ki divino na mao",
				  pl.Forma.Avaliar("beast", pl.Ficha.BP, 1, false, Perfil(pl)) == RecusaForma.SemFuria,
				  pl.Forma.Avaliar("beast", pl.Ficha.BP, 1, false, Perfil(pl)).ToString());

			Checa("o gancho devolve TRUE na erupcao",
				  AmigoAbatido(pl, "Krillin", NivelDeRaiva.Extrema));
			Checa("e FALSE quando so prolonga (a cinematica nao toca duas vezes)",
				  !AmigoAbatido(pl, "Yamcha", NivelDeRaiva.Extrema));
			Checa("o perfil ja le a furia do relogio", Perfil(pl).Raiva == NivelDeRaiva.Extrema);
			Checa("com a furia acesa, o Beast abre",
				  pl.Forma.Avaliar("beast", pl.Ficha.BP, 1, false, Perfil(pl)) == RecusaForma.Pode);

			// A JANELA FECHA SOZINHA, e isto e o que prova que nao ha furia permanente: o prazo e
			// puxado pra tras em vez de esperar 2 minutos de bancada.
			pl.FuriaExtremaAte -= (long)(SegundosDeRaiva * 1000) + 500;
			Checa("passado o prazo, a furia acaba sem ninguem apaga-la",
				  Perfil(pl).Raiva == NivelDeRaiva.Nenhuma);
			Checa("e o Beast volta a ser recusado",
				  pl.Forma.Avaliar("beast", pl.Ficha.BP, 1, false, Perfil(pl)) == RecusaForma.SemFuria);

			// DESPERTO UMA VEZ, VIRA TOGGLE (o `hasbeast` do DM) -- sem furia nenhuma.
			pl.Forma.Liberar("beast");
			Checa("desperto, o Beast dispensa a furia pra sempre",
				  pl.Forma.Avaliar("beast", pl.Ficha.BP, 1, false, Perfil(pl)) == RecusaForma.Pode);

			// E DEVOLVE TUDO. O Beast (56x) atropelaria a subida degrau a degrau logo abaixo, e a
			// classe Prodigial fecharia a escada Saiyajin que ela testa.
			pl.Forma.Entrar(Catalogo.IdBase);
			pl.Forma.Liberadas.Remove(Catalogo.Rede(Catalogo.IdMistico));
			pl.Forma.Liberadas.Remove(Catalogo.Rede("beast"));
			pl.FuriaExtremaAte = 0;
			pl.Ficha.Class = classeAntes;
			pl.Ficha.godki = godkiAntes;
			AplicarForma(pl);
		}

		// AS DUAS SECOES DA CURVA E DA PORTA. Moram em metodos proprios porque cada uma mexe em
		// classe, ki divino e formas liberadas -- e o `finally` de cada uma e o que impede que o
		// estranho de um bloco vire o resultado do seguinte.
		ACurvaDoMisticoNoCorpo(pl, Checa);
		OBeastEAFuriaInerte(pl, Checa);

		// A FURIA LENDARIA -- o ciclo inteiro (armar, perder, devolver) no corpo vivo. Mesmo motivo de
		// morar em metodo proprio: ela poe o personagem numa forma de outra linha e possui o corpo, e o
		// `finally` dela e o que impede esse estranho de virar o resultado das linhas abaixo.
		AFuriaLendariaNoCorpo(pl, Checa);

		// O BLOQUEIO E O FIO. Depende de a secao acima ter rodado (ela e quem prova que a posse
		// ACONTECE); esta pergunta as outras duas metades -- o que exatamente para de passar, e o que
		// chega no cliente dos OUTROS. Ver o cabecalho dela.
		OBloqueioEOFioDaPosse(pl, Checa);

		// OS DOIS DEGRAUS DE RAIVA, no corpo vivo. Mora em arquivo proprio
		// (`GameServer.RaivaTeste.cs`) pelo mesmo motivo das duas de cima -- ela troca a CLASSE do
		// personagem duas vezes (Normal e Legendary) pra medir o desconto da linha, e um `finally`
		// proprio e o que impede aquele Legendary de virar o resultado da escada aqui embaixo.
		ADuplaRaivaAoVivo(pl, Checa);

		// E A CORRENTE INTEIRA, do convivio ate a tecla C. Ela NAO usa o `pl` -- forja os proprios
		// quatro corpos, porque precisa de um que morra, um que mate, um que assista e um que esteja
		// longe demais pra ver. Ver `GameServer.ConvivioTeste.cs`.
		OConvivioAoVivo(Checa);

		// E O KARMA, pela mesma porta e pelo mesmo motivo: ele so acontece pra quem `EhJogador` diz
		// que e jogador, e esse predicado pede `Peer != null` -- ou seja, uma bancada de BOOT (sem
		// cliente) mediria corpos que o jogo nao considera gente e passaria por ausencia. Aqui os
		// corpos forjados emprestam o `Peer` do host. Ver `GameServer.KarmaTeste.cs`.
		OKarmaAoVivo(pl, Checa);
		OJulgamentoDoEnma(pl, Checa);

		// E AS SKILLS QUE OS CARGOS ENTREGAM, pela mesma porta e pelo mesmo motivo -- e por um terceiro
		// que so daqui se ve: quem entrega o kit e o `TickDosCargos`, que so olha pra quem `EhJogador`
		// aprova. A `--cargoportas` chama o `ReconciliarDadiva` a mao sobre corpos sem `Peer`; aqui o
		// trono se ocupa e o TIQUE DE PRODUCAO tem que entregar sozinho. Ver `GameServer.CargoVivoTeste.cs`.
		OsCargosAoVivo(pl, Checa);

		// ============================ A ESCADA AGORA PEDE O SSJ2 DOMINADO PELA METADE ============================
		// O SSJ3 deixou de ser um degrau que a raiva abre e passou a cobrar 50% de maestria no SSJ2
		// (`Transformation Controls.dm:46`, regra confirmada pelo dono -- ver a entrada `ssj3` no
		// catalogo). Sem esta linha o C PARA no SSJ2, e as cinco checagens que dependem de chegar la em
		// cima -- a subida degrau a degrau, a estreia do SSJ4 e a recusa que aponta pra fera -- reprovam
		// todas por um motivo que nao e o delas.
		//
		// E O NUMERO SAI DO CATALOGO, nao um `50` escrito aqui: se a regra mudar pra 60%, esta bancada
		// acompanha em vez de virar o unico lugar do projeto que ainda acha que sao 50.
		//
		// ELA NAO ESCONDE O GATE -- quem prova que o SSJ3 recusa sem a maestria e a bancada do Core
		// (`FormasBench.Gates`, "SSJ3 recusado com 49% de maestria no SSJ2"). Aqui o assunto e o que
		// vem DEPOIS do SSJ3, e pra isso a maestria precisa estar paga.
		// ========================================================================================================
		pl.Forma.Maestria.Por("ssj2", Catalogo.Ssj3PedeSsj2Pct);

		// ============================ E A ESCADA AGORA PEDE RAIVA, TAMBEM ============================
		// Mesmo caso da maestria acima, e pelo mesmo motivo: o tronco Saiyajin passou a cobrar a raiva
		// do LUTO (ver `Catalogo.RaivaExigida`), entao um corpo calmo nao sobe degrau nenhum -- e as
		// checagens daqui pra baixo, que sao sobre MULTIPLICADOR, MAESTRIA e a porta do SSJ4,
		// reprovariam todas por um motivo que nao e o delas. Foi exatamente o que aconteceu na
		// primeira rodada depois da regra nova: "subiu 0 degraus a partir da base".
		//
		// PELO GANCHO E NAO PELO CAMPO (`pl.FuriaExtremaAte = ...`): a raiva chega aqui pela mesma
		// porta por onde a amizade vai chama-la um dia, e assim esta linha continua valendo se o
		// prazo, a janela ou o nome do campo mudarem.
		//
		// E ELA NAO ESCONDE O GATE -- quem prova que o tronco RECUSA sem raiva sao a bancada `raiva`,
		// a secao [4] do Core (`SSJ1 recusado NA porta de BP -- porque agora falta a raiva`) e o bloco
		// do Beast logo acima, que mede a recusa saindo pela boca do jogo.
		//
		// O PRAZO E DE 2 MINUTOS DE RELOGIO REAL e a bancada inteira roda em segundos; mesmo assim ela
		// e reacesa antes do ultimo bloco que sobe a escada (o do SSJ4 vindo da lua), porque "cabe no
		// prazo hoje" e a promessa que envelhece calada quando a bancada cresce.
		// ==========================================================================================
		AmigoAbatido(pl, "um amigo de bancada", NivelDeRaiva.Extrema);

		// ============================ E O "VALOR BASE" E REMEDIDO DEPOIS DA RAIVA ============================
		// A RAIVA VIROU PODER DE VERDADE. O `bpBase` do comeco deste metodo foi lido com o corpo em
		// PAZ; desde que o `Fighter.Anger` passou a ser escrito (ver `GameServer.ProjetarRaiva`), a
		// linha acima acende ate 2x de `angerBuff` e o corpo na base ja nao vale o que valia. Sem
		// esta releitura o "descer volta o BP expresso pro valor base" reprova por 1,5x -- e a
		// bancada estaria certa e o codigo tambem: os dois numeros e que descreviam corpos
		// diferentes.
		//
		// E O PONTO E ESSE: `bpBase` significa *"este corpo, na forma base, agora"*, e nao *"este
		// corpo antes de qualquer coisa acontecer com ele"*. As checagens que o usam sao todas sobre
		// o que a FORMA multiplica -- comparar contra um corpo mais calmo mediria forma + raiva.
		//
		// COM O TANQUE CHEIO, e isto nao e detalhe: o `kiratio` e fator do `powerlevel()`
		// (`Fighter.Power.cs`), e o laco logo abaixo enche o Ki antes de cada degrau. Remedir com o
		// Ki que sobrou dos blocos anteriores compararia um corpo cheio (o de depois de descer) com
		// um corpo pela metade -- a primeira tentativa deste conserto errou por 1,67x exatamente
		// assim, e o numero enganava porque *parecia* a raiva.
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		Medir(pl);
		bpBase = pl.Ficha.expressedBP;

		// --- SUBIR: a MESMA funcao da tecla C -----------------------------
		var visitadas = new List<string>();
		for (int i = 0; i < 12; i++)
		{
			string antes = pl.Forma.Atual;
			pl.Ficha.Ki = pl.Ficha.MaxKi;   // a bancada nao esta testando o dreno aqui
			Transformar(pl, subir: true);
			if (pl.Forma.Atual == antes) break;
			visitadas.Add(pl.Forma.Atual);

			Medir(pl);
			FormaDef d = pl.Forma.Def!;
			double esperado = Catalogo.Multiplicador(d.Id, pl.Forma.Maestria, Perfil(pl),
													 pl.Forma.CombateSegundos);

			// O QUE ESTA SENDO MEDIDO: o `ssjBuff` chegou na ficha, e o `powerlevel()` o usou.
			Checa($"{d.Nome}: ssjBuff = {esperado:0.##}",
				  Math.Abs(pl.Ficha.ssjBuff - esperado) < 1e-6, $"ssjBuff {pl.Ficha.ssjBuff:0.####}");
			Checa($"{d.Nome}: BP expresso subiu (x{pl.Ficha.expressedBP / bpBase:0.##})",
				  pl.Ficha.expressedBP > bpBase * 1.05,
				  $"{pl.Ficha.expressedBP:N0} contra base {bpBase:N0}");
		}
		Checa($"subiu {visitadas.Count} degraus a partir da base", visitadas.Count >= 3,
			  string.Join(" -> ", visitadas));

		// --- DESCER volta o multiplicador pra 1 ---------------------------
		Transformar(pl, subir: false);
		Medir(pl);
		Checa("descer volta o ssjBuff pra 1", Math.Abs(pl.Ficha.ssjBuff - 1) < 1e-9, $"{pl.Ficha.ssjBuff}");
		Checa("descer volta o BP expresso pro valor base",
			  Math.Abs(pl.Ficha.expressedBP - bpBase) / bpBase < 0.01,
			  $"{pl.Ficha.expressedBP:N0} contra {bpBase:N0}");

		// ======================== A SOBRECARGA DE KI ATRAVESSA A TRANSFORMACAO ========================
		// O DEFEITO DO DONO: *"ao travar o ki ao se transformar, ele volta pro 100%, oq n deveria
		// acontecer"*. A causa era o `if (primeira) Ki = MaxKi` do `Transformar` -- um PRESENTE de
		// tanque cheio escrito como atribuicao absoluta, que virava CORTE pra quem chegava comprimido
		// pela tecla C.
		//
		// ============================ POR QUE ISTO NAO E O `AProporcaoDeKi` DE NOVO ============================
		// A bancada `--diagforma` ja media a razao a 190% subindo e descendo, em quatro formas, e
		// passou VERDE o tempo todo enquanto o dono via o defeito em jogo. O motivo e que ela chama o
		// `AplicarForma` DIRETO -- e `AplicarForma` sempre esteve certo. Quem cortava era a linha que
		// vem DEPOIS dele, dentro do funil da tecla C, e so na ESTREIA.
		//
		// Entao o que este bloco tem de diferente e exatamente isso, e nada mais: ele passa pelo
		// `Transformar` (a mesma funcao que a tecla C chama) e apaga a estreia de proposito, pra cair
		// no ramo `primeira`. Medir pelo `AplicarForma` aqui seria reescrever a bancada que ja existe
		// e continuar cega no mesmo lugar.
		// ==================================================================================================
		{
			var estreiasDoKi = new HashSet<int>(pl.Forma.EstreiaVista);
			const double sobrecarga = 1.90;
			double tetoDaBase = pl.Ficha.MaxKi;

			// --- 1) SUBIR sobrecarregado, na ESTREIA -----------------------
			pl.Ficha.Ki = sobrecarga * pl.Ficha.MaxKi;
			pl.Forma.EstreiaVista.Clear();
			Transformar(pl, subir: true);
			double razaoNoTopo = pl.Ficha.MaxKi > 0 ? pl.Ficha.Ki / pl.Ficha.MaxKi : -1;
			Checa($"ESTREIA com {sobrecarga * 100:0}% de Ki: subir NAO derruba pros 100%",
				  !pl.Forma.NaBase && Math.Abs(razaoNoTopo - sobrecarga) < 1e-9,
				  $"{pl.Forma.Atual}: {razaoNoTopo * 100:0.##}% ({pl.Ficha.Ki:0.0}/{pl.Ficha.MaxKi:0.0})");

			// A REGRA E RAZAO, E RAZAO E GANHO ABSOLUTO. O dono pediu *"mantendo as proporcoes
			// sempre"*: 190% de um tanque pequeno viram 190% de um tanque grande, ou seja MAIS pontos
			// de Ki. Sem esta segunda checagem, "manteve a razao" seria indistinguivel de "nao mexeu
			// no numero" -- que e a outra metade do defeito, a barra despencando ao subir.
			//
			// E ELA E ESCRITA CONTRA O CRESCIMENTO DO TANQUE, nao contra um limiar. A primeira versao
			// dizia `Ki > 1,05 x (190% da base)` e o teste de mutacao a pegou passando VERDE com a
			// linha sabotada: 280 contra o limiar 279,3, por um fio. Um numero cravado mede o tamanho
			// do defeito, e este defeito e pequeno em SSJ1 e grande em SSJ4.
			double cresceu = pl.Ficha.MaxKi / Math.Max(tetoDaBase, 1e-9);
			Checa("...e a razao mantida vira Ki ABSOLUTO a mais (o tanque cresceu junto)",
				  cresceu > 1.01
					  && Math.Abs(pl.Ficha.Ki - sobrecarga * tetoDaBase * cresceu) < 1e-6,
				  $"{pl.Ficha.Ki:0.0} contra {sobrecarga * tetoDaBase * cresceu:0.0} "
				+ $"({sobrecarga * tetoDaBase:0.0} na base x {cresceu:0.00} de tanque)");

			// --- 2) E A VOLTA PRA BASE, o outro sentido --------------------
			// Sem `PassarACena` de proposito: o tique dentro da cena drena Ki, e o que se mede aqui e
			// a troca de tanque e nao o dreno. Descer nao e barrado pela cena (ver `Transformar`).
			Transformar(pl, subir: false);
			double razaoNaVolta = pl.Ficha.MaxKi > 0 ? pl.Ficha.Ki / pl.Ficha.MaxKi : -1;
			Checa($"...e DESCER devolve os mesmos {sobrecarga * 100:0}% no tanque pequeno",
				  pl.Forma.NaBase && Math.Abs(razaoNaVolta - sobrecarga) < 1e-9
					  && Math.Abs(pl.Ficha.Ki - sobrecarga * tetoDaBase) < 1e-6,
				  $"{razaoNaVolta * 100:0.##}% ({pl.Ficha.Ki:0.0}/{pl.Ficha.MaxKi:0.0}), "
				+ $"esperado {sobrecarga * tetoDaBase:0.0}");

			// --- 3) O PRESENTE CONTINUA SENDO PRESENTE ---------------------
			// `Math.Max` nao pode ter virado "nunca mexe no Ki": a forma nova ainda tem que nascer com
			// folego pra ser usada. Quem chega com o tanque pela metade estreia CHEIO, como antes.
			pl.Ficha.Ki = pl.Ficha.MaxKi * 0.30;
			pl.Forma.EstreiaVista.Clear();
			Transformar(pl, subir: true);
			Checa("estreia com 30% de tanque continua enchendo pra 100% (o presente sobreviveu)",
				  !pl.Forma.NaBase && Math.Abs(pl.Ficha.Ki - pl.Ficha.MaxKi) < 1e-6,
				  $"{pl.Forma.Atual}: {pl.Ficha.Ki:0.0}/{pl.Ficha.MaxKi:0.0}");

			// --- 4) O CONTRA-EXEMPLO: 60% CONTINUA SENDO 60% ---------------
			// ============================ POR QUE 190% SOZINHO NAO BASTA ============================
			// As tres medidas de cima so olham ACIMA do cheio, e por isso todas elas passariam verdes
			// num mundo em que a linha da estreia virasse `Ki = Math.Max(MaxKi, Ki)` **pra toda
			// transformacao** -- a "simplificacao" plausivel, ja que o `if (primeira)` parece sobra
			// depois que o `Max` entrou. Nesse mundo o 190% atravessa (o `Max` o preserva), o ganho
			// absoluto atravessa, a volta atravessa... e o jogador que sobe com o tanque pela metade
			// ganha o tanque cheio de graca em TODO degrau, pra sempre. O presente da estreia teria
			// virado uma torneira, e ninguem veria: encher o Ki nao parece defeito.
			//
			// Entao esta medida e a de baixo do cheio, na REPETICAO: o degrau ja foi estreado agora ha
			// pouco (a checagem 3 acabou de entrar nele), entao este `Transformar` cai no ramo em que
			// `primeira` e falso -- que e onde a razao tem que atravessar sozinha, pelo `AplicarForma`,
			// sem presente nenhum por cima.
			//
			// A RAZAO ESCOLHIDA E 60% E NAO 30% de proposito: com 30% a barra sobe pra 100% no ramo
			// certo E no ramo errado quando a estreia esta acesa, e a bancada nao saberia dizer qual
			// dos dois ela mediu. 60% nao e redondo com nada -- se sair 100%, foi presente indevido;
			// se sair qualquer outro numero, foi a razao que se perdeu na troca de tanque.
			// ========================================================================================
			Transformar(pl, subir: false);
			double tetoDaBase60 = pl.Ficha.MaxKi;
			const double meio = 0.60;
			pl.Ficha.Ki = meio * tetoDaBase60;
			Transformar(pl, subir: true);

			double razao60 = pl.Ficha.MaxKi > 0 ? pl.Ficha.Ki / pl.Ficha.MaxKi : -1;
			double cresceu60 = pl.Ficha.MaxKi / Math.Max(tetoDaBase60, 1e-9);
			Checa($"REPETICAO com {meio * 100:0}% de Ki: subir mantem {meio * 100:0}% (nao enche pra 100%)",
				  !pl.Forma.NaBase && Math.Abs(razao60 - meio) < 1e-9,
				  $"{pl.Forma.Atual}: {razao60 * 100:0.##}% ({pl.Ficha.Ki:0.0}/{pl.Ficha.MaxKi:0.0})");
			Checa($"...e {meio * 100:0}% do tanque NOVO e mais Ki absoluto que {meio * 100:0}% do velho",
				  cresceu60 > 1.01
					  && Math.Abs(pl.Ficha.Ki - meio * tetoDaBase60 * cresceu60) < 1e-6,
				  $"{pl.Ficha.Ki:0.0} contra {meio * tetoDaBase60 * cresceu60:0.0} "
				+ $"({meio * tetoDaBase60:0.0} na base x {cresceu60:0.00} de tanque)");

			Transformar(pl, subir: false);
			double razao60NaVolta = pl.Ficha.MaxKi > 0 ? pl.Ficha.Ki / pl.Ficha.MaxKi : -1;
			Checa($"...e descer devolve os mesmos {meio * 100:0}%, no tanque pequeno",
				  pl.Forma.NaBase && Math.Abs(razao60NaVolta - meio) < 1e-9
					  && Math.Abs(pl.Ficha.Ki - meio * tetoDaBase60) < 1e-6,
				  $"{razao60NaVolta * 100:0.##}% ({pl.Ficha.Ki:0.0}/{pl.Ficha.MaxKi:0.0}), "
				+ $"esperado {meio * tetoDaBase60:0.0}");

			// devolve o estado: a cena pendente e queimada, a estreia volta como estava e o tanque
			// enche, que e como os blocos seguintes esperam encontrar o corpo.
			PassarACena(pl);
			Transformar(pl, subir: false);
			pl.Forma.EstreiaVista.Clear();
			pl.Forma.EstreiaVista.UnionWith(estreiasDoKi);
			pl.Ficha.Ki = pl.Ficha.MaxKi;
		}

		// --- O KI DERRUBA A FORMA -----------------------------------------
		// A regra "sem Ki, sem forma" so vale se ela DISPARAR: um dreno que nunca esvazia o tanque
		// e indistinguivel de dreno nenhum.
		Transformar(pl, subir: true);
		string ficouEm = pl.Forma.Atual;
		PassarACena(pl);
		pl.Ficha.Ki = pl.Ficha.MaxKi * 0.001;
		for (int t = 0; t < 200 && !pl.Forma.NaBase; t++) TickDaForma(pl, 0.1);
		Checa($"o Ki no fim derruba a forma ({ficouEm})", pl.Forma.NaBase, $"ficou em {pl.Forma.Atual}");

		// --- A MAESTRIA SO SOBE DENTRO DA FORMA ---------------------------
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		Transformar(pl, subir: true);
		PassarACena(pl);
		string forma = pl.Forma.Atual;
		double m0 = pl.Forma.Maestria.De(forma);
		for (int t = 0; t < 100; t++) { pl.Ficha.Ki = pl.Ficha.MaxKi; TickDaForma(pl, 1.0); }
		Checa("maestria sobe sustentando a forma", pl.Forma.Maestria.De(forma) > m0,
			  $"{m0:0.####} -> {pl.Forma.Maestria.De(forma):0.####}");

		double naBase = pl.Forma.Maestria.De(forma);
		Transformar(pl, subir: false);
		for (int t = 0; t < 100; t++) TickDaForma(pl, 1.0);
		Checa("maestria NAO sobe fora da forma",
			  Math.Abs(pl.Forma.Maestria.De(forma) - naBase) < 1e-12);

		// --- O PRE-REQUISITO DO SSJ4 (a porta que era codigo) -------------
		// Este e o caso que a bancada do Core so consegue simular; aqui ele passa pelo estado real.
		var perfilPrimal = Perfil(pl) with { Linhagem = Oozaru.LinhagemPrimal };
		var limpo = new EstadoDeForma { Atual = "ssj3" };
		limpo.Maestria.Por("ssj3", 100);
		Checa("SSJ4 recusado sem Oozaru Dourado (estado real)",
			  limpo.Avaliar("ssj4", pl.Ficha.BP, 1, false, perfilPrimal) == RecusaForma.SemFormaAnterior);

		// ======================== O SEGURO DA AURA OF DESTRUCTION ========================
		// A regra so vale se ela DISPARAR. Aqui o corpo e levado a morte de VERDADE, pelo funil de
		// verdade (`Corpo.Ferir` -> `DeveMorrer` -> `Morrer()`), com a aura ligada -- e depois sem
		// ela, pra provar que o seguro e 1x por luta e nao um escudo permanente.
		// ==============================================================================
		pl.Ficha.dead = false;
		pl.Ficha.KO = false;
		pl.Combate!.Reviver();
		pl.PoderDaDestruicao.Aprendida = true;
		pl.PoderDaDestruicao.Real = 100;
		pl.PoderDaDestruicao.Atual = 100;
		pl.PoderDaDestruicao.Ligada = true;
		pl.AuraSalvouNestaLuta = false;
		pl.UltimoGolpeRecebido = 1000;
		AplicarDisciplina(pl);
		Checa("a aura ligada escreve a reducao de dano no corpo",
			  pl.Combate.ReducaoDeDano > 20, $"{pl.Combate.ReducaoDeDano:0.#}");

		MatarDeVerdade(pl);
		Checa("a aura NEGOU a morte", !pl.Ficha.dead, "morreu mesmo com a aura ligada");
		Checa("...e a aura foi FORCOSAMENTE desligada", !pl.PoderDaDestruicao.Ligada);
		Checa("...e o corpo ficou de pe por um fio, nao curado",
			  pl.Combate.Corpo.Partes.Where(bp => !bp.Decepado).All(bp => bp.Fracao is >= 0.049 and <= 0.06),
			  string.Join(",", pl.Combate.Corpo.Partes.Select(bp => $"{bp.Fracao:0.###}")));
		Checa("...e a reducao de dano caiu junto com o toggle",
			  Math.Abs(pl.Combate.ReducaoDeDano) < 1e-9, $"{pl.Combate.ReducaoDeDano}");

		// A SEGUNDA VEZ NA MESMA LUTA NAO SEGURA. Sem esta checagem, "1x por luta" seria
		// indistinguivel de "sempre".
		MatarDeVerdade(pl);
		Checa("o segundo golpe fatal MATA (o seguro e 1x por luta)", pl.Ficha.dead);

		// ======================== A MAESTRIA DAS QUATRO FORMAS DIVINAS ========================
		// Ela vem DEPOIS do bloco da aura de proposito: aquele bloco deixa o Poder da Destruicao
		// aprendido, e as duas escolas se excluem -- este bloco precisa escolher qual esta aprendida em
		// cada medida, e o `finally` dele e o que devolve a ficha como estava.
		//
		// Mora em arquivo proprio (`GameServer.DisciplinaFormaTeste.cs`) pelo mesmo motivo das outras
		// secoes deste arquivo: ela mexe em disciplina, forma, liberadas e maestria.
		// ==================================================================================
		AMaestriaDasFormasDeDisciplina(pl, Checa);

		// =====================================================================
		// A FERA: possessao, prazo de controle e a porta do SSJ4
		//
		// A bancada do Core prova a CURVA e os BITS; o que so daqui se ve e a CADEIA -- que perder
		// o prazo poe um `Cerebro` no corpo de um jogador logado, que o mesmo laco que dirige o
		// clone da mente passa a dirigi-lo, que a forma acabando devolve as redeas, e que sair do
		// Dourado dominado deixa o SSJ4 liberado E a estreia dele DEVENDO. Cada um desses passos ja
		// foi, em alguma camada deste port, uma regra escrita e nunca ligada.
		// =====================================================================
		pl.Ficha.dead = false;
		pl.Ficha.KO = false;
		pl.Combate!.Reviver();
		Transformar(pl, subir: false);

		Checa("apertar C nunca oferece a linha do Oozaru",
			  pl.Forma.Proxima(pl.Ficha.BP, Perfil(pl)) is not { Linha: LinhaDeForma.Oozaru },
			  pl.Forma.Proxima(pl.Ficha.BP, Perfil(pl))?.Id ?? "nenhuma");

		bool temRabo = pl.Combate.Corpo.Achar("Rabo") is { Decepado: false };
		if (!temRabo || !string.Equals(pl.Race, "Saiyan", StringComparison.OrdinalIgnoreCase))
		{
			// PULA E DIZ QUE PULOU. Um bloco que some calado num personagem sem rabo daria uma
			// bancada verde que nao testou nada -- e e assim que uma regressao passa.
			GD.Print($"  --   FERA: pulado ({pl.Race}, rabo {temRabo}) -- precisa de Saiyajin com rabo");
		}
		else
		{
			pl.Forma.Maestria.Por(Oozaru.IdRegular, 0);
			EscutaDeAvisos = [];
			Apeshit(pl);
			List<string> naLua = Ouvido();
			Checa("a lua faz a fera", pl.Oozaru == FormaOozaru.Regular, pl.Oozaru.ToString());
			Checa("com 0% de dominio o corpo ainda e do dono no primeiro instante",
				  pl.Cerebro == null && !SemAsRedeas(pl), "");
			Checa("...e o fio diz que o corpo e dele", !EstadoDe(pl, NowMs()).SemRedeas);

			// ============================ O PRAZO E DITO NA HORA DA TRANSFORMACAO ============================
			// "uma regra que so se anuncia quando ja te puniu e uma armadilha, nao uma regra" -- e o numero
			// dito TEM que ser o numero que vai valer, senao o aviso e pior que o silencio.
			//
			// COMO REPROVA SE A REGRA SUMIR: apague o segundo `Avisar` do `Apeshit` e a primeira linha cai;
			// troque o `SegundosDeControle(...)` de la por uma constante e a segunda cai junto com ela.
			// ============================================================================================
			double prazoCru = Oozaru.SegundosDeControle(0, FormaOozaru.Regular);
			Checa("virar fera JA DIZ por quanto tempo o corpo e seu",
				  naLua.Any(a => a.Contains("redeas", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", naLua));
			Checa($"...e o numero dito e o da curva ({prazoCru:0} s)",
				  naLua.Any(a => a.Contains($"{prazoCru:0} s")), string.Join(" | ", naLua));

			// O PRAZO E CONTADO NO RELOGIO REAL (`SegundosNaForma` deriva do `OozaruAte`), e uma
			// bancada sincrona nao gasta tempo real. Envelhecer a forma na mao e o unico jeito de
			// exercitar o vencimento sem por um `Thread.Sleep` de seis segundos no teste.
			EscutaDeAvisos = [];
			pl.OozaruAte -= (long)(Oozaru.SegundosDeGraca * 1000) + 500;
			TickDoOozaru(pl, 0.1);
			List<string> aoPerder = Ouvido();

			Checa("vencido o prazo, o SERVIDOR assume o corpo", pl.Cerebro != null);
			Checa("...e o input do dono passa a ser recusado", SemAsRedeas(pl));

			// ============================ A PUNICAO TEM QUE FALAR, E TEM QUE DIZER A SAIDA ============================
			// Este e o buraco que a fase anterior fechou, e ele e invisivel por natureza: o corpo para de
			// responder a TODAS as teclas de corpo e a unica resposta possivel (meditar) nao aparece em lugar
			// nenhum da tela. Quem aperta tudo e nao ve nada acontecer conclui que o jogo travou -- e um
			// jogador que acha que achou um bug nao aprende a regra, ele reporta.
			//
			// COMO REPROVA SE A REGRA SUMIR: apague o primeiro `Avisar` do `TomarAsRedeas` e a linha 1 cai;
			// apague o segundo (o que ensina o M) e caem as linhas 2 e 3 -- e sao elas que separam "avisou"
			// de "avisou o que fazer", que e a diferenca que custou a sessao passada.
			//
			// A LINHA 3 E A MAIS ESPECIFICA: o prazo dito e LIDO do `RaivaDoOozaru` vivo, nao da constante.
			// Trocar `pl.RaivaDoOozaru` por `Oozaru.SegundosMeditandoAteCair` naquela frase daria o mesmo
			// numero HOJE (a raiva nasce cheia) e mentiria pra quem ja meditou um pouco antes de perder as
			// redeas. Por isso a bancada gasta metade da raiva ANTES de conferir: o valor esperado deixa de
			// coincidir com a constante, e so a leitura certa passa.
			// ======================================================================================================
			Checa("perder o controle AVISA o dono",
				  aoPerder.Any(a => a.Contains("PERDE O CONTROLE")), string.Join(" | ", aoPerder));
			Checa("...e o aviso ensina a SAIDA (a tecla M), que e a unica que ainda funciona",
				  aoPerder.Any(a => a.Contains("MEDITE")), string.Join(" | ", aoPerder));

			DesfazerOozaru(pl, "bancada: recomecar pra medir a raiva parcial");
			pl.Forma.Maestria.Por(Oozaru.IdRegular, 0);
			Apeshit(pl);
			pl.RaivaDoOozaru *= 0.5;   // ele meditou um pouco ANTES de perder as redeas
			double raivaQueFalta = pl.RaivaDoOozaru;
			EscutaDeAvisos = [];
			pl.OozaruAte -= (long)(Oozaru.SegundosDeGraca * 1000) + 500;
			TickDoOozaru(pl, 0.1);
			aoPerder = Ouvido();
			Checa($"...e o prazo de meditacao dito e o que FALTA, nao a constante ({raivaQueFalta:0} s)",
				  aoPerder.Any(a => a.Contains($"{raivaQueFalta:0} s")), string.Join(" | ", aoPerder));

			// ============================ MEDITAR DEVOLVE O CONTROLE ============================
			// A saida existe no servidor (passo 2 do `TickDoOozaru`), esta desbloqueada no despacho
			// (`ComandoDeCorpo` nao lista `Activity`) e e a UNICA -- e as tres coisas so valem se a
			// terceira acontecer de verdade.
			//
			// O `OozaruAte` E EMPURRADO PRA LONGE de proposito. Sem isso, "a forma caiu" nao distinguiria
			// "ele meditou" de "o prazo da forma venceu sozinho" -- e o prazo VENCE, sempre, em 300 s. Com
			// dez minutos de forma pela frente, a unica coisa que pode derrubar a fera aqui e a raiva.
			//
			// COMO REPROVA SE A REGRA SUMIR: apague o bloco `if (pl.Ficha.med)` do `TickDoOozaru` e o laco
			// roda os 4000 tiques inteiros sem soltar o corpo; tire o `DevolverAsRedeas` do
			// `DesfazerOozaru` e a forma cai com o cerebro ainda pendurado (o jogador em forma base
			// assistindo o proprio boneco andar sozinho, sem nada a fazer alem de deslogar).
			// ==================================================================================
			pl.OozaruAte = NowMs() + 600_000;
			pl.Ficha.med = true;
			double meditou = 0;
			for (int t = 0; t < 4000 && pl.Oozaru != FormaOozaru.Nao; t++)
			{
				TickDoOozaru(pl, 0.1);
				meditou += 0.1;
			}
			pl.Ficha.med = false;
			Checa("meditando, a fera se desfaz mesmo com a forma longe de acabar",
				  pl.Oozaru == FormaOozaru.Nao, $"parou em {pl.Oozaru} apos {meditou:0.#}s");
			Checa("...e no prazo do `angertick`, nao antes nem 'em algum momento'",
				  Math.Abs(meditou - raivaQueFalta) < 1.0,
				  $"{meditou:0.#}s contra os {raivaQueFalta:0.#}s que faltavam");
			Checa("...e as redeas voltam pra mao do dono",
				  pl.Cerebro == null && !SemAsRedeas(pl) && !EstadoDe(pl, NowMs()).SemRedeas);

			// e de volta ao estado que o resto deste bloco espera: fera crua, sem controle
			pl.Forma.Maestria.Por(Oozaru.IdRegular, 0);
			Apeshit(pl);
			pl.OozaruAte -= (long)(Oozaru.SegundosDeGraca * 1000) + 500;
			TickDoOozaru(pl, 0.1);
			Checa("(recomeco) o servidor volta a assumir o corpo", SemAsRedeas(pl));

			// ============================ E O RELOGIO DA CENA TEM QUE ESCORRER JUNTO ============================
			// Desde que a CINEMATICA entrou no `PodeMexerOCorpo` (o pedido do dono: *"npcs estao
			// conseguindo SE MOVER ENQUANTO TRANSFORMAM"*), quem esta preso numa cena nao anda -- e o
			// `Apeshit` acima marca os 4,0 s da cena do macaco (`Cinematicas.Oozaru`).
			//
			// **A BANCADA PULA O TEMPO PELO PRAZO, E NAO PELO RELOGIO.** Ela empurra o `OozaruAte` pra
			// tras pra dizer "os seis segundos de graca passaram", mas nada aqui roda o `TickDaForma`,
			// que e o UNICO lugar que abate `CenaSegundos`. Resultado: um macaco que, so pra esta
			// bancada, ficava possuido E em cinematica ao mesmo tempo -- e travava.
			//
			// **EM JOGO ISSO NAO ACONTECE**, e por construcao: `Oozaru.SegundosDeControle` tem piso
			// `SegundosDeGraca = 6` e a cena prende 4,0 s. Quando a fera assume o corpo, a cena dela ja
			// acabou ha dois segundos. Entao esta linha nao afrouxa regra nenhuma: ela faz o tempo
			// passar aqui do mesmo jeito que passa no mundo, onde os dez relogios do corpo correm
			// juntos (ver `TickDosRelogiosDoCorpo`).
			// ================================================================================================
			for (int t = 0; t < 200 && pl.CenaSegundos > 0; t++) TickDaForma(pl, 0.1);
			Checa("...e a cena da fera escorreu (o mundo anda os dez relogios juntos)",
				  pl.CenaSegundos <= 0, $"{pl.CenaSegundos:0.#}s");

			// ============================ O QUE MAIS E RECUSADO, ALEM DE ANDAR ============================
			// A recusa vivia num lugar so -- o portao de MOVIMENTO -- e soco, guarda, carga de Ki,
			// habilidade, transformacao e Zanzoken do dono passavam inteiros com a fera solta.
			//
			// COMO REPROVA SE A REGRA SUMIR: tire qualquer um destes ids do `ComandoDeCorpo`, ou o
			// `SemAsRedeas` da porta do `Handle`, e a linha cai.
			// ============================================================================================
			Checa("a fera recusa soco, guarda, carga, habilidade, transformacao e Zanzoken",
				  SemAsRedeas(pl)
				  && ComandoDeCorpo(Protocol.C2S.Action) && ComandoDeCorpo(Protocol.C2S.Guard)
				  && ComandoDeCorpo(Protocol.C2S.Carregar) && ComandoDeCorpo(Protocol.C2S.Habilidade)
				  && ComandoDeCorpo(Protocol.C2S.Transformar) && ComandoDeCorpo(Protocol.C2S.Zanzoken));
			// ...E O QUE **NAO** PODE SER RECUSADO. Meditar e a UNICA saida de quem nao tem pericia
			// (`TickDoOozaru`, passo 2): barrar `Activity` transformaria a paralisia em punicao sem
			// resposta. Esta linha reprova no dia em que alguem "fechar tudo" por seguranca.
			Checa("...mas NAO tira a saida: meditar, falar e os menus continuam passando",
				  !ComandoDeCorpo(Protocol.C2S.Activity) && !ComandoDeCorpo(Protocol.C2S.Chat)
				  && !ComandoDeCorpo(Protocol.C2S.Verbo));

			// A FERA ANDA PELO MESMO LACO DO CLONE. Sem esta checagem, "reusei a IA" seria uma
			// afirmacao do comentario e nao do codigo.
			// ============================ POSICAO E ANIMACAO SAO COISAS DIFERENTES ============================
			// Esta bancada conferia SO a posicao -- e ficou verde durante todo o tempo em que o dono via
			// o macaco DESLIZANDO. Um corpo que muda de lugar com `Moving` falso e exatamente isso, e
			// nenhuma linha daqui sabia perguntar. Agora o passo e medido no PACOTE (`EstadoDe`), que e
			// o unico lugar onde posicao, direcao e "andando" viajam juntos.
			//
			// COMO REPROVA SE A REGRA SUMIR: apague o `npc.Moving = ...` do `TickDosCorposSemDono`, ou o
			// `Moving = pl.Moving` do `EstadoDe`, ou volte o `pl.Moving = false` cego do portao de input,
			// e "andando no fio" cai enquanto "se move sozinho" continua verde -- que e o defeito.
			// ============================================================================================
			Jandirus.Core.World.Vec2 antes = pl.Pos;
			bool andandoNoFio = false, semRedeasNoFio = true;
			for (int t = 0; t < 20; t++)
			{
				TickDosCorposSemDono(0.1);
				EntityState fio = EstadoDe(pl, NowMs());
				andandoNoFio |= fio.Moving;
				semRedeasNoFio &= fio.SemRedeas;
			}
			Checa("o corpo possuido se move sozinho", (pl.Pos - antes).LengthSquared > 1,
				  $"{antes} -> {pl.Pos}");
			Checa("...e o snapshot diz que ele esta ANDANDO (senao ele desliza na tela do dono)",
				  andandoNoFio);
			Checa("...e diz, em TODO tique, que o corpo esta sem redeas", semRedeasNoFio);

			DesfazerOozaru(pl, "bancada: fim da fera");
			Checa("acabar a forma devolve as redeas",
				  pl.Cerebro == null && !SemAsRedeas(pl), "");
			// O AVESSO DO BIT. Sem esta linha, um `SemRedeas = true` cravado passaria em tudo acima --
			// e o cliente ficaria com o corpo travado depois que a forma acabasse.
			Checa("...e o fio para de dizer que ele esta sem redeas", !EstadoDe(pl, NowMs()).SemRedeas);

			// ============================ A RAMPA, NO CAMINHO VIVO ============================
			// A bancada do Core prova a CURVA (`SegundosDeControle`) como funcao pura. O que so daqui se
			// ve e que o `TickDoOozaru` a CONSULTA -- com a maestria de verdade e com a forma de verdade.
			// Um passo 5 escrito com `Oozaru.SegundosDeGraca` fixo no lugar da chamada daria uma curva
			// perfeita no Core e um jogo em que dominar a fera nao compra um segundo de controle.
			//
			// As duas linhas sao os dois lados do mesmo numero, e e o par que prova a rampa: o prazo que
			// derruba um novato (6 s) NAO derruba quem tem metade do dominio, e o prazo DELE derruba.
			// Uma so passaria tambem num sistema que nunca possui (ou que sempre possui).
			//
			// COMO REPROVA SE A REGRA SUMIR: troque o `SegundosDeControle(pl.MaestriaDaFera, pl.Oozaru)`
			// do passo 5 por `Oozaru.SegundosDeGraca` e a primeira cai; troque por `PositiveInfinity` (ou
			// apague o passo) e cai a segunda.
			// ==============================================================================
			pl.Forma.Maestria.Por(Oozaru.IdRegular, 50);
			Apeshit(pl);
			double prazoMeio = Oozaru.SegundosDeControle(50, FormaOozaru.Regular);
			pl.OozaruAte -= (long)(Oozaru.SegundosDeGraca * 1000) + 500;
			TickDoOozaru(pl, 0.1);
			Checa($"com 50% de dominio, o prazo do novato ({Oozaru.SegundosDeGraca:0} s) NAO derruba mais",
				  pl.Cerebro == null, $"perdeu o controle com {Oozaru.SegundosDeGraca + 0.5:0.#}s de forma");

			pl.OozaruAte -= (long)((prazoMeio - Oozaru.SegundosDeGraca) * 1000) + 500;
			TickDoOozaru(pl, 0.1);
			Checa($"...mas o prazo DELE ({prazoMeio:0} s) derruba -- a rampa esta ligada no tique",
				  pl.Cerebro != null, $"aguentou alem de {prazoMeio:0.#}s");
			DesfazerOozaru(pl, "bancada: fim da fera de meio dominio");

			// 100% NAO PERDE MAIS. O avesso da checagem acima -- sem ele, "o prazo vence" passaria
			// tambem num sistema que sempre possui.
			pl.Forma.Maestria.Por(Oozaru.IdRegular, 100);
			Apeshit(pl);
			pl.OozaruAte -= (long)(Oozaru.SegundosRegular * 1000) - 1000;   // quase no fim da forma
			TickDoOozaru(pl, 0.1);
			Checa("a fera DOMINADA nunca escapa", pl.Cerebro == null && pl.Oozaru != FormaOozaru.Nao);
			DesfazerOozaru(pl, "bancada: fim da fera dominada");

			// ============================ AS DUAS FECHADURAS DO DOURADO, NO CAMINHO VIVO ============================
			// A bancada do Core ja prova o `Oozaru.PodeDourado` isolado. O que so daqui se ve e que o
			// `Apeshit` -- a funcao que a lua, o botao e (amanha) a lua artificial chamam -- passa por ele
			// e RECUSA falando o motivo. As tres recusas sao diferentes de proposito:
			//
			//   1. Primal com o SSJ1 quase dominado -> nao vira NADA (nem o regular), e a frase diz a
			//      maestria que falta. Sem o numero, o jogador fica em SSJ olhando pra lua achando que o
			//      jogo travou -- e isso ja foi escrito como o motivo de a mensagem existir.
			//   2. Saiyajin comum em SSJ -> nao vira NADA. E fiel ao DM (`GoldenApeshit` volta sem fazer
			//      nada e o `else` do original ja tinha escolhido o Dourado), e e o que separa as duas
			//      linhagens: nao e "vira o regular", e "a lua nao te responde".
			//   3. Primal com o SSJ1 dominado, mas NA BASE -> vira o REGULAR. O gate do Dourado nao e o
			//      unico teste; estar em SSJ e a primeira pergunta do `QualSai`. Sem esta linha, "a lua da
			//      o Dourado" passaria tambem num sistema que ignora a forma atual.
			//
			// COMO REPROVA SE A REGRA SUMIR: tire `PedeMaestria`/`PedeMaestriaDe` da entrada
			// `oozaru_dourado` e a linha 1 vira Dourado; tire o `PedeLinhagem` e a 2 vira Dourado; troque
			// o `estaEmSsj` do `QualSai` por `true` e a 3 vira Dourado.
			// ====================================================================================================
			pl.Ficha.SaiyanLineage = Oozaru.LinhagemPrimal;
			pl.Forma.Maestria.Por("ssj1", 99);
			pl.Forma.Maestria.Por(Oozaru.IdDourado, 100);
			pl.Forma.Liberadas.Remove(Catalogo.Rede("ssj4"));
			pl.Forma.EstreiaVista.Remove(Catalogo.Rede("ssj4"));

			pl.Ficha.Ki = pl.Ficha.MaxKi;
			pl.Forma.Entrar("ssj1");
			AplicarForma(pl);
			EscutaDeAvisos = [];
			Apeshit(pl);
			List<string> semMaestria = Ouvido();
			Checa("Primal em SSJ sem o SSJ1 DOMINADO nao vira nada (nem o regular)",
				  pl.Oozaru == FormaOozaru.Nao, pl.Oozaru.ToString());
			Checa("...e a recusa diz quanta maestria falta",
				  semMaestria.Any(a => a.Contains("99") && a.Contains("100")),
				  string.Join(" | ", semMaestria));

			string linhagemReal = pl.Ficha.SaiyanLineage;
			pl.Ficha.SaiyanLineage = "Saiyan";
			pl.Forma.Maestria.Por("ssj1", 100);
			EscutaDeAvisos = [];
			Apeshit(pl);
			List<string> semLinhagem = Ouvido();
			Checa("Saiyajin comum em SSJ tambem nao vira nada -- a linhagem e a outra fechadura",
				  pl.Oozaru == FormaOozaru.Nao, pl.Oozaru.ToString());
			Checa("...e a recusa dele NAO fala de maestria (seria mandar treinar a coisa errada)",
				  semLinhagem.Count > 0 && !semLinhagem.Any(a => a.Contains("100%")),
				  string.Join(" | ", semLinhagem));
			pl.Ficha.SaiyanLineage = linhagemReal;

			// 3. NA BASE, com as duas fechaduras abertas: sai o REGULAR.
			Transformar(pl, subir: false);
			Apeshit(pl);
			Checa("na BASE, o Primal com tudo aberto vira o macaco COMUM (o Dourado exige o SSJ)",
				  pl.Oozaru == FormaOozaru.Regular, pl.Oozaru.ToString());
			DesfazerOozaru(pl, "bancada: fim do regular");

			// --- O DOURADO DOMINADO ABRE O SSJ4 SEM GASTAR A ESTREIA -------------------
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			pl.Forma.Entrar("ssj1");
			AplicarForma(pl);
			Apeshit(pl);
			Checa("em SSJ, Primal e com o SSJ1 dominado, a lua da o DOURADO",
				  pl.Oozaru == FormaOozaru.Dourado, pl.Oozaru.ToString());

			// ============================ A QUEDA PRO SSJ4 NAO PODE TER CENA ============================
			// O dono foi textual: *"n tem cinematica, apenas o ozaru e desfeito e o player ao inves de
			// voltar pra forma base ele cai no estagio de ssj4"*. O `AnunciarForma(..., semCena: true)` e a
			// unica `semCena` do jogo, e o efeito dela e um BYTE que some no fio -- em jogo, apagar aquele
			// argumento nao daria erro nenhum: daria um jogador preso ~2 s numa cena encurtada (a maestria
			// do SSJ4 dele e zero) por uma transformacao que ele nem pediu, logo depois de sair de uma
			// forma que ja tem cena propria.
			//
			// A escuta le o degrau NO FUNIL (`AnunciarForma`), que e onde ele nasce -- refazer a conta aqui
			// seria conferir a conta da bancada.
			//
			// COMO REPROVA SE A REGRA SUMIR: apague o `semCena: true` da chamada no `DesfazerOozaru` e o
			// degrau vira `Encurtada`; apague a chamada inteira e cai a linha do "o mundo foi avisado" --
			// que e o defeito anterior, o corpo virando SSJ4 no servidor e continuando base em TODAS as
			// telas, inclusive na do dono.
			// ========================================================================================
			EscutaDeAnuncios = [];
			EscutaDeFeras = [];
			DesfazerOozaru(pl, "bancada: fim do Dourado");
			var anunciados = EscutaDeAnuncios ?? [];
			var feras = EscutaDeFeras ?? [];
			EscutaDeAnuncios = null;
			EscutaDeFeras = null;

			Checa("sair do Dourado dominado cai em SSJ4", pl.Forma.Atual == "ssj4", pl.Forma.Atual);
			Checa("...e a ZONA e avisada da forma nova",
				  anunciados.Any(a => a.Para == "ssj4"),
				  string.Join(" | ", anunciados.Select(a => $"{a.De}->{a.Para}/{a.Degrau}")));
			Checa("...SEM CENA NENHUMA (o degrau anunciado e `Nenhuma`)",
				  anunciados.Where(a => a.Para == "ssj4").All(a => a.Degrau == DegrauDeCena.Nenhuma),
				  string.Join(" | ", anunciados.Select(a => $"{a.De}->{a.Para}/{a.Degrau}")));
			// E A ORDEM: o "deixei de ser macaco" tem que sair ANTES do SSJ4, senao o cliente acende a
			// aura da forma nova e o pacote da fera a apaga em seguida (o jogador vira um SSJ4 apagado).
			Checa("...e o fim da fera foi anunciado ANTES do SSJ4",
				  feras.Any(f => f.Forma == FormaOozaru.Nao) && anunciados.Any(a => a.Para == "ssj4"));
			Checa("...e o SSJ4 fica LIBERADO", pl.Forma.Despertou("ssj4"));
			Checa("...e a ESTREIA dele continua DEVENDO", !pl.Forma.JaViuAEstreia("ssj4"));

			// ============================ O SELETOR NAO PODE COMER A ESTREIA ============================
			// O `DirectSSJ` lia `Despertou` -- o bit que o Oozaru acabou de ligar -- e o `Entrar()` dele
			// marcaria a estreia enquanto anuncia `estreia: false`. A cena morreria calada por um caminho
			// lateral. Esta checagem vale mais que as outras deste bloco: ela e a UNICA que falha se
			// alguem trocar o bit de volta, porque o defeito nao aparece na tela nem no log.
			// ======================================================================================
			pl.Forma.Entrar(Catalogo.IdBase);
			AplicarForma(pl);
			FormaDiretaG4(pl, "4");
			Checa("o atalho DirectSSJ NAO leva a uma forma cuja estreia ainda deve",
				  pl.Forma.NaBase, pl.Forma.Atual);
			Checa("...e nao gastou a estreia do SSJ4", !pl.Forma.JaViuAEstreia("ssj4"));

			// ============================ E A CINEMATICA TOCA MESMO, PELO C, ATE A ZONA ============================
			// Esta e a metade que a bancada do Core NAO alcanca. La o teste e `jogador.Entrar("ssj4")` --
			// o bit, sozinho. Aqui o caminho e o do dedo do jogador: apertar C repetidas vezes, subir a
			// escada pelo `Transformar`, e conferir o DEGRAU que saiu no `AnunciarForma`. E ele que o
			// cliente le; um `Entrar` devolvendo `true` que nao virasse `DegrauDeCena.Estreia` no fio seria
			// a mesma cena perdida, com o bit certo do lado.
			//
			// COMO REPROVA SE A REGRA SUMIR: troque o `Liberar("ssj4")` do `DesfazerOozaru` por `Entrar` e
			// a estreia ja estaria gasta -- o anuncio sairia `Encurtada` (maestria 0) e as duas linhas
			// caem; troque o `estreia` do `AnunciarForma` por um literal `false` e cai a primeira.
			// ====================================================================================================
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			pl.Ficha.KO = false;
			pl.Ficha.dead = false;
			// A RAIVA E REACESA AQUI -- ver o bloco do `AmigoAbatido` la em cima. Este e o ultimo
			// trecho que sobe a escada e ele roda depois da lua inteira; contar com a janela acesa
			// minutos atras seria fazer o resultado depender de quanto tempo a bancada leva pra rodar.
			AmigoAbatido(pl, "um amigo de bancada", NivelDeRaiva.Extrema);
			EscutaDeAnuncios = [];
			for (int c = 0; c < 12 && pl.Forma.Atual != "ssj4"; c++)
			{
				string antesDoC = pl.Forma.Atual;
				pl.Ficha.Ki = pl.Ficha.MaxKi;   // o C nao esta sendo testado contra o dreno aqui
				Transformar(pl, subir: true);
				if (pl.Forma.Atual == antesDoC) break;
			}
			var subindo = EscutaDeAnuncios ?? [];
			EscutaDeAnuncios = null;

			Checa("apertando C, a escada alcanca o SSJ4 que o macaco abriu",
				  pl.Forma.Atual == "ssj4",
				  string.Join(" -> ", subindo.Select(a => a.Para)));
			Checa("...e a CINEMATICA DE ESTREIA toca nessa primeira vez",
				  subindo.Any(a => a.Para == "ssj4" && a.Degrau == DegrauDeCena.Estreia),
				  string.Join(" | ", subindo.Select(a => $"{a.Para}/{a.Degrau}")));

			// ...E SO NESSA. Sem esta linha, "a cena toca" passaria tambem num sistema que a toca sempre --
			// e a estreia deixaria de ser estreia.
			Transformar(pl, subir: false);
			EscutaDeAnuncios = [];
			for (int c = 0; c < 12 && pl.Forma.Atual != "ssj4"; c++)
			{
				string antesDoC = pl.Forma.Atual;
				pl.Ficha.Ki = pl.Ficha.MaxKi;
				Transformar(pl, subir: true);
				if (pl.Forma.Atual == antesDoC) break;
			}
			var deNovo = EscutaDeAnuncios ?? [];
			EscutaDeAnuncios = null;
			Checa("...e da segunda vez ela NAO toca mais",
				  deNovo.Any(a => a.Para == "ssj4")
				  && deNovo.Where(a => a.Para == "ssj4").All(a => a.Degrau != DegrauDeCena.Estreia),
				  string.Join(" | ", deNovo.Select(a => $"{a.Para}/{a.Degrau}")));

			// ============================ QUEM NUNCA PASSOU PELO DOURADO NAO ENTRA NO SSJ4 ============================
			// O avesso de tudo acima, e no caminho vivo: sem a porta aberta, o C nao alcanca o degrau por
			// mais BP que se tenha -- e a recusa aponta pra FERA, nao pra o treino.
			//
			// Aquela ultima metade e a que economiza o tempo do jogador. `PorQueNao` le
			// `PedeMaestriaDe` pra montar a frase; lendo o degrau anterior (que e o SSJ3) ela mandaria
			// dominar a forma errada -- horas de treino com o jogo dizendo que era essa.
			//
			// COMO REPROVA SE A REGRA SUMIR: apague `PedeFormaDespertada`/`PedeMaestria` da entrada `ssj4`
			// e o C sobe direto; troque o `Catalogo.Def(candidato.PedeMaestriaDe) ?? Anterior(...)` do
			// `PorQueNao` pelo `Anterior` puro e a frase passa a falar de Super Saiyajin 3.
			// ====================================================================================================
			Transformar(pl, subir: false);
			pl.Forma.Liberadas.Remove(Catalogo.Rede("ssj4"));
			pl.Forma.EstreiaVista.Remove(Catalogo.Rede("ssj4"));
			pl.Forma.Liberadas.Remove(Catalogo.Rede(Oozaru.IdDourado));
			pl.Forma.EstreiaVista.Remove(Catalogo.Rede(Oozaru.IdDourado));
			pl.Forma.Maestria.Por(Oozaru.IdDourado, 0);

			EscutaDeAvisos = [];
			for (int c = 0; c < 12; c++)
			{
				string antesDoC = pl.Forma.Atual;
				pl.Ficha.Ki = pl.Ficha.MaxKi;
				Transformar(pl, subir: true);
				if (pl.Forma.Atual == antesDoC) break;
			}
			List<string> naParede = Ouvido();
			Checa("sem nunca ter sido Oozaru Dourado, o C PARA antes do SSJ4",
				  pl.Forma.Atual != "ssj4", pl.Forma.Atual);
			Checa("...e a recusa manda virar OOZARU DOURADO, e nao treinar o SSJ3",
				  naParede.Any(a => a.Contains("Oozaru", StringComparison.OrdinalIgnoreCase)
								 && a.Contains("Dourado", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", naParede));

			ASincroniaDaZona(pl, Checa);
		}

		// AS FERRAMENTAS DE ADMIN FICAM FORA DO `if` DO SAIYAJIN DE PROPOSITO. O bloco acima precisa de
		// rabo e de genoma porque ele exercita a LUA, que confere os dois; o `admin_forma` existe
		// justamente pra ignorar essas portas -- e por isso ele tem que ser exercitado em qualquer
		// corpo, inclusive num que a lua nunca responderia. Ver `GameServer.AdminTeste.cs`.
		// A TECLA DE FORMA ANTES DAS FERRAMENTAS DE ADMIN, e a vizinhanca e o assunto: a secao abaixo
		// prova que o `admin_forma` IGNORA os requisitos, e esta prova que o comando do JOGADOR nao
		// ignora nenhum. Lidas em sequencia elas contam a diferenca entre as duas portas.
		ATeclaDeForma(pl, Checa, Ouvido);

		// LOGO DEPOIS DA TECLA DE FORMA, e o parentesco e o assunto: aquela secao prova que PEDIR uma
		// forma pelo nome nao pula portao, e esta prova que mudar o CAMINHO da tecla C tambem nao.
		OsGradesNoCaminhoDoC(pl, Checa, Ouvido);

		// E LOGO EM SEGUIDA O PRECO DELES. A secao acima prova por ONDE a tecla C anda; esta prova o
		// que cada degrau faz com o CORPO (a % de BP efetivo, a forca, a velocidade, a cadencia e a
		// pontaria) e que a preferencia atravessa o .json de verdade. Ver `GameServer.GradesTeste.cs`.
		AsContasDosGrades(pl, Checa, Ouvido);

		AsFerramentasDeAdmin(pl, Checa);

		// ============================ AS OUTRAS ESCADAS DE SANGUE, PELA MESMA TECLA C ============================
		// POR ULTIMO, e nao por ordem de importancia: esta secao troca a RACA do personagem uma vez por
		// linha (Saiyajin, meio-Saiyajin, Frost Demon, Namekuseijin, Alien, Heran) e compra skill no
		// livro dele. Tudo acima daqui mede a escada Saiyajin num corpo Saiyajin -- rodar antes faria
		// aquelas checagens medirem o estranho desta, que e o modo de falha que o cabecalho deste
		// arquivo descreve. Ela tem `finally` proprio e mora em `GameServer.RaciaisTeste.cs`.
		// =================================================================================================
		AsEscadasRaciaisAoVivo(pl, Checa);

		Transformar(pl, subir: false);
		// NINGUEM SAI DAQUI COM ESCUTA LIGADA. Elas sao estaticas: uma esquecida armada acumularia o
		// jogo inteiro numa lista que ninguem mais le -- vazamento de memoria por teste, que e o jeito
		// mais bobo de uma bancada estragar o servidor que ela veio proteger.
		EscutaDeAvisos = null;
		EscutaDeAnuncios = null;
		EscutaDeFeras = null;
		EscutaDeSincronia = null;
		GD.Print($"===== {ok} OK, {falhou} FALHA =====\n");
	}

	// =====================================================================
	// A CURVA DO MISTICO, MEDIDA NO CORPO
	// =====================================================================
	/// <summary>
	/// ============================ O QUE SO DAQUI SE VE ============================
	/// ============================ A TECLA DE FORMA -- `verbo forma &lt;id&gt;` ============================
	/// O jogador pode ligar uma tecla a uma transformacao (`Client/TelaDeTeclas.cs`), e ela estreou um
	/// comando: `TransformarPara`. Esta secao existe porque um atalho e o lugar mais provavel do jogo
	/// pra alguem pular um portao sem querer -- e porque a unica coisa que prova que ele NAO pula e
	/// exercitar o portao.
	///
	/// ============================ ELA ENTRA PELO `Verbo`, E NAO PELO `TransformarPara` ============================
	/// Chamar `TransformarPara` direto mediria a funcao que eu escrevi. O que precisa ser medido e a
	/// CADEIA: o `case "forma"` do switch de verbos existe, nao foi engolido por nenhum dos sete
	/// despachantes de prefixo que rodam antes dele, e cai na funcao certa. Um `case` escrito e nunca
	/// alcancado e a falha assinatura deste port.
	/// ======================================================================================================
	///
	/// ============================ E O QUE ELA MEDE NAO E O QUE EU ESCREVI ============================
	/// As tres checagens que valem alguma coisa nao leem o retorno de funcao nenhuma:
	///
	///   * o `pl.Forma.Atual` DEPOIS de uma recusa -- prova que a recusa nao deixou rastro;
	///   * o `pl.Ficha.ssjBuff` DEPOIS de um aceite -- prova que passou pelo `EntrarNaForma` e nao por
	///     um `Atual =` na mao. Escrever a forma sem o buff daria uma transformacao que nao multiplica
	///     nada, e o `Atual` diria que deu certo;
	///   * o `EscutaDeAnuncios` -- prova que a ZONA soube. Sem o anuncio, a tela de todo mundo (a do
	///     proprio dono inclusive) continua desenhando o corpo base: transformacao invisivel.
	/// ============================================================================================
	/// </summary>
	private void ATeclaDeForma(ServerPlayer pl, Action<string, bool, string> Checa,
							   Func<List<string>> Ouvido)
	{
		// ---------------------------------------------------------------- o corpo de partida
		Transformar(pl, subir: false);
		while (pl.Forma.Atual != Catalogo.IdBase && pl.Forma.Def != null) Transformar(pl, subir: false);
		pl.Ficha.KO = false;
		pl.Ficha.dead = false;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		AplicarForma(pl);

		Checa("[tecla] o corpo comeca na base", pl.Forma.Atual == Catalogo.IdBase, pl.Forma.Atual);

		// ---------------------------------------------------------------- 1. NAO PULA DEGRAU
		//
		// A checagem mais importante desta secao inteira. "Tecla 3 = SSJ3" e exatamente o que um
		// jogador espera ao ligar a tecla, e exatamente o que NAO pode acontecer -- senao o atalho
		// passa por cima da escada e o jogo perde a progressao que ele tem.
		EscutaDeAvisos = [];
		Verbo(pl, "forma", "ssj3");
		List<string> recusa3 = Ouvido();
		Checa("[tecla] pedir SSJ3 da base NAO transforma", pl.Forma.Atual == Catalogo.IdBase, pl.Forma.Atual);
		Checa("[tecla] ...e a recusa diz que ele vem DEPOIS de outra forma",
			  recusa3.Any(a => a.Contains("vem depois", StringComparison.OrdinalIgnoreCase)),
			  string.Join(" | ", recusa3));

		// ---------------------------------------------------------------- 2. A FORMA PEDIDA VEM
		double buffAntes = pl.Ficha.ssjBuff;
		EscutaDeAvisos = [];
		EscutaDeAnuncios = [];
		Verbo(pl, "forma", "ssj1");
		List<string> aceite = Ouvido();
		var anunciados = EscutaDeAnuncios ?? [];
		EscutaDeAnuncios = null;

		Checa("[tecla] pedir SSJ1 da base transforma", pl.Forma.Atual == "ssj1", pl.Forma.Atual);
		// O BUFF E A PROVA DE QUE PASSOU PELO `EntrarNaForma`. Um `Atual = "ssj1"` escrito na mao
		// deixaria esta linha em 1 e a de cima verde -- transformacao que nao multiplica nada.
		Checa("[tecla] ...e o multiplicador subiu (passou pelo EntrarNaForma)",
			  pl.Ficha.ssjBuff > buffAntes + 1e-9, $"{buffAntes} -> {pl.Ficha.ssjBuff}");
		Checa("[tecla] ...e a ZONA foi avisada (aura, cabelo, cinematica)",
			  anunciados.Any(a => a.Quem == pl.Id && a.Para == "ssj1"),
			  $"{anunciados.Count} anuncios");
		Checa("[tecla] ...e o jogador leu o nome da forma",
			  aceite.Any(a => a.Contains("Super Saiyajin", StringComparison.OrdinalIgnoreCase)),
			  string.Join(" | ", aceite));

		// ---------------------------------------------------------------- 3. A MESMA DE NOVO
		EscutaDeAvisos = [];
		Verbo(pl, "forma", "ssj1");
		List<string> jaEsta = Ouvido();
		Checa("[tecla] pedir a forma em que ja se esta responde, e nao cala",
			  jaEsta.Any(a => a.Contains("ja esta", StringComparison.OrdinalIgnoreCase)),
			  string.Join(" | ", jaEsta));

		// ---------------------------------------------------------------- 4. VOLTAR AO NORMAL
		EscutaDeAvisos = [];
		Verbo(pl, "forma", Catalogo.IdBase);
		Ouvido();
		Checa("[tecla] pedir a BASE recua pelo mesmo caminho da tecla X",
			  pl.Forma.Atual == Catalogo.IdBase, pl.Forma.Atual);

		// ---------------------------------------------------------------- 5. FORMA QUE NAO E DELE
		//
		// O Blue e da linha divina e este corpo nao tem ki divino nenhum -- e ele NAO pode virar por
		// atalho so porque o jogador conseguiu digitar o id. Vale por todas as portas: quem passa aqui
		// passa pelo `Avaliar` inteiro.
		EscutaDeAvisos = [];
		Verbo(pl, "forma", "blue");
		List<string> semDivino = Ouvido();
		Checa("[tecla] uma forma de outra linha NAO vem por atalho",
			  pl.Forma.Atual == Catalogo.IdBase, pl.Forma.Atual);
		Checa("[tecla] ...e a recusa nao e muda", semDivino.Count > 0, string.Join(" | ", semDivino));

		// ---------------------------------------------------------------- 6. ID QUE NAO EXISTE
		EscutaDeAvisos = [];
		Verbo(pl, "forma", "kamehameha_dourado_2");
		List<string> inventada = Ouvido();
		Checa("[tecla] um id inventado nao derruba nada e responde",
			  pl.Forma.Atual == Catalogo.IdBase && inventada.Count > 0, string.Join(" | ", inventada));

		// ---------------------------------------------------------------- 7. CAIDO
		//
		// A guarda de topo do `Transformar` -- ela e do GESTO e nao do degrau, entao nao mora no
		// `Avaliar` e teria sido a mais facil de esquecer no caminho novo.
		pl.Ficha.KO = true;
		EscutaDeAvisos = [];
		Verbo(pl, "forma", "ssj1");
		List<string> caido = Ouvido();
		pl.Ficha.KO = false;
		Checa("[tecla] caido nao transforma", pl.Forma.Atual == Catalogo.IdBase, pl.Forma.Atual);
		Checa("[tecla] ...e diz que e por estar caido",
			  caido.Any(a => a.Contains("caido", StringComparison.OrdinalIgnoreCase)),
			  string.Join(" | ", caido));

		// ---------------------------------------------------------------- 8. KI NO FIO
		//
		// Entrar numa forma sem folego e cair dela no segundo seguinte, e o `Avaliar` recusa por isso.
		// Medido AQUI porque um atalho e o gesto mais barato do jogo: sem esta porta, a tecla viraria
		// o jeito de entrar em forma com 1% de Ki repetidamente.
		double kiAntes = pl.Ficha.Ki;
		pl.Ficha.Ki = pl.Ficha.MaxKi * 0.02;
		EscutaDeAvisos = [];
		Verbo(pl, "forma", "ssj1");
		List<string> semKi = Ouvido();
		pl.Ficha.Ki = kiAntes;
		Checa("[tecla] Ki no fio nao transforma", pl.Forma.Atual == Catalogo.IdBase, pl.Forma.Atual);
		Checa("[tecla] ...e diz que o Ki e o problema",
			  semKi.Any(a => a.Contains("Ki", StringComparison.Ordinal)), string.Join(" | ", semKi));

		AplicarForma(pl);
	}

	/// <summary>
	/// ============================ OS GRADES NO CAMINHO DA TECLA C -- o verb `graus` ============================
	/// Pedido do dono: *"um verb no OTHER q ao clicar fala se desativei ou nao os grades; com eles
	/// LIGADOS, no ssj1 (masterizado ou nao) apertar C duas vezes passa pelos grades antes do ssj2;
	/// DESLIGADOS, pula direto pro ssj2"*.
	///
	/// ============================ POR QUE ELE PRECISA DE UM CORPO VIVO ============================
	/// A regra mora no Core (`EstadoDeForma.Proxima`), mas as tres coisas que podem quebrar sem
	/// ninguem ver sao todas de fora dele:
	///
	///   * **o `case "graus"`** existir no switch de verbos e nao ser engolido pelos sete despachantes
	///     de prefixo que rodam antes -- a falha assinatura deste port (botao que promete e nao faz);
	///   * **o `PorQueNao`** enxergar o mesmo caminho que o `Proxima`. Com os grades desligados e o
	///     SSJ2 trancado por BP, um `PorQueNao` que ainda visse o Grade 2 responderia *"pede 50% de
	///     maestria"* -- a porta do degrau que o jogador acabou de tirar do caminho;
	///   * **a preferencia atravessar o disco.** Ela e escrita em `DeJogador` e lida em
	///     `RestaurarFormaEDisciplina`, e este projeto ja perdeu duas escolhas de jogador exatamente
	///     nessa costura (o `wastaught` e a casa escolhida do Metamoriano).
	/// ==========================================================================================
	///
	/// E O CASO QUE MAIS IMPORTA E O DO SSJ1 **DOMINADO**: com 100% de maestria o SSJ1 vale 6x e os
	/// dois grades valem 3x e 4x, entao o seletor de sempre (o mais forte vence) os apaga da escada.
	/// E ali que a preferencia tem que aparecer -- e e ali que uma implementacao que so mexesse em
	/// multiplicador daria verde no papel e nada em jogo.
	/// </summary>
	private void OsGradesNoCaminhoDoC(ServerPlayer pl, Action<string, bool, string> Checa,
									  Func<List<string>> Ouvido)
	{
		// TUDO O QUE SERA REPOSTO PELO MESMO FUNIL QUE O LOGIN USA. `DeJogador` fotografa forma,
		// maestria, liberadas, limiares e disciplina; `RestaurarFormaEDisciplina` devolve. Reescrever
		// a reposicao a mao seria a copia da migracao morando dentro do teste que deveria vigia-la.
		CharacterSave antes = AccountStore.DeJogador(pl, 0);
		double bpAntes = pl.Ficha.BP;

		void ABase()
		{
			while (pl.Forma.Atual != Catalogo.IdBase && pl.Forma.Def != null) Transformar(pl, subir: false);
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			pl.Ficha.KO = false;
			pl.Ficha.dead = false;
		}

		string SobeUm()
		{
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			Transformar(pl, subir: true);
			return pl.Forma.Atual;
		}

		ABase();
		pl.Ficha.BP = 1e13;

		// O SSJ1 **DOMINADO** e os dois degraus seguintes ja liberados: sem `Liberar`, o passo 9 do
		// `Avaliar` cobraria FURIA no SSJ2 (o tronco Saiyajin desperta no luto) e a bancada estaria
		// medindo a raiva em vez do caminho. Os grades nao pedem furia -- eles pedem maestria.
		pl.Forma.Liberar("ssj1");
		pl.Forma.Liberar("ssj2");
		pl.Forma.Maestria.Por("ssj1", 100);
		Verbo(pl, "forma", "ssj1");
		Checa("[graus] o corpo parte do SSJ1 DOMINADO", pl.Forma.Atual == "ssj1", pl.Forma.Atual);

		// ---------------------------------------------------------------- 1. LIGADOS: passa pelos dois
		pl.Forma.GradesLigados = true;
		Checa("[graus] ligados, o C sai do SSJ1 dominado pro GRADE 2 (e nao pro SSJ2)",
			  SobeUm() == "grade2", pl.Forma.Atual);
		Checa("[graus] ...e do Grade 2 pro GRADE 3", SobeUm() == "grade3", pl.Forma.Atual);
		Checa("[graus] ...e so entao pro SSJ2", SobeUm() == "ssj2", pl.Forma.Atual);

		// ---------------------------------------------------------------- 2. O VERB: ele FALA
		ABase();
		Verbo(pl, "forma", "ssj1");
		EscutaDeAvisos = [];
		Verbo(pl, "graus", "");
		List<string> desligou = Ouvido();
		Checa("[graus] o verb `graus` chega no servidor e DESLIGA", pl.Forma.GradesLigados == false,
			  $"{pl.Forma.GradesLigados}");
		Checa("[graus] ...e diz em que pe ficou (o dono pediu que ele FALASSE)",
			  desligou.Any(a => a.Contains("direto", StringComparison.OrdinalIgnoreCase)),
			  string.Join(" | ", desligou));

		// ---------------------------------------------------------------- 3. DESLIGADOS: pula direto
		Checa("[graus] desligados, o C vai do SSJ1 direto pro SSJ2", SobeUm() == "ssj2", pl.Forma.Atual);

		// ---------------------------------------------------------------- 4. PULAR O GRADE NAO PULA O GATE
		//
		// A checagem que o dono pediu em voz alta: *"se a pessoa nao pode entrar no ssj2, ela ouve a
		// recusa DELE, nao cai no grade calada"*. Aqui o SSJ2 esta trancado por BP -- e o Grade 2, que
		// tem a porta do SSJ1 e a maestria de sobra, estaria alcancavel se ainda estivesse no caminho.
		//
		// O `Limiares = null` e pra a porta ser a CONSTANTE do catalogo: o limiar sorteado no
		// nascimento deste corpo poderia estar abaixo do BP que eu escrevo aqui, e a bancada mediria
		// "nao ha recusa" achando que mediu a recusa certa.
		ABase();
		Verbo(pl, "forma", "ssj1");
		LimiaresPessoais? limiaresAntes = pl.Forma.Limiares;
		pl.Forma.Limiares = null;
		pl.Ficha.BP = Catalogo.PortaSsj2 - 1;
		EscutaDeAvisos = [];
		Transformar(pl, subir: true);
		List<string> semPoder = Ouvido();
		pl.Ficha.BP = 1e13;
		pl.Forma.Limiares = limiaresAntes;

		Checa("[graus] com o SSJ2 fora de alcance, o C NAO desvia pro grade", pl.Forma.Atual == "ssj1",
			  pl.Forma.Atual);
		Checa("[graus] ...e a recusa e a do SSJ2, nao a maestria do Grade",
			  semPoder.Any(a => a.Contains("Super Saiyajin 2", StringComparison.Ordinal))
			  && !semPoder.Any(a => a.Contains("Grade", StringComparison.OrdinalIgnoreCase)),
			  string.Join(" | ", semPoder));

		// ---------------------------------------------------------------- 5. DESLIGAR ESTANDO NUM GRADE
		//
		// A pergunta que o desenho tinha que responder. A resposta e "fica" -- preferencia nao
		// transforma ninguem --, e o teste mede as duas metades: o corpo nao se mexe E o jogador e
		// avisado (o silencio aqui faria o botao parecer quebrado).
		ABase();
		pl.Forma.GradesLigados = true;
		Verbo(pl, "forma", "ssj1");
		SobeUm();
		Checa("[graus] (preparo) o corpo esta num grade", pl.Forma.Atual == "grade2", pl.Forma.Atual);

		EscutaDeAvisos = [];
		Verbo(pl, "graus", "");
		List<string> noGrade = Ouvido();
		Checa("[graus] desligar ESTANDO num grade nao transforma ninguem", pl.Forma.Atual == "grade2",
			  pl.Forma.Atual);
		Checa("[graus] ...e o jogador e avisado de que continua nele",
			  noGrade.Any(a => a.Contains("continua", StringComparison.OrdinalIgnoreCase)),
			  string.Join(" | ", noGrade));
		Checa("[graus] ...e o proximo C o tira de la (pro SSJ2, pulando o Grade 3)",
			  SobeUm() == "ssj2", pl.Forma.Atual);

		// ---------------------------------------------------------------- 6. CORPO SEM DONO: NADA MUDA
		//
		// O NPC sorteado e o cerebro da IA sobem por este mesmo `Proxima` e nao tem jogador pra
		// apertar botao nenhum. Com `null` eles ficam com a regra de ontem -- e as duas alternativas
		// os machucariam (ver `EstadoDeForma.GradesLigados`).
		var semDono = new EstadoDeForma();
		semDono.Liberar("ssj1");
		semDono.Liberar("ssj2");
		semDono.Maestria.Por("ssj1", 100);
		semDono.Entrar("ssj1");
		Checa("[graus] corpo SEM DONO (preferencia nula) continua no mais forte: SSJ1 dominado -> SSJ2",
			  semDono.Proxima(1e13, Perfil(pl))?.Id == "ssj2",
			  semDono.Proxima(1e13, Perfil(pl))?.Id ?? "nenhuma");

		// ---------------------------------------------------------------- 7. E ELA ATRAVESSA O DISCO
		ABase();
		pl.Forma.GradesLigados = false;
		CharacterSave gravado = AccountStore.DeJogador(pl, 0);
		Checa("[graus] a escolha e GRAVADA no save", gravado.GradesLigados == false,
			  $"{gravado.GradesLigados}");

		RestaurarFormaEDisciplina(pl, gravado);
		Checa("[graus] ...e volta do disco no login", pl.Forma.GradesLigados == false,
			  $"{pl.Forma.GradesLigados}");

		// SAVE DE ANTES DO VERB EXISTIR: o campo chega NULO e o login o traduz pra LIGADO, que e o
		// comportamento que aquele personagem ja tinha. Um `bool` cru teria desligado os grades do
		// servidor inteiro, calado, no primeiro login depois da mudanca.
		gravado.GradesLigados = null;
		RestaurarFormaEDisciplina(pl, gravado);
		Checa("[graus] save ANTIGO (campo nulo) volta LIGADO, e nao desligado",
			  pl.Forma.GradesLigados == true, $"{pl.Forma.GradesLigados}");

		// ---------------------------------------------------------------- repoe o corpo
		RestaurarFormaEDisciplina(pl, antes);
		pl.Ficha.BP = bpAntes;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		AplicarForma(pl);
	}

	/// <summary>
	/// A bancada do Core prova a CURVA (`FormasBench`, secao [10]) chamando o
	/// `EstadoDeForma.Multiplicador` com um perfil escrito a mao. Ela nao pode provar que o perfil
	/// **do jogo** carrega os dois campos que a curva le: a curva do Mistico e a unica do catalogo
	/// que sai da LINHAGEM e da maestria de KI DIVINO, e as duas chegam pelo `Perfil(pl)` --
	/// `pl.Ficha.Class` e `pl.Ficha.godki.mastery`. Apagar qualquer um dos dois daquele construtor
	/// deixaria o Core inteiro verde e daria 16x no corpo de um Prodigial maduro: metade do poder,
	/// sem erro nenhum na tela.
	///
	/// Entao aqui cada ponto da curva passa pela cadeia inteira -- `TickDaForma` -> `AplicarForma` ->
	/// `MultiplicadorDaForma` -> `Perfil(pl)` -> `ssjBuff` -> `powerlevel()` -- e o que se le no fim
	/// e o `expressedBP` da ficha, que e o numero que o jogo usa pra brigar.
	///
	/// E O CORPO ENTRA NA FORMA PELO CAMINHO DO JOGO: `ConcederMistico` (o gancho do ritual) e a
	/// tecla C (`Transformar`). Escrever `pl.Forma.Atual = "mistico"` mediria o catalogo com o
	/// servidor do lado.
	/// ==========================================================================
	/// </summary>
	private void ACurvaDoMisticoNoCorpo(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		// ---------------------------------------------------------------- o que sera reposto
		string classeAntes = pl.Ficha.Class, formaAntes = pl.Forma.Atual;
		Jandirus.Core.Stats.GodKiState? godkiAntes = pl.Ficha.godki;
		var liberadasAntes = new HashSet<int>(pl.Forma.Liberadas);
		var estreiasAntes = new HashSet<int>(pl.Forma.EstreiaVista);
		double maestriaAntes = pl.Forma.Maestria.De(Catalogo.IdMistico), kiAntes = pl.Ficha.Ki;

		try
		{
			pl.Ficha.KO = false;
			pl.Ficha.dead = false;
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			pl.Forma.Entrar(Catalogo.IdBase);

			// RACA QUALQUER: sem a classe da linhagem e sem ki divino nenhum. E o primeiro patamar da
			// regra do dono, e tambem o estado em que a maioria dos personagens vai receber o dom.
			pl.Ficha.Class = "";
			pl.Ficha.godki = null;
			AplicarForma(pl);

			ConcederMistico(pl, "bancada da curva");
			Transformar(pl, subir: true);
			Checa("o C leva ao Mistico assim que o ritual concede",
				  pl.Forma.Atual == Catalogo.IdMistico, pl.Forma.Atual);

			// SEM QUEIMAR A CENA, NADA ABAIXO SE MOVE: em cinematica o tique volta antes do
			// `AplicarForma` (ver `PassarACena`), e a curva inteira seria medida congelada.
			PassarACena(pl);

			// UM TIQUE E O QUE ATUALIZA O `ssjBuff` EM JOGO -- e por ele que cada ponto passa aqui. O
			// Ki e reposto porque o assunto deste bloco nao e o dreno: tanque vazio derruba a forma no
			// meio da medida e o resto viraria medida da base.
			double NoCorpo()
			{
				pl.Ficha.Ki = pl.Ficha.MaxKi;
				TickDaForma(pl, 0.1);
				Medir(pl);
				return pl.Ficha.ssjBuff;
			}

			// --- OS TRES PATAMARES, no corpo ------------------------------------------------
			double v16 = NoCorpo();
			double bp16 = pl.Ficha.expressedBP;
			Checa("raca qualquer, sem ki divino: o CORPO recebe 16x",
				  Math.Abs(v16 - 16) < 1e-6, $"ssjBuff {v16:0.####}");

			pl.Ficha.Class = Catalogo.ClasseProdigial;
			double v18 = NoCorpo();
			Checa("linhagem Prodigial, sem ki divino: 18x (a classe chega pelo `Perfil`)",
				  Math.Abs(v18 - 18) < 1e-6, $"ssjBuff {v18:0.####}");

			pl.Ficha.godki = new Jandirus.Core.Stats.GodKiState
			{ awakened = true, usage = true, mastery = 0 };
			double v22 = NoCorpo();
			Checa("Prodigial com o ki divino DESTRAVADO (0% de maestria): 22x",
				  Math.Abs(v22 - 22) < 1e-6, $"ssjBuff {v22:0.####}");

			// --- A SUBIDA, medida em 12 pontos ----------------------------------------------
			// COMO REPROVA SE A REGRA SUMIR: duas pontas nao distinguem rampa de escada de dois
			// degraus. O que separa as duas coisas e o PASSO SER CONSTANTE, e passo so existe com
			// pontos suficientes pra compara-los entre si.
			const int amostras = 12;
			double[] valores = new double[amostras];
			for (int k = 0; k < amostras; k++)
			{
				pl.Ficha.godki!.mastery = Catalogo.GodkiBluePct * k / (amostras - 1.0);
				valores[k] = NoCorpo();
			}
			GD.Print("  --   " + string.Join("  ", Enumerable.Range(0, amostras).Select(k =>
				$"{Catalogo.GodkiBluePct * k / (amostras - 1.0):0.#}%={valores[k]:0.##}x")));

			bool subiuSempre = true;
			double passoMin = double.MaxValue, passoMax = double.MinValue;
			for (int k = 1; k < amostras; k++)
			{
				if (valores[k] <= valores[k - 1]) subiuSempre = false;
				passoMin = Math.Min(passoMin, valores[k] - valores[k - 1]);
				passoMax = Math.Max(passoMax, valores[k] - valores[k - 1]);
			}
			Checa($"no corpo, a subida e continua: nenhum dos {amostras - 1} trechos ate 33% e plano",
				  subiuSempre, string.Join(" ", valores.Select(v => $"{v:0.###}")));
			Checa("...e o passo e CONSTANTE -- e rampa, nao escada de degraus fininhos",
				  passoMax - passoMin < 1e-9, $"menor {passoMin:0.######}, maior {passoMax:0.######}");

			// --- O TOPO E O TETO ------------------------------------------------------------
			foreach (double godki in new[] { Catalogo.GodkiBluePct, 50.0, 70.0, 100.0 })
			{
				pl.Ficha.godki!.mastery = godki;
				double v = NoCorpo();
				Checa($"a {godki:0}% de maestria divina o corpo fica em 32x"
					  + (godki > Catalogo.GodkiBluePct ? " (o teto nao e ultrapassado)" : ""),
					  Math.Abs(v - 32) < 1e-6, $"ssjBuff {v:0.####}");
			}

			// --- E O MULTIPLICADOR VIROU PODER ----------------------------------------------
			// A razao entre os dois extremos, e nao um numero absoluto: `powerlevel()` empilha
			// outros fatores (cargo, buffs, nutricao) e travar o BP num literal faria esta linha
			// reprovar no dia em que qualquer um deles mudasse, sem ter nada a ver com o Mistico.
			// O que ela prova e que a curva atravessa ate a ficha: 32/16 = 2.
			double bp32 = pl.Ficha.expressedBP;
			Checa("e o teto vale o DOBRO do 16x no BP EXPRESSO (a curva chegou no poder)",
				  Math.Abs(bp32 / bp16 - 2) < 0.02,
				  $"{bp16:N0} -> {bp32:N0} (x{bp32 / bp16:0.####})");

			// --- O QUE A MAESTRIA DA PROPRIA FORMA FAZ: NADA -------------------------------
			// Os ~20 tiques acima pagaram maestria no proprio Mistico (sustentar a forma E o treino
			// dela). Em toda outra entrada do catalogo isso mudaria o multiplicador em degraus; aqui
			// nao pode mudar, e este e o eixo que mais facilmente voltaria a mandar num refactor --
			// voltaria dando numeros crescentes, que parecem certos.
			double maestriaAgora = pl.Forma.Maestria.De(Catalogo.IdMistico);
			Checa("a maestria do proprio Mistico subiu sustentando a forma",
				  maestriaAgora > maestriaAntes, $"{maestriaAntes:0.####} -> {maestriaAgora:0.####}");
			Checa("...e ela NAO moveu a curva (o corpo continua em 32x)",
				  Math.Abs(pl.Ficha.ssjBuff - 32) < 1e-6, $"ssjBuff {pl.Ficha.ssjBuff:0.####}");
		}
		finally
		{
			// O `Atual` VOLTA POR ATRIBUICAO e nao por `Entrar`: `Entrar` marcaria a estreia da forma
			// reposta, e reposicao de bancada nao pode gastar cinematica de ninguem.
			pl.Forma.Atual = formaAntes;
			pl.Forma.Liberadas.Clear();
			foreach (int r in liberadasAntes) pl.Forma.Liberadas.Add(r);
			pl.Forma.EstreiaVista.Clear();
			foreach (int r in estreiasAntes) pl.Forma.EstreiaVista.Add(r);
			pl.Forma.Maestria.Por(Catalogo.IdMistico, maestriaAntes);
			pl.Ficha.Class = classeAntes;
			pl.Ficha.godki = godkiAntes;
			pl.Ficha.Ki = kiAntes;
			AplicarForma(pl);
		}
	}

	// =====================================================================
	// A PORTA DO BEAST NO JOGO VIVO, E A FURIA QUE NAO SE ACENDE SOZINHA
	// =====================================================================
	/// <summary>
	/// ============================ A AFIRMACAO QUE ESTE BLOCO SUSTENTA ============================
	/// *"O Beast nao sai sem que alguem morra na sua frente."* E uma frase sobre o JOGO, nao sobre o
	/// catalogo, e por isso ela nao se prova no Core: la o teste seria `Avaliar("beast", ...)` com um
	/// `Raiva: Nenhuma` escrito a mao -- provaria o gate, e o gate ninguem duvida.
	///
	/// O que se mede aqui sao as duas metades dela:
	///
	///   1. **O CAMINHO LEGITIMO NAO CHEGA**, com tudo o que se conquista pago (classe Prodigial,
	///      Mistico concedido, os 50% de ki divino que o Beast pede, BP de 1e13, Ki cheio). O funil e
	///      a TECLA C -- `Transformar` -> `Proxima` --, que e o unico jeito de um jogador escolher
	///      forma neste port. E a recusa tem que sair pela boca certa (`PorQueNao`).
	///   2. **A FURIA NAO SE ACENDE SOZINHA**: nem com os tiques passando, nem com uma morte de
	///      VERDADE que nao teve algoz (a que passa pelo funil do corpo, `Corpo.Ferir` -> `Morrer`,
	///      sem ninguem por perto). Ate a sessao do Known-People esta metade dizia "o gancho e
	///      inerte"; agora o gancho TEM dono (`GameServer.Convivio.cs`) e o que ela guarda e a
	///      primeira condicao do original -- morte por ambiente nao enfurece ninguem. Quem prova o
	///      lado positivo (ver um AMIGO morrer ACENDE) e o `OConvivioAoVivo`.
	///
	/// A metade que FALTA nao e omissao: que o admin chega la esta medido em
	/// `AsFerramentasDeAdmin`, que forca as 35 entradas do catalogo -- o Beast entre elas -- pelo
	/// canal de verbs do jogo, com permissao e registro no `admin.log`.
	/// ========================================================================================
	/// </summary>
	/// <summary>
	/// ============================ A FURIA LENDARIA, NO CORPO VIVO ============================
	/// A bancada `formas` do AssetPipeline ja prova a CURVA (`FuriaLendaria.SegundosDeControle` e
	/// `SegundosDePosse` como funcoes puras, com a simetria e as duas pontas). O que so daqui se ve e a
	/// CADEIA -- e ela e a metade que costuma faltar neste projeto: que alguem CHAMA aquela curva, que
	/// o corpo realmente troca de dono, que o bit sai no fio, e -- o mais importante -- que ele VOLTA.
	///
	/// ============================ O BURACO QUE ESTA SECAO EXISTE PRA VIGIAR ============================
	/// `DevolverAsRedeas` so tinha um chamador (`DesfazerOozaru`), e a escada nao passa por la. Um corpo
	/// possuido pela furia cuja FORMA acabasse -- por Ki zerado, por nocaute, por dominar a forma no meio
	/// da posse -- ficaria com o `Cerebro` pendurado: o jogador de volta a forma base assistindo o
	/// proprio boneco andar sozinho, sem nenhum jeito de recuperar o controle a nao ser deslogar.
	///
	/// Nao ha como esse defeito aparecer sozinho num teste de "a posse comeca". Por isso metade das
	/// linhas abaixo mede a SAIDA e nao a entrada.
	/// ==============================================================================================
	///
	/// A FORMA E FORCADA (`Forma.Entrar`) e nao conquistada de proposito: a linha Legendary so abre pra
	/// a classe Legendary, e trocar a classe do personagem aqui puxaria a raiva, o desconto de porta e a
	/// escada inteira junto. O que esta secao mede nao e QUEM chega no Legendary -- e o que acontece com
	/// quem esta nele.
	/// </summary>
	private void AFuriaLendariaNoCorpo(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		const string Id = "legendary";
		FormaDef? d = Catalogo.Def(Id);
		if (d == null) { GD.Print("  --   FURIA: pulado (o catalogo nao tem `legendary`)"); return; }

		// ---------------------------------------------------------------- o que sera reposto
		string formaAntes = pl.Forma.Atual;
		double maestriaAntes = pl.Forma.Maestria.De(Id), kiAntes = pl.Ficha.Ki;
		long furiaAntes = pl.FuriaAte;
		var liberadasAntes = new HashSet<int>(pl.Forma.Liberadas);
		var estreiasAntes = new HashSet<int>(pl.Forma.EstreiaVista);

		// PRAZO EM MILISSEGUNDOS, pra envelhecer o relogio na mao. O `FuriaAte` e relogio REAL, e uma
		// bancada sincrona nao gasta tempo real -- empurrar a data e o unico jeito de exercitar o
		// vencimento sem por tres minutos de `Thread.Sleep` no teste.
		static long Ms(double s) => (long)(s * 1000);

		try
		{
			pl.Ficha.KO = false;
			pl.Ficha.dead = false;
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			pl.CenaSegundos = 0;
			pl.FuriaAte = 0;
			pl.Cerebro = null;
			pl.Forma.Maestria.Por(Id, 0);
			pl.Forma.Entrar(Id);
			AplicarForma(pl);

			double controle = FuriaLendaria.SegundosDeControle(d, 0);
			double posse = FuriaLendaria.SegundosDePosse(d, 0);

			// --- 1. O PRIMEIRO TIQUE ARMA O RELOGIO, E DIZ O PRAZO --------------------------
			// "uma regra que so se anuncia quando ja te puniu e uma armadilha" -- a mesma frase da fera,
			// e aqui ela vale mais: a furia nao tem saida por gesto (nao da nem pra reverter a forma),
			// entao o unico jeito de o jogador entender o que vai acontecer e ouvir antes.
			EscutaDeAvisos = [];
			TickDaFuriaLendaria(pl, 0.1);
			List<string> aoEntrar = EscutaDeAvisos ?? [];
			EscutaDeAvisos = null;

			Checa("na forma lendaria crua, o relogio da furia ARMA sozinho", pl.FuriaAte != 0, "");
			Checa("...e o corpo ainda e do dono no primeiro instante",
				  pl.Cerebro == null && !SemAsRedeas(pl) && !EstadoDe(pl, NowMs()).SemRedeas, "");
			Checa($"...e o prazo dito e o da curva ({controle:0} s)",
				  aoEntrar.Exists(a => a.Contains($"{controle:0} s")), string.Join(" | ", aoEntrar));

			// --- 2. VENCIDO O PRAZO, A FURIA ASSUME -----------------------------------------
			EscutaDeAvisos = [];
			pl.FuriaAte -= Ms(controle) + 500;
			TickDaFuriaLendaria(pl, 0.1);
			List<string> aoPerder = EscutaDeAvisos ?? [];
			EscutaDeAvisos = null;

			Checa("vencido o prazo, o SERVIDOR assume o corpo", pl.Cerebro != null, "");
			Checa("...e o input do dono passa a ser recusado (o MESMO gate da fera)",
				  SemAsRedeas(pl) && ComandoDeCorpo(Protocol.C2S.Action)
				  && ComandoDeCorpo(Protocol.C2S.Transformar), "");
			Checa("...e o fio conta pra ZONA INTEIRA que aquele corpo nao tem dono",
				  EstadoDe(pl, NowMs()).SemRedeas, "");
			Checa("...e o dono e avisado do que aconteceu",
				  aoPerder.Exists(a => a.Contains("TOMA O SEU CORPO")), string.Join(" | ", aoPerder));
			Checa($"...e de quanto tempo ela fica ({posse:0} s)",
				  aoPerder.Exists(a => a.Contains($"{posse:0} s")), string.Join(" | ", aoPerder));

			// A PUPILA E DERIVADA DO MESMO BIT, e nao de um segundo canal. Esta linha e a costura entre
			// o servidor e o desenho: o `CorDoOlho` do cliente le exatamente este `SemRedeas`.
			Checa("...e a cor do olho que o Core deriva desse bit e o branco sem iris",
				  Catalogo.CorDoOlho(d, EstadoDe(pl, NowMs()).SemRedeas)
					  != Catalogo.CorDoOlho(d, semRedeas: false), "");

			// --- 3. ELA ANDA PELO MESMO LACO DO CLONE E DA FERA -----------------------------
			// Sem esta checagem, "reusei a IA do Oozaru" seria afirmacao de comentario e nao de codigo.
			Jandirus.Core.World.Vec2 antes = pl.Pos;
			bool andandoNoFio = false;
			for (int t = 0; t < 20; t++)
			{
				TickDosCorposSemDono(0.1);
				andandoNoFio |= EstadoDe(pl, NowMs()).Moving;
			}
			Checa("o corpo possuido pela furia se move sozinho",
				  (pl.Pos - antes).LengthSquared > 1, $"{antes} -> {pl.Pos}");
			Checa("...e o snapshot diz que ele esta ANDANDO (senao ele desliza na tela do dono)",
				  andandoNoFio, "");

			// --- 4. E ELA DEVOLVE O CORPO -- O RELOGIO CORRE NOS DOIS SENTIDOS --------------
			// COMO REPROVA SE A REGRA SUMIR: troque o `ArmarOControle(..., voltando: true)` do
			// `TickDaFuriaLendaria` por um `return` e a posse vira permanente -- e nada mais no jogo
			// tiraria aquele cerebro.
			EscutaDeAvisos = [];
			pl.FuriaAte -= Ms(posse) + 500;
			TickDaFuriaLendaria(pl, 0.1);
			List<string> aoVoltar = EscutaDeAvisos ?? [];
			EscutaDeAvisos = null;

			Checa("passada a posse, as redeas voltam pra mao do dono",
				  pl.Cerebro == null && !SemAsRedeas(pl), "");
			Checa("...e o fio para de dizer que o corpo esta sem redeas",
				  !EstadoDe(pl, NowMs()).SemRedeas, "");
			Checa("...e o dono e avisado de que voltou a mandar no proprio corpo",
				  aoVoltar.Exists(a => a.Contains("furia se esvai")), string.Join(" | ", aoVoltar));
			Checa("...e o relogio ja esta armado de novo (o ciclo nao para enquanto a forma dura)",
				  pl.FuriaAte != 0, "");

			// --- 5. A FORMA ACABANDO NO MEIO DA POSSE DEVOLVE O CORPO -----------------------
			// ESTE E O BURACO DO CABECALHO, e ele nao aparece em nenhuma das linhas acima: o caminho de
			// saida delas e o proprio relogio. Aqui a forma cai por FORA (Ki zerado -- o `Reverter` do
			// `TickDaForma`), que e o caminho que nao passava perto de `DevolverAsRedeas`.
			pl.FuriaAte -= Ms(FuriaLendaria.SegundosDeControle(d, 0)) + 500;
			TickDaFuriaLendaria(pl, 0.1);
			Checa("(recomeco) a furia volta a assumir o corpo", SemAsRedeas(pl), "");

			pl.Ficha.Ki = 0;
			TickDaForma(pl, 0.1);          // derruba a forma por Ki zerado, como em jogo
			TickDaFuriaLendaria(pl, 0.1);
			Checa("a forma caindo POR FORA (Ki zerado) devolve o corpo ao dono",
				  pl.Forma.NaBase && pl.Cerebro == null && !SemAsRedeas(pl),
				  $"forma {pl.Forma.Atual}, cerebro {(pl.Cerebro == null ? "nulo" : "vivo")}");
			Checa("...e o relogio se desarma junto (nada fica contando fora da forma)",
				  pl.FuriaAte == 0, $"{pl.FuriaAte}");

			// --- 6. DOMINADA, ELA NUNCA MAIS PEGA ------------------------------------------
			// O avesso de tudo acima: sem esta linha, "a furia assume" passaria tambem num sistema que
			// assume SEMPRE -- e a promessa do dono (o 100% que resolve) nao teria vigia nenhum.
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			pl.Forma.Maestria.Por(Id, 100);
			pl.Forma.Entrar(Id);
			AplicarForma(pl);
			pl.FuriaAte = 0;
			for (int t = 0; t < 10; t++) TickDaFuriaLendaria(pl, 0.1);
			Checa("a forma DOMINADA nao arma relogio nenhum",
				  pl.FuriaAte == 0 && pl.Cerebro == null, $"{pl.FuriaAte}");

			// --- 7. E A ESCADA SAIYAJIN COMUM NUNCA ENTRA NISTO ----------------------------
			// O corte e da LINHA e nao "toda forma nao dominada": um Super Saiyajin cru continua sendo
			// dono do proprio corpo, e e isso que faz a linha Legendary ser a linha da furia.
			pl.Forma.Maestria.Por(Catalogo.IdSsj1, 0);
			pl.Forma.Entrar(Catalogo.IdSsj1);
			AplicarForma(pl);
			pl.FuriaAte = 0;
			for (int t = 0; t < 10; t++) TickDaFuriaLendaria(pl, 0.1);
			Checa("o SSJ1 cru NAO arma a furia (o corte e da linha lendaria)",
				  pl.FuriaAte == 0 && pl.Cerebro == null, $"{pl.FuriaAte}");
		}
		finally
		{
			pl.Cerebro = null;
			pl.FuriaAte = furiaAntes;
			pl.Forma.Maestria.Por(Id, maestriaAntes);
			pl.Forma.Liberadas.Clear(); pl.Forma.Liberadas.UnionWith(liberadasAntes);
			pl.Forma.EstreiaVista.Clear(); pl.Forma.EstreiaVista.UnionWith(estreiasAntes);
			pl.Forma.Entrar(formaAntes);
			pl.Ficha.Ki = kiAntes;
			pl.Ficha.KO = false;
			pl.Ficha.dead = false;
			pl.CenaSegundos = 0;
			AplicarForma(pl);
			EscutaDeAvisos = null;
		}
	}

	// =====================================================================
	// O QUE PARA DE PASSAR, E O QUE CHEGA NO CLIENTE DOS OUTROS
	// =====================================================================
	/// <summary>
	/// ============================ DUAS PERGUNTAS QUE A SECAO DA FURIA NAO FAZ ============================
	/// O <see cref="AFuriaLendariaNoCorpo"/> prova que a posse ACONTECE e que ela passa. Ele confere o
	/// bloqueio com duas linhas -- `ComandoDeCorpo(Action)` e `ComandoDeCorpo(Transformar)` --, e duas
	/// linhas nao distinguem "estes comandos param" de "TUDO para". Um `ComandoDeCorpo` reescrito como
	/// `=> true` passaria nas duas e mataria o unico jeito de sair da posse: **meditar**.
	///
	/// ============================ POR QUE ISSO E O DEFEITO MAIS CARO DESTE SISTEMA ============================
	/// A furia lendaria nao tem saida por gesto -- nao da nem pra reverter a forma. Quem perde as
	/// redeas so as recupera pelo relogio... ou, no caso da fera, MEDITANDO (`Activity`). Barrar o
	/// `Activity` junto com o resto transforma a paralisia em punicao sem resposta, e o jogador nao
	/// tem como saber que o que ele esta tentando nunca ia funcionar. Falar e abrir menu caem na mesma
	/// conta por outro motivo: *"perder as redeas nao e perder a boca nem a interface"*.
	///
	/// Por isso a varredura e do ENUM INTEIRO e a lista esperada esta escrita a mao. Um comando novo
	/// no protocolo (o proximo `C2S`) cai numa das duas listas por decisao de quem o escreveu, e nao
	/// por acidente de um `Contains` largo.
	/// ==================================================================================================
	///
	/// ============================ E O CORPO REMOTO -- O BIT TEM QUE SOBREVIVER AO FIO ============================
	/// A secao da furia le `EstadoDe(pl).SemRedeas` -- o STRUCT, antes de virar bytes. Isso prova o
	/// servidor e nao a replicacao. A pupila branca de um Legendary possuido so aparece na tela de
	/// QUEM OLHA se o bit atravessar o `Write`/`Read` do <see cref="EntityState"/>, onde ele
	/// e um bit (`0x20`) espremido num byte com outros cinco. Trocar duas constantes de mascara ali
	/// nao quebra compilacao, nao quebra o servidor, e o sintoma em jogo seria "o cara ta voando" ou
	/// "o olho dele nao apaga" -- dois relatos que ninguem liga a mesma linha.
	///
	/// Por isso aqui o snapshot e SERIALIZADO e RELIDO, e a cor do olho e derivada do que VOLTOU. E o
	/// mesmo caminho que o cliente do vizinho percorre, medido no unico lugar onde os dois lados do fio
	/// existem na mesma memoria.
	/// ========================================================================================================
	/// </summary>
	private void OBloqueioEOFioDaPosse(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		// ---------------------------------------------------------------- o que sera reposto
		string formaAntes = pl.Forma.Atual;
		double maestriaAntes = pl.Forma.Maestria.De("legendary");
		long furiaAntes = pl.FuriaAte;
		Jandirus.Core.Ai.Cerebro? cerebroAntes = pl.Cerebro;

		try
		{
			// ============================ 1. O BLOQUEIO E UMA LISTA, E ELA E ESTA ============================
			// Os SEIS comandos que mexem no corpo. Escritos a mao e nao lidos do `ComandoDeCorpo`: uma
			// lista que se conferisse com ela mesma passaria em qualquer valor.
			Protocol.C2S[] devemParar =
			[
				Protocol.C2S.Action, Protocol.C2S.Guard, Protocol.C2S.Carregar,
				Protocol.C2S.Habilidade, Protocol.C2S.Transformar, Protocol.C2S.Zanzoken,
			];

			// E O QUE TEM QUE CONTINUAR PASSANDO -- cada um com o motivo dele:
			//   * `Activity` e a SAIDA (meditar). E o unico que, barrado, tranca o jogador pra sempre;
			//   * `Chat` e a boca; `Verbo`, `Cargo`, `Aprender`, `Estilo` e `Tech` sao a interface;
			//   * `InputState` **passa de proposito** e nao e esquecimento: quem recusa movimento e o
			//     portao la embaixo do `Input`, que alem de recusar PRESERVA o `Moving` que a IA
			//     escreveu e responde a posicao (`GameServer.cs`). Barra-lo aqui deixaria o corpo
			//     possuido sem correcao de posicao na tela do dono.
			Protocol.C2S[] devemPassar =
			[
				Protocol.C2S.Activity, Protocol.C2S.Chat, Protocol.C2S.Verbo, Protocol.C2S.Cargo,
				Protocol.C2S.Aprender, Protocol.C2S.Estilo, Protocol.C2S.Tech, Protocol.C2S.Alvo,
				Protocol.C2S.Aim, Protocol.C2S.Lethal, Protocol.C2S.InputState, Protocol.C2S.Ping,
				Protocol.C2S.Login, Protocol.C2S.PickSlot, Protocol.C2S.CreateChar,
				Protocol.C2S.DeleteChar, Protocol.C2S.ClashTecla,
				// A VOZ passa pelo mesmo motivo do `Chat`: e a boca, e boca possuida continua falando. O
				// `C2S.Voz` entrou no "Grande Update parte 3" sem ninguem decidir o lado, e esta linha ficou
				// vermelha ate 2026-09-02 -- exatamente o que ela existe pra fazer.
				Protocol.C2S.Voz,
			];

			// A VARREDURA E DO ENUM INTEIRO, e as duas listas tem que cobri-lo sem sobra nem falta. Sem
			// esta linha um `C2S` novo entraria em jogo sem ninguem decidir de que lado ele esta -- e o
			// lado errado ou tranca o jogador ou deixa um corpo possuido socando.
			Protocol.C2S[] todos = Enum.GetValues<Protocol.C2S>();
			var semLista = todos.Where(c => !devemParar.Contains(c) && !devemPassar.Contains(c)).ToArray();
			Checa($"as duas listas cobrem o protocolo inteiro ({todos.Length} comandos)",
				  semLista.Length == 0, string.Join(", ", semLista));

			var passouQuemDevia = todos.Where(c => devemParar.Contains(c) && !ComandoDeCorpo(c)).ToArray();
			var parouQuemNaoDevia = todos.Where(c => devemPassar.Contains(c) && ComandoDeCorpo(c)).ToArray();
			Checa("os SEIS comandos de corpo sao recusados durante a posse",
				  passouQuemDevia.Length == 0, string.Join(", ", passouQuemDevia));
			Checa("...e NENHUM outro e -- o bloqueio e uma lista, nao um portao geral",
				  parouQuemNaoDevia.Length == 0, string.Join(", ", parouQuemNaoDevia));

			// A LINHA QUE O DONO LERIA. Ela ja esta coberta pela varredura acima, e existe separada
			// porque e a que conta a regra: meditar e a saida, e uma bancada que so diga "17 comandos
			// passam" nao deixa ninguem saber qual deles nao pode faltar.
			Checa("-- e MEDITAR (`Activity`) continua passando: e a unica saida da posse",
				  !ComandoDeCorpo(Protocol.C2S.Activity), "");
			Checa("-- e FALAR tambem: perder as redeas nao e perder a boca",
				  !ComandoDeCorpo(Protocol.C2S.Chat) && !ComandoDeCorpo(Protocol.C2S.Verbo), "");

			// CONTROLE NEGATIVO DAS DUAS VARREDURAS. As duas contam ZERO, e um `ComandoDeCorpo` que
			// respondesse sempre a mesma coisa faria UMA delas passar (a outra cairia) -- mas as duas
			// medem listas escritas por mim, e um erro de copia nas listas (uma vazia) faria as duas
			// passarem juntas. Estas linhas medem a PARTICAO, que e o que nao pode degenerar.
			Checa($"CONTROLE NEGATIVO: o gate separa o protocolo em DOIS lados nao vazios "
				+ $"({devemParar.Length} param, {devemPassar.Length} passam)",
				  devemParar.Length > 0 && devemPassar.Length > devemParar.Length
				  && todos.Count(ComandoDeCorpo) == devemParar.Length,
				  $"{todos.Count(ComandoDeCorpo)} recusados de {todos.Length}");

			// ============================ 2. O GATE E `ComandoDeCorpo` **E** `SemAsRedeas` ============================
			// O despacho pergunta as duas coisas (`GameServer.cs`). Se alguem tirar o `SemAsRedeas` de
			// la, o jogo inteiro perde soco, guarda e transformacao pra todo mundo, o tempo todo -- e
			// nenhuma linha acima cai, porque todas medem so a metade da lista.
			// ======================================================================================================
			pl.Cerebro = null;
			Checa("com as redeas na mao, o corpo NAO esta possuido (o gate tem duas metades)",
				  !SemAsRedeas(pl), "");
			pl.Cerebro = new Jandirus.Core.Ai.Cerebro();
			Checa("...e com cerebro pendurado, esta", SemAsRedeas(pl), "");

			// ============================ 3. O BIT ATRAVESSA O FIO, E A PUPILA SAI DO OUTRO LADO ============================
			FormaDef? lend = Catalogo.Def("legendary");
			if (lend == null) { Checa("o catalogo tem `legendary`", false, ""); return; }

			pl.Forma.Maestria.Por("legendary", 0);
			pl.Forma.Entrar("legendary");
			AplicarForma(pl);

			// O SNAPSHOT VIRA BYTES E VOLTA -- o mesmo `Write`/`Read` do `EntityState`, que e por onde
			// o corpo de qualquer jogador chega na tela de qualquer outro.
			static EntityState PeloFio(EntityState e)
			{
				var w = new NetDataWriter();
				e.Write(w);
				return EntityState.Read(new NetDataReader(w.CopyData()));
			}

			EntityState possuido = PeloFio(EstadoDe(pl, NowMs()));
			Checa("o bit da posse ATRAVESSA o fio (o corpo remoto sabe que ele nao tem dono)",
				  possuido.SemRedeas, "");
			// E A COR SAI DO QUE VOLTOU, e nao do que o servidor sabia: e a conta que o cliente do
			// vizinho faz, com o unico dado que ele tem.
			Checa("...e a pupila que o vizinho desenha e a BRANCA sem iris",
				  Catalogo.CorDoOlho(lend, possuido.SemRedeas) == Catalogo.CorDoOlho(lend, semRedeas: true)
				  && Catalogo.CorDoOlho(lend, possuido.SemRedeas) != Catalogo.CorDoOlho(lend, semRedeas: false),
				  Catalogo.CorDoOlho(lend, possuido.SemRedeas) ?? "nada");

			// O AVESSO, PELO MESMO CAMINHO. Sem ele, um `SemRedeas = true` cravado (ou uma mascara que
			// acende sempre) passaria nas duas linhas de cima -- e a pupila ficaria branca pra sempre,
			// que e metade do defeito que o dono descreveu ("quando o jogador tem o controle a pupila
			// verde volta").
			pl.Cerebro = null;
			EntityState livre = PeloFio(EstadoDe(pl, NowMs()));
			Checa("com as redeas de volta, o fio para de dizer que o corpo e de ninguem",
				  !livre.SemRedeas, "");
			Checa("...e a pupila que o vizinho desenha VOLTA a ser verde",
				  Catalogo.CorDoOlho(lend, livre.SemRedeas) == Catalogo.CorDoOlho(lend, semRedeas: false),
				  Catalogo.CorDoOlho(lend, livre.SemRedeas) ?? "nada");

			// E O BIT NAO ATROPELA OS VIZINHOS DELE NO MESMO BYTE. `SemRedeas` e `0x20` de `flags2`, e
			// `Voando`, `Correndo`, `Deitado`, `Carregando` e `Sobrecarregado` moram nos outros cinco.
			// Uma mascara trocada la nao quebra nada que compile: o sintoma seria um Legendary possuido
			// que "esta voando" pra zona inteira. Aqui os dois estados do bit sao lidos com o RESTO do
			// byte conferido contra o que o servidor pos.
			EntityState fonte = EstadoDe(pl, NowMs());
			Checa("...e o bit nao contamina os outros cinco do mesmo byte",
				  livre.Voando == fonte.Voando && livre.Correndo == fonte.Correndo
				  && livre.Deitado == fonte.Deitado && livre.Carregando == fonte.Carregando
				  && livre.Sobrecarregado == fonte.Sobrecarregado,
				  $"voando {livre.Voando}/{fonte.Voando} correndo {livre.Correndo}/{fonte.Correndo}");
		}
		finally
		{
			pl.Cerebro = cerebroAntes;
			pl.FuriaAte = furiaAntes;
			pl.Forma.Maestria.Por("legendary", maestriaAntes);
			pl.Forma.Entrar(formaAntes);
			AplicarForma(pl);
		}
	}

	private void OBeastEAFuriaInerte(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		// ---------------------------------------------------------------- o que sera reposto
		string classeAntes = pl.Ficha.Class, formaAntes = pl.Forma.Atual;
		Jandirus.Core.Stats.GodKiState? godkiAntes = pl.Ficha.godki;
		var liberadasAntes = new HashSet<int>(pl.Forma.Liberadas);
		var estreiasAntes = new HashSet<int>(pl.Forma.EstreiaVista);
		double maestriaAntes = pl.Forma.Maestria.De(Catalogo.IdMistico), kiAntes = pl.Ficha.Ki;
		long furiaAntes = pl.FuriaExtremaAte;

		try
		{
			pl.Ficha.KO = false;
			pl.Ficha.dead = false;
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			pl.FuriaExtremaAte = 0;
			pl.Forma.Entrar(Catalogo.IdBase);

			// TUDO O QUE SE CONQUISTA, PAGO. Os 50% sao o numero da entrada e nao 100: acima de 70% a
			// linha de Ultra Instinto/Ultra Ego abriria junto, e a recusa que sai pela boca do jogo
			// passaria a ser a DELAS -- o teste mediria a mensagem errada e ninguem veria.
			pl.Ficha.Class = Catalogo.ClasseProdigial;
			pl.Ficha.godki = new Jandirus.Core.Stats.GodKiState
			{ awakened = true, usage = true, mastery = Catalogo.GodkiRoyalePct };
			AplicarForma(pl);
			ConcederMistico(pl, "bancada da porta");

			// --- 1. O C SOBE ATE ONDE DA, E PARA ANTES DA FERA -------------------------------
			EscutaDeAvisos = [];
			var visitadas = new List<string>();
			for (int c = 0; c < 12; c++)
			{
				string antes = pl.Forma.Atual;
				pl.Ficha.Ki = pl.Ficha.MaxKi;
				Transformar(pl, subir: true);
				PassarACena(pl);
				if (pl.Forma.Atual == antes) break;
				visitadas.Add(pl.Forma.Atual);
			}
			List<string> ditos = EscutaDeAvisos ?? [];
			EscutaDeAvisos = null;

			Checa("com TUDO o que se conquista pago, a tecla C NAO alcanca o Beast",
				  pl.Forma.Atual != "beast",
				  "chegou em " + (visitadas.Count > 0 ? string.Join(" -> ", visitadas) : "lugar nenhum"));
			Checa("...e ela chega no Mistico, que e o degrau logo abaixo (o C nao parou antes)",
				  pl.Forma.Atual == Catalogo.IdMistico, pl.Forma.Atual);

			// A RECUSA TEM QUE DESENSINAR. Ver `PorQueNao`: quem ouve isto ja tem tudo o que se
			// treina, e "falta furia" mandaria a pessoa procurar briga. A checagem e pela PALAVRA que
			// a frase carrega, e nao pela frase inteira: reescreve-la e permitido, trocar o sentido
			// dela (voltar a dizer "falta X%") nao e.
			Checa("...e a recusa fala de DOR, e nao de treino nem de poder",
				  ditos.Any(a => a.Contains("dor", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", ditos));
			Checa("...e ela nao entrega um numero pra o jogador perseguir",
				  !ditos.Any(a => a.Contains("furia", StringComparison.OrdinalIgnoreCase)
							   || a.Contains("50%", StringComparison.Ordinal)),
				  string.Join(" | ", ditos));

			// --- 2. E O GANCHO ACESO ABRE, no mesmo corpo e no mesmo instante ----------------
			// O par da linha de cima: sem ele "o C nao alcanca" passaria tambem num mundo em que o
			// Beast e inalcancavel PARA SEMPRE -- e ai o gancho da amizade seria decoracao.
			AmigoAbatido(pl, "Krillin", NivelDeRaiva.Extrema);
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			Transformar(pl, subir: true);
			PassarACena(pl);
			Medir(pl);
			Checa("acesa a furia pelo gancho, o MESMO C chega no Beast",
				  pl.Forma.Atual == "beast", pl.Forma.Atual);
			Checa("...e o corpo recebe os 56x da fera",
				  Math.Abs(pl.Ficha.ssjBuff - 56) < 1e-6, $"ssjBuff {pl.Ficha.ssjBuff:0.####}");

			// --- 3. NADA NO JOGO ACENDE ESSA FURIA ------------------------------------------
			// O corpo volta pra base e o prazo e apagado a mao; dali em diante so o JOGO roda.
			Transformar(pl, subir: false);
			pl.Forma.Liberadas.Remove(Catalogo.Rede("beast"));
			pl.FuriaExtremaAte = 0;
			pl.Ficha.KO = false;
			pl.Ficha.dead = false;

			EscutaDeAvisos = [];
			for (int t = 0; t < 300; t++)
			{
				pl.Ficha.Ki = pl.Ficha.MaxKi;
				TickDaForma(pl, 0.1);
			}
			Checa("30 segundos de tique nao acendem raiva em ninguem",
				  Perfil(pl).Raiva == NivelDeRaiva.Nenhuma, Perfil(pl).Raiva.ToString());

			// ============================ E NEM UMA MORTE **SEM ALGOZ** ============================
			// ESTA CHECAGEM MUDOU DE SIGNIFICADO NA SESSAO DO KNOWN-PEOPLE, e virou mais util.
			//
			// Antes ela dizia "o gancho da amizade e inerte" -- o sistema social nao existia e nada
			// no jogo acendia raiva. Agora existe (`GameServer.Convivio.cs`), e o que ela mede e a
			// PRIMEIRA CONDICAO do original: `Death.dm:75` so enfurece quem assiste quando ha um
			// `deathKiller` de combate, e o comentario de la e explicito -- *"no rage for friendly
			// duels or environmental death"*.
			//
			// O `MatarDeVerdade` mata pelo funil do CORPO (`Corpo.Ferir` -> `Morrer`), sem passar
			// pelo `AoPerderALuta`: e a morte por ambiente, por fome, pela propria explosao. Ela nao
			// pode enfurecer ninguem -- senao bastaria pular de um penhasco perto de um amigo pra
			// fabricar um Super Saiyajin.
			// =====================================================================================
			MatarDeVerdade(pl);
			for (int t = 0; t < 30; t++) TickDaForma(pl, 0.1);
			List<string> noLuto = EscutaDeAvisos ?? [];
			EscutaDeAvisos = null;

			// E AS DUAS JANELAS SAO CONFERIDAS, nao so a do luto: a morte passa pelo `Nocauteou`
			// antes do `Morreu` no funil do combate, entao um gancho de nocaute ligado por engano
			// acenderia a LENDARIA e o SSJ1 de qualquer Legendary abriria calado.
			Checa("uma morte SEM ALGOZ nao acende raiva em ninguem (nao ha luto por acidente)",
				  Perfil(pl).Raiva == NivelDeRaiva.Nenhuma
				  && pl.FuriaExtremaAte == 0 && pl.RaivaLendariaAte == 0,
				  $"extrema={pl.FuriaExtremaAte} lendaria={pl.RaivaLendariaAte}");
			Checa("...e ninguem ouviu a frase do luto",
				  !noLuto.Any(a => a.Contains("PARTE", StringComparison.Ordinal)),
				  string.Join(" | ", noLuto));

			// E COM A FURIA APAGADA O BEAST FECHA DE NOVO, no corpo vivo: a porta nao ficou
			// escancarada pelo teste acima.
			//
			// ============================ O CORPO PRECISA VOLTAR PRO MISTICO ANTES DE PERGUNTAR ============================
			// Esta linha nasceu ERRADA e a bancada a pegou na primeira rodada: perguntada com o corpo
			// na BASE, a recusa que sai e `ForaDeOrdem` (passo 5, o degrau anterior) e nao `SemFuria`
			// (passo 7b) -- o `Avaliar` para no primeiro gate que falha, entao um teste montado no
			// estado errado prova o gate errado e diz que provou o certo.
			//
			// E a volta e pela TECLA C e nao por um `Entrar` na mao: assim ela tambem re-prova, depois
			// da morte e da ressurreicao, que o caminho legitimo continua chegando exatamente ate o
			// Mistico -- que e a metade da afirmacao deste bloco inteiro.
			// ============================================================================================================
			pl.Ficha.dead = false;
			pl.Ficha.KO = false;
			pl.Combate?.Reviver();
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			Transformar(pl, subir: true);
			PassarACena(pl);
			Checa("depois da morte, o C ainda leva ao Mistico e para nele",
				  pl.Forma.Atual == Catalogo.IdMistico, pl.Forma.Atual);
			Checa("...e la, com a furia apagada, o Beast volta a ser recusado por SEM FURIA",
				  pl.Forma.Avaliar("beast", pl.Ficha.BP, 1, false, Perfil(pl)) == RecusaForma.SemFuria,
				  pl.Forma.Avaliar("beast", pl.Ficha.BP, 1, false, Perfil(pl)).ToString());
		}
		finally
		{
			pl.Ficha.dead = false;
			pl.Ficha.KO = false;
			pl.Combate?.Reviver();
			pl.Forma.Atual = formaAntes;
			pl.Forma.Liberadas.Clear();
			foreach (int r in liberadasAntes) pl.Forma.Liberadas.Add(r);
			pl.Forma.EstreiaVista.Clear();
			foreach (int r in estreiasAntes) pl.Forma.EstreiaVista.Add(r);
			pl.Forma.Maestria.Por(Catalogo.IdMistico, maestriaAntes);
			pl.Ficha.Class = classeAntes;
			pl.Ficha.godki = godkiAntes;
			pl.Ficha.Ki = kiAntes;
			pl.FuriaExtremaAte = furiaAntes;
			EscutaDeAvisos = null;
			AplicarForma(pl);
		}
	}

	// =====================================================================
	// QUEM CHEGA NA ZONA VE QUEM JA ESTA TRANSFORMADO
	// =====================================================================
	/// <summary>
	/// A SINCRONIA DE ENTRADA DE ZONA, LIDA NOS BYTES QUE SAIRIAM NO FIO.
	///
	/// ============================ POR QUE ISTO NAO SE PROVA DE OUTRO JEITO ============================
	/// `S2C.Forma` e `S2C.Oozaru` sao pacotes de ACONTECIMENTO. A sincronia os reusa como ESTADO, e a
	/// diferenca entre as duas coisas mora inteira em dois campos que nao aparecem em lugar nenhum do
	/// servidor depois de escritos: o `de` (que tem que ser IGUAL ao `para`, senao o recem-chegado ouve
	/// o estalo de transformacoes que aconteceram antes de ele existir ali) e o degrau
	/// (<see cref="DegrauDeCena.Nenhuma"/>, senao ele fica PRESO assistindo a estreia de um
	/// desconhecido -- o remedio virando doenca).
	///
	/// Nenhum dos dois quebra nada quando esta errado: o jogo continua rodando, so mente. Por isso a
	/// bancada desmonta o pacote (<see cref="LerPacote"/>) em vez de conferir argumentos de chamada.
	/// ============================================================================================
	///
	/// O JOGADOR E POSTO NA ZONA A MAO. Esta bancada roda no `Login`, ANTES do `ZoneList(...).Add(pl)`
	/// (que so acontece umas dez linhas depois) -- e sem ele na lista o `SincronizarFormas` varreria uma
	/// zona vazia e daria verde sem mandar um pacote sequer. Entra e SAI: deixa-lo la o duplicaria
	/// quando o `Login` o inscrever de verdade, e um jogador duas vezes na mesma zona recebe tudo em
	/// dobro pra sempre.
	/// </summary>
	private void ASincroniaDaZona(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		List<ServerPlayer> zona = ZoneList(pl.Zone.Hash);
		bool jaEstava = zona.Contains(pl);
		if (!jaEstava) zona.Add(pl);

		// o estado de forma e restaurado no fim: este bloco mexe nos dois eixos de proposito
		string formaAntes = pl.Forma.Atual;
		FormaOozaru feraAntes = pl.Oozaru;

		// ============================ DECLARADO AQUI FORA PRA PODER SER TIRADO LA EMBAIXO ============================
		// O corpo forjado do passo 5 e criado dentro do `try`, mas quem o tira da zona TEM que ser o
		// `finally`. Ele ja morou dentro do `try` e foi assim que o aborto virou desastre: um estouro
		// no meio saltava a linha que o removia, o forjado ficava na `ZoneList` PRA SEMPRE e o laco de
		// snapshot do tique passava a estourar em todo quadro, em TODAS as zonas.
		//
		// Deixar de fora tambem seria errado: um jogador duas vezes na mesma zona recebe tudo em dobro
		// pra sempre -- e a mesma razao pela qual o `pl` entra e sai (ver o cabecalho deste metodo).
		// ======================================================================================================
		ServerPlayer? jaEstavaLa = null;

		try
		{
			// ============================ A PORTA E O `TrocarAparencias`, E NAO O `SincronizarFormas` ============================
			// Chamar a funcao interna provaria que ELA funciona sem provar que alguem a chama -- e "ligar a
			// regra num chamador e esquecer do outro" e, escrito por todo este port, o erro que mais se
			// repetiu aqui. O `SincronizarFormas` esta pendurado DENTRO do `TrocarAparencias` justamente pra
			// que os dois caminhos de entrada (login e troca de planeta) o herdem, e e por essa porta que a
			// bancada entra.
			//
			// COMO REPROVA SE A REGRA SUMIR: apague a linha `SincronizarFormas(novo)` do `TrocarAparencias`
			// e TODAS as checagens deste bloco caem de uma vez -- que e exatamente o estado anterior a esta
			// fase (quem chegava numa zona via um Super Saiyajin 3 desenhado como lutador comum).
			// ==============================================================================================================
			List<PacoteLido> Sincronizar()
			{
				EscutaDeSincronia = [];
				EscutaDeAnuncios = [];
				EscutaDeFeras = [];
				TrocarAparencias(pl);

				var lidos = (EscutaDeSincronia ?? []).Select(e => LerPacote(e.Pacote)).ToList();
				EscutaDeSincronia = null;
				return lidos;
			}

			// --- 1. CORPO COMUM: A SINCRONIA CALA ----------------------------------
			// "So sai pacote de quem tem o que dizer". Sem esta linha, "o transformado gera pacote" nao
			// distinguiria a regra de um sistema que manda o estado de todo mundo o tempo todo -- e ai o
			// pacote de forma base viraria banda paga por quadro identico, uma vez por pessoa por entrada.
			//
			// COMO REPROVA SE A REGRA SUMIR: tire o `if (!quem.Forma.NaBase)` do `PacotesDeEstado`.
			pl.Forma.Atual = Catalogo.IdBase;
			pl.Oozaru = FormaOozaru.Nao;
			List<PacoteLido> nada = Sincronizar();
			Checa("quem esta na forma base nao gera pacote de sincronia nenhum", nada.Count == 0,
				  string.Join(" | ", nada.Select(p => p.Tipo.ToString())));

			// --- 2. TRANSFORMADO: UM PACOTE, E ELE DIZ "ESTA EM" -------------------
			pl.Forma.Atual = "ssj3";
			List<PacoteLido> transformado = Sincronizar();

			Checa("quem esta em SSJ3 gera exatamente um pacote de estado",
				  transformado.Count == 1, string.Join(" | ", transformado.Select(p => p.Tipo.ToString())));

			PacoteLido f = transformado.FirstOrDefault(p => p.Tipo == Protocol.S2C.Forma);
			Checa("...e ele e um `S2C.Forma` com a forma certa",
				  f.Tipo == Protocol.S2C.Forma && f.Para == "ssj3", $"{f.Tipo} {f.De}->{f.Para}");

			// ============================ O `de` == `para` E A REGRA DO SOM ============================
			// COMO REPROVA SE A REGRA SUMIR: escreva `Catalogo.IdBase` no `de` do `PacotesDeEstado` (a
			// escolha "obvia" pra quem le o codigo sem contexto) e esta linha cai -- e em jogo, chegar num
			// planeta com tres transformados dispararia tres estalos de coisas que ja tinham acontecido.
			// =====================================================================================
			Checa("...e o `de` e IGUAL ao `para` -- 'ele ESTA em', nao 'ele MUDOU pra'",
				  f.De == f.Para, $"{f.De} -> {f.Para}");

			// ============================ O DEGRAU E O QUE PRENDE O CORPO ============================
			// COMO REPROVA SE A REGRA SUMIR: troque `DegrauDeCena.Nenhuma` por qualquer outro degrau no
			// `PacotesDeEstado` e o recem-chegado passa a assistir, preso, a transformacao de alguem que
			// virou Super Saiyajin antes de ele pisar no planeta. Ver `World.AoMudarForma`.
			// ====================================================================================
			Checa("...e vem SEM CENA (`Nenhuma`), senao quem chega fica preso na estreia alheia",
				  f.Degrau == DegrauDeCena.Nenhuma, f.Degrau.ToString());

			// ============================ O PAR (NOVO, NOVO) ============================
			// Relogar ou trocar de planeta transformado DESTROI E RECRIA o boneco local tambem -- e o
			// dono do SSJ4 veria todo mundo certo e a si mesmo careca e sem pelagem. Foi metade do relato.
			//
			// COMO REPROVA SE A REGRA SUMIR: ponha o `MandarEstadoDeForma(novo, outro)` dentro do
			// `if (outro != novo)` do `SincronizarFormas` e esta linha cai sozinha.
			// ==========================================================================
			Checa("...e ela chega ao PROPRIO dono (relogar recria o boneco local tambem)",
				  f.Quem == pl.Id, $"pacote de {f.Quem} pra o jogador {pl.Id}");

			// ============================ A SINCRONIA NAO E UM ANUNCIO ============================
			// Se alguem "simplificar" o `SincronizarFormas` chamando `AnunciarForma`, a zona INTEIRA
			// receberia o pacote (e nao so quem chegou), com o degrau derivado da maestria -- ou seja
			// todos os presentes reveriam a transformacao de todo mundo a cada chegada de alguem.
			// ================================================================================
			Checa("...e a sincronia NAO passa pelo funil do anuncio (senao a zona inteira reveria a cena)",
				  (EscutaDeAnuncios ?? []).Count == 0 && (EscutaDeFeras ?? []).Count == 0,
				  $"{(EscutaDeAnuncios ?? []).Count} anuncios, {(EscutaDeFeras ?? []).Count} feras");

			// --- 3. A FERA ---------------------------------------------------------
			pl.Forma.Atual = Catalogo.IdBase;
			pl.Oozaru = FormaOozaru.Regular;
			List<PacoteLido> macaco = Sincronizar();
			PacoteLido o = macaco.FirstOrDefault(p => p.Tipo == Protocol.S2C.Oozaru);
			Checa("quem ja e macaco gera o pacote da fera",
				  macaco.Count == 1 && o.Fera == FormaOozaru.Regular,
				  string.Join(" | ", macaco.Select(p => $"{p.Tipo}/{p.Fera}")));
			// A CENA DO MACACO TOCA TODA VEZ QUE A FERA NASCE -- menos aqui, e este e o unico lugar do
			// jogo onde ela nao toca. Sem o degrau no pacote, o recem-chegado assistiria um macaco de dez
			// metros se transformando, preso, sem nada explicando por que.
			// COMO REPROVA SE A REGRA SUMIR: tire o byte do `PacoteDeOozaru`, ou mande `Estreia` aqui.
			Checa("...SEM a cena do macaco (que em qualquer outro caminho toca sempre)",
				  o.Degrau == DegrauDeCena.Nenhuma, o.Degrau.ToString());
			// O `primeira` E OUTRA COISA: ele carimba o `** Oozaru **` no chat do dono. Mandar `true` aqui
			// escreveria no chat de quem chegou que ELE virou macaco.
			Checa("...e sem o carimbo de estreia no chat (`primeira` = false)", !o.Primeira, "");

			// --- 4. OS DOIS JUNTOS: A ORDEM ---------------------------------------
			// ESTADO FORCADO A MAO, e dito: em jogo o `Apeshit` derruba o SSJ antes de virar macaco, entao
			// forma da escada e fera nao convivem hoje. A ORDEM continua sendo uma regra viva porque o
			// `PacotesDeEstado` a escreve, e porque o par existe no meio-segundo entre os dois pacotes
			// quando o Dourado cai no SSJ4. Invertida, o `S2C.Forma` chegaria por cima e devolveria o
			// lutador de 32 px por baixo de um corpo que devia ter 96.
			pl.Forma.Atual = "ssj4";
			pl.Oozaru = FormaOozaru.Dourado;
			List<PacoteLido> dois = Sincronizar();
			Checa("com forma E fera, saem os dois pacotes",
				  dois.Count == 2, string.Join(" | ", dois.Select(p => p.Tipo.ToString())));
			Checa("...e a FERA vem depois da escada (o macaco cobre o que a forma desenhou)",
				  dois.Count == 2 && dois[0].Tipo == Protocol.S2C.Forma && dois[1].Tipo == Protocol.S2C.Oozaru,
				  string.Join(" -> ", dois.Select(p => p.Tipo.ToString())));

			// --- 5. AS DUAS DIRECOES ----------------------------------------------
			// ============================ QUEM CHEGA VE, E E VISTO ============================
			// Sao duas regras e nao uma, e o `SincronizarFormas` tem uma linha pra cada. Esquecer a
			// segunda daria o defeito pela metade -- o recem-chegado veria os transformados e apareceria
			// careca pra eles --, que e pior de diagnosticar do que o defeito inteiro.
			//
			// O SEGUNDO CORPO EMPRESTA O MESMO `Peer`: a captura mora no `MandarEstadoDeForma`, que
			// desiste em `Peer == null` -- um clone (que e o NPC que este servidor sabe fabricar) nunca
			// chegaria a linha que a bancada le. O id e proprio, entao os dois lados sao distinguiveis.
			//
			// COMO REPROVA SE A REGRA SUMIR: apague qualquer uma das duas linhas do laco de
			// `SincronizarFormas` e uma das duas checagens abaixo cai.
			// ============================================================================
			// ============================ E ELE NASCE COM FICHA ============================
			// **UM CORPO SEM `Ficha` NA `ZoneList` NAO E UM CORPO INCOMPLETO: E UMA MINA.**
			//
			// `ServerPlayer.Ficha` e `public Fighter Ficha = null!;` (GameServer.cs:38) -- nulo de
			// verdade, com o compilador calado. Sem esta linha o `TrocarAparencias(pl)` logo abaixo
			// estourava NRE ainda DENTRO da bancada: ele termina em `TrocarAureolas`
			// (GameServer.cs:4031), que pra cada OUTRO da zona monta `PacoteDeAureola(outro)` e le
			// `de.Ficha.dead` (GameServer.Alem.cs:410).
			//
			// E o estrago nao parava na prova perdida. O estouro subia ate o tratador de pacote do
			// login, matava o resto da bancada (as 318 provas seguintes nunca rodavam) e deixava o
			// forjado NA ZONA -- porque a linha que o tirava vinha DEPOIS do ponto de estouro. Dali em
			// diante o tique do servidor lia `ServerPlayer.Deitado` (`Ficha.KO`, GameServer.cs:651)
			// nesse orfao a cada quadro, DENTRO do laco que escreve os snapshots
			// (GameServer.cs:4664-4685): nenhuma zona recebia snapshot nem projetil de novo. O
			// servidor ficava de pe, com a porta aberta, e MUDO -- morto parecendo vivo.
			//
			// O `Fighter` e o minimo que o caminho lido pede (o `dead` da aureola); o resto do corpo
			// forjado (`Combate`, `Livro`) continua fora porque nada neste bloco encosta nele -- e
			// forjar o que ninguem le e o jeito de a bancada parar de descrever o produto.
			// ==========================================================================
			jaEstavaLa = new ServerPlayer
			{
				Id = pl.Id + 90_000,
				Peer = pl.Peer,
				Name = "bancada: quem ja estava aqui",
				Race = pl.Race,
				Zone = pl.Zone,
				Pos = pl.Pos,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = pl.Race, BP = 1000 },
			};
			jaEstavaLa.Forma.Atual = "ssj2";
			zona.Add(jaEstavaLa);

			EscutaDeSincronia = [];
			TrocarAparencias(pl);
			var trocados = EscutaDeSincronia ?? [];
			EscutaDeSincronia = null;

			Checa("quem chega recebe a forma de quem JA ESTAVA na zona",
				  trocados.Any(t => t.Quem == jaEstavaLa.Id && t.Para == pl.Id
								 && LerPacote(t.Pacote).Para == "ssj2"),
				  string.Join(" | ", trocados.Select(t => $"{t.Quem}->{t.Para}")));
			Checa("...e quem ja estava recebe a forma de quem CHEGOU",
				  trocados.Any(t => t.Quem == pl.Id && t.Para == jaEstavaLa.Id),
				  string.Join(" | ", trocados.Select(t => $"{t.Quem}->{t.Para}")));
		}
		finally
		{
			pl.Forma.Atual = formaAntes;
			pl.Oozaru = feraAntes;
			EscutaDeSincronia = null;
			EscutaDeAnuncios = null;
			EscutaDeFeras = null;
			if (jaEstavaLa != null) zona.Remove(jaEstavaLa);
			if (!jaEstava) zona.Remove(pl);
		}
	}

	/// <summary>
	/// RECALCULA O `expressedBP`. A primeira versao desta bancada nao fazia isto, e as tres
	/// checagens de BP liam um valor congelado do nascimento -- diziam "o BP nao subiu" com o
	/// `ssjBuff` correto do lado, que e o retrato de um teste medindo a coisa errada.
	///
	/// O `Statify()` cuida dos ATRIBUTOS; quem transforma `ssjBuff` em poder e o `PowerLevel()`, e
	/// no jogo quem o chama e o tique do servidor -- que nao roda no meio de uma funcao sincrona.
	/// </summary>
	private static void Medir(ServerPlayer pl) => pl.Ficha.PowerLevel();

	// =====================================================================
	// ATE ONDE UM ANUNCIO DE FORMA CHEGA -- o alcance da cena, medido no servidor
	// =====================================================================
	/// <summary>
	/// FAZ UM ANUNCIO DE VERDADE E DEVOLVE PRA QUEM ELE FOI. Chamado pela bancada do CLIENTE
	/// (`--diagforma`), que e onde o resto do alcance (musica e tremor) e medido -- as duas metades da
	/// mesma regra tem que aparecer no mesmo placar, senao uma delas envelhece sozinha.
	///
	/// ============================ POR QUE O SERVIDOR PRECISA ENTRAR NESTA CONTA ============================
	/// "A musica alcanca o planeta inteiro e nao alcanca outro planeta" parece regra de cliente e nao e.
	/// O cliente nao filtra nada: o `Transformacao._Ready` poe a faixa no ar em TODA tela que recebeu a
	/// cena, sem uma condicao sequer. Quem recorta o "todas" e o `foreach` do <see cref="AnunciarForma"/>.
	/// Entao medir isso so no cliente e medir a metade que nao decide.
	///
	/// ============================ OS TRES CORPOS FORJADOS, E POR QUE TRES ============================
	///   * <b>quem vira</b> -- e ELE, e nao o jogador da bancada, pra o anuncio nao disparar a cena de
	///     verdade na tela que esta rodando o teste. `Peer` nulo (e um corpo sem dono, que este servidor
	///     ja sabe fabricar pro clone da meditacao), e o id e proprio, entao o cliente que receber o
	///     pacote nao acha corpo nenhum e sai calado -- que e o segundo cinto desta mesma regra;
	///   * <b>o vizinho</b> -- mesmo planeta. Sem ele, "nao chegou no estranho" passaria num anuncio que
	///     nao chegou em NINGUEM (uma lista vazia satisfaz qualquer "nao contem");
	///   * <b>o estranho</b> -- outro planeta PRE-FEITO, e nao um interior nem o espaco: o corte que se
	///     quer medir e o da ZONA, e Namek e uma zona tao planeta quanto a Terra (`Espaco.EhPlaneta`
	///     responde `true` pras duas). Escolher o espaco aqui deixaria a checagem passar pelo motivo
	///     errado.
	/// ==========================================================================================
	///
	/// Devolve nulo se o jogador nao existe (a bancada reprova com isso).
	/// </summary>
	internal (int[] Destinos, int QuemVirou, int Vizinho, int Estranho)? MedirAlcanceDoAnuncio(int idLocal)
	{
		if (!_players.TryGetValue(idLocal, out ServerPlayer? eu)) return null;

		// O OUTRO PLANETA E ESCOLHIDO E NAO CRAVADO: se a bancada um dia rodar em Namek, um "Namek"
		// literal aqui mediria o mesmo planeta duas vezes e a checagem passaria vazia.
		string outroNome = Jandirus.Core.World.Espaco.PreFeitos()
			.Select(p => p.Nome)
			.FirstOrDefault(n => !string.Equals(n, eu.Zone.Name, StringComparison.OrdinalIgnoreCase))
			?? "Namek";

		var quemVira = new ServerPlayer
		{
			Id = idLocal + 70_001, Peer = null, Name = "bancada: quem vira",
			Zone = eu.Zone, Pos = eu.Pos,
		};
		var vizinho = new ServerPlayer
		{
			Id = idLocal + 70_002, Peer = null, Name = "bancada: o vizinho de planeta",
			Zone = eu.Zone, Pos = eu.Pos,
		};
		var estranho = new ServerPlayer
		{
			Id = idLocal + 70_003, Peer = null, Name = "bancada: o de outro planeta",
			Zone = ZoneKey.Premade(outroNome), Pos = eu.Pos,
		};

		List<ServerPlayer> aqui = ZoneList(eu.Zone.Hash);
		List<ServerPlayer> la = ZoneList(estranho.Zone.Hash);
		aqui.Add(quemVira);
		aqui.Add(vizinho);
		la.Add(estranho);

		// ============================ E ELES ENTRAM NO `_players` TAMBEM ============================
		// Nao e enfeite: o `_players` e a OUTRA lista que uma "simplificacao" do `AnunciarForma` pegaria
		// (`foreach (ServerPlayer o in _players.Values)` -- manda pra todo mundo conectado, e o jogo
		// continua parecendo certo pra quem esta perto). Se os tres forjados vivessem so nas ZoneLists,
		// essa troca faria o vizinho SUMIR dos destinos em vez de fazer o estranho APARECER -- a bancada
		// reprovaria, sim, mas apontando pro lugar errado, que e como uma investigacao se perde.
		//
		// Sincrono e desfeito no `finally`: nada mais varre `_players` entre estas duas linhas (o tique
		// do servidor e esta chamada correm na mesma linha de execucao).
		// ======================================================================================
		_players[quemVira.Id] = quemVira;
		_players[vizinho.Id] = vizinho;
		_players[estranho.Id] = estranho;

		try
		{
			EscutaDeDestinos = [];
			// O FUNIL DE VERDADE. `AnunciarForma` e o unico caminho por onde subir, descer, cair por Ki
			// zerado e o DirectSSJ passam (o proprio comentario dele diz "sao SEIS chamadores hoje"), e o
			// laco que recorta a zona mora dentro dele. Remontar o pacote aqui provaria a copia.
			AnunciarForma(quemVira, Catalogo.IdBase, "ssj1", estreia: true);
			int[] destinos = (EscutaDeDestinos ?? []).Select(d => d.Para).ToArray();
			return (destinos, quemVira.Id, vizinho.Id, estranho.Id);
		}
		finally
		{
			EscutaDeDestinos = null;
			aqui.Remove(quemVira);
			aqui.Remove(vizinho);
			la.Remove(estranho);
			_players.Remove(quemVira.Id);
			_players.Remove(vizinho.Id);
			_players.Remove(estranho.Id);
		}
	}

	/// <summary>O que os tres relogios de Ki fizeram durante e depois de uma cinematica.</summary>
	internal readonly record struct CongelamentoMedido(
		string Forma, double PresoPorSegundos,
		double KiAoEntrar, double KiNoMeioDaCena, double KiDepoisDaCena,
		double MaestriaAoEntrar, double MaestriaNoMeioDaCena, double MaestriaDepoisDaCena,
		double RestanteNoMeio, double SegundosMedidosDentro,
		bool NocauteDerrubouAForma, double CenaAposNocaute, double PresoAntesDoNocaute,
		// ---- A CENA MAIS LONGA DO JOGO, do primeiro ao ultimo tique. Ver `MedirACenaLonga`. ----
		string FormaLonga, double PresoLongo, int TiquesLongos,
		double KiAoEntrarNaLonga, double KiNoFimDaLonga,
		double MaestriaAoEntrarNaLonga, double MaestriaNoFimDaLonga,
		double SegundosAteODrenoVoltar, double PassoDoTique);

	/// <summary>
	/// O KI DURANTE A CINEMATICA -- medido nos TRES tiques de producao, na ordem de producao.
	///
	/// ============================ POR QUE ELA MORA NO SERVIDOR E NAO NA BANCADA DO CLIENTE ============================
	/// O congelamento e um portao dentro de tres metodos privados (`TickDaForma`, `TickDaCarga`,
	/// `TickDoVoo`), e os tres so contam a mesma verdade quando rodam JUNTOS: congelar so o dreno da
	/// forma faria o Ki SUBIR na cena (a regeneracao continuaria), e uma bancada que chamasse so o
	/// tique da forma daria verde exatamente nesse defeito. Por isso o que esta exposto aqui e a
	/// MEDIDA, e nao os tiques -- quem os chama e este metodo, na mesma ordem do <see cref="Tick"/>.
	///
	/// O CORPO E FORJADO e nao o do host, pelo mesmo motivo do <see cref="MedirAlcanceDoAnuncio"/> e
	/// do `AProporcaoDeKi`: isto mexe em Ki, forma, maestria e nocaute, e o jogador que esta rodando a
	/// bancada tem que terminar como comecou. `Peer` nulo, fora do `_players` -- o anuncio de forma sai
	/// pra uma `ZoneList` vazia e nao acende cena na tela de ninguem.
	///
	/// A TRANSFORMACAO E A DE VERDADE (`Transformar`, a mesma funcao da tecla C): e ela que passa pelo
	/// `AnunciarForma`, que e o unico lugar onde o prazo da cena e anotado. Escrever `CenaSegundos` a
	/// mao aqui testaria a bancada.
	/// ============================================================================================================
	/// </summary>
	internal CongelamentoMedido? MedirCongelamentoNaCena()
	{
		// ============================ A ZONA NAO PODE SER A `default` ============================
		// A primeira versao disto deixou o `Zone` no valor de fabrica, e a bancada MORREU:
		// `ZoneKey.Hash` desreferencia o nome, e o `foreach (ZoneList(pl.Zone.Hash))` do
		// `AnunciarForma` levou um NullReference que abortou o `_Process` inteiro do robo -- as
		// checagens seguintes sumiram e uma checagem de OUTRO bloco reprovou por tabela. Corpo forjado
		// precisa de zona de verdade.
		//
		// E ELA E ESCOLHIDA VAZIA: um planeta pre-feito onde nao ha ninguem conectado. Assim o anuncio
		// desta transformacao inventada sai pra uma lista vazia e nao chega a tela nenhuma -- nem a de
		// quem esta rodando a bancada, que veria a musica e o tremor de uma cena que nao e dele.
		// ====================================================================================
		ZoneKey zona = ZoneKey.Premade(
			Jandirus.Core.World.Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(
					p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");

		ServerPlayer Forjar(int id)
		{
			var novo = new ServerPlayer
			{
				Id = id, Peer = null, Name = "bancada: o congelamento",
				Race = "Saiyan", Zone = zona,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = "Saiyan", BP = 3_000_000 },
			};

			// ============================ O CORPO FORJADO NASCE EM LUTO ============================
			// O tronco Saiyajin passou a cobrar a raiva do LUTO (`Catalogo.RaivaExigida`), e este
			// bloco inteiro comeca com um `Transformar(pl, subir: true)`: sem raiva o corpo forjado
			// nao sai da base, o metodo devolve `null` e a bancada do cliente reprova com "a bancada
			// conseguiu transformar um corpo forjado" -- uma FALHA que nao fala do que ela mede.
			// Foi exatamente o que aconteceu na primeira rodada do `--diagforma` depois da regra nova.
			//
			// PELO GANCHO E NAO PELO CAMPO: a raiva entra por onde a amizade vai entrar um dia, entao
			// esta linha continua valendo se o prazo, a janela ou o nome do campo mudarem. E ela nao
			// esconde gate nenhum -- o assunto daqui e o Ki parado na cena, e quem prova que o tronco
			// RECUSA sem raiva sao a bancada `raiva` e o `ADuplaRaivaAoVivo`.
			// ==================================================================================
			AmigoAbatido(novo, "um amigo de bancada", NivelDeRaiva.Extrema);
			return novo;
		}

		var pl = Forjar(80_001);
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;

		Transformar(pl, subir: true);
		if (pl.Forma.NaBase || pl.CenaSegundos <= 0) return null;   // a bancada reprova com o nulo

		string forma = pl.Forma.Atual;
		double preso = pl.CenaSegundos;
		double kiAoEntrar = pl.Ficha.Ki, mAoEntrar = pl.Forma.Maestria.De(forma);

		// DENTRO DA CENA. O passo e o do jogo (30 Hz) e o trecho e uma FRACAO do prazo -- medir a cena
		// inteira nao distinguiria "congelou" de "acabou logo".
		const double Dt = 1.0 / 30.0;
		double dentro = Math.Min(preso * 0.5, 8.0);
		for (double t = 0; t < dentro; t += Dt) TiqueDosRelogiosDeKi(pl, Dt);

		double kiNoMeio = pl.Ficha.Ki, mNoMeio = pl.Forma.Maestria.De(forma), restante = pl.CenaSegundos;

		// E DEPOIS DELA: o congelamento tem que ACABAR. E o modo de falha oposto, e o mais caro dos
		// dois -- uma forma que nunca mais cobra Ki e uma forma eterna.
		for (double t = 0; t < preso + 4.0; t += Dt) TiqueDosRelogiosDeKi(pl, Dt);
		double kiDepois = pl.Ficha.Ki, mDepois = pl.Forma.Maestria.De(forma);

		// ---- O NOCAUTE NO MEIO, num corpo novo (o de cima ja gastou a estreia) ----
		var ko = Forjar(80_002);
		ko.Ficha.Statify();
		ko.Ficha.Ki = ko.Ficha.MaxKi;
		Transformar(ko, subir: true);
		double presoAntes = ko.CenaSegundos;
		for (double t = 0; t < Math.Min(presoAntes * 0.25, 3.0); t += Dt) TiqueDosRelogiosDeKi(ko, Dt);
		ko.Ficha.KO = true;
		TiqueDosRelogiosDeKi(ko, Dt);

		(string fLonga, double pLongo, int nTiques, double kEntra, double kFim,
		 double mEntra, double mFim, double ateODreno) = MedirACenaLonga(Forjar(80_003), Dt);

		return new CongelamentoMedido(
			forma, preso, kiAoEntrar, kiNoMeio, kiDepois,
			mAoEntrar, mNoMeio, mDepois, restante, dentro,
			ko.Forma.NaBase, ko.CenaSegundos, presoAntes,
			fLonga, pLongo, nTiques, kEntra, kFim, mEntra, mFim, ateODreno, Dt);
	}

	/// <summary>
	/// A CENA MAIS LONGA DO JOGO, TIQUE A TIQUE, DO COMECO AO FIM -- e nao uma amostra dela.
	///
	/// ============================ POR QUE UMA CENA CURTA NAO BASTA ============================
	/// A medida de cima roda 8 s de uma cena de 25,0 s, e 8 s provam que o portao existe. Nao provam
	/// o que o dono realmente reclamou: a cena do SSJ3 prende **140 s**, e o defeito era o tanque
	/// esvaziar INTEIRO nesse tempo e a forma cair no meio da propria estreia. Um congelamento que
	/// valesse so no comeco (um prazo que zerasse cedo, um `Math.Max` que travasse o contador, uma
	/// perda de precisao somando 4.260 subtracoes de 1/30) passaria naquela amostra e falharia aqui.
	///
	/// E ELA E DERIVADA, e nao "a do ssj3": `Todas.MaxBy(SegundosPreso)`. No dia em que outra cena
	/// passar a ser a mais longa, e ela que vira o pior caso -- que e a pergunta que esta medida faz.
	///
	/// ============================ A FORMA E FORCADA PELAS TRES LINHAS DO ADMIN ============================
	/// `Entrar` + `AplicarForma` + `AnunciarForma` sao, na ordem, exatamente o que o `AdminForcarForma`
	/// faz na secao "3. A ESCADA". Subir pela tecla C ate a forma mais alta exigiria BP, maestria e
	/// tres estreias -- e nenhuma dessas coisas e o que se quer provar aqui. O que NAO se pode pular e
	/// o `AnunciarForma`: ele e o funil unico que anota o prazo da cena, e escrever `CenaSegundos` a
	/// mao testaria a bancada.
	/// ================================================================================================
	/// </summary>
	private (string Forma, double Preso, int Tiques, double KiEntra, double KiFim,
			 double MaestriaEntra, double MaestriaFim, double AteODrenoVoltar)
		MedirACenaLonga(ServerPlayer pl, double dt)
	{
		Cinematica maisLonga = Cinematicas.Todas.MaxBy(c => c.SegundosPreso)!;
		FormaDef? d = Catalogo.Def(maisLonga.Forma);
		if (d == null) return ("", 0, 0, 0, 0, 0, 0, -1);   // a bancada reprova pelo prazo zero

		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;

		string antes = pl.Forma.Atual;
		bool estreia = pl.Forma.Entrar(d.Id);
		AplicarForma(pl);
		pl.Ficha.Ki = pl.Ficha.MaxKi;         // ki cheio ao forcar, como no verb do admin
		AnunciarForma(pl, antes, d.Id, estreia);

		double preso = pl.CenaSegundos;
		double kiEntra = pl.Ficha.Ki, mEntra = pl.Forma.Maestria.De(d.Id);
		double kiFim = kiEntra, mFim = mEntra, ateODreno = -1, andado = 0;
		int tiques = 0;

		// ATE PASSAR DO PRAZO, e nao ate o prazo: o instante em que o dreno VOLTA e metade da regra
		// (congelamento que nao acaba e forma eterna), e ele so se ve tiquando alem do fim.
		while (andado < preso + 2.0)
		{
			TiqueDosRelogiosDeKi(pl, dt);
			andado += dt;
			tiques++;

			// A FOTO DO FIM E O ULTIMO TIQUE QUE AINDA ESTAVA EM CENA. O `CenaSegundos` e abatido no
			// TOPO do `TickDaForma`, entao o tique que consome o resto do prazo ja e o primeiro tique
			// LIVRE -- ler o Ki depois dele daria "o congelamento vazou" com o codigo certo.
			if (pl.CenaSegundos > 0) { kiFim = pl.Ficha.Ki; mFim = pl.Forma.Maestria.De(d.Id); }

			if (ateODreno < 0 && Math.Abs(pl.Ficha.Ki - kiEntra) > 1e-9) ateODreno = andado;
		}

		return (d.Id, preso, tiques, kiEntra, kiFim, mEntra, mFim, ateODreno);
	}

	/// <summary>
	/// OS TRES RELOGIOS QUE MEXEM NO KI, um tique, na ORDEM DO <see cref="Tick"/>.
	///
	/// A ordem nao e decorativa: o proprio `Tick` documenta que forma, carga e voo rodam juntos
	/// "porque mexem no MESMO Ki", e chamar dois deles aqui provaria um congelamento que o terceiro
	/// desfaz. So esta bancada chama isto.
	/// </summary>
	private void TiqueDosRelogiosDeKi(ServerPlayer pl, double dt)
	{
		TickDaForma(pl, dt);
		TickDaCarga(pl, dt);
		TickDoVoo(pl, (float)dt);
	}
}
