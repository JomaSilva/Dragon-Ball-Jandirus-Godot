using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Social;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// ============================ O CORPO QUE FICA, COM DOIS CORPOS DE VERDADE -- `--menteviva` ============================
/// A IRMA VIVA da <see cref="RodarBancadaDaMente"/>, na mesma divisao de trabalho que a
/// `--mestrevivo` tem com a `--mestreteste`. E ela existe por uma razao que esta escrita, com todas
/// as letras, no cabecalho da bancada de regra:
///
///     *"O QUE ESTA BANCADA NAO MEDE: A PORTA (`EntrarNaMente` -> `LargarOCorpo`). O mecanismo do
///     corpo largado exige `Peer != null` -- e a recusa e deliberada [...]. Corpos forjados nao tem
///     `Peer`."*
///
/// Ou seja: **a Camada 1 inteira -- o mecanismo que o dono disse valer pelos dois sistemas -- nunca
/// foi medida pelo caminho por onde o jogador passa.** A `--menteteste` assume o corpo ja la dentro;
/// o que produz esse estado (largar o corpo, deixa-lo visivel, acordar no soco, voltar pro corpo)
/// ficou inteiro fora de qualquer bancada. Esta aqui e essa metade.
///
/// ============================ POR QUE PRECISA DE DOIS PROCESSOS ============================
/// `LargarOCorpo` recusa quem nao tem `Peer` (*"corpo sem dono nao tem atencao pra mandar embora"*),
/// e **nada aqui afrouxa isso** -- uma bancada que abre a porta que ela deveria vigiar deixa de
/// vigiar a porta. Entao os dois corpos sao dois clientes de verdade, em dois processos, com duas
/// contas e dois soquetes:
///
///     Godot --headless --path . --host --rede 7997 --menteviva --raca Saiyan \
///           --conta bancada_menteviva_a --nome MenteViva A
///     Godot --headless --path . --rede 7997 --connect 127.0.0.1 --raca Saiyan \
///           --conta bancada_menteviva_b --nome MenteViva B
///
/// (o `testar-mente.bat` sobe os dois e mata o segundo no fim.)
///
/// **E o VISITANTE nao tem outro jeito de existir**: ele e a unica familia do sistema em que DUAS
/// pessoas precisam atravessar a porta -- uma pra deixar o corpo no chao e outra pra reparar nele. A
/// `--menteteste` mede a PERGUNTA (`MenteAoLado`) com um boneco montado a mao; aqui os dois bonecos
/// sao produzidos pelo mecanismo, e quem os acha e o codigo de producao.
///
/// O PLACAR MORA NO SERVIDOR, e nao nos dois clientes, pela regra da casa: o servidor e a
/// AUTORIDADE. Todo numero que esta bancada afirma e um numero que o servidor decidiu -- os dois
/// processos de cliente existem pra dar `Peer`, conta e assinatura, que e exatamente o que corpo
/// forjado nao tem.
///
/// ============================ QUANDO ELA RODA ============================
/// No login do SEGUNDO corpo com `Peer`, e ainda assim com dois segundos de atraso: o gancho vive no
/// fim do `Entrar`, e ali o corpo que acabou de chegar **ja esta** na `ZoneList` -- mas os dois
/// clientes ainda estao mandando posicao, e um tique de rede no meio da familia 1 mexeria no corpo
/// que ela esta medindo. E o mesmo atraso, e pelo mesmo motivo, que a `--quebrarteste` ja usa.
///
/// A BANCADA E UM BLOCO SINCRONO. Enquanto ela roda, o `_Process` do servidor nao roda -- nao ha
/// tique de combate, de ficha nem de rede entre duas afirmacoes. Onde ela precisa de um tique, ela
/// CHAMA o tique (`TickDeQuemVolta`, `BordasDeQuemEstaFora`, `Treinar`), que e o codigo de producao.
///
/// ============================ AS NOVE FAMILIAS, E COMO CADA UMA REPROVA ============================
///  1. **O CORPO QUE FICA** -- reprova se `LargarOCorpo` deixar de por o boneco na `ZoneList` (o
///     meditador SOME pra quem esta do lado, que e o defeito original), se o boneco entrar tambem no
///     `_players` (ficha tiqueada em dobro), se ele deixar de compartilhar `Ficha`/`Combate` (o dano
///     passaria a precisar de transferencia), ou se a POSE parar de sair do `Ficha.med` -- que e o
///     unico jeito de o corpo aparecer MEDITANDO. Medida no BYTE do snapshot, e nao no campo: pose e
///     3 bits dentro de um byte de flags, e um deslocamento errado nao aparece em campo nenhum.
///  2. **BATER ACORDA, E O DANO VALE** -- reprova se o gatilho sair do `MarcarAgressao` (funil unico
///     de agressao), se a volta deixar de ser adiada (a lista da zona sendo percorrida), ou se o
///     dano parar de cair no corpo do DONO. As duas metades na mesma cena: o corpo perde vida
///     ANTES de a fila drenar, e volta pro corpo DEPOIS.
///  3. **A FERIDA NAO VOLTA** -- reprova se a foto nao for tirada, se o restauro nao rodar antes da
///     viagem, ou se ele passar a CURAR em vez de devolver. Medida membro a membro, com a metade
///     contraria: a MESMA pancada, fora da mente, fica no corpo.
///  4. **SEM ZENKAI** -- reprova se o gate do `AoPerderALuta` sair do funil; e a metade contraria
///     (a mesma cena, fora) reprova se alguem "consertar" isso desligando o Zenkai inteiro.
///  5. **O GANHO A 0,25x** -- reprova se o `RitmoDaZona` nao for escrito pelo funil da troca de
///     zona, se ele nao for limpo na saida, ou se o 0,25 parar de chegar no `TrainGain`. Medido nos
///     DOIS lugares, com o MESMO corpo e o MESMO BP de partida.
///  6. **A RAIVA** -- reprova se o luto escapar do gate: um amigo "caindo" numa luta simulada
///     acenderia a janela que abre SSJ.
///  7. **O VISITANTE** -- reprova se `MenteAoLado` nao achar o corpo largado do vizinho, se o
///     destino for `De(id do vizinho)` em vez da ZONA dele, se o reflexo do anfitriao nao se
///     desfizer, ou se a saida devolver os dois ao corpo errado.
///  8. **AS BORDAS** -- morte, nocaute, corpo sumido e LOGOUT. Reprova se qualquer uma deixar
///     alguem preso fora do corpo, ou se o save gravar a zona da mente (o bolso sem porta).
///  9. **OOZARU E FURIA** -- reprova se o possuido puder largar o corpo, se o boneco passar a
///     acender o bit `SemRedeas` (o olho da fera num corpo meditando), ou se ele passar a ser
///     dirigido pelo `TickDosCorposSemDono`. **As DUAS posses** -- o macaco e a furia lendaria --,
///     porque sao dois escritores do mesmo `Cerebro` e o gate e uma linha so.
/// 10. **O REFLEXO SE REERGUE, COM O CORPO NO CHAO LA FORA** -- reprova se a queda do reflexo
///     encerrar o transe, se ele voltar aos 12 s (o nocaute comum, e nao os 15 do DM), se voltar
///     ferido, se o ciclo nao se repetir, ou se o corpo largado sumir da tela de quem ficou de fora
///     enquanto a luta de dentro acontece. E a irma VIVA da familia 6 da `--menteteste`: la o dono e
///     forjado e nao tem corpo pra deixar; aqui a sessao inteira e a de um jogador de verdade.
/// 11. **O BONECO APANHA MAS NAO CONTA** -- reprova se largar o corpo passar a criar um segundo
///     corpo na conta de corpos sem dono, se um corpo largado sozinho numa zona passar a mante-la
///     "com gente" (o anti-lag do povoamento), se ele entrar na media que escala todo NPC que
///     nascer -- ou, na outra ponta, se ele deixar de ser um ALVO: e o `attackable = 1` do
///     `mind_dummy`, e e por ele que o soco de um NPC acorda o dono pelo mesmo funil do soco de um
///     jogador.
///
/// ============================ AS CONTAS SAO NOVAS, E SAEM NO FIM ============================
/// Os dois personagens sao criados pelo caminho automatico do cliente (`Boot.AutoEscolher`), numa
/// conta propria cada. **Ela e apagada no fim da rodada**, pelo mesmo argumento da `--mestrevivo`:
/// conta reaproveitada traria o personagem da rodada anterior -- com o BP que a bancada empurrou e
/// os chefes que ela mostrou --, e "conta nova" e o pedido. Apagar no fim tambem e o que faz a
/// PROXIMA rodada nascer limpa.
/// ==================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// Faixa de ids desta bancada -- acima da `--mestrevivo` (91.000), que era a maior.
	///
	/// SO O CARRASCO nasce aqui: os dois corpos que importam sao os dois clientes, com os ids que o
	/// servidor deu a eles.
	/// </summary>
	private const int IdBaseDaMenteViva = 91_500;

	/// <summary>As contas dos dois clientes -- apagadas no fim da rodada. Ver o cabecalho.</summary>
	private static readonly string[] ContasDaMenteViva = ["bancada_menteviva_a", "bancada_menteviva_b"];

	private bool _menteVivaLigada;
	private bool _menteVivaJaRodou;
	private int _mvvOk, _mvvFalhou;

	private void AfirmarMvv(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _mvvOk++; GD.Print($"[menteviva]   OK    {oque}"); return; }
		_mvvFalhou++;
		GD.PrintErr($"[menteviva]   FALHA {oque}   {detalhe}");
	}

	/// <summary>
	/// O GANCHO, no fim do <see cref="Entrar"/>. Espera o SEGUNDO corpo com `Peer` -- e diz em voz
	/// alta que esta esperando, porque uma bancada que nao roda tem que ser distinguivel de uma
	/// bancada que passou.
	/// </summary>
	private void MenteVivaNoLogin()
	{
		if (!_menteVivaLigada || _menteVivaJaRodou) return;

		int vivos = _players.Values.Count(p => p.Peer != null);
		if (vivos < 2)
		{
			GD.Print($"[menteviva] {vivos} de 2 corpos de verdade no ar -- esperando o segundo cliente.");
			return;
		}

		// DOIS SEGUNDOS: ver o cabecalho. O atraso e o mesmo recurso (e o mesmo motivo) da
		// `--quebrarteste`, que tambem precisa que o corpo esteja INTEIRO no mundo antes de mexer.
		_menteVivaJaRodou = true;
		SceneTreeTimer t = GetTree().CreateTimer(2.0);
		t.Timeout += RodarBancadaDaMenteViva;
	}

	public void RodarBancadaDaMenteViva()
	{
		_mvvOk = _mvvFalhou = 0;
		GD.Print("[menteviva] ============ O CORPO QUE FICA, COM DOIS CORPOS DE VERDADE ============");

		List<ServerPlayer> gente = _players.Values.Where(p => p.Peer != null).OrderBy(p => p.Id).ToList();
		if (gente.Count < 2)
		{
			AfirmarMvv("ha dois corpos de verdade no mundo", false, $"{gente.Count}");
			Fechar();
			return;
		}

		ServerPlayer a = gente[0], b = gente[1];
		ServerPlayer? carrasco = null;

		// A ZONA E UM PRE-FEITO DE GRAVIDADE 1, e a gravidade e um requisito e nao um detalhe: a
		// familia 5 compara o ganho DENTRO e FORA da mente, e a Interdimension puxa 1x. Medir a
		// partir de Vegeta (10x) seria comparar duas gravidades e chamar o resultado de "ritmo da
		// zona". A afirmacao explicita esta la, na propria familia.
		ZoneKey fora = ZoneKey.Premade("Earth");

		try
		{
			APreparacao(a, b, fora);
			carrasco = ForjarCarrasco(fora);

			OCorpoQueFica(a, b, fora);
			BaterAcordaEODanoVale(a, b, fora);
			OGanhoAUmQuarto(a, fora);
			OReflexoVoltaEDaPraLutar(a, b, fora);
			OBonecoApanhaMasNaoConta(a, b, fora, carrasco);
			OVisitante(a, b, fora, carrasco);
			OLemeDaNave(a, b, fora);
			AsBordas(a, b, fora);
			AFeraNaoLargaOCorpo(a, fora);
			ODeslogueDeQuemEstaFora(b);
		}
		catch (Exception e)
		{
			AfirmarMvv($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			if (carrasco != null) Recolher(carrasco);
			// O QUE A MENTE DE CADA UM TIVER ERGUIDO SAI JUNTO -- senao a rodada deixa um corpo
			// dirigido parado num bolso pra sempre.
			foreach (ServerPlayer p in gente)
				if (p.CloneId != 0 && _players.TryGetValue(p.CloneId, out ServerPlayer? c)) RemoverNpc(c);
			ApagarContasDaMenteViva();
		}

		Fechar();
	}

	private void Fechar()
	{
		GD.Print($"[menteviva] ============ {_mvvOk} OK, {_mvvFalhou} FALHA(S) ============");

		// A RODADA ACABA O PROCESSO, e so esta bancada faz isso no projeto inteiro. O motivo e o que
		// a torna diferente das outras: ela e a unica que precisa de DOIS processos, e um par de
		// processos que nao termina sozinho e um par que fica no ar segurando a porta 7997 -- que e
		// exatamente o que o cabecalho do `--rede` descreve como "as bancadas nao saem sozinhas".
		//
		// UM SEGUNDO DE ATRASO pro `PeerLeft` do encerramento chegar no outro cliente antes de o
		// soquete fechar: sem isso o segundo processo fica com um corpo congelado na tela ate o
		// `taskkill` do .bat -- e a ultima coisa que a bancada faz nao pode ser deixar um fantasma.
		SceneTreeTimer t = GetTree().CreateTimer(1.0);
		t.Timeout += () => GetTree().Quit(_mvvFalhou == 0 ? 0 : 1);
	}

	// =====================================================================
	// A PREPARACAO
	// =====================================================================
	/// <summary>
	/// OS DOIS CORPOS LADO A LADO, curados, com BP igual e sem nada pendurado.
	///
	/// Ela NAO cria os personagens: eles ja existem, criados pela tela de criacao do proprio cliente
	/// (o caminho automatico do `Boot`), com classe SORTEADA no `Nascer` e limiar de SSJ rolado no
	/// nascimento. E a mesma diferenca que a `--mestrevivo` descreve: o que se mede aqui e a regra
	/// alimentada pelo JOGO, e nao por numeros que a bancada escreveu.
	/// </summary>
	private void APreparacao(ServerPlayer a, ServerPlayer b, ZoneKey fora)
	{
		GD.Print("[menteviva] -- 0) a preparacao (dois personagens de verdade, lado a lado)");

		AfirmarMvv("os dois corpos tem `Peer` -- e e isso que a `--menteteste` nao pode ter",
				   a.Peer != null && b.Peer != null);
		AfirmarMvv("os dois tem ASSINATURA de conta (sem ela o luto e o Zenkai saem na primeira linha)",
				   EhPessoa(a) && EhPessoa(b), $"'{a.Assinatura}' / '{b.Assinatura}'");

		// O ZENKAI EXIGE DNA SAIYAJIN. O `.bat` pede `--raca Saiyan` nos dois; se alguem rodar a
		// bancada com outra raca, a familia 4 daria verde POR AUSENCIA -- e a bancada diz isso em voz
		// alta em vez de passar calada.
		AfirmarMvv("o corpo A tem Zenkai (senao a regra 2 mediria o nada)",
				   a.Ficha.HasZenkai(), $"raca {a.Race}");

		foreach (ServerPlayer p in new[] { a, b })
		{
			MoveToZone(p.Id, fora, new Vec2(4000, 4000));
			p.Ficha.med = p.Ficha.train = false;
			p.Combate.Letal = false;
			CurarPorInteiro(p);
			PorBp(p, 500_000);
			p.Ficha.Ki = p.Ficha.MaxKi;
			p.Ficha.zenkaiReady = 0;
			p.AlvoId = 0;
		}

		Encostar(a, b, 64);
		AfirmarMvv("os dois estao na MESMA zona, e e uma zona de gravidade 1",
				   a.Zone.Hash == b.Zone.Hash && Math.Abs(a.Ficha.Planetgrav - 1) < 1e-9,
				   $"{a.Zone.Name} / grav {a.Ficha.Planetgrav}");
	}

	// =====================================================================
	// 1) O CORPO QUE FICA -- VISIVEL, E NA POSE CERTA
	// =====================================================================
	/// <summary>
	/// *"pessoas do lado de fora vao ver vc MEDITANDO normalmente"*.
	///
	/// ============================ O QUE E "VISIVEL", DO LADO DO SERVIDOR ============================
	/// O snapshot de uma zona e montado UMA vez por `ZoneList` e mandado pro `Peer` de cada corpo
	/// daquela lista (`GameServer.cs`, o laco de `_zones.Values`). Entao "B ve o corpo de A" e,
	/// literalmente, duas coisas: **o boneco esta na lista** e **B esta na mesma lista**. Nao ha
	/// terceira condicao, e nao ha filtro por corpo -- o buffer e o mesmo pra todo mundo da zona.
	///
	/// A POSE E MEDIDA NO BYTE, e nao no campo: ela viaja em 3 bits dentro de um byte de flags,
	/// espremida entre a direcao e o "andando". Ler `EstadoDe(...).Pose` provaria a derivacao e
	/// deixaria passar um deslocamento errado na serializacao -- o corpo apareceria NOCAUTEADO na
	/// tela do outro com a bancada verde. Ver <see cref="NoFio"/>.
	/// ==========================================================================================
	/// </summary>
	private void OCorpoQueFica(ServerPlayer a, ServerPlayer b, ZoneKey fora)
	{
		GD.Print("[menteviva] -- 1) o corpo que fica: visivel, e na pose certa");

		AfirmarMvv("antes da porta nao ha boneco nenhum (a bancada nao comeca verde)",
				   a.BonecoLargado == null && !ZoneList(fora.Hash).Any(p => p.DonoDoCorpoLargado != 0));

		// A PORTA, PELO CAMINHO DE PRODUCAO: o mesmo `case "mente"` do canal de habilidade que o
		// botao do menu P dispara. Meditar primeiro, porque a porta exige (e porque e o `med` que vai
		// desenhar o corpo que fica).
		a.Ficha.med = true;
		UsarHabilidade(a, "mente");

		AfirmarMvv("a porta levou A pra DENTRO da propria mente", NaMente(a), a.Zone.ToString());
		AfirmarMvv("...e a mente e a DELE (o dono sai da chave da zona)",
				   DimensaoMental.Anfitriao(a.Zone) == a.Id, $"{DimensaoMental.Anfitriao(a.Zone)}");
		AfirmarMvv("...e o reflexo esta de pe la dentro",
				   a.CloneId != 0 && _players.TryGetValue(a.CloneId, out ServerPlayer? cl)
				   && cl.Zone.Hash == a.Zone.Hash, $"{a.CloneId}");

		ServerPlayer? boneco = a.BonecoLargado;
		if (boneco == null) { AfirmarMvv("o corpo de A ficou pra tras", false, "sem boneco"); return; }

		AfirmarMvv("o CORPO de A ficou na zona de fora (ele nao viajou junto)",
				   boneco.Zone.Hash == fora.Hash && ZoneList(fora.Hash).Contains(boneco), boneco.Zone.ToString());
		AfirmarMvv("...e B esta na MESMA lista de zona -- que e o buffer de snapshot que sai pro peer dele",
				   ZoneList(fora.Hash).Contains(b));
		AfirmarMvv("...e o nome do corpo e o de A, e nao 'A (corpo)'", boneco.Name == a.Name, boneco.Name);

		// A POSE, NO FIO.
		EntityState noFio = NoFio(boneco);
		AfirmarMvv("no BYTE do snapshot o corpo de A esta MEDITANDO",
				   noFio.Pose == Protocol.Pose.Meditando, noFio.Pose.ToString());
		AfirmarMvv("...e ela nao e um campo do boneco: e o `Ficha.med` do DONO que a desenha",
				   ReferenceEquals(boneco.Ficha, a.Ficha) && a.Ficha.med);

		// A METADE CONTRARIA DA POSE: o mesmo boneco, com o dono deixando de meditar, deixa de
		// meditar na tela dos outros. Sem esta metade, uma pose escrita a mao (`Meditando` fixo no
		// boneco) passaria verde -- e era exatamente assim que o DM fazia, com o `icon_state` na mao.
		a.Ficha.med = false;
		AfirmarMvv("...tanto que desligar o `med` do dono muda a pose do corpo que ficou",
				   NoFio(boneco).Pose != Protocol.Pose.Meditando, NoFio(boneco).Pose.ToString());
		a.Ficha.med = true;

		AfirmarMvv("o boneco NAO entra no `_players` (senao a ficha do dono seria tiqueada em dobro)",
				   !_players.ContainsKey(boneco.Id));
		AfirmarMvv("...e ele compartilha o `Combate` do dono -- nao ha dano pra transferir",
				   ReferenceEquals(boneco.Combate, a.Combate));
		AfirmarMvv("...e nao ha um segundo corpo: A e o boneco tem os MESMOS membros",
				   ReferenceEquals(boneco.Combate.Corpo, a.Combate.Corpo));

		// A APARENCIA E A UNICA COISA COPIADA, e ela precisa ser: o `Sanear` do catalogo reescreve a
		// lista que recebe, e duas pessoas apontando pra mesma instancia ja foi um bug do clone.
		AfirmarMvv("a aparencia, essa sim, e COPIA (duas pessoas na mesma lista de roupa ja foi bug)",
				   !ReferenceEquals(boneco.Visual, a.Visual));

		AfirmarMvv("ninguem dirige o corpo largado (`Cerebro` e `Peer` nulos)",
				   boneco.Cerebro == null && boneco.Peer == null);

		// E A PORTA RECUSA QUEM JA ESTA FORA DO CORPO: um segundo pedido nao ergue um segundo boneco.
		int quantosAntes = ZoneList(fora.Hash).Count(p => p.DonoDoCorpoLargado == a.Id);
		UsarHabilidade(a, "mente");
		AfirmarMvv("pedir de novo nao ergue um segundo corpo",
				   ZoneList(fora.Hash).Count(p => p.DonoDoCorpoLargado == a.Id) == quantosAntes,
				   $"{quantosAntes}");
	}

	// =====================================================================
	// 2) BATER NO CORPO REAL ACORDA -- E O DANO VALE
	// =====================================================================
	/// <summary>
	/// *"se elas TE BATEREM vc ACORDA da meditacao pro corpo real"*.
	///
	/// ============================ A CENA E UMA SO, E ELA PROVA AS DUAS METADES ============================
	/// A volta e ADIADA de proposito (`PorNaFilaDeVolta` -> `TickDeQuemVolta`, no fim do tique),
	/// porque `MoveToZone` mexe na lista de zona que a resolucao do golpe esta percorrendo. Isso da a
	/// esta bancada uma janela que o jogador tambem tem: **dentro do mesmo tique, o corpo continua
	/// la.** Entao B soca ate o corpo sangrar, e SO ENTAO a fila e drenada:
	///
	///   * o dano aparece no corpo do DONO antes de qualquer volta -- prova que nao ha transferencia;
	///   * a volta acontece na drenagem -- prova que o gatilho e o `MarcarAgressao` e que ele foi
	///     enfileirado e nao executado no meio do golpe.
	///
	/// Se alguem "consertar" a fila fazendo a volta acontecer na hora, a primeira metade cai (o
	/// segundo soco nao acha mais o boneco) -- que e exatamente o defeito que a fila existe pra
	/// evitar, so que visivel.
	/// ================================================================================================
	/// </summary>
	private void BaterAcordaEODanoVale(ServerPlayer a, ServerPlayer b, ZoneKey fora)
	{
		GD.Print("[menteviva] -- 2) bater no corpo real acorda o dono, e o dano vale");

		ServerPlayer? boneco = a.BonecoLargado;
		if (boneco == null) { AfirmarMvv("A esta fora do corpo pra esta familia", false); return; }

		// B FICA COLADO NO CORPO E VIRADO PRA ELE. `AlvoNaFrente` e um CONE: um alvo fora dele faz a
		// bancada medir geometria em vez de medir a regra.
		b.Pos = boneco.Pos + new Vec2(24, 0);
		b.Facing = MoveRules.FacingFrom(boneco.Pos - b.Pos, b.Facing);
		PorBp(b, 5_000_000);   // desnivel: o soco tem que ACERTAR pra o dano ser mensuravel

		AfirmarMvv("o corpo largado E um alvo -- o cone do soco o encontra",
				   AlvoNaFrente(b) == boneco, AlvoNaFrente(b)?.Name ?? "ninguem");

		double vidaAntes = a.Combate.Corpo.Vida();
		_acordar.Clear();

		// ATE DOZE SOCOS DENTRO DO MESMO TIQUE. A recarga e zerada entre eles porque a cadencia e do
		// `Eactspeed` e nao tem nada a ver com o que se mede aqui; a FILA nao e drenada, porque e ela
		// que esta sob teste.
		int socos = 0;
		while (socos < 12 && a.Combate.Corpo.Vida() >= vidaAntes - 1e-9)
		{
			b.Combate.Recarga = 0;
			Atacar(b, Protocol.Golpe.Pesado);
			socos++;
		}

		AfirmarMvv("bater no corpo largado enfileira a VOLTA do dono (e o `MarcarAgressao`)",
				   _acordar.Contains(a.Id), $"fila: [{string.Join(",", _acordar)}]");
		AfirmarMvv("...e ela e ADIADA: dentro do mesmo tique o corpo AINDA esta na zona",
				   ZoneList(fora.Hash).Contains(boneco) && NaMente(a));
		AfirmarMvv($"o dano cai no corpo do DONO, sem transferencia nenhuma ({socos} soco(s))",
				   a.Combate.Corpo.Vida() < vidaAntes - 1e-9,
				   $"{vidaAntes:0.##} -> {a.Combate.Corpo.Vida():0.##}");

		// A DRENAGEM -- o fim do tique, chamado pelo codigo de producao.
		Vec2 ondeEstavaOCorpo = boneco.Pos;
		TickDeQuemVolta();

		AfirmarMvv("drenada a fila, A esta de volta no proprio corpo", !NaMente(a) && a.Zone.Hash == fora.Hash,
				   a.Zone.ToString());
		AfirmarMvv("...e ele volta ONDE O CORPO ESTAVA (que nao e onde ele foi largado -- o soco empurra)",
				   (a.Pos - ondeEstavaOCorpo).LengthSquared < 1e-6,
				   $"{a.Pos.X:0},{a.Pos.Y:0} != {ondeEstavaOCorpo.X:0},{ondeEstavaOCorpo.Y:0}");
		AfirmarMvv("...e o boneco saiu do mundo (nem na zona, nem pendurado no dono)",
				   a.BonecoLargado == null && !ZoneList(fora.Hash).Contains(boneco));
		AfirmarMvv("...e o reflexo se desfez junto", a.CloneId == 0);

		// ============================ A METADE CONTRARIA: SOCAR ALGUEM QUE ESTA NO PROPRIO CORPO ============================
		// Sem ela, um `PorNaFilaDeVolta` chamado pra todo mundo passaria verde -- e o efeito seria
		// todo jogador do servidor "voltando pro corpo" a cada soco levado.
		// ==============================================================================================================
		_acordar.Clear();
		b.Combate.Recarga = 0;
		b.Pos = a.Pos + new Vec2(24, 0);
		b.Facing = MoveRules.FacingFrom(a.Pos - b.Pos, b.Facing);
		Atacar(b, Protocol.Golpe.Leve);
		AfirmarMvv("socar quem esta DENTRO do proprio corpo nao enfileira volta nenhuma",
				   _acordar.Count == 0, $"fila: [{string.Join(",", _acordar)}]");

		CurarPorInteiro(a);
		PorBp(b, 500_000);

		OGolpeQueNaoEncostaTambemAcorda(a, b, fora);
	}

	// =====================================================================
	// 2c) O GOLPE QUE **NAO ENCOSTA** TAMBEM ACORDA
	// =====================================================================
	/// <summary>
	/// ============================ A FRASE DO AUTOR DO DM, MEDIDA ============================
	/// O gatilho do `mind_dummy` e o `refresh_combat_tag()` (`MindMeditate.dm:147-149`) e nao o dano, e
	/// o comentario ao lado diz por que: *"so this fires no matter the outcome"*. **Quem tocou no seu
	/// corpo te trouxe de volta, tenha machucado ou nao.**
	///
	/// A familia 2 prova o gatilho com um golpe que ACERTA -- e um golpe que acerta passa por dois
	/// caminhos ao mesmo tempo (o dano no corpo compartilhado E o funil de agressao). Um `AcordarNoCorpo`
	/// pendurado por engano no dano (dentro do `AplicarNoMembro`, ou no `ResolverDesfecho`) ficaria verde
	/// naquela familia inteira, e o defeito so apareceria no jogo: quem esquiva de tudo nunca acordaria.
	///
	/// ============================ OS DOIS DESFECHOS SEM DANO, FORCADOS PELO MECANISMO ============================
	/// Nao ha como pedir "erra este soco" ao resolvedor, entao cada um e produzido pelo campo que o
	/// governa -- e os dois campos sao do `CombatState`, que aqui e **o do dono** (o boneco compartilha):
	///
	///   * **ERROU** -- `Deflexao` altissima faz a `CombatMath.Pontaria` sair negativa, e o golpe morre
	///     no passo 1. Nem dano, nem Ki: o desfecho mais "vazio" que existe;
	///   * **ESQUIVOU** -- `ChanceEsquiva = 100` faz o passo 3 sempre disparar. Ele CUSTA Ki
	///     (`0,05 x MaxKi / Etechnique`), e e por isso que a esquiva e reconhecivel: **vida parada e Ki
	///     que desce** e uma assinatura que nenhum outro desfecho produz. Sem ela a bancada nao saberia
	///     distinguir um golpe esquivado de um golpe que simplesmente errou -- e mediria um dos dois
	///     duas vezes.
	///
	/// O laco existe porque a pontaria continua sendo um sorteio no caso da esquiva: soca-se ate o Ki
	/// mexer, que e o instante em que o golpe chegou no passo 3.
	/// ==========================================================================================================
	/// </summary>
	private void OGolpeQueNaoEncostaTambemAcorda(ServerPlayer a, ServerPlayer b, ZoneKey fora)
	{
		GD.Print("[menteviva] -- 2c) golpe ERRADO e golpe ESQUIVADO tambem acordam");

		a.Ficha.med = true;
		UsarHabilidade(a, "mente");
		if (a.BonecoLargado is not { } boneco) { AfirmarMvv("A largou o corpo pra a familia 2c", false); return; }

		b.Pos = boneco.Pos + new Vec2(24, 0);
		b.Facing = MoveRules.FacingFrom(boneco.Pos - b.Pos, b.Facing);
		PorBp(b, 5_000_000);

		a.Combate.Bloqueando = false;    // o bloqueio vem ANTES da esquiva no resolvedor
		a.Ficha.Ki = a.Ficha.MaxKi;

		// --- ERROU: a pontaria morre no passo 1 -----------------------------------------
		double vidaAntes = a.Combate.Corpo.Vida(), kiAntes = a.Ficha.Ki;
		double deflexaoReal = a.Combate.Deflexao;
		a.Combate.Deflexao = 1e9;
		_acordar.Clear();
		b.Combate.Recarga = 0;
		Atacar(b, Protocol.Golpe.Pesado);
		a.Combate.Deflexao = deflexaoReal;

		AfirmarMvv("um golpe que ERRA nao encosta no corpo largado (nem vida, nem Ki)",
				   Math.Abs(a.Combate.Corpo.Vida() - vidaAntes) < 1e-9
				   && Math.Abs(a.Ficha.Ki - kiAntes) < 1e-9,
				   $"vida {vidaAntes:0.##}->{a.Combate.Corpo.Vida():0.##}, ki {kiAntes:0.##}->{a.Ficha.Ki:0.##}");
		AfirmarMvv("...e MESMO ASSIM ele acorda o dono (o gatilho e o funil de agressao, nao o dano)",
				   _acordar.Contains(a.Id), $"fila: [{string.Join(",", _acordar)}]");

		// --- ESQUIVOU: o passo 3, que custa Ki --------------------------------------------
		vidaAntes = a.Combate.Corpo.Vida();
		kiAntes = a.Ficha.Ki;
		double esquivaReal = a.Combate.ChanceEsquiva;
		a.Combate.ChanceEsquiva = 100;
		_acordar.Clear();

		int tentativas = 0;
		while (tentativas < 12 && a.Ficha.Ki >= kiAntes - 1e-9)
		{
			b.Combate.Recarga = 0;
			Atacar(b, Protocol.Golpe.Pesado);
			tentativas++;
		}
		a.Combate.ChanceEsquiva = esquivaReal;

		AfirmarMvv($"o corpo largado ESQUIVOU de verdade -- Ki gasto e vida intacta ({tentativas} tentativa(s))",
				   a.Ficha.Ki < kiAntes - 1e-9 && Math.Abs(a.Combate.Corpo.Vida() - vidaAntes) < 1e-9,
				   $"vida {vidaAntes:0.##}->{a.Combate.Corpo.Vida():0.##}, ki {kiAntes:0.##}->{a.Ficha.Ki:0.##}");
		AfirmarMvv("...e o golpe ESQUIVADO tambem acorda (o `so this fires no matter the outcome` do DM)",
				   _acordar.Contains(a.Id), $"fila: [{string.Join(",", _acordar)}]");

		TickDeQuemVolta();
		AfirmarMvv("...e a volta acontece do mesmo jeito, sem um unico ponto de vida perdido",
				   !NaMente(a) && a.BonecoLargado == null
				   && Math.Abs(a.Combate.Corpo.Vida() - vidaAntes) < 1e-9, a.Zone.ToString());

		MoveToZone(a.Id, fora, new Vec2(4000, 4000));
		MoveToZone(b.Id, fora, new Vec2(4064, 4000));
		a.Ficha.med = false;
		CurarPorInteiro(a);
		a.Ficha.Ki = a.Ficha.MaxKi;
		PorBp(b, 500_000);
	}

	// =====================================================================
	// 5) O GANHO A UM QUARTO, MEDIDO NOS DOIS LUGARES
	// =====================================================================
	/// <summary>
	/// *"o GANHO DE BP E REDUZIDO"* -- `MIND_GAIN_MULT = 0.25` (`MindMeditate.dm:18`).
	///
	/// ============================ O MESMO CORPO, O MESMO BP DE PARTIDA ============================
	/// A tentacao era comparar A com B: sao dois corpos de verdade, um dentro e outro fora. Nao serve
	/// -- os dois nasceram com CLASSE SORTEADA, e classe muda `Egains`, `TrainMod` e `relBPmax`. A
	/// razao entre os dois ganhos seria a razao entre duas fichas, e o 0,25 nunca apareceria limpo.
	///
	/// Entao e o MESMO corpo, medido duas vezes, com o BP devolvido ao valor de partida entre as
	/// duas -- e mais tres campos que carregam de uma medida pra outra (`hiddenpotential`,
	/// `BPBuffer`, `Gaintimer`). Sem devolve-los, a segunda medida comeca com o resto da primeira e a
	/// razao vira 0,25 mais um erro que muda sozinho.
	///
	/// ============================ POR QUE `train` E NAO `med` ============================
	/// Porque **a meditacao nao leva o multiplicador de zona, e isso e do DM**: `mind_gain_mult()` e
	/// chamado no `Train.dm:12`, no `combatgains.dm:146,166` e no `Gravity.dm:28` -- e em lugar
	/// nenhum do `Meditate.dm`. Medir o 0,25 com `med` acusaria um defeito que nao existe.
	///
	/// ============================ E POR QUE A MEDITACAO PRECISA DE DUAS MEDIDAS ============================
	/// A primeira versao desta bancada afirmou *"medita dentro = medita fora"* pelo funil e **ficou
	/// vermelha por 3%** -- e a producao estava certa. O `Treinar` nao chama so o ganho da atividade:
	/// ele chama o `GravGain` LOGO DEPOIS, e o ganho de gravidade **leva** o multiplicador de zona
	/// (`Gravity.dm:28`). Ou seja, quem medita dentro da mente rende:
	///
	///     MedGain (inteiro)  +  GravGain x 0,25
	///
	/// e nenhum numero redondo sai disso. Entao sao duas afirmacoes, e cada uma no seu nivel:
	///
	///   * a REGRA, com o `MedGain` sozinho: razao **exatamente 1** -- e a linha do DM;
	///   * o FUNIL, com o `Treinar` inteiro: razao **estritamente entre 0,25 e 1** -- que e a unica
	///     coisa que pode ser verdade se as duas metades estiverem certas. Fosse 0,25, o corte teria
	///     vazado pro `MedGain`; fosse 1, ele nao teria chegado no `GravGain`.
	///
	/// Uma medida so nao consegue dizer isso: e o caso em que o numero redondo mentiria nos dois
	/// sentidos.
	/// ================================================================================================
	/// </summary>
	private void OGanhoAUmQuarto(ServerPlayer a, ZoneKey fora)
	{
		GD.Print("[menteviva] -- 5) o ganho de BP a 0,25x, medido nos dois lugares");

		const double bpDePartida = 500_000;
		const int tiques = 40;

		double MedirTreino()
		{
			PorBp(a, bpDePartida);
			a.Ficha.hiddenpotential = 0;
			a.Ficha.BPBuffer = 0;
			a.Ficha.train = true;
			a.Ficha.med = false;
			double antes = a.Ficha.BP;
			for (int i = 0; i < tiques; i++) Treinar(a);
			a.Ficha.train = false;
			return a.Ficha.BP - antes;
		}

		double MedirMeditacao()
		{
			PorBp(a, bpDePartida);
			a.Ficha.hiddenpotential = 0;
			a.Ficha.BPBuffer = 0;
			a.Ficha.train = false;
			a.Ficha.med = true;
			double antes = a.Ficha.BP;
			for (int i = 0; i < tiques; i++) Treinar(a);
			a.Ficha.med = false;
			return a.Ficha.BP - antes;
		}

		// A REGRA SOZINHA: o `MedGain` sem o `GravGain` que o `Treinar` pendura nele. Ver o cabecalho
		// -- e a unica medida que consegue dizer que o corte NAO entrou na meditacao.
		double MedirSoAMeditacao()
		{
			PorBp(a, bpDePartida);
			a.Ficha.hiddenpotential = 0;
			a.Ficha.BPBuffer = 0;
			double antes = a.Ficha.BP;
			for (int i = 0; i < tiques; i++) a.Ficha.MedGain(_rng);
			return a.Ficha.BP - antes;
		}

		double treinoFora = MedirTreino();
		double medFora = MedirMeditacao();
		double soMedFora = MedirSoAMeditacao();
		double gravFora = a.Ficha.Planetgrav;

		AfirmarMvv("fora da mente o ritmo da zona e 1x", Math.Abs(a.Ficha.zoneGainMult - 1) < 1e-9,
				   $"{a.Ficha.zoneGainMult}");
		AfirmarMvv("fora da mente o corpo GANHA alguma coisa treinando (a medida nao e de dois zeros)",
				   treinoFora > 0, $"{treinoFora}");

		// DENTRO, PELA PORTA DE PRODUCAO -- e o `AplicarGravidade` da troca de zona quem escreve o
		// 0,25, e nao a bancada.
		a.Ficha.med = true;
		UsarHabilidade(a, "mente");
		if (!NaMente(a)) { AfirmarMvv("A entrou na mente pra esta familia", false); return; }

		AfirmarMvv("dentro da mente o funil de troca de zona escreveu o 0,25 sozinho",
				   Math.Abs(a.Ficha.zoneGainMult - GainKnobs.MindDimensionMult) < 1e-9,
				   $"{a.Ficha.zoneGainMult}");
		AfirmarMvv("...e as duas zonas puxam a MESMA gravidade (senao esta medida mediria gravidade)",
				   Math.Abs(a.Ficha.Planetgrav - gravFora) < 1e-9, $"{gravFora} -> {a.Ficha.Planetgrav}");

		double treinoDentro = MedirTreino();
		double medDentro = MedirMeditacao();
		double soMedDentro = MedirSoAMeditacao();

		double razao = treinoFora > 0 ? treinoDentro / treinoFora : -1;
		AfirmarMvv($"o TREINO dentro rende {GainKnobs.MindDimensionMult}x o de fora (medido: {razao:0.0000})",
				   Math.Abs(razao - GainKnobs.MindDimensionMult) < 0.01,
				   $"{treinoDentro:0.####} / {treinoFora:0.####}");

		// A MEDITACAO, NOS DOIS NIVEIS -- ver o cabecalho: uma medida so mentiria nos dois sentidos.
		double razaoSoMed = soMedFora > 0 ? soMedDentro / soMedFora : -1;
		AfirmarMvv($"a REGRA: o `MedGain` sozinho NAO e cortado (o DM nao o chama no `Meditate.dm`) -- {razaoSoMed:0.0000}",
				   soMedFora > 0 && Math.Abs(razaoSoMed - 1) < 1e-6,
				   $"{soMedDentro:0.######} / {soMedFora:0.######}");

		double razaoMed = medFora > 0 ? medDentro / medFora : -1;
		AfirmarMvv($"o FUNIL: meditar dentro fica ENTRE 0,25 e 1 -- o `GravGain` colado nele e cortado ({razaoMed:0.0000})",
				   razaoMed > GainKnobs.MindDimensionMult + 1e-6 && razaoMed < 1 - 1e-6,
				   $"{medDentro:0.####} / {medFora:0.####}");

		// E A SAIDA LIMPA O 0,25 -- "ligar e esquecer de desligar" e o modo de falha nomeado no
		// cabecalho do `RitmoDaZona`, e ele seria um castigo silencioso de 0,25x na Terra pra sempre.
		UsarHabilidade(a, "sairdamente");
		AfirmarMvv("sair da mente devolve o ritmo a 1x (nao ha 0,25 preso no bolso)",
				   !NaMente(a) && Math.Abs(a.Ficha.zoneGainMult - 1) < 1e-9, $"{a.Ficha.zoneGainMult}");

		PorBp(a, 500_000);
		a.Ficha.med = a.Ficha.train = false;
		CurarPorInteiro(a);
	}

	// =====================================================================
	// 10) O REFLEXO SE REERGUE -- COM O CORPO NO CHAO LA FORA
	// =====================================================================
	/// <summary>
	/// *"o clone volta depois de 15 s e da pra lutar de novo -- e a sessao NAO acabou."*
	///
	/// ============================ O QUE ESTA FAMILIA TEM QUE A `--menteteste` NAO PODE TER ============================
	/// A familia 6 de la mede o `clone_watch` inteiro (o prazo, a cura, o ciclo, a morte que encerra) com
	/// um dono FORJADO -- e um dono forjado nao tem `Peer`, logo **nao tem corpo largado**: a mente dele
	/// e um bolso que ninguem alcanca. Tudo o que aquela familia diz sobre o reflexo continua verdade
	/// aqui e nao e repetido.
	///
	/// O QUE SO DAQUI SE VE E A OUTRA PONTA DA MESMA SESSAO: enquanto o reflexo esta no chao dentro da
	/// cabeca de A, **o corpo de A continua deitado no mapa, meditando, na tela de B**. As duas metades
	/// sao independentes no codigo (uma e o `TicarUmCorpo`, a outra e a `ZoneList` da zona de fora) e o
	/// que o dono pediu foi que as duas valessem ao mesmo tempo. Um `SairDaMente` disparado pela queda
	/// -- que era o comportamento ANTIGO -- derrubaria as duas de uma vez, e so uma bancada com os dois
	/// lados no ar percebe.
	///
	/// ============================ O REFLEXO E ENFRAQUECIDO, E O PINO E ENFRAQUECIDO JUNTO ============================
	/// Ele nasce com o BP EXPRESSO do dono (`EspelharODono`), entao um reflexo em pe de igualdade
	/// derrubaria A durante os 15 s de medida -- e o que estaria sendo medido seria a borda de KO do
	/// jogador, nao a volta do reflexo. A `--menteteste` ja usa o mesmo recurso.
	///
	/// **E NAO BASTA BAIXAR O `Ficha.BP`**: o `TicarUmCorpo` REPINA o reflexo no `BpDaMente` a cada tique
	/// (o `NPCTicker()` do `MindClone`), entao o poder voltaria sozinho no tique seguinte e a bancada
	/// mediria uma briga entre iguais achando que enfraqueceu alguem. Os dois numeros descem juntos --
	/// que e, de quebra, a prova de que o pino existe e esta ligado.
	/// ============================================================================================================
	/// </summary>
	private void OReflexoVoltaEDaPraLutar(ServerPlayer a, ServerPlayer b, ZoneKey fora)
	{
		GD.Print("[menteviva] -- 10) o reflexo cai, se reergue em 15 s, e da pra lutar de novo");

		MoveToZone(a.Id, fora, new Vec2(4000, 4000));
		MoveToZone(b.Id, fora, new Vec2(4096, 4000));
		CurarPorInteiro(a);
		PorBp(a, 500_000);
		a.Ficha.Ki = a.Ficha.MaxKi;
		a.Ficha.med = true;
		a.Ficha.train = false;
		a.Combate.Letal = false;   // dentro da mente nao se decepa nem se mata: e treino
		a.AlvoId = 0;

		UsarHabilidade(a, "mente");
		if (!NaMente(a) || !_players.TryGetValue(a.CloneId, out ServerPlayer? reflexo))
		{ AfirmarMvv("A entrou na propria mente com o reflexo de pe", false, $"{a.Zone} / {a.CloneId}"); return; }
		if (a.BonecoLargado is not { } corpoDeA)
		{ AfirmarMvv("o corpo de A ficou no chao pra esta familia", false); return; }

		// O ENFRAQUECIMENTO -- os DOIS numeros, ver o cabecalho.
		void Enfraquecer()
		{
			reflexo.Ficha.BP = 100;
			reflexo.Ficha.Statify();
			reflexo.Ficha.PowerLevel();
			reflexo.BpDaMente = 100;
			reflexo.SigAtributos = "";
		}
		Enfraquecer();

		void UmTique()
		{
			TickCombate(Protocol.TickSeconds);
			TickDosCorposSemDono(Protocol.TickSeconds);
		}
		void Rodar(double segundos)
		{
			int n = (int)Math.Round(segundos / Protocol.TickSeconds);
			for (int i = 0; i < n; i++) UmTique();
		}

		// A QUEDA VEM DO SOCO DE A, pelo resolvedor de sempre -- e nao de um `KO = true` escrito aqui:
		// e justamente o relogio que aquele caminho arma (12 s) que o mecanismo tem que reescrever.
		int socos = 0;
		while (socos < 60 && !reflexo.Ficha.KO)
		{
			reflexo.Pos = a.Pos + new Vec2(24, 0);
			a.Facing = MoveRules.FacingFrom(reflexo.Pos - a.Pos, a.Facing);
			a.Combate.Recarga = 0;
			Atacar(a, Protocol.Golpe.Pesado);
			socos++;
		}
		AfirmarMvv($"A derruba o proprio reflexo a soco, pelo resolvedor de sempre ({socos} golpe(s))",
				   reflexo.Ficha.KO, $"KO={reflexo.Ficha.KO}");

		UmTique();
		AfirmarMvv("no primeiro tique caido o mecanismo assume a queda (o `clone_watch`)",
				   reflexo.CaiuNaMente, "o bit nao foi escrito");
		AfirmarMvv($"...e o relogio do nocaute foi REESCRITO pro prazo do DM ({SegundosAteOReflexoSeReerguer:0}s)",
				   reflexo.Combate.NocauteRestante > MeleeResolver.SegundosDeNocaute,
				   $"{reflexo.Combate.NocauteRestante:0.00} <= {MeleeResolver.SegundosDeNocaute}");
		AfirmarMvv("...e a QUEDA nao encerrou o transe (era o comportamento antigo)",
				   NaMente(a) && a.CloneId == reflexo.Id, $"{a.Zone} / {a.CloneId}");

		// ============================ A OUTRA PONTA DA MESMA SESSAO ============================
		// So esta bancada consegue afirmar isto: com o reflexo no chao la dentro, o corpo continua
		// deitado la fora, na lista de zona que vira o snapshot de B, e MEDITANDO no byte do fio.
		// ==================================================================================
		AfirmarMvv("...e la fora o corpo de A continua no chao da zona, na lista que vira a tela de B",
				   ZoneList(fora.Hash).Contains(corpoDeA) && ZoneList(fora.Hash).Contains(b),
				   corpoDeA.Zone.ToString());
		AfirmarMvv("...e no BYTE do snapshot ele continua MEDITANDO",
				   NoFio(corpoDeA).Pose == Protocol.Pose.Meditando, NoFio(corpoDeA).Pose.ToString());

		// AOS 13 S ELE AINDA TEM QUE ESTAR NO CHAO: 13 > 12, entao um verde aqui so e possivel se o
		// nocaute comum tiver sido de fato substituido.
		Rodar(13);
		AfirmarMvv("aos 13 s (mais que os 12 do nocaute comum) ele CONTINUA no chao",
				   reflexo.Ficha.KO && reflexo.CaiuNaMente, $"KO={reflexo.Ficha.KO}");
		AfirmarMvv("...e A nao foi expulso da propria mente nesse meio tempo",
				   NaMente(a) && a.BonecoLargado == corpoDeA, a.Zone.ToString());

		Rodar(3);
		AfirmarMvv("passados os 15 s ele esta de PE de novo", !reflexo.Ficha.KO);
		AfirmarMvv("...e e o MESMO corpo (ele nao foi refeito, ele se reergueu)",
				   _players.ContainsKey(reflexo.Id) && a.CloneId == reflexo.Id, $"{a.CloneId}");
		AfirmarMvv("...INTEIRO, e com o Ki cheio (`SpreadHeal(150,1,1)` + `Ki = MaxKi`)",
				   Math.Abs(reflexo.Combate.Corpo.Vida() - 100) < 1e-6
				   && reflexo.Ficha.MaxKi > 0
				   && Math.Abs(reflexo.Ficha.Ki - reflexo.Ficha.MaxKi) < 1e-6,
				   $"vida {reflexo.Combate.Corpo.Vida():0.##}, ki {reflexo.Ficha.Ki:0}/{reflexo.Ficha.MaxKi:0}");
		AfirmarMvv("...e a queda foi dada por encerrada (o bit nao fica preso pro proximo KO)",
				   !reflexo.CaiuNaMente);
		AfirmarMvv("...e o poder dele voltou a ser o do DONO (a reancoragem do `EspelharODono`)",
				   Math.Abs(reflexo.Ficha.BP - a.Ficha.expressedBP) < 1,
				   $"{reflexo.Ficha.BP:N0} x {a.Ficha.expressedBP:N0}");

		// ============================ E A SESSAO NAO ACABOU ============================
		// As tres coisas que a definem, medidas juntas: a pessoa continua no bolso, o corpo continua
		// no mapa e o oponente continua de pe.
		// ===========================================================================
		AfirmarMvv("A SESSAO NAO ACABOU: A na mente, o corpo dele no mapa, e o oponente de pe",
				   NaMente(a) && a.BonecoLargado == corpoDeA
				   && ZoneList(fora.Hash).Contains(corpoDeA) && !reflexo.Ficha.KO,
				   $"{a.Zone} / corpo {(a.BonecoLargado == null ? "sumiu" : "no chao")}");

		// ============================ E DA PRA LUTAR DE NOVO ============================
		// O ciclo inteiro outra vez, pelo mesmo caminho: enfraquece (o reerguer reancorou no dono, ver
		// acima), soca, e o mecanismo tem que reabrir a queda. Um bit que nao se limpasse daria verde na
		// primeira volta e travaria o reflexo no chao pra sempre na segunda.
		// ============================================================================
		Enfraquecer();
		double vidaDoReflexo = reflexo.Combate.Corpo.Vida();
		int socos2 = 0;
		while (socos2 < 60 && !reflexo.Ficha.KO)
		{
			reflexo.Pos = a.Pos + new Vec2(24, 0);
			a.Facing = MoveRules.FacingFrom(reflexo.Pos - a.Pos, a.Facing);
			a.Combate.Recarga = 0;
			Atacar(a, Protocol.Golpe.Pesado);
			socos2++;
		}
		AfirmarMvv($"DA PRA LUTAR DE NOVO: o mesmo reflexo torna a apanhar e a cair ({socos2} golpe(s))",
				   reflexo.Ficha.KO && reflexo.Combate.Corpo.Vida() < vidaDoReflexo - 1e-9,
				   $"vida {vidaDoReflexo:0.##} -> {reflexo.Combate.Corpo.Vida():0.##}");

		UmTique();
		AfirmarMvv("...e a segunda queda reabre o ciclo, com o prazo do DM outra vez",
				   reflexo.CaiuNaMente && reflexo.Combate.NocauteRestante > MeleeResolver.SegundosDeNocaute,
				   $"{reflexo.CaiuNaMente} / {reflexo.Combate.NocauteRestante:0.00}");
		AfirmarMvv("...e o transe continua aberto (parceiro de treino infinito)",
				   NaMente(a) && a.BonecoLargado != null, a.Zone.ToString());

		UsarHabilidade(a, "sairdamente");
		AfirmarMvv("e quem encerra a sessao continua sendo a VONTADE de A",
				   !NaMente(a) && a.CloneId == 0 && a.BonecoLargado == null
				   && !ZoneList(fora.Hash).Any(p => p.DonoDoCorpoLargado != 0), a.Zone.ToString());

		a.Ficha.med = false;
		CurarPorInteiro(a);
		PorBp(a, 500_000);
		a.Ficha.Ki = a.Ficha.MaxKi;
		MoveToZone(a.Id, fora, new Vec2(4000, 4000));
		MoveToZone(b.Id, fora, new Vec2(4064, 4000));
	}

	// =====================================================================
	// 11) O BONECO APANHA COMO UM CORPO, MAS NAO CONTA COMO UM
	// =====================================================================
	/// <summary>
	/// ============================ AS DUAS PERGUNTAS SAO OPOSTAS, E AS DUAS RESPOSTAS SAO CERTAS ============================
	/// Um corpo largado tem que ser **alcancavel** (senao "se elas TE BATEREM vc ACORDA" nao existe) e ao
	/// mesmo tempo **invisivel pra toda conta de corpo do servidor** (senao ele e um segundo lutador
	/// carregando a ficha de alguem). As duas saem da mesma decisao da Camada 1 -- *"ele entra na ZONA e
	/// **nao** no `_players`"* --, e por isso as duas sao medidas juntas:
	///
	///   * a `ZoneList` e "o que existe NESTE lugar": dela saem o snapshot, o `AlvoNaFrente` e o
	///     `PresaDaFera`. Estar nela e o `density = 1` + `attackable = 1` do `mob/npc/mind_dummy`
	///     (`MindMeditate.dm:130-133`) -- **o DM tambem faz dele um alvo, de proposito**;
	///   * o `_players` e "um corpo que o servidor SIMULA": dele saem a media que escala todo NPC que
	///     nascer, o topo de BP do servidor, o censo de corpos sem dono e o anti-lag do povoamento.
	///
	/// ============================ E A FERA ENXERGA O CORPO LARGADO -- ISSO NAO E UM DEFEITO ============================
	/// `PresaDaFera` e *"o corpo em pe mais proximo, seja quem for"*, e o corpo largado e um corpo em pe.
	/// A cadeia inteira fecha: o macaco vai ate ele, soca, e o soco cai no MESMO `MarcarAgressao` do soco
	/// de um jogador -- o dono acorda e o corpo sai do mundo no fim do tique. **Nao ha um caminho de NPC
	/// separado pra manter**, e nao ha como a fera ficar batendo num fantasma: o alvo dela e derivado a
	/// cada tique, nunca guardado.
	///
	/// Esta familia mede essa cadeia inteira em vez de supor que ela existe -- se um dia alguem "consertar"
	/// tirando o corpo largado da lista de zona, o meditador volta a sumir do mapa (o defeito original) e a
	/// primeira metade fica vermelha na mesma linha em que a promessa quebra.
	/// ==============================================================================================================
	/// </summary>
	private void OBonecoApanhaMasNaoConta(ServerPlayer a, ServerPlayer b, ZoneKey fora, ServerPlayer? carrasco)
	{
		GD.Print("[menteviva] -- 11) o boneco apanha como um corpo, mas nao conta como um");

		MoveToZone(a.Id, fora, new Vec2(4000, 4000));
		MoveToZone(b.Id, fora, new Vec2(4064, 4000));
		CurarPorInteiro(a);

		// ============================ OS DOIS BP PRECISAM SER DIFERENTES ============================
		// A media do servidor conta **so quem tem `Peer`** -- ou seja A e B, e mais ninguem (o carrasco
		// desta bancada e um corpo sem dono e ja esta fora dela). Com os dois no mesmo numero, "a media
		// com o boneco" e "a media sem o boneco" dao IGUAL, e a afirmacao de baixo passaria verde sem
		// medir nada: foi assim que ela nasceu, e foi a propria rodada que mostrou.
		// =======================================================================================
		PorBp(a, 2_000_000);
		a.Ficha.med = true;
		a.Ficha.train = false;

		int semDonoAntes = _players.Values.Count(p => p.Peer == null);
		double mediaAntes = MediaDoServidor();

		UsarHabilidade(a, "mente");
		if (a.BonecoLargado is not { } boneco) { AfirmarMvv("A largou o corpo pra a familia 11", false); return; }

		// ============================ A CONTA DE CORPOS SEM DONO ============================
		// A porta faz DUAS coisas: larga o corpo e ergue o reflexo. So UMA delas e um corpo de verdade --
		// e o contraexemplo esta na mesma cena, o que e o melhor lugar pra ele estar.
		// ================================================================================
		int semDonoDepois = _players.Values.Count(p => p.Peer == null);
		AfirmarMvv("a porta poe UM corpo novo na conta de corpos sem dono -- o reflexo -- e nao dois",
				   semDonoDepois == semDonoAntes + 1, $"{semDonoAntes} -> {semDonoDepois}");
		AfirmarMvv("...e a metade que conta e o REFLEXO (o contraexemplo, na mesma cena)",
				   a.CloneId != 0 && _players.ContainsKey(a.CloneId), $"{a.CloneId}");
		AfirmarMvv("...e o boneco nao esta em `_players` de jeito nenhum (nem por id, nem por instancia)",
				   !_players.ContainsKey(boneco.Id) && !_players.ContainsValue(boneco));

		// ============================ A MEDIA QUE ESCALA TODO NPC QUE NASCER ============================
		// Ela le `_players` e exige `Peer`, entao sao DOIS motivos pra o boneco ficar de fora. A afirmacao
		// nao e "o numero nao mudou" (isso seria verdade por acaso): ela compara o valor medido com o
		// valor que sairia se o boneco contasse -- e os dois so diferem porque a preparacao poe A e B em
		// BPs diferentes. Ver o bloco da preparacao.
		// ==========================================================================================
		double media = MediaDoServidor();
		double soma = 0; int quantos = 0;
		foreach (ServerPlayer p in _players.Values)
			if (p.Peer != null && p.Ficha is { BP: > 0 }) { soma += p.Ficha.BP; quantos++; }
		double seContasse = (soma + a.Ficha.BP) / (quantos + 1);

		AfirmarMvv("a media que escala todo NPC que nascer nao mudou por causa do corpo largado",
				   Math.Abs(media - mediaAntes) < 1e-6, $"{mediaAntes:N0} -> {media:N0}");
		AfirmarMvv("...e ela NAO e a media que sairia se ele contasse (a medida nao e verdade por acaso)",
				   Math.Abs(media - seContasse) > 1, $"{media:N0} x {seContasse:N0}");

		// ============================ O ANTI-LAG DO POVOAMENTO ============================
		// `_zonasComGente` e o `planet_player_count` do DM, e e ele que decide se os cidadaos de um
		// planeta congelam. Um corpo largado sozinho numa zona nao pode acordar uma cidade inteira: nao
		// ha ninguem olhando pra ela. B sai da zona pra a pergunta ter uma resposta -- com ele la, a zona
		// contaria por ele e a medida seria vazia.
		// =============================================================================
		ZoneKey longe = ZoneKey.Premade("Namek");
		MoveToZone(b.Id, longe, new Vec2(4000, 4000));
		TickDosCorposSemDono(Protocol.TickSeconds);
		AfirmarMvv("uma zona onde so ha o corpo largado NAO conta como zona com gente (o anti-lag)",
				   !_zonasComGente.Contains(fora.Hash) && _zonasComGente.Contains(longe.Hash),
				   $"fora={_zonasComGente.Contains(fora.Hash)} longe={_zonasComGente.Contains(longe.Hash)}");

		MoveToZone(b.Id, fora, boneco.Pos + new Vec2(96, 0));
		TickDosCorposSemDono(Protocol.TickSeconds);
		AfirmarMvv("...e volta a contar quando UMA PESSOA pisa nela (a outra metade)",
				   _zonasComGente.Contains(fora.Hash));

		// ============================ A OUTRA PONTA: ELE E UM ALVO ============================
		if (carrasco == null) { AfirmarMvv("o carrasco da bancada existe pra a familia 11", false); }
		else
		{
			MoveToZone(carrasco.Id, fora, boneco.Pos + new Vec2(24, 0));
			carrasco.Facing = MoveRules.FacingFrom(boneco.Pos - carrasco.Pos, carrasco.Facing);
			carrasco.Combate.Letal = false;
			carrasco.Ficha.Ki = carrasco.Ficha.MaxKi;

			// B SAI DE PERTO: `PresaDaFera` devolve o corpo em pe MAIS PROXIMO, e com B colado no
			// boneco a medida diria "a fera acha o mais perto" e nao "a fera acha o corpo largado".
			MoveToZone(b.Id, fora, boneco.Pos + new Vec2(2000, 0));

			AfirmarMvv("a fera enxerga o corpo largado como presa (o `attackable = 1` do `mind_dummy`)",
					   PresaDaFera(carrasco, soJogadores: false) == boneco, PresaDaFera(carrasco, soJogadores: false)?.Name ?? "ninguem");
			AfirmarMvv("...e o cone do soco dela tambem o encontra",
					   AlvoNaFrente(carrasco) == boneco, AlvoNaFrente(carrasco)?.Name ?? "ninguem");

			_acordar.Clear();
			carrasco.Combate.Recarga = 0;
			Atacar(carrasco, Protocol.Golpe.Leve);
			AfirmarMvv("...e o golpe de um corpo SEM DONO acorda o dono pelo MESMO funil (nao ha caminho de NPC a parte)",
					   _acordar.Contains(a.Id), $"fila: [{string.Join(",", _acordar)}]");

			TickDeQuemVolta();
			AfirmarMvv("...e A voltou pro corpo, que saiu do mundo no fim do tique",
					   !NaMente(a) && a.BonecoLargado == null && !ZoneList(fora.Hash).Contains(boneco),
					   a.Zone.ToString());
			AfirmarMvv("...e a fera nao fica batendo num fantasma: o alvo dela e derivado, e ja e outro",
					   PresaDaFera(carrasco, soJogadores: false) != boneco, PresaDaFera(carrasco, soJogadores: false)?.Name ?? "ninguem");

			MoveToZone(carrasco.Id, fora, new Vec2(4200, 4000));
		}

		AfirmarMvv("e a conta de corpos sem dono voltou ao que era (nada ficou pendurado)",
				   _players.Values.Count(p => p.Peer == null) == semDonoAntes,
				   $"{semDonoAntes} -> {_players.Values.Count(p => p.Peer == null)}");

		MoveToZone(a.Id, fora, new Vec2(4000, 4000));
		MoveToZone(b.Id, fora, new Vec2(4064, 4000));
		a.Ficha.med = false;
		CurarPorInteiro(a);
		PorBp(a, 500_000);
		a.Ficha.Ki = a.Ficha.MaxKi;
	}

	// =====================================================================
	// 7) O VISITANTE -- e, com ele, a ferida, o Zenkai e a raiva
	// =====================================================================
	/// <summary>
	/// *"se um player meditasse AO SEU LADO ele entraria na sua mente e poderia lutar com vc"*.
	///
	/// **ESTA E A FAMILIA QUE SO EXISTE COM DOIS PROCESSOS.** O que B procura no chao e o CORPO
	/// LARGADO de A -- um objeto que so nasce porque alguem com `Peer` atravessou a porta. Com corpos
	/// forjados, o boneco tem que ser montado a mao (e a `--menteteste` monta), e ai o que se mede e
	/// a pergunta e nao o encontro.
	///
	/// As tres regras do pedido sao cobradas DENTRO desta cena, porque e aqui que elas tem sentido:
	/// duas pessoas de verdade se batendo dentro da cabeca de uma delas.
	/// </summary>
	private void OVisitante(ServerPlayer a, ServerPlayer b, ZoneKey fora, ServerPlayer? carrasco)
	{
		GD.Print("[menteviva] -- 7) o visitante entra pela porta, e luta");

		foreach (ServerPlayer p in new[] { a, b })
		{
			MoveToZone(p.Id, fora, new Vec2(4000, 4000));
			p.Ficha.med = true;
			p.Ficha.train = false;
			CurarPorInteiro(p);
			PorBp(p, 500_000);
			p.AlvoId = 0;
		}
		Encostar(a, b, 32);

		// A ENTRA NA PROPRIA MENTE; B FICA AO LADO DO CORPO DELE.
		UsarHabilidade(a, "mente");
		ServerPlayer? corpoDeA = a.BonecoLargado;
		if (corpoDeA == null) { AfirmarMvv("o corpo de A ficou no chao pro visitante achar", false); return; }

		b.Pos = corpoDeA.Pos + new Vec2(32, 0);
		AfirmarMvv("B, colado no corpo largado de A, encontra a mente DELE",
				   MenteAoLado(b) == a, MenteAoLado(b)?.Name ?? "ninguem");

		// O RAIO E O `oview(1)`: a quatro tiles nao ha porta nenhuma.
		b.Pos = corpoDeA.Pos + new Vec2(4 * 32, 0);
		AfirmarMvv("a quatro tiles do corpo, nao ha mente ao lado", MenteAoLado(b) == null);
		b.Pos = corpoDeA.Pos + new Vec2(32, 0);

		int reflexoDeA = a.CloneId;
		UsarHabilidade(b, "mente");

		AfirmarMvv("B entrou na MENTE DE A (e nao num bolso vazio proprio)",
				   NaMente(b) && b.Zone.Hash == a.Zone.Hash, $"{b.Zone} vs {a.Zone}");
		AfirmarMvv("...e a mente continua sendo a de A (a zona e a DELE, e nao `De(id de B)`)",
				   DimensaoMental.Anfitriao(b.Zone) == a.Id, $"{DimensaoMental.Anfitriao(b.Zone)}");
		AfirmarMvv("...e B nunca teve reflexo (visitante nao ergue clone)", b.CloneId == 0);
		AfirmarMvv("...e o reflexo do ANFITRIAO se desfez (`add_member` deleta o clone)",
				   a.CloneId == 0 && !_players.ContainsKey(reflexoDeA), $"{a.CloneId}");
		AfirmarMvv("...e o corpo de B tambem ficou pra tras, na zona de fora",
				   b.BonecoLargado != null && b.BonecoLargado.Zone.Hash == fora.Hash);
		AfirmarMvv("...e os dois bonecos estao no chao, lado a lado, na tela de quem passar",
				   ZoneList(fora.Hash).Count(p => p.DonoDoCorpoLargado != 0) == 2,
				   $"{ZoneList(fora.Hash).Count(p => p.DonoDoCorpoLargado != 0)}");

		// ============================ O ANFITRIAO QUE ACORDA E VOLTA ============================
		// **ESTA CENA EXISTE PORQUE A BANCADA PASSOU VERDE COM O DEFEITO INJETADO.** A armadilha que o
		// `EntrarNaMente` documenta -- usar `De(id do vizinho)` no lugar da ZONA dele -- nao aparece
		// enquanto o vizinho estiver na PROPRIA mente: ali os dois valores sao o mesmo numero. Trocar
		// um pelo outro na producao nao mudava uma linha do placar.
		//
		// Ela so morde quando o vizinho **esta numa mente que nao e a dele** -- e a cena mais simples
		// que produz isso cabe em dois corpos: o anfitriao apanha e ACORDA (o corpo dele vai embora),
		// o visitante continua la dentro, e ai o anfitriao medita de novo ao lado do corpo do
		// VISITANTE. Com o defeito, ele cai no bolso VAZIO do visitante -- sozinho, num quarto branco,
		// achando que voltou pra propria cabeca.
		//
		// A volta e pelo `VoltarDeOndeEstiver`, que e exatamente o que o soco no corpo chama.
		// ====================================================================================
		VoltarDeOndeEstiver(a, "");
		AfirmarMvv("o anfitriao pode acordar deixando o visitante dentro da mente dele",
				   !NaMente(a) && NaMente(b) && ZoneList(b.Zone.Hash).Contains(b), $"{a.Zone} / {b.Zone}");

		ServerPlayer? corpoDeB = b.BonecoLargado;
		if (corpoDeB != null)
		{
			a.Pos = corpoDeB.Pos + new Vec2(32, 0);
			a.Ficha.med = true;
			AfirmarMvv("...e o corpo do VISITANTE tambem e uma porta (ele esta numa mente, e isso basta)",
					   MenteAoLado(a) == b, MenteAoLado(a)?.Name ?? "ninguem");

			UsarHabilidade(a, "mente");
			AfirmarMvv("voltando pela porta do visitante, o anfitriao cai ONDE ELE ESTA",
					   NaMente(a) && a.Zone.Hash == b.Zone.Hash, $"{a.Zone} vs {b.Zone}");
			AfirmarMvv("...e o bolso continua sendo o DELE, e nao o do visitante (que estaria vazio)",
					   DimensaoMental.Anfitriao(a.Zone) == a.Id
					   && ZoneList(a.Zone.Hash).Count(EhPessoa) == 2,
					   $"anfitriao {DimensaoMental.Anfitriao(a.Zone)}, "
					   + $"{ZoneList(a.Zone.Hash).Count(EhPessoa)} pessoa(s) no bolso");
		}
		else AfirmarMvv("o visitante tem corpo largado pra servir de porta", false);

		corpoDeA = a.BonecoLargado;
		if (corpoDeA == null) { AfirmarMvv("o anfitriao largou o corpo de novo", false); return; }

		// ============================ 3) A FERIDA NAO VOLTA PRO CORPO REAL ============================
		// *"la dentro os FERIMENTOS NAO SAO TRANSFERIDOS pro corpo real"*. Medida MEMBRO A MEMBRO, com
		// a pancada dada pelo resolvedor de golpe de sempre -- e nao escrita na mao: o que se prova e
		// que a luta la dentro roda pelo MESMO combate (aparo, esquiva, quebra) e mesmo assim nao sai
		// de la.
		// ==========================================================================================
		GD.Print("[menteviva] -- 3) a ferida feita na mente nao volta pro corpo real");

		var antesDaLuta = new Dictionary<string, double>(StringComparer.Ordinal);
		foreach (BodyPart p in a.Combate.Corpo.Partes) antesDaLuta[p.Nome] = p.Vida;
		double hpAntes = a.Ficha.HP;

		PorBp(b, 5_000_000);
		b.Pos = a.Pos + new Vec2(24, 0);
		b.Facing = MoveRules.FacingFrom(a.Pos - b.Pos, b.Facing);
		_acordar.Clear();

		int golpes = 0;
		while (golpes < 20 && a.Combate.Corpo.Vida() >= hpAntes - 1e-9)
		{
			b.Combate.Recarga = 0;
			Atacar(b, Protocol.Golpe.Pesado);
			golpes++;
		}

		AfirmarMvv($"dentro da mente o golpe de B fere A de verdade ({golpes} golpe(s))",
				   a.Combate.Corpo.Vida() < hpAntes - 1e-9,
				   $"{hpAntes:0.##} -> {a.Combate.Corpo.Vida():0.##}");
		AfirmarMvv("...e bater em quem esta DENTRO da mente nao 'acorda' ninguem (ali nao ha boneco)",
				   _acordar.Count == 0, $"fila: [{string.Join(",", _acordar)}]");

		// ============================ 4 E 6, NA MESMA CHAMADA ============================
		// O gate mora no `AoPerderALuta`, que e o funil unico da derrota -- entao a mesma chamada
		// responde pelo Zenkai (regra 2) e pelo LUTO (a raiva que abre SSJ).
		// =============================================================================
		GD.Print("[menteviva] -- 4 e 6) sem Zenkai, e sem raiva de verdade, dentro da mente");

		if (carrasco == null) { AfirmarMvv("o carrasco da bancada existe", false); }
		else
		{
			// O CARRASCO ENTRA NA MENTE. Ele nao e um corpo de verdade e nao precisa ser: o que ele
			// da a esta cena e um NUMERO (`expressedBP`, o poder que o Zenkai absorve) e uma
			// assinatura. Os dois papeis que precisam ser de verdade -- quem PERDE (e portanto tem
			// Zenkai, save e conta) e quem ASSISTE (e portanto tem lista de amizade) -- sao A e B.
			MoveToZone(carrasco.Id, a.Zone, a.Pos + new Vec2(48, 0));

			// B E AMIGO DE A: sem o vinculo, `LutoPorQueda` recusa e a familia 6 mediria o nada.
			b.Social.Amizade[a.Assinatura] = Convivio.ExigenciaDeAmigo;
			b.RaivaLendariaAte = b.FuriaExtremaAte = 0;
			a.Ficha.zenkaiReady = 0;
			double bpAntes = a.Ficha.BP;

			AoPerderALuta(a, carrasco, morreu: true);

			AfirmarMvv("DENTRO da mente, perder nao paga Zenkai",
					   Math.Abs(a.Ficha.BP - bpAntes) < 1e-6, $"{a.Ficha.BP:0} != {bpAntes:0}");
			AfirmarMvv("DENTRO da mente, ver o amigo cair nao acende raiva de verdade",
					   b.RaivaLendariaAte == 0 && b.FuriaExtremaAte == 0,
					   $"{b.RaivaLendariaAte} / {b.FuriaExtremaAte}");
		}

		// ============================ A SAIDA: CADA UM NO SEU CORPO ============================
		Vec2 ondeEstaOCorpoDeA = corpoDeA.Pos;
		Vec2 ondeEstaOCorpoDeB = b.BonecoLargado?.Pos ?? default;
		var membrosNaSaida = new Dictionary<string, double>(StringComparer.Ordinal);
		foreach (BodyPart p in a.Combate.Corpo.Partes) membrosNaSaida[p.Nome] = p.Vida;

		UsarHabilidade(a, "sairdamente");
		UsarHabilidade(b, "sairdamente");

		AfirmarMvv("A voltou pro corpo DELE, onde ele estava",
				   !NaMente(a) && a.Zone.Hash == fora.Hash
				   && (a.Pos - ondeEstaOCorpoDeA).LengthSquared < 1e-6, $"{a.Zone}");
		AfirmarMvv("B voltou pro corpo DELE, e nao pro de A",
				   !NaMente(b) && b.Zone.Hash == fora.Hash
				   && (b.Pos - ondeEstaOCorpoDeB).LengthSquared < 1e-6
				   && (b.Pos - a.Pos).LengthSquared > 1e-6, $"{b.Zone}");
		AfirmarMvv("...e nao sobrou boneco nenhum no chao",
				   a.BonecoLargado == null && b.BonecoLargado == null
				   && !ZoneList(fora.Hash).Any(p => p.DonoDoCorpoLargado != 0));

		// A REGRA 1, COBRADA MEMBRO A MEMBRO.
		int devolvidos = 0, feridosNaMente = 0;
		foreach (BodyPart p in a.Combate.Corpo.Partes)
		{
			if (!antesDaLuta.TryGetValue(p.Nome, out double entrada)) continue;
			if (membrosNaSaida.TryGetValue(p.Nome, out double naMente) && naMente < entrada - 1e-9) feridosNaMente++;
			if (Math.Abs(p.Vida - entrada) < 1e-6) devolvidos++;
		}
		AfirmarMvv($"a luta feriu {feridosNaMente} membro(s) la dentro (a medida nao e vazia)",
				   feridosNaMente > 0, $"{feridosNaMente}");
		AfirmarMvv("na saida, TODO membro volta ao valor exato da entrada -- e nao curado",
				   devolvidos == a.Combate.Corpo.Partes.Count, $"{devolvidos}/{a.Combate.Corpo.Partes.Count}");
		AfirmarMvv("...e o HP volta sozinho, porque ele e derivado dos membros",
				   Math.Abs(a.Ficha.HP - hpAntes) < 1e-6, $"{a.Ficha.HP:0.##} != {hpAntes:0.##}");
		AfirmarMvv("...e a foto foi consumida (nao restaura duas vezes)", a.FotoDaMente == null);

		// ============================ AS METADES CONTRARIAS, LA FORA ============================
		// A mesma pancada, o mesmo carrasco, os mesmos vinculos -- so o LUGAR muda. Sem esta metade,
		// um `return` no lugar errado deixaria as tres familias verdes medindo o nada.
		// ====================================================================================
		GD.Print("[menteviva] -- 3/4/6 (a outra metade): a mesma cena, FORA da mente");

		double hpLaFora = a.Ficha.HP;
		b.Pos = a.Pos + new Vec2(24, 0);
		b.Facing = MoveRules.FacingFrom(a.Pos - b.Pos, b.Facing);
		int golpesFora = 0;
		while (golpesFora < 20 && a.Combate.Corpo.Vida() >= hpLaFora - 1e-9)
		{
			b.Combate.Recarga = 0;
			Atacar(b, Protocol.Golpe.Pesado);
			golpesFora++;
		}
		AfirmarMvv("FORA da mente a mesma pancada FICA no corpo",
				   a.Combate.Corpo.Vida() < hpLaFora - 1e-9,
				   $"{hpLaFora:0.##} -> {a.Combate.Corpo.Vida():0.##}");

		if (carrasco != null)
		{
			MoveToZone(carrasco.Id, fora, a.Pos + new Vec2(48, 0));
			b.RaivaLendariaAte = b.FuriaExtremaAte = 0;
			a.Ficha.zenkaiReady = 0;
			double bpAntes = a.Ficha.BP;

			AoPerderALuta(a, carrasco, morreu: true);

			AfirmarMvv("FORA da mente a mesma derrota PAGA Zenkai",
					   a.Ficha.BP > bpAntes + 1e-6, $"{a.Ficha.BP:0} == {bpAntes:0}");
			AfirmarMvv("FORA da mente ver o amigo cair ACENDE raiva de verdade",
					   b.FuriaExtremaAte > 0 || b.RaivaLendariaAte > 0,
					   $"{b.RaivaLendariaAte} / {b.FuriaExtremaAte}");
		}

		foreach (ServerPlayer p in new[] { a, b })
		{
			CurarPorInteiro(p);
			PorBp(p, 500_000);
			p.Ficha.med = false;
			p.RaivaLendariaAte = p.FuriaExtremaAte = 0;
		}
	}

	// =====================================================================
	// 1b/2b) O LEME DA NAVE -- O MESMO MECANISMO, O OUTRO SISTEMA
	// =====================================================================
	/// <summary>
	/// *"e na nave, ao pilotar ou observar, teu corpo vai ficar la parado enquanto vc controla a
	/// nave, e caso alguem te bata vc volta pro corpo principal"*.
	///
	/// ============================ POR QUE ELA REPETE A FAMILIA 1 ============================
	/// Porque a promessa do dono e que **e um mecanismo so**, e uma promessa dessas so tem valor se
	/// alguem a medir nos dois sistemas. Se amanha alguem escrever um boneco proprio pra nave -- que
	/// e literalmente o defeito que o cabecalho do `PilotarDaPonte` diz ter corrigido --, esta
	/// familia continua verde e a de cima tambem: as duas passariam a medir dois mecanismos.
	///
	/// O QUE SO DAQUI SE VE E A POSE: aqui o corpo fica **de pe**, e nao meditando. E a prova de que
	/// a pose e derivada do dono e nao um campo do boneco -- o mesmo `LargarOCorpo`, dois desenhos.
	/// E o `wake_mode` do DM, que aqui nem existe: quem responde "de onde eu volto" e o servidor.
	/// </summary>
	private void OLemeDaNave(ServerPlayer a, ServerPlayer b, ZoneKey fora)
	{
		GD.Print("[menteviva] -- 1b/2b) o leme da nave: o MESMO mecanismo, o outro sistema");

		// UMA CAPITAL SHIP FORJADA NA LISTA, como a `--naveviagemteste` faz: o que esta sob teste e o
		// leme, e nao a aba Tech -- assentar uma nave de dois milhoes de zeni nao e o caminho que
		// esta bancada mede.
		var nave = new Nave
		{
			Id = _proximaNaveId++,
			Tipo = NaveGrande.Tipo,
			X = 4200, Y = 4200,
			ArmaduraMax = 1000, Armadura = 1000,
		};
		nave.PorZona(fora);
		_naves.Add(nave);

		ZoneKey ponte = NaveGrande.ZonaDoInterior(nave.Id);
		try
		{
			// OS DOIS NA PONTE, o piloto colado no console -- e o `set src in oview(1)` do
			// `obj/ShipControl`, que o `NaveDaPonteDe` porta contra a celula da planta.
			MoveToZone(a.Id, ponte, NaveGrande.PixelDe(NaveGrande.CelDoConsole));
			MoveToZone(b.Id, ponte, NaveGrande.PixelDe((NaveGrande.CelDoConsole.X, NaveGrande.CelDoConsole.Y + 2)));
			a.Ficha.med = false;   // ninguem assume um leme meditando

			// ============================ "AO PILOTAR **OU OBSERVAR**" ============================
			// O dono nomeou os dois. OBSERVAR nao larga corpo nenhum -- e nao precisa: ele e um
			// relatorio, o corpo nunca sai da ponte. Esta linha existe pra que a ausencia seja
			// MEDIDA e nao suposta: se alguem um dia fizer o observar mandar a camera pra fora sem
			// passar pelo `LargarOCorpo`, e aqui que aparece.
			// ==================================================================================
			ComandoDaNaveGrande(a, "nave_observar");
			AfirmarMvv("OBSERVAR da ponte nao tira ninguem do corpo (nao ha camera pra mandar embora)",
					   a.BonecoLargado == null && a.Zone.Hash == ponte.Hash, a.Zone.ToString());

			// O COMANDO DE PRODUCAO -- o mesmo `nave_pilotar` que a tecla E dispara.
			ComandoDaNaveGrande(a, "nave_pilotar");

			AfirmarMvv("assumir o leme tira A da ponte", a.Zone.Hash != ponte.Hash, a.Zone.ToString());
			AfirmarMvv("...e registra o piloto", nave.PilotoId == a.Id, $"{nave.PilotoId}");

			ServerPlayer? corpo = a.BonecoLargado;
			if (corpo == null) { AfirmarMvv("o corpo do piloto ficou na ponte", false, "sem boneco"); return; }

			AfirmarMvv("o CORPO do piloto ficou na PONTE, visivel na lista de zona de quem esta la",
					   corpo.Zone.Hash == ponte.Hash && ZoneList(ponte.Hash).Contains(corpo)
					   && ZoneList(ponte.Hash).Contains(b), corpo.Zone.ToString());
			AfirmarMvv("...e e o MESMO mecanismo da mente (a mesma `Ficha`, o mesmo `Combate`)",
					   ReferenceEquals(corpo.Ficha, a.Ficha) && ReferenceEquals(corpo.Combate, a.Combate));

			// A POSE: DE PE, e nao meditando. E a diferenca inteira entre os dois sistemas.
			EntityState noFio = NoFio(corpo);
			AfirmarMvv("no fio, o corpo do piloto esta DE PE (e nao meditando)",
					   noFio.Pose == Protocol.Pose.Normal, noFio.Pose.ToString());
			AfirmarMvv("...e o bit `SemRedeas` continua FALSO nele (aquilo e o olho da fera, nao isto)",
					   !noFio.SemRedeas);

			// BATER NA PONTE TRAZ O PILOTO DE VOLTA.
			b.Pos = corpo.Pos + new Vec2(24, 0);
			b.Facing = MoveRules.FacingFrom(corpo.Pos - b.Pos, b.Facing);
			b.Combate.Recarga = 0;
			_acordar.Clear();

			AfirmarMvv("o corpo do piloto tambem e um alvo do soco", AlvoNaFrente(b) == corpo,
					   AlvoNaFrente(b)?.Name ?? "ninguem");
			Atacar(b, Protocol.Golpe.Leve);
			AfirmarMvv("bater nele enfileira a volta do piloto (o MESMO `MarcarAgressao`)",
					   _acordar.Contains(a.Id), $"fila: [{string.Join(",", _acordar)}]");

			TickDeQuemVolta();
			AfirmarMvv("drenada a fila, A esta de volta na PONTE, no proprio corpo",
					   a.Zone.Hash == ponte.Hash && a.BonecoLargado == null, a.Zone.ToString());
			AfirmarMvv("...e o leme ficou LIVRE (senao a nave fica trancada pra todo mundo)",
					   nave.PilotoId == 0, $"{nave.PilotoId}");
			AfirmarMvv("...e nao sobrou corpo de pe na ponte",
					   !ZoneList(ponte.Hash).Any(p => p.DonoDoCorpoLargado != 0));

			// ============================ O TERCEIRO RAMO: A VOLTA DERIVADA ============================
			// `VoltarDeOndeEstiver` nao guarda `wake_mode`: ele pergunta ONDE a pessoa esta. Esta
			// afirmacao e a prova de que os dois ramos existem e sao distinguiveis -- o mesmo corpo,
			// largado pelo leme, NAO e uma porta de mente pra quem passa do lado.
			// ======================================================================================
			ComandoDaNaveGrande(a, "nave_pilotar");
			ServerPlayer? corpoDoLeme = a.BonecoLargado;
			if (corpoDoLeme != null)
			{
				b.Pos = corpoDoLeme.Pos + new Vec2(32, 0);
				b.Ficha.med = true;
				AfirmarMvv("o corpo largado pelo LEME nao e uma porta de mente pra quem medita do lado",
						   MenteAoLado(b) == null, MenteAoLado(b)?.Name ?? "ninguem");
				b.Ficha.med = false;
				VoltarDeOndeEstiver(a, "");
			}
			AfirmarMvv("largar o leme por vontade propria devolve o corpo do mesmo jeito",
					   a.Zone.Hash == ponte.Hash && a.BonecoLargado == null && nave.PilotoId == 0);
		}
		finally
		{
			_naves.Remove(nave);
			MoveToZone(a.Id, fora, new Vec2(4000, 4000));
			MoveToZone(b.Id, fora, new Vec2(4064, 4000));
			GravarNaves();
		}
	}

	// =====================================================================
	// 8) AS BORDAS
	// =====================================================================
	/// <summary>
	/// As tres maneiras de a viagem deixar de fazer sentido sem ninguem ter batido em nada, e a
	/// resposta as tres e a mesma: **acorda**, nunca "fica preso".
	///
	/// Cada uma passa pelo funil de producao (`BordasDeQuemEstaFora`, chamado por jogador dentro do
	/// `TickCombate`) -- e nao por uma chamada direta ao `VoltarProCorpo`, que provaria a volta e nao
	/// o GATILHO dela.
	/// </summary>
	private void AsBordas(ServerPlayer a, ServerPlayer b, ZoneKey fora)
	{
		GD.Print("[menteviva] -- 8) as bordas: morte, nocaute e corpo sumido");

		// --- a) O CORPO CAI (nocaute) ---------------------------------------------------
		a.Ficha.med = true;
		UsarHabilidade(a, "mente");
		if (!NaMente(a)) { AfirmarMvv("A entrou na mente pra a familia das bordas", false); return; }

		_acordar.Clear();
		a.Combate.Nocautear(30);
		BordasDeQuemEstaFora(a);
		// O ROTULO ANTIGO DIZIA "o `MIND_KO_EJECT` do DM" E ESTAVA ERRADO: aquele define e um ATRASO
		// (50 tiques = 5 s de nocaute dentro da mente antes da expulsao, `MindMeditate.dm:453-462`), e
		// aqui a expulsao e IMEDIATA. O que esta bancada mede e o gatilho -- "cair enfileira a volta"
		// --, que e a metade portada. O atraso e uma decisao do dono que ainda nao foi tomada.
		AfirmarMvv("o corpo NOCAUTEADO la fora enfileira a volta (imediata: o atraso do `MIND_KO_EJECT` nao foi portado)",
				   _acordar.Contains(a.Id), $"fila: [{string.Join(",", _acordar)}]");
		TickDeQuemVolta();
		AfirmarMvv("...e A esta de volta no corpo, caido, e nao preso na mente",
				   !NaMente(a) && a.BonecoLargado == null, a.Zone.ToString());

		a.Ficha.KO = false;
		a.Combate.NocauteRestante = 0;
		CurarPorInteiro(a);

		// --- b) O CORPO MORRE -----------------------------------------------------------
		a.Ficha.med = true;
		UsarHabilidade(a, "mente");
		_acordar.Clear();
		a.Ficha.dead = true;
		BordasDeQuemEstaFora(a);
		AfirmarMvv("o corpo MORTO la fora tambem enfileira a volta",
				   _acordar.Contains(a.Id), $"fila: [{string.Join(",", _acordar)}]");
		TickDeQuemVolta();
		AfirmarMvv("...e o renascimento acontece no mundo, e nao dentro do bolso da mente",
				   !NaMente(a) && a.BonecoLargado == null, a.Zone.ToString());
		a.Ficha.dead = false;
		CurarPorInteiro(a);

		// --- c) O CORPO SUMIU DO MUNDO --------------------------------------------------
		// A zona foi descarregada, a nave explodiu com ele dentro, um admin o apagou. Nao ha pra onde
		// voltar -- e a resposta certa e o destino de emergencia, nunca "fica preso".
		a.Ficha.med = true;
		UsarHabilidade(a, "mente");
		ServerPlayer? sumido = a.BonecoLargado;
		if (sumido != null)
		{
			ZoneList(sumido.Zone.Hash).Remove(sumido);
			_acordar.Clear();
			BordasDeQuemEstaFora(a);
			AfirmarMvv("o corpo que SUMIU do mundo tambem enfileira a volta",
					   _acordar.Contains(a.Id), $"fila: [{string.Join(",", _acordar)}]");
			TickDeQuemVolta();
			AfirmarMvv("...e A cai no destino de emergencia, fora de qualquer mente -- nunca preso",
					   !NaMente(a) && a.BonecoLargado == null, a.Zone.ToString());
		}
		else AfirmarMvv("havia um corpo largado pra sumir", false);

		MoveToZone(a.Id, fora, new Vec2(4000, 4000));
		MoveToZone(b.Id, fora, new Vec2(4064, 4000));
		a.Ficha.med = false;
		CurarPorInteiro(a);
	}

	// =====================================================================
	// 9) A FERA NAO MEDITA E NAO PILOTA
	// =====================================================================
	/// <summary>
	/// OOZARU E FURIA LENDARIA NAO REGREDIRAM, e a diferenca entre os dois estados esta no cabecalho
	/// do `GameServer.CorpoLargado.cs`:
	///
	///   * **possuido** e `Cerebro != null` -- *o servidor dirige este corpo*;
	///   * **corpo largado** e `Cerebro == null` **e** `Peer == null` -- **ninguem** o dirige.
	///
	/// As duas pontas sao medidas aqui: o possuido nao larga o corpo, e o corpo largado nao acende o
	/// bit da possessao. Esse bit e o que pinta o olho da fera -- um corpo meditando com olho de
	/// berserk seria a mentira que os dois sistemas passariam a contar um sobre o outro.
	/// </summary>
	private void AFeraNaoLargaOCorpo(ServerPlayer a, ZoneKey fora)
	{
		GD.Print("[menteviva] -- 9) o Oozaru e a furia: quem esta possuido nao larga o corpo");

		// A FURIA VEM ANTES DO MACACO, e a ordem foi decidida por uma medida: a estreia do Oozaru marca
		// uma CENA (`CenaSegundos`), e uma bancada sincrona nao roda o relogio que a consome -- entao o
		// corpo continuava "em cena" e o `TickDaFuriaLendaria` saia pela primeira porta dele, calado.
		// Rodando primeiro, a furia mede a si mesma; rodando depois, ela media o rastro da familia
		// anterior. (A regra "corpo preso numa cena nao perde as redeas" e da `--formasteste`.)
		AFuriaTambemNaoLargaOCorpo(a);

		GD.Print("[menteviva] -- 9a) o Oozaru: o servidor assume o corpo, e a porta fecha");
		bool temRabo = a.Combate.Corpo.Achar("Rabo") is { Decepado: false };
		if (!temRabo || !string.Equals(a.Race, "Saiyan", StringComparison.OrdinalIgnoreCase))
		{
			// PULA E DIZ QUE PULOU: um bloco que some calado daria uma bancada verde que nao testou
			// nada, e e assim que uma regressao passa.
			AfirmarMvv($"o corpo A da pra virar fera ({a.Race}, rabo {temRabo}) -- familia 9 PULADA", false,
					   "precisa de Saiyajin com rabo");
			return;
		}

		a.Ficha.med = false;
		a.Forma.Maestria.Por(Jandirus.Core.Forms.Oozaru.IdRegular, 0);
		Apeshit(a);

		// O PRAZO DE GRACA E VENCIDO NA MAO -- e o mesmo recurso da `--formasteste`: uma bancada
		// sincrona nao gasta tempo real, e esperar seis segundos num teste seria um `Thread.Sleep`.
		a.OozaruAte -= (long)(Jandirus.Core.Forms.Oozaru.SegundosDeGraca * 1000) + 500;
		TickDoOozaru(a, 0.1);

		AfirmarMvv("vencido o prazo, o SERVIDOR assume o corpo (a fera)", SemAsRedeas(a), $"{a.Cerebro != null}");
		if (SemAsRedeas(a))
		{
			a.Ficha.med = true;
			UsarHabilidade(a, "mente");
			AfirmarMvv("POSSUIDO nao entra na mente (nem pelo canal de habilidade, nem pelo `LargarOCorpo`)",
					   !NaMente(a) && a.BonecoLargado == null, a.Zone.ToString());
			AfirmarMvv("...e o bit `SemRedeas` do corpo possuido esta LIGADO (o contraexemplo)",
					   NoFio(a).SemRedeas);
			a.Ficha.med = false;
		}

		// DEVOLVE AS REDEAS pelo caminho de producao e confere que a porta reabre.
		ReverterOozaru(a);
		a.Cerebro = null;
		a.Oozaru = Jandirus.Core.Forms.FormaOozaru.Nao;
		a.OozaruAte = 0;

		AfirmarMvv("devolvidas as redeas, o corpo volta a ser do dono", !SemAsRedeas(a));
		a.Ficha.med = true;
		UsarHabilidade(a, "mente");
		AfirmarMvv("...e a porta da mente volta a abrir", NaMente(a) && a.BonecoLargado != null);

		// O BONECO NAO E UM CORPO POSSUIDO, e as duas metades desta afirmacao sao o coracao da
		// familia: ele nao acende o bit, e o laco que dirige corpo possuido nao o alcanca.
		if (a.BonecoLargado is { } boneco)
		{
			AfirmarMvv("o corpo LARGADO nao acende `SemRedeas` (ele nao esta possuido -- esta vazio)",
					   !NoFio(boneco).SemRedeas);
			Vec2 onde = boneco.Pos;
			TickDosCorposSemDono(0.5);
			AfirmarMvv("...e o laco que dirige corpo possuido nao mexe nele (filtra por `Cerebro`)",
					   (boneco.Pos - onde).LengthSquared < 1e-6 && boneco.Cerebro == null);
		}

		UsarHabilidade(a, "sairdamente");
		a.Ficha.med = false;
		MoveToZone(a.Id, fora, new Vec2(4000, 4000));
		CurarPorInteiro(a);
	}

	/// <summary>
	/// ============================ A SEGUNDA POSSE, PORQUE O GATE E UMA LINHA SO ============================
	/// O `LargarOCorpo` recusa por `SemAsRedeas(pl)`, que e `Cerebro != null` -- e hoje ha **dois**
	/// escritores desse campo num corpo de jogador: o macaco (`TomarAsRedeas`) e a furia lendaria
	/// (`TomarAsRedeasDaFuria`). O bloco de cima mede o primeiro. Este mede o segundo, e nao por
	/// desconfianca da linha: e porque o dono nomeou os dois no pedido, e "o Oozaru esta coberto" nao e
	/// resposta pra "a furia regrediu?".
	///
	/// ============================ A FORMA E POSTA A MAO, E ISSO E DELIBERADO ============================
	/// A escada lendaria pertence a `--formasteste`: chegar nela por dentro exigiria linhagem Legendary
	/// sorteada no nascimento, e os dois personagens desta bancada sao criados pela tela de criacao do
	/// cliente, com classe SORTEADA -- a familia daria verde por ausencia em nove de cada dez rodadas.
	/// O que esta sob teste aqui e a POSSE, e a posse le a LINHA da forma atual
	/// (`FuriaLendaria.EhDescontrolavel`), nao a classe. O ciclo em si (armar, vencer, tomar, devolver)
	/// e o de producao, chamado tique a tique.
	/// ================================================================================================
	/// </summary>
	private void AFuriaTambemNaoLargaOCorpo(ServerPlayer a)
	{
		GD.Print("[menteviva] -- 9b) a furia lendaria: o outro escritor do mesmo `Cerebro`");

		Jandirus.Core.Forms.FormaDef? lendaria = Jandirus.Core.Forms.Catalogo.Def("legendary");
		if (lendaria == null) { AfirmarMvv("o catalogo conhece a forma Legendary", false); return; }

		string formaAntes = a.Forma.Atual;
		a.Forma.Atual = lendaria.Id;
		a.Forma.Maestria.Por(lendaria.Id, 0);   // sem dominio: com 100% a furia nao pega o corpo
		a.FuriaAte = 0;

		try
		{
			// A FALHA TEM QUE DIZER QUAL DAS SEIS PORTAS FECHOU. `TickDaFuriaLendaria` sai calado por
			// linha/dominio/KO/morte/CENA/posse, e um "esperava != 0, veio 0" mandaria quem lesse o
			// placar reabrir o arquivo pra descobrir qual delas foi -- que foi exatamente o que
			// aconteceu na primeira rodada (era a cena do Oozaru).
			string porQue = $"forma='{a.Forma.Atual}' linha={lendaria.Linha} "
						  + $"maestria={a.Forma.Maestria.De(a.Forma.Atual):0.#} KO={a.Ficha.KO} "
						  + $"morto={a.Ficha.dead} cena={a.CenaSegundos:0.##}s oozaru={a.Oozaru}";

			TickDaFuriaLendaria(a, Protocol.TickSeconds);
			AfirmarMvv("na forma lendaria sem dominio, o primeiro tique ARMA o prazo de controle",
					   a.FuriaAte != 0, porQue);
			AfirmarMvv("...e enquanto o prazo corre o corpo ainda e do jogador", !SemAsRedeas(a));

			// O PRAZO VENCE NA MAO -- mesmo recurso da familia 9: bancada sincrona nao gasta tempo de
			// parede, e esperar dois minutos por uma medida seria um `Thread.Sleep`.
			a.FuriaAte = NowMs() - 1;
			TickDaFuriaLendaria(a, Protocol.TickSeconds);
			AfirmarMvv("vencido o prazo, a FURIA assume o corpo (o segundo escritor do `Cerebro`)",
					   SemAsRedeas(a), $"cerebro={a.Cerebro != null}");

			if (SemAsRedeas(a))
			{
				a.Ficha.med = true;
				UsarHabilidade(a, "mente");
				AfirmarMvv("POSSUIDO PELA FURIA tambem nao larga o corpo -- a recusa e a MESMA linha",
						   !NaMente(a) && a.BonecoLargado == null, a.Zone.ToString());
				AfirmarMvv("...e o bit `SemRedeas` esta ligado nele (o olho que o corpo largado nunca acende)",
						   NoFio(a).SemRedeas);
				a.Ficha.med = false;
			}
		}
		finally
		{
			// PELO CAMINHO DE PRODUCAO, e nao zerando o campo: `DevolverAsRedeas` e quem desfaz o resto
			// do rastro da posse.
			if (SemAsRedeas(a)) DevolverAsRedeas(a);
			a.FuriaAte = 0;
			a.Forma.Atual = formaAntes;
		}

		AfirmarMvv("devolvidas as redeas, a porta da mente reabre pro mesmo corpo", !SemAsRedeas(a));
		a.Ficha.med = true;
		UsarHabilidade(a, "mente");
		AfirmarMvv("...e o corpo volta a ficar pra tras, como em qualquer meditacao",
				   NaMente(a) && a.BonecoLargado != null, a.Zone.ToString());
		UsarHabilidade(a, "sairdamente");
		a.Ficha.med = false;
	}

	// =====================================================================
	// 8d) DESLOGAR DE FORA DO CORPO -- A ULTIMA, PORQUE ELA DERRUBA UM DOS DOIS
	// =====================================================================
	/// <summary>
	/// *"quem desloga fora do corpo volta pro corpo primeiro"* -- o `Logout()` do DM
	/// (`MindMeditate.dm:465-468`), que restaura ANTES do save.
	///
	/// ============================ POR QUE ELA E A ULTIMA ============================
	/// Porque ela chama o `Drop` de verdade, e depois dele B nao existe mais no servidor: nenhuma
	/// familia posterior teria dois corpos. E o `Drop` e o caminho de producao inteiro -- o mesmo que
	/// o desconectar do cliente dispara --, e nao ha um segundo caminho de "save de quem saiu" pra
	/// medir.
	///
	/// O QUE SE MEDE E O ARQUIVO: sem a linha do `Drop`, o save gravaria
	/// `Interior(Interdimension, id)` -- um bolso de uma pessoa, sem clone e sem porta, porque o verb
	/// de saida morre com a sessao. O jogador relogaria dentro dele e nao sairia mais.
	/// </summary>
	private void ODeslogueDeQuemEstaFora(ServerPlayer b)
	{
		GD.Print("[menteviva] -- 8d) deslogar de fora do corpo (e a ultima: ela derruba B)");

		if (b.Peer is not { } peer || _store == null)
		{ AfirmarMvv("B tem peer e ha `AccountStore` pra ler o save", false); return; }

		b.Ficha.med = true;
		UsarHabilidade(b, "mente");
		if (!NaMente(b)) { AfirmarMvv("B entrou na mente pra o teste de logout", false); return; }

		ZoneKey bolso = b.Zone;
		ServerPlayer? corpoDeB = b.BonecoLargado;
		string conta = b.Conta;
		int slot = b.Slot;
		ulong zonaDeFora = corpoDeB?.Zone.Hash ?? 0;

		Drop(peer);

		AfirmarMvv("o `Drop` tirou B do mundo", !_players.ContainsKey(b.Id));
		AfirmarMvv("...e nao deixou um corpo de pe pra sempre com a ficha de quem saiu",
				   corpoDeB == null || !ZoneList(corpoDeB.Zone.Hash).Contains(corpoDeB));
		AfirmarMvv("...nem ninguem parado dentro do bolso da mente",
				   ZoneList(bolso.Hash).Count == 0, $"{ZoneList(bolso.Hash).Count} corpo(s)");

		AccountSave? acc = _store.Carregar(conta);
		CharacterSave? save = acc != null && slot >= 0 && slot < acc.Slots.Length ? acc.Slots[slot] : null;
		if (save == null) { AfirmarMvv("o save de B foi lido do disco", false, $"conta {conta} slot {slot}"); return; }

		AfirmarMvv("o DISCO gravou a zona do CORPO, e nao a da mente",
				   save.ZonaTipo != ZoneKey.KindInterior
				   || !string.Equals(save.Zona, DimensaoMental.Zona, StringComparison.Ordinal),
				   $"{save.ZonaTipo}/{save.Zona}");
		AfirmarMvv("...e ela e exatamente a zona onde o corpo estava",
				   new ZoneKey(save.ZonaTipo, save.Zona, save.ZonaSeed).Hash == zonaDeFora,
				   $"{save.Zona} (esperado hash {zonaDeFora})");
	}

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// O ESTADO COMO ELE SAI NO FIO -- escrito e lido de volta pelo MESMO codigo do snapshot.
	///
	/// Ler `EstadoDe(...)` direto provaria a derivacao e deixaria passar a serializacao, e a pose e
	/// justamente o campo mais fragil: 3 bits espremidos entre a direcao e o "andando", no mesmo
	/// byte. Um deslocamento errado ali poe o corpo NOCAUTEADO na tela do outro sem mudar campo
	/// nenhum do servidor.
	/// </summary>
	private EntityState NoFio(ServerPlayer corpo)
	{
		var w = new NetDataWriter();
		EstadoDe(corpo, NowMs()).Write(w);
		var r = new NetDataReader(w.CopyData());
		return EntityState.Read(r);
	}

	/// <summary>
	/// O CARRASCO: um corpo com assinatura e poder, e nada mais.
	///
	/// Ele NAO e um dos "dois corpos de verdade" e nao precisa ser -- ver o comentario na familia 4.
	/// O que ele da e o numero que o Zenkai absorve e a assinatura sem a qual o `LutoNaVizinhanca`
	/// sai na primeira linha.
	/// </summary>
	private ServerPlayer ForjarCarrasco(ZoneKey zona)
	{
		var c = new ServerPlayer
		{
			Id = IdBaseDaMenteViva + 1,
			Peer = null,
			Name = "bancada: o carrasco",
			Race = "Saiyan", Genero = "Male", Idade = 25,
			Zone = zona, Pos = new Vec2(4200, 4000),
			Conta = "bancada_menteviva_carrasco", Slot = 0,
			Ficha = new Fighter { Race = "Saiyan", BP = 50_000_000 },
		};
		c.Ficha.Class = "Normal";
		PorNoMundo(c);
		c.Ficha.Ki = c.Ficha.MaxKi;
		return c;
	}

	// O `Recolher` (tirar corpo forjado do mundo) ja existe -- `GameServer.SolTeste.cs`. Uma segunda
	// copia aqui seria a duplicata que este port recusa por regra; esta bancada usa aquela.

	/// <summary>Devolve todo membro inteiro e ressincroniza o HP (que e a media deles).</summary>
	private static void CurarPorInteiro(ServerPlayer pl)
	{
		foreach (BodyPart p in pl.Combate.Corpo.Partes) { p.Decepado = false; p.Vida = p.VidaMax; }
		pl.Ficha.KO = false;
		pl.Ficha.dead = false;
		pl.Combate.NocauteRestante = 0;
		pl.Combate.SincronizarVida();
	}

	/// <summary>Poe os dois lado a lado, virados um pro outro.</summary>
	private static void Encostar(ServerPlayer a, ServerPlayer b, float px)
	{
		b.Pos = a.Pos + new Vec2(px, 0);
		a.Facing = MoveRules.FacingFrom(b.Pos - a.Pos, a.Facing);
		b.Facing = MoveRules.FacingFrom(a.Pos - b.Pos, b.Facing);
	}

	/// <summary>
	/// AS CONTAS DA RODADA SAEM NO FIM. Mesmo argumento (e mesmo codigo) da `--mestrevivo`: conta
	/// reaproveitada traria o personagem de ontem, com o BP que a bancada empurrou.
	/// </summary>
	private void ApagarContasDaMenteViva()
	{
		if (_store == null) return;
		foreach (string conta in ContasDaMenteViva)
			try
			{
				string caminho = System.IO.Path.Combine(_store.Pasta, conta + ".json");
				if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
			}
			catch (Exception e) { GD.PushWarning($"[menteviva] nao apaguei '{conta}': {e.Message}"); }
	}
}
