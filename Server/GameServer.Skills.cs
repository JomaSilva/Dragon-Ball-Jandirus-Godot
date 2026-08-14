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
		if (save != null && save.MarcosTotais > 0)
		{
			pl.Livro.MarcosTotais = save.MarcosTotais;
			pl.Livro.MarcosLivres = save.MarcosLivres;
		}
		else pl.Livro.Conceder(MarcosIniciais);
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
		EfeitosDeSkill.Aplicar(pl.Ficha, _skills, pl.Livro.Aprendidas, pl.Livro.Escolhas);

		// A ORDEM IMPORTA: os contadores `bodyskill`/`bodyreadiness` acabaram de ser escritos
		// pelos efeitos, e sao eles que dizem que arvore o progresso abriu. Recalcular antes
		// disso usaria os valores da compra ANTERIOR -- a arvore nova so apareceria na proxima
		// skill comprada, e o jogador atribuiria isso a sorte.
		int antes = pl.Livro.Destravadas.Count;
		pl.Livro.RecalcularDestravadas(pl.Ficha.bodyskill, pl.Ficha.bodyreadiness, pl.Ficha.weaponeq > 0);
		if (pl.Livro.Destravadas.Count > antes)
			Avisar(pl, "o que voce treinou abriu um caminho novo -- olhe a aba de aprendizado.");

		pl.Ficha.Statify();
		pl.SigAtributos = "";
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
		Recusa r = pl.Livro.Aprender(_skills, path, pl.Race, pl.Class, vilao: EhVilao(pl));
		if (r != Recusa.Pode)
		{
			Avisar(pl, Motivo(r, _skills.Get(path)));
			return;
		}

		Skill s = _skills.Get(path)!;
		GD.Print($"[server] {pl.Name} aprendeu '{s.Nome}' ({SkillCatalog.CustoDe(s)} marcos, restam {pl.Livro.MarcosLivres})");
		Avisar(pl, EfeitoEmTexto(s));
		MandarSkills(pl, forcar: true);
		AplicarPoderes(pl);
		AplicarEfeitos(pl);
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

	private static string Motivo(Recusa r, Skill? s) => r switch
	{
		Recusa.NaoExiste => "essa habilidade nao existe.",
		Recusa.JaSabe => "voce ja sabe isso.",
		Recusa.Desligada => $"{s?.Nome ?? "essa habilidade"} esta desativada neste servidor.",
		Recusa.SoVilao => "so um vilao aprende isso.",
		Recusa.RacaOuClasse => "sua raca ou classe nao aprende isso.",
		Recusa.SemArvore => "isso nao pende de nenhuma arvore sua -- e ensinado, nao comprado.",
		Recusa.FaltaPreRequisito => "falta pre-requisito.",
		Recusa.SemMarcos => $"marcos insuficientes ({(s != null ? SkillCatalog.CustoDe(s) : 0)} necessarios).",
		_ => "nao deu.",
	};

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
		string sig = $"{pl.Livro.MarcosLivres}/{pl.Livro.MarcosTotais}:{pl.Livro.Aprendidas.Count}"
				   + $":{(EhVilao(pl) ? 'v' : '-')}";
		if (!forcar && sig == pl.SigSkills) return;
		pl.SigSkills = sig;

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
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
