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
		EfeitosDeSkill.Aplicar(pl.Ficha, _skills, pl.Livro.Aprendidas);

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

		Recusa r = pl.Livro.Aprender(_skills, path, pl.Race, pl.Class, vilao: false);
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
		string sig = $"{pl.Livro.MarcosLivres}/{pl.Livro.MarcosTotais}:{pl.Livro.Aprendidas.Count}";
		if (!forcar && sig == pl.SigSkills) return;
		pl.SigSkills = sig;

		var w = Protocol.Begin(Protocol.S2C.Skills);
		w.Put(pl.Livro.MarcosTotais);
		w.Put(pl.Livro.MarcosLivres);
		w.Put((ushort)pl.Livro.Aprendidas.Count);
		foreach (string p in pl.Livro.Aprendidas) w.Put(p);
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
