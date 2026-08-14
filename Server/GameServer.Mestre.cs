using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Skills;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PEDIDO PENDENTE DE UM MESTRE -- o que o `alert()` bloqueante do BYOND era.
///
/// No DM o `mst_ask_student` (`MasterStudent.dm:452`) e o `mst_ask_awaken` (`:515`) rodam com
/// `waitfor = 0` e CONGELAM o aluno numa caixa de dialogo. Aqui nao ha caixa modal: o molde e o
/// `PedidoDeAmizade` (`GameServer.Convivio.cs:426`) -- um pendente vivo, respondido por verb, com
/// a resposta achando quem pediu **pela assinatura** e nao por quem esta na frente.
/// </summary>
/// <param name="Despertar">FALSE = convite pra virar aluno; TRUE = oferta de despertar assistido.</param>
/// <param name="SigMestre">Quem ofereceu. E por ela que a resposta acha o mestre.</param>
/// <param name="NomeDoMestre">So pra mensagem -- o mestre pode ter saido.</param>
/// <param name="FormaId">A forma oferecida (vazio no convite).</param>
/// <param name="Ate">Prazo (Unix ms). Ver <see cref="Discipulado.PrazoDoConviteSegundos"/>.</param>
public sealed record PedidoDeMestre(bool Despertar, string SigMestre, string NomeDoMestre,
									string FormaId, long Ate);

