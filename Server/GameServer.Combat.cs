using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// COMBATE, LADO DO SERVIDOR.
///
/// Aqui e a excecao declarada ao "cliente calcula, servidor valida": o golpe e resolvido
/// SO AQUI. A razao e que a resolucao sorteia (pontaria, membro atingido, critico) e duas
/// pontas sorteando nunca chegam ao mesmo resultado. O cliente pede "socar" e recebe o
/// relato pronto -- ele nem sabe o BP de quem apanhou.
///
/// A CADENCIA E A TRAVA ANTI-CHEAT. Mandar o pacote de soco mil vezes por segundo nao adianta:
/// o servidor so aceita quando a recarga do proprio lutador zerou, e a recarga sai do
/// `Eactspeed` dele. O cliente recebe o mesmo numero na ficha (`SheetState.SocoMs`) pra nao
/// tentar o que vai ser recusado.
/// </summary>
public partial class GameServer
{
	/// <summary>Quanto tempo o corpo fica no chao antes de renascer, em milissegundos.</summary>
	private const long MsAteRenascer = 15_000;

	/// <summary>
	/// A skill que deixa imagem remanescente (`misc.dm:35`, a `Afterimage Technique`).
	///
	/// O SERVIDOR E QUEM CONSULTA porque a lista de skills do outro e ficha -- ninguem precisa
	/// saber o que o adversario aprendeu, so precisa VER o vulto. Ver `HitEvent.Zanzo`.
	/// </summary>
	private const string PathDoZanzoken = "/datum/skill/ki/Afterimage";

	/// <summary>Segundos de carencia de quem acabou de renascer (ver CombatState.Carencia).</summary>
	private const double SegundosDeCarencia = 6;

	/// <summary>
	/// ALCANCE DO ARRANQUE COM CORRIDA (SHIFT + ESPACO), em pixels. Cinco tiles.
	///
	/// O original aceitava ate 15 tiles, mas la o alvo era ESCOLHIDO (`get_me_a_target`) antes.
	/// Cinco tiles e o que da pra ler na tela como "vou naquele ali" sem o personagem se jogar
	/// em cima de quem passou no canto.
	/// </summary>
	private const float AlcanceDoDash = 160f;

	/// <summary>
	/// ALCANCE DO ARRANQUE SEM CORRIDA (so ESPACO), em pixels. Dois tiles e meio.
	///
	/// Socar alguem que esta a um passo nao deveria ser um soco no ar: o personagem da o passo
	/// e acerta. Curto de proposito -- e ajuste de posicao, nao investida.
	/// </summary>
	private const float AlcanceDoPasso = 80f;

	/// <summary>
	/// ONDE O ARRANQUE PARA: distancia final do alvo, em pixels. Um TILE.
	///
	/// E a mesma distancia que dois mobs vizinhos tinham no BYOND (turfs adjacentes) e cabe
	/// dentro do alcance do soco. Parar mais perto poe um sprite DENTRO do outro -- os dois
	/// tem 32 px de largura, e a tela vira dois bonecos empilhados.
	/// </summary>
	private const float DistanciaDeParada = 32f;

	/// <summary>
	/// QUANTO A INVESTIDA PRECISA ANDAR pra valer a pena existir. MEIO tile.
	///
	/// Comecou em um tile (o pedido original) e o dono jogou: "achei a distancia minima pro tp pro
	/// soco mt grande, coloque a metade da atual". Meio tile ainda mata a investida de 8 px que era
	/// o defeito -- rasgo, som e miragem por um passo que ninguem via -- sem exigir que o alvo esteja
	/// longe pra o personagem arrancar.
	///
	/// E deslocamento, nao distancia ate o alvo: o arranque para a <see cref="DistanciaDeParada"/>
	/// do outro, entao o que ele efetivamente anda e `dist - 32`. Sem este piso, estar a 40 px do
	/// alvo disparava uma investida de 8 px -- com rasgo, som e miragem por um passo que ninguem viu.
	/// </summary>
	private const float DeslocamentoMinimo = 16f;

	/// <summary>Fracao do Ki maximo que cada arranque custa (`15 * BaseDrain` no original).</summary>
	private const double CustoDashKi = 0.05;

	/// <summary>
	/// Espera minima entre dois arranques, em milissegundos.
	///
	/// No original a trava equivalente e o `post_attack`, que sai com `prob(35)` por volta de
	/// laco -- media de 1/0,35 = 2,86 voltas, ou ~480 ms. 500 e o numero mais proximo disso
	/// que nao depende de sorteio.
	/// </summary>
	private const long RecargaDashMs = 500;

	/// <summary>
	/// Racas que REGENERAM membro perdido. Nelas, perder um nucleo poe em coma em vez de
	/// matar -- e o `canheallopped` do original.
	/// </summary>
	private static bool Regenera(string raca) =>
		raca is "Namekian" or "Majin" or "BioAndroid" or "Shapeshifter";

	/// <summary>
	/// Quem nasce com rabo. Saiyajin puro e mesticos.
	///
	/// DESVIO CONSCIENTE: no original SO o Saiyajin puro recebia o rabo, e nao por decisao de
	/// desenho -- a rede de seguranca que garantia o membro testava `Race == "Saiyan"` e o
	/// genoma de Half-Saiyan e IRMAO, nao subtipo, do de Saiyan. Ou seja, o mestico ficava
	/// sem rabo por acidente de tipo. Gohan tinha rabo; aqui o mestico tambem tem.
	/// </summary>
	private static bool TemRabo(string raca) => raca is "Saiyan" or "Halfbreed";

	/// <summary>Monta o corpo de quem acabou de entrar, e devolve a vida ao estado salvo.</summary>
	// NAO E `static`: ele instala o gancho de negar morte, que precisa da instancia do servidor
	// (a explosao varre a zona e manda efeito). Ver `CombatState.NegarMorte`.
	private void PrepararCombate(ServerPlayer pl, CharacterSave? save)
	{
		pl.Combate = new CombatState(pl.Ficha, TemRabo(pl.Race), Regenera(pl.Race));

		// QUEM PODE BARRAR A MORTE DESTE CORPO. Instalado UMA vez, aqui, porque `Morrer()` e a porta
		// unica -- ver `CombatState.NegarMorte`. Hoje so a Aura of Destruction se pendura.
		pl.Combate.NegarMorte = _ => TentarNegarMorte(pl);

		// A vida dos membros PERSISTE entre sessoes: deslogar com o braco quebrado nao cura.
		// (Deslogar com o corpo DECEPADO tambem nao -- isso e coisa de regeneracao ou de morte.)
		if (save?.Membros is { Count: > 0 })
			foreach (BodyPart p in pl.Combate.Corpo.Partes)
				if (save.Membros.TryGetValue(p.Nome, out double[]? v) && v.Length >= 2)
				{
					p.Vida = Math.Clamp(v[0], 0, p.VidaMax);
					p.Decepado = v[1] > 0.5;
				}

		pl.Combate.SincronizarVida();
		AjustarGanhoDoRabo(pl);
		if (pl.Ficha.dead) { pl.RenasceEm = NowMs() + MsAteRenascer; return; }

		// NOCAUTE NAO ATRAVESSA O LOGOUT DE GRACA. O `KO` mora na ficha (que e salva) mas o
		// cronometro que faz levantar mora no estado de combate (que nao e) -- entrar com
		// KO=true e cronometro zerado deixaria o personagem no chao PARA SEMPRE. Quem volta
		// caido recomeca a contagem; quem volta com um nucleo abaixo do limiar cai de novo.
		if (pl.Ficha.KO || pl.Combate.Corpo.DeveNocautear())
			pl.Combate.Nocautear(MeleeResolver.SegundosDeNocaute);
	}

