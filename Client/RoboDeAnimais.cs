using System.Collections;
using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.World;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DOS ANIMAIS (`--diaganimais`) -- a ponta do CLIENTE do pedido do dono de 2026-10-10 (*"adicione
/// animais npcs (como lobos etc) (ja tem sprite deles nos assets...)"*). A regra inteira e medida pela bancada
/// do servidor (`--animaisteste`); o que ela nao ve e a TELA: se cada bicho e desenhado com a folha da especie
/// (e nao como um humano palido), sem cabelo nem olhos de gente por cima, se anda, se a carcaca fica tombada no
/// chao, se a tecla E a enxerga e se o menu de revistar abre com a carne.
///
/// O palco e do servidor (`GameServer.ZooDeTeste`): um bicho de cada especie em volta do host, os mansos numa
/// fileira ao norte e os bravos numa ao sul (quietos pelos primeiros segundos, pra foto). O resto e o jogo.
///
/// Rode com janela (no segundo monitor), no modo host, com BP de sobra pro host aguentar as mordidas:
///   --host --rede 8165 --campoteste 9 --horateste 0.5 --bpteste 3000000 --diaganimais
///   --raca Human --conta bancada_animais_d --nome Fotografo
/// As fotos saem em `user://animais-0N-*.png`.
/// </summary>
public partial class RoboDeAnimais : Node
{
	private int _ok, _falhou;
	private IEnumerator? _roteiro;
	private double _espera = 3.0;
	private bool _acabou;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[danimais]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; GD.PrintErr($"[danimais]   FALHA {nome}   [{detalhe}]"); }
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (GameClient.Instance is not { Connected: true } || World.Instancia?.PosicaoLocal == null) return;

		_espera -= delta;
		if (_espera > 0) return;

		_roteiro ??= Roteiro().GetEnumerator();
		if (!_roteiro.MoveNext())
		{
			_acabou = true;
			GD.Print($"\n[danimais] ===== {_ok} OK, {_falhou} FALHA(S) =====");
			return;
		}
		_espera = _roteiro.Current is double d ? d : 0;
	}

	private void Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { GD.Print("[danimais] sem foto (headless nao renderiza): rode com janela"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		GD.Print($"[danimais] foto {caminho}");
	}

	private static CharacterVisual? VisualDe(int id) =>
		World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");

	/// <summary>
	/// O quadro que o corpo esta mostrando: quantos pixels tem tinta (zero = folha vazia, ou pose que nao existe)
	/// e um carimbo do desenho (pra saber se ele MUDOU de um instante pro outro).
	/// </summary>
	private static (int Tinta, ulong Carimbo) Quadro(CharacterVisual vis)
	{
		if (vis.QuadroDoCorpoDeTeste() is not { } img) return (0, 0);
		int n = 0;
		ulong carimbo = 1469598103934665603UL;
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = 0; x < img.GetWidth(); x++)
			{
				Color c = img.GetPixel(x, y);
				if (c.A > 0.5f) n++;
				carimbo = (carimbo ^ c.ToRgba32()) * 1099511628211UL;
			}
		return (n, carimbo);
	}

	private IEnumerable Roteiro()
	{
		GameClient cli = GameClient.Instance!;
		World mundo = World.Instancia!;
		if (GameServer.Instance is not { } srv) { Afirmar("(montagem) o robo roda no modo host", false); yield break; }

		// ------------------------------------------------------------ 1. o palco: um de cada especie
		List<(int Id, string Nome, string Folha, bool Bravo)> zoo = srv.ZooDeTeste(cli.LocalId);
		Afirmar("(montagem) o palco pos um bicho de cada especie em volta do host, mansos e bravos",
				zoo.Count >= 4 && zoo.Exists(z => z.Bravo) && zoo.Exists(z => !z.Bravo),
				string.Join(", ", zoo.Select(z => z.Nome)));
		if (zoo.Count == 0) yield break;
		yield return 2.5;

		// ------------------------------------------------------------ 2. desenhados como bicho
		var errados = new List<string>();
		foreach ((int id, string nome, string folha, bool _) in zoo)
		{
			if (VisualDe(id) is not { } vis) { errados.Add($"{nome}: sem corpo na tela"); continue; }
			if (vis.FolhaDoCorpoDeTeste != folha) errados.Add($"{nome}: folha '{vis.FolhaDoCorpoDeTeste}'");
			if (vis.TemCabeloVisivelDeTeste) errados.Add($"{nome}: com cabelo");
			if (vis.OlhosVisiveisDeTeste) errados.Add($"{nome}: com olhos de gente");
			if (vis.RoupasNoCorpoDeTeste().Count > 0) errados.Add($"{nome}: com roupa");
			if (Math.Abs(vis.RotationDegrees) > 1f) errados.Add($"{nome}: vivo e girado {vis.RotationDegrees:0} graus");
			int tinta = Quadro(vis).Tinta;
			if (tinta < 40) errados.Add($"{nome}: quadro vazio na pose '{vis.PoseDeTeste}' ({tinta} px)");
		}
		Afirmar("**CADA BICHO E DESENHADO COM A FOLHA DA ESPECIE**, com tinta no quadro, de pe, e sem cabelo, olhos ou roupa de gente por cima",
				errados.Count == 0, string.Join(" | ", errados));
		Fotografar("animais-01-um-de-cada-especie");

		// ------------------------------------------------------------ 3. andando
		var antes = new Dictionary<int, Vector2>();
		var desenhos = new Dictionary<int, HashSet<ulong>>();
		foreach ((int id, string _, string _, bool _) in zoo)
		{
			if (mundo.PosicaoDesenhadaDe(id) is { } p) antes[id] = p;
			desenhos[id] = [];
		}
		var andou = new HashSet<int>();
		for (int i = 0; i < 140; i++)
		{
			yield return 0.1;
			foreach ((int id, string _, string _, bool _) in zoo)
			{
				if (mundo.PosicaoDesenhadaDe(id) is { } p && antes.TryGetValue(id, out Vector2 a) && p.DistanceTo(a) > 12f) andou.Add(id);
				if (VisualDe(id) is { } vis) desenhos[id].Add(Quadro(vis).Carimbo);
			}
		}
		// (DOIS TERCOS de quem andou, e nao todos: a amostra e a cada decimo de segundo, e um passo curto -- doze
		// pixels sao um setimo de segundo andando -- cabe inteiro dentro de UM quadro de uma caminhada de dois.)
		int animaram = zoo.Count(z => andou.Contains(z.Id) && desenhos[z.Id].Count >= 2);
		Afirmar("**ELES ANDAM NA TELA**: em catorze segundos a maioria saiu do lugar, e quem andou trocou de quadro (a caminhada anima)",
				andou.Count * 2 >= zoo.Count && animaram * 3 >= andou.Count * 2,
				$"{andou.Count} de {zoo.Count} andaram; {animaram} deles mostraram mais de um quadro");
		string? quemMordeu = srv.QuemMordeuDeTeste(cli.LocalId);
		Afirmar("...e um BRAVO veio ate o host e mordeu (os mansos, nao)",
				quemMordeu != null && zoo.Exists(z => z.Bravo && z.Nome == quemMordeu), quemMordeu ?? "ninguem mordeu");
		Fotografar("animais-02-pastando-e-mordendo");

		// ------------------------------------------------------------ 4. o abate, a carcaca e a tecla E
		(int Id, string Nome, string Folha, bool Bravo) caca = zoo.Find(z => !z.Bravo && z.Nome == "Vaca");
		if (caca.Id == 0) caca = zoo.Find(z => !z.Bravo);
		// (Os outros saem do palco: a foto do menu nao pode ter um leao mordendo o fotografo.)
		srv.LimparZooDeTeste(caca.Id);
		yield return 0.6;
		Afirmar("(montagem) o servidor abateu o bicho ao lado do host, em nome dele", srv.AbaterDeTeste(caca.Id, cli.LocalId), caca.Nome);
		yield return 1.5;

		(int Id, int Carne, Vec2 Onde)? carcaca = srv.CarcacaDeTeste(caca.Nome);
		Afirmar("o bicho abatido SAIU da tela e a CARCACA entrou no lugar",
				carcaca != null && mundo.CorpoDeTeste(caca.Id) == null && mundo.CorpoDeTeste(carcaca.Value.Id) != null,
				carcaca == null ? "o servidor nao tem a carcaca" : $"carcaca #{carcaca.Value.Id}");
		if (carcaca is not { } c) yield break;
		CharacterVisual? visDaCarcaca = VisualDe(c.Id);
		Afirmar("**A CARCACA E O MESMO BICHO, TOMBADO**: a folha da especie, girada de lado (um bicho morto nao pode parecer um vivo parado)",
				visDaCarcaca != null && visDaCarcaca.FolhaDoCorpoDeTeste == caca.Folha
				&& Math.Abs(Math.Abs(visDaCarcaca.RotationDegrees) - 90f) < 1f && Quadro(visDaCarcaca).Tinta >= 40,
				visDaCarcaca == null ? "sem corpo" : $"folha '{visDaCarcaca.FolhaDoCorpoDeTeste}', girada {visDaCarcaca.RotationDegrees:0} graus");

		Vector2 eu = mundo.PosicaoDesenhadaDe(cli.LocalId) ?? Vector2.Zero;
		(int Id, string Nome, Vector2 Onde)? naMao = mundo.CaidoPerto(eu, Jandirus.Core.Tech.Interacoes.Alcance, cli.CadaverPerto);
		Afirmar("**A TECLA E ENXERGA A CARCACA** (o alvo de revistar do menu), e o servidor nao a oferece pra enterrar",
				naMao?.Id == c.Id && cli.CadaverPerto != c.Id, naMao == null ? "nenhum caido ao alcance" : $"#{naMao.Value.Id} '{naMao.Value.Nome}'");
		Fotografar("animais-03-a-carcaca-no-chao");

		// o gesto do menu: o mesmo verbo que a tecla E manda
		cli.SendVerbo(Saque.Verbo);
		yield return 1.2;
		// (2026-10-13) quem desenha o saque e a tela da mochila, aberta na "mochila" da carcaca -- ver `TelaDeInventario.AbrirSaque`
		(bool Aberto, int Itens, string Titulo) menu = TelaDeInventario.Instancia?.SaqueDeTeste ?? (false, 0, "sem tela");
		Afirmar("**A TELA DE REVISTAR ABRE COM A CARNE**", menu.Aberto && menu.Itens == 1 && menu.Titulo.StartsWith("MOCHILA DE", StringComparison.Ordinal),
				$"aberto={menu.Aberto}, {menu.Itens} item(ns), titulo '{menu.Titulo}'");
		Fotografar("animais-04-revistar-a-carcaca");

		int carneAntes = cli.Mochila.Quantos(CatalogoDeItens.Carne);
		for (int i = 0; i < c.Carne; i++)
		{
			// (mais que o respiro entre um item e outro -- `Saque.SegundosEntreSaques` --, senao o servidor
			// responde "um de cada vez" e o pedaco fica)
			cli.SendVerbo(Saque.Verbo, CatalogoDeItens.Carne);
			yield return Saque.SegundosEntreSaques + 0.5;
		}
		Afirmar("**A CARNE VAI PRA MOCHILA DO JOGADOR**, pedaco por pedaco, e a carcaca vazia some da tela",
				cli.Mochila.Quantos(CatalogoDeItens.Carne) == carneAntes + c.Carne && mundo.CorpoDeTeste(c.Id) == null,
				$"{cli.Mochila.Quantos(CatalogoDeItens.Carne) - carneAntes} de {c.Carne} na mochila");
		ItemDef? def = CatalogoDeItens.Get(CatalogoDeItens.Carne);
		Afirmar("a carne tem ficha e ARTE no cliente (o icone da mochila)", def != null && ResourceLoader.Exists(def.Arte), def?.Arte ?? "sem ficha");

		// ------------------------------------------------------------ 5. comer
		srv.EsvaziarEstomagoDeTeste(cli.LocalId);
		cli.SendVerbo("item_comer", CatalogoDeItens.Carne);
		yield return 1.2;
		Afirmar("**COMER A CARNE** pela porta da mochila: um pedaco a menos, e o estomago enche bastante",
				cli.Mochila.Quantos(CatalogoDeItens.Carne) == carneAntes + c.Carne - 1 && srv.NutricaoDeTeste(cli.LocalId) >= 25,
				$"nutricao {srv.NutricaoDeTeste(cli.LocalId):0.#}");
		Fotografar("animais-05-depois-da-caca");
		srv.LimparZooDeTeste(0);
	}
}
