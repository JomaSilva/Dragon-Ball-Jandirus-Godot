using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Items;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O VACUO COBRA -- o encanamento do <see cref="Vacuo"/>.
///
/// ============================ O QUE ESTE ARQUIVO E, EM UMA FRASE ============================
/// Ele pergunta tres coisas por corpo, uma vez por segundo (esta ele no espaco? esta abrigado?
/// respira?) e, se as tres derem na pior resposta, manda dano pelo funil de sempre. Quem decide
/// QUEM respira e QUANTO custa e o <see cref="Vacuo"/>, no Core.
/// ========================================================================================
///
/// ============================ OS TRES ABRIGOS, E SO UM PRECISOU DE LINHA ============================
/// O dono pediu tres: traje, pod e nave-capital. **Dois ja estavam resolvidos e o terceiro custou
/// duas linhas** -- e dizer isso importa mais do que escrever as excecoes, porque excecao que nao
/// faz nada e a que apodrece:
///
///   1. **TRAJE** -- a unica linha nova: `Roupa Espacial` ou `Respirador` na mochila. As duas ja
///      existiam no `construcoes.json` (extraidas do DM com custo e tech), ja dava pra compra-las na
///      bancada Tech, e ate hoje a unica coisa que elas sabiam fazer era virar movel no chao. Ver o
///      comentario delas em `Core/Items/Inventario.cs`, que explica por que a protecao e ESTAR COM a
///      roupa e nao um bit de "equipado".
///
///   2. **POD / FOGUETE / SPACEPOD** -- `EstaPilotando(pl.Id)`, que ja existia
///      (`GameServer.Nave.cs:229`) e ja alimenta o bit do snapshot. **Este e o unico caso que
///      PRECISA de excecao escrita**, porque a nave copia a zona do piloto
///      (`GameServer.Nave.cs:659-662`): quem esta pilotando esta, pra todos os efeitos do servidor,
///      na zona do espaco. E o `pilot.ship = src` do DM (`PlanetTech.dm:133,144,307,316`), que faz
///      o `!ship` do `Stats.dm:218` falhar.
///
///   3. **CAPITAL SHIP** -- **NENHUMA LINHA, e a ausencia e a resposta.** O interior dela e uma zona
///      propria (`NaveGrande.ZonaDoInterior` = `ZoneKey.Interior("Nave", id)`,
///      `GameServer.NaveGrande.cs:52-57`), entao `Espaco.EhEspaco` ja devolve falso e este tique
///      sequer olha pra quem esta la dentro -- **inclusive pra quem esta na ponte pilotando**, que
///      tambem esta dentro. Escrever `|| EstaNaNaveGrande(pl)` aqui seria uma condicao que nunca
///      muda de valor: ela passaria em toda bancada, envelheceria calada e, no dia em que o interior
///      virasse outra coisa, mentiria. E o mesmo vale pra quem POUSOU num planeta gerado
///      (`ZoneKey.Procedural(nome, seed)` com nome != "Espaco").
/// ================================================================================================
///
/// ============================ O FUNIL E UM SO, E ELE RESPEITA O `Intocavel` ============================
/// O dano sai por <see cref="EspalharDanoG3"/> -- o mesmo `SpreadDamage` (`Injuries.dm:63-87`) que o
/// esmagamento por gravidade usa, com `autor == vitima` porque **nao ha algoz**: o vacuo nao e um
/// jogador, nao ha Zenkai por PK, nao ha karma e nao ha `UltimoAgressor`.
///
/// E ele ja recusa `c.Intocavel` na propria porta (`GameServer.Tecnicas.G3.cs:1209`). O sufocamento
/// **nao nasce com o furo do calor da estrela**, que era o unico dano de terceiro do port a escapar
/// da imunidade da cinematica de transformacao -- e que ja foi tapado (`GameServer.Sol.cs:149`).
/// Ainda assim ha um `Intocavel` explicito aqui, e ele nao e redundante: ele para ANTES do AVISO. Um
/// jogador estreando o SSJ3 nao pode receber "você está sufocando" enquanto nada esta acontecendo
/// com ele -- o aviso e uma promessa de dano, e promessa que nao se cumpre le como bug igual.
/// =====================================================================================================
///
/// ============================ QUEM MAIS NAO SUFOCA, E POR QUE ============================
///   * **MORTO** -- `pl.Ficha.dead`. O cadaver E um `ServerPlayer` em `_players` neste port (ver
///     `GameServer.Cadaver.cs`) e portanto chegaria ate aqui. O `EspalharDanoG3` ja o recusaria, mas
///     a saida e antes: um corpo sendo carregado pelo vacuo nao pode receber avisos de sufocamento,
///     e "morrer de novo" nao e um estado que exista.
///   * **NPC do mundo -- SUFOCA**, por pedido explicito do dono (*"NPC tambem, se ele estiver no
///     vacuo"*). O DM da ao `isNPC` um `sleep(20)` extra no laco do `Stats()` (`Stats.dm:143`), o
///     que na pratica multiplica o folego dele por ~11 -- **isso NAO foi portado de proposito**:
///     aquele `sleep` e um afrouxamento de CUSTO do laco de status do BYOND, nao um pulmao maior. Um
///     NPC no vacuo aguenta os mesmos 20 s de todo mundo.
///   * **NOCAUTEADO NO COLO DE ALGUEM -- SUFOCA.** Quem carrega um inconsciente pro vacuo escolheu
///     isso, e a cena e do jogo. O aviso, porem, vai pros DOIS (ver <see cref="AvisarDoVacuo"/>):
///     quem esta apagado nao le nada, e sem a linha no chat de quem carrega a morte seria muda.
///   * **ADMIN -- SUFOCA, e a ausencia da excecao e deliberada.** O sistema irmao deste (o calor da
///     estrela) nao isenta admin, e o DM nao tem precedente -- o `Admin` de `Turfs.dm:65` e de `obj`.
///     Alem disso o host **e** admin, e isentar admin criaria exatamente o cego de bancada que este
///     port ja pagou uma vez no voo com altitude: a prova subiria o servidor, poria o host no vacuo
///     e mediria zero dano, verde, pra sempre. Admin que quiser passear leva um traje.
/// ======================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// ============================ AS SONDAS -- POR ONDE A BANCADA TROCA A REGRA ============================
	/// Cada campo aqui e a pergunta que <see cref="SufocaAgora"/> e <see cref="Sufocar"/> fazem, e o
	/// valor de cada um E a funcao de producao. Em jogo isto nao muda nada: o caminho saudavel chama
	/// exatamente `Espaco.EhEspaco`, `EstaPilotando`, `TemTrajeEspacial`, `Vacuo.RespiraNoVacuo`,
	/// `Vacuo.CargoRespira` e `Vacuo.DanoPorSegundo`, uma vez por corpo por segundo.
	///
	/// **ELAS EXISTEM PRA A BANCADA PODER REPROVAR.** Uma prova que nunca ficou vermelha e uma frase,
	/// e quase todo defeito deste sistema e de CODIGO e nao de dado: "alguem esqueceu o abrigo da
	/// pod", "a pergunta de zona ficou larga e o interior da nave virou espaco", "o dano ganhou um
	/// divisor de BP por simetria com o sol", "o guarda do `Intocavel` saiu". Nao ha estado de mundo
	/// que encene essas quatro coisas -- o unico jeito honesto de provar que a bancada as pegaria e
	/// rodar as MESMAS provas contra o codigo mutante, e e isto que a `--vacuoteste` faz (ver
	/// `GameServer.VacuoTeste.cs`, `ComDefeito`).
	///
	/// E o padrao nao e novo nesta casa: e o mesmo `Regras` da bancada da agua
	/// (`Tools/AssetPipeline/AguaBench.cs`), so que aqui ele mora no servidor porque tres das seis
	/// perguntas (zona, nave, mochila) sao do servidor e nao do Core.
	/// =================================================================================================
	/// </summary>
	internal sealed class SondasDoVacuo
	{
		/// <summary>Esta zona e o vacuo? O `!isspace` do `Stats.dm:218`.</summary>
		public Func<ZoneKey, bool> EhEspaco = Espaco.EhEspaco;

		/// <summary>Esta dentro de uma pod/foguete? O `!ship` do `Stats.dm:218`. Ver o cabecalho.</summary>
		public Func<int, bool> Pilotando = _ => false;

		/// <summary>A roupa espacial esta com ele? O `!spacesuit` do `Stats.dm:218`.</summary>
		public Func<ServerPlayer, bool> TemTraje = TemTrajeEspacial;

		/// <summary>O cargo dele da folego? `GodOfDestruction.dm:121`.</summary>
		public Func<string, bool> CargoRespira = Vacuo.CargoRespira;

		/// <summary>
		/// O DNA DA FORNADA DELE RESPIRAVA? -- a leitura de <see cref="Fighter.bio_dna_respira"/>,
		/// escrita uma vez no parto (`NascerBioAndroide`).
		///
		/// Ela e sonda e nao leitura direta pelo motivo de sempre: os DOIS defeitos possiveis aqui
		/// sao mudos. *"o bio parou de herdar"* devolve o port ao estado anterior a este trabalho, e
		/// *"todo bio respira"* e o DM 1:1 (`statbiodroid.dm:51`) -- que **nao** e o que o dono
		/// pediu. Sem poder injetar os dois, a familia 11 nao teria como provar que sabe reprovar.
		/// </summary>
		public Func<Fighter, bool> FolegoDoDna = f => f.bio_dna_respira;

		/// <summary>A regra racial inteira -- o `spacebreather` do DM.</summary>
		public Func<string?, string?, bool, bool, bool> Respira = Vacuo.RespiraNoVacuo;

		/// <summary>O corpo esta em cinematica/carencia? Ver o comentario dentro de `Sufocar`.</summary>
		public Func<CombatState, bool> Intocavel = c => c.Intocavel;

		/// <summary>Quanto custa um segundo de vacuo em cada membro.</summary>
		public Func<double, double> Dano = Vacuo.DanoPorSegundo;

		/// <summary>O dano do vacuo MATA. Um vacuo que so desmaia nao e um vacuo.</summary>
		public bool Letal = true;
	}

	/// <summary>
	/// AS SONDAS DESTE SERVIDOR. Preguicosa porque uma delas (<see cref="EstaPilotando"/>) e metodo
	/// de instancia e nao cabe num inicializador de campo. A bancada troca o campo inteiro e devolve
	/// `null` no `finally` -- e devolver nulo REMONTA os padroes de producao, que e mais seguro do
	/// que guardar e repor campo a campo (um campo esquecido ficaria mutante no servidor vivo).
	/// </summary>
	private SondasDoVacuo? _sondasDoVacuo;
	private SondasDoVacuo Sondas => _sondasDoVacuo ??= new SondasDoVacuo { Pilotando = EstaPilotando };

	/// <summary>
	/// A CADA QUANTO O AVISO DE SUFOCAMENTO SE REPETE, em segundos.
	///
	/// Cinco, e o numero sai do orcamento e nao do gosto: sao <see cref="Vacuo.SegundosDeFolego"/>
	/// de vida, entao cinco segundos dao QUATRO linhas antes da morte -- a primeira no instante em
	/// que o ar acaba, a ultima com o corpo ja quase apagado. O esmagamento usa dez
	/// (`SegundosEntreAvisosDeEsmagamento`, o `GRAVCRUSH_WARN_CD` do DM) porque la o castigo pode
	/// durar horas; aqui ele dura vinte segundos e dez daria duas linhas.
	/// </summary>
	private const double SegundosEntreAvisosDeVacuo = 5;

	/// <summary>
	/// QUEM ESTAVA SUFOCANDO NO SEGUNDO PASSADO, e quanto falta pro proximo aviso dele.
	///
	/// **A PRESENCA NO DICIONARIO E O ESTADO**, e e ela que faz existir a mensagem de ALIVIO: sem
	/// guardar quem estava sufocando nao ha como saber que alguem PAROU -- e parar (vestiu o traje,
	/// entrou na pod, pousou) e o momento em que o jogador mais precisa de uma confirmacao de que
	/// fez a coisa certa. E a mesma disciplina do `antes != Dentro` do `TickDoSol`.
	/// </summary>
	private readonly Dictionary<int, double> _sufocando = [];

	/// <summary>
	/// O BUFFER DE QUEM SUFOCA NESTE SEGUNDO, reusado. Mesmo argumento do `_noEspaco`
	/// (`GameServer.RelogiosDoCorpo.cs`): o dano pode MATAR, e matar mexe nas colecoes no meio da
	/// volta, entao a copia e necessaria -- mas ela e do tamanho de quem esta MESMO no vacuo (quase
	/// sempre zero) e nao do tamanho do servidor.
	/// </summary>
	private readonly List<ServerPlayer> _paraSufocar = [];

	/// <summary>
	/// UMA VEZ POR SEGUNDO -- chamado do bloco de 1 Hz do <see cref="Tick"/>, ao lado do
	/// <see cref="TickDoEsmagamento"/>.
	///
	/// ============================ A CADENCIA E A REGRA ============================
	/// O calor da estrela roda no tique CHEIO com `dt` porque a estrela e um lugar por onde se
	/// ATRAVESSA: entrar e sair dela em 200 ms tem que custar 200 ms de dano. O vacuo nao e assim --
	/// ninguem "roça" o espaco --, e a constante que o governa e um prazo de vinte segundos vindo de
	/// um contador de inteiros do DM. Um segundo e a menor unidade que esse prazo distingue.
	///
	/// E o custo importa: este e o unico laco do sistema, e ele varre `_players` uma vez por segundo
	/// fazendo uma comparacao de string por corpo (`Espaco.EhEspaco`). No tique cheio seriam trinta.
	/// ==========================================================================
	///
	/// **O LACO E SOBRE TODO MUNDO E NAO SO SOBRE QUEM ESTA NO ESPACO**, e isso e de proposito: e
	/// aqui que se descobre que alguem PAROU de sufocar (pousou, entrou na nave, deslogou pro
	/// planeta). Um laco restrito a `EhEspaco` nunca visitaria quem saiu, e a mensagem de alivio
	/// nunca sairia -- ficaria o id preso no dicionario ate o corpo se desconectar.
	/// </summary>
	private void TickDoVacuo()
	{
		_paraSufocar.Clear();

		foreach (ServerPlayer pl in _players.Values)
		{
			if (SufocaAgora(pl)) { _paraSufocar.Add(pl); continue; }

			// PAROU DE SUFOCAR: o alivio, uma vez. `Remove` devolve se ele estava la, entao nao ha
			// um segundo `ContainsKey` -- e nem um alivio pra quem nunca esteve no vacuo.
			//
			// **MORTO NAO RECEBE O ALIVIO.** Quem morreu sufocando ja saiu do dicionario no
			// `Sufocar`; quem morreu de OUTRA coisa enquanto sufocava sairia por aqui, e "você volta
			// a respirar" logo abaixo da linha da propria morte e a piada que ninguem escreveu.
			if (_sufocando.Remove(pl.Id) && !pl.Ficha.dead) Avisar(pl, "você volta a respirar.");
		}

		foreach (ServerPlayer pl in _paraSufocar)
		{
			// MOB-ZUMBI: a volta anterior pode ter matado este corpo, e a morte de um NPC o tira do
			// mundo. Mesma receita do `_noEspaco` e do `_dirigidos` -- conferir a cada volta.
			if (_players.ContainsKey(pl.Id)) Sufocar(pl);
		}
	}

	/// <summary>
	/// ============================ A PERGUNTA, E ELA E UMA SO ============================
	/// Tres negativas, na ordem do mais barato pro mais caro: esta no espaco? nao esta pilotando?
	/// nao respira? A ultima e a unica que abre a mochila.
	///
	/// A REGRA RACIAL NAO ESTA AQUI -- ela e <see cref="Vacuo.RespiraNoVacuo"/>, no Core, e e por
	/// isso que o Deus da Destruicao (`Ranks/GodOfDestruction.dm:121`), o androide convertido
	/// (`DNALabs.dm:185`) e o implante de torax (`Cyborgs.dm:751-770`) vao poder entrar pelo
	/// `folegoConcedido` sem que uma linha deste arquivo mude. Hoje ele e `false` porque nenhum dos
	/// tres existe no port; o dia em que existir, o parametro ja esta no lugar.
	/// ==================================================================================
	/// </summary>
	private bool SufocaAgora(ServerPlayer pl)
	{
		SondasDoVacuo s = Sondas;

		if (!s.EhEspaco(pl.Zone)) return false;

		// MORTO NAO SUFOCA -- e o cadaver E um corpo de `_players` neste port. Ver o cabecalho.
		if (pl.Ficha.dead || pl.Combate == null) return false;

		// ABRIGO 2, o unico que precisou de linha: dentro da pod, a zona e a do espaco.
		if (s.Pilotando(pl.Id)) return false;

		// ABRIGO 1 + a raca + o CARGO + o DNA DO BIO, numa pergunta so.
		//
		// O `folegoConcedido` deixou de ser `false` cravado: o Deus da Destruicao respira enquanto
		// portar o titulo (`GodOfDestruction.dm:118-121`) e devolve o folego junto com o trono
		// (`:143`) -- e como o cargo mora na CONTA (`CargoDe`), largar o trono ja tira o folego sem
		// uma segunda linha em lugar nenhum.
		//
		// ============================ E O BIO-ANDROIDE ENTROU PELO MESMO PARAMETRO ============================
		// *"bio androides pegam a capacidade de respirar no espaco caso uma das racas q esta em seu
		// dna consiga"*. A raca dele **nao** esta na lista do Core, e nao pode estar: o folego nao e
		// do cracha "BioAndroid", e sim dos doadores da fornada -- dois bios da mesma especie, um de
		// doador Majin e outro de quatro humanos, tem respostas diferentes.
		//
		// Por isso ele chega aqui como <see cref="Fighter.bio_dna_respira"/>, um bit escrito UMA vez
		// no parto (`GameServer.Tech.NascerBioAndroide`) a partir da MESMA `Vacuo.RespiraNoVacuo` que
		// esta linha usa -- e nao como uma raca a mais numa lista. E um `||` puro, entao ele compoe
		// sem ordem: quem herdou o folego e ainda veste o traje nao respira duas vezes, e quem nao
		// herdou continua salvo pela roupa, pela pod e pelo cargo.
		//
		// Sobram do DM dois portadores que este port ainda nao tem, e eles plugam AQUI sem tocar no
		// dano: o androide de laboratorio (`DNALabs.dm:185` -- a CONVERSAO em Android, Lab=1, que e
		// outro sistema) e o `Rebreather_Module` de ciborgue (`Cyborgs.dm:751-770`).
		// ===================================================================================================
		return !s.Respira(pl.Ficha.Race, pl.Ficha.ParentRace,
						  s.CargoRespira(CargoDe(pl.Conta)) || s.FolegoDoDna(pl.Ficha),
						  s.TemTraje(pl));
	}

	/// <summary>
	/// A ROUPA ESPACIAL ESTA COM ELE? Qualquer uma das duas do DM serve -- o `Spacesuit`
	/// (`PlanetTech.dm:357-396`) ou o `Rebreather` (`Tier2.dm:188-207`), que la somam na mesma var
	/// `spacesuit` e aqui respondem a mesma pergunta booleana.
	///
	/// **E a MOCHILA e nao um bit**, porque a mochila vai pro disco e o bit nao iria: ver o
	/// comentario das duas em `Core/Items/Inventario.cs`. Um NPC do mundo simplesmente nao tem
	/// nenhuma das duas, e por isso a mesma linha serve pra ele.
	/// </summary>
	/// <remarks>
	/// QUEM SABE QUAIS ITENS PROTEGEM E O CATALOGO (<see cref="CatalogoDeItens.ProtegeDoVacuo"/>), e
	/// nao esta linha: uma terceira roupa entra la, num lugar so, e este arquivo nao fica sabendo. Um
	/// `Quantos(Traje) > 0 || Quantos(Respirador) > 0` aqui seria a lista escrita pela segunda vez.
	/// </remarks>
	private static bool TemTrajeEspacial(ServerPlayer pl)
	{
		foreach (Pilha p in pl.Mochila.Pilhas)
			if (p.Quantidade > 0 && CatalogoDeItens.ProtegeDoVacuo(p.Id)) return true;
		return false;
	}

	/// <summary>
	/// UM SEGUNDO DE VACUO NUM CORPO.
	///
	/// ============================ O DANO SAI DO NUCLEO MAIS CHEIO ============================
	/// `EspalharDanoG3` bate o MESMO valor em cada membro nao-aninhado, e a morte vem quando um
	/// NUCLEO zera (`Body.DeveMorrer`, e sao tres: Cabeca, Torso e Abdomen). Entao o valor tem que
	/// ser calculado sobre a vida cheia do nucleo mais resistente que ainda existe -- assim o ultimo
	/// deles chega a zero em exatamente <see cref="Vacuo.SegundosDeFolego"/>, que e o prazo do DM.
	///
	/// Ler `VidaMax` em vez de cravar 100 nao e preciosismo: `BodyPart.VidaMax` e um campo, e o dia
	/// em que uma raca ou uma forma o escalar, este sistema acompanha sozinho em vez de virar uma
	/// morte instantanea (ou uma imortalidade) descoberta em produção.
	/// ====================================================================================
	/// </summary>
	private void Sufocar(ServerPlayer pl)
	{
		CombatState c = pl.Combate!;
		SondasDoVacuo s = Sondas;

		// ============================ O CORPO INTOCAVEL NAO SUFOCA ============================
		// A cinematica de transformacao e a carencia de renascimento. O `EspalharDanoG3` ja recusaria
		// o dano na porta dele (`:1209`), mas a saida e ANTES do aviso: dizer "você está sufocando"
		// pra quem nada pode acontecer e prometer um dano que nao vem.
		//
		// **O ESTADO NAO E LIMPO AQUI**, e a diferenca importa: ele continua no `_sufocando`, entao
		// ao fim da cinematica o castigo retoma sem uma segunda mensagem de "o ar acabou" -- e se ele
		// escapar durante a cena, o alivio ainda sai. Um `return` que apagasse o estado daria ao
		// jogador uma cena de transformacao inteira de graca a cada entrada no vacuo.
		// =================================================================================
		if (s.Intocavel(c)) return;

		double vidaDeNucleo = 0;
		foreach (BodyPart p in c.Corpo.Partes)
			if (p.Papel == Vitalidade.Nucleo && !p.Decepado && p.VidaMax > vidaDeNucleo)
				vidaDeNucleo = p.VidaMax;

		double dano = s.Dano(vidaDeNucleo);
		if (dano <= 0) return;

		// O AVISO VEM ANTES DO DANO, e nao depois: quem morre neste tique tem que ter lido a ultima
		// linha ANTES da linha da morte. Na ordem inversa o chat diria "você sufoca" embaixo de
		// "você morreu", que e a mesma cena contada de tras pra frente.
		AvisarDoVacuo(pl, c, dano);

		bool eraVivo = !pl.Ficha.dead;

		// AUTOR = VITIMA porque nao ha algoz -- o funil trata esse caso (a Final Explosion o usa) e
		// manda a derrota pro `ZenkaiPorDerrota` em vez de enfurecer contra ninguem. `letal: true`
		// porque o vacuo PODE matar: no nao-letal o piso de `Ferir` para no limiar do nocaute, e um
		// vacuo que so desmaia nao e um vacuo.
		EspalharDanoG3(pl, pl, dano, letal: s.Letal);

		if (eraVivo && pl.Ficha.dead)
		{
			Avisar(pl, "o vácuo termina o que começou. Você sufoca e morre.");
			GD.Print($"[server] {pl.Name} SUFOCOU no vacuo (raca {pl.Ficha.Race})");
			_sufocando.Remove(pl.Id);
		}
	}

	/// <summary>
	/// O AVISO, e ele diz O QUE FAZER -- nao "você está sufocando" e ponto, mas as tres saidas, que
	/// e a mesma disciplina do <see cref="AvisarDoEsmagamento"/> (la o DM tem tres frases porque
	/// peso e gravidade se resolvem de jeitos diferentes).
	///
	/// A PRIMEIRA LINHA SAI NO INSTANTE em que o ar acaba (o corpo entra no dicionario com o relogio
	/// ja vencido), e ela e a unica que diz o prazo em segundos. As seguintes contam o que sobrou --
	/// e a conta e do CORPO (`vida do pior nucleo / dano`), nao um cronometro guardado: assim ela
	/// continua certa pra quem entrou no vacuo ja machucado, e pra quem tomou um tiro no meio.
	///
	/// ============================ E QUEM CARREGA O CORPO TAMBEM LE ============================
	/// Um nocauteado levado no colo pro vacuo **sufoca** (ver o cabecalho), e ele nao tem como saber
	/// disso: quem esta apagado nao le chat. Sem esta linha, quem carrega descobriria pelo cadaver.
	/// ======================================================================================
	/// </summary>
	private void AvisarDoVacuo(ServerPlayer pl, CombatState c, double dano)
	{
		bool primeira = !_sufocando.ContainsKey(pl.Id);

		double falta = primeira ? 0 : _sufocando[pl.Id] - 1;
		if (falta > 0) { _sufocando[pl.Id] = falta; return; }
		_sufocando[pl.Id] = SegundosEntreAvisosDeVacuo;

		if (primeira)
		{
			Avisar(pl, "o VÁCUO arranca o ar dos seus pulmões. Seu corpo aguenta "
					   + $"~{Vacuo.SegundosDeFolego:0} s sem uma Roupa Espacial (ou um Respirador) "
					   + "na mochila, dentro de uma pod ou dentro de uma nave.");
		}
		else
		{
			double sobra = double.PositiveInfinity;
			foreach (BodyPart p in c.Corpo.Partes)
				if (p.Papel == Vitalidade.Nucleo && !p.Decepado) sobra = Math.Min(sobra, p.Vida / dano);

			Avisar(pl, double.IsInfinity(sobra)
				? "você continua sufocando."
				: $"você está sufocando... (~{Math.Max(0, sobra):0} s antes do seu corpo ceder)");
		}

		// QUEM ESTA CARREGANDO ESTE CORPO LE JUNTO. So no caso do inconsciente: um corpo acordado ja
		// recebeu a propria linha acima, e mandar a mesma frase pro carregador duplicaria o chat.
		//
		// As tres condicoes sao as MESMAS do <see cref="SendoCarregado"/> (`GameServer.Agarrao.cs:99`)
		// -- e nao chamar aquela funcao e proposital: ela devolve um booleano e aqui o que se precisa
		// e do CORPO de quem carrega, pra mandar a linha pra ele. Duas perguntas, um estado so.
		if (pl.Ficha.KO && pl.AgarradoPorId != 0
			&& _players.TryGetValue(pl.AgarradoPorId, out ServerPlayer? carregador)
			&& carregador.AgarrandoId == pl.Id
			&& carregador.ModoDoAgarrao == ModoDeAgarrao.Carregando)
			Avisar(carregador, $"{pl.Name} está sufocando nos seus braços.");
	}

	/// <summary>O corpo foi embora: esquece o relogio do aviso dele. Ver `EsquecerEsmagamento`.</summary>
	private void EsquecerVacuo(int id) => _sufocando.Remove(id);
}