	/// <summary>
	/// O RABO NO RITMO DE TREINO. Com rabo, o Saiyajin ganha METADE (`tailgain = 0.5`); sem
	/// ele, 1,25 -- ou seja, perder o rabo faz treinar 2,5x mais rapido.
	///
	/// E do original (`statsaiyan.dm:267-290`) e nao e detalhe: e o motivo mecanico de o rabo
	/// ser uma escolha e nao so um enfeite. Quem tem rabo tem Oozaru e cresce devagar; quem
	/// perde, cresce rapido e nunca mais vira macaco.
	/// </summary>
	private static void AjustarGanhoDoRabo(ServerPlayer pl)
	{
		BodyPart? rabo = pl.Combate.Corpo.Achar("Rabo");
		if (rabo == null) { pl.Ficha.tailgain = 1; return; }   // raca sem rabo: neutro
		pl.Ficha.tailgain = rabo.Decepado ? 1.25 : 0.5;
	}

	/// <summary>Fotografa o corpo pro savefile: vida e "esta decepado" por membro.</summary>
	public static Dictionary<string, double[]> FotografarCorpo(CombatState c)
	{
		var d = new Dictionary<string, double[]>(c.Corpo.Partes.Count);
		foreach (BodyPart p in c.Corpo.Partes) d[p.Nome] = [p.Vida, p.Decepado ? 1 : 0];
		return d;
	}

	// =====================================================================
	// O GOLPE
	// =====================================================================
	private void Atacar(ServerPlayer a, Protocol.Golpe golpe)
	{
		CombatState ca = a.Combate;
		if (!ca.PodeAtacar()) return;   // morto, caido, atordoado ou ainda em recarga

		// ALVO MARCADO MANDA VIRAR. E o que faz "seus ataques serem focados nele" valer mesmo
		// com o alvo pelas costas: o personagem se volta pra ele ANTES de arrancar. Sem isso,
		// marcar alguem atras de voce e apertar espaco daria um soco no ar na direcao errada.
		if (Marcado(a) is { } mira)
			a.Facing = MoveRules.FacingFrom(mira.Pos - a.Pos, a.Facing);

		// APROXIMAR VEM ANTES DE SOCAR, e e assim no original: o `Attack()` fecha a distancia e
		// SO ENTAO chama o `MeleeAttack`. E o que faz o combate parecer Dragon Ball em vez de
		// dois bonecos se cutucando -- voce aponta pra alguem e o personagem VAI.
		bool investiu = Aproximar(a, longo: golpe == Protocol.Golpe.Pesado);

		// A IMAGEM REMANESCENTE nasce AQUI, junto da investida, e nao no relato do golpe: ela
		// marca o lugar de ONDE o corpo saiu. Decidida uma vez e carregada nos dois anuncios
		// (acertou e errou) -- errar uma investida deixa vulto igual, porque o vulto e do
		// deslocamento e nao do soco.
		// SO HA MIRAGEM SE HOUVE DESLOCAMENTO. O dono viu o contrario: "se eu usar shift + espaco
		// mesmo sem ninguem perto e eu tenho after image, eu crio uma miragem no meu lugar mesmo
		// nao me movendo". O vulto e do Zanzoken -- do corpo que saiu rapido demais pra vista
		// acompanhar --, entao sem deslocamento nao ha nada que a vista tenha perdido.
		bool zanzo = investiu && a.Livro?.Sabe(PathDoZanzoken) == true;

		// A MIRAGEM VIAJA COM A ORIGEM, num pacote so pros dois casos.
		//
		// Antes o vulto do dash saia do bit `HitEvent.Zanzo`, que NAO carrega posicao -- e o cliente
		// que assistia caia em `corpo.GlobalPosition`, ou seja, desenhava a miragem onde o corpo JA
		// tinha chegado. Quem executava via no lugar certo (ele guarda `_deOndeSai`) e quem assistia
		// via no lugar errado: era esse o "after image dessincronizado".
		//
		// O `S2C.Zanzo` do teleporte ja resolve isso -- ele leva o ponto de PARTIDA. Usar o mesmo
		// canal aqui deixa UM caminho pra miragem em vez de dois que precisam concordar.
		if (zanzo) AnunciarZanzo(a, a.SaiuDe);

		double tipo = Protocol.PesoDoGolpe(golpe);
		double espera = CombatMath.Cadencia(a.Ficha, tipo);
		ca.Recarga = espera;
		a.AtaqueAte = NowMs() + (long)(espera * 1000);

		// bater com a guarda erguida nao existe: o braco que soca e o que estava aparando
		ca.Guardar(false);

		ServerPlayer? alvo = AlvoNaFrente(a);

		// ZANZO CLASH: os dois se acertaram no mesmo instante? Entao este soco nao acontece -- ele
		// VIRA o embate. Conferido depois de achar o alvo (precisa saber em quem eu ia bater) e
		// antes de resolver (senao o dano sairia alem do embate).
		if (alvo != null && TentarEmbate(a, alvo)) return;

		if (alvo == null)
		{
			// SOCAR O AR AINDA TREINA. E o que o BYOND fazia e o que faz o novato progredir
			// sozinho num canto do mapa -- so que sem o multiplicador de lutar contra alguem.
			a.Ficha.AttackGain(_rng);
			ca.ZerarCombo();
			if (_diagGolpe) ExplicarSocoNoAr(a, investiu);

			// MAS NEM SEMPRE E AR. Antes daqui o golpe sem alvo simplesmente acabava, e socar uma
			// parede de proposito nao fazia nada -- so o corpo ARREMESSADO derrubava cenario. No
			// original quem soca cenario o quebra (`attack_proc.dm:95-112`), e e assim que se abre
			// caminho numa casa ou se derruba uma arvore sem depender de levar um golpe antes.
			//
			// O NIVEL VAI JUNTO porque ele e o que escolhe o baque: um soco leve na parede soa
			// pequeno e um pesado soa grande, igual a bater num corpo. Sem combo -- nao ha combo
			// contra cenario.
			if (SocarCenario(a, (int)Math.Min(3, tipo))) return;

			AnunciarSocoNoAr(a, zanzo, investiu);
			return;
		}

		// ============================ O UNICO SENTIDO QUE A IA TEM DO OPONENTE ============================
		// Chamado ANTES de resolver, e com o golpe que vai ACONTECER -- acertando, sendo aparado ou
		// errando, tanto faz: o que um lutador ve e o GESTO, e nao o resultado. A partir daqui o
		// cerebro estima a cadencia e prepara a guarda pro proximo (`Cerebro.ViuUmGolpe`).
		//
		// **E POR ISSO QUE ELE NAO LE O `alvo.Combate.Recarga`.** O servidor tem na mao o instante
		// exato do proximo soco de qualquer um -- daria uma IA que apara SEMPRE, impossivel de
		// enganar e sem nada pra o jogador descobrir. Aqui ela aprende apanhando, com atraso
		// (`TempoDeReacao`) e com erro, e o jeito de vencê-la e quebrar o proprio ritmo.
		//
		// Vale pro clone da mente, pro NPC e pro corpo possuido -- e nao vale pra jogador nenhum,
		// porque `Cerebro` nulo e a definicao de "quem dirige e o dono".
		alvo.Cerebro?.ViuUmGolpe();

		CombatState cd = alvo.Combate;
		double angulo = MeleeArea.AnguloDeChegada(alvo.Pos, alvo.Facing, a.Pos);

		// O NIVEL DO BAQUE sai daqui, ANTES de resolver: `min(3, tipo + min(1, combo))` do
		// original. Um soco leve isolado soa pequeno; o mesmo soco no meio de uma sequencia
		// soa medio; o pesado soa grande. E o que faz a briga esquentar no ouvido.
		int nivel = (int)Math.Min(3, tipo + Math.Min(1, ca.Combo));

		// O ESTILO ENTRA COMO DANO PLANO, nao como multiplicador -- e o `dmg += compareStyles(M)`
		// do original. Teto de 10: estilo desempata luta parelha, nao vence luta perdida.
		GolpeResultado r = MeleeResolver.Resolver(ca, cd, angulo, _rng, tipo, DanoDeEstilo(a, alvo));
		alvo.UltimoAgressor = a.Id;
		if (_diagGolpe) ExplicarGolpe(a, alvo, r, angulo, investiu);

		// ============================ OS GANCHOS DAS DISCIPLINAS DIVINAS ============================
		// O `ChanceEsquiva` que o resolvedor consultou foi escrito pelo Ultra Instinto, entao uma
		// esquiva com a passiva ligada E uma esquiva do instinto -- e ela paga maestria e empilha o
		// bonus. Sem esta linha a Autonomous Evasion funcionaria e nunca ensinaria nada.
        if (r.Desfecho == Desfecho.Esquivou) AoEsquivarPorInstinto(alvo);

		// E a AURA OF DESTRUCTION filtra o que passou: reduz o dano, devolve recuo em quem bateu e
		// guarda o golpe pra Destruction Explosion. Fica DEPOIS do resolvedor porque o corpo ja foi
		// ferido la dentro -- ver `AuraFiltraDano`, que desconta a diferenca.
		if (r.Dano > 0) AplicarAuraDepoisDoGolpe(alvo, a, r.Dano, ehKi: false);
		// ==========================================================================================

		// A DIRECAO DO SOCO FICA GUARDADA NO ALVO. E ela que deita o corpo se este golpe derrubar --
		// no ar e no chao, o mesmo angulo. Ver `ServerPlayer.DirecaoDeitado`.
		//
		// O VETOR E O MESMO DO ARREMESSO (`MeleeArea.Frente` do atacante), e nao a diferenca entre
		// as duas posicoes: os corpos ficam colados quando o soco acerta, e um vetor de dois pixels
		// e ruido -- a direcao do golpe e pra onde quem bateu estava virado.
		if (r.Encostou) alvo.RumoDoGolpe = MeleeArea.Frente(a.Facing);

		if (r.Encostou) ca.SomarCombo();
		else ca.ZerarCombo();          // errou, foi aparado de longe ou tomou contra: recomeca

		// LUTAR ENSINA OS DOIS. Quem bate ganha pela troca, quem apanha ganha pelo gap de
		// poder -- encarar alguem mais forte e o ganho mais rapido do jogo.
		a.Ficha.AttackGain(_rng, a.Ficha.FightGainMult(alvo.Ficha));
		if (r.Encostou) alvo.Ficha.AttackGain(_rng, alvo.Ficha.FightGainMult(a.Ficha));

		// O contra-ataque devolve o golpe: quem bloqueou na hora certa acerta de volta.
		if (r.Desfecho == Desfecho.Contra)
		{
			GolpeResultado devolta = MeleeResolver.Resolver(
				cd, ca, MeleeArea.AnguloDeChegada(a.Pos, a.Facing, alvo.Pos), _rng, tipo);
			a.UltimoAgressor = alvo.Id;
			ResolverDesfecho(alvo, a, devolta);
			AnunciarGolpe(alvo, a, devolta, 2, zanzo: false);   // o contra nao investe
		}

		// QUEM EU SOQUEI E QUANDO -- e o que deixa o proximo golpe DELE ser reconhecido como troca
		// simultanea. Ver `TentarEmbate`.
		a.UltimoAlvo = alvo.Id;
		a.UltimoSocoMs = NowMs();

		ResolverDesfecho(a, alvo, r);
		AnunciarGolpe(a, alvo, r, nivel, zanzo, investiu);

		// O CORPO SAI DO LUGAR. Vem DEPOIS do dano, como no original (`attack cmn.dm:110`, logo
		// apos o `hitProc`): o arremesso e consequencia do golpe que ACERTOU, e o `Impact` le o
		// dano ja calculado. Golpe aparado ou esquivado nao arremessa -- `r.Encostou` diz isso.
		if (r.Encostou) TentarEmpurrar(a, alvo, r.Dano, golpe);

		// O CHAO RACHA sob um golpe pesado ou critico. Ver `RacharChao`.
		if (r.Encostou && (golpe == Protocol.Golpe.Pesado || r.Desfecho == Desfecho.Critico))
			RacharChao(a.Zone, alvo.Pos, Math.Max(a.Ficha.expressedBP, alvo.Ficha.expressedBP));
	}

