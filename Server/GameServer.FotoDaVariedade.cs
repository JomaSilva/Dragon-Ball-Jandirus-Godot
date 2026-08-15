using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS JANELAS DA BANCADA QUE FOTOGRAFA A VARIEDADE (`--diagvariedade`, `Client/RoboDeVariedadeDeKi.cs`).
///
/// ============================ POR QUE ELAS EXISTEM, SE A `--diagartedeki` JA E VERDE ============================
/// A `--diagartedeki` monta os `ProjetilDesenhado` COM A MAO e chama `Vestir` -- ela prova que a folha
/// carrega e que o pixel muda, e nada disso e pouco. Mas ela nunca atira: entre a tabela e a tela ha um
/// caminho inteiro que ela pula (o verb do jogador, o gate da skill, o `Canalizar`, o `Disparar`, o
/// `ArteDeProjetil.De` com a RACA e a SEMENTE do corpo de verdade, o `ushort` do anuncio de nascimento,
/// o `AoNascerTiro` do cliente). Um so daqueles elos escrito e nao ligado da o defeito que o dono
/// relatou -- todos os ataques com a mesma cara -- com a bancada de arte inteirinha verde.
///
/// Entao aqui nada e forjado. Cada tiro sai pelo verb (`UsarHabilidade`, o mesmo que o `C2S.Habilidade`
/// aciona), e o que a foto pega e o node que o pacote de nascimento criou.
/// ==========================================================================================================
///
/// ============================ O QUE ESTAS JANELAS **NAO** FAZEM ============================
/// Nao escrevem `Arte` em projetil nenhum, nao chamam `Disparar` e nao tocam no `ArteDeProjetil`. Se
/// escrevessem, a foto mostraria a bancada. As unicas coisas que elas escrevem sao as que um JOGADOR
/// tambem escreveria antes de atirar: o que ele aprendeu (livro e niveis), quanto Ki ele tem, e pra
/// onde ele esta olhando.
/// ======================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O CORPO ARMADO PRA ATIRAR TUDO -- livro cheio, degraus cruzados e tanque grande.
	///
	/// O livro leva o CATALOGO INTEIRO (o mesmo `_skills.Todas` da bancada da IA) e os niveis sobem
	/// por `DoSave`, que e o caminho do LOGIN. Os dois sao precisos e nenhum e suficiente: metade dos
	/// verbos de tiro vem de skill comprada e a outra metade de degrau de nivel (`Basic_Blast` no 35
	/// de `Ki_Unlocked`, `Guided_Ball` no 30 de `Basic_Ki_Control`) -- ver o bloco do `SabeTecnica`.
	///
	/// O `beamskill` alto e deliberado: os raios nomeados trocam de RECEITA por degrau de pericia
	/// (`DegrauDoRaioG6`), e um corpo cru sairia sempre no degrau de baixo. A foto tem que mostrar a
	/// tecnica que um jogador de verdade usa.
	/// </summary>
	/// <returns>quantas skills entraram no livro e quantos verbos o corpo passou a reconhecer.</returns>
	internal (int Skills, int Verbos) ArmarParaAVariedade(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return (0, 0);

		int skills = 0;
		var save = new NivelSave();
		if (_skills != null)
			foreach (Skill s in _skills.Todas)
			{
				// ============================ `DarComoEnsinada`, E ISTO CUSTOU UMA RODADA INTEIRA ============================
				// A primeira versao usava `Dar`, e OITO tecnicas respondiam "voce nao sabe" -- Kamehameha,
				// Final Flash, Galick Ho, Death Beam, Dodon Ray, Kikoho, Kill Driver, Buster Shell. Todas
				// de `/datum/skill/rank/...`, e todas some UM TIQUE depois de entrarem no livro.
				//
				// Quem as tira e o `ReconciliarDadiva` (`GameServer.CargoPortas.cs:158`): o que SO um cargo
				// poderia ter dado e que ninguem ENSINOU e revogado -- `DadivaDeCargo.Revogavel` e
				// literalmente `soDeCargo.Contains(path) && !livro.FoiEnsinada(path)`. Uma bancada que da a
				// skila na mao esta, pra aquele codigo, forjando um kit de cargo que o corpo nao tem.
				//
				// `DarComoEnsinada` e o caminho do MESTRE (`teachable.dm:53-54`), e e o unico jeito honesto
				// de um corpo sem cargo ter a tecnica: alguem lhe ensinou.
				// ========================================================================================================
				pl.Livro.DarComoEnsinada(s.Path);
				// O DEGRAU MAXIMO EM TUDO. `VerbosAtivos()` e quem responde pelos verbos concedidos
				// por nivel, e ela le exatamente esta tabela.
				save.Skills[s.Path] = [100, 0];
				skills++;
			}
		pl.Niveis.DoSave(save);

		pl.Ficha.beamskill = Math.Max(pl.Ficha.beamskill, 75);
		pl.Ficha.blastskill = Math.Max(pl.Ficha.blastskill, 75);
		pl.Ficha.guidedskill = Math.Max(pl.Ficha.guidedskill, 75);
		pl.Ficha.kidebuffskill = Math.Max(pl.Ficha.kidebuffskill, 75);

		// ============================ O TANQUE NAO SE ENCHE ESCREVENDO `MaxKi` ============================
		// A primeira versao escrevia `MaxKi = 50.000.000` e MESMO ASSIM a Esfera Teleguiada e a Paralysis
		// respondiam "isso pede pelo menos 3673 de energia". `MaxKi` e DERIVADO
		// (`Fighter.Statify.cs:122`) e o proximo tique o reescreve -- escrever nele e escrever na areia.
		//
		// O corpo e fortalecido pelo caminho de que ele sai: STATS, e depois `Statify`. E o
		// `kiefficiencyskill` e a peca que faltava -- ele DIVIDE o `BaseDrain` (`sqrt(MaxKi/140)/log_9`),
		// e sem ele nenhum Ki maximo alcanca as duas tecnicas caras: o custo cresce com a RAIZ do
		// tanque, entao encher o tanque nao paga a conta sozinho.
		// ============================================================================================
		pl.Ficha.physoff = Math.Max(pl.Ficha.physoff, 400);
		pl.Ficha.physdef = Math.Max(pl.Ficha.physdef, 400);
		pl.Ficha.technique = Math.Max(pl.Ficha.technique, 400);
		pl.Ficha.kioff = Math.Max(pl.Ficha.kioff, 400);
		pl.Ficha.kidef = Math.Max(pl.Ficha.kidef, 400);
		pl.Ficha.kiskill = Math.Max(pl.Ficha.kiskill, 400);
		pl.Ficha.speed = Math.Max(pl.Ficha.speed, 400);
		pl.Ficha.kiefficiencyskill = Math.Max(pl.Ficha.kiefficiencyskill, 100);
		pl.Ficha.Statify();

		// ============================ O TANQUE E O `baseKi`, E ELE TEM TETO PROPRIO ============================
		// `MaxKi = baseKi * KiMod * kiAmp * ...` (`Statify:122`), e `baseKi` NAO e derivado: ele so sobe
		// TREINANDO, ate o `baseKiMax` que o BP permite (`Statify:155`). Um corpo de bancada com BP
		// enorme e sem uma hora de treino continua com o `baseKi` de recem-nascido -- foi por isso que a
		// segunda rodada ainda ouviu "isso pede pelo menos 288 de energia" com MaxKi de 141.
		//
		// Enche-se ate o TETO DO PROPRIO BP, que e onde o treino chegaria: nao e um numero inventado, e
		// o mesmo `baseKiMax` que o `Fighter.Training` persegue.
		// ==================================================================================================
		pl.Ficha.baseKi = Math.Max(pl.Ficha.baseKi, pl.Ficha.baseKiMax);
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();

		// O FOLEGO tem o teto dele em outro lugar: `maxstamina = min(200*|Estaminamod|, maxstamina)`
		// (`Statify:40`) -- a linha so APERTA, entao o maximo alcancavel e exatamente aquele produto.
		pl.Ficha.maxstamina = Math.Max(pl.Ficha.maxstamina, 200 * Math.Abs(pl.Ficha.Estaminamod));
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Ficha.stamina = pl.Ficha.maxstamina;

		// A ANCORA: onde este corpo tem que estar em TODA foto. Ver `ReporNaAncora`.
		_ancoraDaVariedade = pl.Pos;

		return (skills, TecnicasDe(pl).Count);
	}

	/// <summary>Onde o corpo da bancada tem que estar em toda foto. Ver <see cref="ReporNaAncora"/>.</summary>
	private Vec2 _ancoraDaVariedade;

	/// <summary>
	/// O CORPO VOLTA PRO MESMO PONTO ANTES DE CADA TIRO -- e isto NAO e zelo, e o que faz a comparacao
	/// existir.
	///
	/// A primeira rodada desta bancada saiu com a foto de CONTROLE mostrando outro pedaco de terreno: o
	/// corpo tinha andado alguns pixels ao longo dos cinco minutos (empurrao de NPC, o proprio recuo do
	/// tiro), e como a foto do fundo e tirada UMA vez no comeco, "98% dos pixels mudaram" passou a ser a
	/// resposta pra qualquer coisa. Com o cenario deslizando debaixo do recorte, TODO par de tecnicas
	/// fica diferente -- e a bancada ficaria verde por acaso, medindo grama.
	/// </summary>
	internal void ReporNaAncora(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Pos = _ancoraDaVariedade;
		pl.Moving = false;
	}

	/// <summary>O estado que a bancada espera antes de comecar: nocaute, morte e os dois tanques.</summary>
	internal (bool KO, bool Morto, double Ki, double MaxKi, double Folego) EstadoDaVariedade(int id)
		=> _players.TryGetValue(id, out ServerPlayer? pl)
			? (pl.Ficha.KO, pl.Ficha.dead, pl.Ficha.Ki, pl.Ficha.MaxKi, pl.Ficha.stamina)
			: (true, true, 0, 0, 0);

	/// <summary>
	/// UM ALVO MARCADO, LONGE -- o que o duplo clique faz (`ServerPlayer.AlvoId`).
	///
	/// Tres tecnicas do roteiro EXIGEM alvo (`Scattering_Bullet` recusa sem ele; a Esfera Teleguiada e o
	/// Kienzan viram teleguiados quando ha um). O corpo nasce a `tiles` de distancia NO RUMO DO TIRO --
	/// bem alem do recorte, que pega quatro tiles: ele nao aparece em foto nenhuma, e o tiro continua
	/// indo pro mesmo lugar em todas.
	/// </summary>
	internal int MarcarAlvoDaVariedade(int dono, Facing f, int tiles)
	{
		if (!_players.TryGetValue(dono, out ServerPlayer? pl)) return 0;

		Vec2 d = MeleeArea.Frente(f);
		int alvo = ForjarCorpoDeFoto(
			dono, new Vec2(d.X * tiles * ZoneCollision.TileSize, d.Y * tiles * ZoneCollision.TileSize),
			"Foto: o alvo marcado", 200_000, comEscada: false);
		if (alvo != 0) pl.AlvoId = alvo;
		return alvo;
	}

	/// <summary>
	/// MEIO-DIA DE NOVO, ANTES DE CADA FOTO -- e isto foi medido, nao previsto.
	///
	/// ============================ UM DIA DO JOGO DURA 24 MINUTOS ============================
	/// A quarta rodada desta bancada leva uns seis: o sol anda um QUARTO do dia entre a primeira
	/// tecnica e a ultima. As duas fotos do CONTROLE (o mesmo Ki Wave, o mesmo lugar, o mesmo
	/// instante geometrico) sairam com a grama visivelmente mais clara na segunda -- e como a
	/// mascara do tiro e "o que difere do fundo", menos contraste virou menos pixel de tiro: 3.324
	/// px na primeira contra 1.480 na segunda, do MESMO desenho. O chao do ruido subiu pra 3.388 e
	/// engoliu metade da tabela.
	///
	/// `AdminMeioDia` e a conta de producao (`GameServer.Ceu.cs:135`), a mesma que o verb de admin
	/// usa, e ela resolve em vez de varrer. Cravada a cada tiro, a luz e identica em todas as fotos
	/// -- e ai "menos pixel de tiro" volta a significar "tiro menor".
	/// ==================================================================================
	/// </summary>
	internal void CravarMeioDiaDaVariedade(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) AdminMeioDia(pl);
	}

	/// <summary>PRA ONDE ELE OLHA. E o que um jogador faz com as setas antes de atirar.</summary>
	internal void ApontarParaAVariedade(int id, Facing f)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Facing = f;
	}

	/// <summary>
	/// OS DOIS TANQUES CHEIOS de novo entre um tiro e o outro -- ver <see cref="RegarOKiDeFoto"/>.
	///
	/// O FOLEGO ENTRA JUNTO porque o Spirit Gun cobra estamina e nao Ki (`isso pede 147,5 de folego`),
	/// e uma bancada que enche so metade fotografa a recusa da outra.
	/// </summary>
	internal void RegarOKiDaVariedade(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.baseKi = Math.Max(pl.Ficha.baseKi, pl.Ficha.baseKiMax);
		pl.Ficha.maxstamina = Math.Max(pl.Ficha.maxstamina, 200 * Math.Abs(pl.Ficha.Estaminamod));
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Ficha.stamina = pl.Ficha.maxstamina;

		// O SANGUE tambem: o Kikoho cobra VIDA (`o Kikoho cobra 2 de sangue seu`), e vinte disparos
		// numa rodada matariam o corpo no meio da sessao de fotos.
		pl.Combate?.Corpo.Restaurar();
		pl.Combate?.SincronizarVida();
	}

	/// <summary>
	/// APERTA O BOTAO -- e e literalmente o botao: <see cref="UsarHabilidade"/>, a funcao que o
	/// `case Protocol.C2S.Habilidade` chama quando um jogador clica na tecnica.
	///
	/// Devolve o que o servidor RESPONDEU (os `Avisar` daquela chamada, colados). Vazio quer dizer
	/// "saiu calado", que e o normal de um tiro aceito. A resposta sai junto porque uma foto sem tiro
	/// nao diz por que nao houve tiro -- e a lista tem tecnica cara, tecnica com recarga e tecnica
	/// que pede alvo.
	/// </summary>
	internal string GatilhoDaVariedade(int id, string verbo)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return "corpo fora do mundo";

		List<string>? antes = EscutaDeAvisos;
		EscutaDeAvisos = [];
		try
		{
			UsarHabilidade(pl, verbo);
			return string.Join(" | ", EscutaDeAvisos);
		}
		finally { EscutaDeAvisos = antes; }
	}

	/// <summary>
	/// O TIRO MAIS NOVO DESTE DONO, lido da lista de projeteis da zona.
	///
	/// A `Arte` daqui e a do SERVIDOR. Ela existe pra a bancada poder cruzar com a que o CLIENTE
	/// vestiu (`World.TirosDesenhados`) -- as duas tem que bater, e e essa travessia que prova que o
	/// `ushort` do anuncio de nascimento carrega o que deve.
	/// </summary>
	internal (bool Achou, int IdDoTiro, ArteDeKi Arte, TipoDeProjetil Tipo,
			  Vec2 Pos, double AndouTiles, float Escala) TiroDaVariedade(int dono)
	{
		if (!_players.TryGetValue(dono, out ServerPlayer? pl)) return (false, 0, default, default, default, 0, 1);

		Projetil? novo = null;
		foreach (Projetil p in ProjeteisDaZona(pl.Zone.Hash))
			if (p.Dono == dono && p.Vivo && (novo == null || p.Id > novo.Id)) novo = p;

		return novo == null
			? (false, 0, default, default, default, 0, 1)
			: (true, novo.Id, novo.Arte, novo.Tipo, novo.Pos, novo.AndouTiles, (float)novo.EscalaVisual);
	}

	/// <summary>
	/// LIMPA A MESA ENTRE UM TIRO E O OUTRO: fecha o canal (se havia raio de pe) e apaga os tiros
	/// soltos deste dono pelo mesmo caminho que a saida do jogo usa.
	/// </summary>
	internal void LimparOsTirosDaVariedade(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		if (_canais.TryGetValue(id, out CanalDeKi? c)) FecharCanal(id, c, null);
		LimparProjeteisDeUmDono(id, pl.Zone.Hash);
	}

	/// <summary>
	/// A UNICA ESCOLHA DE ARTE QUE O JOGADOR FAZ NO ORIGINAL -- a tecnica inventada
	/// (`pick_game_icon`, `customattacks.dm:543-568`).
	///
	/// E ela e feita AQUI PELO MESMO CAMINHO DA TELA: `ca_criar`, `ca_tipo`, `ca_texto`, `ca_arte` e
	/// `ca_salvar` sao os cinco comandos que os botoes do painel mandam
	/// (<see cref="ComandoDeTecnicaCustomizada"/>, o ramo do `GameServer.Verbos.cs:97`). Nenhum campo
	/// e escrito na unha -- inclusive o recorte por tipo, que e do servidor e recusaria uma folha de
	/// raio pendurada numa bola.
	/// </summary>
	/// <returns>o verbo (`Custom_Attack&lt;n&gt;`), ou vazio se a mesa recusou.</returns>
	internal string InventarTecnicaComArte(int id, TipoDeProjetil tipo, ArteDeKi arte, string nome,
										   out string relato)
	{
		relato = "";
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return "";

		List<string>? antes = EscutaDeAvisos;
		EscutaDeAvisos = [];
		try
		{
			ComandoDeTecnicaCustomizada(pl, "ca_criar", "");
			if (pl.Mesa is not { } m) { relato = string.Join(" | ", EscutaDeAvisos); return ""; }

			// O TIPO SO E TOCADO SE FOR PRA MUDAR. `CriarTecnica` ja abre a mesa em Beam
			// (`pl.Mesa.PorTipo(Beam)`), e sair do Beam OBRIGA a desfazer os modificadores de raio --
			// uma recusa legitima do proprio jogo (`TipoDaTecnica`), que numa bancada vira so ruido.
			if (tipo != TipoDeProjetil.Beam)
				ComandoDeTecnicaCustomizada(pl, "ca_tipo",
					tipo == TipoDeProjetil.Guided ? "guided" : "blast");

			ComandoDeTecnicaCustomizada(pl, "ca_texto", "nome/" + nome);
			ComandoDeTecnicaCustomizada(pl, "ca_arte", ((int)arte).ToString());

			// O QUE A MESA ACEITOU, lido ANTES do salvar: se o recorte por tipo tivesse recusado a
			// folha, a bancada precisa saber disso aqui e nao adivinhar pela foto.
			ArteDeKi ficou = m.Arte;
			int slot = m.Id;
			ComandoDeTecnicaCustomizada(pl, "ca_salvar", "");

			TecnicaCustomizada? pronta = pl.Customizadas.Find(t => t.Id == slot && t.Criada);
			relato = string.Join(" | ", EscutaDeAvisos)
				   + $" [mesa ficou com {ficou}, pediram {arte}, pronta={(pronta == null ? "nao" : pronta.Arte.ToString())}]";

			return pronta != null && pronta.Arte == arte ? pronta.Verbo : "";
		}
		finally { EscutaDeAvisos = antes; }
	}

	/// <summary>
	/// O JOGADOR MUDA DE IDEIA -- a MESMA tecnica, outra folha. `ca_editar` abre uma copia da tecnica
	/// de pe, `ca_arte` troca a escolha e `ca_salvar` confirma; e o caminho do painel, botao por
	/// botao.
	///
	/// Existe pra a foto poder responder o pedido do dono no ponto exato em que ha ESCOLHA: duas
	/// fotos do MESMO verbo, com duas artes que o jogador escolheu. Duas tecnicas diferentes
	/// mostrariam duas tecnicas diferentes, que e outra coisa.
	/// </summary>
	internal bool TrocarArteDaInventada(int id, int slot, ArteDeKi arte)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;

		ComandoDeTecnicaCustomizada(pl, "ca_editar", slot.ToString());
		ComandoDeTecnicaCustomizada(pl, "ca_arte", ((int)arte).ToString());
		ComandoDeTecnicaCustomizada(pl, "ca_salvar", "");

		return pl.Customizadas.Find(t => t.Id == slot) is { } t2 && t2.Arte == arte;
	}

	/// <summary>As tecnicas inventadas saem da cabeca -- a mesa nao pode sobrar pro proximo login.</summary>
	internal void EsquecerAsInventadasDaVariedade(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		foreach (TecnicaCustomizada t in pl.Customizadas.ToList())
			ComandoDeTecnicaCustomizada(pl, "ca_esquecer", t.Id.ToString());
	}

	/// <summary>
	/// UM RUMO EM QUE O TIRO TEM ESPACO PRA ANDAR -- as `tiles` celulas a frente livres, no mapa do
	/// SERVIDOR (o mesmo que para o projetil).
	///
	/// Sem isto a bancada escolheria a direcao no escuro, e um raio que morre numa pedra a dois tiles
	/// nao chega ao ponto em que a foto e tirada -- e as fotos sairiam de tamanhos diferentes por
	/// causa do cenario, nao da arte. Todas as fotos precisam do MESMO fundo pra a comparacao
	/// significar alguma coisa.
	/// </summary>
	internal Facing RumoLivreDaVariedade(int id, int tiles)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return Facing.South;

		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		if (mapa == null) return Facing.South;

		const int T = ZoneCollision.TileSize;
		int cx = (int)Math.Floor(pl.Pos.X / T), cy = (int)Math.Floor(pl.Pos.Y / T);

		foreach (Facing f in (Facing[])[Facing.South, Facing.East, Facing.North, Facing.West])
		{
			Vec2 d = MeleeArea.Frente(f);
			bool livre = true;
			for (int k = 1; k <= tiles && livre; k++)
				if (mapa.BlockedCell(cx + (int)d.X * k, cy + (int)d.Y * k)) livre = false;
			if (livre) return f;
		}
		return Facing.South;
	}

	/// <summary>Onde o corpo esta, na conta do servidor.</summary>
	internal Vec2? PosDaVariedade(int id)
		=> _players.TryGetValue(id, out ServerPlayer? pl) ? pl.Pos : null;
}
