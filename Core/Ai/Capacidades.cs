using Jandirus.Core.Forms;

namespace Jandirus.Core.Ai;

/// <summary>
/// O QUE O JOGO RESPONDEU QUE ESTE CORPO PODE FAZER -- e nao o que a IA acha que pode.
///
/// ============================ ESTE E O DISPOSITIVO ANTI-ATALHO ============================
/// A disciplina "a IA passa pelos mesmos funis" e facil de escrever num comentario e impossivel de
/// garantir com boa vontade: basta alguem, com pressa, escrever `npc.Voando = true` em vez de
/// `AlternarVoo(npc)` -- e nao ha teste funcional que pegue isso, porque o NPC voa nos dois casos.
/// O que aparece seis meses depois e "o NPC voa sem ficar sem Ki", e ai o defeito ja nao tem dono.
///
/// A saida e mecanica e nao disciplinar: **todo campo daqui e o valor de retorno de uma funcao de
/// RECUSA da producao** -- `PodeVoar`, `CargaDeKi.SabeReunir`, `TemEscada`, `AscendePorDecisao`,
/// `EstadoDeForma.Proxima`/`Avaliar`. O cerebro so pode QUERER o que estas funcoes ja disseram
/// sim; e como o atuador nao confere nada, uma recusa nova que apareca amanha (uma skill, um
/// cargo, um debuff) passa a valer pra a IA no mesmo commit em que passa a valer pro jogador,
/// sem ninguem lembrar dela.
///
/// O CEREBRO NAO PODE CONSTRUIR ISTO. Ele mora no Core e nao conhece `ServerPlayer` -- quem
/// preenche e `GameServer.LerCapacidades`, e e ele que paga a leitura (1 Hz, ver o rodizio).
/// ======================================================================================
///
/// ============================ E NAO E CACHE DE PERMISSAO ============================
/// A tentacao seguinte seria o cerebro CONFIAR nisto pra pular a chamada ("PodeVoar e falso, entao
/// nem tento"). Ele pula mesmo -- e isso e PODA, nao permissao: serve pra o saibaman nao tentar
/// ascender 4 vezes por segundo pra sempre. A recusa de verdade continua acontecendo la, no funil,
/// e o `Transformar` continua sendo quem decide. Se este struct mentir (porque envelheceu um
/// segundo), o pior que acontece e uma tentativa que o jogo recusa -- exatamente o que acontece com
/// um jogador que aperta C na hora errada.
/// ================================================================================
/// </summary>
public readonly struct Capacidades
{
	/// <summary>
	/// `GameServer.PodeVoar` -- nivel 1 da skill `flying` OU metade da maestria de Ki.
	///
	/// **O dono foi explicito**: a IA so voa *"se ela tiver a capacidade de voar"*. Sem isto um
	/// NPC de saibaman tentaria decolar a cada decisao e ouviria a recusa pra sempre.
	/// </summary>
	public bool PodeVoar { get; init; }

	/// <summary>O que custa SAIR DO CHAO agora (`Voo.CustoParaLigar`). Zero pra quem tem proficiencia alta.</summary>
	public double CustoDeDecolar { get; init; }

	/// <summary>O que custa CONTINUAR no ar, por segundo (`Voo.CustoPorSegundo`). E o dreno mais caro do jogo.</summary>
	public double CustoDoVooPorSegundo { get; init; }

	/// <summary>`CargaDeKi.SabeReunir` -- o `MeditateGivesKiRegen`. Sem isto a tecla C nao faz nada.</summary>
	public bool SabeReunirKi { get; init; }

	/// <summary>
	/// HA UM DEGRAU ACIMA DESTE CORPO? E o `EstadoDeForma.Proxima() != null` -- o MESMO seletor da
	/// tecla C, com o MESMO catalogo e os MESMOS portoes.
	/// </summary>
	public bool HaDegrauAcima { get; init; }

	/// <summary>
	/// POR QUE O DEGRAU DE CIMA NAO SAI AGORA -- avaliado com o Ki **REAL**.
	///
	/// ============================ POR QUE ESTE CAMPO EXISTE SEPARADO ============================
	/// `Proxima` avalia com `kiFracao: 1` (`EstadoDeForma.cs:143`) -- ela responde "que degrau este
	/// corpo alcanca", nao "da pra agora". Entao um NPC com 5% de Ki tem `HaDegrauAcima` verdadeiro
	/// e ouviria `SemKi` toda vez que apertasse.
	///
	/// Com a recusa REAL na mao, `SemKi` deixa de ser fracasso e vira **pre-condicao reparavel**: o
	/// cerebro carrega primeiro e sobe depois -- que e a cadeia que o DM tambem tem, com o gate
	/// literal `if(Ki &lt; MaxKi * 0.25) return` no `npc_try_transform()` (`NPCAI.dm:365`).
	/// As outras recusas (`SemPoder`, `SemMaestria`, `SemFuria`, `LinhaFechada`) NAO sao reparaveis
	/// dentro de uma luta, e e o que faz a poda ser correta em vez de teimosa.
	/// ========================================================================================
	/// </summary>
	public RecusaForma RecusaDaForma { get; init; }

	/// <summary>
	/// `GameServer.AscendePorDecisao` -- **"tem a forma e nao a usa"**.
	///
	/// Falso no chefe roteirizado (o Freeza do planeta Vegeta). Quem barra de verdade e o
	/// `Transformar`, que e o funil; isto e a poda, pra ele nao apertar C pra sempre contra uma
	/// porta que nunca abre.
	/// </summary>
	public bool AscendePorDecisao { get; init; }

	/// <summary>
	/// TEM BRACO OU PERNA INTEIRO PRA APARAR? E a mesma pergunta do `MeleeResolver.EscolherGuarda`:
	/// sem nenhum, a guarda cai sozinha no primeiro golpe (`MeleeResolver.cs:95`).
	/// </summary>
	public bool TemComQueAparar { get; init; }

	/// <summary>O que cada golpe APARADO custa de Ki (`MaxKi * CombatKnobs.CustoKiDaGuarda`).</summary>
	public double CustoDaGuarda { get; init; }

	/// <summary>
	/// O QUE ELE PODE ATIRAR DE LONGE -- vazio pra quem nao comprou nenhuma das tres skills que
	/// voam. Ver <see cref="Arsenal"/> e `TecnicasDeLonge`.
	///
	/// ============================ POR QUE ISTO MORA AQUI E NAO NUM CAMPO NOVO DO CEREBRO ============================
	/// Porque e exatamente a mesma pergunta que os outros campos deste struct fazem -- *"o que o
	/// JOGO respondeu que este corpo pode?"* -- e por isso ela herda, de graca, as tres propriedades
	/// que fazem este arquivo ser o dispositivo anti-atalho:
	///
	///   1. e preenchido SO pelo servidor, a partir do que a producao respondeu (o livro de skills e
	///      o preco que a propria tecnica cobra), e nunca pelo cerebro;
	///   2. paga o relogio de 1 Hz que ja existe, entao a varredura do livro que o beam vai exigir
	///      ja nasce fora do caminho de 30 Hz;
	///   3. o atuador continua nao conferindo nada: ele manda o id pelo `UsarHabilidade` e e a
	///      TECNICA quem recusa por recarga, por Ki ou por "voce nao sabe isso".
	///
	/// Um `List&lt;string&gt; TecnicasDaIa` no `Cerebro` teria parecido mais simples e seria a segunda
	/// verdade sobre o que o corpo sabe -- a que envelhece quando o jogador esquece uma skill.
	/// ==========================================================================================================
	/// </summary>
	public Arsenal DeLonge { get; init; }

	/// <summary>
	/// ELE SABE O SOPRO? (`Kiai`) -- a resposta do MESMO `SabeTecnica` que responde ao verb do
	/// jogador, perguntada no relogio de 1 Hz junto com o resto.
	///
	/// ============================ POR QUE UM BOOL, E NAO UMA LINHA NO <see cref="Arsenal"/> ============================
	/// Porque o sopro **nao e um tiro**, e por na tabela de longe seria a IA escolhendo entre ele e um
	/// raio pela mesma nota. O `Kiai` nao viaja, nao tem alcance, nao tem risco de errar e nao tem
	/// linha de visao: ele ARREMESSA quem esta colado (`Ki2.0/Kiai.dm:15`) e existe pra UMA situacao
	/// -- estar encurralado. No DM ele tambem esta fora da rotacao normal: e um `if` proprio, ANTES da
	/// economia de Ki e antes do ramo de blaster (`NPCAI.dm:399`).
	///
	/// Um `Tiro` de alcance zero na tabela teria feito o `EscolherTiro` compara-lo com o raio por
	/// `(1 - risco) * CustoDeKi` -- e como ele e o mais caro dos quatro, ele venceria quase sempre.
	/// ================================================================================================================
	/// </summary>
	public bool SabeSopro { get; init; }

	/// <summary>
	/// O QUE O SOPRO COBRA, em Ki ABSOLUTO pra este corpo (`50 * BaseDrain`). Mesma disciplina do
	/// <see cref="Tiro.CustoDeKi"/>: o numero sai da funcao que o VERB usa, e nunca de uma copia.
	///
	/// O DM confere `40 * BaseDrain` no `npc_kiai()` (`NPCAI.dm:296`) e o verb do jogador cobra 50 --
	/// ou seja, la a IA tem um preco proprio e mais barato que o das pessoas. Aqui ela paga o do
	/// jogador, que e a regra deste port inteiro.
	/// </summary>
	public double CustoDoSopro { get; init; }

	/// <summary>
	/// DA PRA SUBIR UM DEGRAU AGORA, sem nenhuma pendencia? Derivado -- um campo ao lado seria a
	/// mesma verdade duas vezes.
	/// </summary>
	public bool PodeSubirForma => HaDegrauAcima && AscendePorDecisao && RecusaDaForma == RecusaForma.Pode;

	/// <summary>
	/// SO FALTA FOLEGO? A unica recusa que uma luta consegue resolver -- ver <see cref="RecusaDaForma"/>.
	/// </summary>
	public bool FormaSoFaltaKi => HaDegrauAcima && AscendePorDecisao && RecusaDaForma == RecusaForma.SemKi;
}
