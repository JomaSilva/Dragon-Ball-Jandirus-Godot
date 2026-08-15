using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--tresteste` -- OS TRES RELATOS DO DONO, MEDIDOS.
///
/// ============================ O QUE O DONO DISSE, PALAVRA POR PALAVRA ============================
///   A. *"sempre q ele usa o SOCO FORTE, e eu to batendo nele, por uns segundos (1 ou 2) MEUS SOCOS
///      N ACERTAM ELE enquanto ele ta no sprite de socar. n sei se e pq to dando MISS, mas sei q N E
///      DODGE pq ele n da o efeito de dodge e o jogo n avisa q ele desviou"*;
///   B. *"os NPCs N USAM O DASH, eles so andam"*;
///   C. *"n tive um ZANZOCLASH com meu clone, acho q ele N PEGA MINHAS SKILLS"*.
/// ==================================================================================================
///
/// ============================ E O QUE A MEDICAO ANTERIOR JA TINHA DESCARTADO ============================
/// A hipotese forte era o `CombatState.Intocavel` tirando o alvo da BUSCA. **Nao era**: os unicos
/// escritores dele sao a carencia de renascimento (`Reviver`) e a cinematica de forma (`MarcarCena`),
/// e nenhum dos dois esta num caminho de ataque. Miss de pontaria tambem nao era: pontaria que falha
/// vira `Desfecho.Esquivou`, que o cliente DESENHA e ANUNCIA -- e o dono diz que nao ve nada disso.
///
/// O que sobrou foi GEOMETRIA, e a familia A mede a causa raiz numero a numero: **todo soco pesado
/// que encosta chama o Impact sem sorteio** (`attack cmn.dm:110-118` -- o leve depende de `prob`, o
/// pesado cai direto no `else`), o corpo voa por 0,8-0,9 s, e durante esse voo o port **ainda
/// aceitava socar**. O DM nao aceita: o efeito de knockback escreve `canfight -= 1`
/// (`Movement Effects.dm:40`) e o `testAttack()` recusa (`attack_bck.dm:175`).
/// ==========================================================================================================
///
/// ============================ O QUE ELA TENTA REPROVAR ============================
///  A1. O SOCO NO VOO -- um corpo arremessado nao ataca, e a recusa e a do DM. **Com a armadilha
///      armada** (o gancho retirado) o corpo volta a socar, que e o comportamento que produziu o
///      relato -- se a armadilha ficar verde nos dois lados, o conserto nao esta ligado em nada.
///  A2. O ARRANQUE ATE O ALVO MARCADO -- quinze tiles (`attack_bck.dm:78`), e SO com marca e SO no
///      golpe pesado. Sem marca continua valendo o cone curto de cinco tiles.
///  B.  A INVESTIDA DA IA -- o cerebro pede golpe de LONGE (e pesado, senao o arranque nao alcanca).
///      Com a manivela em zero -- o estado anterior -- ele nao pede NENHUM, que e o "eles so andam".
///  C.  O REFLEXO NO EMBATE -- o clone herda a Imagem Remanescente (o `C.haszanzo = M.haszanzo` do
///      `MindMeditate.dm:276`) e **so** ela: o livro dele nao pode virar o livro do dono, porque o BP
///      dele ja e o EXPRESSO (forma inclusa) e uma forma por cima contaria duas vezes.
/// ==================================================================================
///
/// ============================ E A SEGUNDA CAMADA: OS QUATRO PEDIDOS, COM **DOIS CORPOS** ============================
/// As quatro familias acima medem a PECA -- um gancho, um alcance, uma manivela, uma condicao --, e
/// isso deixa um vao inteiro de fora: **peca boa e jogo quebrado nao se distinguem por elas**. Um
/// gancho instalado que ninguem consulta, um alcance certo que o cerebro nunca pede, uma condicao que
/// fecha e um embate que mesmo assim nunca acontece: os quatro passariam de verde.
///
/// Entao vieram quatro familias novas, e cada uma e o pedido do dono na moeda dele -- dois corpos, no
/// funil de producao, com o defeito INJETADO ao lado:
///
///   1. TROCA LONGA        -> quarenta socos contra alguem que esta **no sprite de socar**, e nenhum
///                            deles cai no vazio / o contra-exemplo: quem esta DE FATO intocavel
///                            (carencia de renascimento) continua protegido, e a busca o pula
///   2. PERSEGUICAO        -> o cacador INVESTE numa perseguicao de verdade, contada em tempo real
///                            (a recarga do arranque e relogio de parede) / o contra-exemplo: parado
///                            e colado, ele soca e **nao** arranca
///   3. A SKILL DO REFLEXO -> ela tem NOME (`/datum/skill/ki/Afterimage`), e sem ela a mesma troca
///                            simultanea entre o dono e o reflexo **nao vira embate**
///   4. O EMBATE ACONTECE  -> o Zanzo Clash roda do comeco ao fim contra o reflexo, e ele APERTA as
///                            letras / o contra-exemplo: uma reacao lenta demais o devolve a estatua
///
/// ============================ POR QUE ELA GASTA ~15 SEGUNDOS DE RELOGIO DE PAREDE ============================
/// Duas coisas deste lote nao correm por `dt`: a **recarga do arranque** (500 ms, `NowMs()`) e a
/// **janela da letra** do quick time event (900 ms, `NowMs()`). Num laco sincrono -- que e como as
/// outras bancadas rodam -- 900 tiques passam em milissegundos de parede, e o cacador so investiria
/// UMA vez e o reflexo so receberia UMA letra: a medida seria do laco, e nao do jogo. Por isso as
/// familias 2 e 4 rodam a 30 Hz **no relogio de verdade**, que e o unico jeito de a conta "quantas
/// investidas por segundo" e "quantas letras ele respondeu" querer dizer alguma coisa.
///
/// ============================ O QUE ELA MEXE NO MUNDO ============================
/// Golpe pesado RACHA O CHAO e o embate ARRASA o que bloqueia em volta (`Arrasar`), entao esta
/// bancada abre algumas celulas da Terra -- em memoria, no `_cenarioCaido`, que nao vai pro disco e
/// morre no proximo boot. E o mesmo efeito colateral que a `--clashteste` ja tem, e ele so acontece
/// com a flag.
/// ==========================================================================================================
/// </summary>
public partial class GameServer
{
	private int _tresOk, _tresFalhou;

	private void AfirmarTres(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _tresOk++; GD.Print($"[tres]   OK    {oque}"); return; }
		_tresFalhou++;
		GD.PrintErr($"[tres]   FALHA {oque}   {detalhe}");
	}

	/// <summary>Zera os relogios de quem vai socar de novo -- recarga, pose e recarga do arranque.</summary>
	private static void PronroParaOutroGolpe(ServerPlayer p)
	{
		p.Combate.Recarga = 0;
		p.Combate.Stun = 0;
		p.AtaqueAte = 0;
		p.DashLivreEm = 0;
		p.Ficha.Ki = p.Ficha.MaxKi;
	}

	public void RodarBancadaDosTresRelatos()
	{
		_tresOk = _tresFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[tres] ================ OS TRES RELATOS DO DONO ================");
		AfirmarTres("a zona da bancada tem colisao carregada", _pjMapa != null);

		// ---- A CAMADA DA PECA: numeros, fronteiras e as duas armadilhas ----
		FamiliaSocoNoVoo();
		FamiliaArranqueAteOMarcado();
		FamiliaInvestidaDaIa();
		FamiliaReflexoNoEmbate();

		// ---- A CAMADA DO JOGO: os quatro pedidos, com DOIS CORPOS e o defeito injetado ----
		FamiliaTrocaLonga();
		FamiliaPerseguicaoDeVerdade();
		FamiliaSkillDoReflexo();
		FamiliaOEmbateAcontece();

		LimparTudoDaBancada();
		GD.Print($"[tres] ===== FIM: {_tresOk} ok, {_tresFalhou} falha(s) =====");
	}

	// =====================================================================
	// A1 -- O SOCO DURANTE O ARREMESSO
	// =====================================================================
	/// <summary>
	/// A CAUSA RAIZ DO RELATO A, medida dos dois lados: o soco pesado ARREMESSA sempre, e o corpo
	/// arremessado nao pode socar.
	///
	/// A ARMADILHA E O CORACAO DESTA FAMILIA. Retirar o gancho <see cref="CombatState.SendoArremessado"/>
	/// reproduz **exatamente** o jogo que o dono jogou: o corpo no ar aceita o golpe, o golpe nao acha
	/// ninguem (o inimigo ficou centenas de pixels pra tras) e sai um soco no ar sem dodge e sem
	/// mensagem. Se as duas afirmacoes -- com e sem gancho -- ficarem verdes, o conserto nao esta
	/// ligado a nada.
	/// </summary>
	private void FamiliaSocoNoVoo()
	{
		GD.Print("[tres] ---- A1: o soco durante o arremesso (`canfight -= 1` do DM) ----");

		Vec2 onde = CorredorLivre(24);
		ServerPlayer bate = Forjar("TresBate", onde, 5_000);
		ServerPlayer leva = Forjar("TresLeva", onde + new Vec2(28, 0), 5_000);
		bate.Facing = Facing.East;
		leva.Facing = Facing.West;

		Atacar(bate, Protocol.Golpe.Pesado);

		AfirmarTres("o soco PESADO que encosta arremessa sempre (o `else` do `attack cmn.dm:118`)",
					leva.TiquesDeVoo > 0, $"TiquesDeVoo={leva.TiquesDeVoo}");

		// ============================ AS OUTRAS QUATRO RECUSAS SAEM DA FRENTE ============================
		// `PodeAtacar()` diz nao por CINCO motivos (morte, nocaute, atordoamento, recarga e o voo), e o
		// par de afirmacoes abaixo so quer dizer alguma coisa sobre o QUINTO. O golpe que acabou de
		// arremessar tambem deixou recarga, e as vezes NOCAUTEIA -- e ai a recusa fica verde pelo motivo
		// errado e a ARMADILHA fica **vermelha** pelo motivo errado.
		//
		// **NAO E TEORIA**: rodando esta bancada cinco vezes seguidas, a quarta reprovou exatamente aqui.
		// Uma armadilha que dispara de vez em quando nao e uma armadilha, e mede o dado.
		// ==========================================================================================
		if (leva.Ficha.KO) leva.Combate.Levantar();
		leva.Combate.Recarga = 0;
		leva.Combate.Stun = 0;

		// ---- a recusa ----
		AfirmarTres("...e o corpo no ar NAO PODE ATACAR (`canfight -= 1`, `Movement Effects.dm:40`)",
					!leva.Combate.PodeAtacar());

		// ---- A ARMADILHA: sem o gancho, o jogo de antes volta ----
		Func<bool>? guardado = leva.Combate.SendoArremessado;
		leva.Combate.SendoArremessado = null;
		bool socavaAntes = leva.Combate.PodeAtacar();
		leva.Combate.SendoArremessado = guardado;
		AfirmarTres("ARMADILHA ARMADA: SEM o gancho o mesmo corpo, no mesmo voo, aceita socar -- "
					+ "e esse era o jogo que o dono jogou", socavaAntes);

		// ---- e o golpe pedido no meio do voo nao produz nada: nem recarga, nem pose ----
		PronroParaOutroGolpe(leva);
		leva.Combate.Recarga = 0;
		Atacar(leva, Protocol.Golpe.Leve);
		AfirmarTres("...e pedir o golpe no meio do voo nao consome recarga nem arma a pose "
					+ "(o `Atacar` sai na primeira linha)",
					leva.Combate.Recarga <= 0 && leva.AtaqueAte <= 0,
					$"recarga={leva.Combate.Recarga:0.###} pose={leva.AtaqueAte}");

		// ---- quanto ele voa, e por quanto tempo ----
		Vec2 partiu = leva.Pos;
		int tiquesDeServidor = 0;
		while (leva.TiquesDeVoo > 0 && tiquesDeServidor < 600) { TickDoEmpurrao(); tiquesDeServidor++; }
		float voou = (leva.Pos - partiu).Length;
		double segundos = tiquesDeServidor * Protocol.TickSeconds;
		GD.Print($"[tres]        MEDIDO: o corpo voou {voou:0} px em {segundos:0.00} s "
				 + $"({voou / ZoneCollision.TileSize:0.#} tiles) -- e o vao que o soco de "
				 + $"{CombatKnobs.Alcance:0} px tem que atravessar de volta");

		AfirmarTres("o arremesso dura de 0,5 a 1,5 s -- o \"uns segundos (1 ou 2)\" do relato",
					segundos is > 0.4 and < 1.6, $"{segundos:0.00} s");
		AfirmarTres("...e joga o corpo MUITO alem do alcance do soco",
					voou > CombatKnobs.Alcance * 3, $"{voou:0} px contra {CombatKnobs.Alcance:0}");

		// ---- acabado o voo, ele volta a socar ----
		PronroParaOutroGolpe(leva);
		AfirmarTres("acabado o voo o mesmo corpo volta a poder atacar -- a recusa e do VOO e nao "
					+ "um estado que ficou pra tras", leva.Combate.PodeAtacar());

		LimparTudoDaBancada();
	}

	// =====================================================================
	// A2 -- O ARRANQUE ATE O ALVO MARCADO
	// =====================================================================
	/// <summary>
	/// O CONSERTO DO RELATO A: com o alvo MARCADO, o arranque longo alcanca os quinze tiles do
	/// `attack_bck.dm:78` -- que e o que da ao jogador o gesto de voltar pra briga depois de ser
	/// arremessado.
	///
	/// AS QUATRO AFIRMACOES SAO AS QUATRO FRONTEIRAS, e cada uma existe pra impedir um exagero:
	/// sem marca continua curto (nao se arranca em cima de quem passou no canto), com marca alcanca,
	/// alem dos quinze tiles nao alcanca, e o golpe LEVE nao herda a esticada (o dono ja reclamou de
	/// investida grande demais uma vez).
	/// </summary>
	private void FamiliaArranqueAteOMarcado()
	{
		GD.Print("[tres] ---- A2: o arranque ate o alvo MARCADO (15 tiles) ----");

		Vec2 onde = CorredorLivre(24);

		float Percorrido(float distancia, bool marcando, Protocol.Golpe golpe)
		{
			ServerPlayer a = Forjar("TresDashA", onde, 5_000);
			ServerPlayer b = Forjar("TresDashB", onde + new Vec2(distancia, 0), 5_000);
			a.Facing = Facing.East;
			b.Facing = Facing.West;
			if (marcando) Mirar(a, b.Id);
			PronroParaOutroGolpe(a);

			Vec2 partiu = a.Pos;
			Atacar(a, golpe);
			float andou = (a.Pos - partiu).Length;

			LimparTudoDaBancada();
			return andou;
		}

		float semMarca = Percorrido(300, marcando: false, Protocol.Golpe.Pesado);
		AfirmarTres("SEM marca, um alvo a 300 px (9 tiles) esta fora do arranque -- o cone continua "
					+ "valendo cinco tiles", semMarca < 1f, $"andou {semMarca:0} px");

		float comMarca = Percorrido(300, marcando: true, Protocol.Golpe.Pesado);
		AfirmarTres("COM marca, o mesmo alvo a 300 px puxa a investida ate um tile dele",
					comMarca > 250f, $"andou {comMarca:0} px");

		float longeDemais = Percorrido(600, marcando: true, Protocol.Golpe.Pesado);
		AfirmarTres("...e a 600 px (18 tiles) nem a marca vale: os quinze tiles do DM sao um LIMITE",
					longeDemais < 1f, $"andou {longeDemais:0} px");

		float leveComMarca = Percorrido(300, marcando: true, Protocol.Golpe.Leve);
		AfirmarTres("...e o golpe LEVE nao herda a esticada -- o passo curto continua sendo passo curto",
					leveComMarca < 1f, $"andou {leveComMarca:0} px");
	}

	// =====================================================================
	// B -- A INVESTIDA DA IA
	// =====================================================================
	/// <summary>
	/// O RELATO B, medido no CEREBRO PURO -- e de proposito: o conserto e inteiramente uma decisao
	/// (*quando pedir o golpe*), e quem executa a investida e o `Aproximar`, que a familia A2 acabou
	/// de medir com o mesmo corpo do jogador. Medir as duas coisas juntas confundiria "ele nao decide"
	/// com "ele decide e nao consegue".
	/// </summary>
	private void FamiliaInvestidaDaIa()
	{
		GD.Print("[tres] ---- B: a IA investe em vez de so andar ----");

		(int pesados, int leves) Sondar(float distancia, float alcanceDaInvestida)
		{
			var c = new Cerebro { AlcanceDaInvestida = alcanceDaInvestida };
			var rng = new Random(20260815);
			int pesados = 0, leves = 0;

			for (int t = 0; t < 600; t++)
			{
				var p = new Percepcao
				{
					TemAlvo = true, IdDoAlvo = 4242,
					Minha = Vec2.Zero, DoAlvo = new Vec2(distancia, 0),
					VidaFrac = 1, KiFrac = 1, FolegoFrac = 1,
					VidaDoAlvo = 1, MeuPoder = 5_000, PoderDoAlvo = 5_000,
				};
				Comando cm = c.Pensar(p, Protocol.TickSeconds, rng);
				if (cm.Pesado) pesados++;
				else if (cm.Leve) leves++;
			}
			return (pesados, leves);
		}

		// COLADO: o comportamento de sempre, que nao pode ter mudado.
		(int pPerto, int lPerto) = Sondar(40, new Cerebro().AlcanceDaInvestida);
		AfirmarTres("colado, o cerebro continua socando como sempre socou", pPerto + lPerto > 0,
					$"{pPerto} pesados, {lPerto} leves");

		// A ARMADILHA: com a manivela em zero, o estado ANTERIOR -- 150 px e terra de ninguem.
		(int pAntes, int lAntes) = Sondar(150, 0f);
		AfirmarTres("ARMADILHA ARMADA: com a manivela em ZERO -- o cerebro de antes -- um alvo a 150 px "
					+ "nao arranca golpe NENHUM. Era o \"eles so andam\"", pAntes + lAntes == 0,
					$"{pAntes} pesados, {lAntes} leves");

		(int pDepois, int lDepois) = Sondar(150, new Cerebro().AlcanceDaInvestida);
		AfirmarTres("...e com a manivela de fabrica ele INVESTE no mesmo ponto", pDepois > 0,
					$"{pDepois} pesados, {lDepois} leves");
		AfirmarTres("...e a investida sai sempre PESADA -- o arranque longo so existe no pesado",
					lDepois == 0, $"{lDepois} leves escaparam");
		GD.Print($"[tres]        MEDIDO: a 150 px, {pDepois} investidas em 600 tiques (20 s) "
				 + $"-- {pDepois / 20.0:0.0}/s");

		// E A FRONTEIRA: alem do alcance da investida ele volta a so andar.
		(int pLonge, int lLonge) = Sondar(500, new Cerebro().AlcanceDaInvestida);
		AfirmarTres("alem dos dez tiles do `NPCAI.dm:449` ele nao investe -- a receita tem FIM",
					pLonge + lLonge == 0, $"{pLonge} pesados, {lLonge} leves");
	}

	// =====================================================================
	// C -- O REFLEXO E O EMBATE
	// =====================================================================
	/// <summary>
	/// O RELATO C. O que se mede aqui e a CONDICAO do embate (`a.Livro.Sabe(...) &amp;&amp;
	/// d.Livro.Sabe(...)`), e nao o embate inteiro: quem mede o embate e a `--clashteste`.
	///
	/// E se mede tambem o que **nao** foi herdado, que e a metade que o dono nao pediu e que o codigo
	/// exige: o livro do reflexo tem UMA entrada. Duas seriam a porta pra uma forma entrar num corpo
	/// cujo BP base ja e o expresso do dono -- a forma contada duas vezes.
	/// </summary>
	private void FamiliaReflexoNoEmbate()
	{
		GD.Print("[tres] ---- C: o reflexo da mente no Zanzo Clash ----");

		Vec2 onde = CorredorLivre(8);
		ServerPlayer dono = Forjar("TresDono", onde, 5_000);
		dono.Livro.Dar(PathDoZanzoken);
		dono.Livro.Dar("/datum/skill/ki/Kamehameha");   // uma skill que ele NAO deve repassar

		ServerPlayer reflexo = CriarClone(dono, dono.Zone);

		AfirmarTres("o reflexo herda a Imagem Remanescente (o `C.haszanzo = M.haszanzo` do "
					+ "`MindMeditate.dm:276`)", reflexo.Livro.Sabe(PathDoZanzoken));

		AfirmarTres("...e SO ela: o livro do dono nao foi copiado (o BP do reflexo ja e o EXPRESSO, "
					+ "e uma forma por cima o contaria duas vezes)",
					reflexo.Livro.Aprendidas.Count == 1,
					$"{reflexo.Livro.Aprendidas.Count} entradas");

		AfirmarTres("...e por isso ele continua sem tecnica nenhuma (o Kamehameha do dono nao passou)",
					!reflexo.Livro.Sabe("/datum/skill/ki/Kamehameha"));

		// A CONDICAO DO EMBATE, lida como o `TentarEmbate` a le.
		bool osDoisSabem = dono.Livro?.Sabe(PathDoZanzoken) == true
						&& reflexo.Livro?.Sabe(PathDoZanzoken) == true;
		AfirmarTres("a condicao do Zanzo Clash (`haszanzo && M.haszanzo`) FECHA entre o dono e o "
					+ "reflexo dele -- era isto que nunca acontecia", osDoisSabem);

		// E O DONO SEM A SKILL NAO FABRICA UM REFLEXO COM ELA.
		ServerPlayer cru = Forjar("TresCru", onde + new Vec2(96, 0), 5_000);
		ServerPlayer reflexoCru = CriarClone(cru, cru.Zone);
		AfirmarTres("...e quem NAO tem a skill nao ganha um reflexo que tem (a heranca e do dono, "
					+ "nao um brinde do sistema)", !reflexoCru.Livro.Sabe(PathDoZanzoken)
					&& reflexoCru.Livro.Aprendidas.Count == 0);

		// O CEREBRO DO REFLEXO RESPONDE O QUICK TIME EVENT (senao o embate seria contra uma estatua).
		AfirmarTres("o reflexo tem cerebro -- e ele que aperta as letras do embate por um corpo sem "
					+ "dono (ver `ResponderPelaMaquina`)", reflexo.Cerebro != null);

		foreach (ServerPlayer p in new[] { reflexo, reflexoCru })
		{
			_players.Remove(p.Id);
			ZoneList(p.Zone.Hash).Remove(p);
		}
		LimparTudoDaBancada();
	}

	// =====================================================================
	// AS FERRAMENTAS DA SEGUNDA CAMADA
	// =====================================================================
	/// <summary>
	/// UM TIQUE DE MUNDO, na ordem do <see cref="Tick"/> de producao -- e so as quatro linhas de que
	/// este lote precisa, nenhuma inventada.
	///
	/// A ORDEM E A REGRA e nao arrumacao: a grade de colisao e montada ANTES de qualquer um andar
	/// (senao uns leem o quadro de agora e outros o quadro passado), o combate anda antes de a IA
	/// pensar (senao ela decide sobre a recarga do tique que ja passou) e o arremesso vem por ultimo,
	/// que e onde ele esta la.
	/// </summary>
	private void UmTiqueDeMundoDaBancada()
	{
		MontarAsGrades();
		TickCombate(Protocol.TickSeconds);
		TickDosCorposSemDono(Protocol.TickSeconds);
		TickDoEmpurrao();
	}

	/// <summary>
	/// RODA O MUNDO A 30 Hz NO RELOGIO DE PAREDE, pelo tempo pedido.
	///
	/// ============================ POR QUE NAO UM LACO SINCRONO ============================
	/// Porque as duas coisas que estas familias contam sao carimbadas com <see cref="NowMs"/> e nao
	/// abatidas por `dt`: a recarga do arranque (`RecargaDashMs`, 500 ms) e o prazo da letra do embate
	/// (`MsPorTecla`, 900 ms). Num laco sincrono o relogio de parede nao anda, entao o arranque fica
	/// travado depois do primeiro e a letra nunca vence -- e a bancada mediria **o laco**, dando "1
	/// investida" e "1 letra" com o jogo perfeito ou quebrado, indistintamente.
	///
	/// E o mesmo erro de unidade que este port ja registra duas vezes (o `sleep()` do BYOND lido como
	/// N/12, o passeio do habitante lido no relogio de parede em vez do relogio do mundo): quando o
	/// sistema medido conta numa moeda, a bancada tem que pagar naquela moeda.
	/// ==================================================================================
	/// </summary>
	private int RodarEmTempoReal(double segundos, Action? antesDoTique = null, Action? depoisDoTique = null)
	{
		long fim = NowMs() + (long)(segundos * 1000);
		long proximo = NowMs();
		int tiques = 0;

		while (NowMs() < fim)
		{
			if (NowMs() < proximo) { System.Threading.Thread.Sleep(1); continue; }
			proximo = NowMs() + (long)(Protocol.TickSeconds * 1000);

			antesDoTique?.Invoke();
			UmTiqueDeMundoDaBancada();
			depoisDoTique?.Invoke();
			tiques++;
		}
		return tiques;
	}

	// =====================================================================
	// 1 -- A TROCA LONGA: SOCAR QUEM ESTA SOCANDO **ACERTA**
	// =====================================================================
	/// <summary>
	/// O RELATO A, LITERAL: *"sempre q ele usa o SOCO FORTE, e eu to batendo nele, por uns segundos
	/// MEUS SOCOS N ACERTAM ELE **enquanto ele ta no sprite de socar**"*.
	///
	/// ============================ O QUE E "ESTAR NO SPRITE DE SOCAR", MEDIDO ============================
	/// Duas coisas que o `Atacar` escreve na LINHA 396, antes de procurar alvo nenhum:
	/// <see cref="ServerPlayer.AtaqueAte"/> no futuro (e o campo que o cliente le pra desenhar a pose) e
	/// <see cref="CombatState.Recarga"/> acima de zero. A familia arma esse estado quarenta vezes e soca
	/// dentro dele quarenta vezes.
	///
	/// **ELE SOCA O AR, DE COSTAS, DE PROPOSITO.** O que o relato acusa e o ESTADO de quem esta socando,
	/// nao o desfecho do golpe dele -- e um golpe que ACERTA me arremessa, e ai eu deixo de poder socar
	/// (a familia A1 e sobre isso) e o vao ate ele deixa de ser um vao de soco. Misturar as duas coisas
	/// mediria a geometria de novo e deixaria a pergunta desta familia sem resposta.
	///
	/// ============================ COMO ELA REPROVA ============================
	///   * `emPose &lt; 40`   -- o corpo nao estava socando quando eu medi: a familia inteira e vazia, e
	///                          esta afirmacao existe pra que "0 de 0" nao possa passar por 100%;
	///   * `achou &lt; 40`    -- houve uma janela em que ele estava na minha frente e a busca nao o
	///                          devolveu. E EXATAMENTE o defeito relatado;
	///   * `vazio &gt; 0`     -- o golpe saiu pelo ramo do vazio (`SocarCenario`/`AnunciarSocoNoAr`), que
	///                          e o "animacao e som de soco no ar, sem dodge e sem mensagem".
	///
	/// E o DEFEITO INJETADO e a hipotese do relato posta de pe: fazer o corpo em pose responder
	/// `Intocavel`. O mesmo criterio fica vermelho -- ou seja, se um dia alguem ligar `Intocavel` num
	/// caminho de ataque, esta linha pega.
	/// ======================================================================
	/// </summary>
	private void FamiliaTrocaLonga()
	{
		GD.Print("[tres] ---- 1: socar quem esta socando ACERTA (troca longa, dois corpos) ----");

		Vec2 onde = CorredorLivre(24);
		ServerPlayer eu = Forjar("TresEu", onde, 5_000);
		ServerPlayer ele = Forjar("TresEle", onde + new Vec2(28, 0), 5_000);

		const int Voltas = 40;
		int emPose = 0, achou = 0, resolveu = 0, vazio = 0, encostou = 0, defendeu = 0;

		for (int v = 0; v < Voltas; v++)
		{
			// ---- ELE SOCA: a pose e a recarga ficam armadas ----
			ele.Pos = onde + new Vec2(28, 0);
			ele.Facing = Facing.East;          // de costas pra mim: eu fico fora do cone dele
			if (ele.Ficha.KO) ele.Combate.Levantar();
			ele.Combate.Recarga = 0;
			ele.Combate.Stun = 0;
			ele.AtaqueAte = 0;
			ele.Ficha.Ki = ele.Ficha.MaxKi;
			Atacar(ele, Protocol.Golpe.Pesado);

			if (ele.AtaqueAte > NowMs() && ele.Combate.Recarga > 0) emPose++;

			// ---- EU SOCO ELE, DENTRO DESSA POSE ----
			eu.Pos = ele.Pos - new Vec2(28, 0);
			eu.Facing = Facing.East;
			PronroParaOutroGolpe(eu);
			eu.UltimoAlvo = 0;

			if (AlvoNaFrente(eu) == ele) achou++;

			double vidaAntes = ele.Combate.Corpo.Vida();
			Atacar(eu, Protocol.Golpe.Leve);

			// `UltimoAlvo` E O CARIMBO DE "ESTE GOLPE TEVE ALVO": o `Atacar` so o escreve depois de o
			// resolvedor rodar, e o ramo do vazio nem chega la. E o unico observavel que separa "acertou"
			// de "nao achou ninguem" sem que a bancada precise de um gancho proprio dentro do combate.
			if (eu.UltimoAlvo == ele.Id)
			{
				resolveu++;
				if (ele.Combate.Corpo.Vida() < vidaAntes - 1e-9) encostou++; else defendeu++;
			}
			else vazio++;

			// o meu golpe pode te-lo arremessado: deixa o voo acabar (senao a volta seguinte mediria
			// um corpo que nao pode socar, e a pose nao se armaria)
			int giros = 0;
			while (ele.TiquesDeVoo > 0 && giros++ < 300) TickDoEmpurrao();
			foreach (ServerPlayer p in new[] { eu, ele })
			{
				if (p.Ficha.KO) p.Combate.Levantar();
				p.Combate.Corpo.Restaurar();
				p.Combate.SincronizarVida();
			}
		}

		AfirmarTres($"o corpo estava DE FATO no sprite de socar nas {Voltas} voltas (pose armada + "
					+ "recarga correndo) -- sem isto a familia mediria o nada", emPose == Voltas,
					$"{emPose}/{Voltas}");
		AfirmarTres($"e a busca o ACHOU nas {Voltas} -- nao ha uma janela de \"nao achou alvo\" "
					+ "enquanto ele soca", achou == Voltas, $"{achou}/{Voltas}");
		AfirmarTres("e nenhum dos socos caiu no ramo do vazio (o soco no ar sem dodge e sem mensagem "
					+ "do relato)", vazio == 0 && resolveu == Voltas, $"{resolveu} resolvidos, {vazio} no vazio");
		GD.Print($"[tres]        MEDIDO: {resolveu} socos resolvidos contra ele -- {encostou} encostaram, "
				 + $"{defendeu} foram aparados ou desviados, {vazio} no vazio");

		// ---- O CONTRA-EXEMPLO: quem esta DE FATO intocavel continua protegido ----
		// ============================ OS DOIS VOLTAM PRO LUGAR **ANTES** DE MEDIR ============================
		// **A SEGUNDA RODADA DESTA BANCADA REPROVOU AQUI, E A PRIMEIRA NAO -- com o mesmo codigo.** O
		// ultimo soco da troca e LEVE, e o arremesso do leve e sorteado (`TentarEmpurrar`): metade das
		// vezes o corpo terminava o laco a 450 px daqui. E ai o contra-exemplo ficava VERDE POR DISTANCIA
		// -- "a busca nao o achou" e verdade sobre alguem que esta longe, e nao diz nada sobre a carencia.
		//
		// E o mesmo defeito de bancada que este projeto ja registra por escrito ("nascer DENTRO do estado
		// nunca testa a ENTRADA nele"): a afirmacao abaixo so quer dizer alguma coisa se, um instante
		// antes, ele ERA alvo. Por isso a posicao e refeita e a pergunta e feita nos dois sentidos.
		// ==============================================================================================
		eu.Pos = onde;
		ele.Pos = onde + new Vec2(28, 0);
		eu.Facing = Facing.East;
		ele.Facing = Facing.West;
		AfirmarTres("com os dois recolocados lado a lado, ele E alvo -- e sem esta linha o contra-exemplo "
					+ "abaixo passaria por distancia em vez de por regra", AlvoNaFrente(eu) == ele);

		ele.Combate.Reviver(1, SegundosDeCarencia);
		AfirmarTres("...mas quem acabou de RENASCER (carencia) sai da busca -- a regra que protege quem "
					+ "levantou continua de pe", AlvoNaFrente(eu) == null);

		PronroParaOutroGolpe(eu);
		eu.UltimoAlvo = 0;
		double vidaDoRenascido = ele.Combate.Corpo.Vida();
		Atacar(eu, Protocol.Golpe.Leve);
		AfirmarTres("...e o golpe nele nao encosta em nada", eu.UltimoAlvo != ele.Id
					&& ele.Combate.Corpo.Vida() >= vidaDoRenascido - 1e-9);

		ele.Combate.Carencia = 0;
		AfirmarTres("...e vencida a carencia ele volta a ser alvo NO MESMO LUGAR (era o relogio, e nao "
					+ "a posicao)", AlvoNaFrente(eu) == ele);

		// ---- O DEFEITO INJETADO: a hipotese do relato, posta de pe ----
		Func<bool>? cenaGuardada = ele.Combate.EmCinematica;
		Mutacao(AfirmarTres,
			"o criterio da familia: com ele na minha frente, a busca do soco o devolve",
			"o corpo em pose de soco passa a responder `Intocavel` -- a hipotese do relato",
			() => { PronroParaOutroGolpe(eu); return AlvoNaFrente(eu) == ele; },
			() => ele.Combate.EmCinematica = () => true,
			() => ele.Combate.EmCinematica = cenaGuardada);

		// ---- E O UNICO VAZIO LEGITIMO: quando ele REALMENTE saiu do alcance ----
		// ATE ARREMESSAR, e nao "um soco e torcer": o pesado que ENCOSTA arremessa sem sorteio, mas
		// encostar depende do resolvedor (pontaria, guarda, esquiva). Amarrar a medida a um unico
		// golpe seria por um dado no meio de uma afirmacao -- o mesmo tipo de fragilidade que a
		// recolocacao acima acabou de consertar.
		int tentativas = 0;
		while (ele.TiquesDeVoo <= 0 && tentativas++ < 6)
		{
			PronroParaOutroGolpe(eu);
			eu.UltimoAlvo = 0;
			Atacar(eu, Protocol.Golpe.Pesado);
		}
		int voltasDoVoo = 0;
		while (ele.TiquesDeVoo > 0 && voltasDoVoo++ < 300) TickDoEmpurrao();
		float vao = (ele.Pos - eu.Pos).Length;
		PronroParaOutroGolpe(eu);
		eu.UltimoAlvo = 0;
		Atacar(eu, Protocol.Golpe.Leve);
		AfirmarTres($"e o soco que SIM cai no vazio e o que sucede o arremesso: ele esta a {vao:0} px, "
					+ $"fora dos {CombatKnobs.Alcance:0} px do punho -- a causa e geometria, e ela e visivel",
					vao > CombatKnobs.Alcance && eu.UltimoAlvo != ele.Id, $"vao {vao:0} px");

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 2 -- A PERSEGUICAO DE VERDADE: A IA INVESTE
	// =====================================================================
	/// <summary>
	/// O RELATO B, LITERAL: *"os NPCs N USAM O DASH, eles so andam"* -- medido no CORPO e nao no
	/// cerebro. A familia B ja provou que ele DECIDE investir; esta prova que a investida ACONTECE,
	/// pelo funil de producao inteiro (`TickDosCorposSemDono` -> `Cerebro.Pensar` -> `AplicarComando`
	/// -> `Atacar` -> `Aproximar`), que e o mesmo caminho do jogador.
	///
	/// ============================ O CONTADOR E `DashLivreEm`, E ELE NAO MENTE ============================
	/// So o <see cref="Aproximar"/> escreve esse campo, e so no `return true` -- o ramo em que houve
	/// deslocamento de verdade. Ele nao e escrito quando falta Ki, quando ha parede no caminho, quando o
	/// alvo esta perto demais nem quando o passo curto so ajeitou a posicao. Contar as MUDANCAS dele e
	/// contar investidas consumadas, e nao intencoes: uma IA que pedisse o golpe e fosse recusada pelo
	/// `Aproximar` deixaria este numero em zero -- que e exatamente o que o dono relatou ver.
	///
	/// ============================ A PRESA MANTEM O VAO, E ESSA E A PERSEGUICAO ============================
	/// Sem isso a medida acaba no primeiro segundo: a primeira investida poe o cacador a um tile do
	/// alvo, e dali pra frente ele so soca -- a bancada contaria "1" com o sistema perfeito. Uma presa
	/// que recua e o que o jogador faz, e e o unico cenario em que a pergunta ("quantas vezes ele
	/// investe?") tem resposta. Ela e curada e reposta de pe a cada tique pelo mesmo motivo: sem isso a
	/// medida do CACADOR viraria a medida de quanto tempo uma vitima passa no chao.
	/// ==============================================================================================
	/// </summary>
	private void FamiliaPerseguicaoDeVerdade()
	{
		GD.Print("[tres] ---- 2: a IA INVESTE numa perseguicao (tempo real, 30 Hz) ----");

		const double Duracao = 6.0;

		(int Investidas, int Golpes, int Tiques, double Cadencia) longe =
			Perseguir(vao: 150f, alcanceDaInvestida: new Cerebro().AlcanceDaInvestida, segundos: Duracao);

		AfirmarTres("perseguindo um alvo a 150 px, o cacador ARRANCA -- e nao so anda",
					longe.Investidas >= 2, $"{longe.Investidas} investidas em {longe.Tiques} tiques");
		AfirmarTres("...e TODO golpe que ele conseguiu dar dali FOI uma investida: a 150 px nao existe "
					+ "soco sem arranque, entao os dois contadores tem que bater",
					longe.Investidas == longe.Golpes, $"{longe.Investidas} investidas x {longe.Golpes} golpes");

		// ============================ O TETO REAL NAO E A RECARGA DO ARRANQUE ============================
		// A calibragem da familia B (a do cerebro puro) mediu 1,7 PEDIDO por segundo e comparou com os
		// 2/s da recarga do arranque. Com o corpo na frente aparece um terceiro numero, e ele e o menor
		// dos tres: a **cadencia do golpe PESADO**, que e `Eactspeed/div * tipo/10` = 1,0 s com os
		// numeros de fabrica. Pedido que chega com a recarga do soco correndo nao vira investida nenhuma
		// -- entao o gargalo de uma investida CONSUMADA e o soco, e nao o arranque nem o sorteio.
		//
		// Vale escrito porque muda o que faria sentido mexer: subir o `ChanceDeInvestir` acima daqui nao
		// poria uma investida a mais na tela, so pedidos recusados.
		// ============================================================================================
		GD.Print($"[tres]        MEDIDO: {longe.Investidas} investidas e {longe.Golpes} golpes em "
				 + $"{longe.Tiques} tiques de 30 Hz ({Duracao:0} s) -- {longe.Investidas / Duracao:0.0}/s. "
				 + $"A cadencia do pesado deste corpo e {longe.Cadencia:0.00} s (teto de "
				 + $"{1 / longe.Cadencia:0.0}/s); a recarga do arranque ({RecargaDashMs} ms) nunca chega "
				 + "a ser o gargalo");
		AfirmarTres("...e ele nao ultrapassa o teto da CADENCIA DO PESADO -- o gargalo verdadeiro da "
					+ "investida consumada", longe.Investidas <= (int)(Duracao / longe.Cadencia) + 1,
					$"{longe.Investidas} em {Duracao:0} s com cadencia {longe.Cadencia:0.00} s");

		// ---- O CONTRA-EXEMPLO: parado e colado, ele soca e NAO arranca ----
		(int Investidas, int Golpes, int Tiques, double Cadencia) colado =
			Perseguir(vao: 34f, alcanceDaInvestida: new Cerebro().AlcanceDaInvestida, segundos: 3.0);

		AfirmarTres("colado no alvo (34 px) ele NAO arranca -- nao ha vao pra atravessar, e investida de "
					+ "dois pixels foi reclamacao do dono uma vez", colado.Investidas == 0,
					$"{colado.Investidas} investidas");
		AfirmarTres("...mas ele continua SOCANDO: o que a receita nova mudou foi o ALCANCE, e nao o soco",
					colado.Golpes > 0, $"{colado.Golpes} golpes");

		// ---- O DEFEITO INJETADO: a manivela em zero e o jogo que o dono jogou ----
		(int Investidas, int Golpes, int Tiques, double Cadencia) antes =
			Perseguir(vao: 150f, alcanceDaInvestida: 0f, segundos: 3.0);

		AfirmarTres("DEFEITO INJETADO (a manivela do alcance em ZERO -- o cerebro de antes): o mesmo "
					+ "cacador, no mesmo vao, nao arranca NENHUMA vez", antes.Investidas == 0,
					$"{antes.Investidas} investidas");
		AfirmarTres("   ...e nem soca: a 150 px ele so anda atras do alvo. Era o \"eles so andam\"",
					antes.Golpes == 0, $"{antes.Golpes} golpes");
	}

	/// <summary>
	/// UMA PERSEGUICAO, em tempo real. Devolve investidas consumadas, golpes pedidos e tiques rodados.
	/// </summary>
	/// <param name="vao">A distancia que a presa MANTEM do cacador, em pixels.</param>
	private (int Investidas, int Golpes, int Tiques, double CadenciaPesada)
		Perseguir(float vao, float alcanceDaInvestida, double segundos)
	{
		// O CORREDOR PRECISA SER LONGO: a cada investida o par ANDA (o cacador chega a um tile da presa
		// e ela recua de novo), entao em tres segundos a briga atravessa uns vinte tiles. Num corredor
		// curto a parede recusaria o arranque -- e a bancada leria isso como "ele nao investe".
		Vec2 onde = CorredorLivre(30);
		ServerPlayer cacador = Forjar("TresCacador", onde, 5_000);
		ServerPlayer presa = Forjar("TresPresa", onde + new Vec2(vao, 0), 5_000);
		cacador.Facing = Facing.East;
		presa.Facing = Facing.West;

		// SEM `Papel` E SEM `DonoDoClone`, ele cai no ramo da FERA do `TicarUmCorpo` -- o corpo que caca
		// o mais proximo, seja quem for. E o ramo que nao depende de molde do `npcs.json` nem de plateia
		// na zona, e o que se quer medir aqui e a decisao de investir, que e a mesma nos tres ramos.
		cacador.Cerebro = new Cerebro { AlcanceDaInvestida = alcanceDaInvestida };

		int investidas = 0, golpes = 0;
		long dashAntes = cacador.DashLivreEm, poseAntes = cacador.AtaqueAte;

		int tiques = RodarEmTempoReal(segundos,
			antesDoTique: () =>
			{
				presa.Pos = cacador.Pos + new Vec2(vao, 0);
				presa.Facing = Facing.West;
				if (presa.Ficha.KO) presa.Combate.Levantar();
				presa.Combate.Corpo.Restaurar();
				presa.Combate.SincronizarVida();
			},
			depoisDoTique: () =>
			{
				if (cacador.DashLivreEm != dashAntes) { investidas++; dashAntes = cacador.DashLivreEm; }
				if (cacador.AtaqueAte != poseAntes) { golpes++; poseAntes = cacador.AtaqueAte; }
			});

		// LIDA DO CORPO E NAO ESCRITA A MAO: e a mesma `CombatMath.Cadencia` que o `Atacar` chama pra
		// escrever a recarga, com o mesmo peso de golpe. Um numero copiado aqui envelheceria calado.
		double cadencia = CombatMath.Cadencia(cacador.Ficha, Protocol.PesoDoGolpe(Protocol.Golpe.Pesado));

		LimparTudoDaBancada();
		return (investidas, golpes, tiques, cadencia);
	}

	// =====================================================================
	// 3 -- A SKILL DO REFLEXO, PELO NOME
	// =====================================================================
	/// <summary>
	/// O RELATO C, LITERAL: *"acho q ele N PEGA MINHAS SKILLS (oq deveria acontecer)"*.
	///
	/// A familia C ja mostra QUE ele herda e o que ele nao herda. Esta responde a outra metade do
	/// pedido -- **nomeie uma, e prove que sem ela o clash nao acontece** -- e ela e a unica das oito
	/// que exercita o <see cref="TentarEmbate"/> de verdade, com dois corpos trocando soco.
	///
	/// A SKILL TEM NOME: `/datum/skill/ki/Afterimage`, a Imagem Remanescente. E a unica entrada do livro
	/// do reflexo, e e a linha `C.haszanzo = M.haszanzo` do `MindMeditate.dm:276`.
	///
	/// ============================ O SORTEIO SAI DE CENA, E SO ELE ============================
	/// `_clashSempre` (a mesma flag da `--clashteste`) tira o `prob(50)` e o piso de Ki. **A condicao da
	/// skill NAO passa por ela** -- `a.Livro?.Sabe(...) != true || d.Livro?.Sabe(...) != true` e conferido
	/// antes e sem excecao --, que e justamente o que faz o defeito injetado poder ficar vermelho: se a
	/// flag apagasse a regra medida, a mutacao passaria e a bancada seria enfeite.
	/// ====================================================================================
	/// </summary>
	private void FamiliaSkillDoReflexo()
	{
		GD.Print("[tres] ---- 3: a skill do reflexo tem NOME, e sem ela nao ha embate ----");

		// O `finally` NAO E CERIMONIA: `--tresteste` roda no boot e o **servidor continua de pe** depois
		// dela. Um `_clashSempre` que vazasse deixaria o jogo daquela sessao inteira com o `prob(50)` do
		// embate desligado -- uma bancada mudando a regra do jogo pra quem esta jogando.
		bool sorteioGuardado = _clashSempre;
		_clashSempre = true;
		try
		{
			Vec2 onde = CorredorLivre(12);
			ServerPlayer dono = Forjar("TresDonoVivo", onde, 5_000);
			dono.Livro.Dar(PathDoZanzoken);
			ServerPlayer reflexo = CriarClone(dono, dono.Zone);

			AfirmarTres($"A SKILL, PELO NOME: o reflexo nasce sabendo `{PathDoZanzoken}` (a Imagem "
						+ "Remanescente do `MindMeditate.dm:276`)", reflexo.Livro.Sabe(PathDoZanzoken));

			Jandirus.Core.Skills.SkillBook livroDoReflexo = reflexo.Livro!;
			Jandirus.Core.Skills.SkillBook livroDoDono = dono.Livro!;

			Mutacao(AfirmarTres,
				"com ela, a troca simultanea entre o dono e o reflexo VIRA Zanzo Clash",
				"o reflexo volta a nascer com o LIVRO VAZIO -- o jogo que o dono jogou",
				() => UmaTrocaSimultanea(dono, reflexo, onde),
				() => reflexo.Livro = new Jandirus.Core.Skills.SkillBook(),
				() => reflexo.Livro = livroDoReflexo);

			Mutacao(AfirmarTres,
				"a mesma troca, medida de novo pelo outro lado",
				"quem perde a skill agora e o DONO -- a condicao do DM e `haszanzo && M.haszanzo`, dos DOIS",
				() => UmaTrocaSimultanea(dono, reflexo, onde),
				() => dono.Livro = new Jandirus.Core.Skills.SkillBook(),
				() => dono.Livro = livroDoDono);

			SoltarDoEmbate(dono.Id);
			_players.Remove(reflexo.Id);
			ZoneList(reflexo.Zone.Hash).Remove(reflexo);
		}
		finally { _clashSempre = sorteioGuardado; }
		LimparTudoDaBancada();
	}

	/// <summary>
	/// UMA TROCA SIMULTANEA entre dois corpos, e a resposta: virou embate?
	///
	/// Ele bate em mim (o `M.lastMeleeFoe == src` do DM), eu bato de volta dentro dos 700 ms da janela
	/// (`zanzoClashWindow`), e o <see cref="TentarEmbate"/> decide. Nada aqui e simulado: sao dois
	/// <see cref="Atacar"/> de producao.
	///
	/// ============================ AS TRES LIMPEZAS, E POR QUE CADA UMA ============================
	///   * o embate anterior e SOLTO -- senao a segunda medida cairia no `_emEmbate.ContainsKey` e a
	///     mutacao ficaria verde pelo motivo errado;
	///   * o `_sorteioLivreEm` e limpo DEPOIS disso, porque `Terminar` acabou de escrever +3 s nele (o
	///     respiro do DM). Medir tres vezes em milissegundos de parede reprovaria por um gate de tempo
	///     que em jogo nunca e o obstaculo;
	///   * o `UltimoAlvo`/`UltimoSocoMs` dos dois e zerado, senao o soco DELE ja acharia uma troca
	///     simultanea pendente da volta anterior e o embate comecaria pela mao errada.
	/// ========================================================================================
	/// </summary>
	private bool UmaTrocaSimultanea(ServerPlayer eu, ServerPlayer ele, Vec2 onde)
	{
		SoltarDoEmbate(eu.Id);
		SoltarDoEmbate(ele.Id);

		// ============================ O EMBATE ANTERIOR TERMINA COM UM CORPO NO AR ============================
		// **ISTO CUSTOU TRES FALHAS NA PRIMEIRA RODADA DESTA BANCADA**, e as tres pareciam ser de
		// coisas diferentes: a segunda mutacao da familia 3 reprovava "antes do defeito", a estatua da
		// familia 4 nunca comecava, e a confirmacao do desfazimento lia lixo. A causa era uma so --
		// `Terminar` desfere o GOLPE DE SAIDA com arremesso **garantido** (`GolpeDeSaida`), e quem esta
		// voando nem soca (`canfight`, a familia A1) nem entra em embate (`!move`, o `Deitado`).
		//
		// Ou seja: a bancada estava medindo o rastro do embate anterior. O voo tem que acabar aqui, e
		// nao depois do primeiro soco -- e o `while` e o mesmo laco de producao do arremesso.
		// ==============================================================================================
		int limpando = 0;
		while ((eu.TiquesDeVoo > 0 || ele.TiquesDeVoo > 0) && limpando++ < 400) TickDoEmpurrao();

		// DEPOIS do `SoltarDoEmbate`, e nao antes: `Terminar` acabou de escrever o respiro de 3 s
		// (`MsDeDescanso`) neste dicionario. Limpar primeiro seria limpar o que ainda ia ser escrito.
		_sorteioLivreEm.Remove(eu.Id);
		_sorteioLivreEm.Remove(ele.Id);

		foreach (ServerPlayer p in new[] { eu, ele })
		{
			if (p.Ficha.KO) p.Combate.Levantar();
			p.Combate.Corpo.Restaurar();
			p.Combate.SincronizarVida();
			p.Combate.Stun = 0;
			p.UltimoAlvo = 0;
			p.UltimoSocoMs = 0;
			p.Ficha.Ki = p.Ficha.MaxKi;
		}

		ele.Pos = onde + new Vec2(28, 0);
		ele.Facing = Facing.West;
		eu.Pos = onde;
		eu.Facing = Facing.East;

		PronroParaOutroGolpe(ele);
		Atacar(ele, Protocol.Golpe.Leve);          // ELE bate em MIM

		// o golpe dele pode ter arremessado um dos dois: quem esta voando nao soca (`canfight`) e nao
		// entra em embate (`!move`), entao a troca so pode ser conferida com os dois no chao de novo
		int giros = 0;
		while ((eu.TiquesDeVoo > 0 || ele.TiquesDeVoo > 0) && giros++ < 300) TickDoEmpurrao();

		ele.Pos = onde + new Vec2(28, 0);
		eu.Pos = onde;
		ele.Facing = Facing.West;
		eu.Facing = Facing.East;
		if (eu.Ficha.KO) eu.Combate.Levantar();
		if (ele.Ficha.KO) ele.Combate.Levantar();

		PronroParaOutroGolpe(eu);
		Atacar(eu, Protocol.Golpe.Leve);           // EU bato de volta, dentro dos 700 ms

		return _emEmbate.ContainsKey(eu.Id) && _emEmbate.ContainsKey(ele.Id);
	}

	// =====================================================================
	// 4 -- O ZANZO CLASH ACONTECE CONTRA O CLONE
	// =====================================================================
	/// <summary>
	/// O PEDIDO LITERAL DO DONO: *"n tive um ZANZOCLASH com meu clone"*. Aqui ele tem.
	///
	/// ============================ FECHAR A CONDICAO NAO E TER O EMBATE ============================
	/// A familia 3 prova que a troca VIRA embate. Isso ainda deixaria o dono com nove cruzamentos
	/// contra uma ESTATUA: o quick time event nasceu entre dois jogadores e nunca tinha encontrado um
	/// corpo sem dono, porque corpo sem dono nunca tinha Imagem Remanescente. Um embate em que um dos
	/// lados nao aperta nada e uma vitoria de graca com um soco pesado de brinde -- e nao e um embate.
	///
	/// Entao esta familia RODA o embate inteiro, a 30 Hz e no relogio de verdade (a letra tem prazo de
	/// 900 ms de parede -- ver <see cref="RodarEmTempoReal"/>), com o dono lendo e apertando a letra
	/// certa, e conta as vezes em que o REFLEXO apertou a dele.
	///
	/// ============================ O CONTADOR E A AGENDA DA MAQUINA ============================
	/// `Embate.RespondeA/B` e o instante em que o corpo sem dono vai apertar; o
	/// <see cref="ResponderPelaMaquina"/> o zera ao apertar e o <see cref="NovaTecla"/> o rearma na
	/// letra seguinte. Contar a transicao ">0 para 0" e contar TECLAS APERTADAS -- inclusive as que ele
	/// errou, que e o que se quer: o que separa um oponente de uma estatua e ele ter jogado, e nao ele
	/// ter ganhado.
	///
	/// ============================ COMO ELA REPROVA ============================
	///   * o embate nao comeca                  -- a familia 3 ja explicaria por que;
	///   * `respostas == 0`                     -- o reflexo e a estatua: o embate roda contra ninguem;
	///   * o embate nao termina no prazo         -- os corpos ficariam invisiveis e atordoados pra sempre;
	///   * `Stun` ou invisibilidade sobrando     -- o embate levou embora um corpo que ele so pegou emprestado.
	///
	/// **O DEFEITO INJETADO E A ESTATUA**, e ele e injetado pelo relogio do proprio cerebro: uma reacao
	/// de 60 s poe o instante de apertar 42 segundos a frente, muito alem dos 900 ms da letra. Nenhuma
	/// linha do embate e tocada -- e por isso a medida vale: e o MESMO codigo, com a maquina lenta
	/// demais pra jogar.
	///
	/// **POR QUE NAO PELO `Mutacao`**: cada avaliacao do criterio aqui custa um embate INTEIRO de
	/// relogio de parede (ate 6,3 s), e o helper avalia tres vezes. Os tres passos dele estao escritos
	/// a mao logo abaixo -- mede, estraga, mede, conserta, confere --, so que o terceiro passo usa o
	/// criterio BARATO (a agenda voltou pra dentro da janela da letra) em vez de um quarto embate.
	/// ======================================================================
	/// </summary>
	private void FamiliaOEmbateAcontece()
	{
		GD.Print("[tres] ---- 4: o ZANZO CLASH acontece contra o clone, e ele JOGA ----");

		// O MESMO `finally` DA FAMILIA 3, e pelo mesmo motivo: o servidor continua de pe depois desta
		// bancada, e uma flag vazada mudaria a regra do embate pra quem estiver jogando nele.
		bool sorteioGuardado = _clashSempre;
		_clashSempre = true;
		try
		{
			Vec2 onde = CorredorLivre(12);
			ServerPlayer dono = Forjar("TresDuelista", onde, 5_000);
			dono.Livro.Dar(PathDoZanzoken);
			ServerPlayer reflexo = CriarClone(dono, dono.Zone);

			// ---- 1. O EMBATE ACONTECE ----
			bool comecou = UmaTrocaSimultanea(dono, reflexo, onde);
			AfirmarTres("O PEDIDO LITERAL: o Zanzo Clash COMECOU entre o dono e o reflexo dele",
						comecou && _emEmbate.ContainsKey(dono.Id));

			(int respostas, int acertos, double ptsMaquina, double ptsDono, double segundos, bool acabou) duelo =
				RodarOEmbate(dono, reflexo, tetoSegundos: 7.0);

			AfirmarTres("...e o reflexo NAO e uma estatua: ele apertou as letras dele, pelo mesmo funil do "
						+ "jogador (`TeclaDoEmbate`)", duelo.respostas >= 2, $"{duelo.respostas} teclas");
			AfirmarTres("...e o embate ACABOU sozinho, no prazo dele", duelo.acabou,
						$"{duelo.segundos:0.0} s sem terminar");
			GD.Print($"[tres]        MEDIDO: {duelo.respostas} teclas apertadas pelo reflexo em "
					 + $"{duelo.segundos:0.0} s, {duelo.acertos} certas (a conta do `PisoDeAcertoDaMaquina` "
					 + $"pro tempero dele e {PisoDeAcertoDaMaquina + (1 - PisoDeAcertoDaMaquina) * reflexo.Cerebro!.Inteligencia:0.00}) "
					 + $"-- placar {duelo.ptsDono:0.#} (dono) x {duelo.ptsMaquina:0.#} (reflexo)");

			AfirmarTres("...e os dois foram DEVOLVIDOS: nenhum ficou invisivel nem preso pelo atordoamento "
						+ "que o embate usou pra travar o corpo",
						!_invisiveis.Contains(dono.Id) && !_invisiveis.Contains(reflexo.Id)
						&& dono.Combate.Stun <= 0 && reflexo.Combate.Stun <= 0,
						$"stun {dono.Combate.Stun:0.##}/{reflexo.Combate.Stun:0.##}");

			// ---- 2. O DEFEITO INJETADO: a estatua ----
			double reacaoGuardada = reflexo.Cerebro!.TempoDeReacao;
			reflexo.Cerebro.TempoDeReacao = 60;

			bool comecouDeNovo = UmaTrocaSimultanea(dono, reflexo, onde);
			(int respostas, int acertos, double ptsMaquina, double ptsDono, double segundos, bool acabou) estatua =
				RodarOEmbate(dono, reflexo, tetoSegundos: 2.2);

			AfirmarTres("DEFEITO INJETADO (a reacao do reflexo 240x mais lenta que a janela da letra): o "
						+ "MESMO embate roda e ele nao aperta NADA -- a estatua",
						comecouDeNovo && estatua.respostas == 0,
						comecouDeNovo ? $"{estatua.respostas} teclas" : "o embate nem comecou");

			SoltarDoEmbate(dono.Id);
			reflexo.Cerebro.TempoDeReacao = reacaoGuardada;

			// ---- 3. E desfeito o defeito, a agenda dele volta pra dentro da janela da letra ----
			bool terceiro = UmaTrocaSimultanea(dono, reflexo, onde);
			long agora = NowMs();
			long quando = _emEmbate.TryGetValue(reflexo.Id, out Embate? e3)
						? (e3.A == reflexo ? e3.RespondeA : e3.RespondeB) : 0;
			AfirmarTres("   ...e desfeito o defeito ele volta a ter hora marcada DENTRO dos 900 ms da letra "
						+ "(era a causa, e nao um estrago que ficou)",
						terceiro && quando > 0 && quando - agora < MsPorTecla,
						$"faltavam {quando - agora} ms pra ele apertar");

			SoltarDoEmbate(dono.Id);
			_players.Remove(reflexo.Id);
			ZoneList(reflexo.Zone.Hash).Remove(reflexo);
		}
		finally { _clashSempre = sorteioGuardado; }
		LimparTudoDaBancada();
	}

	/// <summary>
	/// RODA UM EMBATE JA COMECADO ate o fim (ou ate o teto), com o DONO jogando bem.
	///
	/// O dono aperta a letra certa no primeiro tique em que ela aparece -- ele e o lado "atento", e e
	/// preciso que haja um: um embate em que so um lado pontua nao mostra se o placar tem dois lados.
	/// Quem responde pelo reflexo e o servidor, no <see cref="ResponderPelaMaquina"/>, sem que a bancada
	/// encoste nele.
	/// </summary>
	private (int Respostas, int Acertos, double PtsMaquina, double PtsDono, double Segundos, bool Acabou)
		RodarOEmbate(ServerPlayer dono, ServerPlayer maquina, double tetoSegundos)
	{
		if (!_emEmbate.TryGetValue(dono.Id, out Embate? e)) return (0, 0, 0, 0, 0, false);

		bool maquinaEhA = e.A == maquina;
		long t0 = NowMs(), proximo = t0;
		int respostas = 0, acertos = 0;

		while (_emEmbate.ContainsKey(dono.Id) && NowMs() - t0 < tetoSegundos * 1000)
		{
			if (NowMs() < proximo) { System.Threading.Thread.Sleep(1); continue; }
			proximo = NowMs() + (long)(Protocol.TickSeconds * 1000);

			char minha = maquinaEhA ? e.LetraB : e.LetraA;
			if (minha != '\0') TeclaDoEmbate(dono, minha);

			long antes = maquinaEhA ? e.RespondeA : e.RespondeB;
			double ptsAntes = maquinaEhA ? e.PtsA : e.PtsB;
			TickDosEmbates();
			long depois = maquinaEhA ? e.RespondeA : e.RespondeB;

			// A TRANSICAO, e nao o valor: `ResponderPelaMaquina` zera ao apertar e `NovaTecla` so rearma
			// na letra seguinte -- que nasce depois do prazo, nunca no mesmo tique.
			if (antes > 0 && depois == 0)
			{
				respostas++;
				// O SINAL DO PONTO DIZ O QUE ELE FEZ: acerto soma `Mult`, erro tira um ponto plano
				// (`PontoPorErro`). Sem isto a bancada saberia que ele APERTOU e nao saberia se ele
				// esta jogando ou martelando o teclado -- e o `PisoDeAcertoDaMaquina` ficaria sem
				// nenhuma medida em cima dele.
				if ((maquinaEhA ? e.PtsA : e.PtsB) > ptsAntes) acertos++;
			}
		}

		return (respostas, acertos, maquinaEhA ? e.PtsA : e.PtsB, maquinaEhA ? e.PtsB : e.PtsA,
				(NowMs() - t0) / 1000.0, !_emEmbate.ContainsKey(dono.Id));
	}
}
