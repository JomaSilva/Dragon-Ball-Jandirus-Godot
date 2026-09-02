using Godot;
using Jandirus.Core.Skills;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// SKILLS, LADO DO SERVIDOR: quem sabe o que, e quem pode comprar o que.
///
/// O catalogo (319 skills em 47 arvores) vem do `skills.json` que o Tools/AssetPipeline extrai
/// da arvore de tipos do DM. Aqui nao ha lista de skill nenhuma escrita a mao -- o que existe e
/// a REGRA: arvore vem da raca, skill pende de arvore, marco e a moeda.
///
/// A VALIDACAO E TODA AQUI e o cliente tem a MESMA funcao (<see cref="SkillBook.PodeAprender"/>,
/// que mora no Core). O cliente usa pra pintar o botao e nao prometer o que vai ser recusado; o
/// servidor usa pra decidir. Uma regra so, nos dois lados -- e o que impede as duas pontas de
/// divergirem em silencio.
/// </summary>
public partial class GameServer
{
	private SkillCatalog? _skills;

	/// <summary>
	/// Marcos que um personagem novo recebe.
	///
	/// Nao e generosidade: sem nenhum marco a aba de aprendizado nasce inteira apagada, e um
	/// jogador novo nao tem como descobrir que o sistema existe. Tres compra as skills de base
	/// de tier 1 e deixa a escolha de vocacao pra ele.
	/// </summary>
	private const int MarcosIniciais = 3;

	private void CarregarSkills()
	{
		const string cs = "res://Assets/Data/skills.json";
		const string ct = "res://Assets/Data/skilltrees.json";
		if (!Godot.FileAccess.FileExists(cs) || !Godot.FileAccess.FileExists(ct))
		{
			GD.PushWarning("[server] sem skills.json -- rode o AssetPipeline (comando 'skills')");
			return;
		}
		_skills = SkillCatalog.Parse(Godot.FileAccess.GetFileAsString(cs), Godot.FileAccess.GetFileAsString(ct));
		GD.Print($"[server] skills: {_skills.Total} entradas ({_skills.Arvores.Count()} arvores)");
	}

	/// <summary>Monta o livro de skills de quem entrou, do save ou do zero.</summary>
	private static void PrepararSkills(ServerPlayer pl, CharacterSave? save)
	{
		pl.Livro = new SkillBook();
		if (save is { Skills.Count: > 0 }) pl.Livro.Carregar(save.Skills);

		// QUAIS DELAS VIERAM DE UM MESTRE (o `wastaught` do DM). **DEPOIS do `Carregar`**, que e o
		// que a `CarregarEnsinadas` exige pra poder descartar marca orfa.
		//
		// SEM ESTA LINHA a marca morre no logout, e o efeito e exatamente a corrente que o sistema
		// existe pra impedir: aprendeu, deslogou, voltou repassando. E ela some CALADA -- a skill
		// continua no livro, so o "nao repassa" evapora.
		if (save is { SkillsEnsinadas.Count: > 0 }) pl.Livro.CarregarEnsinadas(save.SkillsEnsinadas);

		// A CASA ESCOLHIDA nas skills de escolha unica, pela MESMA razao e na MESMA ordem: e um
		// dado a mais sobre uma skill que ele ja sabe, e o leitor descarta escolha orfa.
		if (save is { SkillsEscolhas.Count: > 0 }) pl.Livro.CarregarEscolhas(save.SkillsEscolhas);
		// O QUE A COMPRA SOMOU NA FICHA (o `storedBP` do DM), pelo mesmo motivo e na mesma ordem.
		if (save is { SkillsGanhos.Count: > 0 }) pl.Livro.CarregarGanhos(save.SkillsGanhos);
		if (save != null && save.MarcosTotais > 0)
		{
			pl.Livro.MarcosTotais = save.MarcosTotais;
			pl.Livro.MarcosLivres = save.MarcosLivres;
		}
		else pl.Livro.Conceder(MarcosIniciais);

		MigrarRazao(pl, save);
	}

	/// <summary>A versao do razao que este servidor grava. Ver `CharacterSave.RazaoVersao`.</summary>
	public const int RazaoVersaoAtual = 1;

	/// <summary>
	/// OS CAMPOS QUE NASCERAM NESTE LOTE. Um save de versao 0 pode ter qualquer um deles no razao como
	/// "ja aplicado" -- sem o campo existir, o `Aplicar` registrava o total inteiro mesmo assim. A
	/// lista e fechada e datada de proposito: e a lista do que NAO existia antes da versao 1.
	/// </summary>
	private static readonly string[] CamposNovosNaVersao1 =
		["pitted", "HPregenbuff", "KaiokenMastery", "RegenerationDeSkill", "SpiritFistCost", "SpiritFistDamage"];