	/// <summary>As consequencias fora do corpo: nocaute, morte, Zenkai.</summary>
	private void ResolverDesfecho(ServerPlayer a, ServerPlayer d, GolpeResultado r)
	{
		if (r.RaboArrancado)
		{
			AjustarGanhoDoRabo(d);
			GD.Print($"[server] {a.Name} ARRANCOU O RABO de {d.Name}"
					 + $" (ritmo de treino de {d.Name}: x{d.Ficha.tailgain})");
		}

		// O ODIO POR GOLPE (`ENMITY_HIT`, `CombatMovement.dm:225`): so cresce contra quem a vitima
		// ja tinha declarado rival. Fica aqui, e nao no `Atacar`, porque este e o ponto que ja
		// responde "este golpe teve consequencia".
		if (r.Encostou) GolpeDeRival(d, a);

		if (r.Nocauteou)
		{
			GD.Print($"[server] {a.Name} NOCAUTEOU {d.Name} ({r.Membro})");
			// DERROTADO E NOCAUTEADO **OU** MORTO. Sem esta linha o Zenkai nunca aconteceria em
			// luta amistosa (golpe nao-letal e o padrao, e ali ninguem morre) -- justamente onde
			// a mecanica mais faz sentido. A recarga de uma hora impede o dobro quando o nocaute
			// vira morte na sequencia.
			//
			// E DESDE QUE A AMIZADE EXISTE ISTO FAZ MAIS UMA COISA: quem gostava de quem caiu, e
			// estava vendo, entra em RAIVA LENDARIA -- que e o que a linha Legendary pede. Ver
			// `AoPerderALuta`, que e o funil unico das consequencias de derrota.
			AoPerderALuta(d, a, morreu: false);
		}

		if (!r.Morreu) return;

		d.RenasceEm = NowMs() + MsAteRenascer;
		GD.Print($"[server] {a.Name} MATOU {d.Name} ({r.Membro})");

		// ZENKAI: perder pra alguem mais forte arranca poder do corpo. E pago na hora, direto
		// no BP base -- e recompensa, nao treino, entao nao passa pelo CapCheck. A concessao e
		// a MESMA do nocaute (ver GameServer.Raciais.cs), inclusive a recarga.
		// E o LUTO, aqui com o grau EXTREMO -- e ele que abre o SSJ1.
		AoPerderALuta(d, a, morreu: true);
	}

