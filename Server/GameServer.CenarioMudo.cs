using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A GUARDA DE BOOT DO CENARIO MUDO -- nada e solido e invisivel calado, e a MAQUINA entra na conta.
///
/// ============================ POR QUE ELA PRECISOU EXISTIR ============================
/// A varredura anterior deste projeto (`CidadeBench`) fechou com *"nenhuma celula solida sem dono, 0
/// em 27 zonas"* -- e o dono achou mais no dia seguinte. A causa nao foi um erro de conta: foi a
/// PERGUNTA. Ela perguntava **"quem responde por esta parede?"** e aceitava quatro donos (fonte,
/// porta, MAQUINA, planta da cidade). Uma maquina sem arte E um dono aceito. Ou seja, a varredura
/// isentava exatamente o caso que o jogador ve: ele esbarra no nada, soca, e o nada quebra com todos
/// os efeitos porque o servidor sabe que ha um objeto ali.
///
/// Entao a isencao morre aqui, e no BOOT: quem POE a maquina de pe (`CarregarObjetosDoMapa`) e
/// tambem quem tem que dizer, em voz alta e pelo NOME DO TIPO, que ela vai bloquear sem aparecer.
/// =====================================================================================
///
/// ============================ O QUE ELA CONSEGUE VER, E O QUE NAO ============================
/// O servidor le `.col`, `.duro`, `.objetos`, `.portas` e o catalogo -- e nao le `.pedacos`, que e a
/// pintura do cenario (so o cliente monta tilemap). Ou seja **ele nao sabe se um TILE desenhou**, e
/// mentir sobre isso seria pior que calar. Essa metade e do PIPELINE, que tem a arvore de tipos do
/// DM na mao e nomeia o typepath: `dotnet run --project Tools/AssetPipeline -- censo ...` (ver
/// `CensoMudoBench`, que agora REPROVA em vez de so contar).
///
/// O que mora aqui e a metade que so o boot conhece: a lista do que esta REALMENTE de pe neste
/// mundo, com o catalogo carregado por cima.
/// =============================================================================================
///
/// DUAS SEVERIDADES, e a diferenca e a queixa do dono:
///   * `PushError`   -- vai bloquear e NAO vai desenhar (arte vazia, `.tres` sumido). E o defeito.
///   * `PushWarning` -- vai desenhar o QUADRO ERRADO (o `icon_state` nao existe na folha e o cliente
///     cai na primeira animacao, ver `ObraDesenhada.MontarSprite`). Feio, e visivel -- outra queixa.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// Roda no fim do <see cref="CarregarTech"/> -- depois do catalogo E das maquinas de pe, que sao
	/// as duas coisas que ela cruza. Devolve quantos ALARMES (nao avisos) saiu, pra quem quiser
	/// medir: um `foreach` com `PushError` dentro da carga nao pode ser provado, e essa licao ja
	/// esta escrita no `AvisarDasMudas`.
	/// </summary>
	private int ConferirCenarioMudo()
	{
		if (_catalogo == null) return 0;

		// ---- 1. AS MAQUINAS QUE O MAPA POE DE PE ----
		// Por TIPO e nao por celula: doze copias da mesma bancada muda sao um defeito, e nao doze.
		var zonasDo = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		foreach (ZoneEntry e in _catalogo.Todas)
		{
			if (e.Objetos.Length == 0 || !Godot.FileAccess.FileExists(e.Objetos)) continue;
			foreach (ObjetoDoMapa o in ObjetosDoMapa.Parse(Godot.FileAccess.GetFileAsString(e.Objetos)))
				(zonasDo.TryGetValue(o.Id, out List<string>? l) ? l : zonasDo[o.Id] = []).Add(e.Zona);
		}

		int alarmes = 0;
		foreach ((string id, List<string> zonas) in zonasDo.OrderBy(kv => kv.Key, StringComparer.Ordinal))
		{
			string onde = $"{zonas.Count} celula(s) em {string.Join(", ", zonas.Distinct())}";
			Construcao? c = _obras?.Get(id);
			if (c == null)
			{
				alarmes++;
				GD.PushError($"[cenario] a maquina '{id}' esta no mapa ({onde}) e NAO existe no "
							 + "catalogo -- ela nao sera desenhada nem posta de pe. Rode o "
							 + "AssetPipeline ('tech').");
				continue;
			}

			(bool desenha, bool quadroCerto, string porque) = JulgarArteDaObra(c);
			if (!desenha)
			{
				alarmes++;
				GD.PushError($"[cenario] '{c.Id}' ({c.Tipo}) esta no mapa ({onde})"
							 + (c.Densa ? " e BLOQUEIA" : "") + $", e nao tem desenho: {porque}. "
							 + "E parede invisivel -- converta o .dmi e rode o AssetPipeline ('tech').");
			}
			else if (!quadroCerto)
				GD.PushWarning($"[cenario] '{c.Id}' ({c.Tipo}, {onde}) desenha o QUADRO ERRADO: "
							   + $"{porque}. O cliente cai na primeira animacao da folha.");
		}

		// ---- 2. AS PORTAS ----
		// Elas sao a OUTRA isencao da varredura velha, pela mesma regra: porta e um "dono aceito" da
		// parede. Uma porta cuja folha sumiu bloqueia fechada e nao aparece.
		var portasPorArte = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		foreach (ZoneEntry e in _catalogo.Todas)
		{
			if (e.Portas.Length == 0 || !Godot.FileAccess.FileExists(e.Portas)) continue;
			foreach (PortaDoMapa p in PortasDaZona.Parse(Godot.FileAccess.GetFileAsString(e.Portas)))
				(portasPorArte.TryGetValue(p.Arte, out List<string>? l) ? l : portasPorArte[p.Arte] = [])
					.Add(e.Zona);
		}

		foreach ((string arte, List<string> zonas) in portasPorArte.OrderBy(kv => kv.Key, StringComparer.Ordinal))
		{
			string onde = $"{zonas.Count} porta(s) em {string.Join(", ", zonas.Distinct())}";
			if (arte.Length == 0 || !Godot.FileAccess.FileExists(arte))
			{
				alarmes++;
				GD.PushError($"[cenario] porta sem folha de sprite ('{arte}', {onde}) -- ela bloqueia "
							 + "fechada e nao aparece. Rode o AssetPipeline ('maps').");
				continue;
			}

			// OS QUATRO ESTADOS, e nao so o "closed": o `Porta.cs` do cliente cai de `opening` pra
			// `open` e de `closing` pra `closed` quando falta (`Porta.cs:82`), entao faltar um dos
			// dois do meio e feio e nao e mudo -- mas faltar `closed` e uma porta fechada invisivel.
			HashSet<string> anims = AnimacoesDoTres(arte);
			string[] faltando = [.. new[] { "closed", "open", "opening", "closing" }.Where(a => !anims.Contains(a))];
			if (faltando.Contains("closed"))
			{
				alarmes++;
				GD.PushError($"[cenario] a folha de porta '{arte}' ({onde}) nao tem o estado "
							 + "\"closed\" -- porta fechada e invisivel.");
			}
			else if (faltando.Length > 0)
				GD.PushWarning($"[cenario] a folha de porta '{arte}' ({onde}) nao tem "
							   + $"{string.Join("/", faltando)} -- a porta pula direto pro estado final.");
		}

		// ---- 3. O RESUMO, e ele sai SEMPRE ----
		// Silencio total e indistinguivel de "a guarda nao rodou", e foi assim que este defeito
		// sobreviveu meses: nao faltou codigo, faltou alguem PERGUNTAR em voz alta.
		GD.Print($"[cenario] guarda do mudo: {zonasDo.Count} tipo(s) de maquina e "
				 + $"{portasPorArte.Count} folha(s) de porta no mapa | alarmes: {alarmes}");
		return alarmes;
	}

	/// <summary>
	/// ESTA CONSTRUCAO APARECE, E APARECE CERTO?
	///
	/// A pergunta e feita do jeito que o CLIENTE a faz (`ObraDesenhada.MontarSprite`), e nao de um
	/// jeito parecido: mesmo saneamento de nome, mesma queda pra primeira animacao. Uma guarda que
	/// julga por uma regra propria acusa o que funciona e absolve o que quebra.
	/// </summary>
	private static (bool Desenha, bool QuadroCerto, string Porque) JulgarArteDaObra(Construcao c)
	{
		if (c.Arte.Length == 0) return (false, false, "`arte` vazia no construcoes.json");
		if (!Godot.FileAccess.FileExists(c.Arte)) return (false, false, $"o `.tres` '{c.Arte}' nao existe");

		HashSet<string> anims = AnimacoesDoTres(c.Arte);
		if (anims.Count == 0) return (false, false, $"'{c.Arte}' nao tem animacao nenhuma");

		string alvo = c.Estado.Length > 0 ? SanearComoOCliente(c.Estado) : "default";
		if (anims.Contains(alvo)) return (true, true, "");
		return (true, false, $"'{c.Arte}' nao tem o estado \"{alvo}\" (de icon_state=\"{c.Estado}\")");
	}

	/// <summary>
	/// OS NOMES DE ANIMACAO DE UM `.tres`, lidos como TEXTO.
	///
	/// De proposito: `ResourceLoader.Load&lt;SpriteFrames&gt;` traria a folha inteira -- todas as
	/// texturas -- pra dentro da memoria do SERVIDOR, que nao desenha nada e roda headless. Sao
	/// algumas dezenas de arquivos pequenos e o dado que interessa e uma linha de cada um.
	/// </summary>
	private static HashSet<string> AnimacoesDoTres(string res)
	{
		var nomes = new HashSet<string>(StringComparer.Ordinal);
		if (!Godot.FileAccess.FileExists(res)) return nomes;
		foreach (System.Text.RegularExpressions.Match m in
				 System.Text.RegularExpressions.Regex.Matches(
					 Godot.FileAccess.GetFileAsString(res), "\"name\": &\"([^\"]*)\""))
			nomes.Add(m.Groups[1].Value);
		return nomes;
	}

	/// <summary>
	/// O MESMO saneamento do <c>ObraDesenhada.Sanear</c> e do `SpriteFramesWriter`. Copiado e nao
	/// chamado porque aquele mora no CLIENTE, e o servidor nao depende do cliente -- mas copiar tem
	/// preco, entao fica dito: se um mudar, o outro tem que mudar junto, e o sintoma de nao mudarem
	/// e esta guarda acusar exatamente as construcoes que estao certas.
	/// </summary>
	private static string SanearComoOCliente(string s)
	{
		var sb = new System.Text.StringBuilder(s.Length);
		foreach (char ch in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
		string r = sb.ToString().Trim('_');
		while (r.Contains("__")) r = r.Replace("__", "_");
		return r.Length == 0 ? "state" : r;
	}
}