	/// <summary>
	/// A MIGRACAO UNICA DO RAZAO. Save antigo (versao 0): tira do razao os campos que nao existiam,
	/// pra que o proximo `Aplicar` os aplique de verdade. Roda UMA vez porque o save sai gravado com
	/// a versao atual -- rodar de novo num save ja migrado somaria o buff duas vezes.
	/// </summary>
	private static void MigrarRazao(ServerPlayer pl, CharacterSave? save)
	{
		if (save == null || save.RazaoVersao >= RazaoVersaoAtual) return;
		foreach (string campo in CamposNovosNaVersao1)
		{
			pl.Ficha.BuffsDeSkill.Remove(campo);
			pl.Ficha.FlagsDeSkill.Remove(campo);
			pl.Ficha.MultsDeSkill.Remove(campo);
			save.Niveis?.Somados.Remove(campo);
			save.Niveis?.Multiplicados?.Remove(campo);
		}
	}

	/// <summary>
	/// Poe no corpo os buffs de tudo que a pessoa sabe, e reconta a ficha.
	///
	/// CHAMADO NO LOGIN TAMBEM, e nao so ao aprender: buff de skill e permanente, mas ele vive
	/// no <see cref="Fighter"/>, que e reconstruido do save a cada entrada. Sem reaplicar aqui, a
	/// pessoa perde no relog tudo que comprou -- em silencio, porque a skill continua na lista.
	/// A aplicacao e idempotente de proposito (ver <see cref="EfeitosDeSkill"/>); reaplicar sem
	/// necessidade nao empilha.
	/// </summary>
	private void AplicarEfeitos(ServerPlayer pl)
	{
		if (_skills == null) return;

		// O QUE UM DEGRAU JA CRUZADO CONCEDE E O LIVRO AINDA NAO TEM (o Sense do nivel 5 do Ki
		// Unlocked). Entra ANTES de aplicar: a skill concedida tem os proprios efeitos e poderes.
		if (ConcederPorDegrau(pl) > 0) AplicarPoderes(pl);

		EfeitosDeSkill.Aplicar(pl.Ficha, _skills, pl.Livro.Aprendidas, pl.Livro.Escolhas);

		// A ORDEM IMPORTA: os contadores `bodyskill`/`bodyreadiness`/`kieffusionskill` acabaram de
		// ser escritos pelos efeitos, e sao eles que as regras das arvores leem (`growbranches()`,
		// extraido em `skills.json`). Recalcular antes disso usaria os valores da compra ANTERIOR
		// -- a arvore nova so apareceria na proxima skill comprada, e o jogador atribuiria isso a
		// sorte. O contexto le a ficha AO VIVO, entao ele e guardado no livro e continua certo.
		//
		// O `enableskill` DISPARADO POR DEGRAU entra pelo mesmo contexto, tambem ao vivo: e o que
		// acende Advanced Ki Awareness no nivel 100 da Basic (Mind.dm:186) -- ver `SkillBook.AvaliarArvore`.
		var abertasAntes = new HashSet<string>(pl.Livro.Destravadas, StringComparer.OrdinalIgnoreCase);
		ContextoDeRegra ctx = ContextoDeRegra.De(pl.Ficha, pl.Race, pl.Class);
		ctx.DestravadasPorDegrau = () => pl.Niveis.Destravadas();
		pl.Livro.Recalcular(_skills, ctx, pl.Race, pl.Class);
		foreach (string p in pl.Livro.Destravadas)
			if (!abertasAntes.Contains(p) && _skills.Get(p) is { } arv)
				Avisar(pl, $"o que voce treinou abriu um caminho novo: {arv.Nome} -- olhe a aba de aprendizado.");

		// O EIXO DA CURA E REFEITO: as skills que somam no `Regeneration` do genoma (Doll
		// Regeneration, Regenerate, Grace) acabaram de mexer em `RegenerationDeSkill`, e o corpo
		// guarda o PERFIL derivado dele -- e o `assign_regen()` que o DM roda depois do `add_to_stat`.
		if (pl.Combate != null) pl.Combate.Corpo.Regen = EixoDeRegen(pl.Race, pl.Ficha);

		pl.Ficha.Statify();
		pl.SigAtributos = "";

		// O ESTADO DAS ARVORES MUDOU (ou pode ter mudado) e ele viaja no `S2C.Skills`: a assinatura
		// do pacote cobre o estado, entao quem chama `MandarSkills` depois disto manda so se mudou.
		MandarSkills(pl);
	}