	// =====================================================================
	// O ARRANQUE (o "dash")
	// =====================================================================
	/// <summary>
	/// FECHAR A DISTANCIA ANTES DE SOCAR. E o `Attack()` do original: aperta ataque, o
	/// personagem VAI ate quem esta na frente e so entao bate.
	///
	/// Dois alcances, e a diferenca e a tecla:
	///   * SHIFT + ESPACO (<paramref name="longo"/>) -- investida de cinco tiles, e o golpe
	///     que sai dela ja e o pesado;
	///   * so ESPACO -- passo curto de dois tiles e meio, so pra nao errar um soco por meio
	///     metro de distancia. Custa metade do Ki.
	///
	/// MORA INTEIRO NO SERVIDOR, e isso e deliberado: um arranque e um salto de posicao, e
	/// salto de posicao calculado no cliente e exatamente o que a validacao de movimento
	/// existe pra recusar. Feito aqui, o cliente so aperta a tecla -- a posicao nova volta
	/// como correcao e o golpe volta como relato, os dois ja decididos.
	/// </summary>
	/// <returns>
	/// Houve deslocamento de verdade. E o que decide a IMAGEM REMANESCENTE: o dono viu que
	/// "shift + espaco mesmo sem ninguem perto" deixava miragem parado no lugar, e miragem de quem
	/// nao saiu do lugar e o oposto do que o Zanzoken quer dizer. Todos os `return` daqui sao
	/// "nao investiu": sem alvo, ja colado, sem Ki, em recarga, ou parede no caminho.
	/// </returns>
	private bool Aproximar(ServerPlayer a, bool longo)
	{
		long agora = NowMs();
		if (a.Combate == null || agora < a.DashLivreEm) return false;

		float alcance = longo ? AlcanceDoDash : AlcanceDoPasso;
		ServerPlayer? alvo = AlvoParaArranque(a, alcance);
		if (alvo == null) return false;

		double custo = a.Ficha.MaxKi * CustoDashKi * (longo ? 1 : 0.5);
		if (a.Ficha.Ki < custo) return false;

		// PARA ONDE: a um TILE do alvo, na linha que liga os dois. Encostado, nao POR CIMA --
		// parar dentro do alcance do soco mas colado empilha um sprite no outro, que foi
		// exatamente o que apareceu na primeira versao.
		Vec2 d = alvo.Pos - a.Pos;
		float dist = d.Length;

		// ============================ INVESTIDA PRECISA SER UMA INVESTIDA ============================
		// A conta antiga so exigia `dist > DistanciaDeParada` -- ou seja, a 40 px do alvo ela
		// disparava e deslocava OITO pixels. O jogador via o rasgo, ouvia o som e ganhava a miragem
		// por um passo que nao existiu; e o que o dono descreveu como "o personagem ta dando tp mesmo
		// estando pertinho do outro".
		//
		// Agora o que importa e o DESLOCAMENTO, e ele tem que valer pelo menos um tile. Vale igual
		// pros dois golpes -- o que muda entre leve e pesado e o ALCANCE (ate onde a investida busca
		// alguem), nao o quao curta ela pode ser.
		// =============================================================================================
		if (dist - DistanciaDeParada < DeslocamentoMinimo)
		{
			// PERTO DEMAIS PRO ARRANQUE, LONGE DEMAIS PRO SOCO -- o pior lugar do combate.
			//
			// O dono: "as vezes vc n da tp pq ta perto mas n o suficiente pra bater ai vc soca o nada
			// e e anti climatico". Ele esta certo, e a culpa era do piso que eu mesmo pus: sem
			// investida o corpo ficava parado a 40 px e o soco (que alcanca 40) passava raspando.
			//
			// Entao o corpo ANDA o resto. Nao e investida -- nao ha rasgo, som nem miragem, e a
			// funcao devolve `false` --, e so o passo que faltava pra o punho chegar. A parede
			// continua mandando: o mesmo teste de caminho de sempre.
			if (dist > CombatKnobs.Alcance && mapaDoAtaque(a) is { } m
				&& !MoveRules.PathOccupied(m, a.Pos, a.Pos + d.Normalized() * (dist - DistanciaDeParada)))
			{
				a.Pos += d.Normalized() * (dist - DistanciaDeParada);
				a.Facing = MoveRules.FacingFrom(d, a.Facing);
				a.LastInputMs = agora;
				a.CorrecaoEsperadaAte = agora + 500;
				a.SeqDoTeleporte = a.SeqInput;
				a.OrcamentoPx = 0;

				var passo = Protocol.Begin(Protocol.S2C.Correction);
				passo.Put(a.SeqInput);
				passo.PutVec(a.Pos);
				a.Peer?.Send(passo, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			}
			return false;
		}

		Vec2 destino = a.Pos + d.Normalized() * (dist - DistanciaDeParada);

		// PAREDE MANDA MAIS QUE O ARRANQUE. Sem esta checagem, investir contra um muro com
		// alguem do outro lado atravessaria a parede -- o unico jeito de andar por dentro dela.
		ZoneCollision? mapa = _catalogo?.Get(a.Zone)?.Mapa;
		if (mapa != null && MoveRules.PathOccupied(mapa, a.Pos, destino)) return false;

		a.Ficha.Ki -= custo;
		a.SaiuDe = a.Pos;   // pra miragem: e daqui que o vulto nasce, e nao de onde ele chegou
		a.Pos = destino;
		a.Facing = MoveRules.FacingFrom(d, a.Facing);   // chega OLHANDO pro alvo
		a.DashLivreEm = agora + RecargaDashMs;
		a.LastInputMs = agora;   // o cliente vai reportar da posicao NOVA: nao conta como passo
		// os pacotes que o cliente ja mandou com a posicao ANTIGA vao chegar e ser corrigidos.
		// Isso e o arranque funcionando, nao um cliente desonesto -- nao conta no medidor.
		a.CorrecaoEsperadaAte = agora + 500;   // + a SEQUENCIA: input montado antes deste instante nao opina sobre onde o corpo esta
		a.SeqDoTeleporte = a.SeqInput;

		// a posicao nova precisa chegar ao dono AGORA, senao o proximo pacote dele parte do
		// lugar antigo e o servidor devolve correcao em cima de correcao
		var w = Protocol.Begin(Protocol.S2C.Correction);
		w.Put(a.SeqInput);   // O PACOTE TEM DUAS PARTES desde o conserto do dash:
		// sequencia + posicao. Escrever so a posicao aqui mandava um pacote MALFORMADO --
		// o cliente leria a coordenada X como se fosse a sequencia.
		w.PutVec(a.Pos);
		a.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		return true;   // investiu de verdade: houve deslocamento
	}

	/// <summary>
	/// Quem esta na frente e vale um arranque. O cone e o mesmo do soco; o que muda e o
	/// comprimento -- e por isso que o arranque pega quem esta "mais ou menos" na frente e
	/// ignora quem esta ao lado.
	/// </summary>
	/// <summary>O mapa da zona de quem ataca. Atalho -- o `Aproximar` consulta duas vezes.</summary>
	private ZoneCollision? mapaDoAtaque(ServerPlayer a) => _catalogo?.Get(a.Zone)?.Mapa;

	private ServerPlayer? AlvoParaArranque(ServerPlayer a, float alcance)
	{
		// MARCADO PRIMEIRO. So precisa estar no ALCANCE -- o cone nao entra: quem marcou ja
		// disse em quem quer bater, e obrigar a mirar de novo com o mouse seria pedir a mesma
		// informacao duas vezes.
		if (Marcado(a) is { } mira)
		{
			float d2 = (mira.Pos - a.Pos).LengthSquared;
			if (d2 <= alcance * alcance && d2 > DistanciaDeParada * DistanciaDeParada) return mira;
			return null;   // marcou alguem longe demais: nao arranca atras de outro sem querer
		}

		ServerPlayer? melhor = null;
		float melhorDist = float.MaxValue;
		Vec2 frente = MeleeArea.Frente(a.Facing);

		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
		{
			if (o == a || o.Ficha.dead || o.Combate.Intocavel) continue;
			if (!AlcancaPelaAltura(a, o)) continue;

			Vec2 d = o.Pos - a.Pos;
			float dist2 = d.LengthSquared;
			if (dist2 > alcance * alcance) continue;
			// ja esta de frente: nao ha o que fechar, o soco normal resolve
			if (dist2 <= DistanciaDeParada * DistanciaDeParada) continue;
			if (MeleeArea.Angulo(frente, d) > CombatKnobs.MeioAnguloCone) continue;

			if (dist2 >= melhorDist) continue;
			melhorDist = dist2;
			melhor = o;
		}
		return melhor;
	}

	/// <summary>
	/// Quem esta no cone do golpe. O MAIS PROXIMO leva -- socar nao acerta dois de uma vez.
	/// Morto e ignorado: o corpo esta no chao, nao no caminho.
	/// </summary>
	private ServerPlayer? AlvoNaFrente(ServerPlayer a)
	{
		// MARCADO PRIMEIRO, e so a DISTANCIA conta. O `Atacar` ja virou o personagem pra ele,
		// entao normalmente o cone tambem aprovaria -- mas com dois colados o cone aprova os
		// dois, e e ai que marcar tem que decidir.
		if (Marcado(a) is { } mira
			&& (mira.Pos - a.Pos).LengthSquared <= CombatKnobs.Alcance * CombatKnobs.Alcance)
			return mira;

		ServerPlayer? melhor = null;
		float melhorDist = float.MaxValue;

		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
		{
			if (o == a || o.Ficha.dead || o.Combate.Intocavel) continue;
			// ============================ QUEM ALCANCA QUEM, POR ANDAR ============================
			// Antes desta linha o alcance era so horizontal, e voar nao mudava NADA no combate --
			// dois corpos a 20 tiles de altura de diferenca socavam um ao outro normalmente, o que
			// tornava a altitude decoracao. As regras (assimetricas!) moram no `Voo.PodeAcertar`.
			// ====================================================================================
			if (!AlcancaPelaAltura(a, o)) continue;
			if (!MeleeArea.NoAlcance(a.Pos, a.Facing, o.Pos)) continue;

			float dist = (o.Pos - a.Pos).LengthSquared;
			if (dist >= melhorDist) continue;
			melhorDist = dist;
			melhor = o;
		}
		return melhor;
	}

	/// <summary>
	/// MARCAR ALGUEM. Zero limpa a mira.
	///
	/// ============================ ISTO ESTAVA DENTRO DO `switch` DO DESPACHO ============================
	/// A validacao ("so vale mirar em quem existe e esta na MESMA zona -- o resto e cliente
	/// inventando") era duas linhas soltas no `case Protocol.C2S.Alvo`. Enquanto so o pacote mirava,
	/// tanto fazia; a partir do momento em que a IA tambem mira -- e ela mira pra poder atirar de
	/// longe --, ou a regra vira funcao ou ela vira DUAS regras, e a segunda e sempre a mais frouxa.
	///
	/// Continua sendo o mesmo funil pros dois lados: `Handle` chama isto, `AplicarComando` chama
	/// isto, e nao ha uma terceira escrita em `AlvoId` no servidor inteiro fora do auto-limpar do
	/// <see cref="Marcado"/>.
	/// ==============================================================================================
	/// </summary>
	private void Mirar(ServerPlayer quem, int alvo) =>
		quem.AlvoId = alvo != 0 && _players.TryGetValue(alvo, out ServerPlayer? o)
					  && o != quem && o.Zone.Hash == quem.Zone.Hash ? alvo : 0;

	/// <summary>
	/// O ALVO MARCADO, se ainda valer a pena. Devolve nulo quando ninguem foi marcado ou
	/// quando o marcado saiu de cena (morreu, trocou de zona, virou intocavel) -- e ai o
	/// combate volta a escolher pelo cone, sozinho.
	/// </summary>
	private ServerPlayer? Marcado(ServerPlayer a)
	{
		if (a.AlvoId == 0) return null;
		if (!_players.TryGetValue(a.AlvoId, out ServerPlayer? o)
			|| o == a || o.Ficha.dead || o.Combate.Intocavel || o.Zone.Hash != a.Zone.Hash)
		{
			a.AlvoId = 0;   // limpa sozinho: alvo morto nao fica preso na mira pra sempre
			return null;
		}
		// A ALTURA VALE ATE PRO MARCADO, e este e o caso que mais precisava: marcar alguem e
		// depois subir dois andares nao pode virar um passe livre pra continuar socando de longe.
		// O alvo NAO e limpo -- ele volta a valer assim que os dois se emparelharem de novo.
		if (!AlcancaPelaAltura(a, o)) return null;
		return o;
	}

	/// <summary>
	/// SOCO NO AR. Anuncia pra zona um golpe que nao achou ninguem -- e o que faz o corte do
	/// punho no vazio (`meleemiss1/2/3`) soar, pra quem soca e pra quem esta perto.
	///
	/// Vai por canal NAO CONFIAVEL: quem treina soca tres vezes por segundo, e reenviar um
	/// "nao aconteceu nada" e o tipo de trafego que nao vale um unico byte de garantia. Perder
	/// um custa um som.
	/// </summary>
	private void AnunciarSocoNoAr(ServerPlayer a, bool zanzo = false, bool investiu = false)
	{
		var e = new Protocol.HitEvent
		{
			Atacante = a.Id, Alvo = 0, Desfecho = (byte)Desfecho.Errou,
			Nivel = 1, Membro = "", Investiu = investiu,
		};
		var w = Protocol.Begin(Protocol.S2C.Hit);
		e.Write(w);

		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelState, DeliveryMethod.Unreliable);
	}

