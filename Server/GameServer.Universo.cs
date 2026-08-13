using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A PONTA DO SERVIDOR NA PERGUNTA "AS DUAS PONTAS ENUMERAM O MESMO UNIVERSO?".
///
/// ============================ POR QUE ISTO PRECISOU EXISTIR ============================
/// A regra 0.2 diz que cliente e servidor chegam ao mesmo mundo sem trocar um byte de mapa, e que
/// todo sistema novo precisa de uma assinatura equivalente a do <see cref="TerrenoGerado.Assinatura"/>.
/// A assinatura de <see cref="Sistemas.Assinatura"/> existe desde a Fase 2 -- mas ate agora ela era
/// conferida por um TERCEIRO processo (a bancada do `AssetPipeline`), que nao e ponta nenhuma: ele
/// chama a mesma funcao no mesmo binario e sempre concorda consigo mesmo.
///
/// O precedente do terreno tambem nao fecha o laco: o servidor IMPRIME a dele
/// (`GameServer.Procedural.cs`) e o cliente IMPRIME a dele (`Client.PlanetaProcedural`), e quem
/// compara os dois numeros e um ser humano lendo dois logs. Isso nao reprova nada -- e "falhar
/// alto" e a regra 5 da Parte 3.
///
/// Este verb fecha o laco: o cliente PEDE a assinatura ao servidor, o servidor a calcula com a
/// SEED DELE, e a comparacao acontece em codigo, do lado que sabe qual era a resposta esperada.
/// =====================================================================================
///
/// ============================ ELE NAO E UM CAMINHO PARALELO ============================
/// A regra 0.7 proibe bancada que chama atalho proprio. Aqui nao ha atalho: a resposta sai de
/// <see cref="Sistemas.Assinatura"/> e de <see cref="Sistemas.Do"/>, as MESMAS funcoes que decidem
/// onde os planetas nascem, alimentadas pela MESMA <see cref="SeedDoUniverso"/> que o servidor usa
/// pra pousar gente. Um defeito na enumeracao aparece aqui porque e a enumeracao de verdade.
/// ===================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// TETO DO LADO DA REGIAO PEDIDA -- e ele e uma trava de servico, nao um capricho.
	///
	/// A assinatura enumera TODOS os planetas da regiao e monta o nome de cada um (5,2 ms medidos
	/// pra 32x32 celulas, ~5.560 corpos). O custo cresce com o QUADRADO do lado: um cliente pedindo
	/// `lado 1000` compraria 1 milhao de celulas, ~5,4 milhoes de corpos e minutos de tique parado --
	/// e um tique parado e o servidor inteiro parado, nao so quem pediu (regra 0.4).
	///
	/// 32 e o maior lado que a propria bancada mede em 5,2 ms, ou seja abaixo de um tique de 33 ms.
	/// Acima disso o pedido e RECUSADO em vez de truncado: truncar responderia uma assinatura de
	/// outra regiao com cara de resposta certa, que e o modo de falha da regra 5 da Parte 3.
	/// </summary>
	private const int LadoMaximoDaAssinatura = 32;

	/// <summary>
	/// A RESPOSTA E LEGIVEL POR MAQUINA, e de proposito.
	///
	/// Todo o resto do `Avisar` e prosa pro jogador. Esta linha e a unica do servidor que existe pra
	/// ser PARSEADA, e por isso ela tem um prefixo proprio e campos separados por barra: uma bancada
	/// que tivesse que achar um hexadecimal no meio de uma frase quebraria no dia em que alguem
	/// mexesse na frase, e quebraria dizendo "as duas pontas divergem" -- a pior mentira possivel
	/// pra esta bancada contar.
	/// </summary>
	internal const string MarcaDaAssinatura = "univ|";

	/// <summary>
	/// A ASSINATURA DE UMA REGIAO, PELA PONTA DO SERVIDOR.
	///
	/// Argumento: "sx sy lado". Devolve, alem do hash, quantos sistemas e quantos planetas a regiao
	/// tem -- e esses dois numeros nao sao enfeite: sem eles, "as duas pontas deram o mesmo numero"
	/// pode significar "as duas enumeraram o VAZIO". Uma regiao onde todas as celulas fossem
	/// anuladas assinaria igual dos dois lados e nao provaria nada.
	/// </summary>
	private void VerboAssinaturaDoUniverso(ServerPlayer pl, string arg)
	{
		string[] p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (p.Length != 3
			|| !int.TryParse(p[0], out int sx) || !int.TryParse(p[1], out int sy)
			|| !int.TryParse(p[2], out int lado))
		{ Avisar(pl, $"{MarcaDaAssinatura}erro|argumento (esperado: sx sy lado)"); return; }

		if (lado < 1 || lado > LadoMaximoDaAssinatura)
		{ Avisar(pl, $"{MarcaDaAssinatura}erro|lado {lado} fora de 1..{LadoMaximoDaAssinatura}"); return; }

		long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
		// ============================ ESTA LINHA JA FOI SABOTADA DE PROPOSITO, E FUNCIONOU ============================
		// Trocar `SeedDoUniverso` por `SeedDoUniverso + 1` aqui e rodar o par servidor/cliente poe a
		// familia 1 da `--diaguniverso` em 12 falhas: as SEIS assinaturas divergem, e as seis
		// contraprovas de "com a seed+1 a assinatura MUDA" tambem caem -- porque com o servidor um
		// degrau adiante, e a seed+1 do cliente que passa a bater. Ou seja: a bancada nao so reprova,
		// ela DIZ QUAL e o defeito.
		//
		// O que ficou VERDE na sabotagem e a licao que vale guardar: "as contagens batem" e "as duas
		// pontas estao na MESMA seed" continuaram passando com o universo inteiro trocado. Contagem e
		// numero anunciado nao provam enumeracao -- so o hash da enumeracao inteira prova.
		// =========================================================================================================
		ulong h = Sistemas.Assinatura(SeedDoUniverso, sx, sy, lado);

		// A CONTAGEM ANDA JUNTO COM A ASSINATURA, na mesma varredura conceitual e com a mesma funcao
		// pura. Se ela saisse de outra fonte (um cache, uma tabela), as duas poderiam discordar entre
		// si e o servidor estaria se contradizendo sozinho.
		int sistemas = 0, planetas = 0, anuladas = 0;
		for (int y = sy; y < sy + lado; y++)
			for (int x = sx; x < sx + lado; x++)
			{
				if (Sistemas.Do(SeedDoUniverso, x, y) is not { } s) { anuladas++; continue; }
				sistemas++;
				planetas += s.Orbitas;
			}
		// O TEMPO VAI EM MICROSSEGUNDOS INTEIROS, e nao em milissegundos com virgula.
		//
		// `Avisar` formata com a cultura corrente do processo, e a desta maquina escreve "5,200"
		// onde a bancada esperaria "5.200" -- o cliente leria 5.200 ms e a linha de custo mentiria
		// por mil. Numero que atravessa o fio nao tem separador decimal.
		long us = (long)(System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1000);

		Avisar(pl, $"{MarcaDaAssinatura}ok|{sx}|{sy}|{lado}|{h:X16}|{sistemas}|{planetas}|{anuladas}"
				   + $"|{SeedDoUniverso}|{us}");
	}

	/// <summary>
	/// LEVA O ADMIN AO CENTRO DA ESTRELA MAIS PROXIMA.
	///
	/// ============================ POR QUE UM VERB, E NAO UM ATALHO DE BANCADA ============================
	/// O `--solteste` prova o funil do SERVIDOR com corpos forjados (`Peer = null`): ele fere, mata e
	/// confere a bandeira. O que ele NAO pode provar e que a morte CHEGA em quem esta jogando -- os
	/// corpos dele nao tem soquete, entao nenhum pacote sai e nenhum aviso viaja.
	///
	/// Pra provar a corrente inteira (tique do servidor -> `Ferir` -> `Morrer` -> aviso -> cliente) e
	/// preciso um jogador DE VERDADE dentro de uma estrela. Voar ate uma custa minutos (a de Earth
	/// esta a 7.952 px, e ela e a mais perto que existe do nascimento), o que nenhuma bancada pode
	/// pagar -- e o piloto automatico nao teleporta de proposito.
	///
	/// Entao o teleporte e um verb de admin, que e o que ele seria de qualquer jeito: "me poe dentro
	/// daquela estrela" e exatamente a ferramenta que um admin quer pra conferir o sistema em jogo.
	/// A busca sai de <see cref="Sistemas.EstrelaPerto"/>, a mesma que o `TickDoSol` consulta.
	/// =================================================================================================
	///
	/// SEM RECUO SILENCIOSO: fora do espaco ele RECUSA em vez de decolar sozinho. Um verb que muda de
	/// zona por conta propria e um verb que faz duas coisas, e a segunda ninguem pediu.
	/// </summary>
	private void AdminIrParaAEstrela(ServerPlayer adm)
	{
		if (!Espaco.EhEspaco(adm.Zone)) { Avisar(adm, "decole primeiro: a estrela fica no espaco."); return; }

		if (!Sistemas.EstrelaPerto(SeedDoUniverso, adm.Pos, out Estrela e, out double dist))
		{ Avisar(adm, "nenhuma estrela nas celulas em volta."); return; }

		MoveToZone(adm.Id, ZonaDoEspaco, e.Pos);

		// O CARIMBO DE CHUNK E A VIZINHANCA VAO JUNTO. E o mesmo par de linhas do `MandarProBerco` e
		// do `DecolarDeProcedural`: sem eles quem salta pra outra chunk chega sem nenhum corpo
		// desenhado, inclusive sem a estrela em que acabou de entrar.
		adm.ChunkAtual = ChunkId.De(adm.Pos);
		MandarVizinhanca(adm);

		Avisar(adm, $"voce salta {dist:0} px ate o centro de uma estrela de raio {e.Raio:0}.");
		GD.Print($"[server] {adm.Name} foi levado ao centro de uma estrela {e.Classe} (raio {e.Raio:0} px)");
	}
}