	// =====================================================================
	// O KI LIBERADO -- as duas pecas que a tecla C exige
	// =====================================================================
	/// <summary>
	/// DESTRAVA O KI: a compra da raiz da arvore E o degrau que acende o `canPower`.
	///
	/// ============================ SAO DUAS PECAS, E ELAS NAO SAO A MESMA ============================
	/// `Ki_Unlocked` e uma SKILL COMPRADA: ela carrega os flags do proprio catalogo
	/// (`KiUnlockPercent`, `MeditateGivesKiRegen`) e quem os escreve e o <see cref="AplicarEfeitos"/>.
	/// `Basic_Ki_Control` no NIVEL 5 e outro canal: o flag `canPower=1` mora num DEGRAU do
	/// `niveis.json`, e quem o escreve e o <see cref="Jandirus.Core.Skills.NiveisDeSkill.Aplicar"/>.
	/// E o `canPower` -- e so ele -- que deixa a carga do C passar de 100%.
	///
	/// Ter uma sem a outra e o modo de falhar silencioso deste sistema: meditar regenera Ki e o C nao
	/// carrega, ou o C carrega e a meditacao nao rende nada. Por isso as duas moram numa funcao so.
	/// ==========================================================================================
	///
	/// ============================ E POR QUE O `Basic_Ki_Control` TAMBEM ENTRA NO LIVRO ============================
	/// A versao anterior desta logica (`--kiteste`) escrevia so o NIVEL, sem por a skill no livro. O
	/// nivel nao sobrevive a isso: `NiveisDeSkill.Efetor` comeca chamando `Sincronizar(livro)`, que
	/// APAGA todo path que o livro nao conhece ("skill esquecida perde o nivel"). Ou seja, o degrau 5
	/// era removido no primeiro tique do efetor e o nivel nunca chegava ao save -- so nao aparecia
	/// como defeito porque o `canPower` ja escrito no `Fighter` nao e desfeito por ninguem (flag se
	/// escreve, nao se acumula) e porque a flag de bancada reaplicava tudo a cada login.
	///
	/// Numa concessao de RUNTIME isso deixaria de ser invisivel: o admin liberaria o Ki, jogaria, e
	/// perderia o `canPower` no relog seguinte sem uma linha explicando. Dar a skill custa nada
	/// (`SkillBook.Dar` e presente, nao compra) e faz o nivel persistir pelo caminho normal.
	/// ======================================================================================================
	/// </summary>
	private static void LiberarOKi(ServerPlayer pl)
	{
		pl.Livro.Dar(SkillKiUnlocked);
		pl.Livro.Dar(SkillKiControl);
		pl.Niveis.Por(SkillKiControl, 5);
	}

	/// <summary>
	/// CONFERE QUE A CONCESSAO PEGOU, e nao so que ela foi escrita.
	///
	/// Os dois canais (skill -> flag, degrau -> flag) ja se romperam neste projeto -- a
	/// `Tools/AssetPipeline/CargaBench.cs` existe por causa disso. "Nao deu erro" nao e prova: podia
	/// ser o personagem nao ter nascido, ou o `niveis.json` nao ter sido gerado. A linha imprime os
	/// DOIS canais e o teto de carga, e devolve se esta inteiro.
	/// </summary>
	private static bool ConferirKiLiberado(ServerPlayer pl, string origem)
	{
		bool ok = pl.Ficha.canPower != 0 && pl.Ficha.MeditateGivesKiRegen != 0;
		string msg = $"[server] {origem} em `{pl.Name}`: canPower={pl.Ficha.canPower:0} "
				   + $"MeditateGivesKiRegen={pl.Ficha.MeditateGivesKiRegen:0} "
				   + $"kicapacity={pl.Ficha.kicapacity:0.00} powerupcap={pl.Ficha.powerupcap:0.00}";
		if (ok) GD.Print(msg + "  OK");
		else GD.PushError(msg + "  <<< NAO PEGOU: o elo skill -> flag esta roto "
						+ "(ver Tools/AssetPipeline/CargaBench.cs)");
		return ok;
	}