/// <summary>
/// MESTRE E ALUNO, LADO DO SERVIDOR -- porte de `Code/Modules/Players/MasterStudent.dm`.
///
/// As regras puras (os numeros, o portao de 3x, que formas se ensinam) moram no
/// <see cref="Discipulado"/>. Aqui mora o que e ESTADO e DECISAO: quem e aluno de quem, o que
/// acontece quando um dos dois desloga, e a cadeia do despertar assistido.
///
/// ============================ O VINCULO E DO MUNDO, E POR ASSINATURA ============================
/// No DM o `mst_list` (`:32`) e uma lista global fora do mob, gravada no savefile `MASTER`, com a
/// **assinatura do ALUNO como chave** -- e e essa escolha, e nao um contador, que garante o "um
/// aluno, um mestre". Aqui e o mesmo desenho e o mesmo ciclo de vida do `cargos.txt`
/// (`GameServer.Ranks.cs`): dicionario em memoria, arquivo de texto ao lado dos saves, lido UMA VEZ
/// no boot e **gravado na hora** em cada mudanca. O DM escreve o porque em `mst_bind` (`:102`):
/// *"um reboot engole o vinculo"*.
///
/// A CHAVE E A ASSINATURA E NAO A CONTA -- e aqui divirjo do `cargos.txt` de proposito. Cargo tem
/// um dono no mundo e o dono e a pessoa; mestre e do PERSONAGEM: a mesma conta pode ter um
/// Namekiano aluno de alguem e um Saiyajin que nao e aluno de ninguem. A assinatura
/// (`ServerPlayer.Assinatura`, hash de conta+slot) e exatamente o identificador certo -- e por ser
/// derivada do slot ela e RECICLADA por um personagem novo, que e o motivo de existir o
/// <see cref="PurgarAssinatura"/> (o `mst_purge_sig` do DM, `:123`).
/// ==============================================================================================
///
/// ============================ E O QUE ELE FAZ SAO TRES COISAS ============================
///   1. **Ganho** -- treinar contra o proprio mestre paga ate 3x (piso 1,2x). Ver
///      <see cref="EhMeuMestre"/>: e o segundo parametro do `Fighter.FightGainMult`, que estava
///      escrito e **orfao** desde que foi portado -- sete chamadores, todos passando um argumento
///      so. Ligar o discipulado e passar o bit nesses sete pontos.
///   2. **Despertar assistido** -- ver <see cref="OferecerDespertar"/>: o requisito PESSOAL de BP
///      vale metade (`MST_HALF`), e o corte entra pelo funil do `EstadoDeForma.Avaliar`.
///   3. **Testemunha** -- ver <see cref="NotarFormaVista"/>: quem viu alguem numa forma pode ser
///      ensinado nela.
/// =====================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// O ESTADO
	// =====================================================================
	/// <summary>assinatura do ALUNO -> assinatura do MESTRE. Ver o cabecalho.</summary>
	private readonly Dictionary<string, string> _mestreDe = new(StringComparer.Ordinal);

	/// <summary>
	/// assinatura -> nome, so pra exibicao. E o `mst_names` do DM (`:33`), e ele existe pelo mesmo
	/// motivo la e aqui: o painel do aluno precisa dizer o nome do mestre mesmo com o mestre
	/// deslogado -- e um vinculo que atravessa o logout nao pode virar "(desconhecido)" toda vez
	/// que a outra pessoa vai dormir.
	/// </summary>
	private readonly Dictionary<string, string> _nomePorAssinatura = new(StringComparer.Ordinal);

	/// <summary>Pelo `_store` e nao pelo caminho cravado -- ver o porque em `GameServer.Ranks.cs`.</summary>
	private string ArquivoDeMestres =>
		System.IO.Path.Combine(_store?.Pasta ?? ProjectSettings.GlobalizePath("user://saves"), "mestres.txt");

	/// <summary>
	/// LE O VINCULO DO DISCO. Chamado UMA VEZ no boot, ao lado do <see cref="CarregarCargos"/>.
	///
	/// Formato: `alunoSig \t mestreSig \t nomeDoAluno \t nomeDoMestre`. Um arquivo de texto de
	/// algumas dezenas de linhas -- mesmo argumento do `cargos.txt`: da pra ler num editor no dia
	/// em que alguem disser "sumiu meu mestre", e isso vale mais que economia de bytes.
	/// </summary>
	private void CarregarMestres()
	{
		_mestreDe.Clear();
		_nomePorAssinatura.Clear();
		if (!System.IO.File.Exists(ArquivoDeMestres)) return;

		foreach (string linha in System.IO.File.ReadAllLines(ArquivoDeMestres))
		{
			string[] p = linha.Split('\t');
			if (p.Length < 2 || p[0].Length == 0 || p[1].Length == 0) continue;
			_mestreDe[p[0]] = p[1];
			if (p.Length > 2 && p[2].Length > 0) _nomePorAssinatura[p[0]] = p[2];
			if (p.Length > 3 && p[3].Length > 0) _nomePorAssinatura[p[1]] = p[3];
		}
		GD.Print($"[server] discipulado: {_mestreDe.Count} vinculo(s) mestre-aluno");
	}

	private void SalvarMestres()
	{
		try
		{
			System.IO.File.WriteAllLines(ArquivoDeMestres, _mestreDe.Select(
				kv => $"{kv.Key}\t{kv.Value}\t{NomeDaAssinatura(kv.Key)}\t{NomeDaAssinatura(kv.Value)}"));
		}
		catch (Exception e) { GD.PushWarning($"[server] nao gravei o discipulado: {e.Message}"); }
	}

	// =====================================================================
	// AS CONSULTAS
	// =====================================================================
	/// <summary>A assinatura do mestre deste aluno ("" = nao tem). E o `mst_master_sig` (`:59`).</summary>
	private string MestreDe(string sigAluno) =>
		sigAluno.Length > 0 && _mestreDe.TryGetValue(sigAluno, out string? m) ? m : "";

	/// <summary>Quantos alunos este mestre carrega. `mst_count` (`:63`).</summary>
	private int ContarAlunos(string sigMestre) =>
		sigMestre.Length == 0 ? 0 : _mestreDe.Count(kv => kv.Value == sigMestre);

	private IEnumerable<string> AlunosDe(string sigMestre) =>
		sigMestre.Length == 0 ? [] : _mestreDe.Where(kv => kv.Value == sigMestre).Select(kv => kv.Key);

	private string NomeDaAssinatura(string sig) =>
		_nomePorAssinatura.TryGetValue(sig, out string? n) ? n : "alguem";

	/// <summary>
	/// ESTE OUTRO E O MEU MESTRE? `mst_is_my_master` (`:82`) -- e a pergunta que o
	/// `fight_gain_mult` faz A CADA GOLPE, entao ela e um `TryGetValue` e mais nada.
	///
	/// A ORDEM DOS ARGUMENTOS E A PERGUNTA: quem ganha e o `aluno`. O bonus nao e simetrico -- o
	/// mestre nao ganha nada por bater no aluno, e no DM tambem nao (`combatgains.dm:88-100`
	/// pergunta pelo mestre de QUEM esta ganhando).
	/// </summary>
	private bool EhMeuMestre(ServerPlayer aluno, ServerPlayer? outro)
	{
		if (outro == null || !EhPessoa(aluno) || !EhPessoa(outro)) return false;
		return MestreDe(aluno.Assinatura) == outro.Assinatura;
	}

	/// <summary>O corpo online daquela assinatura, se houver. `mst_mob_of` (`:75`).</summary>
	private ServerPlayer? CorpoDaAssinatura(string sig) =>
		sig.Length == 0 ? null : _players.Values.FirstOrDefault(p => EhPessoa(p) && p.Assinatura == sig);

	// =====================================================================
	// VINCULAR E DESVINCULAR
	// =====================================================================
	/// <summary>
	/// `mst_bind` (`:95`). GRAVA NA HORA -- ver o cabecalho da classe.
	/// </summary>
	private void Vincular(ServerPlayer mestre, ServerPlayer aluno)
	{
		_mestreDe[aluno.Assinatura] = mestre.Assinatura;
		_nomePorAssinatura[aluno.Assinatura] = aluno.Name;
		_nomePorAssinatura[mestre.Assinatura] = mestre.Name;
		SalvarMestres();

		GD.Print($"[server] discipulado: {aluno.Name} agora e aluno de {mestre.Name} "
				 + $"({ContarAlunos(mestre.Assinatura)}/{Discipulado.MaxAlunos})");
		Avisar(mestre, $"{aluno.Name} aceita ser seu aluno "
					 + $"({ContarAlunos(mestre.Assinatura)}/{Discipulado.MaxAlunos}).");
		Avisar(aluno, $"{mestre.Name} agora e seu mestre. Treinar contra ele rende ate 3x mais, "
					+ "e ele pode te guiar numa transformacao.");
	}

	/// <summary>
	/// `mst_unbind` (`:107`). Avisa **quem estiver online** -- o outro descobre pelo painel, que e
	/// o que o DM faz (`if(A && A.client)`).
	/// </summary>
	private void Desvincular(string sigAluno, string motivo)
	{
		if (!_mestreDe.TryGetValue(sigAluno, out string? sigMestre)) return;
		_mestreDe.Remove(sigAluno);
		SalvarMestres();

		if (CorpoDaAssinatura(sigAluno) is { } aluno)
			Avisar(aluno, $"o vinculo com {NomeDaAssinatura(sigMestre)} se desfaz -- {motivo}.");
		if (CorpoDaAssinatura(sigMestre) is { } mestre)
			Avisar(mestre, $"{NomeDaAssinatura(sigAluno)} nao e mais seu aluno -- {motivo}.");
		GD.Print($"[server] discipulado desfeito: {NomeDaAssinatura(sigAluno)} x "
				 + $"{NomeDaAssinatura(sigMestre)} ({motivo})");
	}

	/// <summary>
	/// `mst_purge_sig` (`:123`): a assinatura foi RECICLADA por um personagem novo, entao ela nao
	/// herda mestre nem alunos.
	///
	/// Isto NAO e paranoia de porte -- e consequencia direta do desenho: a assinatura aqui e
	/// `hash(conta, slot)`, entao apagar o personagem do slot 2 e criar outro no mesmo slot devolve
	/// **a mesma assinatura**. Sem esta limpeza, o personagem novo nasceria com os alunos do morto.
	/// </summary>
	private void PurgarAssinatura(string sig)
	{
		if (sig.Length == 0) return;
		bool mudou = _mestreDe.Remove(sig);
		foreach (string aluno in AlunosDe(sig).ToList()) { _mestreDe.Remove(aluno); mudou = true; }
		_nomePorAssinatura.Remove(sig);
		if (mudou) { SalvarMestres(); GD.Print($"[server] discipulado: assinatura {sig} reciclada, vinculos limpos"); }
	}

	// =====================================================================
	// A TESTEMUNHA
	// =====================================================================
	/// <summary>
	/// "EU VI ALGUEM NESSA FORMA" -- o `mst_note_form` (`MasterStudent.dm:207`).
	///
	/// ============================ NO DM SAO SETE CHAMADORES; AQUI E UM ============================
	/// La a linha esta repetida em cada buff racial (`supersaiyanbuff.dm:190`, `lssjbuff.dm:71`,
	/// `IcerTransform.dm:109`, `HeranBuff.dm:45`, `CellFormBuff.dm:21`,
	/// `Alien_Transformations.dm:52`, `Super_Namek.dm:39`) -- e uma forma nova que esquecesse a
	/// linha simplesmente nunca seria vista por ninguem, calada. Aqui o
	/// <see cref="AnunciarForma"/> e o funil unico (subir, descer, cair por Ki zerado, o DirectSSJ
	/// do admin), e este metodo e chamado de dentro do laco dele.
	///
	/// **O RAIO E CONFERIDO AQUI, e e a metade que importa**: o laco do anuncio varre a ZONA
	/// INTEIRA, entao sem os 8 tiles do `MST_WITNESS_RANGE` "eu vi" viraria "eu estava no mesmo
	/// planeta" e o pre-requisito nao gatearia nada -- o "teto que nunca dispara" da PARTE 3.
	/// ==========================================================================================
	/// </summary>
	private static void NotarFormaVista(ServerPlayer quemVe, ServerPlayer quemSeTransforma, string idDaForma)
	{
		if (quemVe == quemSeTransforma || !EhPessoa(quemVe)) return;
		if (Catalogo.Def(idDaForma) is not { } d || d.Id == Catalogo.IdBase) return;
		if (Vec2.Distance(quemVe.Pos, quemSeTransforma.Pos)
			> Discipulado.TilesDeTestemunha * ZoneCollision.TileSize) return;

		quemVe.FormasVistas.Add(d.IdRede);
	}

	/// <summary>
	/// O PRE-REQUISITO DO ENSINO: o aluno **viu** a forma, ou o mestre **a conhece**.
	/// `mst_teachable` (`:390`) -- `mst_saw_form(tag) || mestre.mst_form_known(tag)`.
	/// </summary>
	private static bool PodeSerEnsinada(ServerPlayer mestre, ServerPlayer aluno, FormaDef d) =>
		aluno.FormasVistas.Contains(d.IdRede) || mestre.Forma.Despertou(d.Id);

	// =====================================================================
	// OS VERBOS
	// =====================================================================
	/// <summary>
	/// Os verbs do discipulado. Entram pelo mesmo cano de <see cref="UsarHabilidade"/> -- o
	/// `C2S.Habilidade`, um id de texto e mais nada.
	///
	/// `mst_ensinar` aceita SUFIXO (`mst_ensinar:ssj2`) pra escolher a forma. Sem sufixo o servidor
	/// escolhe o degrau que falta -- ver <see cref="OferecerDespertar"/> pro porque de nao haver
	/// menu como no DM.
	/// </summary>
	private bool UsarVerboDeMestre(ServerPlayer pl, string id)
	{
		if (id.StartsWith("mst_ensinar", StringComparison.Ordinal))
		{
			int dp = id.IndexOf(':');
			OferecerDespertar(pl, dp >= 0 ? id[(dp + 1)..] : "");
			return true;
		}

		switch (id)
		{
			case "mst_convidar": ConvidarAluno(pl); return true;
			case "mst_aceitar": ResponderAoMestre(pl, aceitou: true); return true;
			case "mst_recusar": ResponderAoMestre(pl, aceitou: false); return true;
			case "mst_largar": LargarOMestre(pl); return true;
			case "mst_dispensar": DispensarAluno(pl); return true;
			default: return false;
		}
	}

	/// <summary>
	/// `Convidar_Aluno` (`MasterStudent.dm:423`): convida quem esta **na frente**.
	///
	/// O DM usa `get_step(src, dir)` -- o tile exatamente a frente -- pra ESCOLHER, e so depois, na
	/// revalidacao pos-caixa, afrouxa pra `get_dist <= MST_RANGE`. Aqui quem escolhe e o
	/// <see cref="AlvoNaFrente"/>, o mesmo cone que o soco e o ensino de disciplina usam: e o
	/// precedente vivo deste port pra "quem esta na minha frente", e um segundo jeito de responder
	/// a mesma pergunta seria a duplicata que a PARTE 3 chama de "duas casas pra uma formula".
	/// </summary>
	private void ConvidarAluno(ServerPlayer mestre)
	{
		ServerPlayer? aluno = AlvoNaFrente(mestre);
		if (aluno == null) { Avisar(mestre, "nao ha ninguem na sua frente."); return; }

		Discipulado.RecusaDeVinculo r = Discipulado.AvaliarVinculo(
			mestreTemAssinatura: EhPessoa(mestre), alunoTemAssinatura: EhPessoa(aluno),
			mesmaPessoa: mestre == aluno,
			alunoJaTemMestre: MestreDe(aluno.Assinatura).Length > 0,
			alunosDoMestre: ContarAlunos(mestre.Assinatura),
			bpMestre: mestre.Ficha.BP, bpAluno: aluno.Ficha.BP,
			algumCaido: mestre.Ficha.KO || mestre.Ficha.dead || aluno.Ficha.KO || aluno.Ficha.dead,
			distanciaTiles: Vec2.Distance(mestre.Pos, aluno.Pos) / ZoneCollision.TileSize);

		if (r != Discipulado.RecusaDeVinculo.Pode) { Avisar(mestre, PorQueNaoVincula(r, aluno)); return; }

		aluno.PedidoDoMestre = new PedidoDeMestre(false, mestre.Assinatura, mestre.Name, "",
			NowMs() + (long)(Discipulado.PrazoDoConviteSegundos * 1000));
		Avisar(mestre, $"voce oferece a {aluno.Name} um lugar como seu aluno.");
		Avisar(aluno, $"{mestre.Name} se oferece pra ser seu mestre. Aceite ou recuse na aba Learning "
					+ $"(voce tem {Discipulado.PrazoDoConviteSegundos:0} s).");
	}

	/// <summary>
	/// A MENSAGEM DIZ O QUE FALTA -- mesmo criterio do `PorQueNaoAprende` das disciplinas: "nao
	/// pode" manda a pessoa procurar um requisito que ela nunca vai achar.
	/// </summary>
	private static string PorQueNaoVincula(Discipulado.RecusaDeVinculo r, ServerPlayer aluno) => r switch
	{
		Discipulado.RecusaDeVinculo.SemAssinatura => "essa criatura nao tem identidade propria.",
		Discipulado.RecusaDeVinculo.EleMesmo => "ninguem e aluno de si mesmo.",
		Discipulado.RecusaDeVinculo.JaTemMestre => $"{aluno.Name} ja tem um mestre.",
		Discipulado.RecusaDeVinculo.Lotado =>
			$"voce ja guia {Discipulado.MaxAlunos} alunos -- dispense um antes de tomar outro.",
		Discipulado.RecusaDeVinculo.MestreFraco =>
			$"voce precisa ser pelo menos {Discipulado.RazaoDeBp:0}x mais forte que {aluno.Name} "
			+ "pra ter o que ensinar.",
		Discipulado.RecusaDeVinculo.Caido => "nao da, com alguem caido.",
		Discipulado.RecusaDeVinculo.Longe => $"{aluno.Name} esta longe demais.",
		_ => "agora nao.",
	};

	/// <summary>
	/// A RESPOSTA DO ALUNO -- vale pro convite E pra oferta de despertar (ver
	/// <see cref="PedidoDeMestre"/>).
	///
	/// TUDO E REVALIDADO AQUI, e nao e zelo: e o que o `mst_ask_student` (`:452`) e o
	/// `mst_ask_awaken` (`:515`) fazem depois da caixa. Entre a oferta e o "sim" o mestre pode ter
	/// se afastado, perdido poder, lotado a lista ou saido do mundo.
	/// </summary>
	private void ResponderAoMestre(ServerPlayer aluno, bool aceitou)
	{
		if (aluno.PedidoDoMestre is not { } p) { Avisar(aluno, "ninguem te ofereceu nada."); return; }
		if (NowMs() > p.Ate)
		{
			aluno.PedidoDoMestre = null;
			Avisar(aluno, $"a oferta de {p.NomeDoMestre} ja tinha expirado.");
			return;
		}

		aluno.PedidoDoMestre = null;
		ServerPlayer? mestre = CorpoDaAssinatura(p.SigMestre);
		if (mestre == null) { Avisar(aluno, $"{p.NomeDoMestre} nao esta mais por aqui."); return; }

		if (!aceitou)
		{
			Avisar(aluno, $"voce recusa a oferta de {p.NomeDoMestre}.");
			Avisar(mestre, $"{aluno.Name} recusou.");
			return;
		}

		if (p.Despertar) { DespertarAssistido(mestre, aluno, p.FormaId); return; }

		Discipulado.RecusaDeVinculo r = Discipulado.AvaliarVinculo(
			mestreTemAssinatura: EhPessoa(mestre), alunoTemAssinatura: EhPessoa(aluno),
			mesmaPessoa: mestre == aluno,
			alunoJaTemMestre: MestreDe(aluno.Assinatura).Length > 0,
			alunosDoMestre: ContarAlunos(mestre.Assinatura),
			bpMestre: mestre.Ficha.BP, bpAluno: aluno.Ficha.BP,
			algumCaido: mestre.Ficha.KO || mestre.Ficha.dead || aluno.Ficha.KO || aluno.Ficha.dead,
			// A DISTANCIA VOLTA A SER CONFERIDA, agora pelo `MST_RANGE` (4 tiles) e nao pelo tile da
			// frente: entre a oferta e o "sim" os dois se mexeram, e exigir que continuem colados
			// faria o aceite falhar por um passo. E o afrouxamento que o proprio DM faz (`:452`).
			distanciaTiles: mestre.Zone.Hash != aluno.Zone.Hash
				? double.MaxValue : Vec2.Distance(mestre.Pos, aluno.Pos) / ZoneCollision.TileSize);

		if (r != Discipulado.RecusaDeVinculo.Pode)
		{
			Avisar(aluno, PorQueNaoVincula(r, aluno));
			Avisar(mestre, $"o vinculo com {aluno.Name} nao se fechou: {PorQueNaoVincula(r, aluno)}");
			return;
		}

		Vincular(mestre, aluno);
	}

	/// <summary>O aluno larga o mestre (`mstleave` do painel do DM).</summary>
	private void LargarOMestre(ServerPlayer aluno)
	{
		if (MestreDe(aluno.Assinatura).Length == 0) { Avisar(aluno, "voce nao tem mestre."); return; }
		Desvincular(aluno.Assinatura, "o aluno seguiu o proprio caminho");
	}

	/// <summary>
	/// O mestre dispensa o aluno que estiver na frente (`mstdrop`).
	///
	/// PELO ALVO NA FRENTE e nao por uma lista: o canal de habilidade carrega um id de texto, e uma
	/// lista de alunos na tela e trabalho de UI que este verb nao precisa pra existir. Quem quiser
	/// dispensar alguem que nao esta por perto usa o painel, no dia em que ele existir.
	/// </summary>
	private void DispensarAluno(ServerPlayer mestre)
	{
		ServerPlayer? aluno = AlvoNaFrente(mestre);
		if (aluno == null) { Avisar(mestre, "nao ha ninguem na sua frente."); return; }
		if (MestreDe(aluno.Assinatura) != mestre.Assinatura)
		{
			Avisar(mestre, $"{aluno.Name} nao e seu aluno."); return;
		}
		Desvincular(aluno.Assinatura, "o mestre dispensou o aluno");
	}

	// =====================================================================
	// O DESPERTAR ASSISTIDO
	// =====================================================================
	/// <summary>
	/// `Ajudar_A_Transformar` (`MasterStudent.dm:477`): o mestre provoca ou ensina, e o requisito
	/// PESSOAL de BP do aluno vale METADE nessa tentativa.
	///
	/// ============================ POR QUE NAO HA MENU COMO NO DM ============================
	/// La o verb monta a lista de `mst_teachable` e pede ao mestre que escolha. Aqui o canal de
	/// habilidade carrega um id de texto e nada mais, e um menu exigiria pacote proprio -- entao o
	/// servidor escolhe **o degrau que falta**: o mais barato (menor `Ordem`) entre os ensinaveis
	/// que passam em tudo menos na raiva. Na pratica e a MESMA escolha, porque a escada tem um
	/// proximo degrau so; e o sufixo (`mst_ensinar:ssj2`) fica aberto pra quando houver painel --
	/// e pra a bancada poder pedir uma forma pelo nome.
	/// ====================================================================================
	///
	/// **A RECARGA E CONSUMIDA ANTES DE PERGUNTAR**, e isso e literal do DM (`:510`): a tentativa e
	/// o gesto, nao o resultado. Se o "nao" do aluno devolvesse os 5 minutos, provocar viraria
	/// gratis e o mestre poderia martelar o aluno ate a raiva pegar.
	/// </summary>
	private void OferecerDespertar(ServerPlayer mestre, string idPedido)
	{
		if (!EhPessoa(mestre)) { Avisar(mestre, "este corpo nao tem identidade propria."); return; }

		long agora = NowMs();
		if (mestre.RecargaDeEnsino > agora)
		{
			Avisar(mestre, $"voce ainda esta sem folego pra ensinar "
						 + $"({(mestre.RecargaDeEnsino - agora) / 1000.0:0} s).");
			return;
		}

		ServerPlayer? aluno = AlvoNaFrente(mestre);
		if (aluno == null) { Avisar(mestre, "nao ha ninguem na sua frente."); return; }
		if (MestreDe(aluno.Assinatura) != mestre.Assinatura)
		{
			Avisar(mestre, $"{aluno.Name} nao e seu aluno."); return;
		}
		if (aluno.Ficha.KO || aluno.Ficha.dead) { Avisar(mestre, $"{aluno.Name} esta caido."); return; }

		FormaDef? alvo = idPedido.Length > 0 ? Catalogo.Def(idPedido) : null;
		if (idPedido.Length > 0 && (alvo == null || !Discipulado.EhEnsinavel(alvo.Id)))
		{
			Avisar(mestre, "essa forma nao se ensina."); return;
		}
		alvo ??= EscolherFormaDoEnsino(mestre, aluno);

		if (alvo == null)
		{
			Avisar(mestre, $"nao ha nada que voce possa despertar em {aluno.Name} agora -- "
						 + "ou ele nunca viu a forma, ou o caminho dele ainda nao chegou la.");
			return;
		}
		if (!PodeSerEnsinada(mestre, aluno, alvo))
		{
			Avisar(mestre, $"{aluno.Name} nunca viu essa forma, e voce nao a possui pra mostrar.");
			return;
		}

		// A RECARGA CORRE A PARTIR DAQUI. Ver o cabecalho.
		mestre.RecargaDeEnsino = agora + (long)(Discipulado.RecargaDeEnsinoSegundos * 1000);

		bool porRaiva = Catalogo.RaivaExigida(alvo) != NivelDeRaiva.Nenhuma;
		aluno.PedidoDoMestre = new PedidoDeMestre(true, mestre.Assinatura, mestre.Name, alvo.Id,
			agora + (long)(Discipulado.PrazoDoConviteSegundos * 1000));

		// PROVOCACAO OU ENSINAMENTO, conforme a forma nasca da raiva ou nao (`:510` do DM manda a
		// frase como FALA, e aqui ela e o aviso dos dois lados).
		Avisar(mestre, porRaiva
			? $"voce provoca {aluno.Name}, procurando a ferida que acorda {Catalogo.NomeDe(alvo, false)}."
			: $"voce guia {aluno.Name} pelo caminho de {Catalogo.NomeDe(alvo, false)}.");
		Avisar(aluno, porRaiva
			? $"{mestre.Name} te provoca sem piedade. Aceite (aba Learning) e deixe a raiva subir."
			: $"{mestre.Name} se oferece pra te guiar ate {Catalogo.NomeDe(alvo, false)}. "
			+ "Aceite na aba Learning.");
	}

	/// <summary>
	/// O DEGRAU QUE FALTA: o mais barato entre os ensinaveis que este aluno alcanca **com o corte
	/// de metade**, contando a raiva como pendencia (ela e o passo seguinte, e quem a acende e o
	/// mestre).
	/// </summary>
	private static FormaDef? EscolherFormaDoEnsino(ServerPlayer mestre, ServerPlayer aluno)
	{
		PerfilDeFormas perfil = Perfil(aluno);
		foreach (FormaDef d in Discipulado.Ensinaveis)
		{
			if (aluno.Forma.Despertou(d.Id)) continue;
			if (!PodeSerEnsinada(mestre, aluno, d)) continue;
			RecusaForma r = aluno.Forma.Avaliar(d.Id, aluno.Ficha.BP, kiFracao: 1, caido: false,
												perfil, Discipulado.FatorAssistido);
			if (r is RecusaForma.Pode or RecusaForma.SemFuria) return d;
		}
		return null;
	}

	/// <summary>
	/// A CADEIA DO DESPERTAR, depois do "sim" do aluno -- o `mst_ask_awaken` (`:515`).
	///
	/// ============================ A ORDEM E A REGRA, E ELA E DO DM ============================
	/// O DM confere TUDO (vinculo, distancia, forma aberta, degrau anterior, poder pela metade, os
	/// tetos divinos) e **so entao** acende a raiva -- o comentario de la explica: acender antes
	/// faria toda tentativa fadada virar um buff de raiva de graca, uma vez por recarga.
	///
	/// Aqui essa ordem sai **de graca**, e nao por eu a ter reescrito: o `Avaliar` ja poe a raiva
	/// por ULTIMO (passo 9 -- ver o comentario dele), entao `SemFuria` como resposta significa
	/// literalmente *"tudo o mais ja esta pago"*. Qualquer outra recusa e uma falha que nao acende
	/// nada.
	/// ======================================================================================
	/// </summary>
	private void DespertarAssistido(ServerPlayer mestre, ServerPlayer aluno, string idDaForma)
	{
		if (Catalogo.Def(idDaForma) is not { } d || !Discipulado.EhEnsinavel(d.Id)) return;

		// 1. O VINCULO E A DISTANCIA, de novo. Ver `ResponderAoMestre`.
		if (MestreDe(aluno.Assinatura) != mestre.Assinatura)
		{
			Avisar(aluno, $"{mestre.Name} nao e mais seu mestre."); return;
		}
		if (mestre.Zone.Hash != aluno.Zone.Hash
			|| Vec2.Distance(mestre.Pos, aluno.Pos) > TilesDoDespertar * ZoneCollision.TileSize)
		{
			Avisar(aluno, $"{mestre.Name} esta longe demais pra te guiar.");
			Avisar(mestre, $"{aluno.Name} se afastou.");
			return;
		}
		if (!PodeSerEnsinada(mestre, aluno, d)) { Avisar(aluno, "voce nao faz ideia do que ele quer de voce."); return; }

		PerfilDeFormas perfil = Perfil(aluno);
		double kiFrac = aluno.Ficha.MaxKi > 0 ? aluno.Ficha.Ki / aluno.Ficha.MaxKi : 1;
		bool caido = aluno.Ficha.KO || aluno.Ficha.dead;

		RecusaForma r = aluno.Forma.Avaliar(d.Id, aluno.Ficha.BP, kiFrac, caido, perfil,
											Discipulado.FatorAssistido);

		// 2. A RAIVA, E SO AQUI. Ver o cabecalho.
		if (r == RecusaForma.SemFuria)
		{
			NivelDeRaiva pede = Catalogo.RaivaExigida(d);
			AcenderJanelaDeRaiva(aluno, pede, NowMs() + (long)(SegundosDeRaiva * 1000));
			Avisar(aluno, $"as palavras de {mestre.Name} acertam onde doi. Alguma coisa em voce SE PARTE.");

			// ============================ O PERFIL E UMA FOTO, E ELA ENVELHECEU ============================
			// `PerfilDeFormas` e um record IMUTAVEL tirado do corpo -- e a raiva e um dos campos dele.
			// Reavaliar com o perfil de cima seria perguntar de novo com a foto de ANTES da
			// provocacao, e a resposta seria `SemFuria` pra sempre: o despertar assistido nunca
			// aconteceria, com a raiva acesa e o mestre pagando a recarga.
			//
			// **QUEM ACHOU FOI A BANCADA**, e o defeito e invisivel na leitura: as duas chamadas de
			// `Avaliar` sao identicas linha a linha, e o que mudou entre elas mora fora das duas.
			// =========================================================================================
			perfil = Perfil(aluno);
			r = aluno.Forma.Avaliar(d.Id, aluno.Ficha.BP, kiFrac, caido, perfil,
									Discipulado.FatorAssistido);
		}

		if (r != RecusaForma.Pode)
		{
			Avisar(aluno, $"voce tenta, e nao vem. ({r})");
			Avisar(mestre, $"{aluno.Name} tenta, e nao vem -- ainda nao e a hora dele.");
			GD.Print($"[server] despertar assistido falhou: {mestre.Name} -> {aluno.Name} ({d.Id}, {r})");
			return;
		}

		// 3. O CORTE FICA. Sem isto o aluno desperta agora e nao consegue REENTRAR na forma depois
		//    -- exatamente o bug que o `mst_form_apply` do DM (`:358-360`) descreve. Ver
		//    `EstadoDeForma.PortasCortadas`.
		aluno.Forma.CortarPorta(d.Id);

		// 4. E A ENTRADA E PELO CAMINHO NORMAL. `mst_form_apply` (`:361`) tambem chama a proc
		//    canonica (`SSj()`, `Restrained_SSj()`...) em vez de escrever as vars na mao, e pelo
		//    mesmo motivo: quem seta forma por fora perde tudo o que o funil faz -- Ki cheio,
		//    cinematica, clima, aviso na zona, cabelo nos outros clientes.
		EntrarNaForma(aluno, d);
		Avisar(mestre, $"{aluno.Name} DESPERTA {Catalogo.NomeDe(d, false)} na sua frente.");
		GD.Print($"[server] despertar assistido: {mestre.Name} guiou {aluno.Name} ate {d.Id}");
	}

	/// <summary>
	/// Quao perto os dois tem que estar no INSTANTE do despertar. O DM usa `get_dist <= 1` (tile
	/// vizinho); aqui as posicoes sao continuas em pixels e "um tile" e um numero apertado demais
	/// pra um gesto que envolve duas pessoas se mexendo -- dois tiles e o mesmo "colado", com folga
	/// de um passo.
	/// </summary>
	private const double TilesDoDespertar = 2;
}
