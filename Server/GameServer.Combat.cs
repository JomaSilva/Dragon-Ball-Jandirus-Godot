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
	private static void PrepararCombate(ServerPlayer pl, CharacterSave? save)
	{
		pl.Combate = new CombatState(pl.Ficha, TemRabo(pl.Race), Regenera(pl.Race));

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
			AnunciarSocoNoAr(a, zanzo, investiu);
			return;
		}

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

		if (r.Nocauteou)
		{
			GD.Print($"[server] {a.Name} NOCAUTEOU {d.Name} ({r.Membro})");
			// DERROTADO E NOCAUTEADO **OU** MORTO. Sem esta linha o Zenkai nunca aconteceria em
			// luta amistosa (golpe nao-letal e o padrao, e ali ninguem morre) -- justamente onde
			// a mecanica mais faz sentido. A recarga de uma hora impede o dobro quando o nocaute
			// vira morte na sequencia.
			ZenkaiPorDerrota(d, a);
		}

		if (!r.Morreu) return;

		d.RenasceEm = NowMs() + MsAteRenascer;
		GD.Print($"[server] {a.Name} MATOU {d.Name} ({r.Membro})");

		// ZENKAI: perder pra alguem mais forte arranca poder do corpo. E pago na hora, direto
		// no BP base -- e recompensa, nao treino, entao nao passa pelo CapCheck. A concessao e
		// a MESMA do nocaute (ver GameServer.Raciais.cs), inclusive a recarga.
		ZenkaiPorDerrota(d, a);
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
			if (!MeleeArea.NoAlcance(a.Pos, a.Facing, o.Pos)) continue;

			float dist = (o.Pos - a.Pos).LengthSquared;
			if (dist >= melhorDist) continue;
			melhorDist = dist;
			melhor = o;
		}
		return melhor;
	}

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

			// REGENERACAO PASSIVA: so fora de combate, e so pra quem nao esta morto. Enquanto
			// a tag de luta esta no ar o corpo nao se recupera -- senao ninguem perde nunca.
			if (!pl.Ficha.dead && !pl.Ficha.KO && c.EmCombate <= 0 && pl.Ficha.HP < 99.99)
			{
				c.Corpo.Curar(RegenPorSegundo * dt);
				c.SincronizarVida();
			}

			if (pl.Ficha.dead && agora >= pl.RenasceEm) Renascer(pl);

			MandarCorpo(pl);       // barato: sai sozinho quando nada mudou
			MandarAtributos(pl);   // idem: atributo so muda quando se treina
			MandarSkills(pl);
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
			FormaAtual = (ushort)pl.Forma.Atual,
			Maestrias = [.. pl.Forma.Maestria.Todas.Select(t => ((ushort)t.F, (float)t.V))],
		};

		// assinatura grossa: 1% de resolucao por atributo. Sem isso o ruido de ponto flutuante
		// remandaria o pacote a cada tick.
		string sig = $"{a.PhysOff:0.##}|{a.PhysDef:0.##}|{a.KiOff:0.##}|{a.KiDef:0.##}|"
				   + $"{a.Technique:0.##}|{a.KiSkill:0.##}|{a.Speed:0.##}|{a.Esoteric:0.##}|"
				   + $"{a.Willpower:0.##}|{a.Stamina:0.#}|{a.Poderes}|{a.Idade}|{a.FormaAtual}|"
				   + string.Join(',', a.Maestrias.Select(m => $"{m.Forma}:{m.Pct:0.#}"));
		if (sig == pl.SigAtributos) return;
		pl.SigAtributos = sig;

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

	/// <summary>
	/// Vida por segundo que o corpo recupera fora de combate. Um corpo inteiro leva ~1 minuto
	/// pra sair de zero -- lento o bastante pra derrota doer, rapido o bastante pra nao
	/// obrigar ninguem a ficar sentado esperando.
	/// </summary>
	private const double RegenPorSegundo = 100.0 / 60.0;

	/// <summary>
	/// Morrer devolve o personagem inteiro no ponto de spawn, com metade da vida.
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

		if (pl.Zone.Hash != SpawnZone.Hash) MoveToZone(pl.Id, SpawnZone, SpawnPos);
		else
		{
			pl.Pos = SpawnPos;
			var w = Protocol.Begin(Protocol.S2C.Correction);
			w.Put(pl.SeqInput);   // O PACOTE TEM DUAS PARTES desde o conserto do dash:
			// sequencia + posicao. Escrever so a posicao aqui mandava um pacote MALFORMADO --
			// o cliente leria a coordenada X como se fosse a sequencia.
			w.PutVec(pl.Pos);
			pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}

		GD.Print($"[server] {pl.Name} renasceu ({SegundosDeCarencia:0}s de carencia)");
	}
}
