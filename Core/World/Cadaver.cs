namespace Jandirus.Core.World;

/// <summary>
/// ============================ O CADAVER QUE FICA ============================
/// *"ao morrer o corpo deve FICAR NO CHAO ate alguem ENTERRAR ele (basta apertar E perto do corpo
/// pra enterrar, ou vc pode AGARRAR o corpo e levar pra outro lugar). o corpo mesmo morto TEM TODAS
/// AS INTERACOES DE UM CORPO VIVO: pode sofrer dano, ser agarrado, jogado etc, e pessoas podem
/// agarrar o corpo e SAIR VOANDO com ele, e ele tb vai estar considerado como VOANDO"* -- o pedido.
///
/// Aqui mora so o que as duas pontas precisam concordar sem se falar: **como o cadaver se chama**,
/// **quantos cabem num lugar** e **o que se escreve na lapide**. As regras de dentro (quem o deixa,
/// quem o desfaz, quem o enterra) moram em `GameServer.Cadaver.cs`.
/// ==========================================================================
///
/// ============================ O DESENHO E O DO DM, E ELE E DE DOIS OBJETOS ============================
/// `mob/proc/Death()` faz `GenerateCorpse()` no passo 5 (`Death.dm:64-67`) e `loc = locate(187,104,6)`
/// no passo 11 (`:110`). Ou seja: **o cadaver e um objeto SEPARADO que fica, e o mob VIAJA**. O
/// `GenerateCorpse` (`Corpse.dm:75-85`) nao copia ficha nenhuma -- ele copia o ICONE e os OVERLAYS
/// daquele instante:
///
///     var/obj/mobCorpse/A = new
///     A.loc = loc
///     A.name = "[name]'s Corpse"
///     A.icon = icon ; A.icon_state = "KO"
///     A.overlays += icon('Bloody body.dmi',"KO")
///     A.overlays += overlays
///
/// **ISSO E O ITEM 6 DA TAREFA RESOLVIDO POR CONSTRUCAO, e nao por um corte escrito depois**: o
/// cadaver nao carrega vida nem BP porque ele nunca recebeu nenhum dos dois. Ver
/// `GameServer.Cadaver.DeixarOCadaver` -- a ficha dele nasce ZERADA, e o unico campo que vem do
/// morto e a APARENCIA, que e literalmente o que o DM copia.
///
/// ============================ E O PORT DIVERGE NUM PONTO, DE PROPOSITO ============================
/// La o cadaver e um `/obj` (`IsntAItem=1`, `Corpse.dm:2`): ele nao apanha, nao e agarrado e nao voa.
/// Aqui ele e um CORPO, porque o pedido do dono e literalmente *"TODAS AS INTERACOES DE UM CORPO
/// VIVO"* -- e porque o agarrao deste port foi escrito sobre CORPO e nao sobre "esta vivo?" (ver o
/// cabecalho de `GameServer.Agarrao.cs`). Sendo corpo, ele ganha de graca: colisao (`GradeDeCorpos`
/// le a `ZoneList`), agarrao, arremesso pelo funil do `Empurrao`, altitude de quem o carrega e a pose
/// deitada. **Nao ha uma linha de "se for cadaver" em nenhum desses sistemas.**
/// ==================================================================================================
/// </summary>
public static class Cadaver
{
	// =====================================================================
	// 1. O NOME
	// =====================================================================
	/// <summary>
	/// `A.name = "[name]'s Corpse"` (`Corpse.dm:78`), em portugues.
	///
	/// O NOME MUDA, E ISSO E DELIBERADO -- o boneco do corpo largado mantem o nome do dono de
	/// proposito (*"pessoas do lado de fora vao ver vc MEDITANDO normalmente"*), e aqui e o oposto: o
	/// dono daquele corpo **nao esta mais nele**, ele esta no Outro Mundo. Um cadaver que continuasse
	/// se chamando "Fulano" faria a lista de quem esta na zona mentir, e faria quem chegasse depois
	/// achar que Fulano esta ali deitado -- quando Fulano ja renasceu do outro lado do mapa.
	/// </summary>
	public static string NomeDo(string nome) => $"corpo de {nome}";

	/// <summary>
	/// O EPITAFIO PADRAO -- o `"Here lies [name]"` que o DM oferece como resposta pre-preenchida do
	/// `input()` (`Corpse.dm:20`).
	///
	/// ============================ NO DM O TEXTO E DIGITADO -- E AQUI TAMBEM, DESDE 2026-09-04 ============================
	/// `GenerateCross(input(usr,"Grave text.","","Here lies [name]") as text)` abre uma caixa de texto
	/// do BYOND. O menu deste port so sabia escolher da lista e digitar numero; o dono pediu a terceira
	/// pergunta -- *"ao criar a lapide deveria ter a opcao de escrever manualmente na lapide, abrindo
	/// uma caixa de texto"* -- e ela existe (`Interacoes.Forma.Texto`, `Client/CaixaDeTexto.cs`). Esta
	/// funcao e o VALOR INICIAL do campo, exatamente o papel que o `input()` lhe da; o que a pessoa
	/// confirmar passa por <see cref="EpitafioLimpo"/> no servidor.
	/// ====================================================================================================
	/// </summary>
	public static string EpitafioPadrao(string nome) => $"Aqui jaz {nome}";

