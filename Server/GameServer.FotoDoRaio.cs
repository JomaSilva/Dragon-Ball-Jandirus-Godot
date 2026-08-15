using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// AS JANELAS DA BANCADA DE FOTO (`--diagraio`, `Client/RoboDeFotoDoRaio.cs`).
///
/// ============================ POR QUE UMA BANCADA DE FOTO PRECISA DE JANELA NO SERVIDOR ============================
/// Os tres pedidos do dono sao VISUAIS ou de SENSACAO -- "npcs se movendo enquanto transformam", "um
/// rastro no chao igual o do knock back", "empurrar a pessoa junto". As bancadas sem janela
/// (`--projetilteste`, `--iateste`, `--diagdecalque`) ja medem todas as tres em NUMERO e em BYTE, e
/// esta nao repete nada disso: ela existe pra a metade que nenhum campo responde -- **o desenho**.
///
/// E pra fotografar o desenho, as tres cenas tem que ACONTECER, no jogo, com o cliente ligado. Nao
/// da pra o cliente forjar um NPC nem disparar um raio: quem tem corpo e projetil e o servidor. Entao
/// as tres cenas nascem aqui, e sempre pela porta de producao:
///
///   * o corpo, pelo `PorNoMundo` -- o mesmo de todo corpo sem dono;
///   * a transformacao, pelo `Transformar` -- o mesmo funil do jogador e da IA;
///   * o passo, pelo `AplicarComando` -- o MESMO atuador por onde o plano do cerebro sai;
///   * o tiro, pelo `Disparar` -- a porta unica dos projeteis.
///
/// Nenhuma escreve `Pos`, `CenaSegundos` ou `ArrastoRestante`. Se alguma escrevesse, a foto mostraria
/// a bancada e nao o jogo.
/// ==============================================================================================================
///
/// ============================ O PASSO E EMPURRADO DE FORA, E ISSO E DELIBERADO ============================
/// O corpo da foto (a) nasce SEM CEREBRO e recebe, a cada quadro, o MESMO comando de andar
/// (<see cref="EmpurrarCorpoDeFoto"/>). Podia ter cerebro e vagar sozinho -- e ai a foto teria um
/// problema calado: **um NPC parado na foto nao prova nada se ele nao estava tentando andar**. Um
/// plano que decidiu ficar quieto, um alvo que sumiu, Ki no fim: qualquer um deles daria a mesma
/// foto, com o conserto do dono desligado.
///
/// Com o comando empurrado de fora a intencao e garantida e a foto passa a significar uma coisa so.
/// E o comando nao e um atalho: `AplicarComando` e o atuador de producao -- e por ele que o `Rumo`
/// que o `Cerebro.Pensar` devolve vira passo, e e nele que mora o `PodeMexerOCorpo` que este pedido
/// consertou.
/// =======================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Faixa de ids desta bancada -- longe do `_nextId` e das outras (90.100, 90.400, 90.800).</summary>
	private const int IdBaseDaFoto = 91_200;

	private int _fotoCorpos;

	/// <summary>Os corpos que esta bancada pos no mundo, pra o <see cref="LimparAFoto"/> recolher.</summary>
	private readonly List<int> _fotoNascidos = [];

	/// <summary>O raio que ela plantou (0 = nenhum).</summary>
	private int _fotoRaio;

	/// <summary>
	/// UM CORPO AO LADO DO JOGADOR -- na zona DELE, no deslocamento pedido, e no mundo de verdade.
	///
	/// `comEscada` abre a escada de formas com maestria ZERO, que e o unico jeito de haver
	/// CINEMATICA: `Cinematicas.Degrau` dispensa a cena a partir de 50% de maestria, e um corpo de
	/// bancada com 100% nunca teria mostrado o defeito que o dono viu. Os moldes de verdade
	/// (`npcs.json`) vao de 0 a 100 e o povoamento nasce com o `default` 0 -- quem andava enquanto
	/// transformava e a metade DE BAIXO da tabela.
	/// </summary>
	/// <returns>o id do corpo, ou 0 se o dono nao esta em jogo.</returns>
	internal int ForjarCorpoDeFoto(int idDono, Vec2 desloc, string nome, double bp, bool comEscada)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono)) return 0;

		var f = new Fighter
		{
			Name = nome, Race = "Saiyan", BP = bp,
			physoff = 5, physdef = 5, technique = 5, kioff = 5, kidef = 5,
			kiskill = 5, speed = 5, magiskill = 5, Idade = 25,
			maxstamina = 100, stamina = 100,
		};
		f.Statify();

		// O `expressedBP` NAO SAI DO `Statify` -- ele sai do `PowerLevel`, que num corpo de verdade
		// roda no `TickFichas`. Sem esta linha o corpo nasce com poder EXPRESSO zero, e o
		// `BpModulus` da cadeia de ki (que e uma RAZAO entre poderes) devolveria o mesmo numero pra
		// todo mundo. E a mesma pegadinha que a bancada do sol e a da IA ja pagaram.
		f.PowerLevel();
		f.Ki = f.MaxKi;
		f.Class = "Saiyan";

		var c = new ServerPlayer
		{
			Id = IdBaseDaFoto + _fotoCorpos++,
			Peer = null,
			Name = nome,
			Race = "Saiyan",
			Genero = "Male",
			Idade = 25,
			Zone = dono.Zone,
			Pos = dono.Pos + desloc,
			Conta = "bancada_foto",
			Slot = 0,
			LastInputMs = NowMs(),
			Ficha = f,
			Livro = new Jandirus.Core.Skills.SkillBook(),
			Cerebro = null,   // ver o cabecalho: quem quer andar aqui e o `EmpurrarCorpoDeFoto`

			// ============================ SEM ISTO A FOTO NAO TEM NINGUEM DENTRO ============================
			// A primeira rodada desta bancada saiu com o rastro de posicao certo -- um ponto so durante a
			// cinematica, uma fileira antes e depois -- e **nenhum corpo desenhado nele**: um corpo
			// forjado sem `Visual` nao tem sprite nenhum no cliente, e a foto mostrava um marcador
			// flutuando sobre a areia. As checagens todas ficaram verdes, porque elas leem a posicao
			// DESENHADA (que existe) e nao o pixel.
			//
			// A aparencia vem do MESMO `AparenciaDeNpc` que veste todo habitante de planeta -- corpo,
			// tom, cabelo e roupa sorteados do catalogo. Nao ha aparencia de bancada.
			// ============================================================================================
			Visual = AparenciaDeNpc(
				new MoldeDeNpc { Id = "bancada_foto", Racas = ["Saiyan"], Classe = "Saiyan" },
				"Saiyan", "Male", (ulong)(IdBaseDaFoto + _fotoCorpos)),
		};
		PorNoMundo(c);
		c.Ficha.Tick(agoraMs: NowMs());

		// ============================ E A APARENCIA PRECISA SER APRESENTADA ============================
		// `PorNoMundo` poe o corpo na zona; quem conta pras telas QUEM ele e e o `TrocarAparencias`, num
		// pacote proprio -- a aparencia nao viaja no snapshot (uma ficha de aparencia por quadro por
		// corpo gastaria a banda inteira repetindo o que nao muda).
		//
		// A segunda rodada desta bancada saiu sem isto: o `Visual` estava montado, o `Sanear` nao
		// reclamou, o node do corpo NASCEU na tela (a checagem do `CorpoDeTeste` ficou verde) -- e a
		// foto mostrava um tufo de cabelo no chao, sem corpo e sem roupa. Todo NPC de producao passa
		// por esta linha (`GameServer.Npc.cs:277`); o corpo da foto tambem passa.
		// ==========================================================================================
		TrocarAparencias(c);

		if (comEscada)
			SorteioDeNpc.AbrirFormas(
				c.Forma,
				new MoldeDeNpc { Id = "bancada_foto", Racas = ["Saiyan"], Classe = "Saiyan", EscadaAutomatica = true, Maestria = 0 },
				c.Ficha.BP, Perfil(c));

		_fotoNascidos.Add(c.Id);
		return c.Id;
	}

	/// <summary>
	/// O CORPO SOBE DE FORMA PELO FUNIL DE PRODUCAO. Devolve os segundos de cena que ele ganhou --
	/// zero quer dizer "esta forma nao tem cinematica", que e uma resposta e nao um erro.
	/// </summary>
	internal double TransformarCorpoDeFoto(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		Transformar(pl, subir: true);
		return pl.CenaSegundos;
	}

	/// <summary>
	/// O MESMO COMANDO DE ANDAR, MAIS UMA VEZ -- ver o cabecalho. Devolve se o funil ACEITOU o passo,
	/// que e a leitura que a foto acompanha.
	/// </summary>
	internal bool EmpurrarCorpoDeFoto(int id, Vec2 rumo, double dt)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		bool podia = PodeMexerOCorpo(pl);
		AplicarComando(pl, new Comando { Rumo = rumo }, dt);
		return podia;
	}

	/// <summary>
	/// O ESTADO QUE A FOTO PRECISA LER: quanto falta da cena, se o funil de vetor aceita o passo, a
	/// forma, e o PORQUE de nao aceitar quando nao aceita.
	///
	/// O motivo sai junto porque uma foto de corpo parado nao diz por que ele parou -- e a primeira
	/// rodada desta bancada reprovou exatamente ai: o corpo continuava plantado DEPOIS da cinematica,
	/// e sem esta leitura a bancada nao sabia dizer se o portao tinha vazado ou se o corpo tinha
	/// desmaiado de Ki (era a segunda -- ver `RegarOKiDeFoto`).
	/// </summary>
	internal (double Cena, bool PodeAndar, string Forma, string Motivo) CenaDeFoto(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return (0, false, "", "corpo fora do mundo");

		string motivo = pl.Ficha.dead ? "morto"
					  : pl.Ficha.KO ? "nocauteado"
					  : EmCena(pl) ? "em cinematica"
					  : pl.Carregando ? "carregando"
					  : _emEmbate.ContainsKey(pl.Id) ? "em embate"
					  : _emEmbateDeKi.ContainsKey(pl.Id) ? "em embate de ki"
					  : EnraizadoPorKi(pl.Id) ? "preso por canal de ki"
					  : Paralisado(pl.Id) ? "paralisado"
					  : pl.ArrastoRestante > 0 ? "levado por um feixe"
					  : PodeMexerOCorpo(pl) ? "" : "outra recusa do funil";

		return (pl.CenaSegundos, PodeMexerOCorpo(pl), pl.Forma.Atual, motivo);
	}

	/// <summary>
	/// O TANQUE CHEIO DE NOVO -- e ela existe porque o DRENO DA FORMA nao e o assunto de nenhuma das
	/// tres fotos.
	///
	/// Um SSJ de maestria zero (a unica maestria que tem cinematica) queima Ki depressa, e a cena do
	/// SSJ1 prende o corpo por 10 s. Sem regar, o corpo desmaia no meio da espera e a foto de DEPOIS
	/// mostraria um NPC parado por NOCAUTE -- verde pra o defeito errado, e vermelha pro certo. O
	/// `--iateste` faz a mesma coisa na familia 7b, com a mesma frase.
	///
	/// Repare no que ela NAO faz: nao mexe em `CenaSegundos`, em `Pos` nem em recusa nenhuma. O que a
	/// foto mede continua vindo inteiro do jogo.
	/// </summary>
	internal void RegarOKiDeFoto(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Ficha.Ki = pl.Ficha.MaxKi;
	}

	/// <summary>
	/// UM RAIO DE VERDADE SAINDO DA MAO DO JOGADOR, pela porta unica (`Disparar`).
	///
	/// `Canalizando` na unha pelo mesmo motivo da `--projetilteste`: um beam nascido pelo `Disparar`
	/// tem cauda EM CIMA da cabeca, e sem o bit o passo 3(c) do `AndarProjetil` o mata de `Cessou` no
	/// primeiro tique -- antes de ele riscar chao nenhum. `Deflectivel = false` porque um sorteio no
	/// meio de uma foto fotografaria o dado.
	///
	/// O RESTO E O JOGO: quem carimba o sulco e o `MarcarSulcoDoTiro` dentro do laco de sub-passos,
	/// quem abre a onda e o cliente ao ver o tiro cruzar agua, e quem empurra quem apanha e o
	/// `ArrastarComOFeixe`. Esta janela nao chama nenhum dos tres.
	/// </summary>
	internal int RaioDeFoto(int idDono, Vec2 rumo, double alcanceTiles, double baseDano)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono)) return 0;

		dono.Ficha.Ki = dono.Ficha.MaxKi;
		Projetil p = Disparar(dono, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam, BaseDano = baseDano, Velocidade = 1,
			AlcanceTiles = alcanceTiles, Deflectivel = false, Nome = "Onda de Ki",
		}, rumoDado: rumo);
		p.Canalizando = true;

		_fotoRaio = p.Id;
		return p.Id;
	}

	/// <summary>
	/// O QUE O RAIO DA FOTO ESTA FAZENDO: onde a cabeca esta, quem ela leva e quantos tiles andou.
	/// So leitura -- a bancada compara isso com o que a TELA mostrou.
	/// </summary>
	internal (bool Vivo, Vec2 Cabeca, int Arrastando, double AndouTiles) RaioDaFoto(int idDono)
	{
		if (_fotoRaio == 0 || !_players.TryGetValue(idDono, out ServerPlayer? dono))
			return (false, default, 0, 0);

		foreach (Projetil p in ProjeteisDaZona(dono.Zone.Hash))
			if (p.Id == _fotoRaio)
				return (p.Vivo, p.Pos, p.Arrastando, p.AndouTiles);

		return (false, default, 0, 0);
	}

	/// <summary>Onde este corpo esta, na conta do SERVIDOR -- pra a foto conferir com o que desenhou.</summary>
	internal Vec2? PosDeFoto(int id)
		=> _players.TryGetValue(id, out ServerPlayer? pl) ? pl.Pos : null;

	/// <summary>
	/// A CELULA DEBAIXO DESTE PONTO E AGUA? -- a pergunta feita ao mapa do SERVIDOR.
	///
	/// A bancada do cliente tem a dela (`World.EhAgua`, que le o tilemap desenhado), e as duas
	/// existem: quem escolhe onde o raio sai e o servidor, e mandar o tiro pra um lago que so o
	/// cliente conhece daria uma foto sem onda por motivo nenhum.
	/// </summary>
	internal bool EhAguaDeFoto(int idDono, Vec2 onde)
		=> _players.TryGetValue(idDono, out ServerPlayer? dono)
		   && MapaDaZonaOuCatalogo(dono.Zone) is { } mapa
		   && mapa.EhAguaEm(onde);

	/// <summary>Tira do mundo tudo que esta bancada pos nele.</summary>
	internal void LimparAFoto()
	{
		foreach (int id in _fotoNascidos)
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) continue;
			_players.Remove(id);
			ZoneList(pl.Zone.Hash).Remove(pl);
		}
		_fotoNascidos.Clear();
		_fotoRaio = 0;
	}
}