	/// <summary>
	/// Conta o golpe pra zona. Os DOIS ENVOLVIDOS recebem o dano; quem so assistiu recebe o
	/// evento sem numero -- ve o impacto e ouve o som, mas nao le a ficha alheia.
	/// </summary>
	/// <param name="zanzo">
	/// Houve imagem remanescente. PADRAO FALSO porque o vulto e da INVESTIDA do soco pesado, e
	/// golpe de tecnica (as quatro chamadas de `Tecnicas.G2/G3`) nao investe -- ele acontece de
	/// onde o corpo ja estava. Deixar o padrao aqui evita que cada tecnica nova tenha que decidir
	/// sobre um efeito que nao e dela.
	/// </param>
	private void AnunciarGolpe(ServerPlayer a, ServerPlayer d, GolpeResultado r, int nivel, bool zanzo = false, bool investiu = false)
	{
		var cheio = new Protocol.HitEvent
		{
			Atacante = a.Id, Alvo = d.Id, Desfecho = (byte)r.Desfecho,
			Nivel = (byte)Math.Clamp(nivel, 1, 3),
			// DE ONDE O SOCO SAIU. `a.Pos` aqui ja e o destino do arranque -- e essa a coordenada
			// com que o golpe FOI resolvido, e e ela que o desenho precisa. Ver `HitEvent.PosAtacante`.
			PosAtacante = a.Pos,
			TemDano = true, Dano = (float)r.Dano, Membro = r.Membro,
			Quebrou = r.Quebrou, Decepou = r.Decepou, Nocauteou = r.Nocauteou, Morreu = r.Morreu,
			Rabo = r.RaboArrancado, Investiu = investiu,
			// a esquiva e do OUTRO: quem se desviou e quem deixa o vulto
			ZanzoEsquiva = r.Desfecho == Desfecho.Esquivou && d.Livro?.Sabe(PathDoZanzoken) == true,
		};
		Protocol.HitEvent magro = cheio;
		magro.TemDano = false;

		var wCheio = Protocol.Begin(Protocol.S2C.Hit); cheio.Write(wCheio);
		var wMagro = Protocol.Begin(Protocol.S2C.Hit); magro.Write(wMagro);

		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
		{
			// Pros DOIS envolvidos o relato e confiavel: perder o pacote que diz "voce perdeu
			// o braco" nao e opcao. Pra quem so assiste vai sem garantia -- e uma piscada e um
			// som, e reenviar isso pra uma zona cheia de gente e o tipo de trafego que derruba
			// servidor.
			if (o == a || o == d)
				o.Peer?.Send(wCheio, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			else
				o.Peer?.Send(wMagro, Protocol.ChannelState, DeliveryMethod.Unreliable);
		}
	}

	// =====================================================================
	// `--diaggolpe`: A CONTA DO SOCO, EM VOZ ALTA
	// =====================================================================
	/// <summary>
	/// Liga o relato de cada soco no console do servidor. Ver <see cref="ExplicarGolpe"/>.
	/// </summary>
	private bool _diagGolpe;

	/// <summary>
	/// POR QUE O SOCO NAO PEGOU NINGUEM -- a diferenca que o jogador nao consegue ver.
	///
	/// ============================ DOIS ERROS QUE PARECEM UM ============================
	/// Um soco pode falhar de dois jeitos completamente diferentes, e o cliente desenha os dois
	/// igual (o mesmo som de vento):
	///
	///   * ERRO DE GEOMETRIA -- nao havia ninguem no alcance nem no cone. E o que este metodo
	///     relata. Se acontece logo depois de uma investida, ha algo errado: a investida para a
	///     um tile do alvo, bem dentro do alcance do soco.
	///   * ERRO DE PONTARIA -- havia alguem, e o sorteio de `CombatMath.Pontaria` falhou. Isso e
	///     mecanica, nao defeito, e sai pelo outro relato (`ExplicarGolpe`).
	///
	/// O dono relatou "mesmo dando o tp de soco o hit n pega". Sem separar os dois, nao da pra
	/// saber se a queixa e de uma hitbox furada ou da taxa de acerto do personagem dele.
	/// ===================================================================================
	/// </summary>
	private void ExplicarSocoNoAr(ServerPlayer a, bool investiu)
	{
		// QUEM ESTAVA POR PERTO, e por que cada um ficou de fora. Sem isto o relato diria so
		// "nao achei ninguem", que e a pergunta e nao a resposta.
		var perto = new List<string>();
		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
		{
			if (o == a || o.Ficha.dead) continue;
			Vec2 d = o.Pos - a.Pos;
			float dist = d.Length;
			if (dist > 200) continue;
			double ang = MeleeArea.Angulo(MeleeArea.Frente(a.Facing), d);
			perto.Add($"{o.Name} a {dist:0} px, {ang:0}° do olhar"
					  + $" ({(dist <= CombatKnobs.Alcance ? "no alcance" : "LONGE")},"
					  + $" {(ang <= CombatKnobs.MeioAnguloCone ? "no cone" : "FORA do cone")})");
		}

		GD.Print($"[golpe] {a.Name} socou o AR{(investiu ? " DEPOIS DE INVESTIR" : "")}"
				 + $" | olhando {a.Facing} | alcance {CombatKnobs.Alcance:0} px, cone ±{CombatKnobs.MeioAnguloCone:0}°"
				 + (perto.Count > 0 ? $" | por perto: {string.Join(" ; ", perto)}" : " | ninguem por perto"));
	}

	/// <summary>
	/// A CONTA DO GOLPE QUE ACHOU ALGUEM: quem, quanto, e de onde saiu o numero.
	///
	/// ============================ POR QUE ISTO EXISTE ============================
	/// O dono relatou um admin dando "+100 de dano" com apenas ~10 pontos de diferenca de BP.
	/// Com BP parelho a formula rende uns 3 de dano por soco -- pra chegar a 100 falta um fator
	/// de mais de trinta, e ele tem que estar em ALGUM dos termos.
	///
	/// O suspeito de sempre e o `BpModulus`, que e a RAZAO entre os dois BP EXPRESSOS e nao tem
	/// teto: nocaute derruba o expresso pra um decimo do BP base, invisibilidade o trava em 5, e
	/// um defensor com expresso zerado devolve 999 de multiplicador. Nenhuma dessas tres coisas
	/// aparece na tela de quem esta apanhando, e todas produzem exatamente o que foi relatado.
	///
	/// Por isso o relato imprime os dois BP expressos e o multiplicador SEPARADOS do dano final:
	/// com essas tres colunas na frente, o fator de trinta ou esta ali ou nao esta em lugar nenhum.
	/// =============================================================================
	/// </summary>
	private void ExplicarGolpe(ServerPlayer a, ServerPlayer d, GolpeResultado r, double angulo, bool investiu)
	{
		double bpA = a.Ficha.expressedBP, bpD = d.Ficha.expressedBP;
		GD.Print($"[golpe] {a.Name} -> {d.Name}: {r.Desfecho}, dano {r.Dano:0.##}"
				 + $" | BP expresso {bpA:N0} vs {bpD:N0} (razao x{CombatMath.BpModulus(bpA, bpD):0.##})"
				 + $" | BP base {a.Ficha.BP:N0} vs {d.Ficha.BP:N0}"
				 + $" | physoff {a.Ficha.Ephysoff:0.##} vs physdef {d.Ficha.Ephysdef:0.##}"
				 + $" | tecnica {a.Ficha.Etechnique:0.##}"
				 + $" | angulo {angulo:0}° | dist {(d.Pos - a.Pos).Length:0} px"
				 + $"{(investiu ? " | investiu" : "")}"
				 + $"{(d.Ficha.KO ? " | ALVO NOCAUTEADO" : "")}"
				 + $"{(d.Ficha.isconcealed ? " | ALVO INVISIVEL (expresso travado)" : "")}"
				 + $"{(a.Ficha.isconcealed ? " | EU INVISIVEL (expresso travado)" : "")}"
				 + $" | admin {(EhAdmin(a) ? "sim" : "nao")}");
	}

	// =====================================================================
	// PASSAGEM DE TEMPO
	// =====================================================================
	/// <summary>
	/// Roda a cada tick do servidor (30 Hz): cronometros de recarga, atordoamento, guarda,
	/// saida do nocaute e o renascimento de quem morreu.
	/// </summary>
	private void TickCombate(double dt)
	{
		long agora = NowMs();
		foreach (ServerPlayer pl in _players.Values)
		{
			CombatState c = pl.Combate;
			if (c == null) continue;

			bool eraKO = pl.Ficha.KO;
			c.Tick(dt);
			if (eraKO && !pl.Ficha.KO)
			{
				c.SincronizarVida();
				GD.Print($"[server] {pl.Name} levantou");
			}

			RegenerarPassivo(pl, dt);

			if (pl.Ficha.dead && agora >= pl.RenasceEm) Renascer(pl);

			TickDoEstomago(pl, dt);

			MandarCorpo(pl);       // barato: sai sozinho quando nada mudou
			MandarAtributos(pl);   // idem: atributo so muda quando se treina
			MandarSkills(pl);
		}
	}

	/// <summary>
	/// O ESTOMAGO, na cadencia dele -- ver <see cref="Jandirus.Core.Stats.Nutricao"/>.
	///
	/// ============================ ELE NAO RODA NO TIQUE DO SERVIDOR ============================
	/// As constantes de digestao do original sao POR PASSADA de tres segundos, nao por segundo.
	/// Chamar isto a 30 Hz multiplicaria a digestao por noventa: o tanque esvaziaria em dois
	/// segundos e o vigor encheria no mesmo tempo -- os dois defeitos opostos de uma vez.
	///
	/// Por isso ha um acumulador por jogador. E a mesma disciplina do `_accumulator` do servidor:
	/// um sistema com relogio proprio guarda o relogio proprio.
	/// ===========================================================================================
	/// </summary>
	private readonly Dictionary<int, double> _relogioDoEstomago = [];

	/// <summary>Quanto dano de fome ja se acumulou, por jogador. Ver `Nutricao.DanoDaFome`.</summary>
	private readonly Dictionary<int, double> _fomeAcumulada = [];

	private void TickDoEstomago(ServerPlayer pl, double dt)
	{
		double relogio = _relogioDoEstomago.GetValueOrDefault(pl.Id) + dt;

		// O metabolismo acelera as passadas, e nao so aumenta o tanque: quem digere rapido come
		// mais vezes por minuto. E o `spawn(30 / Metabolism)` do DM.
		double intervalo = Jandirus.Core.Stats.Nutricao.SegundosPorPassada
						 / Math.Max(pl.Ficha.Metabolism, 0.1);
		if (relogio < intervalo) { _relogioDoEstomago[pl.Id] = relogio; return; }
		_relogioDoEstomago[pl.Id] = relogio - intervalo;

		Jandirus.Core.Stats.Nutricao.QueimarVigor(pl.Ficha);
		Jandirus.Core.Stats.Nutricao.Digerir(pl.Ficha);

		// PASSAR FOME MACHUCA, e o dano vai pelo mesmo caminho do combate -- espalhado pelo corpo.
		double acumulado = _fomeAcumulada.GetValueOrDefault(pl.Id);
		double dano = Jandirus.Core.Stats.Nutricao.DanoDaFome(pl.Ficha, ref acumulado);
		_fomeAcumulada[pl.Id] = acumulado;
		if (dano > 0) Espalhar(pl, dano);   // ele ja sincroniza a vida

		// O BARRIGAO RONCA UMA VEZ, e nao a cada tres segundos. O `Hungry` da ficha e o que impede
		// o aviso de virar spam; ele so volta a zero quando o personagem come.
		if (!pl.Ficha.Hungry && Jandirus.Core.Stats.Nutricao.TemFome(pl.Ficha))
		{
			pl.Ficha.Hungry = true;
			Avisar(pl, pl.Ficha.CurrentNutrition <= 0
				? "seu estômago ronca. Você precisa comer ALGUMA COISA."
				: "seu fôlego está no fim. Comer alguma coisa ajudaria.");
		}
	}

	/// <summary>
	/// A FICHA LENTA pro dono: os oito atributos e o que o menu (tecla P) mostra.
	///
	/// Mesma disciplina do corpo -- so sai quando MUDA. Atributo mexe quando se treina, e
	/// treinar leva minutos: mandar isto junto com a vida, cinco vezes por segundo, seria
	/// repetir o mesmo pacote milhares de vezes por sessao.
	/// </summary>
	private static void MandarAtributos(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		var a = new Protocol.AtributosState
		{
			PhysOff = (float)f.Rphysoff, PhysDef = (float)f.Rphysdef,
			KiOff = (float)f.Rkioff, KiDef = (float)f.Rkidef,
			Technique = (float)f.Rtechnique, KiSkill = (float)f.Rkiskill,
			Speed = (float)f.Rspeed, Esoteric = (float)f.Rmagiskill,
			Willpower = (float)f.Ewillpower, Stamina = (float)f.staminapercent,
			Idade = pl.Idade,
			Raca = pl.Race,

			// AINDA ZERO, e de proposito: nenhuma destas habilidades existe no port. Quem
			// acende cada bit e a etapa que trouxer o sistema -- a skill de Sense, o scouter,
			// o nav system. Enquanto estiver zerado, as abas correspondentes NAO aparecem, que
			// e exatamente a regra do original.
			// O NAV so existe NO ESPACO. Em terra firme um mapa estelar nao serve pra nada, e a
			// aba apareceria vazia -- e aba vazia ensina que o sistema nao funciona.
			// A CARTA ESTELAR VALE EM QUALQUER LUGAR -- ver `PoderesVisiveis`, que e quem explica.
			Poderes = (uint)PoderesVisiveis(pl),
			// O FIO CONTINUA FALANDO O MESMO NUMERO de sempre (o `IdRede` do catalogo e o inteiro
			// que o enum antigo tinha), entao cliente velho e save velho leem sem conversao.
			FormaAtual = Jandirus.Core.Forms.Catalogo.Rede(pl.Forma.Atual),
			Maestrias = [.. pl.Forma.Maestria.Todas.Select(t => (Jandirus.Core.Forms.Catalogo.Rede(t.Id), (float)t.V))],
			Disciplina = (byte)(pl.UltraInstinct.Aprendida ? 1 : pl.PoderDaDestruicao.Aprendida ? 2 : 0),
			DiscReal = (float)(pl.UltraInstinct.Aprendida ? pl.UltraInstinct.Real : pl.PoderDaDestruicao.Real),
			DiscAtual = (float)(pl.UltraInstinct.Aprendida ? pl.UltraInstinct.Atual : pl.PoderDaDestruicao.Atual),
			DiscLigada = pl.UltraInstinct.Aprendida ? pl.UltraInstinct.Ligada : pl.PoderDaDestruicao.Ligada,
		};

		// assinatura grossa: 1% de resolucao por atributo. Sem isso o ruido de ponto flutuante
		// remandaria o pacote a cada tick.
		string sig = $"{a.PhysOff:0.##}|{a.PhysDef:0.##}|{a.KiOff:0.##}|{a.KiDef:0.##}|"
				   + $"{a.Technique:0.##}|{a.KiSkill:0.##}|{a.Speed:0.##}|{a.Esoteric:0.##}|"
				   + $"{a.Willpower:0.##}|{a.Stamina:0.#}|{a.Poderes}|{a.Idade}|{a.FormaAtual}|"
				   + string.Join(',', a.Maestrias.Select(m => $"{m.Forma}:{m.Pct:0.#}"));
		if (sig == pl.SigAtributos) return;
		pl.SigAtributos = sig;

		// A BANCADA ESCUTA A FICHA QUE SAIU. Uma linha, nula em jogo, e pelo mesmo motivo das quatro
		// escutas do `GameServer.FormasTeste.cs`: pacote que saiu no fio nao volta, e a aba de formas
		// (que le a proficiencia das disciplinas) mora do outro lado dele. Ver `EscutaDeAtributos`.
		EscutaDeAtributos?.Add(a);

		var w = Protocol.Begin(Protocol.S2C.Atributos);
		a.Write(w);
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}


	// =====================================================================
	// O CORPO PRO DONO
	// =====================================================================
	/// <summary>
	/// Manda o estado de cada membro pro proprio jogador -- e o que alimenta o boneco de dano.
	///
	/// SO QUANDO MUDA. A assinatura e a mesma ideia do `last_sig` do original: com o corpo
	/// parado, isto e zero trafego; com a luta rolando, e um pacote de ~200 bytes por golpe
	/// que altera alguma coisa. Mandar 15 membros a 5 Hz por jogador seria desperdicio puro.
	/// </summary>
	private static void MandarCorpo(ServerPlayer pl)
	{
		if (pl.Combate == null) return;

		var partes = new List<Protocol.ParteState>(pl.Combate.Corpo.Partes.Count);
		var sb = new System.Text.StringBuilder();
		foreach (BodyPart p in pl.Combate.Corpo.Partes)
		{
			// ARREDONDA AO MAIS PROXIMO. O original usa duas contas diferentes e vale saber
			// qual e qual: o HP GERAL usa `round(x)` de um argumento, que no DM e PISO; a
			// porcentagem POR MEMBRO usa `round(x, 1)` de dois, que arredonda. E a de membro
			// que alimenta o boneco.
			byte v = (byte)Math.Clamp(Math.Round(p.Fracao * 100), 0, 100);
			partes.Add(new Protocol.ParteState { Nome = p.Nome, Vida = v, Decepado = p.Decepado });
			sb.Append(p.Decepado ? 'L' : (char)('0' + v / 4));   // 4% de resolucao: nao remanda por ruido
		}

		string sig = sb.ToString();
		if (sig == pl.CorpoEnviado) return;
		pl.CorpoEnviado = sig;

		var w = Protocol.Begin(Protocol.S2C.Corpo);
		w.PutCorpo(partes);
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// ============================ A CURA PASSIVA MUDOU DE CASA ============================
	// Ela era um `private const` daqui. Foi pra `CombatKnobs.RegenPorSegundo`, no Core, e a razao e
	// a regra 0.1 da especificacao: **e uma regra que decide resultado de jogo**, e agora ela decide
	// mais de um. O calor da estrela e calibrado CONTRA ela (o piso do dano existe pra ficar acima da
	// cura -- ver `CalorDaEstrela.FatorMinimo`), e a bancada do AssetPipeline precisa afirmar essa
	// desigualdade sem subir o Godot. Duas copias do 1,67 seriam duas copias que envelhecem separadas.
	// ==================================================================================

	/// <summary>
	/// REGENERACAO PASSIVA: so fora de combate, e so pra quem nao esta morto. Enquanto a tag de
	/// luta esta no ar o corpo nao se recupera -- senao ninguem perde nunca.
	///
	/// ============================ POR QUE ISTO VIROU FUNCAO ============================
	/// Eram cinco linhas soltas dentro do <see cref="TickCombate"/>. Elas sairam de la quando a
	/// estrela passou a queimar, e o motivo e medido: o dano do sol e de 3,0 a 80 por segundo e a
	/// cura e de 1,67 -- ou seja **a cura e um TERCO do dano no caso mais leve**, e o piso de
	/// <see cref="Jandirus.Core.World.CalorDaEstrela.FatorMinimo"/> so faz sentido contra ela.
	///
	/// Uma bancada que medisse "quanto tempo o corpo aguenta dentro do sol" sem regeneracao mediria
	/// outro jogo. E copiar as cinco linhas pra dentro da bancada seria justamente o "atalho
	/// paralelo" que a regra 0.7 recusa: testaria a copia, e no dia em que a cura mudasse aqui, a
	/// bancada continuaria verde medindo a cura antiga. Uma casa so, dois chamadores.
	/// ==============================================================================
	/// </summary>
	private static void RegenerarPassivo(ServerPlayer pl, double dt)
	{
		CombatState? c = pl.Combate;
		if (c == null || pl.Ficha.dead || pl.Ficha.KO || c.EmCombate > 0 || pl.Ficha.HP >= 99.99) return;

		c.Corpo.Curar(CombatKnobs.RegenPorSegundo * dt);
		c.SincronizarVida();
	}

	/// <summary>
	/// Morrer devolve o personagem inteiro no BERCO dele, com metade da vida.
	///
	/// ============================ ERA A TERRA CRAVADA, E ESSE E O BUG QUE NAO APARECE ============================
	/// Este metodo mandava todo mundo pra `SpawnZone` -- a Terra. Ligar o berco so no NASCIMENTO
	/// teria deixado o jogador nascendo em casa e ressuscitando na Terra, e ninguem perceberia por
	/// semanas: sao dois caminhos diferentes no codigo pra a mesma pergunta do jogador ("onde eu
	/// apareco?"). Por isso os dois saem do mesmo funil (`GameServer.Berco.cs`), que e o que o DM
	/// tinha num lugar so (`mob/proc/Locate()`).
	/// ========================================================================================================
	///
	/// O JULGAMENTO DO ENMA (karma, o alem, o revive por Zeni) e outra etapa -- ate la, morrer
	/// custa a luta, o tempo no chao e mais nada. Fica marcado aqui pra nao virar "esqueci".
	/// </summary>
	private void Renascer(ServerPlayer pl)
	{
		// MORRER DEVOLVE O CORPO INTEIRO, rabo incluso -- e o `Restaurar()` do Body. O ritmo
		// de treino precisa voltar junto, senao o Saiyajin ressuscitado ficaria com o bonus
		// de quem nao tem rabo e o rabo de volta no lugar.
		pl.Combate.Reviver(0.5, SegundosDeCarencia);
		AjustarGanhoDoRabo(pl);
		pl.Ficha.Ki = pl.Ficha.MaxKi * 0.5;
		pl.RenasceEm = 0;

		(ZoneKey destino, Vec2 onde) = DestinoDoBerco(pl.Berco);

		if (pl.Zone.Hash != destino.Hash) MandarProBerco(pl);
		else
		{
			// MESMA ZONA: nao ha o que recarregar, so um teleporte dentro dela. O ponto vem do
			// funil do mesmo jeito -- num mundo gerado ele e a chegada que o gerador garantiu
			// livre, e num pre-feito e a celula livre mais perto do (249,250).
			pl.Pos = onde;
			var w = Protocol.Begin(Protocol.S2C.Correction);
			w.Put(pl.SeqInput);   // O PACOTE TEM DUAS PARTES desde o conserto do dash:
			// sequencia + posicao. Escrever so a posicao aqui mandava um pacote MALFORMADO --
			// o cliente leria a coordenada X como se fosse a sequencia.
			w.PutVec(pl.Pos);
			pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}

		GD.Print($"[server] {pl.Name} renasceu em {destino.Name} ({SegundosDeCarencia:0}s de carencia)");
	}
}
