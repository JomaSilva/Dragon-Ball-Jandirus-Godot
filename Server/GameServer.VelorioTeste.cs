using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ AS JANELAS DO VELORIO (`--velorio`) ============================
/// A bancada de DOIS CORPOS mora do lado do cliente (`Client/RoboDoVelorio.cs`) porque metade das
/// perguntas dela e sobre PIXEL -- a auréola sobre uma cabeca, o balao sobre a outra. Mas a outra
/// metade e sobre coisas que so o servidor sabe (quem viaja, quem nao viaja, quem fica preso), e o
/// cliente nao pode inventa-las.
///
/// Entao aqui ficam as JANELAS: encenar e fotografar. Nenhuma decide nada.
///
/// ============================ AS TRES REGRAS DESTE ARQUIVO ============================
///   1. **NENHUMA JANELA COPIA UMA REGRA.** Matar e `CombatState.Morrer` (o funil de producao, o
///      mesmo do soco e do Kamehameha); nocautear e `CombatState.Nocautear`; a triagem, a viagem e o
///      renascimento sao o TIQUE do servidor -- <see cref="VencerORelogioDoVelorioDeTeste"/> so
///      empurra o relogio pro passado e sai da frente. Uma janela que chamasse `IrProAlem` na mao
///      provaria que a funcao funciona e nao provaria que alguem a chama.
///
///   2. **A FOTO NAO PERGUNTA `Alem.TemAureola`.** Ela devolve <see cref="FotoDoVelorio.AureolaNoFio"/>,
///      que e o ultimo valor ENVIADO (`ServerPlayer.EnvAureola`). A regra de quem tem auréola esta
///      sendo reescrita no momento em que isto foi escrito (o cadaver dos 15 s deixou de ter uma), e
///      uma bancada que chamasse a funcao concordaria com ela por construcao -- inclusive errada.
///      O que esta bancada cobra e o que SAIU do servidor e o que virou PIXEL no cliente.
///
///   3. **TUDO O QUE ELA MEXE, ELA DESMONTA.** <see cref="DesmontarOVelorioDeTeste"/> tira os corpos
///      forjados do mundo e devolve o host inteiro. O host MORRE de verdade aqui dentro -- e o unico
///      jeito de exercitar a viagem, porque a triagem so leva quem tem dono na tela.
/// ==================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// Faixa de ids propria -- longe do `_nextId`, da faixa do `--alemteste` (91.300) e das outras.
	/// </summary>
	private const int IdBaseDoVelorio = 91_400;

	private readonly List<ServerPlayer> _corposDoVelorio = [];

	/// <summary>
	/// O ESTADO DE UM CORPO, NUM INSTANTE. Tudo o que a bancada do outro lado precisa saber e nao
	/// tem como ver -- e nada alem disso.
	/// </summary>
	/// <param name="Existe">Achou o corpo? Falso quer dizer "esse id nao esta no mundo".</param>
	/// <param name="Zona">O nome da zona (e a resposta de "foi pro alem?", pelo nome).</param>
	/// <param name="AureolaNoFio">O ultimo valor que o servidor ENVIOU pra zona -- ver a regra 2.</param>
	/// <param name="FaltamMs">Quanto falta pro proximo passo da morte (negativo = ja venceu).</param>
	/// <param name="SaindoDoMundo">Esta na fila de remocao (`_npcsPraTirar`) -- o destino do NPC.</param>
	/// <param name="NaMente">
	/// Esta dentro de uma <see cref="DimensaoMental"/>? A pergunta vem PRONTA daqui porque a resposta
	/// e do tipo da `ZoneKey` (uma zona de mente nao e `Premade`), e o nome sozinho -- que e o que a
	/// foto carrega -- nao a responde. Uma bancada que remontasse a chave pelo nome perguntaria
	/// sempre pela zona errada e a espera passaria no primeiro quadro.
	/// </param>
	/// <param name="NaPonte">Esta dentro do interior de uma nave (a zona DINAMICA)? Mesmo motivo.</param>
	internal readonly record struct FotoDoVelorio(
		bool Existe, string Zona, bool Morto, bool KO, bool DePe, bool Deitado,
		bool AureolaNoFio, long FaltamMs, bool SaindoDoMundo, bool NoMundo, bool PresoNaSala,
		bool NaMente, bool NaPonte);

	/// <summary>A FOTO. So le.</summary>
	internal FotoDoVelorio FotoDoVelorioDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl))
		{
			// O corpo pode ter SAIDO do mundo (e o destino do NPC morto) -- e isso e uma resposta, nao
			// um erro. Ainda assim se procura na fila de saida, que e onde ele esta no vao entre a
			// triagem e a manutencao.
			ServerPlayer? indo = _npcsPraTirar.Find(p => p.Id == id);
			return indo == null ? default : Retrato(indo, noMundo: false, saindo: true);
		}

		return Retrato(pl, noMundo: true, saindo: _npcsPraTirar.Contains(pl));
	}

	private FotoDoVelorio Retrato(ServerPlayer pl, bool noMundo, bool saindo) =>
		new(true, pl.Zone.Name, pl.Ficha.dead, pl.Ficha.KO, pl.MortoDePe, pl.Deitado,
			pl.EnvAureola, pl.RelogioDaMorte - NowMs(), saindo, noMundo, pl.SalaPreso,
			DimensaoMental.EhAMente(pl.Zone), NaveGrande.EhInterior(pl.Zone, out _));

	// =====================================================================
	// OS CORPOS QUE A BANCADA PRECISA TER AO LADO
	// =====================================================================
	/// <summary>
	/// FORJA UM CORPO ao lado de outro. Devolve o id, ou 0.
	///
	/// ============================ POR QUE FORJADO, E NAO `NascerNpc` ============================
	/// O corpo de CONTROLE desta bancada (o vivo que fica ao lado do morto o tempo todo) nao pode ter
	/// vontade propria: um cidadao de verdade tem <see cref="PapelDeNpc"/> e por isso tem CEREBRO --
	/// ele anda, foge, briga e pode ate morrer no meio da medida. O controle tem que estar parado e
	/// vivo do primeiro ao ultimo quadro, senao "o vivo nao tem auréola" as vezes mede um cadaver.
	///
	/// O `cidadao`, esse SIM nasce pelo caminho de producao (<see cref="NascerNpc"/>): ele existe pra
	/// provar o destino do NPC DO MUNDO na triagem, e forjar um "quase NPC" com `Papel` na mao seria
	/// medir a minha imitacao de habitante.
	/// ==========================================================================================
	/// </summary>
	/// <param name="papel">`vivo`, `reflexo`, `boneco` ou `cidadao` -- ver o `switch`.</param>
	internal int ForjarNoVelorioDeTeste(string papel, int aoLadoDe)
	{
		if (!_players.TryGetValue(aoLadoDe, out ServerPlayer? perto)) return 0;

		// UM PASSO PRO LADO (32 px = um tile): nascer EM CIMA do host poe dois bonecos no mesmo pixel,
		// e uma foto de duas cabecas com uma cabeca so nao prova nada.
		Vec2 onde = new(perto.Pos.X + 32f, perto.Pos.Y);

		if (papel == "cidadao")
		{
			ServerPlayer? npc = NascerNpc("cidadao", perto.Zone, onde, 91_400_000UL);
			if (npc == null) return 0;
			npc.Name = "velorio: o cidadao";
			_corposDoVelorio.Add(npc);
			return npc.Id;
		}

		var corpo = new ServerPlayer
		{
			Id = IdBaseDoVelorio + _corposDoVelorio.Count + 1,
			Peer = null,
			Name = "velorio: o " + papel,
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = perto.Zone,
			Pos = onde,
			Conta = "bancada_velorio",
			Slot = 0,
			Ficha = new Fighter { Race = "Human", BP = 1000 },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		corpo.Ficha.Class = "Normal";

		// ============================ O BONECO DO CORPO LARGADO CARREGA O `Peer` DO DONO ============================
		// E ele e o unico corpo do jogo que passa pelas DUAS primeiras pernas do crivo do
		// <see cref="Gente.EhJogador"/>: tem dono na tela e nao tem papel. So a TERCEIRA o recusa.
		// Emprestar o `Peer` aqui e seguro porque o destino deste grupo e escrever um numero e mais
		// nada -- nenhum pacote sai. (No corpo que VIAJASSE, o emprestimo mandaria um `ZoneChanged`
		// com o id errado e a tela do host trocaria de planeta sozinha.)
		// ========================================================================================================
		if (papel == "boneco")
		{
			corpo.DonoDoCorpoLargado = perto.Id;
			corpo.Peer = perto.Peer;
		}

		PorNoMundo(corpo);
		_corposDoVelorio.Add(corpo);
		return corpo.Id;
	}

	// =====================================================================
	// ENCENAR -- e cada verbo destes e um caminho de producao
	// =====================================================================
	/// <summary>
	/// MATA PELO FUNIL. `CombatState.Morrer` e o mesmo ponto por onde passam o soco letal, o
	/// Kamehameha, a explosao do planeta e a fome -- e e ele que dispara o gancho `AoMorrer`
	/// (`GameServer.Alem.AMorteAconteceu`), que e quem arma o relogio.
	///
	/// `ignorarSeguro` porque o berco tem carencia de spawn, e a bancada nao esta medindo a carencia.
	/// </summary>
	internal bool MatarNoVelorioDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate.Morrer(ignorarSeguro: true);

	/// <summary>
	/// NOCAUTEIA -- o CONTRA-EXEMPLO da bancada inteira. Nocaute e morte sao vizinhos neste jogo (a
	/// mesma pose, o mesmo corpo no chao) e a auréola e justamente o que tem que separar os dois.
	/// </summary>
	internal bool NocautearNoVelorioDeTeste(int id, double segundos)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		pl.Combate.Nocautear(segundos);
		return pl.Ficha.KO;
	}

	/// <summary>
	/// VENCE O RELOGIO -- e SO isso.
	///
	/// ============================ ELA NAO CHAMA A TRIAGEM, E ESSE E O PONTO ============================
	/// Quem chama `VenceuOPrazoDaMorte` e o `TickCombate` (`GameServer.Combat.cs`), 30 vezes por
	/// segundo, com o `if (pl.Ficha.dead && agora >= pl.RelogioDaMorte)` que e METADE da regra: e ele
	/// que recusa o NOCAUTEADO (que nao tem `dead`) sem uma linha propria pra isso.
	///
	/// Uma janela que chamasse a triagem na mao pularia justamente esse `if` -- e a bancada mediria a
	/// viagem de um corpo que o jogo nunca teria mandado viajar. Aqui ela empurra o vencimento pro
	/// passado e ESPERA o tique, como qualquer morte de verdade que ficou 15 s no chao.
	/// ================================================================================================
	/// </summary>
	internal bool VencerORelogioDoVelorioDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		pl.RelogioDaMorte = NowMs() - 1;
		return true;
	}

	/// <summary>
	/// ============================ MORTO E JA VIAJADO, ESCRITO DIRETO NA FICHA ============================
	/// O estado de um corpo que **ja tem auréola**, sem passar pela viagem. Ele existe pra a bancada
	/// poder ter um corpo com auréola que ENTRA no campo de visao de alguem depois de ja estar assim
	/// -- e essa e a unica forma de exercitar a linha do `World.VestirCorpoInteiro` que veste a
	/// auréola em quem NASCE na minha tela (o pacote e reliable e pode ter chegado antes de o boneco
	/// existir).
	///
	/// Sem esta janela nao ha como montar o caso: os unicos corpos que viajam sao os que tem dono na
	/// tela, e num processo so ha um desses. E o `--morte`, que tem dois processos, so olha um corpo
	/// que morre NA FRENTE dele -- ali o boneco ja existia. A linha ficava descoberta nas tres
	/// bancadas (medido: apagando-a, todas as tres continuavam verdes).
	///
	/// ESCREVER NA FICHA E O CAMINHO DE VERDADE de quatro pontos do jogo (o restauro da mente, a
	/// volta no tempo do `Tecnicas.G4`, o reflexo que nasce vivo, a gestacao do bio-androide) -- por
	/// isso a auréola sai por DIFERENCA no `TickDasAureolas`, e nao por chamada. Aqui e o quinto.
	/// ================================================================================================
	/// </summary>
	internal bool MarcarMortoJaViajadoNoVelorioDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		pl.Ficha.dead = true;
		pl.MorteJaViajou = true;   // o unico ponto desta bancada que toca o campo da etapa
		pl.RelogioDaMorte = long.MaxValue;   // corpo sem dono: o funil nao tem o que fazer com ele
		return true;
	}

	/// <summary>REVIVE pelo caminho do verbo de admin (`Reviver` + o corpo inteiro de volta).</summary>
	internal bool ReviverNoVelorioDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		pl.Combate.Reviver();
		pl.RelogioDaMorte = 0;
		pl.Combate.SincronizarVida();
		MandarFicha(pl);
		return !pl.Ficha.dead;
	}

	/// <summary>
	/// LEVA UM CORPO PRA UMA ZONA. E o `MoveToZone` de producao, o mesmo da passagem e da nave.
	///
	/// A bancada usa isto pra por o corpo VIVO de controle ao lado do morto DENTRO do Outro Mundo --
	/// que e a unica encenacao que separa "a auréola e da morte" de "a auréola e do lugar". Sem um
	/// vivo la dentro, um `TemAureola` reescrito como `EhOAlem(zona)` passaria verde em tudo.
	/// </summary>
	internal bool LevarProAlemNoVelorioDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		MoveToZone(pl.Id, alem, MesaDoEnma(alem));
		return Alem.EhOAlem(pl.Zone);
	}

	// =====================================================================
	// AS TRES BORDAS
	// =====================================================================
	/// <summary>
	/// MERGULHA NA MENTE -- pela porta de producao (<see cref="EntrarNaMente"/>), com uma unica
	/// condicao POSADA: `Ficha.med`.
	///
	/// Meditar de verdade custa a tecla M, o tempo do transe e um estado que a bancada nao esta
	/// medindo; a porta, essa, e exercitada inteira -- inclusive o `LargarOCorpo`, que e quem deixa o
	/// BONECO pra tras (e o boneco e um dos corpos que a triagem tem que recusar).
	/// </summary>
	internal bool MergulharNaMenteNoVelorioDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		pl.Ficha.med = true;
		EntrarNaMente(pl);
		return DimensaoMental.EhAMente(pl.Zone);
	}

	/// <summary>
	/// PRENDE NA SALA DO TEMPO -- o bit de prisao, que e o que a morte tem que abrir.
	///
	/// SO O BIT E POSTO A MAO; quem o apaga continua sendo o `AMorteSaiDaSala` chamado de dentro do
	/// `IrProAlem`. A sala inteira (a porta, a sessao, a comida) tem bancada propria -- `--salateste`;
	/// o que ESTA cobra e uma coisa so, e e a que o dono escolheu: *"depois de preso, so morrendo pra
	/// sair"*. Se a morte deixasse o bit aceso, o sujeito voltaria a vida marcado como preso pra
	/// sempre e a porta o recusaria pelo resto da vida do personagem.
	/// </summary>
	internal bool PrenderNaSalaNoVelorioDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		pl.SalaPreso = true;
		return pl.SalaPreso;
	}

	/// <summary>
	/// PoE O CORPO NUMA PONTE DE NAVE -- uma zona DINAMICA (`Interior("Nave", n)`), que e o que a
	/// borda tem de diferente: ela nao esta no catalogo de mapas, ela e criada em runtime e pode
	/// deixar de existir com a nave.
	///
	/// A pergunta da bancada e curta: morrer la dentro tira o corpo de la? E o revive nao o devolve
	/// pra dentro de uma zona que talvez nao exista mais?
	/// </summary>
	internal bool PorNaPonteNoVelorioDeTeste(int id, int naveFicticia)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		ZoneKey ponte = NaveGrande.ZonaDoInterior(naveFicticia);
		// A CELULA DE CHEGADA da planta, e nao a do console: aquela e PAREDE (`NaveGrande.cs:292`
		// levanta o computador em cima dela), e um corpo posto dentro de parede e a segunda maneira
		// de prender alguem -- que e justamente o que esta borda existe pra medir.
		MoveToZone(pl.Id, ponte, NaveGrande.PixelDe(NaveGrande.CelDaChegada));
		return NaveGrande.EhInterior(pl.Zone, out _);
	}

	// =====================================================================
	// DESMONTAR
	// =====================================================================
	/// <summary>
	/// TIRA OS CORPOS FORJADOS DO MUNDO. O avesso do <see cref="ForjarNoVelorioDeTeste"/>.
	///
	/// O `Peer` emprestado ao boneco e devolvido ANTES de qualquer outra coisa: um corpo de bancada
	/// segurando o `Peer` do host e o unico estrago desta bancada que sairia dela viva.
	/// </summary>
	internal void DesmontarOVelorioDeTeste()
	{
		foreach (ServerPlayer c in _corposDoVelorio)
		{
			c.Peer = null;
			_npcsPraTirar.Remove(c);
			_players.Remove(c.Id);
			ZoneList(c.Zone.Hash).Remove(c);
		}
		_corposDoVelorio.Clear();
		GD.Print("[velorio] corpos de bancada desmontados");
	}
}
