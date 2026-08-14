using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// KNOCKBACK E DESTRUICAO DE CENARIO -- as duas coisas vivem juntas porque no original tambem
/// vivem: e o corpo arremessado batendo na parede que derruba a parede.
///
/// ============================ DE ONDE VEM ============================
/// `Impact.dm` decide, `/effect/knockback` (Movement Effects.dm) executa, `turf/proc/Destroy`
/// (NewTurfs.dm) derruba. As formulas estao no <see cref="Empurrao"/>, no Core; aqui fica o
/// encanamento: quem voa, por quanto tempo, o que ele encontra e quem fica sabendo.
///
/// O TIQUE DO ORIGINAL E 1 DECIMO e cada tique da DOIS passos de tile -- ou seja ~2 tiles a cada
/// 0,1 s, ate 10 tiques. E rapido de proposito: o corpo tem que SUMIR de onde estava.
/// =====================================================================
///
/// ============================ POR QUE O SERVIDOR MOVE, E NAO O CLIENTE ============================
/// Todo o resto do movimento e do cliente ("cliente calcula, servidor valida"). O arremesso nao:
/// o jogador nao esta dirigindo, esta sendo jogado. Quem calcula e quem sabe -- e por isso o
/// `Estado` ganhou um bit dizendo "voce esta voando", pra o cliente parar de integrar input e so
/// seguir as correcoes. Sem esse bit, o cliente empurraria de volta e os dois brigariam pelo corpo,
/// que e exatamente a briga que fazia o personagem TREMER na parede.
/// ==================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O GOLPE MEXEU COM O CORPO? Chamado depois de o dano ser resolvido, como no original
	/// (`attack cmn.dm:110`, logo apos o `hitProc`).
	/// </summary>
	private void TentarEmpurrar(ServerPlayer a, ServerPlayer d, double dmg, Protocol.Golpe golpe)
	{
		if (!a.Knockback || d.TiquesDeVoo > 0 || d.Ficha.dead) return;

		double check = Empurrao.Check(a.Ficha, d.Ficha);
		double forca;
		bool inevitavel = false;

		if (golpe == Protocol.Golpe.Leve)
		{
			// o sorteio do soco leve: ou arremessa de verdade, ou da um empurraozinho garantido
			if (Empurrao.SorteioDoSocoLeve(dmg, check, p => _rng.NextDouble() * 100 < p) is not { } s) return;
			forca = s.Dmg;
			inevitavel = s.Inevitavel;
		}
		else forca = Empurrao.ForcaDoPesado(dmg, a.Ficha, check);

		double limiar = Empurrao.Limiar(d.Ficha, a.Ficha.expressedBP);
		(EfeitoDeImpacto efeito, int tiques) = Empurrao.Avaliar(forca, limiar, inevitavel);

		switch (efeito)
		{
			case EfeitoDeImpacto.Arremesso:
				Arremessar(d, MeleeArea.Frente(a.Facing), a.Ficha.expressedBP, tiques);
				break;

			// CAMBALEIA E LENTO ainda nao tem efeito proprio no port (nao ha `slowed`/`stagger` na
			// ficha), entao viram atordoamento curto -- que e o que o CombatState ja sabe fazer e o
			// que o jogador sente igual: perder o proximo golpe. Anotado como aproximacao.
			case EfeitoDeImpacto.Cambaleia:
				d.Combate.Stun = Math.Max(d.Combate.Stun, Empurrao.TiquesDeTropeco * Empurrao.SegundosPorTique);
				break;
		}
	}

	/// <summary>
	/// PoE O CORPO NO AR -- o `AddEffect(/effect/knockback)` do original, e a UNICA porta pra isso.
	///
	/// ============================ POR QUE ISTO VIROU METODO ============================
	/// Ate o lote G6 este bloco morava dentro do `case Arremesso` do <see cref="TentarEmpurrar"/>,
	/// e era a unica coisa no jogo que arremessava alguem -- entao ele nao precisava de nome. O
	/// sopro (`Kiai`, `Shockwave`, `Explosive_Roar`) arremessa SEM golpe nenhum: o `KiKnockback`
	/// do DM (`KiStatsModule.dm:42`) escreve `kbpow`/`kbdur`/`kbdir` na mao e liga o mesmo efeito.
	///
	/// Copiar as sete linhas seria a segunda resposta pra "como um corpo comeca a voar", e as duas
	/// divergiriam no primeiro dia em que alguem mexesse numa -- comecando pelo `MarcarSulco` e
	/// pelo `MandarFicha`, que sao justamente os dois que ninguem lembra de repetir (e cuja falta
	/// ja produziu o "voa travado e rotacionado errado" que este arquivo conta em detalhe).
	/// ==================================================================================
	/// </summary>
	/// <param name="forca">
	/// O `kbpow`: o `expressedBP` de QUEM empurrou. Nao e o dano -- e ele que decide se a parede no
	/// caminho cai ou se o voo para nela.
	/// </param>
	/// <param name="tiques">O `kbdur`, em tiques de 0,1 s. O efeito ja aplica o teto de 10.</param>
	private void Arremessar(ServerPlayer d, Vec2 rumo, double forca, int tiques)
	{
		tiques = Math.Clamp(tiques, 1, Empurrao.TiquesMax);

		d.TiquesDeVoo = tiques;
		d.TiquesIniciaisDoVoo = tiques;
		d.RumoDoVoo = rumo;
		d.ForcaDoVoo = forca;
		d.VooNoTique = 0;
		d.UltimoSulco = default;
		// A PONTA DE COMECO sai aqui, e nao no primeiro tique: no DU o "begin" e carimbado
		// quando `knock_dist == original_distance-1`, ou seja no primeiro passo, com o corpo
		// ainda na origem.
		MarcarSulco(d, Protocol.Decal.SulcoPonta);

		// A FICHA SAI AGORA, e nao no proximo tique de 5 Hz.
		//
		// O bit "estou voando" (e a direcao do corpo) so viaja no pacote de ficha, e ficha sai
		// no `TickFichas`, que roda 1 vez a cada 6 tiques. O voo comeca no tique CHEIO e a
		// primeira correcao sai ~33 ms depois -- ou seja, por ate 200 ms o cliente ainda nao
		// sabia que estava voando: nao girava o corpo E teleportava 64 px por correcao em vez
		// de deslizar. Como o voo mais curto tem 300 ms, ate dois tercos dele aconteciam em pe
		// e aos saltos. Era esse o resto de "voa travado" e "rotacionado errado".
		//
		// O canal e ReliableOrdered, entao mandar aqui poe a ficha NA FRENTE da primeira
		// correcao -- quando ela chega, o cliente ja esta no modo de voo.
		MandarFicha(d);
	}

	/// <summary>
	/// O VOO. Roda no tick cheio e so mexe em quem esta no ar.
	///
	/// ============================ O TIQUE DO DM E GROSSO DEMAIS PRA DESENHAR ============================
	/// O original anda em passos de 0,1 s valendo DOIS TILES cada, e era assim que isto estava: o
	/// corpo ficava 100 ms exatamente parado e entao pulava 64 px de uma vez. Quem assiste recebe
	/// snapshot a 30 Hz, entao via dois quadros de corpo imovel e um de corpo deslizando 64 px --
	/// dispara-para-dispara. Era o "quando toma knock back voa sem fluidez", e nao tinha conserto
	/// possivel no cliente: a interpolacao estava reproduzindo com fidelidade um movimento que de
	/// fato acontecia aos solavancos.
	///
	/// Agora o passo e FATIADO pelo tique do servidor -- um terco dele por vez, ~21 px a 30 Hz.
	/// Distancia e duracao totais nao mudam (tres fatias fecham os mesmos dois tiles no mesmo
	/// decimo de segundo), e `TiquesDeVoo` continua contando na moeda do DM, que e o que o dano do
	/// baque le. O que muda e so que o corpo atravessa o caminho em vez de aparecer no fim dele.
	///
	/// A COLISAO SO MELHOROU: o caminho continua sendo varrido de meio tile em meio tile pra nao
	/// pular parede, e agora isso acontece tres vezes por tique do original em vez de uma.
	/// ======================================================================================================
	/// </summary>
	private void TickDoEmpurrao()
	{
		long agora = NowMs();

		// Que fracao do tique do original cabe num tique do servidor. Com 30 Hz e 0,1 s, um terco.
		double fatia = Protocol.TickSeconds / Empurrao.SegundosPorTique;

		foreach (ServerPlayer pl in _players.Values)
		{
			if (pl.TiquesDeVoo <= 0) continue;

			ZoneCollision? mapa = _catalogo?.Get(pl.Zone)?.Mapa;
			Vec2 passo = pl.RumoDoVoo * (float)(Empurrao.TilesPorTique * ZoneCollision.TileSize * fatia);
			Vec2 destino = pl.Pos + passo;

			// ---- o que tem no caminho ----
			bool parou = false;

			// OUTRO CORPO: os dois se machucam e o voo acaba. `SpreadDamage(duration)` no original.
			foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			{
				if (o == pl || o.Ficha.dead) continue;
				if ((o.Pos - destino).LengthSquared > 32 * 32) continue;
				Espalhar(pl, pl.TiquesDeVoo);
				Espalhar(o, pl.TiquesDeVoo);
				parou = true;
				break;
			}

			// ============================ TODA PAREDE DO CAMINHO, NAO SO A DO FIM ============================
			// Quando o passo andava dois tiles de uma vez, testar so a celula de DESTINO deixava a
			// parede do MEIO do salto ser atravessada -- o dono viu exatamente isso, "algumas paredes
			// q ele passa n quebram". Por isso o passo e caminhado de meio tile em meio tile.
			//
			// A VARREDURA CONTINUA VALENDO com o passo fatiado, e so ficou mais fina: a fatia mede
			// 21 px contra os 16 do meio tile, entao sao duas amostras por tique do servidor e seis
			// por tique do original -- onde antes eram quatro.
			//
			// Cada parede encontrada e derrubada (se a forca vence) e o voo continua; a primeira que
			// RESISTE para o corpo ali, no ultimo ponto livre -- a mesma regra do `Ticked` do original.
			// ================================================================================================
			if (!parou && mapa != null)
			{
				const float Amostra = ZoneCollision.TileSize / 2f;
				int passos = Math.Max(1, (int)MathF.Ceiling(passo.Length / Amostra));
				Vec2 andado = pl.Pos;

				for (int i = 1; i <= passos && !parou; i++)
				{
					Vec2 p = pl.Pos + passo * (i / (float)passos);

					// O QUE O CORPO ATRAVESSA TAMBEM SOFRE. E o `for(var/obj/O in get_step(...))
					// if(O.fragile) O.takeDamage(pow)` do original: a arvore e a bancada nao
					// precisam BLOQUEAR pra serem arrancadas por alguem passando voando por cima.
					EstragarObrasNoCaminho(pl, p);

					// ============================ QUEM E ARREMESSADO ATRAVESSA A AGUA ============================
					// E literal do original: o `testWaters()` deixa passar `M.KB`
					// (`Swim.dm:31`) junto com quem voa e quem nada. Faz sentido em jogo -- o corpo
					// esta no ar, nao esta pisando --, e sem o `modo` aqui um soco que jogasse
					// alguem pro lago o pararia na beirada como se a agua fosse muro, cortando o
					// arremesso no meio sem nada explicando por que.
					//
					// PAREDE CONTINUA PARANDO (e sendo derrubada logo abaixo): o modo muda a
					// resposta da AGUA e so dela. Ver `ClasseDeAgua.Bloqueia`.
					if (!MoveRules.Occupied(mapa, p, ModoDeTravessia.Arremessado)) { andado = p; continue; }

					if (pl.ForcaDoVoo >= Empurrao.ResistenciaPadrao && DerrubarCenario(pl.Zone, p))
					{
						andado = p;   // a parede caiu: o corpo passa por cima do escombro
						continue;
					}

					// RESISTIU: o corpo se arrebenta nela e o voo acaba no ultimo ponto livre.
					Espalhar(pl, pl.TiquesDeVoo);
					parou = true;
				}
				destino = andado;
			}

			pl.Pos = destino;

			// ============================ UM SULCO POR TILE, E NAO POR TIQUE ============================
			// A primeira versao carimbava no fim de cada tique do DM. So que o tique do DM anda DOIS
			// tiles (`Empurrao.TilesPorTique`), e o DU carimba dentro do laco que anda UM
			// (`step(src,knock_dir,32)` -- death.dm:216-224). Resultado: uma marca a cada 64 px, com
			// um buraco de um tile entre elas. O dono fotografou: "o rastro ta vindo picotado e n
			// continuo".
			//
			// Aqui a chamada e por FATIA (~21 px) e quem faz a conta certa e a guarda de celula que
			// ja existia dentro do `MarcarSulco`: ela deixa passar a primeira fatia de cada celula e
			// recusa as seguintes. Uma marca por tile, sem buraco e sem sobreposicao.
			// ==========================================================================================
			if (!parou && pl.TiquesDeVoo > 0) MarcarSulco(pl, Protocol.Decal.Sulco);

			// O RELOGIO DO DM SO VIRA QUANDO AS FATIAS FECHAM UM TIQUE INTEIRO. `TiquesDeVoo` e a
			// moeda do original -- e dela que sai o dano do baque (`Espalhar`) e a duracao do voo --
			// entao ela continua contando de 0,1 em 0,1 s, por mais que o corpo ande tres vezes
			// nesse intervalo. A folga no teste absorve o erro de ponto flutuante das tres somas.
			if (parou) { pl.TiquesDeVoo = 0; pl.VooNoTique = 0; }
			else
			{
				pl.VooNoTique += fatia;
				if (pl.VooNoTique >= 1 - 1e-6)
				{
					pl.VooNoTique -= 1;
					pl.TiquesDeVoo--;
				}
			}

			// A OUTRA BORDA: o pouso. Mesma razao da decolagem -- sem isto o corpo ficava ate 200 ms
			// torto e sem aceitar comando DEPOIS de ja ter parado.
			bool pousou = pl.TiquesDeVoo <= 0;

			// ============================ O FIM DO ARRASTO ============================
			// No DU sao duas coisas no mesmo instante: a ULTIMA marca do sulco vira "begin" de novo
			// (`knock_dist==0`), e onde o corpo para nasce uma CRATERA (`obj/Crater`, que cresce de
			// 1% ate o tamanho cheio). A fumaca nao vem do DU -- e o pedido do dono, e nasce junto
			// com a cratera porque e ela que a levanta.
			//
			// Vale pros DOIS fins: parar numa parede e chegar ao fim da distancia. E o que o dono
			// pediu -- "no final (ou se alguma parede parasse o jogador/npc voando)".
			// ==========================================================================
			if (pousou)
			{
				MarcarSulco(pl, Protocol.Decal.SulcoPonta);
				if (RastroVale(pl))
				{
					MandarDecalque(pl.Zone, Protocol.Decal.Cratera, pl.Pos, pl.Facing);
					MandarDecalque(pl.Zone, Protocol.Decal.Fumaca, pl.Pos, pl.Facing);
				}
			}

			// O CLIENTE PRECISA SABER ONDE ELE ESTA, e com a sequencia carimbada -- senao os pacotes
			// que ele ja mandou (da posicao antiga) seriam tratados como cliente errado e puxariam o
			// corpo de volta. Mesma armadilha do dash.
			pl.LastInputMs = agora;
			pl.CorrecaoEsperadaAte = agora + 500;
			pl.SeqDoTeleporte = pl.SeqInput;
			pl.OrcamentoPx = 0;

			var w = Protocol.Begin(Protocol.S2C.Correction);
			w.Put(pl.SeqInput);
			w.PutVec(pl.Pos);
			pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			if (pousou) MandarFicha(pl);   // a posicao final vai ANTES do "acabou"
		}
	}

	/// <summary>
	/// ESTE ARREMESSO DEIXA RASTRO NO CHAO?
	///
	/// ============================ AS REGRAS SAO DO DU, MENOS UMA ============================
	/// `death.dm:218` pede tres coisas: a flag `dirt_trail`, distancia total >= 8 tiles, e direcao
	/// CARDEAL.
	///
	/// A terceira NAO VIRA CODIGO AQUI, e isso e de proposito: `RumoDoVoo` sai de
	/// `MeleeArea.Frente(a.Facing)`, e `Facing` so tem quatro valores (as quatro do BYOND). Um
	/// `if` de direcao cardeal seria uma guarda que nunca falha -- codigo morto vestido de regra.
	///
	/// A `dirt_trail` do DU e um parametro por chamada (o `Stat Loop` passa 0 num caso); aqui ela
	/// virou "esta no CHAO", que e o que o dono pediu -- quem esta voando nao ara a terra.
	/// ======================================================================================
	///
	/// ============================ E NO ESPACO NAO HA CHAO PRA ARAR ============================
	/// A guarda de altura NAO cobria o vacuo: `pl.Altitude` e zero no espaco (a altitude e um
	/// sistema de planeta -- `TentarRomperAAtmosfera` devolve na porta quando a zona e o espaco), e
	/// `TiquesIniciaisDoVoo` passa de 4 em qualquer arremesso decente. Resultado: arremessar alguem
	/// no vacuo carimbava SULCO DE TERRA e CRATERA no nada, e o pacote ia pro `ZoneList` do espaco,
	/// que e o universo INTEIRO -- todo mundo online via a cratera, a qualquer distancia.
	///
	/// Ficou invisivel enquanto ninguem brigava no espaco. Jogar alguem no sol e exatamente brigar
	/// no espaco, entao a Fase 4 e quem paga esta conta.
	///
	/// A pergunta certa nao e "estou no chao": e "existe chao". `Espaco.EhPlaneta` e quem responde
	/// isso, e ja e o mesmo criterio que a volta da borda e a musica de transformacao usam.
	/// ======================================================================================
	/// </summary>
	private static bool RastroVale(ServerPlayer pl)
		=> pl.Altitude <= 0f
		   && Espaco.EhPlaneta(pl.Zone)
		   && pl.TiquesIniciaisDoVoo * Empurrao.TilesPorTique >= TilesParaDeixarRastro;

	/// <summary>Distancia minima do arremesso pra ele arar o chao. `death.dm:218` -- oito tiles.</summary>
	private const int TilesParaDeixarRastro = 8;

	/// <summary>
	/// CARIMBA UM SULCO onde o corpo esta, se o arremesso merecer rastro.
	///
	/// A guarda de celula repetida existe porque as tres fatias de um tique podem cair na mesma
	/// celula quando o corpo bate em algo e anda pouco -- e duas marcas sobrepostas leem como uma
	/// mancha, nao como pegada.
	/// </summary>
	private void MarcarSulco(ServerPlayer pl, Protocol.Decal tipo)
	{
		if (!RastroVale(pl)) return;

		// ============================ A CELULA E A DOS PES, NAO A DO CENTRO ============================
		// `pl.Pos` e o CENTRO do corpo; quem encosta no chao sao os pes, 8 px abaixo
		// (`MoveRules.FeetOffsetY` -- a mesma ancora que a colisao e a sombra de voo ja usam).
		//
		// Escolher a celula pelo centro punha o sulco uma fileira acima do arrasto de verdade, e como
		// a marca e alinhada a grade esse erro nao se dissolve: ele aparece como um rastro deslocado
		// do personagem. O dono viu: "as vezes fica um pouco torto com a posiçao do personagem".
		//
		// So a ESCOLHA DA CELULA muda -- o alinhamento a grade (que foi o que fechou os buracos)
		// continua igual.
		// ==============================================================================================
		Vec2 pes = pl.Pos + new Vec2(0, MoveRules.FeetOffsetY);
		var celula = new Vec2(MathF.Floor(pes.X / ZoneCollision.TileSize),
							  MathF.Floor(pes.Y / ZoneCollision.TileSize));
		if (celula.X == pl.UltimoSulco.X && celula.Y == pl.UltimoSulco.Y) return;
		pl.UltimoSulco = celula;

		// ============================ O SULCO VAI NO CENTRO DA CELULA ============================
		// Nao na posicao do corpo. O DU carimba no TURF (`var/turf/t=loc; TimedOverlay(t,600,i)` --
		// death.dm:223), ou seja alinhado a grade; carimbar no pixel exato onde o corpo estava faz a
		// distancia entre duas marcas VARIAR.
		//
		// A conta: o passo do servidor e ~21 px e a celula tem 32. O corpo entra em cada celula com
		// uma sobra diferente (0, 10, 20, 30...), entao duas marcas consecutivas ficam de 22 a 42 px
		// uma da outra -- e o sprite tem 32. Onde a sobra e grande, aparece o buraco. Era o "ta
		// melhor mas n ta continuo" do dono.
		//
		// Alinhado a celula sao 32 px exatos entre marcas de 32 px: encostam, sem vao e sem
		// sobreposicao.
		// ========================================================================================
		float t = ZoneCollision.TileSize;
		var noCentro = new Vec2((celula.X + 0.5f) * t, (celula.Y + 0.5f) * t);

		// A DIRECAO E A DO ARREMESSO, e nao pra onde o corpo olha: o sulco e a marca do arrasto.
		MandarDecalque(pl.Zone, tipo, noCentro, MoveRules.FacingFrom(pl.RumoDoVoo, pl.Facing));
	}

	/// <summary>
	/// DANO ESPALHADO PELO CORPO INTEIRO, em vez de num membro so.
	///
	/// No arremesso ele e o baque, e a dose e o que faltava voar. Mas o mecanismo nao e do
	/// arremesso: passar fome usa o mesmo caminho (`TickDoEstomago`), e por isso a dose e um
	/// `double` -- o dano de fome e fracionario por passada, e um `int` o truncaria pra zero.
	/// </summary>
	private static void Espalhar(ServerPlayer pl, double dano)
	{
		foreach (Jandirus.Core.Combat.BodyPart parte in pl.Combate.Corpo.Partes)
			parte.Vida = Math.Max(0, parte.Vida - dano / pl.Combate.Corpo.Partes.Count);
		pl.Combate.SincronizarVida();
	}

	// =====================================================================
	// DESTRUICAO DE CENARIO
	// =====================================================================
	/// <summary>
	/// AS CELULAS QUE JA CAIRAM, por zona. Vive em memoria e nao no `.col`: o arquivo e imutavel e
	/// compartilhado (ver `ZoneCollision`), e o mapa tem que voltar ao normal quando o servidor
	/// reinicia -- o original tambem nao salvava turf destruido fora do `MapSave`.
	/// </summary>
	private readonly Dictionary<string, HashSet<(int X, int Y)>> _cenarioCaido =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// DERRUBA A CELULA que <paramref name="onde"/> toca. Devolve se caiu alguma coisa.
	///
	/// ============================ O QUE O ORIGINAL FAZ ============================
    /// `turf/proc/Destroy()`: se `destroyable`, 25% de chance de poeira e o turf e SUBSTITUIDO por
    /// `/turf/Ground/Ground8` -- chao liso. Ou seja destruir nao abre buraco, aplaina.
    ///
    /// A resistencia padrao de todo turf e todo obj e VINTE (`buildable.dm:353-360`), e por isso
    /// praticamente tudo e destrutivel desde o primeiro soco: quem protege um pedaco do mapa e o
    /// `destroyable = 0` (borda do mundo, arena, espaco), nao a resistencia.
    ///
    /// AQUI O `destroyable` E APROXIMADO pela BORDA DO MUNDO: o conversor ja marca essas celulas
    /// com um grupo proprio (`ZoneCollision.BordaDoMundo`), que e exatamente o
    /// `/turf/Other/Blank` -- o unico `destroyable = 0` que aparece em quantidade nos mapas. As
    /// outras excecoes do DM (arena, Void_Wall) ficam anotadas como divida.
	/// =============================================================================
	/// </summary>
	private bool DerrubarCenario(ZoneKey zona, Vec2 onde)
	{
		ZoneCollision? mapa = _catalogo?.Get(zona)?.Mapa;
		if (mapa == null) return false;

		(int cx, int cy) = CelulaDoPonto(onde);
		if (!mapa.BlockedCell(cx, cy)) return false;

		// ============================ BORDA NAO CAI: E GEOMETRIA ============================
		// Duas perguntas, e as duas precisam ser feitas:
		//
		// A BEIRADA DO MAPA, por aritmetica. Nao depende de plano nenhum, e e a que faltava: um mapa
		// que termina em agua nao tem `BordaDoMundo` marcado em lugar nenhum, e a ultima coluna caia
		// como qualquer cenario.
		//
		// O QUE ESTAVA AQUI ERA CODIGO MORTO -- em 100% dos casos, nao "quase".
		//
		// A guarda antiga perguntava `Grupo(cx,cy) == BordaDoMundo`. O plano de grupo e escrito no
		// arquivo `.vis`, e o servidor carrega o `.col`, que sai SEM ele: `Grupo` devolvia
		// `SemGrupo` (255) em toda celula de toda zona do jogo, e 255 nunca e 0. A condicao
		// `Visao.Length > 0` que a acompanhava tambem nao protegia nada -- as 40 entradas do
		// manifest tem `visao`.
		//
		// E nem daria pra consertar mandando o servidor ler o `.vis`: o plano nasce ZERADO e so as
		// celulas cegas sao escritas, entao 244.198 das 250.000 celulas da Terra ficam com grupo 0,
		// que e o MESMO codigo de `BordaDoMundo`. O sentinela colide com o preenchimento padrao, e
		// ler o plano faria 97% do mapa responder "sou borda do mundo".
		//
		// Sobra a aritmetica, que nao depende de plano nenhum e vale ate em mundo sorteado.
		// ===================================================================================
		if (mapa.NaBorda(cx, cy)) return false;

		return DerrubarCelula(zona, cx, cy);
	}

	/// <summary>
	/// DERRUBA UMA CELULA JA ESCOLHIDA, sem perguntar de novo se ela podia cair.
	///
	/// Separado do <see cref="DerrubarCenario"/> porque agora ha dois caminhos ate aqui -- o corpo
	/// que voa (que descobre a celula andando) e o SOCO (que ja sabe qual e a celula da frente). O
	/// que os dois nao podem e ter cada um a sua copia da parte que muda o mundo: as guardas de
	/// borda ficam com quem escolhe, a mudanca fica aqui.
	/// </summary>
	private bool DerrubarCelula(ZoneKey zona, int cx, int cy)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		if (mapa == null) return false;

		if (!_cenarioCaido.TryGetValue(zona.Name, out HashSet<(int X, int Y)>? caidas))
		{
			caidas = [];
			_cenarioCaido[zona.Name] = caidas;
		}
		if (!caidas.Add((cx, cy))) return false;

		// O MAPA MUDA PROS DOIS LADOS: aqui, e no cliente pelo pacote abaixo. A celula deixa de
		// bloquear e deixa de cegar -- ela virou chao.
		mapa.Abrir(cx, cy);

		MandarCelulaCaida(zona, cx, cy);
		return true;
	}

	/// <summary>
	/// O CHAO RACHA NO IMPACTO. E o `attack cmn.dm:49` do original:
	///
	///     for(var/turf/T in view(1,src))
	///         if(prob(40) && !T.isSpecial && !T.proprietor && T.Resistance <= max(expressedBP, M.expressedBP))
	///             if(prob(60)) createDust(T,1)
	///             T.Destroy()
	///
	/// La isso acontece no ZanzoClash; aqui vale pra todo golpe PESADO ou CRITICO, que e onde o
	/// dono esperava ver estrago: "mesmo com golpes fortes e criticos que causam knock back e onda
	/// de choque o chao em si tb n esta quebrando".
	///
	/// ============================ CHAO NAO E PAREDE ============================
	/// O <see cref="DerrubarCenario"/> so mexe em celula que BLOQUEIA -- ele existe pra derrubar o
	/// muro em que o corpo bateu. Rachar o chao e outra coisa: a celula continua passavel, o que
	/// muda e o DESENHO (vira terra batida, o Ground8). Por isso o caminho aqui e proprio e nao
	/// consulta colisao nenhuma.
	/// ==========================================================================
	/// </summary>
	private void RacharChao(ZoneKey zona, Vec2 centro, double bp)
	{
		if (bp < Empurrao.ResistenciaPadrao) return;
		if (_catalogo?.Get(zona)?.Mapa is not { } mapa) return;

		const int T = ZoneCollision.TileSize;
		int cx0 = (int)Math.Floor(centro.X / T), cy0 = (int)Math.Floor(centro.Y / T);

		if (!_cenarioCaido.TryGetValue(zona.Name, out HashSet<(int X, int Y)>? caidas))
		{
			caidas = [];
			_cenarioCaido[zona.Name] = caidas;
		}

		// `view(1)` sao as nove celulas em volta -- a do impacto e as oito vizinhas.
		for (int dy = -1; dy <= 1; dy++)
			for (int dx = -1; dx <= 1; dx++)
			{
				int cx = cx0 + dx, cy = cy0 + dy;
				if (cx < 0 || cy < 0 || cx >= mapa.Width || cy >= mapa.Height) continue;
				if (_rng.NextDouble() >= 0.40) continue;            // o `prob(40)` do original
				if (mapa.NaBorda(cx, cy)) continue;   // beirada do mapa nao racha (ver `DerrubarCenario`)
				if (!caidas.Add((cx, cy))) continue;

				// PAREDE QUE RACHA TAMBEM DEIXA DE BLOQUEAR -- ela caiu. Chao livre so muda de cara.
				if (mapa.BlockedCell(cx, cy)) mapa.Abrir(cx, cy);

				MandarCelulaCaida(zona, cx, cy);
			}
	}

	/// <summary>Quantas celulas o `--quebrarteste N` derruba no nascimento. 0 = desligado.</summary>
	private int _quebrarDeTeste;

	/// <summary>
	/// BANCADA: derruba cenario em volta de quem nasceu, sem precisar de briga.
	///
	/// Usa o MESMO <see cref="RacharChao"/> do combate -- e nao um caminho proprio -- porque um
	/// atalho de teste que nao passa pelo codigo de verdade testa o atalho. A forca vai alta de
	/// proposito (a resistencia do cenario e 20) e o raio cresce ate juntar o tanto pedido.
	/// </summary>
	private void QuebrarCenarioDeTeste(ServerPlayer pl)
	{
		const int T = ZoneCollision.TileSize;
		_cenarioCaido.TryGetValue(pl.Zone.Name, out HashSet<(int X, int Y)>? antes);
		int alvo = (antes?.Count ?? 0) + _quebrarDeTeste;

		for (int anel = 0; anel < 12; anel++)
		{
			for (int dy = -anel; dy <= anel; dy++)
				for (int dx = -anel; dx <= anel; dx++)
				{
					if (Math.Abs(dx) != anel && Math.Abs(dy) != anel) continue;   // so a casca
					RacharChao(pl.Zone, pl.Pos + new Vec2(dx * T, dy * T), 1_000_000);
				}
			if (_cenarioCaido.TryGetValue(pl.Zone.Name, out HashSet<(int X, int Y)>? agora)
				&& agora.Count >= alvo) break;
		}

		int quantas = _cenarioCaido.TryGetValue(pl.Zone.Name, out HashSet<(int X, int Y)>? fim) ? fim.Count : 0;
		Godot.GD.Print($"[server] BANCADA: {quantas} celula(s) de cenario quebradas em {pl.Zone.Name}");

		MartelarABorda(pl.Zone);
	}

	/// <summary>
	/// BANCADA: bate com forca de sobra NOS QUATRO CANTOS do mapa e confere que NADA caiu.
	///
	/// ============================ POR QUE ISTO PRECISA DE TESTE ============================
	/// O dono fotografou o personagem no limite do mundo com uma mancha de chao quebrado ao lado.
	/// A guarda que existia olhava so o GRUPO da celula (`BordaDoMundo`), que o conversor marca
	/// apenas no `/turf/Other/Blank` -- um mapa que termina em AGUA nao tem nada marcado na ultima
	/// coluna, e a beirada do oceano racha como qualquer outro chao.
	///
	/// A regra nova (`ZoneCollision.NaBorda`) e aritmetica e nao depende de plano nenhum. Mas uma
	/// regra escrita nao e uma regra ligada -- e a coisa que este projeto mais erra. Aqui ela e
	/// EXERCIDA: quatro cantos, forca de um milhao, e a conta antes e depois.
	/// ======================================================================================
	/// </summary>
	private void MartelarABorda(ZoneKey zona)
	{
		if (_catalogo?.Get(zona)?.Mapa is not { } mapa) return;
		const int T = ZoneCollision.TileSize;

		_cenarioCaido.TryGetValue(zona.Name, out HashSet<(int X, int Y)>? antes);
		int comecou = antes?.Count ?? 0;

		foreach ((int cx, int cy) in new[]
		{
			(0, 0), (mapa.Width - 1, 0), (0, mapa.Height - 1), (mapa.Width - 1, mapa.Height - 1),
			(mapa.Width / 2, 0), (mapa.Width / 2, mapa.Height - 1),
			(0, mapa.Height / 2), (mapa.Width - 1, mapa.Height / 2),
		})
		{
			var onde = new Vec2(cx * T + T / 2f, cy * T + T / 2f);
			RacharChao(zona, onde, 1_000_000);
			DerrubarCenario(zona, onde);
		}

		int agora = _cenarioCaido.TryGetValue(zona.Name, out HashSet<(int X, int Y)>? fim2) ? fim2.Count : 0;
		// O RELATO NAO FALA EM "AGUENTAR". A borda nao tem resistencia nem contador: a destruicao
		// RECUSA antes de olhar forca (ver `NaBorda` em `RacharChao` e `DerrubarCenario`). As oito
		// marteladas sao do TESTE, e nao um limite -- oitocentas dariam o mesmo zero.
		Godot.GD.Print(agora == comecou
			? $"[server] BANCADA: a borda de {zona.Name} ({mapa.Width}x{mapa.Height}) e INQUEBRAVEL -- "
			  + "a destruicao recusa a beirada antes de olhar forca (8 tentativas, 0 celulas)"
			: $"[server] BANCADA: FALHA -- a borda de {zona.Name} cedeu em {agora - comecou} celula(s)");
	}

	/// <summary>Manda pra quem chega a lista do que ja caiu na zona.</summary>
	private void MandarCenario(ServerPlayer pl)
	{
		if (!_cenarioCaido.TryGetValue(pl.Zone.Name, out HashSet<(int X, int Y)>? caidas)) return;
		foreach ((int cx, int cy) in caidas) pl.Peer?.Send(PacoteDeCenario(false, cx, cy),
			Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O PACOTE DE CENARIO, num lugar so.
	///
	/// Ele tem tres escritores (a parede que cai, o chao que racha, e a lista que vai pra quem
	/// chega) e ganhou um campo novo -- o `limpar`. Tres copias da mesma escrita sao tres chances
	/// de uma delas ficar pra tras quando o formato muda, e foi por isso que virou funcao.
	/// </summary>
	private static NetDataWriter PacoteDeCenario(bool limpar, int cx, int cy, ulong zona = 0)
	{
		var w = Protocol.Begin(Protocol.S2C.Cenario);
		w.Put(limpar);
		w.Put((ushort)cx);
		w.Put((ushort)cy);
		// A ZONA SO VIAJA NA LIMPEZA. Uma celula que cai e sempre da zona em que quem recebe esta
		// -- o pacote so sai pra `ZoneList` dela. A limpeza, nao: ela vai pra TODO MUNDO, porque
		// quem esta noutro planeta guardou a cena suja no cache e precisa jogar fora aquela copia.
		if (limpar) w.Put(zona);
		return w;
	}

	private void MandarCelulaCaida(ZoneKey zona, int cx, int cy)
	{
		NetDataWriter w = PacoteDeCenario(false, cx, cy);
		foreach (ServerPlayer o in ZoneList(zona.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// "ESQUECA TODO O ESTRAGO DESTA ZONA" -- o verb de admin que refaz o cenario.
	///
	/// O cliente nao tem como DESFAZER celula por celula: ele apagou as camadas do tilemap e
	/// escreveu terra batida por cima, e nao guardou o que havia antes. Por isso a ordem e
	/// grossa: zera a lista, devolve a colisao ao que o arquivo diz, e recarrega a cena do disco.
	/// </summary>
	private void MandarLimpezaDeCenario(ZoneKey zona)
	{
		// TODO MUNDO, e nao so quem esta na zona.
		//
		// O cliente guarda ate duas cenas de zona vivas no cache (`_zonasVivas`). Quem viu paredes
		// cairem na Terra e viajou pra Namek levou a cena da Terra SUJA junto -- com as celulas
		// apagadas no tilemap. Se a limpeza so fosse pra quem esta na zona, essa pessoa voltaria
		// pra Terra, o cache acertaria, e ela veria o buraco que ja nao existe mais pra ninguem.
		//
		// O pacote e minusculo e isto acontece uma vez, quando um admin manda refazer.
		NetDataWriter w = PacoteDeCenario(true, 0, 0, zona.Hash);
		foreach (ServerPlayer o in _players.Values)
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