	/// <summary>Teto do que cabe numa lapide. O fio aceita 256 (`Protocol.MaxArgDeVerbo`); uma frase de tumulo nao precisa de metade.</summary>
	public const int MaxEpitafio = 120;

	/// <summary>O AVESSO de <see cref="NomeDo"/>: de "corpo de Fulano" tira "Fulano" -- o menu E so tem o nome do alvo.</summary>
	public static string QuemJazEm(string nomeDoCorpo) =>
		nomeDoCorpo.StartsWith("corpo de ", StringComparison.Ordinal) ? nomeDoCorpo["corpo de ".Length..] : nomeDoCorpo;

	/// <summary>
	/// O TEXTO QUE VAI PRA LAPIDE, a partir do que foi digitado: sem quebra de linha (a lapide e uma
	/// frase, e o chat a le numa linha), aparado, com um espaco so entre palavras, cortado no teto -- e
	/// o padrao do DM quando veio vazio, que e o que o `input()` devolve se a pessoa so confirmar.
	/// Puro e sem servidor dentro, pra bancada medir os quatro casos sem forjar corpo nenhum.
	/// </summary>
	public static string EpitafioLimpo(string digitado, string nome)
	{
		string t = string.Join(' ', (digitado ?? "").Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
		if (t.Length == 0) return EpitafioPadrao(nome);
		return t.Length <= MaxEpitafio ? t : t[..MaxEpitafio].TrimEnd();
	}

	// =====================================================================
	// 2. QUANTOS CABEM
	// =====================================================================
	/// <summary>
	/// ============================ QUANTO TEMPO O CADAVER FICA: **PARA SEMPRE** ============================
	/// **NAO HA PRAZO, E A AUSENCIA DELE E A DECISAO.** O dono disse *"ate alguem ENTERRAR"*, e o DM
	/// concorda por omissao: `obj/mobCorpse` (`Corpse.dm:1-49`) **nao tem `del` com relogio nenhum** --
	/// o cadaver de la so some por uma das quatro maos (`Eat`, `Bury`, `Destroy`, `Skin_Corpse`). A
	/// varredura foi feita: o unico `mobCorpse` citado fora do proprio arquivo e o `Rewards.dm:199`,
	/// que o EXCLUI de uma lista de premio.
	///
	/// Um cronometro aqui seria a mesma familia de defeito que o `Agarrao` recusou: um corpo que se
	/// desfaz sozinho tira do jogador a unica coisa que o pedido pede -- a decisao de enterrar.
	/// ====================================================================================================
	///
	/// ============================ MAS O TETO EXISTE, E O NUMERO E MEDIDO ============================
	/// Sem prazo, o que segura o mundo e a LOTACAO. E ela tem preco conhecido, ja escrito neste port
	/// pra a populacao (`Povoamento.MaxPorZona`): **cada corpo custa ~16 bytes por tique, 30 vezes por
	/// segundo** -- ~480 B/s por corpo, no canal de CADA pessoa que estiver na zona. Um cadaver custa
	/// exatamente o que um cidadao custa, porque ele e um corpo igual no snapshot.
	///
	/// 24 cadaveres = ~11,5 KB/s por espectador. E menos de um terco do que os 80 habitantes que a
	/// mesma zona ja pode ter custam (~38 KB/s), e cobre com folga o que o jogo produz: uma briga
	/// grande deixa 5 a 10 corpos, e uma invasao de ondas deixa umas dezenas espalhadas pelo mapa.
	///
	/// **QUANDO ESTOURA, O MAIS ANTIGO SE DESFAZ** -- e nao o mais novo. O corpo recem-caido e o que
	/// alguem esta vindo buscar; o de vinte minutos atras e cenario. E a mesma escolha que o dono ja
	/// aprovou noutro lugar: *"prefira sempre o que nao prende ninguem"*.
	///
	/// **ELE NAO E UM PRAZO DISFARCADO**: numa zona com dois corpos, os dois ficam pra sempre. O teto
	/// so morde onde ha matanca em serie -- que e exatamente onde a banda de rede importa.
	/// ============================================================================================
	/// </summary>
	public const int TetoPorZona = 24;

	/// <summary>
	/// ============================ O CADAVER NAO VAI PRO DISCO, E ISSO E ESCOLHA ============================
	/// Ele vive em memoria, como o NPC e como o boneco do corpo largado -- um reinicio limpa o campo de
	/// batalha. A LAPIDE, sim, vai (ela e uma `Obra`, e `Obra` mora no `mundo.json`).
	///
	/// A assimetria e o ponto: o que o jogador FEZ (enterrar alguem) e patrimonio do mundo e sobrevive
	/// ao reinicio; o que ficou por fazer nao e. Gravar cadaveres poria estado de combate no arquivo
	/// que guarda as CONSTRUCOES -- duas vidas uteis muito diferentes no mesmo arquivo, que e o mesmo
	/// argumento que manteve as macas das arvores fora dele.
	/// ========================================================================================================
	/// </summary>
	public const bool VaiProDisco = false;
}
