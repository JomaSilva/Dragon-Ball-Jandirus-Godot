using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A LICAO OFERECIDA E AINDA NAO RESPONDIDA -- o que o `input()` bloqueante do BYOND era.
///
/// SEPARADO DO <see cref="PedidoDeMestre"/>, e o comentario de la explica por que aquele e um so
/// pros dois tipos dele: convite e despertar sao *mutuamente exclusivos por construcao*. Uma licao
/// de skill **nao e** exclusiva com nenhum dos dois -- ensinar nao pede vinculo nenhum (ver
/// <see cref="EnsinoDeSkill"/>), entao um mestre pode estar convidando alguem enquanto um terceiro
/// oferece um Kamehameha ao mesmo corpo. Empilhar os tres num campo so criaria um estado que o
/// jogo produz de verdade e que alguem teria que decidir o que significa.
/// </summary>
public sealed record PedidoDeLicao(string SigMestre, string NomeDoMestre, string Path, long Ate);

/// <summary>
/// O ENSINO DE SKILL, LADO DO SERVIDOR -- porte de
/// `Code/Modules/Skills/Skills Master/teachable.dm`.
///
/// As regras puras (o custo, as tres condicoes do repasse, os portoes desligados) moram no
/// <see cref="EnsinoDeSkill"/>. Aqui mora o gesto: quem esta na frente de quem, a oferta pendente,
/// a recarga e o aviso.
///
/// ============================ A CORRENTE INTEIRA, EM QUATRO PASSOS ============================
///   1. o mestre aponta uma skill sua (`ens_ensinar:&lt;typepath&gt;`) pra quem esta na frente;
///   2. a recarga de 6 s comeca **no gesto** e o aluno recebe a oferta;
///   3. o aluno aceita (`ens_licao_sim`) -- e o servidor **revalida tudo**;
///   4. a copia entra marcada: `wastaught = TRUE`, `teacher = FALSE`. **O aluno nao repassa.**
///
/// E o aluno **nao paga nada** -- ele so precisa TER os marcos. Ver o cabecalho do
/// <see cref="EnsinoDeSkill"/>, que e onde essa descoberta esta escrita por extenso.
/// ==========================================================================================
///
/// ============================ UMA DIVERGENCIA DE OLHO ABERTO: QUEM RESPONDE ============================
/// No DM o `Study` chama `input("Do you want to learn ...")` **sem alvo**, e `input()` sem alvo usa
/// `usr` -- que naquele ponto da pilha e o MESTRE, porque a chamada nasceu do verb dele
/// (`teachable.dm:21`). Ou seja: no original quem responde "quer aprender?" e a propria pessoa que
/// esta ensinando, e o aluno nem ve a caixa.
///
/// E um bug de la, e a evidencia esta no proprio bloco: o texto e escrito na segunda pessoa do
/// ALUNO (*"you have [src.skillpoints]"*) e as duas mensagens seguintes vao pra `src` e pro
/// `Teacher` como pessoas diferentes. Aqui quem responde e o aluno.
/// ====================================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// OS VERBOS
	// =====================================================================
	/// <summary>
	/// Os verbs do ensino de skill, pelo mesmo cano do resto (`C2S.Habilidade`, um id de texto).
	///
	/// `ens_ensinar` e `ens_esquecer` aceitam SUFIXO com o typepath (`ens_ensinar:/datum/skill/...`)
	/// porque, ao contrario da escada de formas, **nao existe "a proxima skill"**: o DM abre um menu
	/// (`input("Teach which Skill?") in Teach`) e a escolha e do jogador. Quem monta esse menu aqui e
	/// o cliente, que ja tem o catalogo e a lista de aprendidas -- ver `Client/Habilidades.cs`.
	/// </summary>
	private bool UsarVerboDeEnsino(ServerPlayer pl, string id)
	{
		if (id.StartsWith("ens_ensinar", StringComparison.Ordinal))
		{
			int dp = id.IndexOf(':');
			OferecerLicao(pl, dp >= 0 ? id[(dp + 1)..] : "");
			return true;
		}
		if (id.StartsWith("ens_esquecer", StringComparison.Ordinal))
		{
			int dp = id.IndexOf(':');
			EsquecerLicao(pl, dp >= 0 ? id[(dp + 1)..] : "");
			return true;
		}

		switch (id)
		{
			case "ens_licao_sim": ResponderALicao(pl, aceitou: true); return true;
			case "ens_licao_nao": ResponderALicao(pl, aceitou: false); return true;
			default: return false;
		}
	}

	/// <summary>
	/// `Teach_Skill` (`teachable.dm:5`): oferece uma skill sua a quem esta na frente.
	///
	/// **A RECARGA COMECA AQUI, no gesto**, e nao no "sim" do aluno -- e o `sleep(60)` do DM (`:22`)
	/// tambem: o unico caminho que escapa dele e o `return` do "Cancel" (`:17`), ou seja o mestre
	/// que desiste antes de escolher alguem nao paga. Se o "nao" do aluno devolvesse os 6 s, oferecer
	/// viraria gratis.
	/// </summary>
	private void OferecerLicao(ServerPlayer mestre, string path)
	{
		if (_skills == null) { Avisar(mestre, "o servidor esta sem catalogo de skills."); return; }
		if (path.Length == 0) { Avisar(mestre, "escolha qual habilidade voce quer ensinar."); return; }

		ServerPlayer? aluno = AlvoNaFrente(mestre);
		if (aluno == null) { Avisar(mestre, "nao ha ninguem na sua frente."); return; }

		Skill? s = _skills.Get(path);
		long agora = NowMs();

		RecusaDeLicao r = EnsinoDeSkill.Avaliar(
			doMestre: mestre.Livro, doAluno: aluno.Livro, s,
			mestreTemAssinatura: EhPessoa(mestre), alunoTemAssinatura: EhPessoa(aluno),
			mesmaPessoa: mestre == aluno,
			distanciaTiles: mestre.Zone.Hash != aluno.Zone.Hash
				? double.MaxValue : Vec2.Distance(mestre.Pos, aluno.Pos) / ZoneCollision.TileSize,
			naRecarga: mestre.RecargaDaLicao > agora);

		if (r != RecusaDeLicao.Pode)
		{
			Avisar(mestre, PorQueNaoEnsina(r, s, aluno, mestre, agora));
			return;
		}

		// A RECARGA CORRE A PARTIR DAQUI. Ver o cabecalho do metodo.
		mestre.RecargaDaLicao = agora + (long)(EnsinoDeSkill.RecargaSegundos * 1000);

		aluno.PedidoDeLicao = new PedidoDeLicao(mestre.Assinatura, mestre.Name, s!.Path,
			agora + (long)(EnsinoDeSkill.PrazoDaOfertaSegundos * 1000));

		Avisar(mestre, $"voce comeca a mostrar {s.Nome} a {aluno.Name}.");
		// A MENSAGEM DIZ OS DOIS NUMEROS, como a caixa do DM (`:48`) -- inclusive a parte que
		// costuma surpreender: **os marcos nao somem**. Sem essa frase o jogador junta marcos
		// achando que vai gastar, e nunca descobre que so precisava chegar la.
		Avisar(aluno, $"{mestre.Name} se oferece pra te ensinar {s.Nome}. "
					+ $"Exige {EnsinoDeSkill.Custo(s)} marco(s) e voce tem {aluno.Livro.MarcosLivres} -- "
					+ "e aprender NAO gasta marco nenhum. Aceite ou recuse na aba Learning "
					+ $"({EnsinoDeSkill.PrazoDaOfertaSegundos:0} s).");
	}

	/// <summary>
	/// A RECUSA DIZ O QUE FALTA. Mesmo criterio do `Motivo` das skills compradas e do
	/// `PorQueNaoVincula` do discipulado -- e aqui ela carrega uma carga extra: as duas frases de
	/// <see cref="RecusaDeLicao.AprendeuDeAlguem"/> e <see cref="RecusaDeLicao.SemMarcos"/> sao o
	/// unico lugar do jogo onde o jogador descobre as duas regras centrais do sistema (nao se
	/// repassa o que foi ensinado; o custo e limiar e nao preco).
	/// </summary>
	private static string PorQueNaoEnsina(RecusaDeLicao r, Skill? s, ServerPlayer aluno,
										  ServerPlayer mestre, long agora) => r switch
	{
		RecusaDeLicao.NaoExiste => "essa habilidade nao existe.",
		RecusaDeLicao.NaoSabe => "voce nao sabe isso.",
		RecusaDeLicao.NaoSeEnsina => $"{s?.Nome ?? "isso"} nao se ensina -- nao ha como mostrar.",
		RecusaDeLicao.AprendeuDeAlguem =>
			$"{s?.Nome ?? "isso"} foi ENSINADO a voce, e quem aprendeu de um mestre nao repassa.",
		RecusaDeLicao.SemAssinatura => "essa criatura nao tem identidade propria.",
		RecusaDeLicao.EleMesmo => "ninguem ensina a si mesmo.",
		RecusaDeLicao.AlunoJaSabe => $"{aluno.Name} ja sabe isso.",
		RecusaDeLicao.Longe => $"{aluno.Name} precisa estar colado em voce.",
		RecusaDeLicao.Recarga =>
			$"voce acabou de ensinar ({(mestre.RecargaDaLicao - agora) / 1000.0:0.#} s).",
		RecusaDeLicao.SemMarcos =>
			$"{aluno.Name} ainda nao andou o bastante: precisa TER {(s != null ? EnsinoDeSkill.Custo(s) : 0)} "
			+ $"marco(s) livre(s) e tem {aluno.Livro.MarcosLivres} (nenhum sera gasto).",
		_ => "agora nao.",
	};

	/// <summary>
	/// A RESPOSTA DO ALUNO (`teachable.dm:49-62`).
	///
	/// TUDO E REVALIDADO, pelo mesmo motivo do `ResponderAoMestre`: entre a oferta e o "sim" os dois
	/// se afastaram, o aluno pode ter aprendido a skill por outro caminho, o mestre pode ter saido.
	/// A recarga do mestre e a UNICA coisa que nao volta a ser conferida -- ela ja foi paga la atras,
	/// e cobra-la de novo aqui puniria o aluno por demorar a responder.
	/// </summary>
	private void ResponderALicao(ServerPlayer aluno, bool aceitou)
	{
		if (aluno.PedidoDeLicao is not { } p) { Avisar(aluno, "ninguem esta te ensinando nada."); return; }
		if (NowMs() > p.Ate)
		{
			aluno.PedidoDeLicao = null;
			Avisar(aluno, $"a licao de {p.NomeDoMestre} ja tinha passado.");
			return;
		}

		aluno.PedidoDeLicao = null;
		if (_skills == null) return;

		ServerPlayer? mestre = CorpoDaAssinatura(p.SigMestre);
		if (mestre == null) { Avisar(aluno, $"{p.NomeDoMestre} nao esta mais por aqui."); return; }

		Skill? s = _skills.Get(p.Path);
		if (!aceitou)
		{
			// AS DUAS MENSAGENS DO DM (`:59-60`): o mestre fica sabendo. Ensinar e um gesto entre
			// duas pessoas, e um "nao" mudo faria o mestre achar que o verb quebrou.
			Avisar(aluno, $"voce decide nao aprender {s?.Nome ?? "isso"} agora.");
			Avisar(mestre, $"{aluno.Name} escolheu nao aprender {s?.Nome ?? "isso"}.");
			return;
		}

		RecusaDeLicao r = EnsinoDeSkill.Avaliar(
			doMestre: mestre.Livro, doAluno: aluno.Livro, s,
			mestreTemAssinatura: EhPessoa(mestre), alunoTemAssinatura: EhPessoa(aluno),
			mesmaPessoa: mestre == aluno,
			distanciaTiles: mestre.Zone.Hash != aluno.Zone.Hash
				? double.MaxValue : Vec2.Distance(mestre.Pos, aluno.Pos) / ZoneCollision.TileSize,
			naRecarga: false);

		if (r != RecusaDeLicao.Pode)
		{
			string porque = PorQueNaoEnsina(r, s, aluno, mestre, NowMs());
			Avisar(aluno, porque);
			Avisar(mestre, $"a licao com {aluno.Name} nao fechou: {porque}");
			return;
		}

		if (!EnsinoDeSkill.Ensinar(aluno.Livro, s!)) return;

		// O RESTO E O MESMO DE QUEM COMPRA (ver `Aprender`): sem estas tres linhas a skill entra no
		// livro e nao acontece nada -- sem botao, sem buff, sem bit de poder. A skill "entrou" e o
		// jogador nao ganhou nada, calado. (O `AplicarEfeitos` ja recalcula as arvores destravadas,
		// e na ordem certa: os contadores do corpo sao escritos por ele.)
		MandarSkills(aluno, forcar: true);
		AplicarPoderes(aluno);
		AplicarEfeitos(aluno);

		Avisar(aluno, $"{mestre.Name} te ensinou {s!.Nome}! Seus marcos continuam intactos "
					+ $"({aluno.Livro.MarcosLivres}) -- mas voce nao pode repassar isto a ninguem.");
		Avisar(mestre, $"{aluno.Name} aprendeu {s.Nome}.");
		GD.Print($"[server] ensino: {mestre.Name} ensinou '{s.Nome}' a {aluno.Name} "
			   + $"(limiar {EnsinoDeSkill.Custo(s)} marcos, o aluno segue com {aluno.Livro.MarcosLivres})");
	}

	/// <summary>
	/// `Forget_Studied_Skill` (`teachable.dm:25`): so o que foi ENSINADO se esquece.
	///
	/// **SEM DEVOLVER MARCO**, e o porque esta no <see cref="EnsinoDeSkill.Esquecer"/> -- o aluno nao
	/// pagou nada, entao o reembolso do `forget()` do DM imprimiria marcos do nada.
	/// </summary>
	private void EsquecerLicao(ServerPlayer pl, string path)
	{
		if (_skills == null) { Avisar(pl, "o servidor esta sem catalogo de skills."); return; }
		if (_skills.Get(path) is not { } s) { Avisar(pl, "essa habilidade nao existe."); return; }

		if (!pl.Livro.FoiEnsinada(s.Path))
		{
			Avisar(pl, pl.Livro.Sabe(s.Path)
				? $"{s.Nome} e sua -- voce nao aprendeu de ninguem, e so se esquece o que foi ensinado."
				: $"voce nao sabe {s.Nome}.");
			return;
		}

		// O NIVEL DA SKILL **NAO** E ZERADO, e isso e o precedente e nao um esquecimento: o
		// `admin_skill` (`GameServer.Admin.cs:1133`) tambem so mexe no livro e chama a mesma trinca.
		// Um segundo jeito de "tirar uma skill de alguem" divergiria do primeiro no dia seguinte.
		EnsinoDeSkill.Esquecer(pl.Livro, s);
		MandarSkills(pl, forcar: true);
		AplicarPoderes(pl);
		AplicarEfeitos(pl);
		Avisar(pl, $"voce deixa {s.Nome} pra tras. Nenhum marco volta -- nenhum tinha sido gasto.");
		GD.Print($"[server] ensino: {pl.Name} esqueceu a licao '{s.Nome}'");
	}
}
