using Jandirus.Core.Races;
using LiteNetLib.Utils;

namespace Jandirus.Net;

/// <summary>
/// OS DETALHES DE UM CONHECIDO, como viajam no `S2C.DetalhesDeConhecido` -- o que eu sei de alguem ALEM da
/// linha da lista, e so depois de conviver o bastante com ele (`Convivio.ConvivioParaDetalhes`).
///
/// ============================ E SO ISTO, E A LISTA E FECHADA DE PROPOSITO ============================
/// Pedido do dono (2026-10-10): *"ao abrir na aba people e clicar em pessoas q vc conhece (tem q ter um certo
/// nivel de convivio minimo) vc pode ver mais detalhes da pessoa, como idade, a backstory dela etc"*.
///
/// O que entra e o que e PUBLICO de alguem: a idade e o genero (o `c_age`/`c_gender` do Contact do DM,
/// `Statistics.dm:152-154`), o cargo que ele carrega (o painel de cargos ja diz quem ocupa cada um), a HISTORIA
/// que ele escreveu na criacao pra ser lida (o verb `Backstory` do DM, `CharacterCreation.dm:3`), e duas datas
/// que sao MINHAS: desde quando eu o conheco e quando o vi por ultimo.
///
/// O QUE NAO ENTRA, E NAO PODE ENTRAR (`GameServer.Sigilo.cs`): poder de luta e atributos, a classe, onde a
/// pessoa esta, o que ela carrega. Um campo novo aqui e um campo que TODO conhecido passa a ler -- a bancada
/// (`--conhecidosteste`) confere que o pacote acaba exatamente no ultimo campo desta lista.
/// ====================================================================================================
/// </summary>
/// <param name="Assinatura">De quem sao os detalhes (a chave da lista de conhecidos).</param>
/// <param name="Genero">"Male"/"Female", como na ficha.</param>
/// <param name="Idade">Em anos, como eu a vi por ultimo.</param>
/// <param name="Titulo">O nome do cargo que ela carregava ("" = nenhum que eu saiba).</param>
/// <param name="OuviuAHistoria">FALSE = eu ainda nao estive com ela desde que o convivio abriu os detalhes.</param>
/// <param name="Historia">A historia que ela escreveu, ja limpa (`Convivio.HistoriaLegivel`).</param>
/// <param name="ConheceHaSegundos">Ha quanto tempo eu a conheco (0 = nao sei: ficha de antes deste campo).</param>
/// <param name="VistoHaSegundos">Ha quanto tempo a ficha foi vista por ultimo.</param>
public readonly record struct DetalhesDeConhecido(
	string Assinatura, string Genero, int Idade, string Titulo, bool OuviuAHistoria, string Historia,
	uint ConheceHaSegundos, uint VistoHaSegundos);

/// <summary>
/// O corpo do `S2C.DetalhesDeConhecido`, escrito e lido NUM LUGAR SO (o leitor do LiteNetLib nao confere o fim
/// do pacote: um campo a mais num dos lados le lixo). Sem o opcode: quem escreve o pos antes, quem le ja o
/// consumiu no despacho.
///
///     `string assinatura` + `string genero` + `ushort idade` + `string titulo` + `bool ouviu` +
///     `string historia` + `uint conheceHa` + `uint vistoHa`
/// </summary>
public static class FioDosConhecidos
{
	/// <summary>O teto de LEITURA de cada texto (o que passar disso chega vazio, e nao estourado).</summary>
	public const int MaxAssinatura = 96, MaxGenero = 8, MaxTitulo = 64, MaxHistoria = CharacterDraft.BackstoryMax;

	public static void EscreverDetalhes(NetDataWriter w, in DetalhesDeConhecido d)
	{
		w.Put(d.Assinatura);
		w.Put(d.Genero);
		w.Put((ushort)Math.Clamp(d.Idade, 0, ushort.MaxValue));
		w.Put(d.Titulo);
		w.Put(d.OuviuAHistoria);
		w.Put(d.Historia);
		w.Put(d.ConheceHaSegundos);
		w.Put(d.VistoHaSegundos);
	}

	public static DetalhesDeConhecido LerDetalhes(NetDataReader r) => new(
		r.GetString(MaxAssinatura), r.GetString(MaxGenero), r.GetUShort(), r.GetString(MaxTitulo),
		r.GetBool(), r.GetString(MaxHistoria), r.GetUInt(), r.GetUInt());
}