	/// <summary>
	/// O aviso de aprendizado DIZ O QUE MUDOU. "voce aprendeu Backstab" nao ensina nada; o
	/// jogador nao tem como saber se comprou um numero ou uma tecnica, e as duas coisas se jogam
	/// de jeitos opostos. Skill sem efeito portado admite isso em vez de fingir.
	/// </summary>
	private static string EfeitoEmTexto(Skill s)
	{
		if (s.Verbos.Length > 0)
			return $"voce aprendeu {s.Nome} -- nova habilidade: {string.Join(", ", s.Verbos.Select(NomesLegiveis.Habilidade))}.";
		if (s.Buffs.Count > 0)
			return $"voce aprendeu {s.Nome} ({string.Join(", ", s.Buffs.Select(b => $"{NomesLegiveis.Campo(b.Key)} {b.Value:+0.##;-0.##}"))}).";
		// A ESCOLHA UNICA que pergunta: sem casa escolhida ela nao rende nada (e fiel), entao o aviso
		// tem que mandar escolher -- senao a Holy Trinity parece uma compra que nao fez nada.
		if (s.Escolhas.Length > 0 && s.EscolhaSegue.Length == 0)
			return $"voce aprendeu {s.Nome} -- escolha uma casa com `skill_escolha {s.Path}`.";
		if (s.Escolhas.Length > 0 || s.Genes.Count > 0 || s.Mults.Count > 0 || s.Flags.Count > 0 || s.Compra.Length > 0)
			return $"voce aprendeu {s.Nome}.";
		return $"voce aprendeu {s.Nome} (sem efeito mecanico ainda).";
	}

	/// <summary>
	/// "Quero comprar esta skill." O cliente manda o typepath; quem decide e aqui.
	///
	/// A RECUSA VOLTA COM MOTIVO, e isso nao e enfeite: "faltam marcos" e "sua raca nunca vai
	/// poder" mandam o jogador fazer coisas opostas. O original engolia a diferenca e a pessoa
	/// ficava juntando marco pra uma skill que nunca ia abrir.
	/// </summary>
	private void Aprender(ServerPlayer pl, string path)
	{
		if (_skills == null) { Avisar(pl, "o servidor esta sem catalogo de skills."); return; }

		// ============================ O `vilao:` ERA `false` CRAVADO ============================
		// Enquanto foi assim, **ninguem no port conseguia aprender Planet Destroy** -- a unica skill
		// `vilao: 1` do catalogo (1 de 366 entradas do `skills.json`). A recusa saia com o texto
		// certo ("so um vilao aprende isso") sobre uma condicao que nao existia, o que e a pior
		// forma de uma regra estar quebrada: ela PARECE ligada.
		//
		// Agora a pergunta e a ficha, e a ficha e escrita por admin (`admin_vilao`) -- literal ao
		// `villainonly = 1 //only an admin-designated Villain can learn it` (`Planets.dm:382`).
		// Ver `GameServer.EhVilao`.
		// ==================================================================================
		Veredito v = pl.Livro.Avaliar(_skills, path, pl.Race, pl.Class, vilao: EhVilao(pl));
		Recusa r = v.Motivo == Recusa.Pode
			? pl.Livro.Aprender(_skills, path, pl.Race, pl.Class, vilao: EhVilao(pl))
			: v.Motivo;
		if (r != Recusa.Pode)
		{
			Avisar(pl, Motivo(v));
			return;
		}

		Skill s = _skills.Get(path)!;
		GD.Print($"[server] {pl.Name} aprendeu '{s.Nome}' ({SkillCatalog.CustoDe(s)} marcos, restam {pl.Livro.MarcosLivres})");
		Avisar(pl, EfeitoEmTexto(s));

		// O GANHO NA COMPRA COM EXPRESSAO -- `BP += max(1, BP*0.01)`, `hiddenpotential += relBPmax*2`
		// (One Hundred, Bodybuilding.dm:89-92). Uma vez, agora, sobre a ficha de agora; o que somou
		// fica no livro pra ser devolvido ao esquecer. Ver `GanhoNaCompra`.
		Dictionary<string, double> somou = GanhoNaCompra.Aplicar(pl.Ficha, pl.Livro, s);
		if (somou.Count > 0)
			Avisar(pl, "seu corpo absorve o treino: " + string.Join(", ",
				somou.Select(kv => $"{NomesLegiveis.Campo(kv.Key)} {kv.Value:+0.##;-0.##}")) + ".");

		// O PACOTE SAI DE DENTRO DO `AplicarEfeitos`, depois do recalculo das arvores -- manda-lo
		// antes daqui entregava a lista nova com o estado VELHO das arvores, e um segundo pacote
		// logo atras. A assinatura (marcos, contagem, estado) garante que ele sai.
		AplicarPoderes(pl);
		AplicarEfeitos(pl);
	}

	// =====================================================================
	// O QUE UM DEGRAU DE NIVEL CONCEDE E ACENDE
	// =====================================================================
	/// <summary>
	/// A SKILL `flying` que o nivel 30 do Ki Unlocked entregaria (Mind.dm:109-110) NAO e concedida:
	/// o dono cortou o voo-por-skill -- "voar tem que LIBERAR SOZINHO ao chegar em metade da
	/// maestria do Ki, e antes disso nem aparecer" (ver `GameServer.Voo.PodeVoar` e
	/// `MaestriaQueDestravaVoo`). Conceder a skill aqui reabriria pelo nivel 30 a porta que ele
	/// fechou no 50. O dado continua no `niveis.json` (e fiel ao DM); o desvio mora nesta lista.
	/// </summary>
	private static readonly HashSet<string> NaoConcedidasPorDegrau =
		new(StringComparer.OrdinalIgnoreCase) { SkillDoVoo };

	/// <summary>
	/// CONCEDE o que os degraus ja cruzados entregam e o livro ainda nao tem -- o
	/// `var/datum/skill/A = new/datum/skill/sense; A.learn(savant, 1)` do Mind.dm:103-104, lido do
	/// NIVEL (e nao do instante da subida): quem passou do 5 antes de este canal existir recebe o
	/// Sense no login. Devolve quantas skills entraram.
	/// </summary>
	private int ConcederPorDegrau(ServerPlayer pl)
	{
		if (_skills == null || pl.Livro == null || pl.Niveis == null) return 0;
		int n = 0;
		foreach ((string path, int nivel) in pl.Niveis.ConcessoesPendentes(pl.Livro).ToList())
		{
			if (NaoConcedidasPorDegrau.Contains(path) || _skills.Get(path) is not { Arvore: false } s) continue;
			pl.Livro.Dar(path);
			// o `baselevel` do `learn()` -- so tem onde morar em skill COM regra de nivel (`Por` ignora
			// as outras; o Sense e uma delas: o effector dele e quarentena inteira, entao o 1 nao muda nada)
			pl.Niveis.Por(path, nivel);
			Avisar(pl, $"voce recebe {s.Nome}.");
			GD.Print($"[server] {pl.Name} recebeu '{s.Nome}' por degrau de nivel (nivel {nivel})");
			n++;
		}
		return n;
	}

	/// <summary>
	/// O TIQUE DO EFETOR DE UM CORPO -- o corpo do laco do `TickDosNiveis`, separado pra que a bancada
	/// (`--arvoreteste`) exercite a MESMA funcao num corpo forjado (que nao passa por `EhJogador`).
	/// Devolve se algum nivel subiu.
	/// </summary>
	private bool TicarNiveisDe(ServerPlayer pl)
	{
		if (_skills == null || pl.Livro == null) return false;

		// O ESTADO DO CORPO ENTRA NO EFETOR. Sem ele, as 122 regras de exp condicionais do
		// `niveis.json` continuariam abertas e descartadas -- entre elas as tres do
		// `Ki_Unlocked`, a raiz da arvore de Ki: 2 por tique MEDITANDO, 2 VOANDO, 1 parado.
		// Era por isso que meditar nao rendia maestria de Ki nenhuma.
		List<NiveisDeSkill.Subida> subiu =
			pl.Niveis.Efetor(_rng, _skills, pl.Livro,
				new NiveisDeSkill.EstadoDoCorpo(
					Meditando: pl.Ficha.med, Voando: pl.Voando, Treinando: pl.Ficha.train));
		if (subiu.Count == 0) return false;

		pl.Niveis.Aplicar(pl.Ficha);
		pl.Ficha.Statify();
		pl.SigAtributos = "";

		foreach (NiveisDeSkill.Subida s in subiu)
		{
			GD.Print($"[server] {pl.Name}: {s.Nome} chegou ao nivel {s.Nivel}");
			Avisar(pl, s.Degrau is { Aviso.Length: > 0 } d
				? $"{s.Nome} — {d.Aviso}"
				: $"{s.Nome} chega ao nível {s.Nivel}.");
		}
		// o que o degrau CONCEDE e ACENDE, e o recalculo das arvores
		DepoisDaSubida(pl, subiu);
		return true;
	}

	/// <summary>
	/// DEPOIS DE UMA SUBIDA DE NIVEL: o que o degrau concede entra no livro, o que ele acende e
	/// anunciado ("You can now learn [nS.name]!", Mind.dm:54), as arvores sao recalculadas com o
	/// nivel novo (e o `destrava` do degrau) e o cliente recebe o estado. Chamado pelo `TicarNiveisDe`.
	/// </summary>
	private void DepoisDaSubida(ServerPlayer pl, List<NiveisDeSkill.Subida> subiu)
	{
		if (_skills == null) return;
		foreach (NiveisDeSkill.Subida s in subiu)
			foreach (Degrau d in s.Degraus ?? [])
				foreach (string alvo in d.Destrava)
					if (_skills.Get(alvo) is { } acesa && !pl.Livro.Sabe(alvo))
						Avisar(pl, $"voce agora pode aprender {acesa.Nome}!");

		AplicarPoderes(pl);
		AplicarEfeitos(pl);        // concede por degrau, reaplica, recalcula as arvores e manda o pacote
		HabilidadesMudaram(pl);    // um degrau pode ter concedido verb novo: o menu precisa saber
	}

	/// <summary>
	/// A ESCOLHA UNICA: `skill_escolha <typepath> <casa>`, ou so `<typepath>` pra LISTAR as casas.
	///
	/// ============================ POR QUE ELA E SEPARADA DO APRENDER ============================
	/// No DM a pergunta e um `input()` que trava o jogador dentro do `after_learn()`
	/// (`meta.dm:105`). Num servidor autoritativo nao ha "travar o jogador" -- a pergunta vira
	/// estado: a skill fica aprendida e SEM RENDER NADA ate a resposta chegar. E isso e fiel, nao
	/// concessao: os buffs do DM moram DENTRO do `switch(input(...))`, e sem resposta nenhuma casa
	/// entra.
	///
	/// O EFEITO SO EXISTE DEPOIS: `AplicarEfeitos` passa `pl.Livro.Escolhas` pro
	/// <see cref="EfeitosDeSkill"/>, e como a aplicacao e idempotente, escolher (ou trocar, se um
	/// dia isso for permitido) recalcula do zero em vez de empilhar.
	/// ==========================================================================================
	/// </summary>
	private void VerboEscolhaDeSkill(ServerPlayer pl, string arg)
	{
		if (_skills == null || pl.Livro == null) return;

		string[] p = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
		string path = p.Length > 0 ? p[0] : "";
		Skill? s = _skills.Get(path);
		if (s == null || s.Escolhas.Length == 0) { Avisar(pl, "essa habilidade nao tem escolha nenhuma."); return; }
		if (!pl.Livro.Sabe(path)) { Avisar(pl, "voce ainda nao aprendeu isso."); return; }

		// SEM NUMERO = LISTAR. O jogador tem que poder LER as casas antes de fechar uma escolha
		// que nao volta -- e sem isto o unico jeito de saber o que cada uma faz seria adivinhar.
		if (p.Length < 2 || !int.TryParse(p[1], out int casa))
		{
			Avisar(pl, $"{s.Nome}: escolha uma linhagem.");
			for (int i = 0; i < s.Escolhas.Length; i++)
				Avisar(pl, $"   {i + 1}. {s.Escolhas[i].Rotulo} -- {ResumoDaCasa(s.Escolhas[i])}");
			return;
		}

		// A ESCOLHA E DEFINITIVA, como no DM: la o `chosen` so muda por `before_forget()`, ou seja,
		// esquecendo a skill inteira. Deixar trocar de graca transformaria os tres conjuntos num
		// menu de buff por ocasiao -- fisico pra brigar, Ki pra atirar.
		if (pl.Livro.Escolhas.ContainsKey(path)) { Avisar(pl, "voce ja escolheu, e isso nao se desfaz."); return; }

		if (!pl.Livro.Escolher(_skills, path, casa)) { Avisar(pl, "essa casa nao existe."); return; }

		Avisar(pl, $"voce escolheu: {s.Escolhas[casa - 1].Rotulo}.");
		AplicarEfeitos(pl);
	}

	/// <summary>O que uma casa da, em texto -- pro jogador poder comparar antes de decidir.</summary>
	private static string ResumoDaCasa(Escolha e)
	{
		var partes = new List<string>();
		foreach ((string campo, double v) in e.Buffs) partes.Add($"{NomesLegiveis.Campo(campo)} +{v:0.##}");
		foreach ((string campo, double v) in e.Mults) partes.Add($"{NomesLegiveis.Campo(campo)} x{v:0.##}");
		foreach ((string stat, double v) in e.Genes) partes.Add($"{stat} +{v:0.##}");
		foreach ((string campo, double v) in e.Flags) partes.Add($"{NomesLegiveis.Campo(campo)} = {v:0.##}");
		partes.AddRange(e.Verbos.Select(NomesLegiveis.Habilidade));
		return partes.Count > 0 ? string.Join(", ", partes) : "nada que o port ja entenda";
	}

	/// <summary>
	/// A RECUSA EM TEXTO, pro chat. E so a boca: o DADO e o <see cref="Veredito"/>, e e ele que a
	/// tela le (pelo `SkillBook.Avaliar` do cliente, sobre o estado que o pacote leva).
	/// </summary>
	private string Motivo(Veredito v)
	{
		Skill? s = _skills?.Get(v.Path);
		string nome = s?.Nome ?? "essa habilidade";
		return v.Motivo switch
		{
			Recusa.NaoExiste => "essa habilidade nao existe.",
			Recusa.JaSabe => "voce ja sabe isso.",
			Recusa.Desligada => $"{nome} nao acende por caminho nenhum deste port (nem pre-requisito, nem regra de arvore).",
			Recusa.SoVilao => "so um vilao aprende isso.",
			Recusa.RacaOuClasse => "sua raca ou classe nao aprende isso.",
			Recusa.SemArvore => "isso nao pende de nenhuma arvore sua -- e ensinado, nao comprado.",
			Recusa.FaltaPreRequisito => "falta pre-requisito: "
				+ string.Join(", ", v.PreReqsFaltando.Select(p => _skills?.Get(p)?.Nome ?? p)) + ".",
			Recusa.SemMarcos => $"marcos insuficientes ({v.Custo} necessarios, faltam {v.FaltamMarcos}).",
			Recusa.TierTrancado => $"{nome} e tier {v.TierDaSkill} e {_skills?.Get(v.Arvore)?.Nome ?? v.Arvore} "
				+ $"so mostra ate o tier {v.TierDaArvore}"
				+ (v.FaltaInvestir > 0 ? $" -- invista mais {v.FaltaInvestir} marco(s) nela." : "."),
			Recusa.AguardaAcendedor => $"{nome} acende quando: {v.Acendedor}.",
			Recusa.Apagada => $"{nome} foi apagada por uma regra da arvore ({v.Acendedor}).",
			_ => "nao deu.",
		};
	}

	/// <summary>
	/// `skill_esquecer <typepath>`: ESQUECE UMA SKILL COMPRADA E RECEBE OS MARCOS DE VOLTA -- o
	/// `attemptforget` da janela de arvores do DM (`SkillTreesWindow.dm`, modo FORGET), que chama o
	/// `refund()` (`trees.dm:93-98`) e, por ele, o `treeshrink()`.
	///
	/// A CASCATA E AVISADA UMA A UMA: no DM cada skill que a arvore encolhendo leva junto sai com
	/// "You don't have enough skill in [tree] to maintain [skill]!" (`trees.dm:125`). Sem o aviso
	/// o jogador esquece UMA e ve tres sumirem.
	/// </summary>
	private void VerboEsquecerSkill(ServerPlayer pl, string arg)
	{
		if (_skills == null || pl.Livro == null) return;
		string path = arg.Trim();
		Skill? s = _skills.Get(path);
		if (s == null || s.Arvore) { Avisar(pl, "essa habilidade nao existe."); return; }
		if (!pl.Livro.Sabe(path)) { Avisar(pl, $"voce nao sabe {s.Nome}."); return; }
		if (!s.Esquecivel) { Avisar(pl, $"{s.Nome} nao se esquece."); return; }
		// copia ensinada tem a propria porta (`esquecer_licao`), sem reembolso -- ver `GameServer.Ensino.cs`
		if (pl.Livro.FoiEnsinada(path)) { Avisar(pl, $"{s.Nome} foi ensinada: esqueca-a pela porta do ensino."); return; }

		int antes = pl.Livro.MarcosLivres;
		List<string> cascata = pl.Livro.EsquecerEReembolsar(_skills, path, pl.Race, pl.Class);
		Avisar(pl, $"voce esquece {s.Nome} ({pl.Livro.MarcosLivres - antes} marco(s) de volta).");
		foreach (string p in cascata)
			Avisar(pl, $"sem isso voce nao sustenta {_skills.Get(p)?.Nome ?? p}, e a esquece junto.");
		GD.Print($"[server] {pl.Name} esqueceu '{s.Nome}' (+{pl.Livro.MarcosLivres - antes} marcos, cascata: {cascata.Count})");

		// O QUE A COMPRA TINHA SOMADO VOLTA -- o `before_forget()` (`savant.BP -= storedBP`,
		// Bodybuilding.dm:98), pra skill esquecida E pra cascata que a arvore levou junto.
		foreach (string p in cascata.Prepend(path))
		{
			Dictionary<string, double> devolveu = GanhoNaCompra.Desfazer(pl.Ficha, pl.Livro, p);
			if (devolveu.Count > 0)
				Avisar(pl, $"{_skills.Get(p)?.Nome ?? p} levou embora: " + string.Join(", ",
					devolveu.Select(kv => $"{NomesLegiveis.Campo(kv.Key)} -{kv.Value:0.##}")) + ".");
		}

		AplicarPoderes(pl);
		AplicarEfeitos(pl);   // reaplica os buffs sem a skill, recalcula as arvores e manda o pacote
	}

	/// <summary>
	/// Acende os bits de <see cref="Protocol.Poder"/> que dependem de skill aprendida.
	///
	/// E o `register_html_tab("Sense")` do original virado do avesso: em vez de a skill mexer na
	/// interface, ela mexe no ESTADO, e a interface le o estado. Assim o cliente nao precisa
	/// conhecer skill nenhuma pra saber que a aba Sense existe agora.
	///
	/// ============================ O RECALCULO E DESTRUTIVO ============================
	/// `pl.Poderes` e refeito do ZERO aqui -- e tem que ser, senao um bit de uma skill esquecida
	/// ficaria aceso pra sempre. Por isso os bits CONCEDIDOS (admin) moram noutro campo e sao
	/// somados de volta no fim: eles nao vem de skill nenhuma e nao podem ser varridos junto.
	///
	/// Sem esta soma o host entrava admin e perdia o admin no mesmo login, porque `Entrar` marcava
	/// o bit ANTES de chamar este metodo. Ver `ServerPlayer.PoderesConcedidos`.
	/// ==================================================================================
	/// </summary>
	private void AplicarPoderes(ServerPlayer pl)
	{
		var p = Protocol.Poder.Nenhum;
		foreach (string path in pl.Livro.Aprendidas)
		{
			Skill? s = _skills?.Get(path);
			if (s == null) continue;
			if (s.Nome.Contains("Sense", StringComparison.OrdinalIgnoreCase)
				|| path.Contains("/sense", StringComparison.OrdinalIgnoreCase)) p |= Protocol.Poder.Sense;
		}
		pl.Poderes = p | pl.PoderesConcedidos;
		pl.SigAtributos = "";   // forca o proximo pacote de atributos a sair com o bit novo
	}

	/// <summary>Manda a lista de aprendidas e os marcos. Como o resto: so quando muda.</summary>
	private static void MandarSkills(ServerPlayer pl, bool forcar = false)
	{
		// O BIT DE VILAO ENTRA NA ASSINATURA. Todo campo que vai no pacote precisa estar aqui, senao
		// ele so chega de carona quando outro muda -- e a promocao a vilao (que nao mexe em marco
		// nem em skill aprendida) so apareceria na tela quando o jogador comprasse a proxima coisa.
		// E a mesma familia de defeito do cache da ficha.
		// O ESTADO DAS ARVORES TAMBEM ENTRA, pelo mesmo motivo do bit de vilao: um contador que
		// subiu por NIVEL (o `kieffusionskill` do degrau 35 da Basic Ki Circulation) abre a Effusive
		// Mastery sem mexer em marco nem em skill aprendida -- e a tela so saberia na proxima compra.
		string sig = $"{pl.Livro.MarcosLivres}/{pl.Livro.MarcosTotais}:{pl.Livro.Aprendidas.Count}"
				   + $":{(EhVilao(pl) ? 'v' : '-')}:{pl.Livro.AssinaturaDasArvores()}";
		if (!forcar && sig == pl.SigSkills) return;
		pl.SigSkills = sig;

		pl.Peer?.Send(MontarPacoteDeSkills(pl), Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// OS BYTES DO `S2C.Skills`. Separado do envio pra que a bancada (`--arvoreteste`) possa
	/// desmontar o pacote com o leitor do CLIENTE e conferir que o que sai no fio e o que o livro tem
	/// -- um pacote que so existe dentro de um `Peer.Send` nao se confere.
	/// </summary>
	private static NetDataWriter MontarPacoteDeSkills(ServerPlayer pl)
	{
		var w = Protocol.Begin(Protocol.S2C.Skills);
		w.Put(pl.Livro.MarcosTotais);
		w.Put(pl.Livro.MarcosLivres);

		// ============================ POR QUE O CLIENTE PRECISA SABER ============================
		// O menu de skills monta a lista chamando `PodeAprender(..., vilao:)` por conta propria
		// (`Client/MenuJogo.cs`), e ele passava `false` cravado. Sem este bit, um vilao veria a
		// unica skill de vilao do jogo desenhada como "so um vilao aprende isso" -- e ela seria
		// comprada com sucesso se ele clicasse assim mesmo, porque quem decide e o servidor.
		// Regra ligada de um lado e desligada do outro e pior do que regra desligada.
		// ==================================================================================
		w.Put(EhVilao(pl));

		w.Put((ushort)pl.Livro.Aprendidas.Count);
		foreach (string p in pl.Livro.Aprendidas) w.Put(p);

		// A CAUDA: o estado das arvores -- o RESULTADO do `growbranches()`, e nao os contadores.
		// O porque esta em `Protocol.PorEstadoDeSkills`.
		Protocol.PorEstadoDeSkills(w, pl.Livro);
		return w;
	}
}
